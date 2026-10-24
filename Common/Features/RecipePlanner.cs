using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	// New ingredients and stations of the recipes in a world (null = the original ones)
	public class RecipePlan
	{
		public (int type, int stack)[][] Ingredients;
		public long[] IngredientValues;
		public int[][] Tiles;
		// What each recipe gives, always: exactly what RecipeSystem.Apply sets (no stand-in before its tier)
		public (int type, int stack)[] Results;
		// The recipe whose vanilla result (and stack) each recipe gives instead of its own, -1 when none (RecipeResultSwapChance:
		// the two recipes of a swap point to each other)
		public int[] SwapPartner;
		// U of each result: the unlocked tier of its true tier (0 when not gated). Display and tests only: the recipe gives
		// Results[i] at once, a key ingredient keeps it from being made before U
		public int[] ResultTiers;
		// Tier at which each item can be obtained (found or crafted), with this plan
		public int[] Availability;
		// Recipes that received a key ingredient of tier U from LockResults or LockSpecials (on top of the planned key)
		public bool[] KeyIngredient;
		// Recipes switched off when applied and left out of Availability: the copies of an earlier recipe once planned (same
		// result, ingredients, groups and conditions: the crafting menu never shows one recipe on two stations), and the
		// LoopOff ones
		public bool[] Twin;
		// The rare recipe kept vanilla that would loop through a protected ingredient (Rebuilder.KeepOriginals), switched off
		// (Twin too) because its result is still had otherwise (found, or another recipe)
		public bool[] LoopOff;
		// Recipes the Rebuilder could not rebuild that keep their vanilla ingredients but the ones looping through the plan,
		// replaced (KeepOriginals): IngredientTiers holds their T, and they have no planned key
		public bool[] Repaired;
		// Tests: how many cancelled swap pairs did not leave the candidates exactly as before (only counted in BuildWith)
		public int RestoreMismatches;
		// Recipe groups of each recipe once planned (null = the original ones): a rebuilt recipe drops the groups of the
		// slots it replaced and adds the ones it picked as ingredients
		public int[][] Groups;
		// Tier T each recipe's ingredients were rebuilt for (-1: not rebuilt), and the position of each recipe in the planning
		// order (-1: switched off as set up): an ingredient made by a recipe was only a candidate once that recipe was planned
		public int[] IngredientTiers;
		public int[] PlanOrder;
		// The key material PlanIngredients put in each rebuilt recipe (0: none), the use cap of the other materials by
		// tier, and the tier from which each item could be a new ingredient when the planning ended (Never: not a candidate)
		public int[] PlannedKeys;
		public int[] UseCaps;
		public int[] CandidateTiers;
		// For the shell recipes of the items no recipe makes (RecipeSystem.IsShell) that this world may plan: the tier they are
		// doable from (ShellTier); -1 for any other recipe. A shell is switched on when it got ingredients (ShellActive)
		public int[] ShellTiers;
		// Recipes whose ingredients RaiseValue could not bring to their value (no richer material had in time): the value
		// checks leave them out
		public bool[] ValueShort;
		// The key material LockResults or LockSpecials put in each recipe (0: none)
		public int[] LockKeys;
		// Keys that took a material not fitting the progression rules (Fits: tier, pickaxe level) for lack of another
		public int UnfitKeys;
	}

	// Plans the ingredients and stations of the recipes from the seed and the settings only (the same on every machine,
	// and independent of progression). Every recipe stays doable at its level:
	// - an item is "available" from a tier if it can be found (Findability) or crafted from available items at an
	//   available station; this is computed as a fixed point from the raw sources, so it never relies on a loop;
	// - an ingredient is replaced only by a material that is found (not crafted) no later than the recipe was doable;
	// - a station is replaced only by a station available no later than the original one.
	public static class RecipePlanner
	{
		public const int Never = Findability.Never;

		private static RecipePlan plan;

		// The results of the recipes with the swaps of the settings (SwappedResults), and the partner of each swapped recipe:
		// shared by the plan and the requirement providers
		private static (int type, int stack)[] baseResults;
		private static int[] basePartners;

		static RecipePlanner()
		{
			WorldCaches.Reset += Reset;
		}

		// Not kept when findability was reset while it was built (Findability.Version): it read the old tiers
		public static RecipePlan Get()
		{
			if (plan != null) return plan;
			int started = Findability.Version;
			RecipePlan built = Build(CraftingConfig.Instance.RecipesForUncraftable, PlanSettings.FromConfig());
			if (started == Findability.Version) plan = built;
			return built;
		}

		// A plan built with explicit settings, not cached (tests: the config is never changed). null = the setting
		public static RecipePlan BuildWith(bool shells, float? reusePenalty = null, float? swapChance = null, float? surpriseChance = null)
		{
			PlanSettings settings = PlanSettings.FromConfig();
			if (reusePenalty != null) settings.ReusePenalty = reusePenalty.Value;
			if (swapChance != null) settings.SwapChance = swapChance.Value;
			if (surpriseChance != null) settings.SurpriseChance = surpriseChance.Value;
			settings.CheckRestore = true;
			return Build(shells, settings);
		}

		// The settings a plan is built with (the config, or explicit values for the tests)
		private sealed class PlanSettings
		{
			public float ReusePenalty;
			public float SwapChance;
			public float SurpriseChance;
			// Tests: compare the candidates before and after each cancelled swap pair (RecipePlan.RestoreMismatches)
			public bool CheckRestore;

			public static PlanSettings FromConfig()
			{
				CraftingConfig cfg = CraftingConfig.Instance;
				return new PlanSettings { ReusePenalty = cfg.IngredientReusePenalty, SwapChance = cfg.RecipeResultSwapChance, SurpriseChance = cfg.IngredientSurpriseChance };
			}
		}

		public static void Reset()
		{
			plan = null;
			baseResults = null;
			basePartners = null;
			pairing = null;
		}

		private static int Count => Math.Min(RecipeSystem.RecipeCount, Recipe.numRecipes);

		// Recipes whose ingredients and stations may change and whose result may be swapped (a house recipe keeps its result)
		private static bool Randomized(int i) => CraftingConfig.Instance.RandomizeRecipes && RecipeSystem.Touchable(i) && !RecipeSystem.HouseRecipe(i);

		// U, the progression lock of a recipe's result: the unlocked tier of its true tier. The recipe always gives its
		// result; its ingredients (a key ingredient of tier U) or stations make it wait for U. Only the Coherent mode is gated
		public static int ResultTier(int i, int result)
		{
			if (!Gated(i)) return 0;
			// An upgrade waits for its own tier (the Life Fruit for a mechanical boss), not for the unlock of its rarity
			if (ItemPool.IsSpecial(result)) return Protection.UpgradeTier(result);
			return Tiers.Clamp(ItemPool.UnlockTierOf(result));
		}

		// The randomized recipes, and the protected results whose ingredients are rebuilt (a Key of Night made of Fallen Stars
		// was doable before hardmode): both wait for U. Not an assignable station kept as it is (the station plan and the
		// guarantees count it from its vanilla level, RecipeTier)
		private static bool Gated(int i)
		{
			if (!ProgressionConfig.Instance.TierGating || GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent) return false;
			if (Randomized(i)) return true;
			if (RecipeSystem.Touchable(i) || RecipeSystem.HouseRecipe(i) || !Rebuildable(i)) return false;
			int original = RecipeSystem.OriginalResult(i);
			return !(Stations.PlacedTile(original, out int tile) && Assignable(tile));
		}

		// The tier a rebuilt recipe giving this item is never planned below (tier gating in Coherent mode): the item's own tier,
		// its true tier when its vanilla progression has it later (a Pickaxe Axe needs Hallowed Bars). U alone lets it come a tier
		// early (TierAllowance), and so does the vanilla doable tier T starts from: Findability counts gated drops from
		// UnlockTier, so Chlorophyte Ore "found" in early hardmode gave the Chlorophyte Pickaxe a Cursed Flame key. Random drops
		// keep that allowance; a recipe rebuilt for the item keeps its level. Only randomizable items: not an upgrade (its own
		// tier: ResultTier), nor a protected result or an assignable station (the station plan and the guarantees count them
		// from their vanilla level, RecipeTier)
		public static int ResultFloor(int result)
		{
			if (!ProgressionConfig.Instance.TierGating || GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent) return 0;
			if (!ItemPool.IsRandomizable(result) || ItemPool.IsSpecial(result)) return 0;
			if (Stations.PlacedTile(result, out int tile) && Assignable(tile)) return 0;
			return Tiers.Clamp(Math.Max(ItemPool.TierOf(result), ItemPool.TrueTierOf(result)));
		}

		// Providers that can't read the plan (it depends on them) count a swapped result as made by nothing: its ingredients
		// are rebuilt for it, and the host's original ones say nothing of when it is made (a spare guarantee is harmless)
		private static void DropSwapped(int[] results)
		{
			int[] partners = BasePartners();
			for (int i = 0; i < results.Length && i < partners.Length; i++)
			{
				if (partners[i] >= 0) results[i] = 0;
			}
		}

		// The level of a recipe: its original result, the station it makes (an Orichalcum Anvil has rarity 3, but the tile is a
		// hardmode station made of hardmode bars) and the level of its stations
		public static int RecipeTier(int i)
		{
			int original = RecipeSystem.OriginalResult(i);
			int tier = ItemPool.TierOf(original);
			if (Stations.PlacedTile(original, out int made) && Stations.Info(made).Kind == StationKind.Progression) tier = Math.Max(tier, Stations.Info(made).Tier);
			foreach (int tile in RecipeSystem.OriginalTiles(i))
			{
				Stations.StationInfo info = Stations.Info(tile);
				if (info.Kind == StationKind.Progression) tier = Math.Max(tier, info.Tier);
			}
			return Tiers.Clamp(tier);
		}

		private static bool InGroup(IEnumerable<int> groups, int item, out RecipeGroup group)
		{
			foreach (int id in groups)
			{
				if (RecipeGroup.recipeGroups.TryGetValue(id, out group) && group.ContainsItem(item)) return true;
			}
			group = null;
			return false;
		}

		// Availability fixed point: found items, then items crafted from available ingredients at available stations.
		// recipeTiers[i] = tier from which recipe i can be made (ingredients and stations only); recipe i gives results[i]
		// from resultTiers[i]. skip: recipes switched off by the plan (RecipePlan.Twin), counted as absent. groups: the recipe
		// groups going with these ingredients (null: the live recipes' ones)
		public static int[] Availability(Func<int, int> found, int[] results, int[] resultTiers, (int type, int stack)[][] ingredients, int[][] tiles, out int[] recipeTiers, bool[] skip = null,
			int[][] groups = null)
		{
			int[] avail = new int[ItemPool.Info.Length];
			for (int item = 1; item < avail.Length; item++) avail[item] = found(item);
			Dictionary<int, List<int>> itemsByTile = Stations.PlacersByTile();
			// Each station's availability, kept up to date as its placing items become available
			Dictionary<int, int> tileAvail = new();
			foreach (int tile in itemsByTile.Keys) tileAvail[tile] = TileAvailability(tile, itemsByTile, avail);
			// Tiles without an item (Demon Altar...) are part of the world
			int TileAvail(int tile) => tileAvail.TryGetValue(tile, out int a) ? a : 0;
			int GroupAvail(RecipeGroup group)
			{
				int best = Never;
				foreach (int v in group.ValidItems)
				{
					if (v < avail.Length && avail[v] < best) best = avail[v];
				}
				return best;
			}
			int count = Count;
			recipeTiers = new int[count];
			Array.Fill(recipeTiers, Never);
			// The recipes that count, listed once (the shells left off are not looked at in every pass): a shell once it has
			// ingredients (planned, or live once switched on), other recipes as set up
			List<int> live = new(count);
			for (int i = 0; i < count; i++)
			{
				if ((RecipeSystem.IsShell(i) ? ingredients[i].Length == 0 : RecipeSystem.VanillaDisabled(i)) || (skip != null && i < skip.Length && skip[i])) continue;
				live.Add(i);
			}
			bool changed = true;
			for (int pass = 0; pass < 40 && changed; pass++)
			{
				changed = false;
				foreach (int i in live)
				{
					IEnumerable<int> recipeGroups = groups != null ? groups[i] : Main.recipe[i].acceptedGroups;
					int tier = 0;
					foreach ((int type, _) in ingredients[i])
					{
						int a = InGroup(recipeGroups, type, out RecipeGroup group) ? GroupAvail(group) : (type < avail.Length ? avail[type] : Never);
						tier = Math.Max(tier, a);
						if (tier == Never) break;
					}
					foreach (int tile in tiles[i])
					{
						if (tier == Never) break;
						tier = Math.Max(tier, TileAvail(tile));
					}
					if (tier == Never) continue;
					recipeTiers[i] = Math.Min(recipeTiers[i], tier);
					int result = results[i];
					int resultTier = Math.Max(tier, resultTiers[i]);
					if (result > 0 && result < avail.Length && resultTier < avail[result])
					{
						avail[result] = resultTier;
						changed = true;
						int placed = ContentSamples.ItemsByType.TryGetValue(result, out Item sample) ? sample.createTile : -1;
						if (placed >= TileID.Dirt && tileAvail.TryGetValue(placed, out int current) && resultTier < current) tileAvail[placed] = resultTier;
					}
				}
			}
			return avail;
		}

		// ---------------------------------------------------------------- Results

		// The results with the swaps of the settings, from the original recipes (the plan must not depend on progression)
		private static (int type, int stack)[] BaseResults()
		{
			if (baseResults != null) return baseResults;
			((int, int)[] results, int[] partners) = SwappedResults(CraftingConfig.Instance.RecipeResultSwapChance);
			basePartners = partners;
			// Not kept before the item pool is ready (no swap then)
			return ItemPool.Ready ? baseResults = results : results;
		}

		private static int[] BasePartners()
		{
			if (baseResults == null) BaseResults();
			return basePartners;
		}

		// Every randomized recipe gives its vanilla result, except the rare swaps: two recipes of the same tier (any tier in
		// Chaos mode) exchange their results, stacks included, so every item stays the result of as many recipes as in vanilla.
		// A swapped recipe gets ingredients rebuilt for the item it gives (its tier, key and value, a shape mostly its own).
		// The pairs come from the seed only (SwapPairing: never the chance, hardly the protections), and a pair is swapped when
		// both recipes are swappable and its own draw is below the chance (scaled so about chance% of the swappable recipes
		// are): raising the chance only adds swaps. The Rebuilder may still cancel a pair it can't key. Static data only
		// (never Findability: the guaranteed sources read these results). chance in percent; results[i] and partners[i]
		// (-1: not swapped)
		public static ((int type, int stack)[] results, int[] partners) SwappedResults(float chance)
		{
			int count = Count;
			(int, int)[] results = new (int, int)[count];
			int[] partners = Enumerable.Repeat(-1, count).ToArray();
			for (int i = 0; i < count; i++) results[i] = (RecipeSystem.OriginalResult(i), RecipeSystem.OriginalStack(i));
			if (chance <= 0 || !ItemPool.Ready) return (results, partners);
			bool anyTier = GeneralConfig.Instance.RandomizerMode == RandomizerMode.Chaos;
			int[] pairing = SwapPairing(anyTier);
			HashSet<int> stationTiles = Stations.RequiredTiles();
			bool[] swappable = Enumerable.Range(0, count).Select(i => Swappable(i, stationTiles)).ToArray();
			bool PairSwaps(int i, int j) => j > i && swappable[i] && swappable[j] && KeptValueFits(i, j) && KeptValueFits(j, i);
			// By group (tier): the swappable recipes, and the pairs that may swap
			Dictionary<int, (int eligible, int pairs)> groups = new();
			int TierGroup(int i) => anyTier ? 0 : ItemPool.TierOf(RecipeSystem.OriginalResult(i));
			for (int i = 0; i < count; i++)
			{
				if (!swappable[i]) continue;
				int g = TierGroup(i);
				(int eligible, int pairs) = groups.GetValueOrDefault(g);
				int j = pairing[i];
				groups[g] = (eligible + 1, pairs + (j >= 0 && PairSwaps(i, j) ? 1 : 0));
			}
			for (int i = 0; i < count; i++)
			{
				int j = pairing[i];
				if (j < 0 || !PairSwaps(i, j)) continue;
				(int eligible, int pairs) = groups[TierGroup(i)];
				// pairs x threshold = chance x eligible / 2: about chance% of the swappable recipes, whatever share of the pairs
				// has two swappable recipes. Above 1 (few pairs for a high chance) every pair swaps and the share stays below the
				// setting: harmless, it can't do better
				double threshold = chance / 100.0 * eligible / (2.0 * pairs);
				if (PairDraw(i, j) >= threshold) continue;
				partners[i] = j;
				partners[j] = i;
				(results[i], results[j]) = (results[j], results[i]);
			}
			return (results, partners);
		}

		// A swapped recipe keeps its protected ingredients (Rebuilder): recipe i may give the item of recipe j only when they are
		// worth no more than j's recipe may be (MaxValueFactor), else a Nebula Fragment Block's recipe giving a cheap item kept its
		// fragment (a 10000 recipe for an 8 one)
		private static bool KeptValueFits(int i, int j)
		{
			long kept = RecipeSystem.OriginalIngredients(i).Where(p => !IngredientReplaceable(p.type)).Sum(p => UnitValue(p.type) * p.stack);
			return kept <= (long)(RecipeSystem.OriginalIngredientValue(j) * MaxValueFactor) + Item.silver;
		}

		// The pair's own draw in [0, 1), the same whichever recipe is named first
		private static double PairDraw(int i, int j)
		{
			int a = RecipeSystem.RecipeKey(i), b = RecipeSystem.RecipeKey(j);
			// DetRandom.Hash ends with hash ^= hash >> 15 (an arithmetic shift): its sign bit is always clear, so only the 31
			// low bits are drawn (read as unsigned, every draw was below 0.5 and twice the chance swapped)
			return (DetRandom.Hash(SeedSystem.WorldSeed, RollKey.Recipe, Math.Min(a, b), Math.Max(a, b), RollKey.Surprise) & int.MaxValue) / 2147483648.0;
		}

		private static int[] pairing;
		private static bool pairingAnyTier;

		// The swap partner of every vanilla recipe (-1: none), from the seed only: the recipes of each tier (one group in Chaos
		// mode) in an order drawn from the seed, each unpaired one taking a partner among the next unpaired ones that fit, of
		// another category when it can. Protections and bans are not looked at (SwappedResults skips the pairs they touch), so
		// they hardly move the other pairs
		private static int[] SwapPairing(bool anyTier)
		{
			if (pairing != null && pairingAnyTier == anyTier && pairing.Length == Count) return pairing;
			int count = Count;
			int[] pairs = Enumerable.Repeat(-1, count).ToArray();
			VanillaLoops loops = new();
			IEnumerable<IGrouping<int, int>> byTier = Enumerable.Range(0, count)
				.Where(i => !RecipeSystem.IsShell(i) && !RecipeSystem.VanillaDisabled(i) && !RecipeSystem.LeftAlone(i) && ItemPool.IsValid(RecipeSystem.OriginalResult(i)))
				.GroupBy(i => anyTier ? 0 : ItemPool.TierOf(RecipeSystem.OriginalResult(i))).OrderBy(g => g.Key);
			foreach (IGrouping<int, int> group in byTier)
			{
				List<int> order = group.OrderBy(i => (uint)DetRandom.Hash(SeedSystem.WorldSeed, RollKey.Recipe, RecipeSystem.RecipeKey(i), RollKey.Surprise)).ThenBy(i => i).ToList();
				for (int p = 0; p < order.Count; p++)
				{
					int i = order[p];
					if (pairs[i] >= 0) continue;
					int x = RecipeSystem.OriginalResult(i);
					List<int> fits = new();
					for (int q = p + 1; q < order.Count && fits.Count < SwapWindow; q++)
					{
						int j = order[q];
						if (pairs[j] < 0 && SwapFits(i, j) && SwapFits(j, i) && !loops.Loops(i, j) && !loops.Loops(j, i)) fits.Add(j);
					}
					// Another category first (furniture and blocks are one): a brick for a chair is no surprise
					List<int> different = fits.Where(j => !Alike(CategoryOf(RecipeSystem.OriginalResult(j)), CategoryOf(x))).ToList();
					if (different.Count > 0) fits = different;
					if (fits.Count == 0) continue;
					int partner = fits[DetRandom.Get(RollKey.Recipe, RecipeSystem.RecipeKey(i), RollKey.Surprise).Next(fits.Count)];
					pairs[i] = partner;
					pairs[partner] = i;
				}
			}
			// Not kept before the item pool is ready
			if (ItemPool.Ready)
			{
				pairing = pairs;
				pairingAnyTier = anyTier;
			}
			return pairs;
		}

		// How many unpaired recipes further in the order a recipe may pair with
		private const int SwapWindow = 24;

		// May this recipe take part in a swap? A randomized vanilla recipe whose ingredients are rebuilt, giving no upgrade,
		// banned item, bag, summon or station (the station plan and the guaranteed stations rely on how each one is made).
		// The house recipes and the mining chain are never randomized (Randomized)
		public static bool Swappable(int i) => Swappable(i, Stations.RequiredTiles());

		// stationTiles: Stations.RequiredTiles(), computed once by a caller looking at many recipes
		public static bool Swappable(int i, HashSet<int> stationTiles)
		{
			if (RecipeSystem.IsShell(i) || RecipeSystem.VanillaDisabled(i) || !Randomized(i) || !Rebuildable(i)) return false;
			int x = RecipeSystem.OriginalResult(i);
			if (!ItemPool.IsValid(x) || ItemPool.IsSpecial(x) || ItemPool.IsBanned(x) || Unusable.Contains(ItemPool.Info[x].Category)) return false;
			return !(Stations.PlacedTile(x, out int tile) && stationTiles.Contains(tile));
		}

		// May recipe i give the vanilla result of recipe j? Another item, never one of i's ingredients nor a member of its groups
		// (1 Wood -> 4 Wood would be infinite)
		private static bool SwapFits(int i, int j)
		{
			int item = RecipeSystem.OriginalResult(j);
			if (item == RecipeSystem.OriginalResult(i)) return false;
			return !RecipeSystem.OriginalIngredients(i).Any(x => x.type == item) && !RecipeSystem.InOriginalGroup(i, item);
		}

		// The vanilla recipes as an item graph, read backwards: would recipe i giving the vanilla result of recipe j make a loop
		// through one of i's ingredients that stay (protected, a bag, a summon: never rebuilt)? The whole vanilla graph is
		// looked at (conservative: rebuilt recipes may lose some of these edges)
		private sealed class VanillaLoops
		{
			private readonly Dictionary<int, HashSet<int>> previous = new();
			private readonly Dictionary<int, HashSet<int>> ancestors = new();

			public VanillaLoops()
			{
				for (int r = 0; r < Count; r++)
				{
					if (RecipeSystem.IsShell(r) || RecipeSystem.VanillaDisabled(r)) continue;
					int result = RecipeSystem.OriginalResult(r);
					if (result <= 0) continue;
					if (!previous.TryGetValue(result, out HashSet<int> set)) previous[result] = set = new HashSet<int>();
					int[] groups = RecipeSystem.OriginalGroups(r);
					foreach ((int type, _) in RecipeSystem.OriginalIngredients(r))
					{
						int group = RecipePlanner.GroupOf(groups, type);
						if (group < 0) set.Add(type);
						else set.UnionWith(RecipeGroup.recipeGroups[group].ValidItems);
					}
				}
			}

			public bool Loops(int i, int j)
			{
				if (!ancestors.TryGetValue(i, out HashSet<int> before))
				{
					// Every item the kept ingredients (or their group members) are made from, directly or not, themselves included
					before = new HashSet<int>();
					Stack<int> todo = new();
					int[] groups = RecipeSystem.OriginalGroups(i);
					foreach ((int type, _) in RecipeSystem.OriginalIngredients(i))
					{
						if (IngredientReplaceable(type)) continue;
						int group = RecipePlanner.GroupOf(groups, type);
						foreach (int v in group < 0 ? new[] { type } : (IEnumerable<int>)RecipeGroup.recipeGroups[group].ValidItems)
						{
							if (before.Add(v)) todo.Push(v);
						}
					}
					while (todo.Count > 0)
					{
						if (!previous.TryGetValue(todo.Pop(), out HashSet<int> set)) continue;
						foreach (int from in set)
						{
							if (before.Add(from)) todo.Push(from);
						}
					}
					ancestors[i] = before;
				}
				return before.Contains(RecipeSystem.OriginalResult(j));
			}
		}

		private static ItemCategory CategoryOf(int item) => ItemPool.IsValid(item) ? ItemPool.Info[item].Category : ItemCategory.Misc;

		// Furniture and blocks are one category for the swaps
		public static bool Alike(ItemCategory a, ItemCategory b) =>
			a == b || (a is ItemCategory.Furniture or ItemCategory.Block && b is ItemCategory.Furniture or ItemCategory.Block);

		// ---------------------------------------------------------------- Upgrades

		// The recipe giving each life or mana upgrade in this world, -1 when none: its own vanilla recipe when one makes it (the
		// Mana Crystal), else its shell recipe (RecipeSystem registers one for each upgrade no recipe makes). The upgrades never
		// take another recipe's result: the shell is planned like the others from the upgrade's tier, then LockSpecials gives it
		// a key and a value of at least the upgrade's
		public static int SpecialRecipe(int item)
		{
			if (!ItemPool.IsSpecial(item) || !CraftingConfig.Instance.RandomizeRecipes) return -1;
			int shellRecipe = -1;
			for (int i = 0; i < Count; i++)
			{
				if (RecipeSystem.OriginalResult(i) != item) continue;
				if (RecipeSystem.IsShell(i)) shellRecipe = i;
				else if (!RecipeSystem.VanillaDisabled(i) && !RecipeSystem.LeftAlone(i)) return i;
			}
			return shellRecipe;
		}

		private static int[] Types((int type, int stack)[] results) => results.Select(r => r.type).ToArray();

		private static int[] ResultTiers(int[] results) => Enumerable.Range(0, results.Length).Select(i => ResultTier(i, results[i])).ToArray();

		private static (int, int)[][] OriginalIngredients() => Enumerable.Range(0, Count).Select(RecipeSystem.OriginalIngredients).ToArray();

		private static int[][] OriginalGroups() => Enumerable.Range(0, Count).Select(RecipeSystem.OriginalGroups).ToArray();

		// The groups of a recipe with this plan
		public static int[] GroupsOf(RecipePlan plan, int i) => plan.Groups[i] ?? RecipeSystem.OriginalGroups(i);

		private static int[][] PlannedGroups(RecipePlan plan) => Enumerable.Range(0, Count).Select(i => GroupsOf(plan, i)).ToArray();

		private static int[][] OriginalTiles() => Enumerable.Range(0, Count).Select(RecipeSystem.OriginalTiles).ToArray();

		private static RecipePlan Build(bool shells, PlanSettings settings)
		{
			int count = Count;
			CraftingConfig cfg = CraftingConfig.Instance;
			(int, int)[][] ingredients = OriginalIngredients();
			int[][] tiles = OriginalTiles();
			// The results (vanilla, and the rare swaps) are set once, from the original recipes, and everything is planned around
			// them: a result is never one of its recipe's original ingredients (SwapFits) nor a new one (PlanIngredients), and
			// Apply gives exactly them
			bool fromConfig = settings.SwapChance == CraftingConfig.Instance.RecipeResultSwapChance;
			((int, int)[] swapped, int[] partners) = fromConfig ? (BaseResults(), BasePartners()) : SwappedResults(settings.SwapChance);
			RecipePlan result = new()
			{
				Ingredients = new (int, int)[count][],
				IngredientValues = new long[count],
				Tiles = new int[count][],
				Results = ((int, int)[])swapped.Clone(),
				SwapPartner = (int[])partners.Clone()
			};
			int[] results = Types(result.Results);
			result.ResultTiers = ResultTiers(results);
			result.KeyIngredient = new bool[count];
			result.Twin = new bool[count];
			result.LoopOff = new bool[count];
			result.Repaired = new bool[count];
			result.Groups = new int[count][];
			result.IngredientTiers = Enumerable.Repeat(-1, count).ToArray();
			result.PlanOrder = Enumerable.Repeat(-1, count).ToArray();
			result.PlannedKeys = new int[count];
			result.UseCaps = new int[Tiers.Count];
			result.CandidateTiers = Array.Empty<int>();
			result.ShellTiers = Enumerable.Repeat(-1, count).ToArray();
			result.ValueShort = new bool[count];
			result.LockKeys = new int[count];
			// The vanilla availability (the original recipes as they are): what the ingredient tiers follow; a swapped recipe reads
			// its partner's, the vanilla recipe of the item it gives
			int[] vanillaResults = Enumerable.Range(0, count).Select(RecipeSystem.OriginalResult).ToArray();
			int[] avail = Availability(Findability.Tier, vanillaResults, new int[count], ingredients, tiles, out int[] recipeTiers, null, OriginalGroups());
			// Stations first (from the original recipes only): the ingredient plan needs them to know when each result is made
			if (cfg.RandomizeRecipes && cfg.RandomizeStations) PlanStations(result, results, avail, recipeTiers);
			// The shells are planned by the same rebuilder, after every other recipe (they shift none of their choices): the
			// upgrades' shells whatever the setting, before LockSpecials keys them; the others after the locks
			shells &= cfg.RandomizeRecipes;
			ItemGraph graph = new();
			if (cfg.RandomizeRecipes)
			{
				Rebuilder rebuilder = new(result, results, avail, recipeTiers, tiles, settings, graph);
				rebuilder.Run();
				LockResults(result, results, ingredients, tiles, graph);
				LockSpecials(result, results, ingredients, tiles, graph);
				// The other shells last: the locks and keys above are the same with RecipesForUncraftable on or off
				rebuilder.PlanOtherShells(shells);
				TrimValues(result);
				ShareTwinStations(result, ingredients, tiles, avail);
			}
			// The live recipes give their result at once: no result tier any more; the copies are switched off
			int[][] PlannedTiles() => Enumerable.Range(0, count).Select(i => result.Tiles[i] ?? tiles[i]).ToArray();
			(int, int)[][] PlannedIngredients() => Enumerable.Range(0, count).Select(i => result.Ingredients[i] ?? ingredients[i]).ToArray();
			int[] FinalAvailability()
			{
				result.Availability = Availability(Findability.Tier, results, new int[count], PlannedIngredients(), PlannedTiles(), out int[] doable, result.Twin, PlannedGroups(result));
				return doable;
			}
			if (Enumerable.Range(0, count).Any(i => ShellActive(result, i)))
			{
				// Everything else was planned without the shells (keys, locks): what that plan relies on, the shells
				// left out, must stay as it is with them
				int[] baseAvail = Availability(Findability.Tier, results, new int[count], PlannedIngredients(), PlannedTiles(), out int[] baseDoable, ShellMask(result.Twin), PlannedGroups(result));
				// Each round drops at least one shell; dropping one only delays the others (one of them may become never doable)
				for (int round = 0; ; round++)
				{
					int[] doable = FinalAvailability();
					HashSet<int> usedEarly = UsedByEarlierRecipes(result, doable, baseDoable);
					List<int> early = Enumerable.Range(0, count).Where(i => ShellActive(result, i) && ShellBreaksPlan(result, i, results, doable, baseAvail, usedEarly)).ToList();
					if (early.Count == 0) break;
					// Bounded: past a few rounds, every shell left is switched off (the plan without shells is valid)
					if (round >= 6) early = Enumerable.Range(0, count).Where(i => ShellActive(result, i)).ToList();
					foreach (int i in early) DropShell(result, i);
					ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Recipe plan: {early.Count} shell recipes doable before their tier, never, or making an item earlier than planned switched off");
					if (round >= 6)
					{
						FinalAvailability();
						break;
					}
				}
			}
			else FinalAvailability();
			return result;
		}

		// Must this active shell be switched off? When it is doable before its tier (a key made earlier), or never;
		// when it gives its item earlier than the plan without shells has it (Black Lens made at U by a randomized recipe and
		// chosen as a key of U: a shell from tier 0 unlocked every recipe it keys); or when it gives an item the plan never has
		// earlier and a recipe using it (directly or by a group) became doable earlier than planned. An upgrade's shell only
		// for the first reasons: it is the upgrade's recipe (never a new ingredient, so it keys nothing), a little early at worst
		private static bool ShellBreaksPlan(RecipePlan result, int i, int[] results, int[] doable, int[] baseAvail, HashSet<int> usedEarly)
		{
			if (doable[i] < result.ShellTiers[i] || doable[i] >= Never) return true;
			int item = results[i];
			if (ItemPool.IsSpecial(item)) return false;
			if (item <= 0 || item >= baseAvail.Length || doable[i] >= baseAvail[item]) return false;
			return baseAvail[item] < Never || usedEarly.Contains(item);
		}

		// The ingredients (and group members) of the recipes other than shells doable earlier than in the plan without shells
		private static HashSet<int> UsedByEarlierRecipes(RecipePlan result, int[] doable, int[] baseDoable)
		{
			HashSet<int> items = new();
			for (int j = 0; j < doable.Length; j++)
			{
				if (RecipeSystem.IsShell(j) || doable[j] >= baseDoable[j]) continue;
				foreach ((int type, _) in result.Ingredients[j] ?? RecipeSystem.OriginalIngredients(j)) items.Add(type);
				foreach (int g in GroupsOf(result, j))
				{
					if (RecipeGroup.recipeGroups.TryGetValue(g, out RecipeGroup group)) items.UnionWith(group.ValidItems);
				}
			}
			return items;
		}

		// ---------------------------------------------------------------- Shells

		// A skip mask for Availability: these recipes and every shell (the planning of the other recipes never depends on them)
		private static bool[] ShellMask(bool[] skip)
		{
			bool[] mask = new bool[Count];
			for (int i = 0; i < mask.Length; i++) mask[i] = RecipeSystem.IsShell(i) || (skip != null && i < skip.Length && skip[i]);
			return mask;
		}

		// Is this recipe a shell this plan switches on (it got ingredients)?
		public static bool ShellActive(RecipePlan plan, int i) => RecipeSystem.IsShell(i) && i < plan.Ingredients.Length && plan.Ingredients[i] != null;

		private static void DropShell(RecipePlan result, int i)
		{
			result.Ingredients[i] = null;
			result.Tiles[i] = null;
			result.Groups[i] = null;
			result.IngredientValues[i] = 0;
			result.PlannedKeys[i] = 0;
			result.ValueShort[i] = false;
		}

		// Items that may get a shell recipe: randomizable blocks, walls, furniture, vanity, dyes, food and materials that no other
		// recipe makes. Never gear, ammo, potions or anything Unusable (bags, summons): they come from loot, and a recipe would
		// skip it; never an upgrade, a banned item, an ore or a mining material (Tainted), a station (the station plan and the
		// guarantees rely on how they are found), nor anything the guaranteed sources place
		private static readonly ItemCategory[] ShellCategories =
		{
			ItemCategory.Block, ItemCategory.Furniture, ItemCategory.Material, ItemCategory.Vanity, ItemCategory.Dye, ItemCategory.Food
		};

		public static bool ShellEligible(int item, HashSet<int> stationTiles, HashSet<int> guaranteed) =>
			ItemPool.IsRandomizable(item) && !ItemPool.IsSpecial(item) && !ItemPool.IsBanned(item) && ShellCategories.Contains(ItemPool.Info[item].Category)
			&& !Obtainability.RecipeResults.Contains(item) && !Tainted(item) && !OreMap.IsOre(item) && !guaranteed.Contains(item)
			&& !(Stations.PlacedTile(item, out int tile) && stationTiles.Contains(tile));

		// The tier a shell is doable from: when the item is found (its tier when it never is), and never before the unlocked tier
		// of its true tier (gated rolls give it from there): crafting it never makes it available earlier
		public static int ShellTier(int item)
		{
			int found = Findability.Tier(item);
			int tier = found < Never ? found : ItemPool.TierOf(item);
			return Tiers.Clamp(Math.Max(tier, ItemPool.UnlockTierOf(item)));
		}

		// ---------------------------------------------------------------- Key ingredients

		// A randomized recipe always gives its result, so it must not be doable before U, the unlocked tier of that result
		// (ResultTier). A rebuilt recipe has a key of its tier T >= U (Rebuilder); one still doable earlier (no key found, a
		// random station on vanilla ingredients, only protected ingredients) gets a key material found exactly at U (U + 1
		// when U has none), in a replaceable slot or as a new ingredient. Checked again against the live recipes: a key placed
		// later may make another recipe doable earlier
		private static void LockResults(RecipePlan result, int[] results, (int, int)[][] ingredients, int[][] tiles, ItemGraph graph)
		{
			int count = Count;
			(int, int)[][] Planned() => Enumerable.Range(0, count).Select(i => result.Ingredients[i] ?? ingredients[i]).ToArray();
			int[][] plannedTiles = Enumerable.Range(0, count).Select(i => result.Tiles[i] ?? tiles[i]).ToArray();
			// The shells are left out: the locks are the same with the setting on or off; the recipes switched off too. Not the
			// upgrades' shells, planned whatever the setting: a Life Crystal made at 0 makes a Repaired Life Crystal doable then
			bool[] noShells = ShellMask(result.Twin);
			for (int i = 0; i < count; i++)
			{
				if (ShellActive(result, i) && ItemPool.IsSpecial(results[i])) noShells[i] = false;
			}
			List<int>[,] buckets = FindableBuckets();
			WeightTable weights = CraftingConfig.Instance.IngredientWeights;
			Dictionary<int, int> uses = new();
			int keys = 0, late = 0, rekeyed = 0, unlocked = 0;
			SortedSet<int> noMaterial = new();
			// First with every result had from its U (what the plan was built on), then with the live recipes
			for (int round = 0; round < 4; round++)
			{
				int[] avail = Availability(Findability.Tier, results, round == 0 ? LockTiers(result) : new int[count], Planned(), plannedTiles, out int[] doable, noShells, PlannedGroups(result));
				int placed = 0;
				unlocked = 0;
				for (int i = 0; i < count; i++)
				{
					// The upgrades: LockSpecials
					if (!NeedsLock(result, i, doable) || ItemPool.IsSpecial(results[i])) continue;
					int key = PlaceKey(result, results, i, result.ResultTiers[i], buckets, weights, avail, uses, graph, out bool later, append: true);
					if (key <= 0)
					{
						unlocked++;
						noMaterial.Add(result.ResultTiers[i]);
						continue;
					}
					placed++;
					result.KeyIngredient[i] = true;
					RaiseAfterKey(result, results, i, key, result.ResultTiers[i] + (later ? 1 : 0), buckets, avail, graph);
					if (round > 0) rekeyed++;
					else
					{
						keys++;
						if (later) late++;
					}
				}
				if (placed == 0) break;
			}
			string topKeys = string.Join(", ", uses.OrderByDescending(p => p.Value).ThenBy(p => p.Key).Take(5).Select(p => $"{DetRandom.ItemName(p.Key)} x{p.Value}"));
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Recipe plan: {keys} key ingredients added ({late} from U+1, {rekeyed} chosen again), {unlocked} recipes left doable before their tier; " +
				$"tiers without a key material: {(noMaterial.Count == 0 ? "none" : string.Join(", ", noMaterial))}; most used keys: {topKeys}; {result.UnfitKeys} keys in all not fitting progression for lack of another");
		}

		// A key placed by LockResults takes the value of the slot it replaced, which may have been most of the recipe: below
		// MinValueFactor of its vanilla value, richer materials had by the key's tier fill it (RaiseValue; the key and the
		// planned key stay), else the recipe is ValueShort
		private static void RaiseAfterKey(RecipePlan result, int[] results, int i, int key, int tier, List<int>[,] buckets, int[] avail, ItemGraph graph)
		{
			if (result.Ingredients[i] == null || RecipeSystem.HouseRecipe(i)) return;
			long vanilla = RecipeSystem.OriginalIngredientValue(result.SwapPartner[i] >= 0 ? result.SwapPartner[i] : i);
			long want = (long)Math.Ceiling(vanilla * MinValueFactor);
			if (result.IngredientValues[i] >= want) return;
			int top = Tiers.Clamp(tier);
			long most = (long)(vanilla * MaxValueFactor) + Item.silver;
			HashSet<int> below = graph.From(results[i]);
			int originalResult = RecipeSystem.OriginalResult(i);
			List<int> richer = new();
			for (int c = 0; c < ItemCategories.Count; c++)
			{
				if (!MaterialLike((ItemCategory)c)) continue;
				for (int t = 0; t <= top; t++)
				{
					richer.AddRange(buckets[c, t].Where(x => x != results[i] && x != originalResult && x < avail.Length && avail[x] <= top && !below.Contains(x)
						&& Fits(x, results[i], top) && UnitValue(x) <= most));
				}
			}
			Random raise = DetRandom.Get(RollKey.KeyIngredient, RecipeSystem.RecipeKey(i), RollKey.Fallback);
			// Still short with every free slot taken: a new ingredient while the recipe has room (its vanilla count plus the
			// spread, plus the key), on a stream of its own
			int room = RecipeSystem.OriginalIngredients(i).Length + CraftingConfig.Instance.IngredientCountSpread + 1;
			if (!RaiseValue(result, i, want, key, richer, raise)
				&& !RaiseValue(result, i, want, key, richer, DetRandom.Get(RollKey.KeyIngredient, RecipeSystem.RecipeKey(i), RollKey.Fallback, RollKey.Special), append: true, appendMax: room))
			{
				result.ValueShort[i] = true;
			}
			graph.AddRecipe(result.Ingredients[i], GroupsOf(result, i), results[i]);
		}

		// The recipes of the life and mana upgrades (SpecialRecipe), last (nothing else is planned around them): each one always
		// has a key ingredient (of the upgrade's tier U; at tier 0, an uncommon material, so a cheap recipe is no free crystal),
		// added as a new ingredient when no slot may take it, and its ingredients are worth at least the upgrade (stacks
		// raised). A vanilla recipe left as it is (vanilla ingredients) is the upgrade's vanilla source. Only the RollKey.Special
		// stream: the other recipes stay as they were
		private static void LockSpecials(RecipePlan result, int[] results, (int, int)[][] ingredients, int[][] tiles, ItemGraph graph)
		{
			int count = Count;
			(int, int)[][] planned = Enumerable.Range(0, count).Select(i => result.Ingredients[i] ?? ingredients[i]).ToArray();
			int[][] plannedTiles = Enumerable.Range(0, count).Select(i => result.Tiles[i] ?? tiles[i]).ToArray();
			int[] avail = Availability(Findability.Tier, results, new int[count], planned, plannedTiles, out _, ShellMask(result.Twin), PlannedGroups(result));
			List<int>[,] buckets = FindableBuckets();
			WeightTable weights = CraftingConfig.Instance.IngredientWeights;
			Dictionary<int, int> uses = new();
			foreach (int item in Protection.Upgrades.OrderBy(x => x))
			{
				int i = SpecialRecipe(item);
				if (i < 0 || i >= count || results[i] != item) continue;
				bool shell = RecipeSystem.IsShell(i);
				if (!shell && result.Ingredients[i] == null) continue;
				int u = Protection.UpgradeTier(item);
				Random rng = DetRandom.Get(RollKey.Special, DetRandom.ItemKey(item), RollKey.KeyIngredient);
				int key = result.PlannedKeys[i];
				if (key == 0)
				{
					key = PlaceKey(result, results, i, u, buckets, weights, avail, uses, graph, out _, rng, uncommon: u == 0, append: true);
					if (key > 0) result.KeyIngredient[i] = true;
				}
				// A shell without a key (none at its tier) stays off
				if (shell && result.PlannedKeys[i] == 0 && !result.KeyIngredient[i])
				{
					DropShell(result, i);
					ModContent.GetInstance<UltimateRandomizer>().Logger.Warn($"No key material for the recipe of {DetRandom.ItemName(item)}: it keeps its vanilla sources only");
					continue;
				}
				// Worth at least the upgrade: richer materials had by its tier (never one it leads to) when stacks can't
				HashSet<int> below = graph.From(item);
				List<int> richer = new();
				for (int c = 0; c < ItemCategories.Count; c++)
				{
					if (!MaterialLike((ItemCategory)c)) continue;
					for (int t = 0; t <= u; t++)
					{
						richer.AddRange(buckets[c, t].Where(x => x < avail.Length && avail[x] <= u && !below.Contains(x) && Fits(x, item, u)));
					}
				}
				Random raise = DetRandom.Get(RollKey.Special, DetRandom.ItemKey(item), RollKey.Fallback);
				if (!RaiseValue(result, i, StackRules.ValueOf(item), key, richer, raise, append: true)) result.ValueShort[i] = true;
				graph.AddRecipe(result.Ingredients[i], GroupsOf(result, i), item);
			}
		}

		// The last word on value: no planned recipe (but an upgrade's, worth its upgrade, and a house's) is worth more than
		// MaxValueFactor times its vanilla recipe (the item's own for a swap) when stacks can say so: the replaceable ingredients
		// lose stacks, the most valuable first, never below one (a key placed after the rebuild, LockResults or RecheckKeys,
		// took the value of the slot it replaced, which may have been most of the recipe)
		private static void TrimValues(RecipePlan result)
		{
			int trimmed = 0;
			for (int i = 0; i < Count; i++)
			{
				if (result.Ingredients[i] == null || RecipeSystem.HouseRecipe(i) || ItemPool.IsSpecial(result.Results[i].type)) continue;
				(int type, int stack)[] current = ((int, int)[])result.Ingredients[i].Clone();
				long max = (long)(RecipeSystem.OriginalIngredientValue(result.SwapPartner[i] >= 0 ? result.SwapPartner[i] : i) * MaxValueFactor) + Item.silver;
				long value = current.Sum(p => UnitValue(p.type) * p.stack);
				if (value <= max) continue;
				foreach (int k in Enumerable.Range(0, current.Length).Where(k => IngredientReplaceable(current[k].type)).OrderByDescending(k => UnitValue(current[k].type)).ThenBy(k => k).ToList())
				{
					if (value <= max) break;
					(int type, int stack) = current[k];
					long unit = UnitValue(type);
					int lower = (int)Math.Min(stack - 1, (value - max + unit - 1) / unit);
					if (lower <= 0) continue;
					current[k] = (type, stack - lower);
					value -= lower * unit;
				}
				result.Ingredients[i] = current;
				result.IngredientValues[i] = value;
				trimmed++;
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Recipe plan: {trimmed} recipes trimmed to at most {MaxValueFactor} times their vanilla value");
		}

		// Raises a recipe's ingredients until they are worth at least target, with sane stacks (StackCap: never 9999 walls nor
		// 273 potions): the most valuable replaceable ingredients up to their cap first; then, while that is not enough, the
		// least valuable replaceable one (never keep, the key, nor a group slot) gives way to a richer material (richer, in
		// order: one of the cheapest that fill the gap within their cap, else the most valuable). Returns whether target was reached
		// append: an upgrade's recipe may get a new ingredient when no slot is left (a rebuilt recipe keeps its count, or
		// stays within appendMax ingredients when given)
		private static bool RaiseValue(RecipePlan result, int i, long target, int keep, IReadOnlyList<int> richer, Random rng, bool append = false, int appendMax = 0)
		{
			(int type, int stack)[] current = ((int, int)[])(result.Ingredients[i] ?? RecipeSystem.OriginalIngredients(i)).Clone();
			long Value() => current.Sum(p => UnitValue(p.type) * p.stack);
			if (current.Length == 0 || Value() >= target) return true;
			int[] groups = GroupsOf(result, i);
			bool Free(int k) => IngredientReplaceable(current[k].type) && current[k].type != keep && current[k].type != result.PlannedKeys[i] && GroupOf(groups, current[k].type) < 0;
			foreach (int k in Enumerable.Range(0, current.Length).OrderByDescending(k => UnitValue(current[k].type)).ThenBy(k => k).ToList())
			{
				long missing = target - Value();
				if (missing <= 0) break;
				(int type, int stack) = current[k];
				if (!IngredientReplaceable(type)) continue;
				long wanted = stack + (missing + UnitValue(type) - 1) / UnitValue(type);
				current[k] = (type, (int)Math.Max(stack, Math.Min(wanted, StackCap(result, i, type))));
			}
			HashSet<int> replaced = new();
			while (Value() < target)
			{
				List<int> slots = Enumerable.Range(0, current.Length).Where(k => Free(k) && !replaced.Contains(k)).OrderBy(k => UnitValue(current[k].type) * current[k].stack).ThenBy(k => k).ToList();
				// No slot left to give way (the key alone): a new ingredient while the recipe has room
				int most = Math.Max(1, appendMax > 0 ? Math.Min(appendMax, CraftingConfig.Instance.IngredientMax) : CraftingConfig.Instance.IngredientMax);
				if (slots.Count == 0 && (!append || current.Length >= most)) break;
				int slot = slots.Count > 0 ? slots[0] : current.Length;
				bool appended = slot == current.Length;
				if (appended) current = current.Append((0, 0)).ToArray();
				(int type, int stack) old = current[slot];
				long oldValue = appended ? 0 : UnitValue(old.type) * old.stack;
				long need = target - (Value() - oldValue);
				List<int> options = richer.Where(x => !current.Any(p => p.type == x) && !RecipeSystem.InGroups(groups, x)).ToList();
				if (options.Count == 0)
				{
					if (appended) current = current.Take(slot).ToArray();
					break;
				}
				List<int> enough = options.Where(x => UnitValue(x) * StackCap(result, i, x) >= need).OrderBy(UnitValue).ThenBy(x => x).Take(3).ToList();
				int picked = enough.Count > 0 ? enough[rng.Next(enough.Count)] : options.OrderByDescending(x => UnitValue(x) * StackCap(result, i, x)).ThenBy(x => x).First();
				int pickedStack = (int)Math.Clamp((need + UnitValue(picked) - 1) / UnitValue(picked), 1, StackCap(result, i, picked));
				// Nothing fills the gap and the richest option is worth no more than what the slot holds: the slot stays
				if (UnitValue(picked) * pickedStack <= oldValue)
				{
					if (appended) current = current.Take(slot).ToArray();
					break;
				}
				current[slot] = (picked, pickedStack);
				replaced.Add(slot);
			}
			result.Ingredients[i] = current;
			result.IngredientValues[i] = Value();
			return Value() >= target;
		}

		// The most of one ingredient a planned recipe asks for: the ingredient stack cap (or three times the item's own vanilla
		// stack in the recipe, or in the vanilla recipe of the item it gives), never above the item's max stack; an item had
		// one at a time (OneOff) a few at most
		public static int StackCap(RecipePlan plan, int i, int type)
		{
			int partner = plan?.SwapPartner != null && i >= 0 && i < plan.SwapPartner.Length ? plan.SwapPartner[i] : -1;
			int vanilla = 0;
			foreach (int r in new[] { i, partner })
			{
				if (r < 0 || r >= RecipeSystem.RecipeCount) continue;
				foreach ((int t, int n) in RecipeSystem.OriginalIngredients(r))
				{
					if (t == type) vanilla = Math.Max(vanilla, n);
				}
			}
			int cap = Math.Max(CraftingConfig.Instance.IngredientStackCap, vanilla * 3);
			if (OneOff(type)) cap = Math.Max(vanilla, OneOffCap);
			return Math.Clamp(cap, 1, StackRules.MaxStackOf(type));
		}

		// Items had one at a time: a relic, an Expert or Master drop, a rare material (one per bag, a rare drop): a few per recipe
		private const int OneOffCap = 3;

		public static bool OneOff(int item)
		{
			if (ItemPool.IsValid(item) && ItemPool.CommonnessOf(item) == Commonness.Rare) return true;
			if (!ContentSamples.ItemsByType.TryGetValue(item, out Item sample)) return false;
			return sample.rare is ItemRarityID.Expert or ItemRarityID.Master || sample.createTile == TileID.MasterTrophyBase;
		}

		// Value of one unit of an ingredient (at least 1 copper; a value-0 crafted item counts its ingredients)
		private static long UnitValue(int type) => RecipeSystem.EffectiveValue(type);

		// Progression of a new ingredient: never one of a later true tier than both the item made and the recipe's tier T (a
		// Mythril Anvil of Chlorophyte Bars, a Mechanical Skull eating a Marrow, a Shroomite Bar of Beetle Husks), and for a
		// pickaxe or a drill, never an ore or bar (or anything made of them) that needs its own pick power or more to be mined
		// (an Orichalcum Pickaxe of Chlorophyte Bars). The vanilla ingredients of the item stay allowed (PickaxeAllowed)
		private static bool Fits(int x, int result, int top) => ItemPool.TrueTierOf(x) <= Math.Max(ItemPool.TrueTierOf(result), top) && PickaxeAllowed(x, result);

		public static bool PickaxeAllowed(int x, int result)
		{
			int pick = PickPower(result);
			return pick <= 0 || MineRequirement(x) < pick || VanillaIngredientOf(x, result);
		}

		// Is x an ingredient of a vanilla recipe of this item?
		private static bool VanillaIngredientOf(int x, int item) => VanillaRecipesOf(item).Any(r => RecipeSystem.OriginalIngredients(r).Any(p => p.type == x));

		// The enabled vanilla recipes (not shells, not other mods') making each item, as set up
		private static Dictionary<int, List<int>> vanillaRecipes;
		private static int vanillaRecipesCount = -1;

		private static List<int> VanillaRecipesOf(int item)
		{
			if (vanillaRecipes == null || vanillaRecipesCount != RecipeSystem.RecipeCount)
			{
				vanillaRecipes = new Dictionary<int, List<int>>();
				vanillaRecipesCount = RecipeSystem.RecipeCount;
				mineRequirements = null;
				for (int r = 0; r < RecipeSystem.RecipeCount; r++)
				{
					if (RecipeSystem.IsShell(r) || RecipeSystem.VanillaDisabled(r) || RecipeSystem.LeftAlone(r)) continue;
					int made = RecipeSystem.OriginalResult(r);
					if (!vanillaRecipes.TryGetValue(made, out List<int> list)) vanillaRecipes[made] = list = new List<int>();
					list.Add(r);
				}
			}
			return vanillaRecipes.TryGetValue(item, out List<int> found) ? found : NoRecipe;
		}

		private static readonly List<int> NoRecipe = new();

		// Pick power needed to get an item: its ore level (MiningLevels) or the tile it places (PickToMine), else the least of its
		// vanilla recipes (the most of their ingredients: a Chlorophyte Bar needs 200). From the recipes as set up only
		private static int[] mineRequirements;

		public static int MineRequirement(int item)
		{
			int need = Mine(item, out _);
			return need == MineCycle ? 0 : need;
		}

		// An item whose requirement is being computed: a recipe needing it is left out (a brick made back from its walls
		// needs what the brick's other recipe needs, not nothing)
		private const int MineCycle = int.MaxValue;
		private static int[] mineDepth;
		private static int mineDepthNow;

		// low: the smallest depth of the items in progress this result depended on (int.MaxValue: none); such a result is not
		// cached (it left out a recipe only that chain loops through)
		private static int Mine(int item, out int low)
		{
			low = int.MaxValue;
			if (item <= 0) return 0;
			VanillaRecipesOf(item);
			if (mineRequirements == null || mineRequirements.Length < ItemPool.Info.Length)
			{
				mineRequirements = new int[Math.Max(ItemPool.Info.Length, 1)];
				Array.Fill(mineRequirements, -1);
				mineDepth = new int[mineRequirements.Length];
				mineDepthNow = 0;
			}
			if (item >= mineRequirements.Length) return PickToMine(item);
			if (mineRequirements[item] >= 0) return mineRequirements[item];
			if (mineRequirements[item] == -2)
			{
				low = mineDepth[item];
				return MineCycle;
			}
			int own = PickToMine(item);
			foreach ((int pick, int[] ores) in MiningLevels)
			{
				if (ores.Contains(item)) own = Math.Max(own, pick);
			}
			if (own > 0) return mineRequirements[item] = own;
			int depth = mineDepthNow++;
			mineDepth[item] = depth;
			mineRequirements[item] = -2;
			int best = MineCycle;
			try
			{
				foreach (int r in VanillaRecipesOf(item))
				{
					int[] groups = RecipeSystem.OriginalGroups(r);
					int need = 0;
					foreach ((int type, _) in RecipeSystem.OriginalIngredients(r))
					{
						int group = GroupOf(groups, type);
						int part;
						if (group < 0)
						{
							part = Mine(type, out int l);
							low = Math.Min(low, l);
						}
						else
						{
							// The easiest item of the group (MineCycle when all of them loop)
							part = MineCycle;
							foreach (int valid in RecipeGroup.recipeGroups[group].ValidItems)
							{
								part = Math.Min(part, Mine(valid, out int l));
								low = Math.Min(low, l);
							}
						}
						need = Math.Max(need, part);
						if (need == MineCycle) break;
					}
					best = Math.Min(best, need);
				}
			}
			finally
			{
				mineDepthNow--;
			}
			if (low < depth)
			{
				mineRequirements[item] = -1;
				return best;
			}
			low = int.MaxValue;
			return mineRequirements[item] = best == MineCycle ? 0 : best;
		}

		// Doable (with what it has now) before the U of its result. A vanilla source (VanillaSource) is never locked
		private static bool NeedsLock(RecipePlan result, int i, int[] doable) =>
			Gated(i) && !RecipeSystem.VanillaDisabled(i) && result.ResultTiers[i] > 0 && doable[i] < result.ResultTiers[i] && !VanillaSource(result, i);

		// A recipe still giving its vanilla result with its vanilla ingredients and stations is that item's vanilla source: it
		// gives it as soon as it is doable. One whose ingredients or stations were replaced is locked like any other (a Luminite
		// Bullet recipe made of early materials would give Luminite Bullets at hardmode's start)
		public static bool VanillaSource(RecipePlan result, int i) => result.Results[i].type == RecipeSystem.OriginalResult(i)
			&& result.Ingredients[i] == null && result.Tiles[i] == null;

		// The tier each recipe gives its result from while planning: U for the results a key will lock, at once for the vanilla
		// sources. With U for a vanilla source, its result looked later than it is, and was chosen as a key of that tier
		private static int[] LockTiers(RecipePlan result) => Enumerable.Range(0, Count).Select(i => VanillaSource(result, i) ? 0 : result.ResultTiers[i]).ToArray();

		// One replaceable material ingredient (never the planned key, never a group slot) becomes a key material of tier u
		// (else u + 1), weighted by commonness; each material is key of at most KeyUseCap recipes while others are left. Never
		// an item made (directly or not) from the recipe's result (graph: no loop).
		// rng: RollKey.KeyIngredient of the recipe when null; uncommon: an uncommon material when the tier has one; append: a
		// recipe without a replaceable material slot gets the key as a new ingredient (a Zenith keeps its swords), else any
		// replaceable slot takes it
		private static int PlaceKey(RecipePlan result, int[] results, int i, int u, List<int>[,] buckets, WeightTable weights, int[] avail, Dictionary<int, int> uses, ItemGraph graph,
			out bool later, Random rng = null, bool uncommon = false, bool append = false)
		{
			later = false;
			int[] groups = GroupsOf(result, i);
			(int type, int stack)[] current = ((int, int)[])(result.Ingredients[i] ?? RecipeSystem.OriginalIngredients(i)).Clone();
			// A group slot stays (it would leave its group behind), and so does the planned key (a key of T must not give way to
			// the key of U)
			List<int> slots = Enumerable.Range(0, current.Length).Where(k => IngredientReplaceable(current[k].type) && !InGroup(groups, current[k].type, out _)
				&& current[k].type != result.PlannedKeys[i]).ToList();
			if (slots.Count == 0 && !append) return 0;
			rng ??= DetRandom.Get(RollKey.KeyIngredient, RecipeSystem.RecipeKey(i));
			int slot = -1;
			List<int> materialSlots = slots.Where(k => MaterialLike(ItemPool.Info[current[k].type].Category)).ToList();
			if (materialSlots.Count > 0) slot = materialSlots[rng.Next(materialSlots.Count)];
			// Without a material slot, a new ingredient rather than one of the shape's (a Zenith keeps its swords)
			else if (slots.Count > 0 && !append) slot = slots[rng.Next(slots.Count)];
			int originalResult = RecipeSystem.OriginalResult(i);
			HashSet<int> below = graph.From(results[i]);
			// The key keeps the recipe's value: it is worth what the original ingredients were, less the other slots now
			// (the slot may already hold a replacement held by the stack cap or rounded up: its value does not compound)
			long others = current.Where((p, k) => k != slot).Sum(p => UnitValue(p.type) * p.stack);
			// The value of the recipe the ingredients were planned as: the item's own vanilla recipe for a swap
			long planned = RecipeSystem.OriginalIngredientValue(result.SwapPartner[i] >= 0 ? result.SwapPartner[i] : i);
			long budget = Math.Max(1, (long)(planned * MaxValueFactor) + Item.silver - others);
			long need = Math.Max(1, planned - others);
			for (int t = u; t <= Math.Min(u + 1, Tiers.Max); t++)
			{
				List<(int item, float weight)> options = new();
				for (int c = 0; c < ItemCategories.Count; c++)
				{
					int categoryWeight = weights.Get((ItemCategory)c);
					if (categoryWeight <= 0) continue;
					foreach (int x in buckets[c, t])
					{
						// Found at t and not crafted earlier: the recipe is doable exactly from t
						if (x == originalResult || x == results[i] || current.Any(p => p.type == x) || x >= avail.Length || avail[x] != t || InGroup(groups, x, out _) || below.Contains(x)) continue;
						options.Add((x, categoryWeight * CommonnessWeight(x)));
					}
				}
				if (options.Count == 0) continue;
				// Real materials first (stackable materials, bars, blocks): a bucket, a sponge or a tool only when the tier has none
				List<(int item, float weight)> materials = options.Where(o => KeyMaterial(o.item)).ToList();
				if (materials.Count > 0) options = materials;
				int tier = t;
				options = PreferFits(result, options, x => Fits(x, results[i], tier));
				options = Prefer(options, x => UnitValue(x) <= budget);
				// One whose stack can be worth the slot (no 30 walls for an ingot's worth)
				options = Prefer(options, x => UnitValue(x) * ReplacementStack(long.MaxValue / 4, x) >= need);
				if (uncommon)
				{
					List<(int item, float weight)> rarer = options.Where(o => ItemPool.CommonnessOf(o.item) == Commonness.Uncommon).ToList();
					if (rarer.Count > 0) options = rarer;
				}
				// Anti-abuse: spread the keys while other materials of the tier are left; a common material may serve more recipes
				List<(int item, float weight)> fresh = options.Where(o => uses.GetValueOrDefault(o.item) < KeyUseCap(o.item)).ToList();
				if (fresh.Count == 0)
				{
					float least = options.Min(o => uses.GetValueOrDefault(o.item) / (float)KeyUseCap(o.item));
					fresh = options.Where(o => uses.GetValueOrDefault(o.item) / (float)KeyUseCap(o.item) <= least + 1e-4f).ToList();
				}
				// Common keys far more often than rare ones (on top of the category weight)
				fresh = fresh.Select(o => (o.item, o.weight / CommonnessWeight(o.item) * KeyCommonnessWeight(o.item))).ToList();
				int key = WeightedPick(fresh, rng);
				if (key <= 0) continue;
				(int, int) placed = (key, ReplacementStack(Math.Max(1, planned - others), key));
				if (slot >= 0) current[slot] = placed;
				else current = current.Append(placed).ToArray();
				result.Ingredients[i] = current;
				result.IngredientValues[i] = current.Sum(p => UnitValue(p.type) * p.stack);
				uses[key] = uses.GetValueOrDefault(key) + 1;
				graph.Add(key, results[i]);
				if (result.LockKeys != null) result.LockKeys[i] = key;
				later = t > u;
				return key;
			}
			return 0;
		}

		// Prefer(Fits), counting the keys left with an unfit material (RecipePlan.UnfitKeys)
		private static List<(int item, float weight)> PreferFits(RecipePlan plan, List<(int item, float weight)> options, Func<int, bool> fits)
		{
			List<(int item, float weight)> kept = options.Where(o => fits(o.item)).ToList();
			if (kept.Count > 0) return kept;
			plan.UnfitKeys++;
			return options;
		}

		// The options passing a preference, or all of them when none does (a key is better late than missing)
		private static List<(int item, float weight)> Prefer(List<(int item, float weight)> options, Func<int, bool> preferred)
		{
			List<(int item, float weight)> kept = options.Where(o => preferred(o.item)).ToList();
			return kept.Count > 0 ? kept : options;
		}

		// A key material serves at most this many recipes while another one of its tier is less used (common ones more)
		private static int KeyUseCap(int item) => ItemPool.CommonnessOf(item) switch
		{
			Commonness.Common => 18,
			Commonness.Rare => 3,
			_ => UncommonKeyUseCap
		};

		private const int UncommonKeyUseCap = 6;

		// Commonness weights of a key: a tier's common materials lock most recipes, rare ones (Broken Hero Sword) seldom
		private static float KeyCommonnessWeight(int item) => ItemPool.CommonnessOf(item) switch
		{
			Commonness.Common => 10f,
			Commonness.Rare => 0.1f,
			_ => 1f
		};

		// A real crafting material: a stackable material, bar or block (not a bucket, a sponge or a tool used in a recipe), and
		// not a wall (a quarter of a block: 30 Luminite Brick Walls are no key of a Lunar armor)
		private static bool KeyMaterial(int item) => ItemPool.IsValid(item) && MaterialLike(ItemPool.Info[item].Category) && StackRules.MaxStackOf(item) > 1
			&& !(ContentSamples.ItemsByType.TryGetValue(item, out Item sample) && sample.createWall > WallID.None);

		// Item -> item edges of the planned recipes (each ingredient, or every member of its recipe group, to the result): an
		// ingredient is refused when the recipe's result already leads to it, so no item is ever made from itself
		private sealed class ItemGraph
		{
			private readonly Dictionary<int, HashSet<int>> next = new();
			// The edges added since Mark (null: not recorded), so a tentative plan can be taken back (Undo)
			private List<(int from, int to)> journal;

			public void Add(int from, int to)
			{
				if (from <= 0 || to <= 0) return;
				if (!next.TryGetValue(from, out HashSet<int> set)) next[from] = set = new HashSet<int>();
				if (set.Add(to)) journal?.Add((from, to));
			}

			public void Mark() => journal = new List<(int, int)>();

			// Removes the edges added since Mark; keep: stop recording and keep them
			public void Undo(bool keep = false)
			{
				if (journal != null && !keep)
				{
					foreach ((int from, int to) in journal) next[from].Remove(to);
				}
				journal = null;
			}

			public void AddRecipe(IEnumerable<(int type, int stack)> ingredients, int[] groups, int result)
			{
				if (result <= 0) return;
				foreach ((int type, _) in ingredients)
				{
					int group = GroupOf(groups, type);
					if (group < 0) Add(type, result);
					else
					{
						foreach (int v in RecipeGroup.recipeGroups[group].ValidItems) Add(v, result);
					}
				}
			}

			// Every item made from this one, directly or not (itself included)
			public HashSet<int> From(int item)
			{
				HashSet<int> seen = new() { item };
				Stack<int> todo = new();
				todo.Push(item);
				while (todo.Count > 0)
				{
					if (!next.TryGetValue(todo.Pop(), out HashSet<int> set)) continue;
					foreach (int to in set)
					{
						if (seen.Add(to)) todo.Push(to);
					}
				}
				return seen;
			}
		}

		// ---------------------------------------------------------------- Twins

		// Two vanilla recipes that end up with the same result and the same ingredients (vanilla alternatives that differed by
		// their station only get the same rolls: their key is the result and the ingredients) would show as one recipe on two
		// random stations. The one usable earliest (no later than any of them) stays, the others are copies (Twin), switched
		// off when applied and left out of the availability. The copies keep their own planned stations: those of another
		// recipe could be above their level or thematic (a Sandstone Brick copy on a Bone Welder), and a switched-off recipe
		// needs none. Recipes whose stations stay vanilla keep them (vanilla alternatives)
		private static void ShareTwinStations(RecipePlan result, (int, int)[][] ingredients, int[][] tiles, int[] avail)
		{
			Dictionary<int, List<int>> placers = Stations.PlacersByTile();
			int[] Tiles(int i) => result.Tiles[i] ?? tiles[i];
			int StationAvail(int i) => Tiles(i).Select(tile => TileAvailability(tile, placers, avail)).DefaultIfEmpty(0).Max();
			string Key(int i)
			{
				Recipe recipe = Main.recipe[i];
				string items = string.Join(",", (result.Ingredients[i] ?? ingredients[i]).Select(p => $"{p.Item1}x{p.Item2}").OrderBy(s => s, StringComparer.Ordinal));
				string groups = string.Join(",", GroupsOf(result, i).OrderBy(g => g));
				string conditions = string.Join(",", recipe.Conditions.Select(c => c.Description.Key));
				return $"{result.Results[i].type}x{result.Results[i].stack}|{items}|{groups}|{conditions}";
			}
			int groupsShared = 0, twins = 0;
			List<string> examples = new();
			IEnumerable<List<int>> groupsFound = Enumerable.Range(0, Count).Where(i => !RecipeSystem.IsShell(i) && !RecipeSystem.LeftAlone(i) && !RecipeSystem.VanillaDisabled(i) && !result.Twin[i])
				.GroupBy(Key).Select(g => g.ToList()).Where(g => g.Count > 1);
			foreach (List<int> members in groupsFound)
			{
				// Kept stations win (a protected recipe's are its own); several different kept ones stay vanilla alternatives
				List<int> fixedOnes = members.Where(i => !RecipeSystem.Touchable(i)).ToList();
				if (fixedOnes.Select(i => string.Join(",", tiles[i].OrderBy(t => t))).Distinct().Count() > 1) continue;
				int chosen = fixedOnes.Count > 0 ? fixedOnes[0] : members.OrderBy(StationAvail).ThenBy(i => i).First();
				int[] shared = Tiles(chosen);
				bool differed = false;
				foreach (int i in members)
				{
					if (i == chosen) continue;
					if (!Tiles(i).OrderBy(t => t).SequenceEqual(shared.OrderBy(t => t)))
					{
						// Vanilla stations (not randomized): vanilla alternatives, left as they are
						if (!CraftingConfig.Instance.RandomizeStations) continue;
						differed = true;
					}
					result.Twin[i] = true;
					twins++;
				}
				if (differed) groupsShared++;
				if (examples.Count < 5 && members.Any(i => result.Twin[i])) examples.Add($"{DetRandom.ItemName(result.Results[chosen].type)} (recipes {string.Join("/", members)})");
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Recipe plan: {twins} identical recipe copies switched off, {groupsShared} groups on different stations; " +
				$"e.g. {(examples.Count == 0 ? "none" : string.Join(", ", examples))}");
		}

		// ---------------------------------------------------------------- Stations

		// Tier from which a station can be placed with an availability (RecipePlan.Availability), for the tests
		public static int StationTier(int tile, int[] avail) => TileAvailability(tile, Stations.PlacersByTile(), avail);

		// Earliest tier a station can be placed: its cheapest placing item (crafted, found or guaranteed: avail), or its
		// protected tier
		private static int TileAvailability(int tile, Dictionary<int, List<int>> itemsByTile, int[] avail)
		{
			// Tiles without an item (Demon Altar...) are part of the world
			if (!itemsByTile.TryGetValue(tile, out List<int> placers)) return 0;
			int best = KeepsVanillaSource(placers) ? ProtectedStationTier(tile) : Never;
			foreach (int placer in placers)
			{
				if (placer < avail.Length) best = Math.Min(best, avail[placer]);
			}
			return best;
		}

		// A protected placer that no recipe makes (bought from a town NPC, found in the world) keeps its vanilla way to be
		// obtained. An unprotected one may lose its shop, drop or chest: it only counts through avail (its guaranteed drops)
		public static bool KeepsVanillaSource(IEnumerable<int> placers) =>
			placers.Any(p => ItemPool.IsValid(p) && ItemPool.Info[p].Protected && !Obtainability.RecipeResults.Contains(p));

		// Progression stations kept with their vanilla source are usable from their tier. A crafted one (Mythril Anvil,
		// Adamantite Forge...) is available once its recipe is doable, like any item: a protected recipe keeps its result;
		// an unprotected station also counts from its guaranteed drops (Findability.Tier)
		public static int ProtectedStationTier(int tile)
		{
			Stations.StationInfo info = Stations.Info(tile);
			return info.Kind == StationKind.Progression ? info.Tier : Never;
		}

		// Stations a recipe may be given: basic and progression ones (not the Demon Altar, not themed ones). A modded progression
		// station (Stations.ModdedInfo) only to recipes of its tier or above, like the vanilla ones (orderTier, RecipeTier)
		private static bool Assignable(int tile)
		{
			StationKind kind = Stations.Info(tile).Kind;
			return !Stations.Info(tile).World && (kind == StationKind.Basic || kind == StationKind.Progression);
		}

		private static void PlanStations(RecipePlan result, int[] results, int[] avail, int[] recipeTiers)
		{
			// avail does not change while stations are planned: each station's availability is computed once
			Dictionary<int, List<int>> itemsByTile = Stations.PlacersByTile();
			Dictionary<int, int> tileAvail = new();
			int TileAvail(int tile)
			{
				if (!tileAvail.TryGetValue(tile, out int a)) tileAvail[tile] = a = TileAvailability(tile, itemsByTile, avail);
				return a;
			}
			// A fixed order of the stations: a recipe making a station only ever requires stations placed before it,
			// so no station can end up needing itself
			// From the original stations: the live ones are already randomized (the plan would depend on the previous one)
			List<int> order = Stations.RequiredTiles().Where(Assignable)
				.OrderBy(TileAvail).ThenBy(t => Stations.Info(t).Kind == StationKind.Basic ? 0 : 1).ThenBy(t => DetRandom.StableHash(Stations.Name(t)))
				.ToList();
			Dictionary<int, int> position = order.Select((tile, index) => (tile, index)).ToDictionary(x => x.tile, x => x.index);
			int[] orderAvail = order.Select(TileAvail).ToArray();
			int[] orderTier = order.Select(s => Stations.Info(s).Kind == StationKind.Progression ? Stations.Info(s).Tier : -1).ToArray();
			float keep = CraftingConfig.Instance.StationKeepChance / 100f;
			for (int i = 0; i < Count; i++)
			{
				int[] original = RecipeSystem.OriginalTiles(i);
				if (original.Length == 0 || !RecipeSystem.Touchable(i)) continue;
				int bound = int.MaxValue;
				foreach (int made in new[] { RecipeSystem.OriginalResult(i), results[i] })
				{
					if (Stations.PlacedTile(made, out int placed) && position.TryGetValue(placed, out int p)) bound = Math.Min(bound, p);
				}
				Random rng = DetRandom.Get(RollKey.Station, RecipeSystem.RecipeKey(i));
				List<int> tiles = new();
				foreach (int tile in original)
				{
					int chosen = tile;
					if (Assignable(tile))
					{
						int limit = TileAvail(tile);
						int recipeTier = RecipeTier(i);
						// A house recipe only ever needs a basic station (guaranteed in the world from the start)
						bool house = RecipeSystem.HouseRecipe(i);
						// Never a progression station above the recipe's level (it may also be unavailable: bought, or modded)
						List<int> allowed = new();
						for (int k = 0; k < order.Count && k < bound; k++)
						{
							if (orderAvail[k] <= limit && orderTier[k] <= recipeTier && (!house || Stations.Info(order[k]).Kind == StationKind.Basic)) allowed.Add(order[k]);
						}
						bool keepOwn = allowed.Contains(tile) && rng.NextDouble() < keep;
						if (!keepOwn && allowed.Count > 0) chosen = allowed[rng.Next(allowed.Count)];
					}
					if (!tiles.Contains(chosen)) tiles.Add(chosen);
				}
				if (!tiles.SequenceEqual(original)) result.Tiles[i] = tiles.ToArray();
			}
		}

		// ---------------------------------------------------------------- Ingredients

		private static readonly ItemCategory[] Unusable = { ItemCategory.Bag, ItemCategory.BossSummon };

		// Categories whose items stack (the surprises of a stackable result): no gear, no vanity
		private static readonly HashSet<ItemCategory> StackableCategories = new()
		{
			ItemCategory.Misc, ItemCategory.Critter, ItemCategory.Dye, ItemCategory.Ammo, ItemCategory.Potion, ItemCategory.Food, ItemCategory.OreBar,
			ItemCategory.Block, ItemCategory.Furniture, ItemCategory.Material
		};

		// Read-only, for the tests (TierCoherence): the materials found (never crafted) at each tier, by category: the raw part of
		// what PlanIngredients picks from (crafted items join them as their recipes are planned), and the key materials of
		// LockResults
		public static List<int>[,] IngredientCandidates() => FindableBuckets();

		// May the planner replace this ingredient at all (randomizable, not an upgrade, a bag or a summon)? A recipe group slot
		// may be replaced too: the rebuilt recipe then drops that group
		public static bool IngredientReplaceable(int item) =>
			ItemPool.IsRandomizable(item) && !ItemPool.IsSpecial(item) && !Unusable.Contains(ItemPool.Info[item].Category);

		// May this item be a new ingredient at all (found or crafted)?
		private static bool Eligible(int item, HashSet<int> stationItems) =>
			item > 0 && item < ItemPool.Info.Length && IngredientReplaceable(item) && !ItemPool.IsBanned(item) && !stationItems.Contains(item);

		// Hardmode pair ores (present or not depending on the altars: the plan must not depend on them), ores and blocks that need
		// a strong pickaxe (Obsidian, Ebonstone, dungeon bricks...: a pickaxe chain could otherwise need its own block): never a
		// new ingredient, nor anything made of them
		private static bool Tainted(int item) => OreMap.IsHardmodePairOre(item) || MiningOres.Contains(item) || PickToMine(item) >= MiningLevels[0].pick;

		// Guaranteed stations (basic ones, and progression or themed ones when unprotected) are never new ingredients
		private static HashSet<int> GuaranteedStationItems()
		{
			HashSet<int> stationItems = Stations.Basic().Select(s => s.info.Item).ToHashSet();
			stationItems.UnionWith(GuaranteedSources.Requirements.Where(r => r.Station).Select(r => r.Item));
			return stationItems;
		}

		private static List<int>[,] NewBuckets()
		{
			List<int>[,] buckets = new List<int>[ItemCategories.Count, Tiers.Count];
			for (int c = 0; c < ItemCategories.Count; c++)
			{
				for (int t = 0; t < Tiers.Count; t++) buckets[c, t] = new List<int>();
			}
			return buckets;
		}

		// Materials found at each tier (by category), never crafted
		private static List<int>[,] FindableBuckets()
		{
			List<int>[,] buckets = NewBuckets();
			HashSet<int> stationItems = GuaranteedStationItems();
			for (int item = 1; item < ItemPool.Info.Length; item++)
			{
				if (!Eligible(item, stationItems) || Tainted(item)) continue;
				int tier = Findability.Tier(item);
				if (tier < Tiers.Count) buckets[(int)ItemPool.Info[item].Category, tier].Add(item);
			}
			return buckets;
		}

		// Commonness weights of a new or key ingredient (MaterialTiers): a small core of common materials carries most
		// recipes, rare ones stay occasional
		public const float CommonWeight = 10f;
		public const float UncommonWeight = 3f;
		public const float RareWeight = 0.5f;

		public static float CommonnessWeight(int item) => ItemPool.CommonnessOf(item) switch
		{
			Commonness.Common => CommonWeight,
			Commonness.Rare => RareWeight,
			_ => UncommonWeight
		};

		// Anti-abuse: a material is a (non key) ingredient of at most this share of the rebuilt recipes that may use it (those
		// planned at its tier or later), and never less than MinUseCap
		public const float MaxUseShare = 0.05f;
		public const int MinUseCap = 12;

		// The vanilla shape: the categories of the vanilla ingredients add this share of the ingredient weights' total, split
		// between them (a Zenith gets weapons often, never only weapons; weight-0 categories only come from the shape)
		private const double ShapeBonus = 0.8;

		// Weight of a tier for a slot: by its distance to the tier of the vanilla ingredient the slot follows (0, 1, 2), plus a
		// pull towards the recipe's own tier and the one before, and a little for any other tier
		private static readonly float[] BlurWeights = { 3f, 2f, 1f };
		private const float TopTierWeight = 3f;
		private const float BelowTopWeight = 1.5f;
		private const float AnyTierWeight = 0.25f;

		// The new ingredients are worth the vanilla ones times a factor in [0.6, 1.6] (soft: stacks stay within their caps)
		private const double MinValueFactor = 0.6;
		private const double ValueFactorRange = 1.0;

		// Never more than this many times the vanilla value in all (no "25 arrows for a Hallowed Bar's recipe"), and the least
		// weight a candidate keeps when its unit value suits its slot badly
		private const double MaxValueFactor = 2.0;
		private const double MinCloseness = 0.25;

		// Attempts of the first slot of a recipe keeping its result at the non-material categories of its shape
		private const int ShapeAttempts = 6;

		// Share of the host recipe's shape in a swapped recipe's categories (the rest: the item's own vanilla recipe)
		private const double SwapHostShape = 0.7;

		// Ingredients of a shell recipe before the spread (IngredientCountSpread 2: 1 to 4)
		private const int ShellIngredientCount = 2;

		private static bool MaterialLike(ItemCategory category) => category is ItemCategory.Material or ItemCategory.OreBar or ItemCategory.Block;

		// Weighted pick (deterministic: options in a fixed order); 0 when nothing has a weight
		private static int WeightedPick(List<(int item, float weight)> options, Random rng)
		{
			double total = options.Sum(o => (double)o.weight);
			if (total <= 0) return 0;
			double roll = rng.NextDouble() * total;
			foreach ((int item, float weight) in options)
			{
				roll -= weight;
				if (roll < 0) return item;
			}
			return options[^1].item;
		}

		// The change in the number of ingredients: 0, +1 and -1 three times as likely as each larger change
		private static int CountDelta(Random rng, int spread)
		{
			if (spread <= 0) return 0;
			List<(int item, float weight)> options = new();
			for (int d = -spread; d <= spread; d++) options.Add((d, Math.Abs(d) <= 1 ? 3f : 1f));
			return WeightedPick(options, rng);
		}

		// Stack of a new ingredient worth a value (rounded up), within the stack caps
		private static int ReplacementStack(long value, int x)
		{
			long unit = UnitValue(x);
			int cap = OneOff(x) ? Math.Min(OneOffCap, CraftingConfig.Instance.IngredientStackCap) : CraftingConfig.Instance.IngredientStackCap;
			return (int)Math.Clamp((value + unit - 1) / unit, 1, Math.Min(StackRules.MaxStackOf(x), cap));
		}

		// The recipe group of a recipe's groups an item belongs to (-1: none)
		private static int GroupOf(IEnumerable<int> groups, int item)
		{
			foreach (int id in groups)
			{
				if (RecipeGroup.recipeGroups.TryGetValue(id, out RecipeGroup group) && group.ContainsItem(item)) return id;
			}
			return -1;
		}

		// Recipes PlanIngredients rebuilds: touchable, enabled as set up, with at least one replaceable vanilla ingredient
		public static bool Rebuildable(int i) => CraftingConfig.Instance.RandomizeRecipes && CraftingConfig.Instance.IngredientMode == IngredientMode.Randomized
			&& RecipeSystem.IngredientsTouchable(i) && !RecipeSystem.VanillaDisabled(i) && RecipeSystem.OriginalIngredients(i).Any(x => IngredientReplaceable(x.type));

		// Every touchable recipe is rebuilt from the fuzzy shape of its vanilla one: about as many ingredients, categories biased
		// towards the vanilla ones, tiers blurred around the vanilla ones and pulled towards T, the recipe's tier (never above),
		// one key ingredient found exactly at T, about the vanilla value. A swapped recipe is rebuilt for the item it gives (its
		// proxy, the item's vanilla recipe: tier, key, value, tiers followed), with mostly its own shape and count (the swap
		// shows); both recipes of a pair are planned together, and a pair one of them can't be planned for is cancelled. Recipes are
		// planned in an acyclic order (by T, then RecipeKey): an item is a candidate once found (Findability) or once a recipe
		// planned earlier makes it (doable with what was had then, at its planned station), and never when the result already
		// leads to it (ItemGraph), so no recipe ever relies on itself, directly or not. Kept: protected ingredients (and their
		// groups), results, stations
		private sealed class Rebuilder
		{
			private readonly RecipePlan result;
			private readonly int[] results;
			private readonly int[] vanillaAvail;
			private readonly int[] recipeTiers;
			private readonly int[][] tiles;
			private readonly PlanSettings settings;
			private readonly ItemGraph graph;
			private readonly CraftingConfig cfg = CraftingConfig.Instance;
			private readonly WeightTable weights = CraftingConfig.Instance.IngredientWeights;
			private readonly WeightTable lootWeights = LootConfig.Instance.DropCategoryWeights;
			private readonly HashSet<int> stationItems = GuaranteedStationItems();
			private readonly Dictionary<int, List<int>> placers = Stations.PlacersByTile();
			// When each item is had: found, or made by a recipe planned so far (real); the same without anything made of tainted
			// items (clean): what the candidates are judged by
			private readonly int[] real;
			private readonly int[] clean;
			// The recipe that made each item a crafted candidate (-1: found)
			private readonly int[] source;
			// Eligible items by category and clean tier
			private readonly List<int>[,] pool = NewBuckets();
			// Vanilla recipe groups that may be a new ingredient (as their iconic item), with that item's category
			private readonly List<(int id, RecipeGroup group, ItemCategory category)> groups = new();
			// Uses of each material (a group: -(id + 1)) as a new ingredient, and as a planned key: what the use caps and the reuse
			// penalty read, live (the penalty is felt within a tier too)
			private Dictionary<int, int> uses = new();
			private Dictionary<int, int> keyUses = new();
			private readonly int[] targets;
			private int[] useCaps;
			private Counters stats;
			private int position, cancelled, repaired, switchedOff, keptLooping, rekeyed, rekeyFailed;
			private readonly SortedSet<int> noKey = new();
			// The recipes as set up (original results), for the loops that are already there (KeepOriginals)
			private ItemGraph originalGraph;

			// What the log line counts (a struct: a tentative pair is taken back with a copy)
			private struct Counters
			{
				public int Rebuilt, VanillaKept, Keyed, Crafted, GroupSlots, SlotsTotal, Surprises, SurpriseSlots, Swapped;
				// The same for the recipes of a stackable result, and the rebuilt recipes with a surprise at least
				public int StackableSurprises, StackableSurpriseSlots, SurpriseRecipes;
				// Keys that took a material not fitting the progression rules (Fits) for lack of another
				public int UnfitKeys;
			}

			public Rebuilder(RecipePlan result, int[] results, int[] vanillaAvail, int[] recipeTiers, int[][] tiles, PlanSettings settings, ItemGraph graph)
			{
				this.result = result;
				this.results = results;
				this.vanillaAvail = vanillaAvail;
				this.recipeTiers = recipeTiers;
				this.tiles = tiles;
				this.settings = settings;
				this.graph = graph;
				int items = ItemPool.Info.Length;
				real = new int[items];
				clean = new int[items];
				source = new int[items];
				Array.Fill(source, -1);
				for (int item = 1; item < items; item++)
				{
					real[item] = Findability.Tier(item);
					clean[item] = Tainted(item) ? Never : real[item];
					if (clean[item] < Tiers.Count && Eligible(item, stationItems)) pool[(int)ItemPool.Info[item].Category, clean[item]].Add(item);
				}
				real[0] = clean[0] = Never;
				SortedSet<int> used = new();
				for (int i = 0; i < Count; i++)
				{
					if (!RecipeSystem.LeftAlone(i)) used.UnionWith(RecipeSystem.OriginalGroups(i));
				}
				foreach (int id in used)
				{
					if (!RecipeGroup.recipeGroups.TryGetValue(id, out RecipeGroup group) || !Eligible(group.IconicItemId, stationItems) || Tainted(group.IconicItemId)) continue;
					groups.Add((id, group, ItemPool.Info[group.IconicItemId].Category));
				}
				targets = new int[Count];
			}

			// The recipe whose vanilla recipe a recipe is rebuilt as: its swap partner, or itself
			private int Proxy(int i) => result.SwapPartner[i] >= 0 ? result.SwapPartner[i] : i;

			// T: the tier the recipe (its proxy) was doable at (never later: its level, RecipeTier), and never before the U of its
			// result (the key ingredient then locks it) nor its ResultFloor. A house recipe is built at once
			private int TargetTier(int i)
			{
				if (RecipeSystem.HouseRecipe(i)) return 0;
				int proxy = Proxy(i);
				int rt = RecipeTier(proxy);
				int cap = recipeTiers[proxy] < Never ? Math.Min(rt, recipeTiers[proxy]) : rt;
				return Tiers.Clamp(Math.Max(Math.Max(cap, result.ResultTiers[i]), ResultFloor(results[i])));
			}

			// w / (1 + k * uses / tolerance): a material already in many recipes is picked less often (IngredientReusePenalty),
			// from the live counts. A common material tolerates more uses (its weight is higher, so it is expected in more recipes):
			// the penalty spreads the picks without turning the commonness order upside down
			private float Penalty(Dictionary<int, int> counts, int key, float tolerance) =>
				settings.ReusePenalty <= 0 ? 1f : 1f / (1f + settings.ReusePenalty * counts.GetValueOrDefault(key) / tolerance);

			private float IngredientPenalty(int key, int item) => Penalty(uses, key, Math.Max(1f, CommonnessWeight(item) / UncommonWeight));

			private float KeyPenalty(int item) => Penalty(keyUses, item, Math.Max(1f, KeyUseCap(item) / (float)UncommonKeyUseCap));

			public void Run()
			{
				int count = Count;
				int[] reaching = new int[Tiers.Count];
				for (int i = 0; i < count; i++)
				{
					targets[i] = !RecipeSystem.IsShell(i) && Rebuildable(i) ? TargetTier(i) : -1;
					if (targets[i] < 0 || RecipeSystem.HouseRecipe(i)) continue;
					for (int t = 0; t <= targets[i]; t++) reaching[t]++;
				}
				useCaps = reaching.Select(r => Math.Max(MinUseCap, (int)Math.Ceiling(r * MaxUseShare))).ToArray();
				// The edges no choice can change: the recipes kept as they are, and the protected ingredients of the rebuilt ones.
				// A swapped recipe's lead to both results it may give (CancelSwap gives it its own back): a recipe planned before a
				// cancel never takes an item its protected ingredients then make
				for (int i = 0; i < count; i++)
				{
					if (RecipeSystem.IsShell(i) || RecipeSystem.VanillaDisabled(i)) continue;
					graph.AddRecipe(FixedIngredients(i), RecipeSystem.OriginalGroups(i), results[i]);
					if (result.SwapPartner[i] >= 0) graph.AddRecipe(FixedIngredients(i), RecipeSystem.OriginalGroups(i), RecipeSystem.OriginalResult(i));
				}
				// The other recipes (kept ingredients) are evaluated in the same order, from the tier they were doable at
				List<int> order = Enumerable.Range(0, count).Where(i => !RecipeSystem.VanillaDisabled(i))
					.OrderBy(i => targets[i] >= 0 ? targets[i] : recipeTiers[i] < Never ? recipeTiers[i] : Tiers.Count)
					.ThenBy(i => (uint)RecipeSystem.RecipeKey(i)).ThenBy(i => i).ToList();
				int[] rank = Enumerable.Repeat(-1, count).ToArray();
				for (int k = 0; k < order.Count; k++) rank[order[k]] = k;
				void PlanAlone(int i)
				{
					result.PlanOrder[i] = position++;
					if (targets[i] >= 0 && !Rebuild(i)) KeepOriginals(i);
					// Switched off by KeepOriginals: it makes nothing
					if (!result.Twin[i]) Made(i);
				}
				foreach (int i in order)
				{
					// Planned with its swap partner
					if (result.PlanOrder[i] >= 0) continue;
					int partner = result.SwapPartner[i];
					if (targets[i] >= 0 && partner >= 0 && targets[partner] >= 0)
					{
						// A pair is planned once the order reaches its later recipe: everything made by then is known, so the keys of
						// both are had no earlier than their tiers (in Chaos mode the two may be tiers apart)
						if (result.PlanOrder[partner] < 0 && rank[partner] > rank[i]) continue;
						if (PlanPair(partner, i)) continue;
						// Cancelled: the recipe left waiting is planned alone too, first
						if (result.PlanOrder[partner] < 0) PlanAlone(partner);
					}
					PlanAlone(i);
				}
				RecheckKeys();
				result.UnfitKeys += stats.UnfitKeys;
				result.UseCaps = useCaps;
				string busiest = string.Join(", ", uses.Where(p => p.Key > 0).OrderByDescending(p => p.Value).ThenBy(p => p.Key).Take(5).Select(p => $"{DetRandom.ItemName(p.Key)} x{p.Value}"));
				string surpriseShare = stats.SurpriseSlots == 0 ? "-" : $"{stats.Surprises / (double)stats.SurpriseSlots:P0}";
				string stackableShare = stats.StackableSurpriseSlots == 0 ? "-" : $"{stats.StackableSurprises / (double)stats.StackableSurpriseSlots:P0}";
				string recipeShare = stats.Rebuilt == 0 ? "-" : $"{stats.SurpriseRecipes / (double)stats.Rebuilt:P0}";
				ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Recipe plan: {stats.Rebuilt} recipes rebuilt ({stats.Swapped} for a swapped result, {cancelled} swaps cancelled; {stats.VanillaKept} kept vanilla: " +
					$"no candidate or no key, {repaired} of them with a looping ingredient replaced, {switchedOff} switched off for a loop, {keptLooping} looping but kept: no other source), " +
					$"{stats.Keyed} with a key of their tier ({rekeyed} keys chosen again: made earlier later on, {rekeyFailed} without another) " +
					$"(none at tiers {(noKey.Count == 0 ? "-" : string.Join(", ", noKey))}), {stats.SlotsTotal} new ingredients of which {stats.Crafted} crafted, {stats.GroupSlots} recipe groups and " +
					$"{stats.Surprises} surprises ({surpriseShare} of the {stats.SurpriseSlots} slots drawn as one; stackable results {stackableShare} of {stats.StackableSurpriseSlots}; " +
					$"{recipeShare} of the rebuilt recipes with one), {stats.UnfitKeys} keys not fitting progression for lack of another; use caps {string.Join("/", useCaps)}; most used: {busiest}");
				// The upgrades' shells whatever the setting (they are the upgrades' recipes): they shift no choice above
				PlanShells(SpecialShells(), true);
			}

			// The other shells (RecipesForUncraftable), after the locks: nothing else is planned around them. Then the candidates
			// are final
			public void PlanOtherShells(bool shells)
			{
				if (shells) PlanShells(EligibleShells(), false);
				result.CandidateTiers = (int[])clean.Clone();
			}

			// The ingredients of a recipe no choice can change: all of them when it is not rebuilt, its protected ones otherwise
			private IEnumerable<(int type, int stack)> FixedIngredients(int i)
			{
				IEnumerable<(int type, int stack)> fixedOnes = RecipeSystem.OriginalIngredients(i);
				return targets[i] >= 0 ? fixedOnes.Where(p => !IngredientReplaceable(p.type)) : fixedOnes;
			}

			// The two recipes of a swap are planned together, when the later one is reached (Run): when either can't be rebuilt
			// for the item it gives (no key of its tier, no candidate), everything done for the pair is taken back (the candidates
			// exactly as they were) and the swap is cancelled, so no swapped recipe is ever keyless or left with the ingredients
			// of another item. Returns whether the pair was planned (false: both are planned alone now)
			private bool PlanPair(int i, int j)
			{
				Counters counters = stats;
				Dictionary<int, int> usesBefore = new(uses), keyUsesBefore = new(keyUses);
				SortedSet<int> noKeyBefore = new(noKey);
				int item = results[i];
				(int real, int clean, int source, int index) itemBefore = item > 0 && item < real.Length ? (real[item], clean[item], source[item], PoolIndex(item, clean[item])) : (Never, Never, -1, -1);
				Snapshot snapshot = settings.CheckRestore ? TakeSnapshot() : null;
				graph.Mark();
				if (Rebuild(i))
				{
					Made(i);
					if (Rebuild(j))
					{
						graph.Undo(keep: true);
						result.PlanOrder[i] = position++;
						result.PlanOrder[j] = position++;
						Made(j);
						return true;
					}
				}
				graph.Undo();
				stats = counters;
				uses = usesBefore;
				keyUses = keyUsesBefore;
				noKey.Clear();
				noKey.UnionWith(noKeyBefore);
				RestoreItem(item, itemBefore);
				foreach (int r in new[] { i, j })
				{
					result.Ingredients[r] = null;
					result.Groups[r] = null;
					result.IngredientValues[r] = 0;
					result.IngredientTiers[r] = -1;
					result.PlannedKeys[r] = 0;
					result.ValueShort[r] = false;
				}
				if (snapshot != null && !snapshot.Matches(this)) result.RestoreMismatches++;
				CancelSwap(i, j);
				return false;
			}

			// Where an item is in its candidate list (-1: none), so a cancel puts it back at the same place: the picks walk the
			// lists in order, a cancelled pair must leave them as if it never was
			private int PoolIndex(int item, int tier)
			{
				if (tier >= Tiers.Count || !Eligible(item, stationItems)) return -1;
				return pool[(int)ItemPool.Info[item].Category, tier].IndexOf(item);
			}

			// Puts back what Made gave an item (when it is had, and as which candidate, at its place in the list)
			private void RestoreItem(int item, (int real, int clean, int source, int index) before)
			{
				if (item <= 0 || item >= real.Length) return;
				real[item] = before.real;
				if (clean[item] != before.clean && Eligible(item, stationItems))
				{
					int category = (int)ItemPool.Info[item].Category;
					if (clean[item] < Tiers.Count) pool[category, clean[item]].Remove(item);
					if (before.clean < Tiers.Count)
					{
						List<int> list = pool[category, before.clean];
						list.Insert(before.index >= 0 && before.index <= list.Count ? before.index : list.Count, item);
					}
				}
				clean[item] = before.clean;
				source[item] = before.source;
			}

			// Tests: the candidates and when each item is had, to check that a cancelled pair leaves them as they were
			private sealed class Snapshot
			{
				private readonly int[] real, clean, source;
				private readonly int[][] pool;

				public Snapshot(Rebuilder r)
				{
					real = (int[])r.real.Clone();
					clean = (int[])r.clean.Clone();
					source = (int[])r.source.Clone();
					pool = r.pool.Cast<List<int>>().Select(l => l.ToArray()).ToArray();
				}

				public bool Matches(Rebuilder r) => real.SequenceEqual(r.real) && clean.SequenceEqual(r.clean) && source.SequenceEqual(r.source)
					&& pool.Zip(r.pool.Cast<List<int>>(), (a, b) => a.SequenceEqual(b)).All(x => x);
			}

			private Snapshot TakeSnapshot() => new(this);

			// Both recipes give their own vanilla result again, planned for it (their tier, their U)
			private void CancelSwap(int i, int j)
			{
				foreach (int r in new[] { i, j })
				{
					result.Results[r] = (RecipeSystem.OriginalResult(r), RecipeSystem.OriginalStack(r));
					results[r] = result.Results[r].type;
					result.SwapPartner[r] = -1;
					result.ResultTiers[r] = ResultTier(r, results[r]);
				}
				// Their kept ingredients already lead to their own results (Run)
				foreach (int r in new[] { i, j }) targets[r] = TargetTier(r);
				cancelled++;
			}

			// A recipe that could not be rebuilt keeps its vanilla ingredients, unless one of them is made from its result through
			// the plan (a loop the recipes as set up don't have: Wood <-> Wood Platform stays as it is): those are replaced by
			// materials had by T that the result does not lead to (within the use caps), the stack keeping the value (a group slot
			// drops its group). A protected one, or one with no replacement, switches the recipe off (RecipePlan.LoopOff) when
			// its result is still had otherwise; else the recipe stays vanilla, loop and all (a warning: never an item lost)
			private void KeepOriginals(int i)
			{
				int item = results[i];
				HashSet<int> below = graph.From(item);
				HashSet<int> vanillaBelow = OriginalGraph().From(item);
				(int type, int stack)[] current = ((int, int)[])RecipeSystem.OriginalIngredients(i).Clone();
				List<int> recipeGroups = RecipeSystem.OriginalGroups(i).ToList();
				IEnumerable<int> Members(int type)
				{
					int group = GroupOf(recipeGroups, type);
					return group < 0 ? new[] { type } : (IEnumerable<int>)RecipeGroup.recipeGroups[group].ValidItems;
				}
				List<int> looping = Enumerable.Range(0, current.Length).Where(k => Members(current[k].type).Any(below.Contains) && !Members(current[k].type).Any(vanillaBelow.Contains)).ToList();
				if (looping.Count == 0) return;
				Random rng = DetRandom.Get(RollKey.RecipeIngredient, RecipeSystem.RecipeKey(i), RollKey.Fallback);
				int top = Math.Max(targets[i], 0);
				List<int> picked = new();
				foreach (int k in looping)
				{
					(int type, int stack) = current[k];
					int replacement = 0;
					if (IngredientReplaceable(type))
					{
						// A material of the tier the vanilla one was had at first, else any tier up to T
						int wanted = type < vanillaAvail.Length && vanillaAvail[type] < Never ? Math.Min(vanillaAvail[type], top) : top;
						for (int pass = 0; pass < 2 && replacement == 0; pass++)
						{
							List<(int item, float weight)> options = new();
							for (int c = 0; c < ItemCategories.Count; c++)
							{
								if (!MaterialLike((ItemCategory)c)) continue;
								for (int t = pass == 0 ? wanted : 0; t <= (pass == 0 ? wanted : top); t++)
								{
									foreach (int x in pool[c, t])
									{
										if (x == item || below.Contains(x) || current.Any(p => p.type == x) || RecipeSystem.InGroups(recipeGroups, x) || uses.GetValueOrDefault(x) >= useCaps[t]) continue;
										options.Add((x, CommonnessWeight(x) * IngredientPenalty(x, x)));
									}
								}
							}
							replacement = WeightedPick(options, rng);
						}
					}
					if (replacement == 0)
					{
						if (StillHad(i, item))
						{
							result.Twin[i] = result.LoopOff[i] = true;
							switchedOff++;
							ModContent.GetInstance<UltimateRandomizer>().Logger.Warn($"Recipe plan: the recipe of {DetRandom.ItemName(item)} would loop through {DetRandom.ItemName(type)}: switched off (the item is had otherwise)");
						}
						else
						{
							keptLooping++;
							ModContent.GetInstance<UltimateRandomizer>().Logger.Warn($"Recipe plan: the recipe of {DetRandom.ItemName(item)} loops through {DetRandom.ItemName(type)}, but it is the item's only source: kept vanilla");
						}
						return;
					}
					int group = GroupOf(recipeGroups, type);
					if (group >= 0) recipeGroups.Remove(group);
					current[k] = (replacement, ReplacementStack(UnitValue(type) * stack, replacement));
					picked.Add(replacement);
				}
				foreach (int x in picked) uses[x] = uses.GetValueOrDefault(x) + 1;
				result.Ingredients[i] = current;
				result.Groups[i] = recipeGroups.SequenceEqual(RecipeSystem.OriginalGroups(i)) ? null : recipeGroups.ToArray();
				result.IngredientValues[i] = current.Sum(p => UnitValue(p.type) * p.stack);
				// Its tier, for the tests and the decraft rule (shimmering an item had earlier into these ingredients)
				result.IngredientTiers[i] = top;
				result.Repaired[i] = true;
				repaired++;
			}

			// Is an item still had without this recipe: found, or given by another recipe left on?
			private bool StillHad(int i, int item)
			{
				if (Findability.Tier(item) < Never) return true;
				for (int j = 0; j < Count; j++)
				{
					if (j != i && results[j] == item && !result.Twin[j] && !RecipeSystem.VanillaDisabled(j) && (!RecipeSystem.IsShell(j) || ShellActive(result, j))) return true;
				}
				return false;
			}

			private ItemGraph OriginalGraph()
			{
				if (originalGraph != null) return originalGraph;
				originalGraph = new ItemGraph();
				for (int r = 0; r < Count; r++)
				{
					if (RecipeSystem.IsShell(r) || RecipeSystem.VanillaDisabled(r)) continue;
					originalGraph.AddRecipe(RecipeSystem.OriginalIngredients(r), RecipeSystem.OriginalGroups(r), RecipeSystem.OriginalResult(r));
				}
				return originalGraph;
			}

			// A key had exactly at T when its recipe was planned may be made earlier by a recipe planned after it (one planned
			// alone after a cancelled pair, one on early ingredients): such a recipe takes another key had exactly at T (T + 1
			// when none: later rather than too early); a few rounds, a new key may have been made earlier too
			private void RecheckKeys()
			{
				HashSet<int> failed = new();
				for (int round = 0; round < 4; round++)
				{
					int changed = 0;
					for (int i = 0; i < Count; i++)
					{
						int key = result.PlannedKeys[i];
						int t = result.IngredientTiers[i];
						if (key <= 0 || t <= 0 || RecipeSystem.IsShell(i) || result.Ingredients[i] == null || key >= real.Length || real[key] >= t) continue;
						(int type, int stack)[] current = ((int, int)[])result.Ingredients[i].Clone();
						int[] recipeGroups = GroupsOf(result, i);
						// Still not doable before T by another ingredient: nothing to do
						if (IngredientTier(current.Where(p => p.type != key).ToArray(), recipeGroups, real) >= t) continue;
						int slot = Array.FindIndex(current, p => p.type == key);
						if (slot < 0) continue;
						HashSet<int> below = graph.From(results[i]);
						int originalResult = RecipeSystem.OriginalResult(i);
						bool Usable(int x) => x != results[i] && x != originalResult && !current.Any(p => p.type == x) && !below.Contains(x) && !RecipeSystem.InGroups(recipeGroups, x);
						Random rng = DetRandom.Get(RollKey.RecipeIngredient, RecipeSystem.RecipeKey(i), RollKey.KeyIngredient);
						long others = current.Where((p, k) => k != slot).Sum(p => UnitValue(p.type) * p.stack);
						long budget = Math.Max(1, (long)(RecipeSystem.OriginalIngredientValue(Proxy(i)) * MaxValueFactor) + Item.silver - others);
						int newKey = PickKey(t, null, Usable, rng, x => Fits(x, results[i], t), budget);
						int newTier = t;
						if (newKey == 0 && t < Tiers.Max)
						{
							newKey = PickKey(t + 1, null, Usable, rng, x => Fits(x, results[i], t + 1), budget);
							newTier = t + 1;
						}
						if (newKey == 0)
						{
							failed.Add(i);
							continue;
						}
						current[slot] = (newKey, ReplacementStack(UnitValue(key) * current[slot].stack, newKey));
						keyUses[key] = Math.Max(0, keyUses.GetValueOrDefault(key) - 1);
						keyUses[newKey] = keyUses.GetValueOrDefault(newKey) + 1;
						graph.Add(newKey, results[i]);
						result.Ingredients[i] = current;
						result.IngredientValues[i] = current.Sum(p => UnitValue(p.type) * p.stack);
						result.PlannedKeys[i] = newKey;
						result.IngredientTiers[i] = newTier;
						// A cheaper key (held by its stack cap) may leave the recipe short of its value: richer materials of its tier
						long vanilla = RecipeSystem.OriginalIngredientValue(Proxy(i));
						long want = (long)Math.Ceiling(vanilla * MinValueFactor);
						if (result.IngredientValues[i] < want)
						{
							long most = (long)(vanilla * MaxValueFactor) + Item.silver;
							int keyTier = newTier;
							Raise(i, want, newKey, keyTier, Usable, x => Fits(x, results[i], keyTier), x => UnitValue(x) <= most);
							graph.AddRecipe(result.Ingredients[i], recipeGroups, results[i]);
						}
						// Short of its value (or no longer) with the new key
						result.ValueShort[i] = result.IngredientValues[i] < want;
						failed.Remove(i);
						rekeyed++;
						changed++;
					}
					if (changed == 0) break;
				}
				rekeyFailed = failed.Count;
			}

			// The shells of the upgrades no recipe makes, from the upgrade's tier
			private List<int> SpecialShells()
			{
				List<int> list = new();
				foreach (int item in Protection.Upgrades)
				{
					int i = SpecialRecipe(item);
					if (i < 0 || !RecipeSystem.IsShell(i) || results[i] != item) continue;
					result.ShellTiers[i] = targets[i] = Protection.UpgradeTier(item);
					list.Add(i);
				}
				return list;
			}

			// The shells of the items no recipe makes (ShellEligible), from their tier
			private List<int> EligibleShells()
			{
				HashSet<int> stationTiles = Stations.RequiredTiles();
				HashSet<int> guaranteed = GuaranteedSources.Requirements.Select(r => r.Item).ToHashSet();
				List<int> list = new();
				for (int i = 0; i < Count; i++)
				{
					if (!RecipeSystem.IsShell(i) || !ShellEligible(results[i], stationTiles, guaranteed)) continue;
					// Never before another recipe makes the item: a randomized result that is never found may be the key of the
					// recipes of its tier, and a shell from its own tier would unlock them all early
					int made = real[results[i]];
					result.ShellTiers[i] = targets[i] = made < Tiers.Count ? Math.Max(ShellTier(results[i]), made) : ShellTier(results[i]);
					list.Add(i);
				}
				return list;
			}

			// Shells, after every other recipe (their choices and use counts stay as they were), by tier then item: each one gets
			// a station had by its tier T and ingredients rebuilt like a recipe of tier T with no vanilla shape
			// (ShellIngredientCount, the ingredient weights, about the item's value), its key found exactly at T. A shell of tier
			// T > 0 without a key would be doable earlier: it stays off, like one without any ingredient (an upgrade's shell keeps
			// its station: LockSpecials adds its key)
			private void PlanShells(List<int> shellList, bool special)
			{
				List<int> stations = Stations.RequiredTiles().Where(Assignable)
					.OrderBy(t => Stations.Info(t).Kind == StationKind.Basic ? 0 : 1).ThenBy(t => DetRandom.StableHash(Stations.Name(t))).ToList();
				Dictionary<ItemCategory, (int eligible, int planned)> byCategory = new();
				int planned = 0, noStation = 0;
				foreach (int i in shellList.OrderBy(i => targets[i]).ThenBy(i => (uint)DetRandom.ItemKey(results[i])).ThenBy(i => i))
				{
					ItemCategory category = ItemPool.Info[results[i]].Category;
					(int eligible, int done) = byCategory.GetValueOrDefault(category);
					eligible++;
					result.PlanOrder[i] = position++;
					int top = targets[i];
					// Any basic or progression station had by T (a progression one not above the item's own level)
					Random rng = DetRandom.Get(RollKey.Uncraftable, DetRandom.ItemKey(results[i]), RollKey.Station);
					List<int> allowed = stations.Where(t => TileAvailability(t, placers, real) <= top
						&& (Stations.Info(t).Kind != StationKind.Progression || Stations.Info(t).Tier <= RecipeTier(i))).ToList();
					if (allowed.Count == 0) noStation++;
					else result.Tiles[i] = new[] { allowed[rng.Next(allowed.Count)] };
					if (Rebuild(i))
					{
						planned++;
						done++;
						Made(i);
					}
					else if (!special) DropShell(result, i);
					byCategory[category] = (eligible, done);
				}
				ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Recipe plan: {planned} of {shellList.Count} shell recipes planned for {(special ? "the upgrades" : "items no recipe makes")}" +
					$" ({string.Join(", ", byCategory.OrderBy(p => p.Key).Select(p => $"{p.Key} {p.Value.planned}/{p.Value.eligible}"))}); {noStation} without a station");
			}

			// Availability of a recipe's ingredients in an availability array (a group slot: its best member)
			private int IngredientTier((int type, int stack)[] ingredients, int[] recipeGroups, int[] avail)
			{
				int tier = 0;
				foreach ((int type, _) in ingredients)
				{
					int a = Never;
					int group = GroupOf(recipeGroups, type);
					if (group >= 0)
					{
						foreach (int v in RecipeGroup.recipeGroups[group].ValidItems)
						{
							if (v > 0 && v < avail.Length) a = Math.Min(a, avail[v]);
						}
					}
					else if (type > 0 && type < avail.Length) a = avail[type];
					tier = Math.Max(tier, a);
					if (tier >= Never) return Never;
				}
				return tier;
			}

			// Once a recipe is planned (or kept), its result is had from the tier it is doable at (U for a locked result), and its
			// edges join the graph
			private void Made(int i)
			{
				int item = results[i];
				if (item <= 0 || item >= real.Length) return;
				(int type, int stack)[] ingredients = result.Ingredients[i] ?? RecipeSystem.OriginalIngredients(i);
				int[] recipeGroups = GroupsOf(result, i);
				graph.AddRecipe(ingredients, recipeGroups, item);
				int station = 0;
				foreach (int tile in result.Tiles[i] ?? tiles[i]) station = Math.Max(station, TileAvailability(tile, placers, real));
				if (station >= Never) return;
				int locked = VanillaSource(result, i) ? 0 : result.ResultTiers[i];
				int r = IngredientTier(ingredients, recipeGroups, real);
				if (r < Never) real[item] = Math.Min(real[item], Math.Max(Math.Max(r, station), locked));
				int c = IngredientTier(ingredients, recipeGroups, clean);
				if (c >= Never || Tainted(item)) return;
				int tier = Math.Max(Math.Max(c, station), locked);
				if (tier >= clean[item]) return;
				int old = clean[item];
				clean[item] = tier;
				source[item] = i;
				if (!Eligible(item, stationItems)) return;
				int category = (int)ItemPool.Info[item].Category;
				if (old < Tiers.Count) pool[category, old].Remove(item);
				if (tier < Tiers.Count) pool[category, tier].Add(item);
			}

			private int GroupTier(RecipeGroup group)
			{
				int best = Never;
				foreach (int v in group.ValidItems)
				{
					if (v > 0 && v < clean.Length) best = Math.Min(best, clean[v]);
				}
				return best;
			}

			// Share of the replaceable ingredients of a shape in category c
			private static double ShapeShare(List<int> shape, int c) => shape.Count == 0 ? 0 : shape.Count(g => (int)ItemPool.Info[g].Category == c) / (double)shape.Count;

			// How well a unit value suits a slot worth share: 1 when a stack within the caps can be worth it, less (gently: the
			// square root, never below MinCloseness) for an item worth more than the whole share (a bar for a cheap slot) or that
			// can't reach it (arrows for a bar's worth). Soft, so a cheap recipe is not all Gel and plain blocks
			private double Closeness(int x, double share)
			{
				if (share <= 0) return 1;
				double unit = UnitValue(x);
				double most = unit * Math.Min(StackRules.MaxStackOf(x), cfg.IngredientStackCap);
				if (unit > share) return Math.Max(MinCloseness, Math.Sqrt(share / unit));
				if (most < share) return Math.Max(MinCloseness, Math.Sqrt(most / share));
				return 1;
			}

			// A shell (no vanilla recipe) has no shape: ShellIngredientCount ingredients give or take the spread, the ingredient
			// weights alone, tiers pulled towards T; its own stream (RollKey.Uncraftable), so no other recipe's rolls move.
			// Returns whether the recipe got ingredients (false: it stays as it is)
			private bool Rebuild(int i)
			{
				bool isShell = RecipeSystem.IsShell(i);
				int proxy = Proxy(i);
				bool isSwapped = proxy != i;
				Random rng = isShell ? DetRandom.Get(RollKey.Uncraftable, DetRandom.ItemKey(results[i])) : DetRandom.Get(RollKey.RecipeIngredient, RecipeSystem.RecipeKey(i));
				// Its own stream: the surprises never shift the other rolls of the recipe
				Random surpriseRng = isShell ? DetRandom.Get(RollKey.Uncraftable, DetRandom.ItemKey(results[i]), RollKey.Surprise)
					: DetRandom.Get(RollKey.RecipeIngredient, RecipeSystem.RecipeKey(i), RollKey.Surprise);
				int top = targets[i];
				bool house = RecipeSystem.HouseRecipe(i);
				int result0 = results[i];
				int originalResult = RecipeSystem.OriginalResult(i);
				(int type, int stack)[] original = RecipeSystem.OriginalIngredients(i);
				int[] originalGroups = RecipeSystem.OriginalGroups(i);
				HashSet<int> originalTypes = original.Select(o => o.type).ToHashSet();

				// Protected ingredients (and their groups) stay; the others give the shape (a swapped recipe: mostly its own, blended
				// with the shape of the item's vanilla recipe, which gives the tiers followed) and the count
				List<(int type, int stack)> kept = new();
				List<int> chosenGroups = new();
				List<int> shape = new();
				foreach ((int g, int n) in original)
				{
					if (IngredientReplaceable(g))
					{
						shape.Add(g);
						continue;
					}
					kept.Add((g, n));
					int group = GroupOf(originalGroups, g);
					if (group >= 0 && !chosenGroups.Contains(group)) chosenGroups.Add(group);
				}
				(int type, int stack)[] proxyOriginal = RecipeSystem.OriginalIngredients(proxy);
				List<int> itemShape = isSwapped ? proxyOriginal.Select(o => o.type).Where(IngredientReplaceable).ToList() : shape;
				int n0 = Math.Clamp((isShell ? ShellIngredientCount : original.Length) + CountDelta(rng, cfg.IngredientCountSpread), 1, Math.Max(1, cfg.IngredientMax));
				int slots = Math.Max(1, n0 - kept.Count);

				// Value: the vanilla value times a random factor, the kept ingredients' value first, the rest split unevenly; never
				// more than MaxValueFactor times the vanilla value in all
				long vanillaValue = RecipeSystem.OriginalIngredientValue(proxy);
				double target = vanillaValue * (MinValueFactor + rng.NextDouble() * ValueFactorRange);
				long keptValue = kept.Sum(p => UnitValue(p.type) * p.stack);
				double remaining = Math.Max(0, target - keptValue);
				double[] shares = Enumerable.Range(0, slots).Select(_ => 0.5 + rng.NextDouble()).ToArray();
				double shareSum = shares.Sum();
				long maxValue = (long)(vanillaValue * MaxValueFactor) + Item.silver;
				// Above what the whole recipe may be worth, an item is no candidate (a house is built of anything found at once)
				bool Affordable(int x) => house || UnitValue(x) <= Math.Max(1, maxValue - keptValue);

				// What is had by T, by category and tier (items and groups)
				int[] groupTiers = groups.Select(g => GroupTier(g.group)).ToArray();
				int cats = ItemCategories.Count;
				bool[,] has = new bool[cats, top + 1];
				bool[] hasCategory = new bool[cats];
				for (int c = 0; c < cats; c++)
				{
					if (house && !MaterialLike((ItemCategory)c)) continue;
					for (int t = 0; t <= top; t++)
					{
						if (pool[c, t].Count > 0) has[c, t] = hasCategory[c] = true;
					}
				}
				if (!house)
				{
					for (int k = 0; k < groups.Count; k++)
					{
						if (groupTiers[k] <= top) has[(int)groups[k].category, groupTiers[k]] = hasCategory[(int)groups[k].category] = true;
					}
				}
				// Category weights: the ingredient weights, plus the vanilla shape (a swap: SwapHostShape of the host's, the rest the
				// item's, so the swap shows)
				double[] catWeight = new double[cats];
				double baseTotal = 0;
				for (int c = 0; c < cats; c++)
				{
					if (hasCategory[c]) baseTotal += weights.Get((ItemCategory)c);
				}
				double bonus = baseTotal > 0 ? ShapeBonus * baseTotal : 1;
				for (int c = 0; c < cats; c++)
				{
					if (!hasCategory[c]) continue;
					catWeight[c] = weights.Get((ItemCategory)c);
					if (house) continue;
					double share = !isSwapped || itemShape.Count == 0 ? ShapeShare(shape, c) : shape.Count == 0 ? ShapeShare(itemShape, c)
						: (1 - SwapHostShape) * ShapeShare(itemShape, c) + SwapHostShape * ShapeShare(shape, c);
					catWeight[c] += bonus * share;
				}
				List<(int item, float weight)> categories = Enumerable.Range(0, cats).Where(c => catWeight[c] > 0).Select(c => (c, (float)catWeight[c])).ToList();
				// A stackable result (a bar, a block, ammo) never eats a piece of gear per unit (an Obsidian Pants per Hellstone Bar)
				bool stackableResult = StackRules.MaxStackOf(result0) > 1;
				// Surprise slots draw their category with the loot weights (no bag, no summon: never ingredients); for a stackable
				// result among the stackable categories only (a gear surprise found no candidate, and most surprises were lost)
				List<(int item, float weight)> surpriseCategories = house ? new List<(int, float)>() : Enumerable.Range(0, cats)
					.Where(c => hasCategory[c] && !Unusable.Contains((ItemCategory)c) && lootWeights.Get((ItemCategory)c) > 0
						&& (!stackableResult || StackableCategories.Contains((ItemCategory)c)))
					.Select(c => (c, (float)lootWeights.Get((ItemCategory)c))).ToList();
				// A recipe keeping its result keeps a trace of its shape: when the vanilla one has non-material ingredients (the
				// Zenith's swords), its first slot after the key tries those categories first (the most frequent first)
				List<int> shapeFirst = isSwapped || house || isShell ? new List<int>() : shape.Select(g => (int)ItemPool.Info[g].Category)
					.Where(c => !MaterialLike((ItemCategory)c) && hasCategory[c]).GroupBy(c => c).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => g.Key).ToList();

				// Never the result, nor anything the result already leads to (a loop through other recipes)
				HashSet<int> below = graph.From(result0);
				HashSet<int> taken = kept.Select(p => p.type).ToHashSet();
				bool Usable(int x) => x != result0 && x != originalResult && !taken.Contains(x) && !below.Contains(x) && !RecipeSystem.InGroups(chosenGroups, x)
					&& (!house || (Findability.Tier(x) == 0 && MaterialLike(ItemPool.Info[x].Category) && !originalTypes.Contains(x)));
				bool Stackable(int x) => !stackableResult || StackRules.MaxStackOf(x) > 1;
				// Progression (Fits) for every new ingredient: required of the slots, preferred for the key
				bool FitsHere(int x) => Fits(x, result0, top) && Stackable(x);
				bool UsableGroup(int k)
				{
					RecipeGroup group = groups[k].group;
					if (chosenGroups.Contains(groups[k].id) || group.ContainsItem(result0) || group.ContainsItem(originalResult) || taken.Any(group.ContainsItem)) return false;
					foreach (int other in chosenGroups)
					{
						if (RecipeGroup.recipeGroups.TryGetValue(other, out RecipeGroup o) && o.ValidItems.Overlaps(group.ValidItems)) return false;
					}
					// Like a single item: no member may be made from this result (a loop)
					return !group.ValidItems.Overlaps(below);
				}

				List<int> planned = new();
				void Add(int type)
				{
					taken.Add(type);
					planned.Add(type);
				}

				// The key: a material found exactly at T (had no earlier), so the recipe is doable from T, not before. Without one
				// of the drawn kinds: any kind, then the vanilla ingredient had exactly at T; else a vanilla recipe stays vanilla,
				// a swapped one takes a key of T + 1 (later rather than too early), else fails (PlanPair cancels its swap)
				int key = 0;
				// The tier the recipe ends up at (T, or T + 1 with a later key)
				int keyTier = top;
				if (top > 0 && !house)
				{
					long keyBudget = Math.Max(1, maxValue - keptValue);
					key = PickKey(top, catWeight, Usable, rng, FitsHere, keyBudget);
					if (key == 0) key = PickKey(top, null, Usable, rng, FitsHere, keyBudget);
					if (key == 0) key = VanillaKey(original.Concat(proxyOriginal), top, Usable);
					if (key == 0 && isSwapped && top < Tiers.Max)
					{
						key = PickKey(top + 1, null, Usable, rng, x => Fits(x, result0, top + 1) && Stackable(x), keyBudget);
						if (key > 0) keyTier = top + 1;
					}
				}
				if (key > 0)
				{
					Add(key);
					keyUses[key] = keyUses.GetValueOrDefault(key) + 1;
					result.PlannedKeys[i] = key;
					if (!isShell) stats.Keyed++;
				}
				else if (top > 0 && !house)
				{
					// Without a key, a shell would be doable before the tier its item is found at, a recipe before its tier
					if (isShell) return false;
					noKey.Add(top);
					if (!isSwapped) stats.VanillaKept++;
					return false;
				}

				// About what the slots filled so far are worth (their stack for their share): a surprise must fit in what is left of
				// the recipe's cap
				double spent = 0;
				int firstSlot = planned.Count;
				bool anySurprise = false;
				if (key > 0) spent += UnitValue(key) * ReplacementStack((long)Math.Ceiling(remaining * shares[0] / shareSum), key);
				for (int slot = planned.Count; slot < slots; slot++)
				{
					double slotShare = remaining * shares[slot] / shareSum;
					// Drawn for every slot (two draws), so the stream never depends on the chance
					bool surprise = surpriseRng.NextDouble() * 100 < settings.SurpriseChance;
					int surpriseCategory = surpriseCategories.Count > 0 ? WeightedPick(surpriseCategories, surpriseRng) : -1;
					// The shape's slot is no surprise
					bool shaped = slot == firstSlot && shapeFirst.Count > 0;
					surprise &= surpriseCategory >= 0 && !shaped;
					if (surprise && !isShell)
					{
						stats.SurpriseSlots++;
						if (stackableResult) stats.StackableSurpriseSlots++;
					}
					double room = maxValue - keptValue - spent;
					int picked = 0;
					bool wasSurprise = false;
					for (int attempt = 0; attempt < 12 && picked == 0 && categories.Count > 0; attempt++)
					{
						int c = WeightedPick(categories, rng);
						// The shape's slot: its categories first, in turn
						if (shaped && attempt < ShapeAttempts) c = shapeFirst[attempt % shapeFirst.Count];
						// A surprise slot tries its loot category first, at any value the recipe's cap still allows
						bool surprised = surprise && attempt == 0;
						if (surprised) c = surpriseCategory;
						// The tier follows one of the vanilla ingredients, blurred, pulled towards T (a shell: T itself)
						int baseTier = top;
						if (itemShape.Count > 0)
						{
							int follow = itemShape[rng.Next(itemShape.Count)];
							if (follow < vanillaAvail.Length && vanillaAvail[follow] < Never) baseTier = Math.Min(vanillaAvail[follow], top);
						}
						List<(int item, float weight)> tiers = new();
						for (int t = 0; t <= top; t++)
						{
							if (!has[c, t]) continue;
							int distance = Math.Abs(t - baseTier);
							tiers.Add((t, (distance < BlurWeights.Length ? BlurWeights[distance] : 0f) + (t == top ? TopTierWeight : t == top - 1 ? BelowTopWeight : 0f) + AnyTierWeight));
						}
						if (tiers.Count == 0) continue;
						int tier = WeightedPick(tiers, rng);
						int cap = useCaps[tier];
						List<(int item, float weight)> options = new();
						foreach (int x in pool[c, tier])
						{
							if (!Usable(x) || uses.GetValueOrDefault(x) >= cap || !Affordable(x) || !FitsHere(x)) continue;
							if (surprised && UnitValue(x) > room) continue;
							options.Add((x, (float)(CommonnessWeight(x) * IngredientPenalty(x, x) * Closeness(x, slotShare))));
						}
						if (!house)
						{
							for (int k = 0; k < groups.Count; k++)
							{
								if ((int)groups[k].category != c || groupTiers[k] != tier || uses.GetValueOrDefault(-(groups[k].id + 1)) >= cap || !UsableGroup(k)) continue;
								int iconic = groups[k].group.IconicItemId;
								if (!Affordable(iconic) || (surprised && UnitValue(iconic) > room) || !FitsHere(iconic)) continue;
								options.Add((-(k + 1), (float)(CommonnessWeight(iconic) * IngredientPenalty(-(groups[k].id + 1), iconic) * Closeness(iconic, slotShare))));
							}
						}
						picked = WeightedPick(options, rng);
						wasSurprise = surprised && picked != 0;
					}
					// A house is built at once: any material found at tier 0 then, whatever its uses
					if (picked == 0 && house) picked = HouseMaterial(Usable, rng);
					if (picked == 0) continue;
					if (wasSurprise && !isShell)
					{
						stats.Surprises++;
						if (stackableResult) stats.StackableSurprises++;
						anySurprise = true;
					}
					int added = picked < 0 ? groups[-picked - 1].group.IconicItemId : picked;
					spent += UnitValue(added) * ReplacementStack((long)Math.Ceiling(slotShare), added);
					if (picked < 0)
					{
						(int id, RecipeGroup group, _) = groups[-picked - 1];
						chosenGroups.Add(id);
						Add(group.IconicItemId);
						if (!house) uses[-(id + 1)] = uses.GetValueOrDefault(-(id + 1)) + 1;
						if (!isShell) stats.GroupSlots++;
						continue;
					}
					Add(picked);
					if (!house) uses[picked] = uses.GetValueOrDefault(picked) + 1;
					if (source[picked] >= 0 && !isShell) stats.Crafted++;
				}
				if (planned.Count == 0)
				{
					if (!isShell && !isSwapped) stats.VanillaKept++;
					return false;
				}
				if (!isShell)
				{
					stats.Rebuilt++;
					if (anySurprise) stats.SurpriseRecipes++;
					stats.SlotsTotal += planned.Count;
					if (isSwapped) stats.Swapped++;
				}
				// The value goes to the slots filled (a slot left empty gives its share to the others)
				double shareTotal = shares.Take(planned.Count).Sum();
				(int type, int stack)[] ingredients = planned.Select((type, k) => (type, ReplacementStack((long)Math.Ceiling(remaining * shares[k] / shareTotal), type)))
					.Concat(kept).ToArray();
				if (!house) CapValue(ingredients, planned.Count, maxValue);
				int[] plannedGroups = chosenGroups.ToArray();
				result.Ingredients[i] = ingredients;
				result.Groups[i] = plannedGroups.SequenceEqual(originalGroups) ? null : plannedGroups;
				result.IngredientValues[i] = ingredients.Sum(p => UnitValue(p.type) * p.stack);
				result.IngredientTiers[i] = keyTier;
				// Worth at least MinValueFactor of the vanilla recipe (30 walls are no Molten Fury); with the value cap, the result is
				// weighed against the vanilla ingredients' value: the new ones are worth at least that (a shell's item is worth
				// vanillaValue: it still sells for its ingredients only, CraftedValueFeature)
				if (!house)
				{
					long want = (long)Math.Ceiling(vanillaValue * MinValueFactor);
					if (cfg.LimitRecipeValue && !isShell && (long)StackRules.ValueOf(result.Results[i].type) * result.Results[i].stack > RecipeSystem.MaxResultValue(proxy, result.IngredientValues[i], cfg.RecipeValueGuard)) want = vanillaValue;
					if (cfg.LimitRecipeValue && isShell && vanillaValue > (long)(result.IngredientValues[i] * cfg.RecipeValueGuard) + Item.silver) want = vanillaValue;
					if (result.IngredientValues[i] < want) Raise(i, want, key, top, Usable, FitsHere, Affordable);
				}
				return true;
			}

			// RaiseValue within the rebuilder: the richer materials are the slots' candidates (had by T, usable, within the use
			// caps and the recipe's value cap), and the uses follow the ingredients replaced
			private void Raise(int i, long want, int key, int top, Func<int, bool> usable, Func<int, bool> fits, Func<int, bool> affordable)
			{
				List<int> Richer(bool capped)
				{
					List<int> list = new();
					for (int c = 0; c < ItemCategories.Count; c++)
					{
						if (!MaterialLike((ItemCategory)c)) continue;
						for (int t = 0; t <= top; t++)
						{
							list.AddRange(pool[c, t].Where(x => usable(x) && fits(x) && affordable(x) && (!capped || uses.GetValueOrDefault(x) < useCaps[t])));
						}
					}
					return list;
				}
				HashSet<int> before = result.Ingredients[i].Select(p => p.type).ToHashSet();
				bool shellRecipe = RecipeSystem.IsShell(i);
				int stream = shellRecipe ? RollKey.Uncraftable : RollKey.RecipeIngredient;
				int recipeKey = shellRecipe ? DetRandom.ItemKey(results[i]) : RecipeSystem.RecipeKey(i);
				Random rng = DetRandom.Get(stream, recipeKey, RollKey.Special);
				// Still short (every free slot taken by a richer material, the key alone, the use caps reached): past the use caps,
				// and a new ingredient while the recipe has room (its vanilla count plus the spread). Only the recipes the first
				// pass left short change, on their own stream
				if (!RaiseValue(result, i, want, key, Richer(true), rng)
					&& !RaiseValue(result, i, want, key, Richer(false), DetRandom.Get(stream, recipeKey, RollKey.Special, RollKey.Fallback),
						append: true, appendMax: RecipeSystem.OriginalIngredients(i).Length + cfg.IngredientCountSpread))
				{
					result.ValueShort[i] = true;
				}
				HashSet<int> after = result.Ingredients[i].Select(p => p.type).ToHashSet();
				foreach (int x in before.Except(after)) uses[x] = Math.Max(0, uses.GetValueOrDefault(x) - 1);
				foreach (int x in after.Except(before)) uses[x] = uses.GetValueOrDefault(x) + 1;
			}

			// The new ingredients (the first planned ones) lose stacks, the most valuable first, until the recipe is worth at most
			// max (a stack never goes below 1)
			private static void CapValue((int type, int stack)[] ingredients, int planned, long max)
			{
				long value = ingredients.Sum(p => UnitValue(p.type) * p.stack);
				foreach (int k in Enumerable.Range(0, planned).OrderByDescending(k => UnitValue(ingredients[k].type)).ThenBy(k => k).ToList())
				{
					if (value <= max) return;
					(int type, int stack) = ingredients[k];
					long unit = UnitValue(type);
					int lower = (int)Math.Min(stack - 1, (value - max + unit - 1) / unit);
					if (lower <= 0) continue;
					ingredients[k] = (type, stack - lower);
					value -= lower * unit;
				}
			}

			// A material found (or made, cleanly) exactly at tier t and had no earlier, weighted by category (null: any category
			// alike) and commonness (common keys far more often), real materials first; each one keys at most KeyUseCap recipes
			// while others are left, and the reuse penalty spreads them further
			// fits: progression (Fits) preferred; budget: the most one unit may be worth, preferred (a Pink Pearl keys no banner)
			private int PickKey(int t, double[] catWeight, Func<int, bool> usable, Random rng, Func<int, bool> fits, long budget)
			{
				List<(int item, float weight)> options = new();
				for (int c = 0; c < ItemCategories.Count; c++)
				{
					double weight = catWeight == null ? 1 : catWeight[c];
					if (weight <= 0) continue;
					foreach (int x in pool[c, t])
					{
						if (real[x] == t && usable(x)) options.Add((x, (float)weight));
					}
				}
				if (options.Count == 0) return 0;
				List<(int item, float weight)> materials = options.Where(o => KeyMaterial(o.item)).ToList();
				if (materials.Count > 0) options = materials;
				List<(int item, float weight)> fitting = options.Where(o => fits(o.item)).ToList();
				if (fitting.Count > 0) options = fitting;
				else stats.UnfitKeys++;
				options = Prefer(options, x => UnitValue(x) <= budget);
				List<(int item, float weight)> fresh = options.Where(o => keyUses.GetValueOrDefault(o.item) < KeyUseCap(o.item)).ToList();
				if (fresh.Count == 0)
				{
					float least = options.Min(o => keyUses.GetValueOrDefault(o.item) / (float)KeyUseCap(o.item));
					fresh = options.Where(o => keyUses.GetValueOrDefault(o.item) / (float)KeyUseCap(o.item) <= least + 1e-4f).ToList();
				}
				return WeightedPick(fresh.Select(o => (o.item, o.weight * KeyCommonnessWeight(o.item) * KeyPenalty(o.item))).ToList(), rng);
			}

			// A vanilla ingredient (of the recipe, or of the item's own recipe for a swap) had exactly at tier t, kept as the key
			private int VanillaKey(IEnumerable<(int type, int stack)> ingredients, int t, Func<int, bool> usable)
			{
				foreach ((int type, _) in ingredients)
				{
					if (type > 0 && type < real.Length && real[type] == t && IngredientReplaceable(type) && !Tainted(type) && usable(type)) return type;
				}
				return 0;
			}

			// Any material found at tier 0, by commonness, without the use caps
			private int HouseMaterial(Func<int, bool> usable, Random rng)
			{
				List<(int item, float weight)> options = new();
				for (int c = 0; c < ItemCategories.Count; c++)
				{
					if (!MaterialLike((ItemCategory)c)) continue;
					foreach (int x in pool[c, 0])
					{
						if (usable(x)) options.Add((x, CommonnessWeight(x)));
					}
				}
				return WeightedPick(options, rng);
			}
		}

		// ---------------------------------------------------------------- Vanilla ingredients

		// The tier of each guaranteed station, summon and progression tool, for the providers that can't read Findability.Tier (it depends on them)
		private static Dictionary<int, int> GuaranteedStationTiers()
		{
			Dictionary<int, int> tiers = new();
			foreach (GuaranteedSources.Requirement station in GuaranteedSources.StationRequirements().Concat(GuaranteedSources.OtherStationRequirements())
				.Concat(GuaranteedSources.SummonRequirements()).Concat(GuaranteedSources.ProgressionToolRequirements()))
			{
				tiers[station.Item] = Math.Min(tiers.GetValueOrDefault(station.Item, station.Tier), station.Tier);
			}
			return tiers;
		}

		// Vanilla mode: ingredients that can't be obtained at their recipe's tier, to be placed on enemies
		public static IEnumerable<GuaranteedSources.Requirement> VanillaRequirements()
		{
			CraftingConfig cfg = CraftingConfig.Instance;
			if (!cfg.RandomizeRecipes || cfg.IngredientMode != IngredientMode.Vanilla || RecipeSystem.RecipeCount == 0) yield break;
			(int, int)[][] ingredients = OriginalIngredients();
			int[] results = Types(BaseResults());
			int[] resultTiers = ResultTiers(results);
			DropSwapped(results);
			// Guaranteed stations count from their tier: an unprotected Hellforge or Crystal Ball has no vanilla source left
			Dictionary<int, int> stations = GuaranteedStationTiers();
			int Found(int item) => stations.TryGetValue(item, out int s) ? Math.Min(s, Findability.BaseTier(item)) : Findability.BaseTier(item);
			int[] avail = Availability(Found, results, resultTiers, ingredients, OriginalTiles(), out _, null, OriginalGroups());
			Dictionary<int, int> needed = new();
			for (int i = 0; i < Count; i++)
			{
				if (!RecipeSystem.IngredientsTouchable(i)) continue;
				int rt = RecipeTier(i);
				foreach ((int g, _) in RecipeSystem.OriginalIngredients(i))
				{
					if (InGroup(RecipeSystem.OriginalGroups(i), g, out _) || !ItemPool.IsRandomizable(g) || ItemPool.IsSpecial(g) || Unusable.Contains(ItemPool.Info[g].Category)) continue;
					// The bar of a hardmode ore the altars did not choose needs no source of that ore
					// A hard gate (no allowance) is never guaranteed before its true tier, even for an earlier recipe
					int from = ItemPool.IsHardGate(g) ? Math.Max(rt, ItemPool.TrueTierOf(g)) : rt;
					if (OreMap.IsHardmodePairOre(g) || (g < avail.Length && avail[g] <= from)) continue;
					needed[g] = Math.Min(needed.GetValueOrDefault(g, from), from);
				}
			}
			foreach ((int item, int tier) in needed) yield return new GuaranteedSources.Requirement { Item = item, Tier = tier };
		}

		// ---------------------------------------------------------------- Mining

		// Pick power needed by the ores of each level (Player.GetPickaxeDamage): meteorite 50, demonite and crimtane 55
		// (below the surface), hellstone 65, cobalt/palladium 100, mythril/orichalcum 110, adamantite/titanium 150,
		// chlorophyte 200. Luminite has no check there (and is dropped by the Moon Lord).
		public static readonly (int pick, int[] ores)[] MiningLevels =
		{
			(50, new int[] { ItemID.Meteorite }),
			(55, new int[] { ItemID.DemoniteOre, ItemID.CrimtaneOre }),
			(65, new int[] { ItemID.Hellstone }),
			(100, new int[] { ItemID.CobaltOre, ItemID.PalladiumOre }),
			(110, new int[] { ItemID.MythrilOre, ItemID.OrichalcumOre }),
			(150, new int[] { ItemID.AdamantiteOre, ItemID.TitaniumOre }),
			(200, new int[] { ItemID.ChlorophyteOre })
		};

		public static int PickPower(int item) => ContentSamples.ItemsByType.TryGetValue(item, out Item sample) ? sample.pick : 0;

		// Minimum pick power of the vanilla tiles that have one (Player.GetPickaxeDamage; some only below the surface, in the
		// underworld or outside the world's center: counted everywhere, conservative)
		private static readonly Dictionary<int, int> VanillaTilePick = new()
		{
			[TileID.Meteorite] = 50,
			[TileID.Demonite] = 55, [TileID.Crimtane] = 55, [TileID.Obsidian] = 55,
			[TileID.Ebonstone] = 65, [TileID.Crimstone] = 65, [TileID.Pearlstone] = 65, [TileID.Hellstone] = 65, [TileID.Hellforge] = 65,
			[TileID.Cobalt] = 100, [TileID.Palladium] = 100,
			[TileID.Mythril] = 110, [TileID.Orichalcum] = 110,
			[TileID.Adamantite] = 150, [TileID.Titanium] = 150,
			[TileID.Chlorophyte] = 200,
			[TileID.LihzahrdBrick] = 210, [TileID.LihzahrdAltar] = 210
		};

		// Pick power needed to mine the tile an item places (0: none, or it places nothing): vanilla checks, dungeon bricks
		// (100), a modded tile's MinPick
		public static int PickToMine(int item)
		{
			if (!ContentSamples.ItemsByType.TryGetValue(item, out Item sample) || sample.createTile < TileID.Dirt) return 0;
			int tile = sample.createTile;
			if (VanillaTilePick.TryGetValue(tile, out int pick)) return pick;
			// The temple traps (styles 1 to 4), not the dart trap
			if (tile == TileID.Traps) return sample.placeStyle is >= 1 and <= 4 ? 210 : 0;
			if (tile < Main.tileDungeon.Length && Main.tileDungeon[tile]) return 100;
			return TileLoader.GetTile(tile)?.MinPick ?? 0;
		}

		// The tier at which the ores of a level are mined (-1: none of them in this world)
		public static int MiningTier((int pick, int[] ores) level)
		{
			int tier = int.MaxValue;
			foreach (int ore in level.ores)
			{
				if (ItemPool.IsValid(ore) && OreMap.IsFindable(ore)) tier = Math.Min(tier, ItemPool.TierOf(ore));
			}
			return tier == int.MaxValue ? -1 : tier;
		}

		// Ores that need at least this pick power: not mined before such a pickaxe exists. Randomized ores are a permutation
		// inside each tier (OreMap: the Cobalt item may come from the Adamantite tile), so then every ore of such a tier counts
		public static HashSet<int> OresFrom(int pick)
		{
			HashSet<int> ores = MiningLevels.Where(l => l.pick >= pick).SelectMany(l => l.ores).ToHashSet();
			if (pick <= 55) ores.Add(ItemID.Obsidian);
			if (LootConfig.Instance.RandomizeOreDrops)
			{
				HashSet<int> tiers = ores.Where(ItemPool.IsValid).Select(ItemPool.TierOf).ToHashSet();
				for (int type = 1; type < ItemPool.Info.Length; type++)
				{
					if (OreMap.IsOre(type) && tiers.Contains(ItemPool.TierOf(type))) ores.Add(type);
				}
			}
			return ores;
		}

		// Every ore of a mining level, and Obsidian (55): never a new ingredient
		private static readonly HashSet<int> MiningOres = MiningLevels.SelectMany(l => l.ores).Append(ItemID.Obsidian).ToHashSet();

		// A pickaxe (or drill) strong enough for each mining level, by the tier of its ores: when the recipes (results rolled
		// from the original recipes) and the raw sources don't give one in time, the cheapest vanilla one of that level is
		// placed on common enemies. Computed from Findability.BaseTier and the stations' requirements, never from
		// RecipePlanner.Get() (the plan depends on the guaranteed sources). The ores of the level and above count as not
		// found at all (not even dropped): conservative, a spare pickaxe is harmless.
		public static IEnumerable<GuaranteedSources.Requirement> MiningRequirements()
		{
			if (RecipeSystem.RecipeCount == 0 || !ItemPool.Ready) yield break;
			(int, int)[][] ingredients = OriginalIngredients();
			int[][] tiles = OriginalTiles();
			int[] results = Types(BaseResults());
			int[] resultTiers = ResultTiers(results);
			DropSwapped(results);
			Dictionary<int, int> stations = GuaranteedStationTiers();
			List<int> pickaxes = ContentSamples.ItemsByType.Where(pair => pair.Value.pick > 0 && ItemPool.IsValid(pair.Key) && !ItemPool.Info[pair.Key].Excluded).Select(pair => pair.Key).OrderBy(t => t).ToList();
			Dictionary<int, int> guaranteed = new();
			foreach ((int pick, int[] ores) level in MiningLevels)
			{
				int oreTier = MiningTier(level);
				if (oreTier < 0) continue;
				HashSet<int> unmined = OresFrom(level.pick);
				int Found(int item)
				{
					if (unmined.Contains(item)) return Never;
					int tier = Findability.BaseTier(item);
					if (stations.TryGetValue(item, out int s)) tier = Math.Min(tier, s);
					if (guaranteed.TryGetValue(item, out int g)) tier = Math.Min(tier, g);
					return tier;
				}
				int[] avail = Availability(Found, results, resultTiers, ingredients, tiles, out _, null, OriginalGroups());
				// A crafted pickaxe no earlier than its ResultFloor: a rebuilt recipe keeps its level
				int best = pickaxes.Where(p => PickPower(p) >= level.pick).Select(p => p < avail.Length ? Math.Min(Found(p), Math.Max(avail[p], ResultFloor(p))) : Never).DefaultIfEmpty(Never).Min();
				if (best <= oreTier) continue;
				// The weakest vanilla pickaxe of that level, then the cheapest (a modded one only when no vanilla one is left)
				int chosen = pickaxes.Where(p => PickPower(p) >= level.pick && !ItemPool.IsBanned(p))
					.OrderBy(p => p < ItemID.Count ? 0 : 1).ThenBy(PickPower).ThenBy(StackRules.ValueOf).ThenBy(p => p).DefaultIfEmpty(0).First();
				if (chosen <= 0) continue;
				guaranteed[chosen] = oreTier;
				yield return new GuaranteedSources.Requirement { Item = chosen, Tier = oreTier, Tool = true };
			}
		}
	}
}

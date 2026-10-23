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
		// Copies of an earlier recipe once planned (same result, ingredients, groups and conditions): switched off when applied
		// and left out of Availability, so the crafting menu never shows one recipe on two stations
		public bool[] Twin;
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

		public static RecipePlan Get() => plan ??= Build(CraftingConfig.Instance.RecipesForUncraftable, PlanSettings.FromConfig());

		// A plan built with explicit settings, not cached (tests: the config is never changed). null = the setting
		public static RecipePlan BuildWith(bool shells, float? reusePenalty = null, float? swapChance = null, float? surpriseChance = null)
		{
			PlanSettings settings = PlanSettings.FromConfig();
			if (reusePenalty != null) settings.ReusePenalty = reusePenalty.Value;
			if (swapChance != null) settings.SwapChance = swapChance.Value;
			if (surpriseChance != null) settings.SurpriseChance = surpriseChance.Value;
			return Build(shells, settings);
		}

		// The settings a plan is built with (the config, or explicit values for the tests)
		private sealed class PlanSettings
		{
			public float ReusePenalty;
			public float SwapChance;
			public float SurpriseChance;

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
			return Tiers.Clamp(ItemPool.UnlockTier(ItemPool.TrueTierOf(result)));
		}

		private static bool Gated(int i) => Randomized(i) && ProgressionConfig.Instance.TierGating && GeneralConfig.Instance.RandomizerMode == RandomizerMode.Coherent;

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
		// A swapped recipe gets ingredients rebuilt for the item it gives (its tier, key and value, a shape blending both
		// recipes). Each swappable recipe starts a swap with half the chance (its partner is swapped too, so about chance% of
		// them are), preferably with a recipe of another category. Static data only (never Findability: the guaranteed
		// sources read these results). chance in percent; results[i] and partners[i] (-1: not swapped)
		public static ((int type, int stack)[] results, int[] partners) SwappedResults(float chance)
		{
			int count = Count;
			(int, int)[] results = new (int, int)[count];
			int[] partners = Enumerable.Repeat(-1, count).ToArray();
			for (int i = 0; i < count; i++) results[i] = (RecipeSystem.OriginalResult(i), RecipeSystem.OriginalStack(i));
			if (chance <= 0 || !ItemPool.Ready) return (results, partners);
			HashSet<int> stationTiles = Stations.RequiredTiles();
			List<int> eligible = Enumerable.Range(0, count).Where(i => Swappable(i, stationTiles)).OrderBy(i => (uint)RecipeSystem.RecipeKey(i)).ThenBy(i => i).ToList();
			bool anyTier = GeneralConfig.Instance.RandomizerMode == RandomizerMode.Chaos;
			Dictionary<int, List<int>> byTier = eligible.GroupBy(i => anyTier ? 0 : ItemPool.TierOf(RecipeSystem.OriginalResult(i))).ToDictionary(g => g.Key, g => g.ToList());
			foreach (int i in eligible)
			{
				// One draw per recipe, whatever happened before: the same recipes start a swap in every machine and plan
				Random rng = DetRandom.Get(RollKey.Recipe, RecipeSystem.RecipeKey(i), RollKey.Surprise);
				if (rng.NextDouble() * 100 >= chance / 2 || partners[i] >= 0) continue;
				int x = RecipeSystem.OriginalResult(i);
				List<int> others = byTier[anyTier ? 0 : ItemPool.TierOf(x)].Where(j => j != i && partners[j] < 0 && SwapFits(i, j) && SwapFits(j, i)).ToList();
				// Another category first (furniture and blocks are one): a brick for a chair is no surprise
				List<int> different = others.Where(j => !Alike(CategoryOf(RecipeSystem.OriginalResult(j)), CategoryOf(x))).ToList();
				if (different.Count > 0) others = different;
				if (others.Count == 0) continue;
				int partner = others[rng.Next(others.Count)];
				partners[i] = partner;
				partners[partner] = i;
				(results[i], results[partner]) = (results[partner], results[i]);
			}
			return (results, partners);
		}

		// May this recipe take part in a swap? A randomized vanilla recipe whose ingredients are rebuilt, giving no upgrade,
		// banned item, bag, summon or station (the station plan and the guaranteed stations rely on how each one is made).
		// The house recipes and the mining chain are never randomized (Randomized)
		public static bool Swappable(int i) => Swappable(i, Stations.RequiredTiles());

		private static bool Swappable(int i, HashSet<int> stationTiles)
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
				else if (!RecipeSystem.VanillaDisabled(i) && !RecipeSystem.IsModded(i)) return i;
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
			result.Groups = new int[count][];
			result.IngredientTiers = Enumerable.Repeat(-1, count).ToArray();
			result.PlanOrder = Enumerable.Repeat(-1, count).ToArray();
			result.PlannedKeys = new int[count];
			result.UseCaps = new int[Tiers.Count];
			result.CandidateTiers = Array.Empty<int>();
			result.ShellTiers = Enumerable.Repeat(-1, count).ToArray();
			// The vanilla availability (the original recipes as they are): what the ingredient tiers follow; a swapped recipe reads
			// its partner's, the vanilla recipe of the item it gives
			int[] vanillaResults = Enumerable.Range(0, count).Select(RecipeSystem.OriginalResult).ToArray();
			int[] avail = Availability(Findability.Tier, vanillaResults, new int[count], ingredients, tiles, out int[] recipeTiers, null, OriginalGroups());
			// Stations first (from the original recipes only): the ingredient plan needs them to know when each result is made
			if (cfg.RandomizeRecipes && cfg.RandomizeStations) PlanStations(result, results, avail, recipeTiers);
			// The shells are planned by the same rebuilder, after every other recipe (they shift none of their choices); the
			// upgrades' shells whatever the setting
			shells &= cfg.RandomizeRecipes;
			ItemGraph graph = new();
			if (cfg.RandomizeRecipes) new Rebuilder(result, results, avail, recipeTiers, tiles, shells, settings, graph).Run();
			if (cfg.RandomizeRecipes) LockResults(result, results, ingredients, tiles, graph);
			if (cfg.RandomizeRecipes) LockSpecials(result, results, ingredients, tiles, graph);
			if (cfg.RandomizeRecipes) ShareTwinStations(result, ingredients, tiles, avail);
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
			return Tiers.Clamp(Math.Max(tier, ItemPool.UnlockTier(ItemPool.TrueTierOf(item))));
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
			// The shells are left out: the locks are the same with the setting on or off
			bool[] noShells = ShellMask(null);
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
				$"tiers without a key material: {(noMaterial.Count == 0 ? "none" : string.Join(", ", noMaterial))}; most used keys: {topKeys}");
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
				if (result.PlannedKeys[i] == 0 && PlaceKey(result, results, i, u, buckets, weights, avail, uses, graph, out _, rng, uncommon: u == 0, append: true) > 0) result.KeyIngredient[i] = true;
				// A shell without a key (none at its tier) stays off
				if (shell && result.PlannedKeys[i] == 0 && !result.KeyIngredient[i])
				{
					DropShell(result, i);
					ModContent.GetInstance<UltimateRandomizer>().Logger.Warn($"No key material for the recipe of {DetRandom.ItemName(item)}: it keeps its vanilla sources only");
					continue;
				}
				RaiseValue(result, i, StackRules.ValueOf(item));
			}
		}

		// Raises a recipe's ingredient stacks until they are worth at least target: the most valuable stackable ingredients up
		// to the ingredient stack cap first, then every stack in proportion, then the most valuable ones up to their max stack
		private static void RaiseValue(RecipePlan result, int i, long target)
		{
			(int type, int stack)[] current = ((int, int)[])(result.Ingredients[i] ?? RecipeSystem.OriginalIngredients(i)).Clone();
			long Value() => current.Sum(p => UnitValue(p.type) * p.stack);
			if (current.Length == 0 || Value() >= target) return;
			List<int> byValue = Enumerable.Range(0, current.Length).OrderByDescending(k => UnitValue(current[k].type)).ThenBy(k => k).ToList();
			void Fill(Func<int, int> cap)
			{
				foreach (int k in byValue)
				{
					long missing = target - Value();
					if (missing <= 0) return;
					(int type, int stack) = current[k];
					long wanted = stack + (missing + UnitValue(type) - 1) / UnitValue(type);
					current[k] = (type, (int)Math.Max(stack, Math.Min(wanted, cap(type))));
				}
			}
			Fill(type => Math.Min(StackRules.MaxStackOf(type), CraftingConfig.Instance.IngredientStackCap));
			long value = Value();
			if (value < target)
			{
				double factor = (double)target / value;
				for (int k = 0; k < current.Length; k++)
				{
					(int type, int stack) = current[k];
					current[k] = (type, (int)Math.Max(stack, Math.Min(Math.Ceiling(stack * factor), StackRules.MaxStackOf(type))));
				}
			}
			Fill(StackRules.MaxStackOf);
			result.Ingredients[i] = current;
			result.IngredientValues[i] = Value();
		}

		// Value of one unit of an ingredient (at least 1 copper: a worthless item still counts)
		private static long UnitValue(int type) => Math.Max(StackRules.ValueOf(type), 1);

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

		// One replaceable ingredient (a material one first: a Zenith keeps its swords) becomes a key material of tier u (else
		// u + 1), weighted by commonness; each material is key of at most KeyUseCap recipes while others are left. Never an
		// item made (directly or not) from the recipe's result (graph: no loop).
		// rng: RollKey.KeyIngredient of the recipe when null; uncommon: an uncommon material when the tier has one; append: a
		// recipe without a replaceable slot gets the key as a new ingredient
		private static int PlaceKey(RecipePlan result, int[] results, int i, int u, List<int>[,] buckets, WeightTable weights, int[] avail, Dictionary<int, int> uses, ItemGraph graph,
			out bool later, Random rng = null, bool uncommon = false, bool append = false)
		{
			later = false;
			int[] groups = GroupsOf(result, i);
			(int type, int stack)[] current = ((int, int)[])(result.Ingredients[i] ?? RecipeSystem.OriginalIngredients(i)).Clone();
			// A group slot stays (it would leave its group behind)
			List<int> slots = Enumerable.Range(0, current.Length).Where(k => IngredientReplaceable(current[k].type) && !InGroup(groups, current[k].type, out _)).ToList();
			if (slots.Count == 0 && !append) return 0;
			rng ??= DetRandom.Get(RollKey.KeyIngredient, RecipeSystem.RecipeKey(i));
			int slot = -1;
			if (slots.Count > 0)
			{
				List<int> materialSlots = slots.Where(k => MaterialLike(ItemPool.Info[current[k].type].Category)).ToList();
				slot = (materialSlots.Count > 0 ? materialSlots : slots)[rng.Next(materialSlots.Count > 0 ? materialSlots.Count : slots.Count)];
			}
			int originalResult = RecipeSystem.OriginalResult(i);
			HashSet<int> below = graph.From(results[i]);
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
				// The key keeps the recipe's value: it is worth what the original ingredients were, less the other slots now
				// (the slot may already hold a replacement held by the stack cap or rounded up: its value does not compound)
				long others = current.Where((p, k) => k != slot).Sum(p => UnitValue(p.type) * p.stack);
				(int, int) placed = (key, ReplacementStack(Math.Max(1, RecipeSystem.OriginalIngredientValue(i) - others), key));
				if (slot >= 0) current[slot] = placed;
				else current = current.Append(placed).ToArray();
				result.Ingredients[i] = current;
				result.IngredientValues[i] = current.Sum(p => UnitValue(p.type) * p.stack);
				uses[key] = uses.GetValueOrDefault(key) + 1;
				graph.Add(key, results[i]);
				later = t > u;
				return key;
			}
			return 0;
		}

		// A key material serves at most this many recipes while another one of its tier is less used (common ones more)
		private static int KeyUseCap(int item) => ItemPool.CommonnessOf(item) switch
		{
			Commonness.Common => 18,
			Commonness.Rare => 3,
			_ => 6
		};

		// Commonness weights of a key: a tier's common materials lock most recipes, rare ones (Broken Hero Sword) seldom
		private static float KeyCommonnessWeight(int item) => ItemPool.CommonnessOf(item) switch
		{
			Commonness.Common => 10f,
			Commonness.Rare => 0.1f,
			_ => 1f
		};

		// A real crafting material: a stackable material, bar or block (not a bucket, a sponge or a tool used in a recipe)
		private static bool KeyMaterial(int item) => ItemPool.IsValid(item) && MaterialLike(ItemPool.Info[item].Category) && StackRules.MaxStackOf(item) > 1;

		// Item -> item edges of the planned recipes (each ingredient, or every member of its recipe group, to the result): an
		// ingredient is refused when the recipe's result already leads to it, so no item is ever made from itself
		private sealed class ItemGraph
		{
			private readonly Dictionary<int, HashSet<int>> next = new();

			public void Add(int from, int to)
			{
				if (from <= 0 || to <= 0) return;
				if (!next.TryGetValue(from, out HashSet<int> set)) next[from] = set = new HashSet<int>();
				set.Add(to);
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
			IEnumerable<List<int>> groupsFound = Enumerable.Range(0, Count).Where(i => !RecipeSystem.IsModded(i) && !RecipeSystem.VanillaDisabled(i))
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

		// Stations a recipe may be given: basic and progression ones (not the Demon Altar, not themed or modded ones)
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
				.OrderBy(TileAvail).ThenBy(t => Stations.Info(t).Kind == StationKind.Basic ? 0 : 1).ThenBy(t => DetRandom.StableHash(TileID.Search.GetName(t)))
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
		private const double MinCloseness = 0.02;

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
			long unit = Math.Max(StackRules.ValueOf(x), 1);
			return (int)Math.Clamp((value + unit - 1) / unit, 1, Math.Min(StackRules.MaxStackOf(x), CraftingConfig.Instance.IngredientStackCap));
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
		// one key ingredient found exactly at T, about the vanilla value. A swapped recipe is rebuilt as the vanilla recipe of
		// the item it gives (its proxy: tier, value, count, tiers followed), its categories blending both recipes. Recipes are
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
			private readonly bool shells;
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
			private readonly Dictionary<int, int> uses = new();
			private readonly Dictionary<int, int> keyUses = new();
			// The uses (ingredients and keys) when the planning reached the current tier: the reuse penalty reads these, so a
			// choice changed in a tier never shifts the weights of the other recipes of that tier
			private Dictionary<int, int> frozen = new();
			private int frozenTier = -1;
			private readonly int[] targets;
			private int[] useCaps;
			private int rebuilt, vanillaKept, keyed, crafted, groupSlots, slotsTotal, surprises, swapped;
			private readonly SortedSet<int> noKey = new();

			public Rebuilder(RecipePlan result, int[] results, int[] vanillaAvail, int[] recipeTiers, int[][] tiles, bool shells, PlanSettings settings, ItemGraph graph)
			{
				this.shells = shells;
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
					if (!RecipeSystem.IsModded(i)) used.UnionWith(RecipeSystem.OriginalGroups(i));
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
			// result (the key ingredient then locks it). A house recipe is built at once
			private int TargetTier(int i)
			{
				if (RecipeSystem.HouseRecipe(i)) return 0;
				int proxy = Proxy(i);
				int rt = RecipeTier(proxy);
				int cap = recipeTiers[proxy] < Never ? Math.Min(rt, recipeTiers[proxy]) : rt;
				return Tiers.Clamp(Math.Max(cap, result.ResultTiers[i]));
			}

			// A new tier batch: the uses so far become what the reuse penalty reads until the next one
			private void Freeze(int tier)
			{
				if (tier == frozenTier) return;
				frozenTier = tier;
				frozen = new Dictionary<int, int>(uses);
				foreach ((int item, int n) in keyUses) frozen[item] = frozen.GetValueOrDefault(item) + n;
			}

			// w / (1 + k * uses): a material already in many recipes is picked less often (IngredientReusePenalty)
			private float Penalty(int key) => settings.ReusePenalty <= 0 ? 1f : 1f / (1f + settings.ReusePenalty * frozen.GetValueOrDefault(key));

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
				useCaps = reaching.Select(n => Math.Max(MinUseCap, (int)Math.Ceiling(n * MaxUseShare))).ToArray();
				// The edges no choice can change: the recipes kept as they are, and the protected ingredients of the rebuilt ones
				for (int i = 0; i < count; i++)
				{
					if (RecipeSystem.IsShell(i) || RecipeSystem.VanillaDisabled(i)) continue;
					IEnumerable<(int type, int stack)> fixedOnes = RecipeSystem.OriginalIngredients(i);
					if (targets[i] >= 0) fixedOnes = fixedOnes.Where(p => !IngredientReplaceable(p.type));
					graph.AddRecipe(fixedOnes, RecipeSystem.OriginalGroups(i), results[i]);
				}
				// The other recipes (kept ingredients) are evaluated in the same order, from the tier they were doable at
				List<int> order = Enumerable.Range(0, count).Where(i => !RecipeSystem.VanillaDisabled(i))
					.OrderBy(i => targets[i] >= 0 ? targets[i] : recipeTiers[i] < Never ? recipeTiers[i] : Tiers.Count)
					.ThenBy(i => (uint)RecipeSystem.RecipeKey(i)).ThenBy(i => i).ToList();
				for (int p = 0; p < order.Count; p++)
				{
					int i = order[p];
					result.PlanOrder[i] = p;
					if (targets[i] >= 0)
					{
						Freeze(targets[i]);
						Rebuild(i);
					}
					Made(i);
				}
				result.UseCaps = useCaps;
				string busiest = string.Join(", ", uses.Where(p => p.Key > 0).OrderByDescending(p => p.Value).ThenBy(p => p.Key).Take(5).Select(p => $"{DetRandom.ItemName(p.Key)} x{p.Value}"));
				ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Recipe plan: {rebuilt} recipes rebuilt ({swapped} for a swapped result; {vanillaKept} kept vanilla: no candidate or no key), {keyed} with a key of their tier " +
					$"(none at tiers {(noKey.Count == 0 ? "-" : string.Join(", ", noKey))}), {slotsTotal} new ingredients of which {crafted} crafted, {groupSlots} recipe groups and {surprises} surprises; " +
					$"use caps {string.Join("/", useCaps)}; most used: {busiest}");
				// The upgrades' shells whatever the setting (they are the upgrades' recipes), then the others: neither shifts a
				// choice above
				int position = PlanShells(order.Count, SpecialShells(), true);
				if (shells) PlanShells(position, EligibleShells(), false);
				result.CandidateTiers = (int[])clean.Clone();
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
			// its station: LockSpecials adds its key). Returns the next planning position
			private int PlanShells(int position, List<int> shellList, bool special)
			{
				List<int> stations = Stations.RequiredTiles().Where(Assignable)
					.OrderBy(t => Stations.Info(t).Kind == StationKind.Basic ? 0 : 1).ThenBy(t => DetRandom.StableHash(TileID.Search.GetName(t))).ToList();
				Dictionary<ItemCategory, (int eligible, int planned)> byCategory = new();
				int planned = 0, noStation = 0;
				foreach (int i in shellList.OrderBy(i => targets[i]).ThenBy(i => (uint)DetRandom.ItemKey(results[i])).ThenBy(i => i))
				{
					ItemCategory category = ItemPool.Info[results[i]].Category;
					(int eligible, int done) = byCategory.GetValueOrDefault(category);
					eligible++;
					result.PlanOrder[i] = position++;
					int top = targets[i];
					Freeze(top);
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
				return position;
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

			// How well a unit value suits a slot worth share: 1 when a stack within the caps can be worth it, less for an item worth
			// more than the whole share (a bar for a cheap slot) or that can't reach it (arrows for a bar's worth)
			private double Closeness(int x, double share)
			{
				if (share <= 0) return 1;
				double unit = UnitValue(x);
				double most = unit * Math.Min(StackRules.MaxStackOf(x), cfg.IngredientStackCap);
				if (unit > share) return Math.Max(MinCloseness, share / unit);
				if (most < share) return Math.Max(MinCloseness, most / share);
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

				// Protected ingredients (and their groups) stay; the others give the shape (a swapped recipe: blended with the shape
				// of the item's vanilla recipe, which also gives the count and the tiers followed)
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
				int n0 = Math.Clamp((isShell ? ShellIngredientCount : proxyOriginal.Length) + CountDelta(rng, cfg.IngredientCountSpread), 1, Math.Max(1, cfg.IngredientMax));
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
				// Category weights: the ingredient weights, plus the vanilla shape (a swap: half the item's, half the host's)
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
					double share = !isSwapped || itemShape.Count == 0 ? ShapeShare(shape, c) : shape.Count == 0 ? ShapeShare(itemShape, c) : 0.5 * ShapeShare(itemShape, c) + 0.5 * ShapeShare(shape, c);
					catWeight[c] += bonus * share;
				}
				List<(int item, float weight)> categories = Enumerable.Range(0, cats).Where(c => catWeight[c] > 0).Select(c => (c, (float)catWeight[c])).ToList();
				// Surprise slots draw their category with the loot weights (no bag, no summon: never ingredients)
				List<(int item, float weight)> surpriseCategories = house ? new List<(int, float)>() : Enumerable.Range(0, cats)
					.Where(c => hasCategory[c] && !Unusable.Contains((ItemCategory)c) && lootWeights.Get((ItemCategory)c) > 0).Select(c => (c, (float)lootWeights.Get((ItemCategory)c))).ToList();

				// Never the result, nor anything the result already leads to (a loop through other recipes)
				HashSet<int> below = graph.From(result0);
				HashSet<int> taken = kept.Select(p => p.type).ToHashSet();
				bool Usable(int x) => x != result0 && x != originalResult && !taken.Contains(x) && !below.Contains(x) && !RecipeSystem.InGroups(chosenGroups, x)
					&& (!house || (Findability.Tier(x) == 0 && MaterialLike(ItemPool.Info[x].Category) && !originalTypes.Contains(x)));
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
				// a swapped one takes a key of T + 1 (later rather than too early)
				int key = 0;
				// The tier the recipe ends up at (T, or T + 1 with a later key)
				int keyTier = top;
				if (top > 0 && !house)
				{
					key = PickKey(top, catWeight, Usable, rng);
					if (key == 0) key = PickKey(top, null, Usable, rng);
					if (key == 0) key = VanillaKey(original.Concat(proxyOriginal), top, Usable);
					if (key == 0 && isSwapped && top < Tiers.Max)
					{
						key = PickKey(top + 1, null, Usable, rng);
						if (key > 0) keyTier = top + 1;
					}
				}
				if (key > 0)
				{
					Add(key);
					keyUses[key] = keyUses.GetValueOrDefault(key) + 1;
					result.PlannedKeys[i] = key;
					if (!isShell) keyed++;
				}
				else if (top > 0 && !house)
				{
					// Without a key, a shell would be doable before the tier its item is found at, a recipe before its tier
					if (isShell) return false;
					noKey.Add(top);
					if (!isSwapped)
					{
						vanillaKept++;
						return false;
					}
				}

				for (int slot = planned.Count; slot < slots; slot++)
				{
					double slotShare = remaining * shares[slot] / shareSum;
					// Drawn for every slot (two draws), so the stream never depends on the chance
					bool surprise = surpriseRng.NextDouble() * 100 < settings.SurpriseChance;
					int surpriseCategory = surpriseCategories.Count > 0 ? WeightedPick(surpriseCategories, surpriseRng) : -1;
					surprise &= surpriseCategory >= 0;
					int picked = 0;
					bool wasSurprise = false;
					for (int attempt = 0; attempt < 12 && picked == 0 && categories.Count > 0; attempt++)
					{
						int c = WeightedPick(categories, rng);
						// A surprise slot tries its loot category first, at a value close to its share
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
							if (!Usable(x) || uses.GetValueOrDefault(x) >= cap || !Affordable(x)) continue;
							if (surprised && UnitValue(x) > 2 * slotShare + Item.silver) continue;
							options.Add((x, (float)(CommonnessWeight(x) * Penalty(x) * Closeness(x, slotShare))));
						}
						if (!house)
						{
							for (int k = 0; k < groups.Count; k++)
							{
								if ((int)groups[k].category != c || groupTiers[k] != tier || uses.GetValueOrDefault(-(groups[k].id + 1)) >= cap || !UsableGroup(k)) continue;
								int iconic = groups[k].group.IconicItemId;
								if (!Affordable(iconic) || (surprised && UnitValue(iconic) > 2 * slotShare + Item.silver)) continue;
								options.Add((-(k + 1), (float)(CommonnessWeight(iconic) * Penalty(-(groups[k].id + 1)) * Closeness(iconic, slotShare))));
							}
						}
						picked = WeightedPick(options, rng);
						wasSurprise = surprised && picked != 0;
					}
					// A house is built at once: any material found at tier 0 then, whatever its uses
					if (picked == 0 && house) picked = HouseMaterial(Usable, rng);
					if (picked == 0) continue;
					if (wasSurprise && !isShell) surprises++;
					if (picked < 0)
					{
						(int id, RecipeGroup group, _) = groups[-picked - 1];
						chosenGroups.Add(id);
						Add(group.IconicItemId);
						if (!house) uses[-(id + 1)] = uses.GetValueOrDefault(-(id + 1)) + 1;
						if (!isShell) groupSlots++;
						continue;
					}
					Add(picked);
					if (!house) uses[picked] = uses.GetValueOrDefault(picked) + 1;
					if (source[picked] >= 0 && !isShell) crafted++;
				}
				if (planned.Count == 0)
				{
					if (!isShell) vanillaKept++;
					return false;
				}
				if (!isShell)
				{
					rebuilt++;
					slotsTotal += planned.Count;
					if (isSwapped) swapped++;
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
				// With the value cap, the result is weighed against the vanilla ingredients' value: the new ones are worth at least that
				if (cfg.LimitRecipeValue && !house && !isShell && (long)StackRules.ValueOf(result.Results[i].type) * result.Results[i].stack > RecipeSystem.MaxResultValue(proxy, result.IngredientValues[i], cfg.RecipeValueGuard))
				{
					RaiseValue(result, i, vanillaValue);
				}
				// A shell's item is worth vanillaValue: its ingredients are worth at least that within the guard (the item still
				// sells for its ingredients only, CraftedValueFeature)
				if (cfg.LimitRecipeValue && isShell && vanillaValue > (long)(result.IngredientValues[i] * cfg.RecipeValueGuard) + Item.silver)
				{
					RaiseValue(result, i, vanillaValue);
				}
				return true;
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
			private int PickKey(int t, double[] catWeight, Func<int, bool> usable, Random rng)
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
				List<(int item, float weight)> fresh = options.Where(o => keyUses.GetValueOrDefault(o.item) < KeyUseCap(o.item)).ToList();
				if (fresh.Count == 0)
				{
					float least = options.Min(o => keyUses.GetValueOrDefault(o.item) / (float)KeyUseCap(o.item));
					fresh = options.Where(o => keyUses.GetValueOrDefault(o.item) / (float)KeyUseCap(o.item) <= least + 1e-4f).ToList();
				}
				return WeightedPick(fresh.Select(o => (o.item, o.weight * KeyCommonnessWeight(o.item) * Penalty(o.item))).ToList(), rng);
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

		// The tier of each guaranteed station and summon, for the providers that can't read Findability.Tier (it depends on them)
		private static Dictionary<int, int> GuaranteedStationTiers()
		{
			Dictionary<int, int> tiers = new();
			foreach (GuaranteedSources.Requirement station in GuaranteedSources.StationRequirements().Concat(GuaranteedSources.OtherStationRequirements())
				.Concat(GuaranteedSources.SummonRequirements()))
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
					if (OreMap.IsHardmodePairOre(g) || (g < avail.Length && avail[g] <= rt)) continue;
					needed[g] = Math.Min(needed.GetValueOrDefault(g, rt), rt);
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
				int best = pickaxes.Where(p => PickPower(p) >= level.pick).Select(p => p < avail.Length ? avail[p] : Never).DefaultIfEmpty(Never).Min();
				if (best <= oreTier) continue;
				// The weakest vanilla pickaxe of that level, then the cheapest
				int chosen = pickaxes.Where(p => p < ItemID.Count && PickPower(p) >= level.pick)
					.OrderBy(PickPower).ThenBy(StackRules.ValueOf).ThenBy(p => p).DefaultIfEmpty(0).First();
				if (chosen <= 0) continue;
				guaranteed[chosen] = oreTier;
				yield return new GuaranteedSources.Requirement { Item = chosen, Tier = oreTier, Tool = true };
			}
		}
	}
}

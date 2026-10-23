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
		// U of each result: the unlocked tier of its true tier (0 when not gated). Display and tests only: the recipe gives
		// Results[i] at once, a key ingredient (or the re-pick of the result) keeps it from being made before U
		public int[] ResultTiers;
		// Tier at which each item can be obtained (found or crafted), with this plan
		public int[] Availability;
		// Recipes that received a key ingredient of tier U, and recipes whose result was re-picked (no slot, no material)
		public bool[] KeyIngredient;
		public bool[] ResultRepicked;
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

		// Results rolled from the original recipes, shared by the plan and the requirement providers
		private static (int type, int stack)[] originalResults;
		// The recipe giving each life or mana upgrade (set with originalResults)
		private static Dictionary<int, int> specialRecipes = new();

		static RecipePlanner()
		{
			WorldCaches.Reset += Reset;
		}

		public static RecipePlan Get() => plan ??= Build(CraftingConfig.Instance.RecipesForUncraftable);

		// A plan built with an explicit shell setting, not cached (tests: the config is never changed)
		public static RecipePlan BuildWith(bool shells) => Build(shells);

		public static void Reset()
		{
			plan = null;
			originalResults = null;
			specialRecipes = new Dictionary<int, int>();
		}

		private static int Count => Math.Min(RecipeSystem.RecipeCount, Recipe.numRecipes);

		// Recipes whose result Apply rolls (a house recipe keeps its result: only its ingredients and station change)
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

		// May the planner give this recipe a key ingredient (randomized ingredients, a replaceable original slot)? Without one,
		// a result above what the recipe needs is re-picked
		public static bool HasKeySlot(int i)
		{
			if (CraftingConfig.Instance.IngredientMode != IngredientMode.Randomized || !RecipeSystem.IngredientsTouchable(i)) return false;
			return RecipeSystem.OriginalIngredients(i).Any(x => IngredientReplaceable(x.type));
		}

		// Providers that can't read the plan (it depends on them) count the results the lock may re-pick as made by nothing
		private static void DropRepickable(int[] results)
		{
			for (int i = 0; i < results.Length; i++)
			{
				if (Gated(i) && !HasKeySlot(i) && results[i] != RecipeSystem.OriginalResult(i)) results[i] = 0;
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

		// Results once everything is unlocked, from the original recipes (the plan must not depend on progression)
		private static (int type, int stack)[] UnlockedResults() => originalResults ??= UnlockedResults(OriginalIngredients());

		private static (int type, int stack)[] UnlockedResults((int type, int stack)[][] ingredients)
		{
			int count = Count;
			(int, int)[] results = new (int, int)[count];
			for (int i = 0; i < count; i++) results[i] = ResultFor(i, ingredients[i], lucky: true);
			AssignSpecials(results, ingredients);
			return results;
		}

		// ---------------------------------------------------------------- Upgrades

		// The recipe whose result is this life or mana upgrade in this world, -1 when none
		public static int SpecialRecipe(int item)
		{
			UnlockedResults();
			return specialRecipes.TryGetValue(item, out int i) ? i : -1;
		}

		// Results a special recipe may take the place of: nothing progression relies on (tools, bars, materials, stations,
		// summons, gear), so the guaranteed pickaxes and vanilla ingredients computed from the results stay the same
		private static readonly ItemCategory[] SpecialHosts = { ItemCategory.Furniture, ItemCategory.Block, ItemCategory.Vanity, ItemCategory.Dye, ItemCategory.Misc, ItemCategory.Food };

		private static bool SpecialCandidate(int i, int item, int u, (int type, int stack)[] results, (int type, int stack)[] ingredients, HashSet<int> stationTiles)
		{
			if (!Randomized(i) || RecipeSystem.VanillaDisabled(i) || specialRecipes.ContainsValue(i)) return false;
			int result = results[i].type;
			if (!ItemPool.IsValid(result) || ItemPool.IsSpecial(result) || ItemPool.IsSpecial(RecipeSystem.OriginalResult(i))) return false;
			if (!SpecialHosts.Contains(ItemPool.Info[result].Category) || (Stations.PlacedTile(result, out int tile) && stationTiles.Contains(tile))) return false;
			if (ingredients.Any(x => x.type == item) || RecipeSystem.InOriginalGroup(i, item)) return false;
			// Doable by its tier: a key ingredient of that tier then makes it wait for it (it needs a slot for one)
			if (RecipeTier(i) > u) return false;
			return u == 0 || !Gated(i) || HasKeySlot(i);
		}

		// Each life or mana upgrade becomes the result of one randomized recipe (deterministic, RollKey.Special), preferably one
		// of its tier or the one before; the key ingredient of LockResults then makes it doable from the upgrade's tier
		private static void AssignSpecials((int type, int stack)[] results, (int type, int stack)[][] ingredients)
		{
			specialRecipes = new Dictionary<int, int>();
			if (!CraftingConfig.Instance.RandomizeRecipes || !ItemPool.Ready) return;
			HashSet<int> stationTiles = Stations.RequiredTiles();
			foreach (int item in Protection.Upgrades)
			{
				if (!ItemPool.IsSpecial(item)) continue;
				int u = Protection.UpgradeTier(item);
				List<int> candidates = Enumerable.Range(0, results.Length).Where(i => SpecialCandidate(i, item, u, results, ingredients[i], stationTiles))
					.OrderBy(i => (uint)RecipeSystem.RecipeKey(i)).ThenBy(i => i).ToList();
				List<int> close = candidates.Where(i => RecipeTier(i) >= u - 1).ToList();
				if (close.Count > 0) candidates = close;
				if (candidates.Count == 0)
				{
					ModContent.GetInstance<UltimateRandomizer>().Logger.Warn($"No recipe may give {DetRandom.ItemName(item)}: it keeps its vanilla sources only");
					continue;
				}
				int chosen = candidates[DetRandom.Get(RollKey.Special, DetRandom.ItemKey(item)).Next(candidates.Count)];
				results[chosen] = (item, 1);
				specialRecipes[item] = chosen;
			}
		}

		// lucky: false rolls within the current progression (no longer used by Apply: recipes always give their planned result)
		public static (int type, int stack) ResultFor(int i, (int type, int stack)[] ingredients, bool lucky)
		{
			if (!Randomized(i)) return (RecipeSystem.OriginalResult(i), RecipeSystem.OriginalStack(i));
			long value = ingredients.Sum(x => (long)Math.Max(StackRules.ValueOf(x.type), 1) * x.stack);
			return RecipeSystem.RollResult(i, ingredients.Select(x => x.type).ToList(), ingredients.Sum(x => x.stack), lucky, Math.Min(value, RecipeSystem.OriginalIngredientValue(i)));
		}

		private static int[] Types((int type, int stack)[] results) => results.Select(r => r.type).ToArray();

		private static int[] ResultTiers(int[] results) => Enumerable.Range(0, results.Length).Select(i => ResultTier(i, results[i])).ToArray();

		private static (int, int)[][] OriginalIngredients() => Enumerable.Range(0, Count).Select(RecipeSystem.OriginalIngredients).ToArray();

		private static int[][] OriginalGroups() => Enumerable.Range(0, Count).Select(RecipeSystem.OriginalGroups).ToArray();

		// The groups of a recipe with this plan
		public static int[] GroupsOf(RecipePlan plan, int i) => plan.Groups[i] ?? RecipeSystem.OriginalGroups(i);

		private static int[][] PlannedGroups(RecipePlan plan) => Enumerable.Range(0, Count).Select(i => GroupsOf(plan, i)).ToArray();

		private static int[][] OriginalTiles() => Enumerable.Range(0, Count).Select(RecipeSystem.OriginalTiles).ToArray();

		private static RecipePlan Build(bool shells)
		{
			int count = Count;
			CraftingConfig cfg = CraftingConfig.Instance;
			(int, int)[][] ingredients = OriginalIngredients();
			int[][] tiles = OriginalTiles();
			// The results are rolled once, from the original recipe, and everything is planned around them: they are never
			// one of the original ingredients (RollResult) nor a new one (PlanIngredients), and Apply gives exactly them
			RecipePlan result = new()
			{
				Ingredients = new (int, int)[count][],
				IngredientValues = new long[count],
				Tiles = new int[count][],
				Results = ((int, int)[])UnlockedResults().Clone()
			};
			int[] results = Types(result.Results);
			result.ResultTiers = ResultTiers(results);
			result.KeyIngredient = new bool[count];
			result.ResultRepicked = new bool[count];
			result.Twin = new bool[count];
			result.Groups = new int[count][];
			result.IngredientTiers = Enumerable.Repeat(-1, count).ToArray();
			result.PlanOrder = Enumerable.Repeat(-1, count).ToArray();
			result.PlannedKeys = new int[count];
			result.UseCaps = new int[Tiers.Count];
			result.CandidateTiers = Array.Empty<int>();
			result.ShellTiers = Enumerable.Repeat(-1, count).ToArray();
			// Planned as if every result waited for its U (the key ingredients make it so), except the vanilla sources: they are
			// never locked
			int[] avail = Availability(Findability.Tier, results, LockTiers(result), ingredients, tiles, out int[] recipeTiers, null, OriginalGroups());
			// Stations first (from the original recipes only): the ingredient plan needs them to know when each result is made
			if (cfg.RandomizeRecipes && cfg.RandomizeStations) PlanStations(result, results, avail, recipeTiers);
			// The shells are planned by the same rebuilder, after every other recipe (they shift none of their choices)
			shells &= cfg.RandomizeRecipes;
			if (cfg.RandomizeRecipes && (cfg.IngredientMode == IngredientMode.Randomized || shells)) PlanIngredients(result, results, avail, recipeTiers, tiles, shells);
			if (cfg.RandomizeRecipes) LockResults(result, results, ingredients, tiles);
			if (cfg.RandomizeRecipes) LockSpecials(result, results, ingredients, tiles);
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
				// Everything else was planned without the shells (keys, locks, re-picks): what that plan relies on, the shells
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

		// Must this active shell be switched off? When it is doable before its tier (a re-pick made its key earlier), or never;
		// when it gives its item earlier than the plan without shells has it (Black Lens made at U by a randomized recipe and
		// chosen as a key of U: a shell from tier 0 unlocked every recipe it keys); or when it gives an item the plan never has
		// earlier and a recipe using it (directly or by a group) became doable earlier than planned
		private static bool ShellBreaksPlan(RecipePlan result, int i, int[] results, int[] doable, int[] baseAvail, HashSet<int> usedEarly)
		{
			if (doable[i] < result.ShellTiers[i] || doable[i] >= Never) return true;
			int item = results[i];
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
		// (ResultTier). When its ingredients and stations are all had earlier, one replaceable ingredient becomes a key
		// material found exactly at U (findability U, not available earlier by crafting): the recipe is then doable at U,
		// not before, not after. A tier without such a material takes one found at U+1 (later rather than too early). A
		// recipe without a replaceable slot (groups, protected ingredients, vanilla ingredients), or without any key
		// material, has its result re-picked (isolated, RollKey.KeyIngredient) to one it may give at the tier it is doable.
		// Re-picks first (they only make items available earlier), then the keys on the availability they leave
		private static void LockResults(RecipePlan result, int[] results, (int, int)[][] ingredients, int[][] tiles)
		{
			int count = Count;
			(int, int)[][] Planned() => Enumerable.Range(0, count).Select(i => result.Ingredients[i] ?? ingredients[i]).ToArray();
			int[][] plannedTiles = Enumerable.Range(0, count).Select(i => result.Tiles[i] ?? tiles[i]).ToArray();
			// The shells are left out: the locks are the same with the setting on or off
			bool[] noShells = ShellMask(null);
			int[] avail = Availability(Findability.Tier, results, LockTiers(result), Planned(), plannedTiles, out int[] doable, noShells, PlannedGroups(result));
			int repicked = 0;
			for (int i = 0; i < count; i++)
			{
				if (NeedsLock(result, i, doable) && !HasKeySlot(i) && !ItemPool.IsSpecial(results[i]) && RepickResult(result, results, i, doable[i])) repicked++;
			}
			if (repicked > 0) avail = Availability(Findability.Tier, results, LockTiers(result), Planned(), plannedTiles, out doable, noShells, PlannedGroups(result));
			List<int>[,] buckets = FindableBuckets();
			WeightTable weights = CraftingConfig.Instance.IngredientWeights;
			Dictionary<int, int> uses = new();
			int keys = 0, late = 0;
			SortedSet<int> noMaterial = new();
			for (int i = 0; i < count; i++)
			{
				// A result already re-picked back to its original stays vanilla
				if (!NeedsLock(result, i, doable) || result.ResultRepicked[i]) continue;
				int u = result.ResultTiers[i];
				bool later = false;
				int key = HasKeySlot(i) ? PlaceKey(result, results, i, u, buckets, weights, avail, uses, out later) : 0;
				if (key > 0)
				{
					keys++;
					if (later) late++;
					result.KeyIngredient[i] = true;
					continue;
				}
				if (HasKeySlot(i)) noMaterial.Add(u);
				// An upgrade stays the result of its recipe, even without a key (it is then doable a little early)
				if (ItemPool.IsSpecial(results[i])) continue;
				if (RepickResult(result, results, i, doable[i])) repicked++;
			}
			// Checked against the availability of the live recipes (results at once): a key chosen before a later re-pick or
			// key changed what it relies on is chosen again (a material found exactly at U with that availability)
			int rekeyed = 0;
			for (int round = 0; round < 2; round++)
			{
				avail = Availability(Findability.Tier, results, new int[count], Planned(), plannedTiles, out doable, noShells, PlannedGroups(result));
				int again = 0;
				for (int i = 0; i < count; i++)
				{
					if (!NeedsLock(result, i, doable) || result.ResultRepicked[i]) continue;
					if (!HasKeySlot(i) || PlaceKey(result, results, i, result.ResultTiers[i], buckets, weights, avail, uses, out _) <= 0) continue;
					result.KeyIngredient[i] = true;
					again++;
				}
				rekeyed += again;
				if (again == 0) break;
			}
			string topKeys = string.Join(", ", uses.OrderByDescending(p => p.Value).ThenBy(p => p.Key).Take(5).Select(p => $"{DetRandom.ItemName(p.Key)} x{p.Value}"));
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Recipe plan: {keys} key ingredients ({late} from U+1, {rekeyed} chosen again), {repicked} results re-picked; " +
				$"tiers without a key material: {(noMaterial.Count == 0 ? "none" : string.Join(", ", noMaterial))}; most used keys: {topKeys}");
		}

		// The recipes of the life and mana upgrades, last (nothing else is planned around them): each one always has a key
		// ingredient (of the upgrade's tier U; at tier 0, an uncommon material, so a cheap block recipe is no free crystal),
		// added as a new ingredient when no slot may take it, and its ingredients are worth at least the upgrade (stacks
		// raised). Only the RollKey.Special stream: the other recipes stay as they were
		private static void LockSpecials(RecipePlan result, int[] results, (int, int)[][] ingredients, int[][] tiles)
		{
			int count = Count;
			(int, int)[][] planned = Enumerable.Range(0, count).Select(i => result.Ingredients[i] ?? ingredients[i]).ToArray();
			int[][] plannedTiles = Enumerable.Range(0, count).Select(i => result.Tiles[i] ?? tiles[i]).ToArray();
			int[] avail = Availability(Findability.Tier, results, new int[count], planned, plannedTiles, out _, ShellMask(result.Twin), PlannedGroups(result));
			List<int>[,] buckets = FindableBuckets();
			WeightTable weights = CraftingConfig.Instance.IngredientWeights;
			Dictionary<int, int> uses = new();
			foreach ((int item, int i) in specialRecipes.OrderBy(p => p.Key))
			{
				if (i < 0 || i >= count || results[i] != item) continue;
				int u = result.ResultTiers[i];
				Random rng = DetRandom.Get(RollKey.Special, DetRandom.ItemKey(item), RollKey.KeyIngredient);
				if (!result.KeyIngredient[i] && PlaceKey(result, results, i, u, buckets, weights, avail, uses, out _, rng, uncommon: u == 0, append: true) > 0) result.KeyIngredient[i] = true;
				RaiseValue(result, i, StackRules.ValueOf(item));
			}
		}

		// Raises a recipe's ingredient stacks until they are worth at least target: the most valuable stackable ingredients up
		// to the ingredient stack cap first, then every stack in proportion, then the most valuable ones up to their max stack
		private static void RaiseValue(RecipePlan result, int i, long target)
		{
			(int type, int stack)[] current = ((int, int)[])(result.Ingredients[i] ?? RecipeSystem.OriginalIngredients(i)).Clone();
			long Unit(int type) => Math.Max(StackRules.ValueOf(type), 1);
			long Value() => current.Sum(p => Unit(p.type) * p.stack);
			if (current.Length == 0 || Value() >= target) return;
			List<int> byValue = Enumerable.Range(0, current.Length).OrderByDescending(k => Unit(current[k].type)).ThenBy(k => k).ToList();
			void Fill(Func<int, int> cap)
			{
				foreach (int k in byValue)
				{
					long missing = target - Value();
					if (missing <= 0) return;
					(int type, int stack) = current[k];
					long wanted = stack + (missing + Unit(type) - 1) / Unit(type);
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

		// Doable (with what it has now) before the U of its result. A vanilla source (VanillaSource) is never locked
		private static bool NeedsLock(RecipePlan result, int i, int[] doable) =>
			Gated(i) && !RecipeSystem.VanillaDisabled(i) && result.ResultTiers[i] > 0 && doable[i] < result.ResultTiers[i] && !VanillaSource(result, i);

		// A recipe still giving its vanilla result with its vanilla ingredients and stations (or re-picked back to it: nothing
		// else fits) is that item's vanilla source: it gives it as soon as it is doable. A roll that landed on its own result
		// but whose ingredients or stations were replaced is locked like any other (a Luminite Bullet recipe made of early
		// materials would give Luminite Bullets at hardmode's start)
		public static bool VanillaSource(RecipePlan result, int i) => result.Results[i].type == RecipeSystem.OriginalResult(i)
			&& (result.ResultRepicked[i] || (result.Ingredients[i] == null && result.Tiles[i] == null));

		// The tier each recipe gives its result from while planning: U for the results a key will lock, at once for the vanilla
		// sources. With U for a vanilla source, its result looked later than it is, and was chosen as a key of that tier
		private static int[] LockTiers(RecipePlan result) => Enumerable.Range(0, Count).Select(i => VanillaSource(result, i) ? 0 : result.ResultTiers[i]).ToArray();

		// One replaceable ingredient (a material one first: a Zenith keeps its swords) becomes a key material of tier u (else
		// u + 1), weighted by commonness; each material is key of at most KeyUseCap recipes while others are left.
		// rng: RollKey.KeyIngredient of the recipe when null; uncommon: an uncommon material when the tier has one; append: a
		// recipe without a replaceable slot gets the key as a new ingredient
		private static int PlaceKey(RecipePlan result, int[] results, int i, int u, List<int>[,] buckets, WeightTable weights, int[] avail, Dictionary<int, int> uses, out bool later,
			Random rng = null, bool uncommon = false, bool append = false)
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
						if (x == originalResult || x == results[i] || current.Any(p => p.type == x) || x >= avail.Length || avail[x] != t || InGroup(groups, x, out _)) continue;
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
				long others = current.Where((p, k) => k != slot).Sum(p => (long)Math.Max(StackRules.ValueOf(p.type), 1) * p.stack);
				(int, int) placed = (key, ReplacementStack(Math.Max(1, RecipeSystem.OriginalIngredientValue(i) - others), key));
				if (slot >= 0) current[slot] = placed;
				else current = current.Append(placed).ToArray();
				result.Ingredients[i] = current;
				result.IngredientValues[i] = current.Sum(p => (long)Math.Max(StackRules.ValueOf(p.type), 1) * p.stack);
				uses[key] = uses.GetValueOrDefault(key) + 1;
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

		// The result becomes one the recipe may give at the tier it is doable (isolated re-pick: same category, from its tier
		// down); the original result when none fits
		private static bool RepickResult(RecipePlan result, int[] results, int i, int doable)
		{
			if (doable >= Never) return false;
			int ceiling = ItemPool.GatingLimit(doable, doable >= (int)Tier.Hardmode, 0);
			(int type, int stack)[] ingredients = result.Ingredients[i] ?? RecipeSystem.OriginalIngredients(i);
			List<int> types = ingredients.Select(x => x.type).ToList();
			CraftingConfig cfg = CraftingConfig.Instance;
			long value = ingredients.Sum(p => (long)Math.Max(StackRules.ValueOf(p.type), 1) * p.stack);
			int[] keys = { RollKey.Recipe, RecipeSystem.RecipeKey(i), RollKey.KeyIngredient };
			int chosen = RecipeSystem.OriginalResult(i);
			int stack = RecipeSystem.OriginalStack(i);
			for (int attempt = 0; attempt < 4; attempt++)
			{
				int x = ItemPool.Repick(results[i], ItemPool.TierOf(results[i]), ceiling, keys, 16 + attempt);
				if (!ItemPool.Allowed(x, ceiling) || types.Contains(x) || RecipeSystem.InGroups(GroupsOf(result, i), x) || RecipeSystem.InOriginalGroup(i, x)) continue;
				int n = RecipeSystem.ResultStack(i, x, ingredients.Sum(p => p.stack));
				// The value guard still holds
				if (cfg.LimitRecipeValue && (long)StackRules.ValueOf(x) * n > RecipeSystem.MaxResultValue(i, value, cfg.RecipeValueGuard)) continue;
				chosen = x;
				stack = n;
				break;
			}
			results[i] = chosen;
			result.Results[i] = (chosen, stack);
			result.ResultTiers[i] = ResultTier(i, chosen);
			result.ResultRepicked[i] = true;
			return true;
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

		private static void PlanIngredients(RecipePlan result, int[] results, int[] vanillaAvail, int[] recipeTiers, int[][] tiles, bool shells) =>
			new Rebuilder(result, results, vanillaAvail, recipeTiers, tiles, shells).Run();

		// Every touchable recipe is rebuilt from the fuzzy shape of its vanilla one: about as many ingredients, categories biased
		// towards the vanilla ones, tiers blurred around the vanilla ones and pulled towards T, the recipe's tier (never above),
		// one key ingredient found exactly at T, about the vanilla value. Recipes are planned in an acyclic order (by T, then
		// RecipeKey): an item is a candidate once found (Findability) or once a recipe planned earlier makes it (doable with what
		// was had then, at its planned station), so no recipe ever relies on itself, directly or not. Kept: protected
		// ingredients (and their groups), results, stations
		private sealed class Rebuilder
		{
			private readonly RecipePlan result;
			private readonly int[] results;
			private readonly int[] vanillaAvail;
			private readonly int[] recipeTiers;
			private readonly int[][] tiles;
			private readonly bool shells;
			private readonly CraftingConfig cfg = CraftingConfig.Instance;
			private readonly WeightTable weights = CraftingConfig.Instance.IngredientWeights;
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
			private readonly int[] targets;
			private int[] useCaps;
			private int rebuilt, vanillaKept, keyed, crafted, groupSlots, slotsTotal;
			private readonly SortedSet<int> noKey = new();

			public Rebuilder(RecipePlan result, int[] results, int[] vanillaAvail, int[] recipeTiers, int[][] tiles, bool shells)
			{
				this.shells = shells;
				this.result = result;
				this.results = results;
				this.vanillaAvail = vanillaAvail;
				this.recipeTiers = recipeTiers;
				this.tiles = tiles;
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

			// T: the tier the recipe was doable at (never later: its level, RecipeTier), and never before the U of its result
			// (the key ingredient then locks it). A house recipe is built at once
			private int TargetTier(int i)
			{
				if (RecipeSystem.HouseRecipe(i)) return 0;
				int rt = RecipeTier(i);
				int cap = recipeTiers[i] < Never ? Math.Min(rt, recipeTiers[i]) : rt;
				return Tiers.Clamp(Math.Max(cap, result.ResultTiers[i]));
			}

			public void Run()
			{
				int count = Count;
				int[] reaching = new int[Tiers.Count];
				for (int i = 0; i < count; i++)
				{
					targets[i] = Rebuildable(i) ? TargetTier(i) : -1;
					if (targets[i] < 0 || RecipeSystem.HouseRecipe(i)) continue;
					for (int t = 0; t <= targets[i]; t++) reaching[t]++;
				}
				useCaps = reaching.Select(n => Math.Max(MinUseCap, (int)Math.Ceiling(n * MaxUseShare))).ToArray();
				// The other recipes (kept ingredients) are evaluated in the same order, from the tier they were doable at
				List<int> order = Enumerable.Range(0, count).Where(i => !RecipeSystem.VanillaDisabled(i))
					.OrderBy(i => targets[i] >= 0 ? targets[i] : recipeTiers[i] < Never ? recipeTiers[i] : Tiers.Count)
					.ThenBy(i => (uint)RecipeSystem.RecipeKey(i)).ThenBy(i => i).ToList();
				for (int p = 0; p < order.Count; p++)
				{
					int i = order[p];
					result.PlanOrder[i] = p;
					if (targets[i] >= 0) Rebuild(i);
					Made(i);
				}
				result.UseCaps = useCaps;
				string busiest = string.Join(", ", uses.Where(p => p.Key > 0).OrderByDescending(p => p.Value).ThenBy(p => p.Key).Take(5).Select(p => $"{DetRandom.ItemName(p.Key)} x{p.Value}"));
				ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Recipe plan: {rebuilt} recipes rebuilt ({vanillaKept} kept vanilla: no candidate), {keyed} with a key of their tier " +
					$"(none at tiers {(noKey.Count == 0 ? "-" : string.Join(", ", noKey))}), {slotsTotal} new ingredients of which {crafted} crafted and {groupSlots} recipe groups; " +
					$"use caps {string.Join("/", useCaps)}; most used: {busiest}");
				if (shells) PlanShells(order.Count);
				result.CandidateTiers = (int[])clean.Clone();
			}

			// The shells, after every other recipe (their choices and use counts stay as they were), by tier then item: each
			// eligible one gets a station had by its tier T (ShellTier) and ingredients rebuilt like a recipe of tier T with
			// no vanilla shape (ShellIngredientCount, the ingredient weights, about the item's value), its key found exactly at
			// T. A shell of tier T > 0 without a key would be doable earlier: it stays off, like one without any ingredient
			private void PlanShells(int position)
			{
				HashSet<int> stationTiles = Stations.RequiredTiles();
				HashSet<int> guaranteed = GuaranteedSources.Requirements.Select(r => r.Item).ToHashSet();
				List<int> stations = stationTiles.Where(Assignable)
					.OrderBy(t => Stations.Info(t).Kind == StationKind.Basic ? 0 : 1).ThenBy(t => DetRandom.StableHash(TileID.Search.GetName(t))).ToList();
				List<int> shellList = new();
				for (int i = 0; i < Count; i++)
				{
					if (!RecipeSystem.IsShell(i) || !ShellEligible(results[i], stationTiles, guaranteed)) continue;
					// Never before another recipe makes the item: a randomized result that is never found may be the key of the
					// recipes of its tier, and a shell from its own tier would unlock them all early
					int made = real[results[i]];
					result.ShellTiers[i] = targets[i] = made < Tiers.Count ? Math.Max(ShellTier(results[i]), made) : ShellTier(results[i]);
					shellList.Add(i);
				}
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
					else DropShell(result, i);
					byCategory[category] = (eligible, done);
				}
				ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Recipe plan: {planned} of {shellList.Count} shell recipes planned for items no recipe makes" +
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

			// Once a recipe is planned (or kept), its result is had from the tier it is doable at (U for a locked result)
			private void Made(int i)
			{
				int item = results[i];
				if (item <= 0 || item >= real.Length) return;
				(int type, int stack)[] ingredients = result.Ingredients[i] ?? RecipeSystem.OriginalIngredients(i);
				int[] recipeGroups = GroupsOf(result, i);
				int station = 0;
				foreach (int tile in result.Tiles[i] ?? tiles[i]) station = Math.Max(station, TileAvailability(tile, placers, real));
				if (station >= Never) return;
				int locked = VanillaSource(result, i) ? 0 : result.ResultTiers[i];
				int r = IngredientTier(ingredients, recipeGroups, real);
				if (r < Never) real[item] = Math.Min(real[item], Math.Max(Math.Max(r, station), locked));
				// A result LockResults may still re-pick is no candidate: a consumer would be left without a source
				int c = IngredientTier(ingredients, recipeGroups, clean);
				if (c >= Never || Tainted(item) || MayBeRepicked(i)) return;
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

			// The recipes whose result LockResults may re-pick (RepickResult): gated, not an upgrade, not a vanilla source, with a
			// lock tier, and either no slot for a key (as DropRepickable) or no key planned here (LockResults then looks for one,
			// and re-picks the result when it finds none)
			private bool MayBeRepicked(int i) => Gated(i) && !ItemPool.IsSpecial(results[i]) && result.ResultTiers[i] > 0 && !VanillaSource(result, i)
				&& (!HasKeySlot(i) || result.PlannedKeys[i] == 0);

			private int GroupTier(RecipeGroup group)
			{
				int best = Never;
				foreach (int v in group.ValidItems)
				{
					if (v > 0 && v < clean.Length) best = Math.Min(best, clean[v]);
				}
				return best;
			}

			// Does recipe j (that made a candidate) use this item? Then that candidate would loop with a recipe giving the item
			private bool Uses(int j, int item) =>
				(result.Ingredients[j] ?? RecipeSystem.OriginalIngredients(j)).Any(p => p.type == item) || RecipeSystem.InGroups(GroupsOf(result, j), item);

			// A shell (no vanilla recipe) has no shape: ShellIngredientCount ingredients give or take the spread, the ingredient
			// weights alone, tiers pulled towards T; its own stream (RollKey.Uncraftable), so no other recipe's rolls move.
			// Returns whether the recipe got ingredients
			private bool Rebuild(int i)
			{
				bool isShell = RecipeSystem.IsShell(i);
				Random rng = isShell ? DetRandom.Get(RollKey.Uncraftable, DetRandom.ItemKey(results[i])) : DetRandom.Get(RollKey.RecipeIngredient, RecipeSystem.RecipeKey(i));
				int top = targets[i];
				bool house = RecipeSystem.HouseRecipe(i);
				int result0 = results[i];
				int originalResult = RecipeSystem.OriginalResult(i);
				(int type, int stack)[] original = RecipeSystem.OriginalIngredients(i);
				int[] originalGroups = RecipeSystem.OriginalGroups(i);
				HashSet<int> originalTypes = original.Select(o => o.type).ToHashSet();

				// Protected ingredients (and their groups) stay; the others give the shape
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
				int n0 = Math.Clamp((isShell ? ShellIngredientCount : original.Length) + CountDelta(rng, cfg.IngredientCountSpread), 1, Math.Max(1, cfg.IngredientMax));
				int slots = Math.Max(1, n0 - kept.Count);

				// Value: the vanilla value times a random factor, the kept ingredients' value first, the rest split unevenly
				long Unit(int type) => Math.Max(StackRules.ValueOf(type), 1);
				long vanillaValue = RecipeSystem.OriginalIngredientValue(i);
				double target = vanillaValue * (MinValueFactor + rng.NextDouble() * ValueFactorRange);
				double remaining = Math.Max(0, target - kept.Sum(p => (double)Unit(p.type) * p.stack));
				double[] shares = Enumerable.Range(0, slots).Select(_ => 0.5 + rng.NextDouble()).ToArray();

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
				// Category weights: the ingredient weights, plus the vanilla shape
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
					if (!house && shape.Count > 0) catWeight[c] += bonus * shape.Count(g => (int)ItemPool.Info[g].Category == c) / shape.Count;
				}
				List<(int item, float weight)> categories = Enumerable.Range(0, cats).Where(c => catWeight[c] > 0).Select(c => (c, (float)catWeight[c])).ToList();

				HashSet<int> taken = kept.Select(p => p.type).ToHashSet();
				bool Usable(int x) => x != result0 && x != originalResult && !taken.Contains(x) && !RecipeSystem.InGroups(chosenGroups, x)
					&& (!house || (Findability.Tier(x) == 0 && MaterialLike(ItemPool.Info[x].Category) && !originalTypes.Contains(x)))
					&& (source[x] < 0 || !Uses(source[x], result0));
				bool UsableGroup(int k)
				{
					RecipeGroup group = groups[k].group;
					if (chosenGroups.Contains(groups[k].id) || group.ContainsItem(result0) || group.ContainsItem(originalResult) || taken.Any(group.ContainsItem)) return false;
					foreach (int other in chosenGroups)
					{
						if (RecipeGroup.recipeGroups.TryGetValue(other, out RecipeGroup o) && o.ValidItems.Overlaps(group.ValidItems)) return false;
					}
					// Like a single item: no crafted member may be made by a recipe using this result (a loop)
					foreach (int v in group.ValidItems)
					{
						if (v > 0 && v < source.Length && source[v] >= 0 && Uses(source[v], result0)) return false;
					}
					return true;
				}

				List<int> planned = new();
				void Add(int type)
				{
					taken.Add(type);
					planned.Add(type);
				}

				// The key: a material found exactly at T (had no earlier), so the recipe is doable from T, not before
				int key = top > 0 && !house ? PickKey(top, catWeight, Usable, rng) : 0;
				if (key > 0)
				{
					Add(key);
					keyUses[key] = keyUses.GetValueOrDefault(key) + 1;
					result.PlannedKeys[i] = key;
					if (!isShell) keyed++;
				}
				// Without a key, a shell would be doable before the tier its item is found at
				else if (isShell && top > 0) return false;
				else if (top > 0 && !house) noKey.Add(top);

				for (int slot = planned.Count; slot < slots; slot++)
				{
					int picked = 0;
					for (int attempt = 0; attempt < 12 && picked == 0 && categories.Count > 0; attempt++)
					{
						int c = WeightedPick(categories, rng);
						// The tier follows one of the vanilla ingredients, blurred, pulled towards T (a shell: T itself)
						int baseTier = top;
						if (shape.Count > 0)
						{
							int follow = shape[rng.Next(shape.Count)];
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
							if (Usable(x) && uses.GetValueOrDefault(x) < cap) options.Add((x, CommonnessWeight(x)));
						}
						if (!house)
						{
							for (int k = 0; k < groups.Count; k++)
							{
								if ((int)groups[k].category == c && groupTiers[k] == tier && uses.GetValueOrDefault(-(groups[k].id + 1)) < cap && UsableGroup(k))
								{
									options.Add((-(k + 1), CommonnessWeight(groups[k].group.IconicItemId)));
								}
							}
						}
						picked = WeightedPick(options, rng);
					}
					// A house is built at once: any material found at tier 0 then, whatever its uses
					if (picked == 0 && house) picked = HouseMaterial(Usable, rng);
					if (picked == 0) continue;
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
				}
				// The value goes to the slots filled (a slot left empty gives its share to the others)
				double shareTotal = shares.Take(planned.Count).Sum();
				(int type, int stack)[] ingredients = planned.Select((type, k) => (type, ReplacementStack((long)Math.Ceiling(remaining * shares[k] / shareTotal), type)))
					.Concat(kept).ToArray();
				int[] plannedGroups = chosenGroups.ToArray();
				result.Ingredients[i] = ingredients;
				result.Groups[i] = plannedGroups.SequenceEqual(originalGroups) ? null : plannedGroups;
				result.IngredientValues[i] = ingredients.Sum(p => Unit(p.type) * p.stack);
				result.IngredientTiers[i] = top;
				// With the value cap, the result was rolled against the vanilla ingredients' value: the new ones are worth at least that
				if (cfg.LimitRecipeValue && !house && !isShell && (long)StackRules.ValueOf(result.Results[i].type) * result.Results[i].stack > RecipeSystem.MaxResultValue(i, result.IngredientValues[i], cfg.RecipeValueGuard))
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

			// A material found (or made, cleanly) exactly at tier t and had no earlier, weighted by category and commonness
			// (common keys far more often), real materials first; each one keys at most KeyUseCap recipes while others are left
			private int PickKey(int t, double[] catWeight, Func<int, bool> usable, Random rng)
			{
				List<(int item, float weight)> options = new();
				for (int c = 0; c < ItemCategories.Count; c++)
				{
					if (catWeight[c] <= 0) continue;
					foreach (int x in pool[c, t])
					{
						if (real[x] == t && usable(x)) options.Add((x, (float)catWeight[c]));
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
				return WeightedPick(fresh.Select(o => (o.item, o.weight * KeyCommonnessWeight(o.item))).ToList(), rng);
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
			int[] results = Types(UnlockedResults());
			int[] resultTiers = ResultTiers(results);
			DropRepickable(results);
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
			int[] results = Types(UnlockedResults());
			int[] resultTiers = ResultTiers(results);
			DropRepickable(results);
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

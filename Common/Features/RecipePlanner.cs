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

		static RecipePlanner()
		{
			WorldCaches.Reset += Reset;
		}

		public static RecipePlan Get() => plan ??= Build();

		public static void Reset()
		{
			plan = null;
			originalResults = null;
		}

		private static int Count => Math.Min(RecipeSystem.RecipeCount, Recipe.numRecipes);

		// Recipes whose result Apply rolls
		private static bool Randomized(int i) => CraftingConfig.Instance.RandomizeRecipes && RecipeSystem.Touchable(i);

		// U, the progression lock of a recipe's result: the unlocked tier of its true tier. The recipe always gives its
		// result; its ingredients (a key ingredient of tier U) or stations make it wait for U. Only the Coherent mode is gated
		public static int ResultTier(int i, int result)
		{
			if (!Gated(i)) return 0;
			return Tiers.Clamp(ItemPool.UnlockTier(ItemPool.TrueTierOf(result)));
		}

		private static bool Gated(int i) => Randomized(i) && ProgressionConfig.Instance.TierGating && GeneralConfig.Instance.RandomizerMode == RandomizerMode.Coherent;

		// May the planner give this recipe a key ingredient (randomized ingredients, a replaceable original slot)? Without one,
		// a result above what the recipe needs is re-picked
		public static bool HasKeySlot(int i)
		{
			if (CraftingConfig.Instance.IngredientMode != IngredientMode.Randomized || !RecipeSystem.IngredientsTouchable(i)) return false;
			Recipe recipe = Main.recipe[i];
			return RecipeSystem.OriginalIngredients(i).Any(x => IngredientReplaceable(recipe, x.type));
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

		private static bool InGroup(Recipe recipe, int item, out RecipeGroup group)
		{
			foreach (int id in recipe.acceptedGroups)
			{
				if (RecipeGroup.recipeGroups.TryGetValue(id, out group) && group.ContainsItem(item)) return true;
			}
			group = null;
			return false;
		}

		// Availability fixed point: found items, then items crafted from available ingredients at available stations.
		// recipeTiers[i] = tier from which recipe i can be made (ingredients and stations only); recipe i gives results[i]
		// from resultTiers[i]. skip: recipes switched off by the plan (RecipePlan.Twin), counted as absent
		public static int[] Availability(Func<int, int> found, int[] results, int[] resultTiers, (int type, int stack)[][] ingredients, int[][] tiles, out int[] recipeTiers, bool[] skip = null)
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
			bool changed = true;
			for (int pass = 0; pass < 40 && changed; pass++)
			{
				changed = false;
				for (int i = 0; i < count; i++)
				{
					Recipe recipe = Main.recipe[i];
					if (RecipeSystem.VanillaDisabled(i) || (skip != null && i < skip.Length && skip[i])) continue;
					int tier = 0;
					foreach ((int type, _) in ingredients[i])
					{
						int a = InGroup(recipe, type, out RecipeGroup group) ? GroupAvail(group) : (type < avail.Length ? avail[type] : Never);
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
			return results;
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

		private static int[][] OriginalTiles() => Enumerable.Range(0, Count).Select(RecipeSystem.OriginalTiles).ToArray();

		private static RecipePlan Build()
		{
			int count = Count;
			CraftingConfig cfg = CraftingConfig.Instance;
			(int, int)[][] ingredients = OriginalIngredients();
			int[][] tiles = OriginalTiles();
			// The results are rolled once, from the original recipe, and everything is planned around them: they are never
			// one of the original ingredients (RollResult) nor a replaced one (PickIngredient), and Apply gives exactly them
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
			// Planned as if every result waited for its U (the key ingredients make it so), except the vanilla sources: they are
			// never locked
			int[] avail = Availability(Findability.Tier, results, LockTiers(result), ingredients, tiles, out int[] recipeTiers);
			if (cfg.RandomizeRecipes && cfg.IngredientMode == IngredientMode.Randomized) PlanIngredients(result, results, avail, recipeTiers);
			if (cfg.RandomizeRecipes && cfg.RandomizeStations) PlanStations(result, results, avail, recipeTiers);
			if (cfg.RandomizeRecipes) LockResults(result, results, ingredients, tiles);
			if (cfg.RandomizeRecipes) ShareTwinStations(result, ingredients, tiles, avail);
			(int, int)[][] planned = Enumerable.Range(0, count).Select(i => result.Ingredients[i] ?? ingredients[i]).ToArray();
			// The live recipes give their result at once: no result tier any more; the copies are switched off
			result.Availability = Availability(Findability.Tier, results, new int[count], planned,
				Enumerable.Range(0, count).Select(i => result.Tiles[i] ?? tiles[i]).ToArray(), out _, result.Twin);
			return result;
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
			int[] avail = Availability(Findability.Tier, results, LockTiers(result), Planned(), plannedTiles, out int[] doable);
			int repicked = 0;
			for (int i = 0; i < count; i++)
			{
				if (NeedsLock(result, i, doable) && !HasKeySlot(i) && RepickResult(result, results, i, doable[i])) repicked++;
			}
			if (repicked > 0) avail = Availability(Findability.Tier, results, LockTiers(result), Planned(), plannedTiles, out doable);
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
				if (RepickResult(result, results, i, doable[i])) repicked++;
			}
			// Checked against the availability of the live recipes (results at once): a key chosen before a later re-pick or
			// key changed what it relies on is chosen again (a material found exactly at U with that availability)
			int rekeyed = 0;
			for (int round = 0; round < 2; round++)
			{
				avail = Availability(Findability.Tier, results, new int[count], Planned(), plannedTiles, out doable);
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
		// u + 1), weighted by commonness; each material is key of at most KeyUseCap recipes while others are left
		private static int PlaceKey(RecipePlan result, int[] results, int i, int u, List<int>[,] buckets, WeightTable weights, int[] avail, Dictionary<int, int> uses, out bool later)
		{
			later = false;
			Recipe recipe = Main.recipe[i];
			(int type, int stack)[] current = ((int, int)[])(result.Ingredients[i] ?? RecipeSystem.OriginalIngredients(i)).Clone();
			List<int> slots = Enumerable.Range(0, current.Length).Where(k => IngredientReplaceable(recipe, current[k].type)).ToList();
			if (slots.Count == 0) return 0;
			Random rng = DetRandom.Get(RollKey.KeyIngredient, RecipeSystem.RecipeKey(i));
			List<int> materialSlots = slots.Where(k => MaterialLike(ItemPool.Info[current[k].type].Category)).ToList();
			int slot = (materialSlots.Count > 0 ? materialSlots : slots)[rng.Next(materialSlots.Count > 0 ? materialSlots.Count : slots.Count)];
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
						if (x == originalResult || x == results[i] || current.Any(p => p.type == x) || x >= avail.Length || avail[x] != t || InGroup(recipe, x, out _)) continue;
						options.Add((x, categoryWeight * CommonnessWeight(x)));
					}
				}
				if (options.Count == 0) continue;
				// Real materials first (stackable materials, bars, blocks): a bucket, a sponge or a tool only when the tier has none
				List<(int item, float weight)> materials = options.Where(o => KeyMaterial(o.item)).ToList();
				if (materials.Count > 0) options = materials;
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
				current[slot] = (key, ReplacementStack(Math.Max(1, RecipeSystem.OriginalIngredientValue(i) - others), key));
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
				if (!ItemPool.Allowed(x, ceiling) || types.Contains(x) || RecipeSystem.InAcceptedGroup(i, x)) continue;
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
				string groups = string.Join(",", recipe.acceptedGroups.OrderBy(g => g));
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
						// Never a progression station above the recipe's level (it may also be unavailable: bought, or modded)
						List<int> allowed = new();
						for (int k = 0; k < order.Count && k < bound; k++)
						{
							if (orderAvail[k] <= limit && orderTier[k] <= recipeTier) allowed.Add(order[k]);
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

		// Read-only, for the tests (TierCoherence): the materials a replaced ingredient may become, by category and findability
		// tier, exactly as PlanIngredients sees them
		public static List<int>[,] IngredientCandidates() => FindableBuckets();

		// May the planner replace this ingredient of a recipe at all (not a recipe group, randomizable, not a bag or a summon)?
		public static bool IngredientReplaceable(Recipe recipe, int item) =>
			!InGroup(recipe, item, out _) && ItemPool.IsRandomizable(item) && !Unusable.Contains(ItemPool.Info[item].Category);

		// Materials found at each tier (by category), never crafted: a replaced ingredient only relies on raw sources
		private static List<int>[,] FindableBuckets()
		{
			List<int>[,] buckets = new List<int>[ItemCategories.Count, Tiers.Count];
			for (int c = 0; c < ItemCategories.Count; c++)
			{
				for (int t = 0; t < Tiers.Count; t++) buckets[c, t] = new List<int>();
			}
			// Guaranteed stations (basic ones, and progression or themed ones when unprotected) are never new ingredients
			HashSet<int> stationItems = Stations.Basic().Select(s => s.info.Item).ToHashSet();
			stationItems.UnionWith(GuaranteedSources.Requirements.Where(r => r.Station).Select(r => r.Item));
			for (int item = 1; item < ItemPool.Info.Length; item++)
			{
				// Hardmode pair ores: present or not depending on the altars, the ingredient plan must not depend on them. Ores
				// that need a strong pickaxe (and Obsidian): a pickaxe chain could otherwise need its own ore
				if (!ItemPool.IsRandomizable(item) || ItemPool.IsBanned(item) || stationItems.Contains(item) || OreMap.IsHardmodePairOre(item) || MiningOres.Contains(item)) continue;
				ItemCategory category = ItemPool.Info[item].Category;
				if (Unusable.Contains(category)) continue;
				int tier = Findability.Tier(item);
				if (tier < Tiers.Count) buckets[(int)category, tier].Add(item);
			}
			return buckets;
		}

		// Commonness weights of a replaced or key ingredient (MaterialTiers): a small core of common materials carries most
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

		// Anti-abuse: a material replaces at most this share of the recipes that may use it (those whose cap reaches its
		// tier), and never less than MinUseCap
		public const float MaxUseShare = 0.05f;
		public const int MinUseCap = 12;

		// An ingredient of another kind (weapon, potion, accessory...) keeps its category this often: a Zenith gets weapons,
		// a flask potions; materials follow the ingredient weights (mostly materials)
		private const double SameCategoryChance = 0.6;

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

		private static void PlanIngredients(RecipePlan result, int[] results, int[] avail, int[] recipeTiers)
		{
			CraftingConfig cfg = CraftingConfig.Instance;
			List<int>[,] buckets = FindableBuckets();
			HashSet<int> worldMaterials = Findability.WorldMaterialSet();
			// Never later than the recipe was doable: other recipes may rely on its result from that tier
			int[] caps = new int[Count];
			int[] reaching = new int[Tiers.Count];
			for (int i = 0; i < Count; i++)
			{
				int rt = RecipeTier(i);
				caps[i] = recipeTiers[i] < Never ? Math.Min(rt, recipeTiers[i]) : rt;
				if (!RecipeSystem.IngredientsTouchable(i)) continue;
				for (int t = 0; t <= Math.Min(caps[i], Tiers.Max); t++) reaching[t]++;
			}
			int[] useCaps = reaching.Select(n => Math.Max(MinUseCap, (int)Math.Ceiling(n * MaxUseShare))).ToArray();
			Dictionary<int, int> uses = new();
			for (int i = 0; i < Count; i++)
			{
				// Protected results (boss summons, progression stations...) are planned too: only their result and stations stay
				if (!RecipeSystem.IngredientsTouchable(i)) continue;
				Recipe recipe = Main.recipe[i];
				(int type, int stack)[] original = RecipeSystem.OriginalIngredients(i);
				int cap = caps[i];
				Random rng = DetRandom.Get(RollKey.RecipeIngredient, RecipeSystem.RecipeKey(i));
				List<(int type, int stack)> planned = new();
				List<int> picked = new();
				bool changed = false;
				for (int k = 0; k < original.Length; k++)
				{
					(int g, int n) = original[k];
					bool variety = rng.NextDouble() * 100 < cfg.IngredientVarietyChance;
					bool keep = InGroup(recipe, g, out _) || !ItemPool.IsRandomizable(g) || Unusable.Contains(ItemPool.Info[g].Category)
						|| (g < avail.Length && avail[g] <= cap && (worldMaterials.Contains(g) || !variety));
					long value = (long)Math.Max(StackRules.ValueOf(g), 1) * n;
					// With the value cap, the result was rolled against the original ingredients' value: a replacement held by
					// the stack cap must not make the recipe cheaper than that
					long minValue = cfg.LimitRecipeValue ? value : 0;
					int x = keep ? g : PickIngredient(buckets, cap, rng, i, results[i], planned, original, g, value, minValue, uses, useCaps);
					if (x == g)
					{
						planned.Add((g, n));
						continue;
					}
					changed = true;
					picked.Add(x);
					planned.Add((x, ReplacementStack(value, x)));
				}
				if (!changed) continue;
				// Two slots may now ask for the same item
				(int type, int stack)[] merged = planned.GroupBy(p => p.type).Select(grp => (grp.Key, Math.Min(StackRules.MaxStackOf(grp.Key), grp.Sum(p => p.stack)))).ToArray();
				long plannedValue = merged.Sum(p => (long)Math.Max(StackRules.ValueOf(p.Item1), 1) * p.Item2);
				// Merging may still lose value: the original ingredients then stay, so the cap holds
				if (cfg.LimitRecipeValue && (long)StackRules.ValueOf(result.Results[i].type) * result.Results[i].stack > RecipeSystem.MaxResultValue(i, plannedValue, cfg.RecipeValueGuard)) continue;
				result.Ingredients[i] = merged;
				result.IngredientValues[i] = plannedValue;
				foreach (int x in picked) uses[x] = uses.GetValueOrDefault(x) + 1;
			}
		}

		// Stack of a replacement worth the replaced slot (rounded up), within the stack caps
		private static int ReplacementStack(long value, int x)
		{
			long unit = Math.Max(StackRules.ValueOf(x), 1);
			return (int)Math.Clamp((value + unit - 1) / unit, 1, Math.Min(StackRules.MaxStackOf(x), CraftingConfig.Instance.IngredientStackCap));
		}

		// A replacement: its category from the context (the replaced ingredient's own category often when it is no material,
		// else the ingredient weights), its findability tier mostly the cap or just below (earlier tiers now and then: they
		// keep thin tiers supplied), then the item weighted by commonness, among those under their use cap
		private static int PickIngredient(List<int>[,] buckets, int cap, Random rng, int recipe, int luckyResult, List<(int type, int stack)> planned, (int type, int stack)[] original, int current, long slotValue, long minValue, Dictionary<int, int> uses, int[] useCaps)
		{
			WeightTable weights = CraftingConfig.Instance.IngredientWeights;
			int originalResult = RecipeSystem.OriginalResult(recipe);
			Recipe live = Main.recipe[recipe];
			ItemCategory own = ItemPool.Info[current].Category;
			bool Usable(int x) => x != originalResult && x != luckyResult && !planned.Any(p => p.type == x) && !original.Any(o => o.type == x) && !InGroup(live, x, out _)
				&& (minValue <= 0 || (long)Math.Max(StackRules.ValueOf(x), 1) * ReplacementStack(slotValue, x) >= minValue);
			cap = Math.Min(cap, Tiers.Max);
			for (int attempt = 0; attempt < 12; attempt++)
			{
				int category = -1;
				if (!MaterialLike(own) && HasAny(buckets, (int)own, cap) && rng.NextDouble() < SameCategoryChance) category = (int)own;
				else
				{
					// Categories that have something findable at or below the cap
					int total = 0;
					for (int c = 0; c < ItemCategories.Count; c++)
					{
						if (HasAny(buckets, c, cap)) total += weights.Get((ItemCategory)c);
					}
					if (total <= 0) break;
					int roll = rng.Next(total);
					for (int c = 0; c < ItemCategories.Count; c++)
					{
						if (!HasAny(buckets, c, cap)) continue;
						roll -= weights.Get((ItemCategory)c);
						if (roll < 0)
						{
							category = c;
							break;
						}
					}
				}
				if (category < 0) break;
				// Tier: the cap 4, the tier below 2, every earlier tier together 2
				int lower = 0;
				for (int t = 0; t < cap - 1; t++)
				{
					if (buckets[category, t].Count > 0) lower++;
				}
				List<(int item, float weight)> tiers = new();
				for (int t = 0; t <= cap; t++)
				{
					if (buckets[category, t].Count == 0) continue;
					tiers.Add((t, t == cap ? 4f : t == cap - 1 ? 2f : 2f / lower));
				}
				if (tiers.Count == 0) continue;
				int tier = WeightedPick(tiers, rng);
				List<(int item, float weight)> options = buckets[category, tier].Where(x => Usable(x) && uses.GetValueOrDefault(x) < useCaps[tier])
					.Select(x => (x, CommonnessWeight(x))).ToList();
				int chosen = WeightedPick(options, rng);
				if (chosen > 0) return chosen;
			}
			// Nothing suitable: the ingredient stays
			return current;
		}

		private static bool HasAny(List<int>[,] buckets, int category, int cap)
		{
			for (int t = 0; t <= Math.Min(cap, Tiers.Max); t++)
			{
				if (buckets[category, t].Count > 0) return true;
			}
			return false;
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
			int[] avail = Availability(Found, results, resultTiers, ingredients, OriginalTiles(), out _);
			Dictionary<int, int> needed = new();
			for (int i = 0; i < Count; i++)
			{
				if (!RecipeSystem.IngredientsTouchable(i)) continue;
				Recipe recipe = Main.recipe[i];
				int rt = RecipeTier(i);
				foreach ((int g, _) in RecipeSystem.OriginalIngredients(i))
				{
					if (InGroup(recipe, g, out _) || !ItemPool.IsRandomizable(g) || Unusable.Contains(ItemPool.Info[g].Category)) continue;
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
				int[] avail = Availability(Found, results, resultTiers, ingredients, tiles, out _);
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

using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
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
		// What each recipe gives once unlocked: exactly what RecipeSystem.Apply sets from ResultTiers[i] on
		public (int type, int stack)[] Results;
		// Unlocked tier from which recipe i gives Results[i]; before, Apply gives a stand-in of an unlocked tier
		public int[] ResultTiers;
		// Tier at which each item can be obtained (found or crafted), with this plan
		public int[] Availability;
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

		// The progression lock of a recipe's result (see ItemPool.Roll): the roll targets the original's tier and is
		// replaced by a stand-in while that tier is out of reach; a result above that tier waits for its own tier too.
		// Only the Coherent mode is gated. The result waits for its true tier (a raised item is re-picked before it)
		public static int ResultTier(int i, int result)
		{
			ProgressionConfig progression = ProgressionConfig.Instance;
			if (!Randomized(i) || !progression.TierGating || GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent) return 0;
			int tier = Math.Max(ItemPool.TierOf(RecipeSystem.OriginalResult(i)), ItemPool.TrueTierOf(result));
			return ItemPool.UnlockTier(tier);
		}

		// The level of a recipe: its original result, and the level of its stations
		public static int RecipeTier(int i)
		{
			int tier = ItemPool.TierOf(RecipeSystem.OriginalResult(i));
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
		// from resultTiers[i].
		public static int[] Availability(Func<int, int> found, int[] results, int[] resultTiers, (int type, int stack)[][] ingredients, int[][] tiles, out int[] recipeTiers)
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
					if (recipe.Disabled) continue;
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

		// lucky: false gives the stand-in of the current progression (RecipeSystem.Apply, before ResultTiers[i])
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
			int[] avail = Availability(Findability.Tier, results, result.ResultTiers, ingredients, tiles, out int[] recipeTiers);
			if (cfg.RandomizeRecipes && cfg.IngredientMode == IngredientMode.Randomized) PlanIngredients(result, results, avail, recipeTiers);
			if (cfg.RandomizeRecipes && cfg.RandomizeStations) PlanStations(result, results, avail, recipeTiers);
			(int, int)[][] planned = Enumerable.Range(0, count).Select(i => result.Ingredients[i] ?? ingredients[i]).ToArray();
			result.Availability = Availability(Findability.Tier, results, result.ResultTiers, planned,
				Enumerable.Range(0, count).Select(i => result.Tiles[i] ?? tiles[i]).ToArray(), out _);
			return result;
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
				if (!ItemPool.IsRandomizable(item) || stationItems.Contains(item) || OreMap.IsHardmodePairOre(item) || MiningOres.Contains(item)) continue;
				ItemCategory category = ItemPool.Info[item].Category;
				if (Unusable.Contains(category)) continue;
				int tier = Findability.Tier(item);
				if (tier < Tiers.Count) buckets[(int)category, tier].Add(item);
			}
			return buckets;
		}

		private static void PlanIngredients(RecipePlan result, int[] results, int[] avail, int[] recipeTiers)
		{
			CraftingConfig cfg = CraftingConfig.Instance;
			List<int>[,] buckets = FindableBuckets();
			HashSet<int> worldMaterials = Findability.WorldMaterialSet();
			for (int i = 0; i < Count; i++)
			{
				// Protected results (boss summons, progression stations...) are planned too: only their result and stations stay
				if (!RecipeSystem.IngredientsTouchable(i)) continue;
				Recipe recipe = Main.recipe[i];
				(int type, int stack)[] original = RecipeSystem.OriginalIngredients(i);
				int rt = RecipeTier(i);
				// Never later than the recipe was doable: other recipes may rely on its result from that tier
				int cap = recipeTiers[i] < Never ? Math.Min(rt, recipeTiers[i]) : rt;
				Random rng = DetRandom.Get(RollKey.RecipeIngredient, RecipeSystem.RecipeKey(i));
				List<(int type, int stack)> planned = new();
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
					int x = keep ? g : PickIngredient(buckets, cap, rng, i, results[i], planned, original, g, value, minValue);
					if (x == g)
					{
						planned.Add((g, n));
						continue;
					}
					changed = true;
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
			}
		}

		// Stack of a replacement worth the replaced slot (rounded up), within the stack caps
		private static int ReplacementStack(long value, int x)
		{
			long unit = Math.Max(StackRules.ValueOf(x), 1);
			return (int)Math.Clamp((value + unit - 1) / unit, 1, Math.Min(StackRules.MaxStackOf(x), CraftingConfig.Instance.IngredientStackCap));
		}

		private static int PickIngredient(List<int>[,] buckets, int cap, Random rng, int recipe, int luckyResult, List<(int type, int stack)> planned, (int type, int stack)[] original, int current, long slotValue, long minValue)
		{
			WeightTable weights = CraftingConfig.Instance.IngredientWeights;
			int originalResult = RecipeSystem.OriginalResult(recipe);
			bool Usable(int x) => x != originalResult && x != luckyResult && !planned.Any(p => p.type == x) && !original.Any(o => o.type == x)
				&& (minValue <= 0 || (long)Math.Max(StackRules.ValueOf(x), 1) * ReplacementStack(slotValue, x) >= minValue);
			for (int attempt = 0; attempt < 12; attempt++)
			{
				// Categories that have something findable at or below the cap
				int total = 0;
				for (int c = 0; c < ItemCategories.Count; c++)
				{
					if (HasAny(buckets, c, cap)) total += weights.Get((ItemCategory)c);
				}
				if (total <= 0) break;
				int roll = rng.Next(total);
				int category = 0;
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
				int target = Math.Max(0, cap - rng.Next(2));
				for (int t = target; t >= 0; t--)
				{
					List<int> bucket = buckets[category, t];
					if (bucket.Count == 0) continue;
					int x = bucket[rng.Next(bucket.Count)];
					if (Usable(x)) return x;
					break;
				}
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
			// Guaranteed stations count from their tier: an unprotected Hellforge or Crystal Ball has no vanilla source left
			Dictionary<int, int> stations = GuaranteedStationTiers();
			int Found(int item) => stations.TryGetValue(item, out int s) ? Math.Min(s, Findability.BaseTier(item)) : Findability.BaseTier(item);
			int[] avail = Availability(Found, results, ResultTiers(results), ingredients, OriginalTiles(), out _);
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

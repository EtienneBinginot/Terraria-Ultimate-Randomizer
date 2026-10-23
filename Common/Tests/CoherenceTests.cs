using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Tests
{
	// Measurements for the recipe redesign (key ingredients of each result's tier). Always passes: the numbers are in
	// client.log, every line starting with "TierCoherence:". Read-only: the live plans and caches are only read
	public static class CoherenceTests
	{
		public static void Register()
		{
			TestRunner.Register("TierCoherence", TierCoherence);
		}

		private const int Never = Findability.Never;

		private static string Name(int item) => Lang.GetItemNameValue(item);

		private static void Log(string text) => ModContent.GetInstance<UltimateRandomizer>().Logger.Info("TierCoherence: " + text);

		private static string Show(int tier) => tier >= Never ? "never" : tier.ToString();

		// Up to n items spread over the list (its order kept), for variety in the log
		private static List<T> Spread<T>(List<T> items, int n) =>
			items.Count <= n ? items : Enumerable.Range(0, n).Select(k => items[(int)((long)k * items.Count / n)]).ToList();

		private static string Names(IEnumerable<int> items) => string.Join(", ", items.Select(Name));

		private static string ByCategory(IEnumerable<int> items) =>
			string.Join(", ", items.GroupBy(t => ItemPool.Info[t].Category).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}"));

		private static string StationName(int tile)
		{
			List<int> placers = Stations.Placers(tile);
			return placers.Count > 0 ? Name(placers.Min()) : TileID.Search.GetName(tile);
		}

		private static void TierCoherence()
		{
			if (!ItemPool.Ready || RecipeSystem.RecipeCount == 0) Check.Skip("no world loaded");
			CraftingConfig crafting = CraftingConfig.Instance;
			ProgressionConfig progression = ProgressionConfig.Instance;
			Log($"begin: mode {GeneralConfig.Instance.RandomizerMode}, gating {progression.TierGating}, allowance {progression.TierAllowance}, " +
				$"recipes {crafting.RandomizeRecipes}, ingredients {crafting.IngredientMode}, stations {crafting.RandomizeStations}, unlocked tier {ProgressionSystem.UnlockedTier}, " +
				$"hardmode {Main.hardMode}. Findability and availability tiers are unlock tiers (ItemPool.UnlockTier of an item's tier at best)");
			try
			{
				RecipePlan plan = RecipePlanner.Get();
				List<int>[] candidates = Candidates();
				ProgressionStations(plan.Availability);
				Recipes(plan, candidates);
				Raised();
				JackpotTail();
				VanillaMaterials();
			}
			finally
			{
				// JackpotChestContent rolls: no jackpot mark may stay behind
				ItemPool.ClearJackpot();
			}
			Log("end");
		}

		// 1. What a replaced ingredient may become (RecipePlanner.IngredientCandidates: found, never crafted, randomizable, no
		// guaranteed station, no mining ore, no bag or summon; categories of weight 0 left out as PickIngredient does), by
		// findability tier. Natural: its own tier (or the unlock tier of it); later: a lower-tier item only a later random
		// source gives; earlier: found before its tier (gathered, mined, herbs)
		private static List<int>[] Candidates()
		{
			List<int>[,] buckets = RecipePlanner.IngredientCandidates();
			WeightTable weights = CraftingConfig.Instance.IngredientWeights;
			List<int>[] byTier = new List<int>[Tiers.Count];
			for (int t = 0; t < Tiers.Count; t++)
			{
				List<int> list = new();
				int all = 0;
				for (int c = 0; c < ItemCategories.Count; c++)
				{
					all += buckets[c, t].Count;
					if (weights.Get((ItemCategory)c) > 0) list.AddRange(buckets[c, t]);
				}
				list = list.OrderBy(x => ItemPool.Info[x].Category).ThenBy(x => x).ToList();
				byTier[t] = list;
				int natural = list.Count(x => ItemPool.TierOf(x) == t || ItemPool.UnlockTier(ItemPool.TierOf(x)) == t);
				int later = list.Count(x => ItemPool.TierOf(x) < t);
				int earlier = list.Count(x => ItemPool.UnlockTier(ItemPool.TierOf(x)) > t);
				Log($"candidates findable at tier {t}: {list.Count} (all categories {all}); natural {natural}, later than their tier {later}, earlier {earlier} " +
					$"[{ByCategory(list)}]: {Names(Spread(list, 25))}");
			}
			return byTier;
		}

		// 2. Progression stations by catalog tier, with the tier this plan makes them available from; and the other stations
		// vanilla recipes need (the tier RaiseByRecipes gives them: their earliest placing item)
		private static void ProgressionStations(int[] avail)
		{
			List<int> tiles = Stations.RequiredTiles().OrderBy(t => Stations.Info(t).Tier).ThenBy(t => t).ToList();
			for (int t = 0; t < Tiers.Count; t++)
			{
				List<int> at = tiles.Where(tile => Stations.Info(tile).Kind == StationKind.Progression && Stations.Info(tile).Tier == t).ToList();
				if (at.Count == 0) continue;
				Log($"progression stations tier {t}: " + string.Join(", ", at.Select(tile => $"{StationName(tile)}{(Stations.Info(tile).World ? " (world)" : "")} " +
					$"(available {Show(RecipePlanner.StationTier(tile, avail))})")));
			}
			Dictionary<int, List<int>> placers = Stations.PlacersByTile();
			int TrueTier(int item) => ItemPool.IsValid(item) && !ItemPool.Info[item].Excluded ? ItemPool.TrueTierOf(item) : Tiers.Max;
			List<int> others = tiles.Where(tile => Stations.Info(tile).Kind is StationKind.Thematic or StationKind.Unknown).ToList();
			Log("other stations (kind, tier from their placers, available): " + string.Join(", ", others.Select(tile =>
				$"{StationName(tile)} ({Stations.Info(tile).Kind}, {ItemClassifier.StationTier(tile, placers, TrueTier)}, {Show(RecipePlanner.StationTier(tile, avail))})")));
		}

		// 3. Randomized vanilla recipes by the unlock tier U of their planned result: locked at U (an ingredient or station only
		// had from U on), or needing a key ingredient; and how the needing ones would load the candidates of tier U (or U..U+1)
		private static void Recipes(RecipePlan plan, List<int>[] candidates)
		{
			int[] avail = plan.Availability;
			int count = Math.Min(Math.Min(RecipeSystem.RecipeCount, Recipe.numRecipes), plan.Results.Length);
			Dictionary<int, int> stationTiers = new();
			int StationAvail(int tile)
			{
				if (!stationTiers.TryGetValue(tile, out int a)) stationTiers[tile] = a = RecipePlanner.StationTier(tile, avail);
				return a;
			}
			int ItemAvail(Recipe recipe, int type) =>
				ItemClassifier.IngredientChoices(recipe, type).Select(x => x > 0 && x < avail.Length ? avail[x] : Never).DefaultIfEmpty(Never).Min();
			int[] total = new int[Tiers.Count], locked = new int[Tiers.Count], byStation = new int[Tiers.Count], unavailable = new int[Tiers.Count];
			int[] needing = new int[Tiers.Count], needingNoSlot = new int[Tiers.Count], noSlot = new int[Tiers.Count];
			// Uses of each item as an ingredient in the planned recipes today
			Dictionary<int, int> uses = new();
			for (int i = 0; i < count; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (recipe == null || recipe.Disabled || !RecipeSystem.Touchable(i)) continue;
				int result = plan.Results[i].type;
				if (!ItemPool.IsValid(result)) continue;
				int u = Tiers.Clamp(ItemPool.UnlockTier(ItemPool.TrueTierOf(result)));
				(int type, int stack)[] ingredients = plan.Ingredients[i] ?? RecipeSystem.OriginalIngredients(i);
				int[] tiles = plan.Tiles[i] ?? RecipeSystem.OriginalTiles(i);
				foreach ((int type, _) in ingredients) uses[type] = uses.GetValueOrDefault(type) + 1;
				int ingredientTier = ingredients.Select(x => ItemAvail(recipe, x.type)).DefaultIfEmpty(0).Max();
				int stationTier = tiles.Select(StationAvail).DefaultIfEmpty(0).Max();
				// The redesign would change the original recipe: a slot the planner may replace
				bool slot = RecipeSystem.IngredientsTouchable(i) && RecipeSystem.OriginalIngredients(i).Any(x => RecipePlanner.IngredientReplaceable(recipe, x.type));
				total[u]++;
				if (!slot) noSlot[u]++;
				if (Math.Max(ingredientTier, stationTier) >= Never) unavailable[u]++;
				if (Math.Max(ingredientTier, stationTier) >= u)
				{
					locked[u]++;
					if (ingredientTier < u) byStation[u]++;
				}
				else
				{
					needing[u]++;
					if (!slot) needingNoSlot[u]++;
				}
			}
			Log($"recipes: {total.Sum()} randomizable vanilla recipes (Touchable), {needing.Sum()} would need a key ingredient, {noSlot.Sum()} without a replaceable slot " +
				"(all kept, recipe groups, bags or summons)");
			for (int u = 0; u < Tiers.Count; u++)
			{
				if (total[u] == 0) continue;
				List<int> exact = candidates[u];
				List<int> two = u + 1 < Tiers.Count ? exact.Concat(candidates[u + 1]).ToList() : exact;
				Log($"recipes U={u}: {total[u]}, locked {locked[u]} (by a station only {byStation[u]}, never available {unavailable[u]}), need a key {needing[u]} " +
					$"({needingNoSlot[u]} without a replaceable slot), no replaceable slot {noSlot[u]}; {Load(needing[u], exact, uses, $"tier {u}")}; " +
					$"{Load(needing[u], two, uses, u + 1 < Tiers.Count ? $"tiers {u}-{u + 1}" : $"tier {u}")}");
			}
		}

		private static string Load(int recipes, List<int> materials, Dictionary<int, int> uses, string label)
		{
			if (materials.Count == 0) return $"{label}: no candidate";
			int even = (recipes + materials.Count - 1) / materials.Count;
			int busiest = materials.Max(m => uses.GetValueOrDefault(m));
			return $"{label}: {materials.Count} candidates, {(double)recipes / materials.Count:0.0} uses each, max {even} (busiest with today's uses {busiest + even})";
		}

		private struct RecipeEval
		{
			public int Tier;
			public List<(int item, int tier)> Parts;
			public List<(int tile, int tier)> Tiles;
		}

		// 4. Items whose true tier RaiseByRecipes raised: by (tier > true tier), by category, what set it (the ingredient or
		// station at the maximum of the earliest vanilla recipe, recomputed the same way from the final true tiers) and examples
		private static void Raised()
		{
			int[] all = ItemPool.AllTiers;
			int TierFor(int t) => ItemPool.IsValid(t) && !ItemPool.Info[t].Excluded ? ItemPool.TrueTierOf(t) : t > 0 && t < all.Length ? all[t] : Tiers.Max;
			List<int> raised = Enumerable.Range(1, ItemPool.Info.Length - 1).Where(t => !ItemPool.Info[t].Excluded && ItemPool.TrueTierOf(t) > ItemPool.TierOf(t)).ToList();
			Log($"raised: {raised.Count} items; tier > true tier: " + string.Join(", ", raised.GroupBy(t => (ItemPool.TierOf(t), ItemPool.TrueTierOf(t)))
				.OrderBy(g => g.Key.Item1).ThenBy(g => g.Key.Item2).Select(g => $"{g.Key.Item1}>{g.Key.Item2} {g.Count()}")));
			Log("raised by category: " + ByCategory(raised));
			HashSet<int> set = raised.ToHashSet();
			Dictionary<int, List<int>> placers = Stations.PlacersByTile();
			int stored = RecipeSystem.RecipeCount;
			RecipeEval Evaluate(int i)
			{
				Recipe recipe = Main.recipe[i];
				IEnumerable<int> ingredients = i < stored ? RecipeSystem.OriginalIngredients(i).Select(x => x.type) : recipe.requiredItem.Select(x => x.type);
				RecipeEval eval = new() { Parts = new(), Tiles = new() };
				foreach (int type in ingredients)
				{
					if (type <= 0 || type >= all.Length) continue;
					int[] choices = ItemClassifier.IngredientChoices(recipe, type).Where(t => t > 0 && t < all.Length).ToArray();
					int best = choices.Length == 0 ? type : choices.OrderBy(TierFor).ThenBy(t => t).First();
					eval.Parts.Add((best, choices.Length == 0 ? Tiers.Max : TierFor(best)));
				}
				IEnumerable<int> tiles = i < stored ? RecipeSystem.OriginalTiles(i) : recipe.requiredTile;
				foreach (int tile in tiles.Where(t => t >= 0).Distinct()) eval.Tiles.Add((tile, ItemClassifier.StationTier(tile, placers, TierFor)));
				eval.Tier = eval.Parts.Select(p => p.tier).Concat(eval.Tiles.Select(t => t.tier)).DefaultIfEmpty(0).Max();
				return eval;
			}
			Dictionary<int, RecipeEval> earliest = new();
			for (int i = 0; i < Recipe.numRecipes; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (recipe == null || recipe.createItem == null) continue;
				int result = i < stored ? RecipeSystem.OriginalResult(i) : recipe.createItem.type;
				if (!set.Contains(result)) continue;
				List<int> ingredients = i < stored ? RecipeSystem.OriginalIngredients(i).Select(x => x.type).ToList() : recipe.requiredItem.Select(x => x.type).ToList();
				if (!ItemClassifier.SetsTier(recipe, ingredients)) continue;
				RecipeEval eval = Evaluate(i);
				if (!earliest.TryGetValue(result, out RecipeEval other) || eval.Tier < other.Tier) earliest[result] = eval;
			}
			// Vanilla loot sources (what RaiseByRecipes skips): drops, bags, shops
			HashSet<int> loot = new(Obtainability.ShopItems);
			foreach (var drops in LootSnapshot.Npcs.Values) loot.UnionWith(drops.Select(d => d.itemId));
			foreach (var drops in LootSnapshot.Items.Values) loot.UnionWith(drops.Select(d => d.itemId));
			Dictionary<int, int> culpritItems = new(), culpritTiles = new();
			int mismatch = 0, byGathered = 0, byStation = 0, byOneTier = 0;
			List<string> mismatches = new();
			foreach (int item in raised)
			{
				if (ItemPool.TrueTierOf(item) - ItemPool.TierOf(item) == 1) byOneTier++;
				if (!earliest.TryGetValue(item, out RecipeEval eval))
				{
					mismatch++;
					if (mismatches.Count < 8) mismatches.Add($"{Name(item)} (no recipe found)");
					continue;
				}
				if (eval.Tier != ItemPool.TrueTierOf(item))
				{
					mismatch++;
					if (mismatches.Count < 8) mismatches.Add($"{Name(item)} (true tier {ItemPool.TrueTierOf(item)}, recomputed {eval.Tier})");
				}
				List<int> items = eval.Parts.Where(p => p.tier == eval.Tier).Select(p => p.item).Distinct().ToList();
				List<int> tiles = eval.Tiles.Where(t => t.tier == eval.Tier).Select(t => t.tile).Distinct().ToList();
				foreach (int c in items) culpritItems[c] = culpritItems.GetValueOrDefault(c) + 1;
				foreach (int c in tiles) culpritTiles[c] = culpritTiles.GetValueOrDefault(c) + 1;
				if (items.Count == 0 && tiles.Count > 0) byStation++;
				if (items.Count > 0 && items.All(c => Findability.IsWorldMaterial(c) || OreMap.IsOre(c))) byGathered++;
			}
			Log($"raised: {byOneTier} by one tier only; {byStation} set by a station alone; {byGathered} set only by gathered or mined materials; " +
				$"{mismatch} not recomputed exactly{(mismatches.Count > 0 ? ": " + string.Join(", ", mismatches) : "")}");
			string Culprit(int c) => $"{Name(c)} x{culpritItems[c]} (tier {TierFor(c)}, rarity tier {(ItemPool.IsValid(c) ? ItemPool.Info[c].RarityTier : -1)}, " +
				$"found {Show(Findability.Tier(c))}{(loot.Contains(c) ? ", dropped/sold" : "")}{(Findability.IsWorldMaterial(c) ? ", gathered" : "")}" +
				$"{(OreMap.IsOre(c) ? ", ore" : "")}{(Obtainability.RecipeResults.Contains(c) ? ", crafted" : "")}{(ItemClassifier.HasKnownTier(c) ? ", KnownTiers" : "")})";
			List<int> topItems = culpritItems.OrderByDescending(p => p.Value).ThenBy(p => p.Key).Select(p => p.Key).Take(40).ToList();
			for (int k = 0; k < topItems.Count; k += 10) Log("raised by ingredient: " + string.Join("; ", topItems.Skip(k).Take(10).Select(Culprit)));
			Log("raised by station: " + string.Join("; ", culpritTiles.OrderByDescending(p => p.Value).ThenBy(p => p.Key)
				.Select(p => $"{StationName(p.Key)} x{p.Value} ({Stations.Info(p.Key).Kind}, tier {ItemClassifier.StationTier(p.Key, placers, TierFor)})")));
			string Example(int item)
			{
				string head = $"{Name(item)} [{ItemPool.Info[item].Category}] {ItemPool.TierOf(item)}>{ItemPool.TrueTierOf(item)}";
				if (!earliest.TryGetValue(item, out RecipeEval eval)) return head + ": no recipe";
				return head + ": " + string.Join(" + ", eval.Parts.Select(p => $"{Name(p.item)}({p.tier})")) +
					(eval.Tiles.Count > 0 ? " @ " + string.Join(" + ", eval.Tiles.Select(t => $"{StationName(t.tile)}({t.tier})")) : "");
			}
			List<int> examples = Spread(raised, 40);
			for (int k = 0; k < examples.Count; k += 4) Log("raised example: " + string.Join(" | ", examples.Skip(k).Take(4).Select(Example)));
		}

		// 5. Jackpot chests, by the chest tiers that may hold one (pre-hardmode and unlocked): the tier WildTier rolls for each
		// slot (cap + k, k WildTierDecay times rarer each step), the true tier of what the slot really holds, and the theory
		private static void JackpotTail()
		{
			if (GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent)
			{
				Log("jackpot chests: only measured in Coherent mode");
				return;
			}
			float decay = ProgressionConfig.Instance.WildTierDecay;
			Log($"jackpot chests: decay {decay}, tier window {LootConfig.Instance.TierWindow}, {LootConfig.Instance.JackpotChestChance}% of the eligible chests " +
				"(pre-hardmode and unlocked: surface and underground chests; generated shadow chests are locked)");
			(string kind, int tier)[] kinds = { ("surface wooden", 0), ("underground wooden, gold and themed", 1), ("shadow (opened)", 3) };
			foreach ((string kind, int chestTier) in kinds)
			{
				int cap = ChestSystem.ChestCap(chestTier);
				int steps = Tiers.Max - cap;
				if (steps <= 0) continue;
				int[] rolled = new int[steps + 1], held = new int[steps + 1];
				int chests = 200, slots = 0, items = 0, hardmode = 0, below = 0;
				for (int k = 0; k < chests; k++)
				{
					int x = 300 + k * 11, y = 150 + chestTier * 37 + k % 7;
					// The same draws as JackpotChestContent: the number of slots, then each slot's tier
					int count = DetRandom.Get(RollKey.JackpotChest, x, y).Next(2, 5);
					int[] tiers = Enumerable.Range(0, count).Select(index => ItemPool.WildTier(cap, 100f, decay, DetRandom.Get(RollKey.JackpotChest, x, y, index))).ToArray();
					List<int> content = ChestSystem.JackpotChestContent(x, y, chestTier).Where(e => e.gap > 0).Select(e => e.type).ToList();
					slots += count;
					items += content.Count;
					foreach (int t in tiers) rolled[Math.Clamp(t - cap, 0, steps)]++;
					foreach (int item in content)
					{
						held[Math.Clamp(ItemPool.TrueTierOf(item) - cap, 0, steps)]++;
						if (ItemPool.TrueTierOf(item) >= (int)Tier.Hardmode) hardmode++;
					}
					// Slots and items line up only when no slot came back empty
					if (content.Count == count)
					{
						for (int j = 0; j < count; j++)
						{
							if (ItemPool.TrueTierOf(content[j]) < tiers[j]) below++;
						}
					}
				}
				string Histogram(int[] h, int n) => string.Join(", ", Enumerable.Range(1, steps).Where(g => h[g] > 0 || g <= 3).Select(g => $"+{g} {h[g]} ({100.0 * h[g] / Math.Max(1, n):0.#}%)"));
				double ratio = 1.0 / Math.Max(1.01, decay), sum = Enumerable.Range(1, steps).Sum(g => Math.Pow(ratio, g));
				string theory = string.Join(", ", Enumerable.Range(1, Math.Min(steps, 4)).Select(g => $"+{g} {100 * Math.Pow(ratio, g) / sum:0.#}%"));
				Log($"jackpot {kind} (chest tier {chestTier}, cap {cap}): {chests} chests, {items} items for {slots} slots ({slots - items} empty), " +
					$"hardmode {hardmode} ({100.0 * hardmode / Math.Max(1, items):0.#}%); rolled tier: {Histogram(rolled, slots)}; held true tier: {Histogram(held, items)}; " +
					$"theory: {theory}; items below their rolled tier {below}");
			}
		}

		// 6. Vanilla's own tiers: materials and ores or bars (randomizable, no vanilla recipe makes them) by ItemPool.TierOf
		private static void VanillaMaterials()
		{
			HashSet<int> crafted = new();
			for (int i = 0; i < Math.Min(RecipeSystem.RecipeCount, Recipe.numRecipes); i++)
			{
				if (!RecipeSystem.IsModded(i) && ItemClassifier.IsVanillaRecipe(Main.recipe[i])) crafted.Add(RecipeSystem.OriginalResult(i));
			}
			List<int> materials = Enumerable.Range(1, ItemPool.Info.Length - 1)
				.Where(t => ItemPool.IsRandomizable(t) && ItemPool.Info[t].Category is ItemCategory.Material or ItemCategory.OreBar && !crafted.Contains(t)).ToList();
			for (int t = 0; t < Tiers.Count; t++)
			{
				List<int> list = materials.Where(m => ItemPool.TierOf(m) == t).ToList();
				Log($"vanilla materials tier {t}: {list.Count} [{ByCategory(list)}]: {Names(Spread(list, 25))}");
			}
			Log("vanilla materials by unlock tier (same units as the candidates): " + string.Join(", ", Enumerable.Range(0, Tiers.Count)
				.Select(u => $"{u}: {materials.Count(m => ItemPool.UnlockTier(ItemPool.TierOf(m)) == u)}")));
		}
	}
}

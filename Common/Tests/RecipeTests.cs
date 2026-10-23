using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Tests
{
	// Randomized ingredients and stations of recipes
	public static class RecipeTests
	{
		public static void Register()
		{
			TestRunner.Register("IngredientReplacementsFindable", IngredientReplacementsFindable);
			TestRunner.Register("RecipesDoable", RecipesDoable);
			TestRunner.Register("AvailabilityConsistent", AvailabilityConsistent);
			TestRunner.Register("NoSelfIngredient", NoSelfIngredient);
			TestRunner.Register("ResultNotInGroup", ResultNotInGroup);
			TestRunner.Register("IngredientValueCoherent", IngredientValueCoherent);
			TestRunner.Register("ProtectedIngredientsKept", ProtectedIngredientsKept);
			TestRunner.Register("RecipeGroupsSane", RecipeGroupsSane);
			TestRunner.Register("RecipePlanDeterminism", RecipePlanDeterminism);
			TestRunner.Register("UntouchedRecipesIntact", UntouchedRecipesIntact);
			TestRunner.Register("DecraftSafeWithIngredients", DecraftSafeWithIngredients);
			TestRunner.Register("VanillaModeGuaranteed", VanillaModeGuaranteed);
			TestRunner.Register("StationDag", StationDag);
			TestRunner.Register("StationTierRespected", StationTierRespected);
			TestRunner.Register("HandCraftKept", HandCraftKept);
			TestRunner.Register("StationsVaried", StationsVaried);
			TestRunner.Register("AppliedResultsMatchPlan", AppliedResultsMatchPlan);
			TestRunner.Register("StationPlanFromOriginals", StationPlanFromOriginals);
			TestRunner.Register("RealCrafting", RealCrafting);
			TestRunner.Register("PickaxePerMiningLevel", PickaxePerMiningLevel);
			TestRunner.Register("RecipeRandomizationShare", RecipeRandomizationShare);
			TestRunner.Register("RecipeValueFallback", RecipeValueFallback);
			TestRunner.Register("CraftedValueFromIngredients", CraftedValueFromIngredients);
			TestRunner.Register("FurnitureSurprise", FurnitureSurprise);
			TestRunner.Register("ProtectedRecipesDoable", ProtectedRecipesDoable);
			TestRunner.Register("RecipeAlwaysFinalResult", RecipeAlwaysFinalResult);
			TestRunner.Register("RecipeKeyIngredient", RecipeKeyIngredient);
		}

		private static int Count => Math.Min(RecipeSystem.RecipeCount, Recipe.numRecipes);

		private static string Name(int item) => Lang.GetItemNameValue(item);

		private static void RequireApplied()
		{
			if (!RecipeSystem.Applied) Check.Skip("recipes are not randomized");
		}

		private static void RequireRandomIngredients()
		{
			RequireApplied();
			if (CraftingConfig.Instance.IngredientMode != IngredientMode.Randomized) Check.Skip("ingredients are vanilla");
		}

		// A replaced ingredient is always found (not crafted) by the recipe's level; a key ingredient by the U of its result
		// (U + 1 when U has no key material)
		private static void IngredientReplacementsFindable()
		{
			RequireRandomIngredients();
			RecipePlan plan = RecipePlanner.Get();
			List<string> failures = new();
			int replaced = 0;
			for (int i = 0; i < Count; i++)
			{
				if (plan.Ingredients[i] == null) continue;
				int rt = RecipePlanner.RecipeTier(i);
				if (plan.KeyIngredient[i]) rt = Math.Max(rt, Math.Min(plan.ResultTiers[i] + 1, Tiers.Max));
				HashSet<int> originals = RecipeSystem.OriginalIngredients(i).Select(o => o.type).ToHashSet();
				foreach ((int type, _) in plan.Ingredients[i])
				{
					if (originals.Contains(type)) continue;
					replaced++;
					if (Findability.Tier(type) > rt) failures.Add($"{Name(type)} in {Name(RecipeSystem.OriginalResult(i))}: found at tier {Findability.Tier(type)} > {rt}");
				}
			}
			Check.That(replaced > 0, "no ingredient was replaced");
			Check.All(failures, "Replaced ingredients can't be found in time");
		}

		// Most recipes can be made at their level (some vanilla recipes already need later materials)
		private static void RecipesDoable()
		{
			RequireApplied();
			RecipePlan plan = RecipePlanner.Get();
			Dictionary<int, List<int>> placers = Placers();
			int touchable = 0;
			int doable = 0;
			List<string> examples = new();
			for (int i = 0; i < Count; i++)
			{
				if (!RecipeSystem.Touchable(i)) continue;
				touchable++;
				Recipe recipe = Main.recipe[i];
				int rt = Math.Max(RecipePlanner.RecipeTier(i), plan.ResultTiers[i]);
				bool ok = recipe.requiredItem.All(item => item.type < plan.Availability.Length && (plan.Availability[item.type] <= rt || GroupAvailable(recipe, item.type, plan, rt)))
					&& recipe.requiredTile.All(tile => StationTier(tile, placers, plan.Availability) <= rt);
				if (ok) doable++;
				else if (examples.Count < 5) examples.Add(Name(RecipeSystem.OriginalResult(i)));
			}
			double ratio = touchable == 0 ? 1 : doable / (double)touchable;
			Check.That(ratio >= 0.85, $"only {ratio:P0} of the recipes can be made at their level (e.g. {string.Join(", ", examples)})");
		}

		// Items placing each tile, built here independently of the planner
		private static Dictionary<int, List<int>> Placers()
		{
			Dictionary<int, List<int>> placers = new();
			foreach ((int type, Item item) in ContentSamples.ItemsByType)
			{
				if (item.createTile < TileID.Dirt) continue;
				if (!placers.TryGetValue(item.createTile, out List<int> list)) placers[item.createTile] = list = new List<int>();
				list.Add(type);
			}
			return placers;
		}

		// A station is usable from its cheapest placing item; progression stations whose protected placer no recipe makes from
		// their tier (bought...); tiles no item places (Demon Altar) are part of the world
		private static int StationTier(int tile, Dictionary<int, List<int>> placers, int[] avail)
		{
			if (!placers.TryGetValue(tile, out List<int> items)) return 0;
			Stations.StationInfo info = Stations.Info(tile);
			// A protected progression station no recipe makes (bought, found) is usable from its tier; a crafted or
			// unprotected one through its recipe or its guaranteed drops (avail)
			bool vanillaSource = items.Any(i => ItemPool.IsValid(i) && ItemPool.Info[i].Protected && !Obtainability.RecipeResults.Contains(i));
			int best = info.Kind == StationKind.Progression && vanillaSource ? info.Tier : Findability.Never;
			foreach (int item in items)
			{
				if (item < avail.Length) best = Math.Min(best, avail[item]);
			}
			return best;
		}

		private static bool GroupAvailable(Recipe recipe, int item, RecipePlan plan, int tier)
		{
			foreach (int id in recipe.acceptedGroups)
			{
				if (RecipeGroup.recipeGroups.TryGetValue(id, out RecipeGroup group) && group.ContainsItem(item) && group.ValidItems.Any(v => v < plan.Availability.Length && plan.Availability[v] <= tier)) return true;
			}
			return false;
		}

		// The plan's availability is the one of the applied recipes: recomputed here with a naive fixed point (written
		// independently of the planner) over the live ingredients and stations and the planned results
		private static void AvailabilityConsistent()
		{
			RequireApplied();
			RecipePlan plan = RecipePlanner.Get();
			Dictionary<int, List<int>> placers = Placers();
			int[] naive = new int[plan.Availability.Length];
			for (int t = 1; t < naive.Length; t++) naive[t] = Findability.Tier(t);
			bool changed = true;
			int passes = 0;
			while (changed && passes++ < 200)
			{
				changed = false;
				for (int i = 0; i < Count; i++)
				{
					Recipe recipe = Main.recipe[i];
					if (recipe.Disabled) continue;
					int tier = 0;
					foreach (Item item in recipe.requiredItem)
					{
						int a = item.type < naive.Length ? naive[item.type] : Findability.Never;
						foreach (int id in recipe.acceptedGroups)
						{
							if (!RecipeGroup.recipeGroups.TryGetValue(id, out RecipeGroup group) || !group.ContainsItem(item.type)) continue;
							a = group.ValidItems.Where(v => v < naive.Length).Select(v => naive[v]).DefaultIfEmpty(Findability.Never).Min();
							break;
						}
						tier = Math.Max(tier, a);
					}
					foreach (int tile in recipe.requiredTile) tier = Math.Max(tier, StationTier(tile, placers, naive));
					if (tier == Findability.Never) continue;
					int result = plan.Results[i].type;
					// Recipes give their result as soon as they are doable
					int at = tier;
					if (result > 0 && result < naive.Length && at < naive[result])
					{
						naive[result] = at;
						changed = true;
					}
				}
			}
			List<string> differences = Enumerable.Range(1, naive.Length - 1).Where(t => naive[t] != plan.Availability[t])
				.Select(t => $"{Name(t)}: {Show(naive[t])} recomputed, {Show(plan.Availability[t])} in the plan").Take(5).ToList();
			Check.All(differences, "Availability differs from a recomputation");
			int found = Enumerable.Range(1, plan.Availability.Length - 1).Count(t => plan.Availability[t] < Findability.Never);
			int raw = Enumerable.Range(1, plan.Availability.Length - 1).Count(t => Findability.Tier(t) < Findability.Never);
			Check.That(found >= raw, $"crafting makes fewer items available ({found}) than finding them ({raw})");
			List<string> failures = Enumerable.Range(1, plan.Availability.Length - 1)
				.Where(t => plan.Availability[t] > Findability.Tier(t))
				.Select(t => Name(t)).Take(5).ToList();
			Check.All(failures, "Items less available than their raw sources");
		}

		private static void NoSelfIngredient()
		{
			RequireApplied();
			List<string> failures = new();
			for (int i = 0; i < Count; i++)
			{
				Recipe recipe = Main.recipe[i];
				int result = recipe.createItem.type;
				if (result == RecipeSystem.OriginalResult(i)) continue;
				if (recipe.requiredItem.Any(item => item.type == result)) failures.Add(Name(result));
			}
			Check.All(failures, "Recipes using their own result");
		}

		// A randomized result is never an item one of the recipe's groups accepts (any Wood -> N Boreal Wood would loop)
		private static void ResultNotInGroup()
		{
			RequireApplied();
			RecipePlan plan = RecipePlanner.Get();
			List<string> failures = new();
			int withGroups = 0;
			for (int i = 0; i < Count; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (recipe.acceptedGroups.Count == 0) continue;
				withGroups++;
				foreach (int result in new[] { plan.Results[i].type, recipe.createItem.type })
				{
					if (result == RecipeSystem.OriginalResult(i)) continue;
					foreach (int id in recipe.acceptedGroups)
					{
						if (RecipeGroup.recipeGroups.TryGetValue(id, out RecipeGroup group) && group.ValidItems.Contains(result)) failures.Add($"{Name(RecipeSystem.OriginalResult(i))} gives {Name(result)}, accepted by {group.GetText()}");
					}
				}
			}
			Check.That(withGroups > 0, "no recipe uses a group");
			Check.All(failures, "Recipes giving an item their group accepts");
		}

		private static void IngredientValueCoherent()
		{
			RequireRandomIngredients();
			RecipePlan plan = RecipePlanner.Get();
			int cap = CraftingConfig.Instance.IngredientStackCap;
			List<string> failures = new();
			for (int i = 0; i < Count; i++)
			{
				if (plan.Ingredients[i] == null) continue;
				long before = RecipeSystem.OriginalIngredientValue(i);
				long after = plan.IngredientValues[i];
				// A stack held by the cap may lose value; a replacement's stack is rounded up, so each new ingredient may add up
				// to its own unit value (a x1 of something pricier than what it replaced). Nothing else excuses a change
				bool capped = plan.Ingredients[i].Any(p => p.stack >= Math.Min(cap, StackRules.MaxStackOf(p.type)));
				HashSet<int> originals = RecipeSystem.OriginalIngredients(i).Select(o => o.type).ToHashSet();
				long roundUp = plan.Ingredients[i].Where(p => !originals.Contains(p.type)).Sum(p => (long)Math.Max(StackRules.ValueOf(p.type), 1));
				if ((!capped && after < before / 2) || after > before * 2 + Item.silver + roundUp) failures.Add($"{Name(RecipeSystem.OriginalResult(i))}: {before} -> {after}");
			}
			Check.All(failures, "Ingredient values changed too much");
		}

		private static void ProtectedIngredientsKept()
		{
			RequireApplied();
			List<string> failures = new();
			for (int i = 0; i < Count; i++)
			{
				Recipe recipe = Main.recipe[i];
				foreach ((int type, _) in RecipeSystem.OriginalIngredients(i))
				{
					if (ItemPool.IsRandomizable(type)) continue;
					if (!recipe.requiredItem.Any(item => item.type == type)) failures.Add($"{Name(type)} removed from {Name(RecipeSystem.OriginalResult(i))}");
				}
			}
			Check.All(failures, "Protected ingredients replaced");
		}

		private static void RecipeGroupsSane()
		{
			RequireApplied();
			List<string> failures = new();
			for (int i = 0; i < Count; i++)
			{
				Recipe recipe = Main.recipe[i];
				foreach (int id in recipe.acceptedGroups)
				{
					if (!RecipeGroup.recipeGroups.TryGetValue(id, out RecipeGroup group)) continue;
					if (!recipe.requiredItem.Any(item => group.ContainsItem(item.type))) failures.Add($"{Name(recipe.createItem.type)}: group {id} unused");
				}
			}
			Check.All(failures, "Recipe groups without ingredient");
		}

		private static string Describe(RecipePlan plan) => string.Join("|", Enumerable.Range(0, Count).Select(i =>
			(plan.Ingredients[i] == null ? "-" : string.Join(",", plan.Ingredients[i].Select(p => p.type + "x" + p.stack)))
			+ "@" + (plan.Tiles[i] == null ? "-" : string.Join(",", plan.Tiles[i]))
			+ "=" + plan.Results[i].type + "x" + plan.Results[i].stack + "/" + plan.ResultTiers[i]));

		private static void RecipePlanDeterminism()
		{
			RequireApplied();
			string first = Describe(RecipePlanner.Get());
			RecipePlanner.Reset();
			Check.Equal(first.GetHashCode(), Describe(RecipePlanner.Get()).GetHashCode(), "recipe plan after a rebuild");
		}

		// Protected recipes (mining chain, protected results, modded recipes) keep their stations; modded recipes and
		// non-randomizable results keep their ingredients too (protected results get new ones)
		private static void UntouchedRecipesIntact()
		{
			RequireApplied();
			List<string> failures = new();
			for (int i = 0; i < Count; i++)
			{
				if (RecipeSystem.Touchable(i)) continue;
				Recipe recipe = Main.recipe[i];
				if (!RecipeSystem.IngredientsTouchable(i) && !recipe.requiredItem.Select(item => (item.type, item.stack)).SequenceEqual(RecipeSystem.OriginalIngredients(i))) failures.Add($"{Name(recipe.createItem.type)} ingredients");
				if (!recipe.requiredTile.SequenceEqual(RecipeSystem.OriginalTiles(i))) failures.Add($"{Name(recipe.createItem.type)} stations");
			}
			Check.All(failures, "Protected recipes changed");
		}

		private static void DecraftSafeWithIngredients()
		{
			RequireRandomIngredients();
			RecipePlan plan = RecipePlanner.Get();
			List<string> failures = new();
			int[][] decraft = ItemID.Sets.CraftingRecipeIndices;
			for (int type = 1; type < decraft.Length; type++)
			{
				if (decraft[type] == null) continue;
				foreach (int index in decraft[type])
				{
					if (index < Count && plan.Ingredients[index] != null && plan.IngredientValues[index] > RecipeSystem.OriginalIngredientValue(index) * 5 / 4)
					{
						failures.Add(Name(type));
					}
				}
			}
			Check.All(failures, "Decrafting gives back more valuable ingredients");
		}

		private static void VanillaModeGuaranteed()
		{
			CraftingConfig cfg = CraftingConfig.Instance;
			if (!cfg.RandomizeRecipes || cfg.IngredientMode != IngredientMode.Vanilla) Check.Skip("ingredients are randomized");
			List<string> failures = new();
			foreach (GuaranteedSources.Requirement requirement in RecipePlanner.VanillaRequirements())
			{
				if (!GuaranteedSources.HostsOf(requirement.Item).Any()) failures.Add($"{Name(requirement.Item)} (tier {requirement.Tier})");
			}
			Check.All(failures, "Vanilla ingredients without a source");
		}

		// Recipes making a station never need, directly or not, that same station
		private static void StationDag()
		{
			RequireApplied();
			// The station sets of the recipes making each station
			Dictionary<int, List<int[]>> recipesOf = new();
			for (int i = 0; i < Count; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (recipe.Disabled || !Stations.PlacedTile(recipe.createItem.type, out int made) || Stations.Info(made).Kind is not (StationKind.Basic or StationKind.Progression)) continue;
				if (!recipesOf.TryGetValue(made, out List<int[]> list)) recipesOf[made] = list = new List<int[]>();
				list.Add(recipe.requiredTile.ToArray());
			}
			// Reachable: stations no recipe makes (world, shops), basic ones and the guaranteed ones (found in the world), then
			// any station one of whose recipes only needs reachable stations. Whatever is left needs itself, directly or not
			HashSet<int> reachable = Stations.Basic().Select(s => s.tile).ToHashSet();
			foreach (GuaranteedSources.Requirement r in GuaranteedSources.Requirements.Where(r => r.Station))
			{
				if (Stations.PlacedTile(r.Item, out int placed)) reachable.Add(placed);
			}
			bool Reachable(int tile) => reachable.Contains(tile) || !recipesOf.ContainsKey(tile);
			bool changed = true;
			while (changed)
			{
				changed = false;
				foreach ((int tile, List<int[]> recipes) in recipesOf)
				{
					if (Reachable(tile) || !recipes.Any(needs => needs.All(Reachable))) continue;
					reachable.Add(tile);
					changed = true;
				}
			}
			List<string> failures = recipesOf.Keys.Where(t => !Reachable(t)).Select(t => TileID.Search.GetName(t)).ToList();
			Check.All(failures, "Stations needing themselves (directly or not)");
		}

		private static void StationTierRespected()
		{
			RequireApplied();
			if (!CraftingConfig.Instance.RandomizeStations) Check.Skip("stations are not randomized");
			RecipePlan plan = RecipePlanner.Get();
			List<string> failures = new();
			for (int i = 0; i < Count; i++)
			{
				if (plan.Tiles[i] == null) continue;
				foreach (int tile in plan.Tiles[i])
				{
					Stations.StationInfo info = Stations.Info(tile);
					if (info.Kind is StationKind.Thematic or StationKind.Unknown && !RecipeSystem.OriginalTiles(i).Contains(tile)) failures.Add($"{Name(RecipeSystem.OriginalResult(i))} got {TileID.Search.GetName(tile)}");
					if (info.Kind == StationKind.Progression && info.Tier > RecipePlanner.RecipeTier(i)) failures.Add($"{Name(RecipeSystem.OriginalResult(i))} needs {TileID.Search.GetName(tile)} (tier {info.Tier})");
				}
			}
			Check.All(failures, "Stations above the recipe's level");
		}

		private static void HandCraftKept()
		{
			RequireApplied();
			List<string> failures = new();
			for (int i = 0; i < Count; i++)
			{
				if (RecipeSystem.OriginalTiles(i).Length == 0 && Main.recipe[i].requiredTile.Count > 0) failures.Add(Name(RecipeSystem.OriginalResult(i)));
			}
			Check.All(failures, "Hand-made recipes now need a station");
		}

		private static void StationsVaried()
		{
			RequireApplied();
			if (!CraftingConfig.Instance.RandomizeStations) Check.Skip("stations are not randomized");
			RecipePlan plan = RecipePlanner.Get();
			int withTiles = Enumerable.Range(0, Count).Count(i => RecipeSystem.Touchable(i) && RecipeSystem.OriginalTiles(i).Length > 0);
			int changed = Enumerable.Range(0, Count).Count(i => plan.Tiles[i] != null);
			Check.That(withTiles == 0 || changed >= withTiles / 5, $"only {changed} of {withTiles} recipes changed station");
		}

		// The live recipes give what the plan was built around, and the plan's availability is the one of the live recipes
		private static void AppliedResultsMatchPlan()
		{
			RequireApplied();
			RecipePlan plan = RecipePlanner.Get();
			List<string> failures = new();
			int checkedCount = 0;
			for (int i = 0; i < Count; i++)
			{
				Item result = Main.recipe[i].createItem;
				// Ingredients and stations were planned around the result rolled from the original recipe
				(int type, int stack) rolled = RecipePlanner.ResultFor(i, RecipeSystem.OriginalIngredients(i), lucky: true);
				if (rolled != plan.Results[i] && !plan.ResultRepicked[i]) failures.Add($"{Name(RecipeSystem.OriginalResult(i))}: planned around {Name(rolled.type)} x{rolled.stack}, gives {Name(plan.Results[i].type)} x{plan.Results[i].stack}");
				if (plan.Results[i].type != RecipeSystem.OriginalResult(i) && Main.recipe[i].requiredItem.Any(item => item.type == plan.Results[i].type))
				{
					failures.Add($"{Name(RecipeSystem.OriginalResult(i))}: {Name(plan.Results[i].type)} is both an ingredient and the result");
				}
				checkedCount++;
				if (result.type != plan.Results[i].type || result.stack != plan.Results[i].stack)
				{
					failures.Add($"{Name(RecipeSystem.OriginalResult(i))} gives {Name(result.type)} x{result.stack}, planned {Name(plan.Results[i].type)} x{plan.Results[i].stack}");
				}
			}
			Check.That(checkedCount > 0, "no recipe");
			int[] live = RecipePlanner.Availability(Findability.Tier, plan.Results.Select(r => r.type).ToArray(), new int[plan.Results.Length],
				Enumerable.Range(0, Count).Select(i => Main.recipe[i].requiredItem.Select(item => (item.type, item.stack)).ToArray()).ToArray(),
				Enumerable.Range(0, Count).Select(i => Main.recipe[i].requiredTile.ToArray()).ToArray(), out _);
			for (int t = 1; t < live.Length && failures.Count < 20; t++)
			{
				if (live[t] != plan.Availability[t]) failures.Add($"{Name(t)} available at {Show(live[t])} with the live recipes, {Show(plan.Availability[t])} in the plan");
			}
			Check.All(failures, "Applied recipes differ from the plan");
		}

		private static string Show(int tier) => tier == Findability.Never ? "never" : tier.ToString();

		// The station plan only depends on the original recipes: removing a station from the live recipes changes nothing
		private static void StationPlanFromOriginals()
		{
			RequireApplied();
			if (!CraftingConfig.Instance.RandomizeStations) Check.Skip("stations are not randomized");
			string before = Describe(RecipePlanner.Get());
			List<(Recipe recipe, List<int> tiles)> saved = new();
			try
			{
				for (int i = 0; i < Count; i++)
				{
					Recipe recipe = Main.recipe[i];
					if (!recipe.requiredTile.Contains(TileID.WorkBenches)) continue;
					saved.Add((recipe, recipe.requiredTile.ToList()));
					recipe.requiredTile.Remove(TileID.WorkBenches);
				}
				Check.That(saved.Count > 0, "no live recipe needs a work bench");
				RecipePlanner.Reset();
				string without = Describe(RecipePlanner.Get());
				Check.Equal(before.GetHashCode(), without.GetHashCode(), "recipe plan when the live recipes lose the work bench");
			}
			finally
			{
				foreach ((Recipe recipe, List<int> tiles) in saved)
				{
					recipe.requiredTile.Clear();
					recipe.requiredTile.AddRange(tiles);
				}
				RecipePlanner.Reset();
			}
		}

		// A recipe with randomized ingredients is offered in the crafting menu with its new ingredients, and not with the old ones
		private static void RealCrafting()
		{
			RequireRandomIngredients();
			if (Main.dedServ) Check.Skip("no local player");
			Player player = Main.LocalPlayer;
			if (player.chest != -1) Check.Skip("a chest is open");
			if (!Main.guideItem.IsAir) Check.Skip("an item is in the Guide's slot");
			// Another mod provides crafting materials (Magic Storage...): the menu would see more than the inventory
			if (Terraria.ModLoader.PlayerLoader.GetModdedCraftingMaterials(player).Any(source => source.Item1.Any(item => !item.IsAir))) Check.Skip("another mod provides crafting materials");
			RecipePlan plan = RecipePlanner.Get();
			int index = -1;
			for (int i = 0; i < Count && index < 0; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (plan.Ingredients[i] == null || recipe.Disabled || recipe.Conditions.Count > 0 || recipe.acceptedGroups.Count > 0) continue;
				HashSet<int> old = RecipeSystem.OriginalIngredients(i).Select(o => o.type).ToHashSet();
				if (recipe.requiredItem.Count > 40 || recipe.requiredItem.All(item => old.Contains(item.type))) continue;
				index = i;
			}
			if (index < 0) Check.Skip("no recipe with new ingredients, without groups and conditions");
			Recipe target = Main.recipe[index];
			Item[] inventory = player.inventory.Select(item => item.Clone()).ToArray();
			Item[] voidBag = player.bank4.item.Select(item => item.Clone()).ToArray();
			bool[] adjTile = (bool[])player.adjTile.Clone();
			try
			{
				for (int k = 0; k < player.bank4.item.Length; k++) player.bank4.item[k] = new Item();
				foreach (int tile in target.requiredTile)
				{
					if (tile >= 0 && tile < player.adjTile.Length) player.adjTile[tile] = true;
				}
				bool Offered((int type, int stack)[] given)
				{
					for (int k = 0; k < player.inventory.Length; k++) player.inventory[k] = new Item();
					for (int k = 0; k < given.Length; k++) player.inventory[k] = new Item(given[k].type, given[k].stack);
					Recipe.FindRecipes(false);
					for (int k = 0; k < Main.numAvailableRecipes; k++)
					{
						if (Main.availableRecipe[k] == index) return true;
					}
					return false;
				}
				string name = Name(target.createItem.type);
				Check.That(Offered(target.requiredItem.Select(item => (item.type, item.stack)).ToArray()), $"{name} is not offered with its new ingredients");
				Check.That(!Offered(RecipeSystem.OriginalIngredients(index)), $"{name} is still offered with its old ingredients");
			}
			finally
			{
				for (int k = 0; k < inventory.Length; k++) player.inventory[k] = inventory[k];
				for (int k = 0; k < voidBag.Length; k++) player.bank4.item[k] = voidBag[k];
				Array.Copy(adjTile, player.adjTile, adjTile.Length);
				Recipe.FindRecipes(false);
			}
		}

		// Every mining level has a pickaxe by the tier of its ores, with the live plan and the guaranteed sources, without
		// counting the ores of that level and above as found (they need that pickaxe)
		private static void PickaxePerMiningLevel()
		{
			if (!ItemPool.Ready || RecipeSystem.RecipeCount == 0) Check.Skip("no recipes");
			RecipePlan plan = RecipePlanner.Get();
			int[] results = plan.Results.Select(r => r.type).ToArray();
			(int type, int stack)[][] ingredients = Enumerable.Range(0, Count).Select(i => plan.Ingredients[i] ?? RecipeSystem.OriginalIngredients(i)).ToArray();
			int[][] tiles = Enumerable.Range(0, Count).Select(i => plan.Tiles[i] ?? RecipeSystem.OriginalTiles(i)).ToArray();
			List<int> pickaxes = ContentSamples.ItemsByType.Where(pair => pair.Value.pick > 0).Select(pair => pair.Key).ToList();
			List<string> failures = new();
			int levels = 0;
			foreach ((int pick, int[] ores) level in RecipePlanner.MiningLevels)
			{
				int oreTier = RecipePlanner.MiningTier(level);
				if (oreTier < 0) continue;
				levels++;
				HashSet<int> unmined = RecipePlanner.OresFrom(level.pick);
				int[] avail = RecipePlanner.Availability(item => unmined.Contains(item) ? Findability.Never : Findability.Tier(item), results, new int[results.Length], ingredients, tiles, out _);
				int best = pickaxes.Where(p => RecipePlanner.PickPower(p) >= level.pick && p < avail.Length).Select(p => avail[p]).DefaultIfEmpty(Findability.Never).Min();
				if (best > oreTier) failures.Add($"pick power {level.pick} ({string.Join("/", level.ores.Select(Name))}, tier {oreTier}): first pickaxe at {Show(best)}");
			}
			Check.That(levels > 0, "no mining level in this world");
			Check.All(failures, "Mining levels without a pickaxe in time");
		}

		// Share of the vanilla recipes the plan changes, and why the others keep their result (values logged even when passing)
		private static void RecipeRandomizationShare()
		{
			if (!CraftingConfig.Instance.RandomizeRecipes || !RecipeSystem.Applied) Check.Skip("recipes are not randomized");
			RecipeShare share = RecipeStats.Compute();
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info(share.Summary());
			Check.That(share.Total > 0, "no vanilla recipe");
			Check.That(share.ResultShare >= 0.85, $"only {share.ResultShare:P1} of the recipes change their result");
			if (CraftingConfig.Instance.IngredientMode == IngredientMode.Randomized) Check.That(share.AnyShare >= 0.95, $"only {share.AnyShare:P1} of the recipes change their result or ingredients");
		}

		// The value fallback (explicit limits, whatever the settings): an item of the original's category and tier, never an
		// ingredient, affordable when one is, else the cheapest; deterministic
		private static void RecipeValueFallback()
		{
			if (!RecipeSystem.Applied) Check.Skip("recipes are not randomized");
			List<string> failures = new();
			int tested = 0;
			for (int i = 0; i < Count && tested < 60; i++)
			{
				int original = RecipeSystem.OriginalResult(i);
				if (!RecipeSystem.Touchable(i) || !ItemPool.IsRandomizable(original)) continue;
				List<int> types = RecipeSystem.OriginalIngredients(i).Select(x => x.type).ToList();
				int count = RecipeSystem.OriginalIngredients(i).Sum(x => x.stack);
				int tier = ItemPool.TierOf(original);
				List<(int type, int stack, long value)> candidates = RecipeSystem.FallbackCandidates(i, types, count, tier);
				if (candidates.Count < 2) continue;
				tested++;
				string name = Name(original);
				(int type, int stack) any = RecipeSystem.ValueFallback(i, types, count, long.MaxValue, tier);
				if (!candidates.Any(c => c.type == any.type && c.stack == any.stack)) failures.Add($"{name}: {Name(any.type)} is not a candidate");
				if (ItemPool.Info[any.type].Category != ItemPool.Info[original].Category) failures.Add($"{name}: {Name(any.type)} changes category");
				if (types.Contains(any.type) || RecipeSystem.InAcceptedGroup(i, any.type) || any.type == original) failures.Add($"{name}: gives {Name(any.type)}, an ingredient or the original");
				if (RecipeSystem.ValueFallback(i, types, count, long.MaxValue, tier) != any) failures.Add($"{name}: not deterministic");
				(int type, int stack) none = RecipeSystem.ValueFallback(i, types, count, -1, tier);
				long cheapest = candidates.Min(c => c.value);
				if ((long)StackRules.ValueOf(none.type) * none.stack != cheapest) failures.Add($"{name}: nothing affordable gives {Name(none.type)}, not the cheapest");
				long median = candidates.Select(c => c.value).OrderBy(v => v).ElementAt(candidates.Count / 2);
				(int type, int stack) mid = RecipeSystem.ValueFallback(i, types, count, median, tier);
				if ((long)StackRules.ValueOf(mid.type) * mid.stack > median) failures.Add($"{name}: {Name(mid.type)} x{mid.stack} above the limit {median}");
			}
			if (tested == 0) Check.Skip("no recipe with fallback candidates");
			Check.All(failures, "Value fallback");
		}

		// An item made through a randomized recipe sells for at most its ingredients' value, and keeps that value through
		// a save/load, a network round trip, a split and a stack
		private static void CraftedValueFromIngredients()
		{
			if (!CraftingConfig.Instance.CraftedValueFromIngredients) Check.Skip("crafted items keep their vanilla value");
			if (!RecipeSystem.Applied) Check.Skip("recipes are not randomized");
			int index = -1;
			long consumedValue = 0;
			for (int i = 0; i < Count && index < 0; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (recipe.Disabled || !RecipeSystem.IsRandomized(i)) continue;
				long value = recipe.requiredItem.Sum(item => (long)Math.Max(item.value, 0) * item.stack);
				if (value > 0 && (long)CraftedValueItem.VanillaValue(recipe.createItem.type) * recipe.createItem.stack > value)
				{
					index = i;
					consumedValue = value;
				}
			}
			if (index < 0) Check.Skip("no randomized recipe gives more than its ingredients are worth");
			Recipe target = Main.recipe[index];
			List<Item> consumed = target.requiredItem.Select(item => new Item(item.type, item.stack)).ToList();
			Item made = target.createItem.Clone();
			made.OnCreated(new RecipeItemCreationContext(target, consumed, new Item()));
			string name = Name(made.type);
			Check.That((long)made.value * made.stack <= consumedValue, $"{name} x{made.stack} worth {(long)made.value * made.stack} > ingredients {consumedValue}");
			Check.That(CraftedValueItem.UnitValueOf(made) >= 0, $"{name} has no crafted value");
			Check.Equal(made.value, ItemIO.Load(ItemIO.Save(made)).value, $"{name} value after a save/load");
			Check.Equal(made.value, NetRoundTrip(made).value, $"{name} value after a network round trip");

			// Stacking and splitting, on a stackable item
			int type = ItemID.LesserHealingPotion;
			int vanilla = CraftedValueItem.VanillaValue(type);
			Check.That(vanilla >= 3, "the test potion has no value");
			int unit = vanilla / 3;
			Item a = new(type, 10);
			CraftedValueItem.SetUnitValue(a, unit);
			Check.Equal(unit, a.value, "unit value once set");
			Item b = a.Clone();
			ItemLoader.SplitStack(b, a, 4);
			Check.Equal(4, b.stack, "split stack");
			Check.Equal(unit, b.value, "value of the split part");
			Check.Equal(unit, CraftedValueItem.UnitValueOf(b), "crafted value of the split part");
			Item c = new(type, 6);
			ItemLoader.StackItems(a, c, out _, false, null);
			int expected = (unit * 6 + vanilla * 6) / 12;
			Check.Equal(12, a.stack, "stacked count");
			Check.Equal(expected, a.value, "weighted value after stacking");
			Check.Equal(expected, ItemIO.Load(ItemIO.Save(a)).value, "stacked value after a save/load");
			Item d = new(type, 5);
			Check.Equal(vanilla, d.value, "an uncrafted copy keeps its vanilla value");
		}

		// Recipes making furniture, blocks or walls give something else with the furniture surprise chance (explicit chances,
		// results rolled as the plan rolls them)
		private static void FurnitureSurprise()
		{
			if (GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent) Check.Skip("surprises only exist in the Coherent mode");
			if (RecipeSystem.RecipeCount == 0) Check.Skip("no recipes");
			List<int> recipes = Enumerable.Range(0, Count).Where(i => RecipeSystem.Touchable(i) && RecipeSystem.IsFurnitureLike(RecipeSystem.OriginalResult(i))).ToList();
			if (recipes.Count < 50) Check.Skip($"only {recipes.Count} furniture recipes");
			double SameCategory(float chance)
			{
				int same = 0;
				foreach (int i in recipes)
				{
					(int type, int stack)[] ingredients = RecipeSystem.OriginalIngredients(i);
					int result = RecipeSystem.RollResult(i, ingredients.Select(x => x.type).ToList(), ingredients.Sum(x => x.stack), lucky: true,
						RecipeSystem.OriginalIngredientValue(i), limitValue: false, surpriseChance: chance).type;
					if (ItemPool.IsValid(result) && ItemPool.Info[result].Category == ItemPool.Info[RecipeSystem.OriginalResult(i)].Category) same++;
				}
				return same / (double)recipes.Count;
			}
			double none = SameCategory(0), sixty = SameCategory(60), all = SameCategory(100);
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Furniture recipes: {recipes.Count}, same category {none:P0} at 0%, {sixty:P0} at 60%, {all:P0} at 100%");
			Check.That(none >= 0.9, $"only {none:P0} of the furniture recipes keep their category without surprise");
			Check.That(sixty <= none - 0.4, $"{sixty:P0} of the furniture recipes keep their category at 60% (vs {none:P0} at 0%)");
			Check.That(all <= sixty, $"more furniture at 100% ({all:P0}) than at 60% ({sixty:P0})");
		}

		// Recipes making a progression station or a protected item can be made at their level with the plan: their
		// randomizable ingredients and their stations (the station a recipe makes counts only through its own recipe).
		// Protected ingredients keep their vanilla sources and are not checked here.
		private static void ProtectedRecipesDoable()
		{
			RequireApplied();
			RecipePlan plan = RecipePlanner.Get();
			Dictionary<int, List<int>> placers = Placers();
			List<string> failures = new();
			int checkedCount = 0;
			for (int i = 0; i < Count; i++)
			{
				Recipe recipe = Main.recipe[i];
				int original = RecipeSystem.OriginalResult(i);
				if (recipe.Disabled || !RecipeSystem.IngredientsTouchable(i)) continue;
				bool station = Stations.PlacedTile(original, out int made) && Stations.Info(made).Kind == StationKind.Progression;
				if (!station && ItemPool.IsRandomizable(original)) continue;
				checkedCount++;
				int rt = RecipePlanner.RecipeTier(i);
				foreach (Item item in recipe.requiredItem)
				{
					if (!ItemPool.IsRandomizable(item.type)) continue;
					if (item.type >= plan.Availability.Length || (plan.Availability[item.type] > rt && !GroupAvailable(recipe, item.type, plan, rt)))
					{
						failures.Add($"{Name(original)}: {Name(item.type)} at {Show(item.type < plan.Availability.Length ? plan.Availability[item.type] : Findability.Never)} > {rt}");
					}
				}
				foreach (int tile in recipe.requiredTile)
				{
					int at = StationTier(tile, placers, plan.Availability);
					if (at > rt) failures.Add($"{Name(original)}: needs {TileID.Search.GetName(tile)} at {Show(at)} > {rt}");
				}
			}
			Check.That(checkedCount > 0, "no protected recipe");
			Check.All(failures, "Protected recipes not doable at their level");
		}

		// Every live recipe gives its planned result whatever the progression (no stand-in before its tier), the untouched ones
		// their vanilla result
		private static void RecipeAlwaysFinalResult()
		{
			RequireApplied();
			RecipePlan plan = RecipePlanner.Get();
			List<string> failures = new();
			int ahead = 0;
			for (int i = 0; i < Count; i++)
			{
				(int type, int stack) expected = RecipeSystem.Touchable(i) ? plan.Results[i] : (RecipeSystem.OriginalResult(i), RecipeSystem.OriginalStack(i));
				Item live = Main.recipe[i].createItem;
				if (live.type != expected.type || live.stack != expected.stack)
				{
					failures.Add($"{Name(RecipeSystem.OriginalResult(i))} gives {Name(live.type)} x{live.stack}, planned {Name(expected.type)} x{expected.stack}");
				}
				if (RecipeSystem.AppliedResult(plan, i) != plan.Results[i]) failures.Add($"{Name(RecipeSystem.OriginalResult(i))}: AppliedResult is not the planned result");
				if (plan.ResultTiers[i] > ProgressionSystem.UnlockedTier) ahead++;
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"RecipeAlwaysFinalResult: {ahead} recipes give a result of a tier not unlocked yet (locked by their ingredients)");
			Check.All(failures, "Recipes not giving their final result");
		}

		// A randomized vanilla recipe can't be made before U, the unlocked tier of its result (a key ingredient, a station or
		// its own ingredients make it wait), and a key ingredient makes it doable at U, not later (U + 1 only when U has no
		// key material). A result re-picked back to the original is vanilla and not checked
		private static void RecipeKeyIngredient()
		{
			RequireApplied();
			if (!ProgressionConfig.Instance.TierGating || GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent) Check.Skip("only with tier gating in Coherent mode");
			RecipePlan plan = RecipePlanner.Get();
			int[] results = plan.Results.Select(r => r.type).ToArray();
			(int type, int stack)[][] ingredients = Enumerable.Range(0, Count).Select(i => plan.Ingredients[i] ?? RecipeSystem.OriginalIngredients(i)).ToArray();
			int[][] tiles = Enumerable.Range(0, Count).Select(i => plan.Tiles[i] ?? RecipeSystem.OriginalTiles(i)).ToArray();
			RecipePlanner.Availability(Findability.Tier, results, new int[results.Length], ingredients, tiles, out int[] doable);
			List<string> failures = new();
			int gated = 0, keys = 0, repicked = 0, later = 0, never = 0;
			for (int i = 0; i < Count; i++)
			{
				if (!RecipeSystem.Touchable(i) || Main.recipe[i].Disabled) continue;
				int u = plan.ResultTiers[i];
				int expectedU = Tiers.Clamp(ItemPool.UnlockTier(ItemPool.TrueTierOf(results[i])));
				if (u != expectedU) failures.Add($"{Name(RecipeSystem.OriginalResult(i))}: result tier {u}, {expectedU} expected for {Name(results[i])}");
				if (plan.KeyIngredient[i]) keys++;
				if (plan.ResultRepicked[i]) repicked++;
				if (u == 0) continue;
				gated++;
				if (doable[i] >= Findability.Never)
				{
					never++;
					continue;
				}
				if (doable[i] < u)
				{
					if (!(plan.ResultRepicked[i] && results[i] == RecipeSystem.OriginalResult(i)))
					{
						failures.Add($"{Name(RecipeSystem.OriginalResult(i))} -> {Name(results[i])}: doable at {doable[i]}, before its tier {u} ({Ingredients(ingredients[i])})");
					}
				}
				else if (doable[i] > u)
				{
					later++;
					if (plan.KeyIngredient[i] && doable[i] > u + 1) failures.Add($"{Name(RecipeSystem.OriginalResult(i))} -> {Name(results[i])}: a key ingredient but doable at {doable[i]}, tier {u}");
				}
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"RecipeKeyIngredient: {gated} gated recipes, {keys} key ingredients, {repicked} results re-picked, " +
				$"{later} doable after their tier (their own ingredients or stations), {never} never doable");
			Check.All(failures, "Recipes doable before their result's tier");
		}

		private static string Ingredients((int type, int stack)[] ingredients) => string.Join(", ", ingredients.Select(x => $"{x.stack} {Name(x.type)}"));

		private static Item NetRoundTrip(Item item)
		{
			using MemoryStream stream = new();
			using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, true)) ItemIO.Send(item, writer, true, false);
			stream.Position = 0;
			using BinaryReader reader = new(stream);
			return ItemIO.Receive(reader, true, false);
		}
	}
}

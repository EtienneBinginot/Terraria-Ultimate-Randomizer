using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
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

		// A replaced ingredient is always found (not crafted) by the recipe's level
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
			int touchable = 0;
			int doable = 0;
			List<string> examples = new();
			for (int i = 0; i < Count; i++)
			{
				if (!RecipeSystem.Touchable(i)) continue;
				touchable++;
				Recipe recipe = Main.recipe[i];
				int rt = RecipePlanner.RecipeTier(i);
				bool ok = recipe.requiredItem.All(item => item.type < plan.Availability.Length && (plan.Availability[item.type] <= rt || GroupAvailable(recipe, item.type, plan, rt)));
				if (ok) doable++;
				else if (examples.Count < 5) examples.Add(Name(RecipeSystem.OriginalResult(i)));
			}
			double ratio = touchable == 0 ? 1 : doable / (double)touchable;
			Check.That(ratio >= 0.85, $"only {ratio:P0} of the recipes can be made at their level (e.g. {string.Join(", ", examples)})");
		}

		private static bool GroupAvailable(Recipe recipe, int item, RecipePlan plan, int tier)
		{
			foreach (int id in recipe.acceptedGroups)
			{
				if (RecipeGroup.recipeGroups.TryGetValue(id, out RecipeGroup group) && group.ContainsItem(item) && group.ValidItems.Any(v => v < plan.Availability.Length && plan.Availability[v] <= tier)) return true;
			}
			return false;
		}

		// The availability of items only relies on raw sources: recomputing it from scratch gives the same values
		private static void AvailabilityConsistent()
		{
			RequireApplied();
			RecipePlan plan = RecipePlanner.Get();
			int[] again = RecipePlanner.Get().Availability;
			Check.That(ReferenceEquals(plan.Availability, again), "the plan is rebuilt between two calls");
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
				bool capped = plan.Ingredients[i].Any(p => p.stack >= Math.Min(cap, StackRules.MaxStackOf(p.type)) || p.stack == 1);
				if (!capped && (after < before / 2 || after > before * 2 + Item.silver)) failures.Add($"{Name(RecipeSystem.OriginalResult(i))}: {before} -> {after}");
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

		// Protected recipes (mining chain, protected results, modded recipes) keep their ingredients and stations
		private static void UntouchedRecipesIntact()
		{
			RequireApplied();
			List<string> failures = new();
			for (int i = 0; i < Count; i++)
			{
				if (RecipeSystem.Touchable(i)) continue;
				Recipe recipe = Main.recipe[i];
				if (!recipe.requiredItem.Select(item => (item.type, item.stack)).SequenceEqual(RecipeSystem.OriginalIngredients(i))) failures.Add($"{Name(recipe.createItem.type)} ingredients");
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
			Dictionary<int, HashSet<int>> edges = new();
			for (int i = 0; i < Count; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (!Stations.PlacedTile(recipe.createItem.type, out int made) || Stations.Info(made).Kind is not (StationKind.Basic or StationKind.Progression)) continue;
				if (!edges.TryGetValue(made, out HashSet<int> needs)) edges[made] = needs = new HashSet<int>();
				foreach (int tile in recipe.requiredTile) needs.Add(tile);
			}
			// A station is fine when one of its recipes can be made without itself, or when it is found in the world
			List<string> failures = new();
			foreach ((int tile, HashSet<int> needs) in edges)
			{
				if (needs.Contains(tile) && !Stations.Basic().Any(s => s.tile == tile)) failures.Add(TileID.Search.GetName(tile));
			}
			Check.All(failures, "Stations needing themselves");
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

		// The live recipes give what the plan was built around (once their tier is unlocked), and the plan's availability
		// is the one of the live recipes
		private static void AppliedResultsMatchPlan()
		{
			RequireApplied();
			RecipePlan plan = RecipePlanner.Get();
			ProgressionConfig progression = ProgressionConfig.Instance;
			int limit = progression.TierGating ? Tiers.Clamp(ProgressionSystem.UnlockedTier + progression.TierAllowance) : Tiers.Max;
			List<string> failures = new();
			int checkedCount = 0;
			for (int i = 0; i < Count; i++)
			{
				Item result = Main.recipe[i].createItem;
				// Ingredients and stations were planned around the result rolled from the original recipe
				(int type, int stack) rolled = RecipePlanner.ResultFor(i, RecipeSystem.OriginalIngredients(i), lucky: true);
				if (rolled != plan.Results[i]) failures.Add($"{Name(RecipeSystem.OriginalResult(i))}: planned around {Name(rolled.type)} x{rolled.stack}, gives {Name(plan.Results[i].type)} x{plan.Results[i].stack}");
				if (plan.Results[i].type != RecipeSystem.OriginalResult(i) && Main.recipe[i].requiredItem.Any(item => item.type == plan.Results[i].type))
				{
					failures.Add($"{Name(RecipeSystem.OriginalResult(i))}: {Name(plan.Results[i].type)} is both an ingredient and the result");
				}
				if (ProgressionSystem.UnlockedTier >= plan.ResultTiers[i])
				{
					checkedCount++;
					if (result.type != plan.Results[i].type || result.stack != plan.Results[i].stack)
					{
						failures.Add($"{Name(RecipeSystem.OriginalResult(i))} gives {Name(result.type)} x{result.stack}, planned {Name(plan.Results[i].type)} x{plan.Results[i].stack}");
					}
				}
				// A stand-in: never above what progression allows (or the original's own tier)
				else if (ItemPool.TierOf(result.type) > Math.Max(limit, ItemPool.TierOf(RecipeSystem.OriginalResult(i))))
				{
					failures.Add($"{Name(RecipeSystem.OriginalResult(i))} gives {Name(result.type)} (tier {ItemPool.TierOf(result.type)}) before tier {plan.ResultTiers[i]}");
				}
			}
			Check.That(checkedCount > 0, "no recipe gives its planned result yet");
			int[] live = RecipePlanner.Availability(Findability.Tier, plan.Results.Select(r => r.type).ToArray(), plan.ResultTiers,
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
	}
}

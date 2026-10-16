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
			+ "@" + (plan.Tiles[i] == null ? "-" : string.Join(",", plan.Tiles[i]))));

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
	}
}

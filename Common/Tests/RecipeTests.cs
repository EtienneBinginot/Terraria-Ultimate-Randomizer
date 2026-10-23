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
			TestRunner.Register("RecipeResultsConserved", RecipeResultsConserved);
			TestRunner.Register("CraftedValueFromIngredients", CraftedValueFromIngredients);
			TestRunner.Register("FurnitureSurprise", FurnitureSurprise);
			TestRunner.Register("ProtectedRecipesDoable", ProtectedRecipesDoable);
			TestRunner.Register("RecipeAlwaysFinalResult", RecipeAlwaysFinalResult);
			TestRunner.Register("RecipeKeyIngredient", RecipeKeyIngredient);
			TestRunner.Register("RecipeNoStationTwins", RecipeNoStationTwins);
			TestRunner.Register("UpgradeRecipes", UpgradeRecipes);
			TestRunner.Register("HouseItemsObtainable", HouseItemsObtainable);
			TestRunner.Register("RecipesRebuilt", RecipesRebuilt);
			TestRunner.Register("RecipeIngredientCount", RecipeIngredientCount);
			TestRunner.Register("RecipeIngredientTiers", RecipeIngredientTiers);
			TestRunner.Register("RecipeIngredientsAcyclic", RecipeIngredientsAcyclic);
			TestRunner.Register("RecipeIngredientUseCaps", RecipeIngredientUseCaps);
			TestRunner.Register("RecipeExamples", RecipeExamples);
			TestRunner.Register("UncraftableRecipesValid", UncraftableRecipesValid);
			TestRunner.Register("UncraftableRecipesOff", UncraftableRecipesOff);
			TestRunner.Register("UncraftableNoSpecials", UncraftableNoSpecials);
			TestRunner.Register("UncraftablePricedByIngredients", UncraftablePricedByIngredients);
			TestRunner.Register("RecipePickaxeMineable", RecipePickaxeMineable);
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

		// A new ingredient is always had (found, or crafted) by the tier its recipe was rebuilt for (the recipe's level when it
		// was not rebuilt); a key ingredient of LockResults by the U of its result (U + 1 when U has no key material)
		private static void IngredientReplacementsFindable()
		{
			RequireRandomIngredients();
			RecipePlan plan = RecipePlanner.Get();
			List<string> failures = new();
			int replaced = 0;
			for (int i = 0; i < Count; i++)
			{
				if (plan.Ingredients[i] == null) continue;
				bool rebuilt = plan.IngredientTiers[i] >= 0;
				int rt = rebuilt ? plan.IngredientTiers[i] : RecipePlanner.RecipeTier(i);
				if (plan.KeyIngredient[i]) rt = Math.Max(rt, Math.Min(plan.ResultTiers[i] + 1, Tiers.Max));
				HashSet<int> originals = RecipeSystem.OriginalIngredients(i).Select(o => o.type).ToHashSet();
				foreach ((int type, _) in plan.Ingredients[i])
				{
					if (rebuilt ? !RecipePlanner.IngredientReplaceable(type) : originals.Contains(type)) continue;
					replaced++;
					int at = IngredientAvail(plan, i, type);
					if (at > rt) failures.Add($"{Name(type)} in {Name(RecipeSystem.OriginalResult(i))}: available at {Show(at)} > {rt}");
				}
			}
			Check.That(replaced > 0, "no ingredient was replaced");
			Check.All(failures, "New ingredients can't be had in time");
		}

		// When an ingredient of a recipe is had with this plan (a group slot: its best member)
		private static int IngredientAvail(RecipePlan plan, int i, int type)
		{
			foreach (int id in RecipePlanner.GroupsOf(plan, i))
			{
				if (RecipeGroup.recipeGroups.TryGetValue(id, out RecipeGroup group) && group.ContainsItem(type))
				{
					return group.ValidItems.Where(v => v > 0 && v < plan.Availability.Length).Select(v => plan.Availability[v]).DefaultIfEmpty(Findability.Never).Min();
				}
			}
			return type > 0 && type < plan.Availability.Length ? plan.Availability[type] : Findability.Never;
		}

		// The recipe giving a life or mana upgrade (AssignSpecials): its result and ingredients' value are raised on purpose
		private static bool SpecialRecipe(RecipePlan plan, int i) => ItemPool.IsSpecial(plan.Results[i].type) && RecipePlanner.SpecialRecipe(plan.Results[i].type) == i;

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
				// The upgrade recipes are worth at least their upgrade (LockSpecials)
				if (plan.Ingredients[i] == null || SpecialRecipe(plan, i)) continue;
				long before = RecipeSystem.OriginalIngredientValue(i);
				long after = plan.IngredientValues[i];
				// A stack held by the cap may lose value; a new ingredient's stack is rounded up, so each one may add up to its
				// own unit value (a x1 of something pricier than its share). Nothing else excuses a change: a rebuilt recipe is
				// worth 0.6 to 1.6 times the vanilla one
				bool capped = plan.Ingredients[i].Any(p => p.stack >= Math.Min(cap, StackRules.MaxStackOf(p.type)));
				HashSet<int> originals = RecipeSystem.OriginalIngredients(i).Select(o => o.type).ToHashSet();
				bool rebuilt = plan.IngredientTiers[i] >= 0;
				long roundUp = plan.Ingredients[i].Where(p => rebuilt ? RecipePlanner.IngredientReplaceable(p.type) : !originals.Contains(p.type)).Sum(p => (long)Math.Max(StackRules.ValueOf(p.type), 1));
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
				// A shell is planned from nothing (UncraftableRecipesValid)
				if (RecipeSystem.Touchable(i) || RecipeSystem.IsShell(i)) continue;
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
				if (!RecipeSystem.IsShell(i) && RecipeSystem.OriginalTiles(i).Length == 0 && Main.recipe[i].requiredTile.Count > 0) failures.Add(Name(RecipeSystem.OriginalResult(i)));
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
				// Ingredients and stations were planned around the result the permutation gave the recipe
				(int type, int stack) rolled = RecipePlanner.PermutedResult(i);
				// An upgrade recipe gives its upgrade instead (AssignSpecials), a re-picked one (and its partner) the swapped result
				if (rolled != plan.Results[i] && !plan.ResultRepicked[i] && !plan.ResultSwapped[i] && !SpecialRecipe(plan, i)) failures.Add($"{Name(RecipeSystem.OriginalResult(i))}: planned around {Name(rolled.type)} x{rolled.stack}, gives {Name(plan.Results[i].type)} x{plan.Results[i].stack}");
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
				Enumerable.Range(0, Count).Select(i => Main.recipe[i].requiredTile.ToArray()).ToArray(), out _, plan.Twin);
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
				int[] avail = RecipePlanner.Availability(item => unmined.Contains(item) ? Findability.Never : Findability.Tier(item), results, new int[results.Length], ingredients, tiles, out _, plan.Twin);
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

		// The results of the randomized vanilla recipes are a permutation of their vanilla results (RecipePlanner.PermutedResults):
		// every item one of them made is still made by a recipe of the plan (only the hosts of the life and mana upgrades may
		// lose theirs), and none is made by more of them than in vanilla (an upgrade by its own recipe). Rolled one by one, the
		// results piled up on cheap items (Shadewood Sword) and the Zenith was made by none
		private static void RecipeResultsConserved()
		{
			RequireApplied();
			if (!CraftingConfig.Instance.RandomizeRecipes) Check.Skip("recipes are not randomized");
			if (GeneralConfig.Instance.RandomizerMode == RandomizerMode.Shuffle) Check.Skip("the guaranteed shuffle permutes the items themselves");
			RecipePlan plan = RecipePlanner.Get();
			HashSet<int> hosts = Protection.Upgrades.Select(RecipePlanner.SpecialRecipe).Where(i => i >= 0).ToHashSet();
			HashSet<int> hostItems = hosts.Select(i => RecipePlanner.PermutedResult(i).type).ToHashSet();
			Dictionary<int, int> vanilla = new(), planned = new(), made = new();
			int randomized = 0;
			for (int i = 0; i < Count; i++)
			{
				if (RecipeSystem.IsShell(i) ? !RecipePlanner.ShellActive(plan, i) : RecipeSystem.VanillaDisabled(i)) continue;
				int result = plan.Results[i].type;
				made[result] = made.GetValueOrDefault(result) + 1;
				if (RecipeSystem.IsShell(i) || !RecipeSystem.Touchable(i) || RecipeSystem.HouseRecipe(i)) continue;
				randomized++;
				int original = RecipeSystem.OriginalResult(i);
				vanilla[original] = vanilla.GetValueOrDefault(original) + 1;
				planned[result] = planned.GetValueOrDefault(result) + 1;
			}
			Check.That(randomized > 0, "no randomized recipe");
			List<string> failures = new();
			foreach ((int item, int n) in vanilla.OrderBy(p => p.Key))
			{
				if (made.GetValueOrDefault(item) == 0 && !hostItems.Contains(item)) failures.Add($"{Name(item)}: made by {n} recipe(s) in vanilla, by none now");
			}
			foreach ((int item, int n) in planned.OrderBy(p => p.Key))
			{
				int allowed = vanilla.GetValueOrDefault(item) + (ItemPool.IsSpecial(item) && hosts.Contains(RecipePlanner.SpecialRecipe(item)) ? 1 : 0);
				if (n > allowed) failures.Add($"{Name(item)}: result of {n} randomized recipes, {allowed} in vanilla");
			}
			// The two items of the report
			if (vanilla.ContainsKey(ItemID.Zenith))
			{
				Check.That(made.GetValueOrDefault(ItemID.Zenith) > 0, $"{Name(ItemID.Zenith)} is made by no recipe");
				Check.That(plan.Availability[ItemID.Zenith] < Findability.Never, $"{Name(ItemID.Zenith)} can't be crafted");
			}
			if (vanilla.ContainsKey(ItemID.ShadewoodSword))
			{
				int shadewood = planned.GetValueOrDefault(ItemID.ShadewoodSword);
				Check.That(shadewood <= vanilla[ItemID.ShadewoodSword], $"{Name(ItemID.ShadewoodSword)} is the result of {shadewood} randomized recipes, {vanilla[ItemID.ShadewoodSword]} in vanilla");
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"RecipeResultsConserved: {randomized} randomized recipes, {vanilla.Count} distinct results, "
				+ $"{Enumerable.Range(0, Count).Count(i => plan.ResultRepicked[i])} re-picks swapped, upgrade hosts {string.Join(", ", hostItems.Select(Name))}");
			Check.All(failures, "Recipe results not conserved");
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

		// Recipes making furniture, blocks or walls give something else with the surprise chance (explicit chances for every
		// recipe, results permuted as the plan permutes them). A surprise swaps two surprised recipes of different categories:
		// at most as many furniture recipes change category as other recipes of their tier are surprised
		private static void FurnitureSurprise()
		{
			if (GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent) Check.Skip("surprises only exist in the Coherent mode");
			if (RecipeSystem.RecipeCount == 0 || !CraftingConfig.Instance.RandomizeRecipes) Check.Skip("recipes are not randomized");
			List<int> recipes = Enumerable.Range(0, Count).Where(i => RecipeSystem.Touchable(i) && RecipeSystem.IsFurnitureLike(RecipeSystem.OriginalResult(i))).ToList();
			if (recipes.Count < 50) Check.Skip($"only {recipes.Count} furniture recipes");
			double SameCategory(float chance)
			{
				int same = 0;
				(int type, int stack)[] results = RecipePlanner.PermutedResults(limitValue: false, surpriseChance: chance);
				foreach (int i in recipes)
				{
					int result = results[i].type;
					if (ItemPool.IsValid(result) && ItemPool.Info[result].Category == ItemPool.Info[RecipeSystem.OriginalResult(i)].Category) same++;
				}
				return same / (double)recipes.Count;
			}
			double none = SameCategory(0), sixty = SameCategory(60), all = SameCategory(100);
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Furniture recipes: {recipes.Count}, same category {none:P0} at 0%, {sixty:P0} at 60%, {all:P0} at 100%");
			Check.That(none >= 0.9, $"only {none:P0} of the furniture recipes keep their category without surprise");
			Check.That(sixty <= none - 0.2, $"{sixty:P0} of the furniture recipes keep their category at 60% (vs {none:P0} at 0%)");
			// The pairing is greedy: a little noise between 60% and 100%
			Check.That(all <= sixty + 0.05, $"more furniture at 100% ({all:P0}) than at 60% ({sixty:P0})");
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
				// A key ingredient makes the recipe wait for its random result's tier on purpose (it no longer makes the station)
				int rt = plan.KeyIngredient[i] ? Math.Max(RecipePlanner.RecipeTier(i), plan.ResultTiers[i]) : RecipePlanner.RecipeTier(i);
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
		// key material). A vanilla source (its vanilla result from its vanilla ingredients and stations, or re-picked back to
		// it) is not checked
		private static void RecipeKeyIngredient()
		{
			RequireApplied();
			if (!ProgressionConfig.Instance.TierGating || GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent) Check.Skip("only with tier gating in Coherent mode");
			RecipePlan plan = RecipePlanner.Get();
			int[] results = plan.Results.Select(r => r.type).ToArray();
			(int type, int stack)[][] ingredients = Enumerable.Range(0, Count).Select(i => plan.Ingredients[i] ?? RecipeSystem.OriginalIngredients(i)).ToArray();
			int[][] tiles = Enumerable.Range(0, Count).Select(i => plan.Tiles[i] ?? RecipeSystem.OriginalTiles(i)).ToArray();
			RecipePlanner.Availability(Findability.Tier, results, new int[results.Length], ingredients, tiles, out int[] doable, plan.Twin);
			List<string> failures = new();
			int gated = 0, keys = 0, repicked = 0, later = 0, never = 0;
			for (int i = 0; i < Count; i++)
			{
				if (!RecipeSystem.Touchable(i) || Main.recipe[i].Disabled) continue;
				int u = plan.ResultTiers[i];
				int expectedU = ItemPool.IsSpecial(results[i]) ? Protection.UpgradeTier(results[i]) : Tiers.Clamp(ItemPool.UnlockTier(ItemPool.TrueTierOf(results[i])));
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
					// A recipe still giving its vanilla result from its vanilla ingredients and stations is that item's vanilla
					// source: never locked
					if (!RecipePlanner.VanillaSource(plan, i))
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

		// No two live vanilla recipes give the same result from the same ingredients (same groups and conditions) on different
		// stations: planned twins share their stations and the copies are switched off. Two kept recipes (stations never
		// randomized) are vanilla alternatives and not checked
		private static void RecipeNoStationTwins()
		{
			RequireApplied();
			if (!CraftingConfig.Instance.RandomizeStations) Check.Skip("stations are not randomized");
			RecipePlan plan = RecipePlanner.Get();
			Dictionary<string, int> seen = new();
			List<string> failures = new();
			List<string> examples = new();
			int copies = 0;
			string Key(Recipe recipe) => $"{recipe.createItem.type}x{recipe.createItem.stack}|"
				+ string.Join(",", recipe.requiredItem.Select(item => $"{item.type}x{item.stack}").OrderBy(s => s, StringComparer.Ordinal))
				+ "|" + string.Join(",", recipe.acceptedGroups.OrderBy(g => g)) + "|" + string.Join(",", recipe.Conditions.Select(c => c.Description.Key));
			string Tiles(Recipe recipe) => string.Join(",", recipe.requiredTile.OrderBy(t => t));
			for (int i = 0; i < Count; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (plan.Twin[i])
				{
					copies++;
					if (!recipe.Disabled) failures.Add($"{Name(recipe.createItem.type)}: copy (recipe {i}) still enabled");
					if (examples.Count < 5) examples.Add($"{Name(recipe.createItem.type)} ({Ingredients(recipe.requiredItem.Select(item => (item.type, item.stack)).ToArray())})");
				}
				if (recipe.Disabled || RecipeSystem.IsModded(i)) continue;
				string key = Key(recipe);
				if (!seen.TryGetValue(key, out int other))
				{
					seen[key] = i;
					continue;
				}
				if (!RecipeSystem.Touchable(i) && !RecipeSystem.Touchable(other)) continue;
				if (Tiles(recipe) != Tiles(Main.recipe[other]))
				{
					failures.Add($"{Name(recipe.createItem.type)} ({Ingredients(recipe.requiredItem.Select(item => (item.type, item.stack)).ToArray())}): recipes {other} and {i} on different stations");
				}
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"RecipeNoStationTwins: {copies} identical copies switched off; e.g. {(examples.Count == 0 ? "none" : string.Join("; ", examples))}");
			Check.All(failures, "Same recipe on different stations");
		}

		// Each life or mana upgrade is the result of exactly one live recipe (its vanilla recipe, kept or re-picked back, aside),
		// chosen by the plan; with tier gating, that recipe is doable from the upgrade's tier (a key ingredient), not before,
		// and at most one tier later. It always has a key ingredient, and its ingredients are worth at least the upgrade
		private static void UpgradeRecipes()
		{
			RequireApplied();
			int[] upgrades = Protection.Upgrades.Where(ItemPool.IsSpecial).ToArray();
			if (upgrades.Length == 0) Check.Skip("the upgrades are protected");
			RecipePlan plan = RecipePlanner.Get();
			int[] results = plan.Results.Select(r => r.type).ToArray();
			(int type, int stack)[][] ingredients = Enumerable.Range(0, Count).Select(i => plan.Ingredients[i] ?? RecipeSystem.OriginalIngredients(i)).ToArray();
			int[][] tiles = Enumerable.Range(0, Count).Select(i => plan.Tiles[i] ?? RecipeSystem.OriginalTiles(i)).ToArray();
			RecipePlanner.Availability(Findability.Tier, results, new int[results.Length], ingredients, tiles, out int[] doable, plan.Twin);
			bool gated = ProgressionConfig.Instance.TierGating && GeneralConfig.Instance.RandomizerMode == RandomizerMode.Coherent;
			List<string> failures = new();
			foreach (int item in upgrades)
			{
				int chosen = RecipePlanner.SpecialRecipe(item);
				if (chosen < 0)
				{
					failures.Add($"{Name(item)}: no recipe gives it");
					continue;
				}
				// The live recipes, as the crafting menu reads them
				List<int> giving = Enumerable.Range(0, Count).Where(i => !Main.recipe[i].Disabled && Main.recipe[i].createItem.type == item
					&& !(RecipeSystem.OriginalResult(i) == item && RecipePlanner.VanillaSource(plan, i))).ToList();
				string host = Name(RecipeSystem.OriginalResult(chosen));
				if (giving.Count != 1 || giving[0] != chosen)
				{
					failures.Add($"{Name(item)}: given by {giving.Count} recipes ({string.Join(", ", giving.Select(i => Name(RecipeSystem.OriginalResult(i))))}), planned in the recipe of {host}");
				}
				int u = Protection.UpgradeTier(item);
				string where = $"{Name(item)} in the recipe of {host} ({Ingredients(ingredients[chosen])})";
				if (doable[chosen] >= Findability.Never) failures.Add($"{where}: never doable");
				else if (gated && (doable[chosen] < u || doable[chosen] > Math.Min(u + 1, Tiers.Max))) failures.Add($"{where}: doable at {doable[chosen]}, tier {u}");
				if (!plan.KeyIngredient[chosen]) failures.Add($"{where}: no key ingredient");
				// As the crafting menu reads it
				long value = Main.recipe[chosen].requiredItem.Sum(x => (long)Math.Max(StackRules.ValueOf(x.type), 1) * x.stack);
				if (value < StackRules.ValueOf(item)) failures.Add($"{where}: ingredients worth {value}, the upgrade {StackRules.ValueOf(item)}");
				ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"UpgradeRecipes: {where}, doable at {doable[chosen]}, key ingredient {plan.KeyIngredient[chosen]}");
			}
			Check.All(failures, "Upgrade recipes");
		}

		// The door, platform and torch: a hint target each, and their recipes keep their result but never stay vanilla when
		// ingredients are randomized (a replaceable ingredient changes), with ingredients found at tier 0 and basic stations only
		private static void HouseItemsObtainable()
		{
			RequireApplied();
			CraftingConfig cfg = CraftingConfig.Instance;
			List<string> failures = new();
			foreach (int item in RecipeSystem.HouseItems)
			{
				if (!ItemPool.IsRandomizable(item)) continue;
				if (Codex.HintFacts.RawTarget(item) == null) failures.Add($"{Name(item)} is no hint target");
				if (!cfg.RandomizeRecipes) continue;
				foreach (int i in Enumerable.Range(0, Count).Where(i => RecipeSystem.HouseRecipe(i) && RecipeSystem.OriginalResult(i) == item && !RecipeSystem.VanillaDisabled(i)))
				{
					Recipe recipe = Main.recipe[i];
					(int type, int stack)[] live = recipe.requiredItem.Select(x => (x.type, x.stack)).ToArray();
					string where = $"{Name(item)} ({Ingredients(live)})";
					if (recipe.createItem.type != item) failures.Add($"{where}: gives {Name(recipe.createItem.type)}");
					if (cfg.IngredientMode == IngredientMode.Randomized && RecipeSystem.OriginalIngredients(i).Any(x => RecipePlanner.IngredientReplaceable(x.type))
						&& live.SequenceEqual(RecipeSystem.OriginalIngredients(i)))
					{
						failures.Add($"{where}: vanilla ingredients");
					}
					foreach ((int type, _) in live)
					{
						// A group ingredient (any Wood) only needs one of its items
						bool found = Findability.Tier(type) == 0 || recipe.acceptedGroups.Any(id => RecipeGroup.recipeGroups.TryGetValue(id, out RecipeGroup group) && group.ContainsItem(type)
							&& group.ValidItems.Any(v => Findability.Tier(v) == 0));
						if (!found) failures.Add($"{where}: {Name(type)} found at tier {Findability.Tier(type)}");
					}
					foreach (int tile in recipe.requiredTile)
					{
						if (Stations.Info(tile).Kind != StationKind.Basic) failures.Add($"{where}: needs {TileID.Search.GetName(tile)}, no basic station");
					}
				}
			}
			Check.All(failures, "House items");
		}

		// The ingredients of a recipe as a sorted list, to compare the live ones with the vanilla ones whatever their order
		private static string Sorted(IEnumerable<(int type, int stack)> ingredients) => string.Join(",", ingredients.Select(x => $"{x.type}x{x.stack}").OrderBy(s => s, StringComparer.Ordinal));

		// Nearly every recipe with a replaceable vanilla ingredient is rebuilt: the live ingredients differ from the vanilla ones
		private static void RecipesRebuilt()
		{
			RequireRandomIngredients();
			int rebuildable = 0, changed = 0;
			List<string> same = new();
			for (int i = 0; i < Count; i++)
			{
				if (!RecipePlanner.Rebuildable(i)) continue;
				rebuildable++;
				if (Sorted(Main.recipe[i].requiredItem.Select(x => (x.type, x.stack))) != Sorted(RecipeSystem.OriginalIngredients(i))) changed++;
				else if (same.Count < 10) same.Add(Name(RecipeSystem.OriginalResult(i)));
			}
			if (rebuildable == 0) Check.Skip("no recipe to rebuild");
			double share = changed / (double)rebuildable;
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"RecipesRebuilt: {changed} of {rebuildable} recipes have new ingredients ({share:P1}); unchanged e.g. {string.Join(", ", same)}");
			Check.That(share >= 0.9, $"only {share:P1} of the recipes have new ingredients (unchanged: {string.Join(", ", same)})");
		}

		// A rebuilt recipe has the vanilla count of ingredients, give or take IngredientCountSpread, within [1, IngredientMax]
		// (its protected ingredients always stay, with at least one new one). The upgrade recipes may get one more (their key)
		private static void RecipeIngredientCount()
		{
			RequireRandomIngredients();
			RecipePlan plan = RecipePlanner.Get();
			CraftingConfig cfg = CraftingConfig.Instance;
			List<string> failures = new();
			Dictionary<int, int> deltas = new();
			int rebuilt = 0;
			for (int i = 0; i < Count; i++)
			{
				if (plan.IngredientTiers[i] < 0 || plan.Ingredients[i] == null || SpecialRecipe(plan, i) || RecipeSystem.IsShell(i)) continue;
				rebuilt++;
				(int type, int stack)[] original = RecipeSystem.OriginalIngredients(i);
				int vanilla = original.Length;
				int kept = original.Count(x => !RecipePlanner.IngredientReplaceable(x.type));
				int max = Math.Max(1, cfg.IngredientMax);
				int low = Math.Clamp(vanilla - cfg.IngredientCountSpread, 1, max);
				int high = Math.Max(Math.Clamp(vanilla + cfg.IngredientCountSpread, 1, max), kept + 1);
				int n = plan.Ingredients[i].Length;
				deltas[n - vanilla] = deltas.GetValueOrDefault(n - vanilla) + 1;
				if (n < low || n > high) failures.Add($"{Name(RecipeSystem.OriginalResult(i))}: {n} ingredients, vanilla {vanilla} ({Ingredients(plan.Ingredients[i])})");
			}
			if (rebuilt == 0) Check.Skip("no rebuilt recipe");
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"RecipeIngredientCount: {rebuilt} rebuilt recipes, count minus vanilla count: " +
				string.Join(", ", deltas.OrderBy(p => p.Key).Select(p => $"{p.Key:+0;-0;0} x{p.Value}")));
			Check.All(failures, "Ingredient counts out of range");
		}

		// A rebuilt recipe has no new ingredient had after its tier T (a key of LockResults: U + 1 at most), and most have one
		// had exactly at T (or later), so they are not doable before it
		private static void RecipeIngredientTiers()
		{
			RequireRandomIngredients();
			RecipePlan plan = RecipePlanner.Get();
			List<string> failures = new();
			Dictionary<int, int> missing = new();
			int checkedCount = 0, atTier = 0;
			for (int i = 0; i < Count; i++)
			{
				int tier = plan.IngredientTiers[i];
				if (tier <= 0 || plan.Ingredients[i] == null || SpecialRecipe(plan, i)) continue;
				checkedCount++;
				int allowed = plan.KeyIngredient[i] ? Math.Max(tier, Math.Min(plan.ResultTiers[i] + 1, Tiers.Max)) : tier;
				int highest = plan.Ingredients[i].Where(x => RecipePlanner.IngredientReplaceable(x.type)).Select(x => IngredientAvail(plan, i, x.type)).DefaultIfEmpty(0).Max();
				if (highest > allowed) failures.Add($"{Name(RecipeSystem.OriginalResult(i))} (tier {tier}): an ingredient at {Show(highest)} ({Ingredients(plan.Ingredients[i])})");
				if (highest >= tier) atTier++;
				else missing[tier] = missing.GetValueOrDefault(tier) + 1;
			}
			if (checkedCount == 0) Check.Skip("no rebuilt recipe above tier 0");
			double share = atTier / (double)checkedCount;
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"RecipeIngredientTiers: {atTier} of {checkedCount} rebuilt recipes have an ingredient of their tier ({share:P1}); " +
				$"without one, by tier: {(missing.Count == 0 ? "none" : string.Join(", ", missing.OrderBy(p => p.Key).Select(p => $"{p.Key}: {p.Value}")))}");
			Check.All(failures, "Ingredients above the recipe's tier");
			Check.That(share >= 0.85, $"only {share:P1} of the rebuilt recipes have an ingredient of their tier");
		}

		// No loop: every ingredient of a rebuilt recipe is found (Findability), or given by a recipe planned before it (the
		// planning order), or a protected ingredient kept as it was
		private static void RecipeIngredientsAcyclic()
		{
			RequireRandomIngredients();
			RecipePlan plan = RecipePlanner.Get();
			Dictionary<int, int> firstMade = new();
			for (int j = 0; j < Count; j++)
			{
				if (plan.PlanOrder[j] < 0) continue;
				int made = plan.Results[j].type;
				firstMade[made] = Math.Min(firstMade.GetValueOrDefault(made, int.MaxValue), plan.PlanOrder[j]);
			}
			List<string> failures = new();
			int crafted = 0;
			for (int i = 0; i < Count; i++)
			{
				if (plan.IngredientTiers[i] < 0 || plan.Ingredients[i] == null) continue;
				int[] groups = RecipePlanner.GroupsOf(plan, i);
				foreach ((int type, _) in plan.Ingredients[i])
				{
					if (!RecipePlanner.IngredientReplaceable(type)) continue;
					IEnumerable<int> choices = new[] { type };
					foreach (int id in groups)
					{
						if (RecipeGroup.recipeGroups.TryGetValue(id, out RecipeGroup group) && group.ContainsItem(type)) choices = group.ValidItems.Append(type);
					}
					if (choices.Any(x => Findability.Tier(x) < Findability.Never)) continue;
					crafted++;
					if (!choices.Any(x => firstMade.GetValueOrDefault(x, int.MaxValue) < plan.PlanOrder[i]))
					{
						failures.Add($"{Name(type)} in {Name(RecipeSystem.OriginalResult(i))}: not made by any recipe planned before");
					}
				}
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"RecipeIngredientsAcyclic: {crafted} crafted (never found) ingredients in rebuilt recipes");
			Check.All(failures, "Crafted ingredients made after their recipe");
		}

		// Anti-abuse: no material is a (non key) ingredient of more rebuilt recipes than the use cap of its candidate tier.
		// The house recipes (no caps when short), upgrade recipes and those re-keyed by LockResults are left out
		private static void RecipeIngredientUseCaps()
		{
			RequireRandomIngredients();
			RecipePlan plan = RecipePlanner.Get();
			Dictionary<int, int> uses = new();
			for (int i = 0; i < Count; i++)
			{
				if (plan.IngredientTiers[i] < 0 || plan.Ingredients[i] == null || RecipeSystem.HouseRecipe(i) || SpecialRecipe(plan, i) || plan.KeyIngredient[i]) continue;
				int[] groups = RecipePlanner.GroupsOf(plan, i);
				foreach ((int type, _) in plan.Ingredients[i])
				{
					if (type == plan.PlannedKeys[i] || !RecipePlanner.IngredientReplaceable(type) || RecipeSystem.InGroups(groups, type)) continue;
					uses[type] = uses.GetValueOrDefault(type) + 1;
				}
			}
			if (uses.Count == 0) Check.Skip("no rebuilt recipe");
			List<string> failures = new();
			foreach ((int item, int count) in uses.OrderByDescending(p => p.Value).ThenBy(p => p.Key))
			{
				int tier = item < plan.CandidateTiers.Length ? plan.CandidateTiers[item] : Findability.Never;
				if (tier >= Findability.Never)
				{
					failures.Add($"{Name(item)} is no candidate");
					continue;
				}
				int cap = plan.UseCaps[Math.Min(tier, Tiers.Max)];
				if (count > cap) failures.Add($"{Name(item)}: {count} recipes, cap {cap} (tier {tier})");
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"RecipeIngredientUseCaps: {uses.Count} materials used; most used: " +
				string.Join(", ", uses.OrderByDescending(p => p.Value).ThenBy(p => p.Key).Take(8).Select(p => $"{Name(p.Key)} x{p.Value}")) + $"; caps by tier {string.Join("/", plan.UseCaps)}");
			Check.All(failures, "Materials above their use cap");
		}

		// Ten rebuilt recipes in the log (the Zenith and the Chlorophyte Plate Mail, then some of every tier): the two named
		// ones really changed
		private static void RecipeExamples()
		{
			RequireRandomIngredients();
			RecipePlan plan = RecipePlanner.Get();
			List<int> rebuilt = Enumerable.Range(0, Count).Where(i => plan.IngredientTiers[i] >= 0 && plan.Ingredients[i] != null && !RecipeSystem.IsShell(i))
				.OrderBy(i => plan.IngredientTiers[i]).ThenBy(i => (uint)RecipeSystem.RecipeKey(i)).ToList();
			if (rebuilt.Count == 0) Check.Skip("no rebuilt recipe");
			List<string> failures = new();
			List<int> examples = new();
			foreach (int item in new[] { ItemID.Zenith, ItemID.ChlorophytePlateMail })
			{
				int i = Enumerable.Range(0, Count).FirstOrDefault(r => RecipeSystem.OriginalResult(r) == item && RecipePlanner.Rebuildable(r), -1);
				if (i < 0) continue;
				examples.Add(i);
				if (Sorted(Main.recipe[i].requiredItem.Select(x => (x.type, x.stack))) == Sorted(RecipeSystem.OriginalIngredients(i))) failures.Add($"{Name(item)} keeps its vanilla ingredients");
			}
			for (int k = 0; examples.Count < 10 && k < 8; k++)
			{
				int i = rebuilt[k * rebuilt.Count / 8];
				if (!examples.Contains(i)) examples.Add(i);
			}
			UltimateRandomizer mod = ModContent.GetInstance<UltimateRandomizer>();
			foreach (int i in examples)
			{
				string groups = string.Join(", ", RecipePlanner.GroupsOf(plan, i).Select(id => RecipeGroup.recipeGroups.TryGetValue(id, out RecipeGroup g) ? g.GetText() : id.ToString()));
				mod.Logger.Info($"Recipes: example {Name(RecipeSystem.OriginalResult(i))} -> {Name(plan.Results[i].type)} x{plan.Results[i].stack} (tier {plan.IngredientTiers[i]}): " +
					$"vanilla {Ingredients(RecipeSystem.OriginalIngredients(i))} => {Ingredients(Main.recipe[i].requiredItem.Select(x => (x.type, x.stack)).ToArray())}" +
					(groups.Length > 0 ? $" (groups: {groups})" : "") + $" at {string.Join(", ", Main.recipe[i].requiredTile.Select(t => TileID.Search.GetName(t)))}");
			}
			Check.All(failures, "Example recipes");
		}

		// Items no recipe makes get a shell recipe switched on, with ingredients, giving that item, doable from the item's tier
		// (when it is found, and the unlocked tier of its true tier) and never before; switched-off shells stay off, and no
		// shell ever shimmers back. The shells change nothing the rest of the plan relies on: every other recipe is doable
		// exactly when it is without them, and no item is had earlier than without them (a shell from tier 0 for a key
		// material made at U by a randomized recipe unlocked every recipe it keys)
		private static void UncraftableRecipesValid()
		{
			RequireApplied();
			if (!CraftingConfig.Instance.RecipesForUncraftable) Check.Skip("no recipes for uncraftable items");
			RecipePlan plan = RecipePlanner.Get();
			int[] results = plan.Results.Select(r => r.type).ToArray();
			(int type, int stack)[][] ingredients = Enumerable.Range(0, Count).Select(i => Main.recipe[i].requiredItem.Select(x => (x.type, x.stack)).ToArray()).ToArray();
			int[][] tiles = Enumerable.Range(0, Count).Select(i => Main.recipe[i].requiredTile.ToArray()).ToArray();
			RecipePlanner.Availability(Findability.Tier, results, new int[results.Length], ingredients, tiles, out int[] doable, plan.Twin);
			List<string> failures = new();
			List<int> active = new();
			int eligible = 0;
			Dictionary<ItemCategory, int> byCategory = new();
			for (int i = 0; i < Count; i++)
			{
				if (!RecipeSystem.IsShell(i)) continue;
				Recipe recipe = Main.recipe[i];
				int item = RecipeSystem.OriginalResult(i);
				if (plan.ShellTiers[i] >= 0) eligible++;
				if (!RecipePlanner.ShellActive(plan, i))
				{
					if (!recipe.Disabled) failures.Add($"{Name(item)}: unplanned shell switched on");
					continue;
				}
				active.Add(i);
				ItemCategory category = ItemPool.Info[item].Category;
				byCategory[category] = byCategory.GetValueOrDefault(category) + 1;
				string where = $"{Name(item)} ({Ingredients(ingredients[i])})";
				int tier = plan.ShellTiers[i];
				if (recipe.Disabled) failures.Add($"{where}: switched off");
				if (recipe.createItem.type != item) failures.Add($"{where}: gives {Name(recipe.createItem.type)}");
				if (recipe.requiredItem.Count == 0) failures.Add($"{where}: no ingredient");
				if (recipe.requiredItem.Any(x => x.type == item)) failures.Add($"{where}: uses its own item");
				if (tier < 0) failures.Add($"{where}: planned but not eligible");
				int found = Findability.Tier(item);
				int unlock = ItemPool.UnlockTier(ItemPool.TrueTierOf(item));
				if (found < Findability.Never && tier < Math.Min(found, Tiers.Max) || tier < Math.Min(unlock, Tiers.Max)) failures.Add($"{where}: tier {tier}, found at {Show(found)}, unlocked at {unlock}");
				if (doable[i] >= Findability.Never) failures.Add($"{where}: never doable");
				else if (doable[i] < tier) failures.Add($"{where}: doable at {doable[i]}, before its tier {tier}");
				if (Terraria.GameContent.ShimmerTransforms.GetDecraftingRecipeIndex(item) == i || (ItemID.Sets.CraftingRecipeIndices[item]?.Contains(i) ?? false)) failures.Add($"{where}: shimmers back");
			}
			bool[] noShells = Enumerable.Range(0, Count).Select(i => RecipeSystem.IsShell(i) || (i < plan.Twin.Length && plan.Twin[i])).ToArray();
			int[] withShells = RecipePlanner.Availability(Findability.Tier, results, new int[results.Length], ingredients, tiles, out _, plan.Twin);
			int[] without = RecipePlanner.Availability(Findability.Tier, results, new int[results.Length], ingredients, tiles, out int[] doableWithout, noShells);
			for (int i = 0; i < Count; i++)
			{
				if (RecipeSystem.IsShell(i) || noShells[i] || doable[i] == doableWithout[i]) continue;
				failures.Add($"{Name(RecipeSystem.OriginalResult(i))} -> {Name(results[i])}: doable at {Show(doable[i])} with the shells, {Show(doableWithout[i])} without ({Ingredients(ingredients[i])})");
			}
			foreach (int i in active)
			{
				int item = results[i];
				if (without[item] < Findability.Never && withShells[item] < without[item]) failures.Add($"{Name(item)}: had at {withShells[item]} with its shell, {without[item]} without");
			}
			if (eligible == 0) Check.Skip("no item may get a shell recipe");
			Check.That(active.Count > 0, $"none of the {eligible} eligible shells is switched on");
			UltimateRandomizer mod = ModContent.GetInstance<UltimateRandomizer>();
			mod.Logger.Info($"UncraftableRecipesValid: {active.Count} of {eligible} eligible shells switched on ({string.Join(", ", byCategory.OrderBy(p => p.Key).Select(p => $"{p.Key} {p.Value}"))})");
			for (int k = 0; k < 8 && k < active.Count; k++)
			{
				int i = active[k * active.Count / 8];
				mod.Logger.Info($"Uncraftable: example {Name(RecipeSystem.OriginalResult(i))} x{Main.recipe[i].createItem.stack} (tier {plan.ShellTiers[i]}, doable at {Show(doable[i])}): " +
					$"{Ingredients(ingredients[i])} at {string.Join(", ", Main.recipe[i].requiredTile.Select(t => TileID.Search.GetName(t)))}");
			}
			Check.All(failures, "Shell recipes");
		}

		// With the setting off, the planner (called with an explicit setting, the config is not touched) switches no shell on,
		// and plans every other recipe exactly as with it on: the shells take nothing from them
		private static void UncraftableRecipesOff()
		{
			if (!CraftingConfig.Instance.RandomizeRecipes || RecipeSystem.RecipeCount == 0) Check.Skip("recipes are not randomized");
			RecipePlan on = RecipePlanner.BuildWith(true);
			RecipePlan off = RecipePlanner.BuildWith(false);
			List<string> failures = new();
			int shells = 0;
			for (int i = 0; i < Count; i++)
			{
				if (RecipeSystem.IsShell(i))
				{
					shells++;
					if (RecipePlanner.ShellActive(off, i) || off.Tiles[i] != null) failures.Add($"{Name(RecipeSystem.OriginalResult(i))}: shell planned with the setting off");
					continue;
				}
				string Plan(RecipePlan plan) => (plan.Ingredients[i] == null ? "-" : string.Join(",", plan.Ingredients[i].Select(p => p.type + "x" + p.stack)))
					+ "@" + (plan.Tiles[i] == null ? "-" : string.Join(",", plan.Tiles[i])) + "=" + plan.Results[i].type + "x" + plan.Results[i].stack + "/" + plan.Twin[i];
				if (Plan(on) != Plan(off) && failures.Count < 20) failures.Add($"{Name(RecipeSystem.OriginalResult(i))}: planned differently with shells ({Plan(on)} / {Plan(off)})");
			}
			if (shells == 0) Check.Skip("no shell recipe registered");
			int activeOn = Enumerable.Range(0, Count).Count(i => RecipePlanner.ShellActive(on, i));
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"UncraftableRecipesOff: {shells} shells registered, {activeOn} planned with the setting on, none off");
			Check.All(failures, "Shell setting");
		}

		// The upgrades never land on a shell (AssignSpecials only takes randomized vanilla recipes), a shell always gives its own
		// item, and is never a twin (copies are found among the vanilla recipes only)
		private static void UncraftableNoSpecials()
		{
			if (!CraftingConfig.Instance.RandomizeRecipes || RecipeSystem.RecipeCount == 0) Check.Skip("recipes are not randomized");
			RecipePlan plan = RecipePlanner.Get();
			List<string> failures = new();
			int shells = 0;
			foreach (int item in Protection.Upgrades)
			{
				int i = RecipePlanner.SpecialRecipe(item);
				if (i >= 0 && RecipeSystem.IsShell(i)) failures.Add($"{Name(item)} given by the shell of {Name(RecipeSystem.OriginalResult(i))}");
			}
			for (int i = 0; i < Count; i++)
			{
				if (!RecipeSystem.IsShell(i)) continue;
				shells++;
				int item = RecipeSystem.OriginalResult(i);
				if (plan.Results[i].type != item) failures.Add($"{Name(item)}: shell planned to give {Name(plan.Results[i].type)}");
				if (plan.Twin[i]) failures.Add($"{Name(item)}: shell switched off as a copy");
				if (plan.KeyIngredient[i] || plan.ResultRepicked[i]) failures.Add($"{Name(item)}: shell locked like a randomized recipe");
			}
			if (shells == 0) Check.Skip("no shell recipe registered");
			Check.All(failures, "Shells and special recipes");
		}

		// A live shell counts as a randomized recipe: what it makes sells for its ingredients (no money loop). How many stay
		// below their soft value target (stacks capped) is logged
		private static void UncraftablePricedByIngredients()
		{
			RequireApplied();
			if (!CraftingConfig.Instance.RecipesForUncraftable) Check.Skip("no recipes for uncraftable items");
			RecipePlan plan = RecipePlanner.Get();
			List<string> failures = new();
			int active = 0, priced = 0, cheap = 0;
			for (int i = 0; i < Count; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (!RecipeSystem.IsShell(i) || recipe.Disabled) continue;
				active++;
				string name = Name(recipe.createItem.type);
				if (!RecipeSystem.IsRandomized(i)) failures.Add($"{name}: shell not counted as randomized");
				long value = recipe.requiredItem.Sum(item => (long)Math.Max(item.value, 0) * item.stack);
				long worth = (long)CraftedValueItem.VanillaValue(recipe.createItem.type) * recipe.createItem.stack;
				if (CraftingConfig.Instance.LimitRecipeValue && (long)(plan.IngredientValues[i] * CraftingConfig.Instance.RecipeValueGuard) + Item.silver < worth)
				{
					cheap++;
				}
				if (!CraftingConfig.Instance.CraftedValueFromIngredients || value <= 0 || worth <= value || priced >= 5) continue;
				priced++;
				List<Item> consumed = recipe.requiredItem.Select(item => new Item(item.type, item.stack)).ToList();
				Item made = recipe.createItem.Clone();
				made.OnCreated(new RecipeItemCreationContext(recipe, consumed, new Item()));
				if ((long)made.value * made.stack > value) failures.Add($"{name} x{made.stack} sells for {(long)made.value * made.stack} > ingredients {value}");
			}
			if (active == 0) Check.Skip("no shell recipe switched on");
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"UncraftablePricedByIngredients: {active} live shells, {priced} worth more than their ingredients checked, {cheap} below their value target");
			Check.All(failures, "Shell prices");
		}

		// No rebuilt recipe making a pickaxe (or drill) gets a new ingredient that needs at least that pick power to be mined
		// (Ebonstone for a Nightmare Pickaxe): taint follows the pick power of the tile an item places
		private static void RecipePickaxeMineable()
		{
			RequireRandomIngredients();
			RecipePlan plan = RecipePlanner.Get();
			List<string> failures = new();
			int tested = 0;
			for (int i = 0; i < Count; i++)
			{
				int pick = RecipePlanner.PickPower(plan.Results[i].type);
				if (pick <= 0 || plan.Ingredients[i] == null) continue;
				tested++;
				HashSet<int> original = RecipeSystem.OriginalIngredients(i).Select(x => x.type).ToHashSet();
				foreach ((int type, _) in plan.Ingredients[i])
				{
					int needed = RecipePlanner.PickToMine(type);
					if (!original.Contains(type) && needed >= pick) failures.Add($"{Name(plan.Results[i].type)} (pick {pick}): {Name(type)} needs {needed}");
				}
			}
			if (tested == 0) Check.Skip("no rebuilt pickaxe recipe");
			Check.All(failures, "Pickaxes made of blocks they can't mine");
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

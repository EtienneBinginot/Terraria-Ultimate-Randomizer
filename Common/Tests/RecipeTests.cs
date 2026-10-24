using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Microsoft.Xna.Framework;
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
			TestRunner.Register("MiningPickaxeGuaranteed", MiningPickaxeGuaranteed);
			TestRunner.Register("OresAsIngredients", OresAsIngredients);
			TestRunner.Register("OrePairGroupsUsed", OrePairGroupsUsed);
			TestRunner.Register("RecipeRandomizationShare", RecipeRandomizationShare);
			TestRunner.Register("RecipeEveryItemKept", RecipeEveryItemKept);
			TestRunner.Register("RecipeNoCycles", RecipeNoCycles);
			TestRunner.Register("RecipeKeyed", RecipeKeyed);
			TestRunner.Register("RecipeSwapsShare", RecipeSwapsShare);
			TestRunner.Register("RecipeSwapCancelClean", RecipeSwapCancelClean);
			TestRunner.Register("ZenithWeapons", ZenithWeapons);
			TestRunner.Register("IngredientReuse", IngredientReuse);
			TestRunner.Register("CraftedValueFromIngredients", CraftedValueFromIngredients);
			TestRunner.Register("ShimmerNoMoneyLoop", ShimmerNoMoneyLoop);
			TestRunner.Register("ShimmerLiveNoMoneyLoop", ShimmerLiveNoMoneyLoop);
			TestRunner.Register("ProtectedRecipesDoable", ProtectedRecipesDoable);
			TestRunner.Register("RecipeKeyIngredient", RecipeKeyIngredient);
			TestRunner.Register("RecipeNoStationTwins", RecipeNoStationTwins);
			TestRunner.Register("UpgradeRecipes", UpgradeRecipes);
			TestRunner.Register("HouseItemsObtainable", HouseItemsObtainable);
			TestRunner.Register("RecipesRebuilt", RecipesRebuilt);
			TestRunner.Register("RecipeIngredientCount", RecipeIngredientCount);
			TestRunner.Register("RecipeIngredientTiers", RecipeIngredientTiers);
			TestRunner.Register("IngredientTierSpread", IngredientTierSpread);
			TestRunner.Register("RecipeIngredientsAcyclic", RecipeIngredientsAcyclic);
			TestRunner.Register("RecipeIngredientUseCaps", RecipeIngredientUseCaps);
			TestRunner.Register("RecipeExamples", RecipeExamples);
			TestRunner.Register("UncraftableRecipesValid", UncraftableRecipesValid);
			TestRunner.Register("UncraftableRecipesOff", UncraftableRecipesOff);
			TestRunner.Register("UncraftableNoSpecials", UncraftableNoSpecials);
			TestRunner.Register("UncraftablePricedByIngredients", UncraftablePricedByIngredients);
			TestRunner.Register("RecipePickaxeMineable", RecipePickaxeMineable);
			TestRunner.Register("RecipeNotBeforeItsTier", RecipeNotBeforeItsTier);
			TestRunner.Register("RecipeValueSane", RecipeValueSane);
			TestRunner.Register("ProtectedRecipesGated", ProtectedRecipesGated);
			TestRunner.Register("PickaxeIngredientsBelowLevel", PickaxeIngredientsBelowLevel);
			TestRunner.Register("StationsCraftable", StationsCraftable);
			TestRunner.Register("HouseRecipesSimple", HouseRecipesSimple);
			TestRunner.Register("IngredientCountCaps", IngredientCountCaps);
			TestRunner.Register("IngredientCountsFromVanilla", IngredientCountsFromVanilla);
			TestRunner.Register("NoNeverIngredients", NoNeverIngredients);
			TestRunner.Register("NoEmptyLiveRecipes", NoEmptyLiveRecipes);
			TestRunner.Register("SwapStationsInTime", SwapStationsInTime);
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

		// Every crafting station (GuaranteedSources.PlacerRequirements) has a live recipe, rebuilt (its shell when no recipe
		// made it), that makes it by its tier without any guardian (RecipePlanner.StationRecipeTiers: the stations' own drops
		// left out, so no station relies on its guardian through another one); its new ingredients are found, never crafted:
		// a critical station's (Work Bench, Furnace, Anvil, Table, Chair) among the start materials (RecipePlanner.StartMaterials:
		// gathered around the spawn in the first minutes, no ore for the Work Bench), another's had by its tier in the biomes
		// open then (StationMaterialTier); never its vanilla ingredient types alone. The Work Bench is made by hand. No station shimmers back into its ingredients. A few
		// recipes are logged
		private static void StationsCraftable()
		{
			RequireRandomIngredients();
			List<GuaranteedSources.Requirement> stations = GuaranteedSources.PlacerRequirements().ToList();
			if (stations.Count == 0) Check.Skip("every station is protected");
			RecipePlan plan = RecipePlanner.Get();
			Dictionary<int, int> crafted = RecipePlanner.StationRecipeTiers(plan);
			List<string> failures = new();
			List<string> examples = new();
			bool Live(int i) => !Main.recipe[i].Disabled && !plan.Twin[i] && (!RecipeSystem.IsShell(i) || RecipePlanner.ShellActive(plan, i));
			int shells = 0;
			List<string> guardianOnly = new();
			foreach (GuaranteedSources.Requirement station in stations)
			{
				string name = Name(station.Item);
				int tier = Tiers.Clamp(station.Tier);
				List<int> recipes = Enumerable.Range(0, Count).Where(i => plan.Results[i].type == station.Item && Live(i)).ToList();
				if (recipes.Count == 0)
				{
					// No recipe and no shell registered for it (an item this mod gives none): its guardian alone, logged
					if (!Enumerable.Range(0, Count).Any(i => RecipeSystem.OriginalResult(i) == station.Item)) guardianOnly.Add(name);
					else failures.Add($"{name} (tier {tier}): no live recipe");
					continue;
				}
				// Another mod's recipe left as its mod made it (RandomizeModdedRecipes off)
				if (recipes.All(RecipeSystem.LeftAlone))
				{
					guardianOnly.Add(name + " (modded recipe left alone)");
					continue;
				}
				shells += recipes.Count(RecipeSystem.IsShell);
				int made = crafted.GetValueOrDefault(station.Item, Findability.Never);
				string Describe(int i) => $"{Ingredients(plan.Ingredients[i] ?? RecipeSystem.OriginalIngredients(i))} at {string.Join("+", (plan.Tiles[i] ?? RecipeSystem.OriginalTiles(i)).Select(Stations.Name).DefaultIfEmpty("hand"))}";
				if (made > tier) failures.Add($"{name} (tier {tier}): crafted at {Show(made)} without its guardian ({string.Join("; ", recipes.Select(Describe))})");
				// Rebuilt when it may be (another mod's recipe left alone keeps its own)
				if (recipes.All(i => plan.Ingredients[i] == null) && recipes.Any(i => RecipeSystem.IsShell(i) || RecipePlanner.Rebuildable(i))) failures.Add($"{name}: recipe not rebuilt");
				foreach (int i in recipes.Where(i => plan.Ingredients[i] != null && !plan.Repaired[i]))
				{
					HashSet<int> vanilla = RecipeSystem.OriginalIngredients(i).Select(x => x.type).ToHashSet();
					// A swapped recipe: the vanilla types of its item's recipe (its proxy) are vanilla too
					int partner = plan.SwapPartner != null && i < plan.SwapPartner.Length ? plan.SwapPartner[i] : -1;
					HashSet<int> vanillaLike = partner >= 0 && partner != i ? vanilla.Concat(RecipeSystem.OriginalIngredients(partner).Select(x => x.type)).ToHashSet() : vanilla;
					// Rebuilt, never its vanilla ingredient types alone (a Work Bench of 12 Wood looks vanilla)
					if (vanillaLike.Count > 0 && plan.Ingredients[i].All(x => vanillaLike.Contains(x.type))) failures.Add($"{name}: rebuilt from its vanilla ingredients only ({Describe(i)})");
					foreach ((int type, _) in plan.Ingredients[i])
					{
						if (vanilla.Contains(type) && !RecipePlanner.IngredientReplaceable(type)) continue;
						if (Findability.Tier(type) > tier) failures.Add($"{name}: {Name(type)} is not found by tier {tier} ({Show(Findability.Tier(type))})");
						if (station.Critical && !RecipePlanner.StartMaterials(station.Item != ItemID.WorkBench).Contains(type)) failures.Add($"{name}: {Name(type)} is not a start material");
						else if (!station.Critical && RecipePlanner.StationMaterialTier(type) > tier) failures.Add($"{name} (tier {tier}): {Name(type)} is in a biome opened at {Show(RecipePlanner.StationMaterialTier(type))}");
					}
				}
				if (examples.Count < 8) examples.Add($"{name} (tier {tier}, crafted at {Show(made)}): {Describe(recipes[0])}");
				// A station from its guardian never shimmers into its recipe's key
				int[] decraft = station.Item < ItemID.Sets.CraftingRecipeIndices.Length ? ItemID.Sets.CraftingRecipeIndices[station.Item] : null;
				if (RecipeSystem.Applied && decraft != null && decraft.Length > 0) failures.Add($"{name}: shimmers back into {decraft.Length} recipe(s)");
			}
			// The Work Bench by hand, from tier 0 materials
			if (stations.Any(r => r.Item == ItemID.WorkBench))
			{
				List<int> benches = Enumerable.Range(0, Count).Where(i => plan.Results[i].type == ItemID.WorkBench && Live(i)).ToList();
				bool byHand = benches.Any(i => (plan.Tiles[i] ?? RecipeSystem.OriginalTiles(i)).Length == 0
					&& (plan.Ingredients[i] ?? RecipeSystem.OriginalIngredients(i)).All(x => Findability.Tier(x.type) == 0 || RecipeSystem.InGroups(RecipePlanner.GroupsOf(plan, i), x.type)));
				if (!byHand) failures.Add($"the Work Bench is not made by hand from tier 0 materials ({string.Join("; ", benches.Select(i => Ingredients(plan.Ingredients[i] ?? RecipeSystem.OriginalIngredients(i))))})");
				if (crafted.GetValueOrDefault(ItemID.WorkBench, Findability.Never) != 0) failures.Add($"the Work Bench is crafted at {Show(crafted.GetValueOrDefault(ItemID.WorkBench, Findability.Never))}");
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"StationsCraftable: {stations.Count} stations, {shells} of them by a shell recipe, guardian only: "
				+ $"{(guardianOnly.Count == 0 ? "none" : string.Join(", ", guardianOnly))}; e.g. {string.Join("; ", examples)}");
			Check.All(failures, "Stations not craftable by their tier");
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
				// A repaired recipe keeps its vanilla ingredients: only its replacements are new, had by its T
				bool rebuilt = plan.IngredientTiers[i] >= 0 && !plan.Repaired[i];
				int rt = plan.IngredientTiers[i] >= 0 ? plan.IngredientTiers[i] : RecipePlanner.RecipeTier(i);
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
		internal static int IngredientAvail(RecipePlan plan, int i, int type)
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

		// The recipe giving a life or mana upgrade (RecipePlanner.SpecialRecipe: its vanilla recipe or its shell, keyed by
		// LockSpecials): its ingredients' value is raised on purpose
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
				// A rebuilt recipe waits for its item's tier (ResultFloor), even above its vanilla level
				int rt = Math.Max(Math.Max(RecipePlanner.RecipeTier(i), plan.ResultTiers[i]), plan.IngredientTiers[i] >= 0 ? RecipePlanner.ResultFloor(plan.Results[i].type) : 0);
				bool ok = recipe.requiredItem.All(item => item.type < plan.Availability.Length && (plan.Availability[item.type] <= rt || GroupAvailable(recipe, item.type, plan, rt)))
					&& recipe.requiredTile.All(tile => StationTier(tile, placers, plan.Availability) <= rt);
				if (ok) doable++;
				else if (examples.Count < 5) examples.Add(Name(RecipeSystem.OriginalResult(i)));
			}
			double ratio = touchable == 0 ? 1 : doable / (double)touchable;
			Check.That(ratio >= 0.85, $"only {ratio:P0} of the recipes can be made at their level (e.g. {string.Join(", ", examples)})");
		}

		// Items placing each tile, built here independently of the planner: also the items placing another mod's tile that
		// counts as it for crafting (ModTile.AdjTiles: Magic Storage's combined stations)
		private static Dictionary<int, List<int>> Placers()
		{
			Dictionary<int, List<int>> placers = new();
			void Add(int tile, int type)
			{
				if (!placers.TryGetValue(tile, out List<int> list)) placers[tile] = list = new List<int>();
				if (!list.Contains(type)) list.Add(type);
			}
			foreach ((int type, Item item) in ContentSamples.ItemsByType)
			{
				if (item.createTile < TileID.Dirt) continue;
				Add(item.createTile, type);
				if (item.createTile >= TileID.Count && TileLoader.GetTile(item.createTile)?.AdjTiles is int[] adj)
				{
					foreach (int tile in adj.Where(t => t >= TileID.Dirt && t < TileID.Count)) Add(tile, type);
				}
			}
			return placers;
		}

		// A station is usable from its cheapest placing item; progression stations whose protected placer no recipe makes from
		// their tier (bought...); tiles no item places (Demon Altar) are part of the world
		private static int StationTier(int tile, Dictionary<int, List<int>> placers, int[] avail)
		{
			// Nothing placing the tile itself: the world's own (Demon Altar), whatever acts as it (Magic Storage's altars)
			if (!placers.TryGetValue(tile, out List<int> items) || !items.Any(p => ContentSamples.ItemsByType[p].createTile == tile)) return 0;
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
			// To the true fixed point (each change lowers a tier: it ends), as the planner does since a pass cap left long chains out
			while (changed)
			{
				changed = false;
				passes++;
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
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Availability recomputed in {passes} passes");
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
			List<string> failures = new();
			for (int i = 0; i < Count; i++)
			{
				// The upgrade recipes are worth at least their upgrade (LockSpecials); a swapped recipe about the vanilla recipe of
				// the item it gives
				if (plan.Ingredients[i] == null || SpecialRecipe(plan, i)) continue;
				long before = RecipeSystem.OriginalIngredientValue(plan.SwapPartner[i] >= 0 ? plan.SwapPartner[i] : i);
				long after = plan.IngredientValues[i];
				// The counts follow the vanilla counts since 1.7.2 and the value only guards the top: a recipe cheaper than vanilla
				// is fine, one worth more than MaxValueFactor times the vanilla value is wrong. A new ingredient keeps at least one
				// unit, so each one may add up to its own unit value (a x1 of something pricier than its share)
				HashSet<int> originals = RecipeSystem.OriginalIngredients(i).Select(o => o.type).ToHashSet();
				bool rebuilt = plan.IngredientTiers[i] >= 0 && !plan.Repaired[i];
				long roundUp = plan.Ingredients[i].Where(p => rebuilt ? RecipePlanner.IngredientReplaceable(p.type) : !originals.Contains(p.type)).Sum(p => RecipeSystem.EffectiveValue(p.type));
				if (after > before * RecipePlanner.MaxValueFactor + Item.silver + roundUp) failures.Add($"{Name(RecipeSystem.OriginalResult(i))}: {before} -> {after}");
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
			List<string> failures = recipesOf.Keys.Where(t => !Reachable(t)).Select(Stations.Name).ToList();
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
					if (info.Kind is StationKind.Thematic or StationKind.Unknown && !RecipeSystem.OriginalTiles(i).Contains(tile)) failures.Add($"{Name(RecipeSystem.OriginalResult(i))} got {Stations.Name(tile)}");
					if (info.Kind == StationKind.Progression && info.Tier > RecipePlanner.RecipeTier(i)) failures.Add($"{Name(RecipeSystem.OriginalResult(i))} needs {Stations.Name(tile)} (tier {info.Tier})");
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
				// Every recipe plans its vanilla result, or the vanilla result (and stack) of its swap partner, which points back
				int partner = plan.SwapPartner[i];
				(int type, int stack) expected = partner >= 0 ? (RecipeSystem.OriginalResult(partner), RecipeSystem.OriginalStack(partner)) : (RecipeSystem.OriginalResult(i), RecipeSystem.OriginalStack(i));
				if (expected != plan.Results[i]) failures.Add($"{Name(RecipeSystem.OriginalResult(i))}: gives {Name(plan.Results[i].type)} x{plan.Results[i].stack}, {Name(expected.type)} x{expected.stack} expected");
				if (partner >= 0 && plan.SwapPartner[partner] != i) failures.Add($"{Name(RecipeSystem.OriginalResult(i))}: swapped with recipe {partner}, which is swapped with {plan.SwapPartner[partner]}");
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

		// Every mining level has a pickaxe by the tier of its ores, with the live plan and the guaranteed sources, counting the
		// ores of that level and above only by their other sources (drops, bags: mining them needs that pickaxe). The plan's
		// recipes may ask for those ores (a pickaxe of an ore it can't mine yet is no cycle as long as this holds)
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
				int[] avail = RecipePlanner.Availability(item => unmined.Contains(item) || Findability.MiningNeed(item) >= level.pick ? Findability.NonMiningTier(item) : Findability.Tier(item),
					results, new int[results.Length], ingredients, tiles, out _, plan.Twin, Enumerable.Range(0, results.Length).Select(i => RecipePlanner.GroupsOf(plan, i)).ToArray());
				int best = pickaxes.Where(p => RecipePlanner.PickPower(p) >= level.pick && p < avail.Length).Select(p => avail[p]).DefaultIfEmpty(Findability.Never).Min();
				if (best > oreTier) failures.Add($"pick power {level.pick} ({string.Join("/", level.ores.Select(Name))}, tier {oreTier}): first pickaxe at {Show(best)}");
			}
			Check.That(levels > 0, "no mining level in this world");
			Check.All(failures, "Mining levels without a pickaxe in time");
		}

		// The mining chain needs no recipe: every mining level whose ores this world has gets a guaranteed pickaxe of its power
		// by the tier of its ores (RecipePlanner.MiningRequirements), found without crafting (it has hosts). A mined ore or block
		// never counts as found before a pickaxe that mines it is found, unless something else gives it (Findability.Tier)
		private static void MiningPickaxeGuaranteed()
		{
			if (!ItemPool.Ready || RecipeSystem.RecipeCount == 0) Check.Skip("no recipes");
			List<string> failures = new();
			List<string> found = new();
			foreach ((int pick, int[] ores) level in RecipePlanner.MiningLevels)
			{
				int oreTier = RecipePlanner.MiningTier(level);
				if (oreTier < 0) continue;
				string what = $"pick power {level.pick} ({string.Join("/", level.ores.Select(Name))}, tier {oreTier})";
				GuaranteedSources.Requirement guaranteed = GuaranteedSources.Requirements.FirstOrDefault(r => r.Tool && RecipePlanner.PickPower(r.Item) >= level.pick && r.Tier <= oreTier);
				if (guaranteed.Item <= 0)
				{
					failures.Add($"{what}: no guaranteed pickaxe");
					continue;
				}
				int foundAt = GuaranteedSources.FoundTier(guaranteed);
				found.Add($"{level.pick}: {Name(guaranteed.Item)} at {Show(foundAt)}");
				if (foundAt == Findability.Never) failures.Add($"{what}: {Name(guaranteed.Item)} has no host");
				if (Findability.PickaxeTier(level.pick) > foundAt) failures.Add($"{what}: first pickaxe found at {Show(Findability.PickaxeTier(level.pick))}, after the guaranteed one");
			}
			int mined = 0;
			for (int type = 1; type < ItemPool.Info.Length; type++)
			{
				if (Findability.MinedTier(type) == Findability.Never) continue;
				int need = Findability.MiningNeed(type);
				if (need <= 0) continue;
				mined++;
				int earliest = Math.Min(Findability.NonMiningTier(type), Math.Max(Findability.MinedTier(type), Findability.PickaxeTier(need)));
				if (Findability.Tier(type) < earliest) failures.Add($"{Name(type)} (pick {need}) found at {Show(Findability.Tier(type))}, before a pickaxe mines it ({Show(earliest)})");
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"MiningPickaxeGuaranteed: {mined} mined materials need a pickaxe; guaranteed pickaxes {string.Join(", ", found)}");
			Check.That(mined > 0, "no mined material needs a pickaxe");
			Check.All(failures, "Mining chain");
		}

		// Ores and blocks that need a pickaxe are ingredients again: the plan's new ingredients (not in the vanilla recipe) take
		// some of them, as items or through an ore pair group (counts logged)
		private static void OresAsIngredients()
		{
			RequireRandomIngredients();
			RecipePlan plan = RecipePlanner.Get();
			HashSet<int> mining = RecipePlanner.MiningLevels.SelectMany(l => l.ores).Append(ItemID.Obsidian).ToHashSet();
			Dictionary<int, int> counts = new();
			int pairGroups = 0;
			for (int i = 0; i < plan.Ingredients.Length; i++)
			{
				if (plan.Ingredients[i] == null || plan.Twin[i]) continue;
				HashSet<int> originals = RecipeSystem.OriginalIngredients(i).Select(o => o.type).ToHashSet();
				int[] groups = RecipePlanner.GroupsOf(plan, i);
				foreach ((int type, _) in plan.Ingredients[i])
				{
					if (originals.Contains(type)) continue;
					if (groups.Any(g => OrePairGroups.IsPairGroup(g) && RecipeGroup.recipeGroups[g].ContainsItem(type)))
					{
						pairGroups++;
						continue;
					}
					if (mining.Contains(type) || RecipePlanner.PickToMine(type) >= RecipePlanner.MiningLevels[0].pick) counts[type] = counts.GetValueOrDefault(type) + 1;
				}
			}
			int total = counts.Values.Sum() + pairGroups;
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"OresAsIngredients: {total} new ingredients mined with a pickaxe ({pairGroups} ore pair groups): "
				+ string.Join(", ", counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key).Select(p => $"{Name(p.Key)} x{p.Value}")));
			Check.That(total > 0, "no ore or block needing a pickaxe is a new ingredient");
		}

		// The ore pair groups ("Cobalt or Palladium Ore", their bars...) exist with both ores, are new ingredients in some recipes
		// (the hardmode pairs: their ore is decided by the altars after the plan), and the live recipe accepts either member
		private static void OrePairGroupsUsed()
		{
			List<string> failures = new();
			List<int> ids = OrePairGroups.Ids.ToList();
			Check.That(ids.Count >= 6, $"only {ids.Count} ore pair groups registered");
			foreach (int id in ids)
			{
				RecipeGroup group = RecipeGroup.recipeGroups[id];
				string text = group.GetText();
				if (group.ValidItems.Count != 2) failures.Add($"group {text}: {group.ValidItems.Count} members");
				if (string.IsNullOrEmpty(text) || text.StartsWith("Mods.")) failures.Add($"group {id} has no name ({text})");
			}
			RequireRandomIngredients();
			RecipePlan plan = RecipePlanner.Get();
			int used = 0;
			for (int i = 0; i < Count; i++)
			{
				if (plan.Ingredients[i] == null || plan.Twin[i] || Main.recipe[i].Disabled) continue;
				foreach (int id in RecipePlanner.GroupsOf(plan, i).Where(OrePairGroups.IsPairGroup))
				{
					used++;
					Recipe recipe = Main.recipe[i];
					RecipeGroup group = RecipeGroup.recipeGroups[id];
					if (!recipe.acceptedGroups.Contains(id)) failures.Add($"{Name(recipe.createItem.type)}: live recipe without {group.GetText()}");
					if (!recipe.requiredItem.Any(item => item.type == group.IconicItemId)) failures.Add($"{Name(recipe.createItem.type)}: {group.GetText()} without its ingredient");
					foreach (int member in group.ValidItems)
					{
						if (!recipe.AcceptedByItemGroups(member, group.IconicItemId)) failures.Add($"{Name(recipe.createItem.type)}: {Name(member)} not accepted for {group.GetText()}");
					}
				}
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"OrePairGroupsUsed: {used} recipe slots ask for an ore pair group");
			Check.That(used > 0, "no recipe asks for an ore pair group");
			Check.All(failures, "Ore pair groups");
		}

		// Share of the vanilla recipes the plan changes, and why the others keep their result (values logged even when passing):
		// nearly all of them change (new ingredients), and few swap their result, about the swap chance
		private static void RecipeRandomizationShare()
		{
			if (!CraftingConfig.Instance.RandomizeRecipes || !RecipeSystem.Applied) Check.Skip("recipes are not randomized");
			RecipeShare share = RecipeStats.Compute();
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info(share.Summary());
			Check.That(share.Total > 0, "no vanilla recipe");
			double most = CraftingConfig.Instance.RecipeResultSwapChance / 100.0 + 0.05;
			Check.That(share.ResultShare <= most, $"{share.ResultShare:P1} of the recipes change their result, at most {most:P0} expected (swap chance)");
			if (CraftingConfig.Instance.IngredientMode == IngredientMode.Randomized) Check.That(share.AnyShare >= 0.95, $"only {share.AnyShare:P1} of the recipes change their result or ingredients");
		}

		// Every item a vanilla recipe makes is still the result of a live recipe (results swapped by pairs, the upgrades on
		// their own recipes), and every item had in vanilla is still had with the plan (nothing lost by the swaps or the rebuilt
		// ingredients); the Zenith by name
		private static void RecipeEveryItemKept()
		{
			RequireApplied();
			RecipePlan plan = RecipePlanner.Get();
			HashSet<int> made = new();
			for (int i = 0; i < Count; i++)
			{
				if (!Main.recipe[i].Disabled) made.Add(Main.recipe[i].createItem.type);
			}
			List<string> failures = new();
			HashSet<int> vanillaMade = new();
			for (int i = 0; i < Count; i++)
			{
				if (RecipeSystem.IsShell(i) || RecipeSystem.VanillaDisabled(i)) continue;
				int item = RecipeSystem.OriginalResult(i);
				if (vanillaMade.Add(item) && !made.Contains(item)) failures.Add($"{Name(item)}: made by a vanilla recipe, by no live recipe");
			}
			// What vanilla has: the original recipes as they are, with the same raw sources
			int[] vanilla = RecipePlanner.Availability(Findability.Tier, Enumerable.Range(0, Count).Select(RecipeSystem.OriginalResult).ToArray(), new int[Count],
				Enumerable.Range(0, Count).Select(RecipeSystem.OriginalIngredients).ToArray(), Enumerable.Range(0, Count).Select(RecipeSystem.OriginalTiles).ToArray(), out _, null,
				Enumerable.Range(0, Count).Select(RecipeSystem.OriginalGroups).ToArray());
			int lost = 0;
			for (int item = 1; item < vanilla.Length && item < plan.Availability.Length; item++)
			{
				if (vanilla[item] >= Findability.Never || plan.Availability[item] < Findability.Never) continue;
				lost++;
				if (failures.Count < 20) failures.Add($"{Name(item)}: had at {vanilla[item]} in vanilla, never with the plan");
			}
			if (vanillaMade.Contains(ItemID.Zenith))
			{
				Check.That(made.Contains(ItemID.Zenith), $"{Name(ItemID.Zenith)} is made by no recipe");
				Check.That(plan.Availability[ItemID.Zenith] < Findability.Never, $"{Name(ItemID.Zenith)} can't be crafted");
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"RecipeEveryItemKept: {vanillaMade.Count} items made by vanilla recipes, {lost} items had in vanilla lost; " +
				$"Zenith at {Show(plan.Availability[ItemID.Zenith])}");
			Check.All(failures, "Items lost by the recipe plan");
		}

		// The live recipes as a graph: each ingredient (or every member of its recipe group) to the result
		private static Dictionary<int, HashSet<int>> LiveGraph()
		{
			Dictionary<int, HashSet<int>> next = new();
			for (int i = 0; i < Count; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (recipe.Disabled) continue;
				foreach (Item ingredient in recipe.requiredItem)
				{
					foreach (int from in Members(recipe, ingredient.type))
					{
						if (!next.TryGetValue(from, out HashSet<int> set)) next[from] = set = new HashSet<int>();
						set.Add(recipe.createItem.type);
					}
				}
			}
			return next;
		}

		// The same graph for the recipes as set up (original ingredients, groups and results)
		private static Dictionary<int, HashSet<int>> OriginalGraph()
		{
			Dictionary<int, HashSet<int>> next = new();
			for (int i = 0; i < Count; i++)
			{
				if (RecipeSystem.IsShell(i) || RecipeSystem.VanillaDisabled(i)) continue;
				int[] groups = RecipeSystem.OriginalGroups(i);
				foreach ((int type, _) in RecipeSystem.OriginalIngredients(i))
				{
					IEnumerable<int> members = new[] { type };
					foreach (int id in groups)
					{
						if (RecipeGroup.recipeGroups.TryGetValue(id, out RecipeGroup group) && group.ContainsItem(type))
						{
							members = group.ValidItems;
							break;
						}
					}
					foreach (int from in members)
					{
						if (!next.TryGetValue(from, out HashSet<int> set)) next[from] = set = new HashSet<int>();
						set.Add(RecipeSystem.OriginalResult(i));
					}
				}
			}
			return next;
		}

		// The items a live recipe's ingredient slot accepts (its group's members, or the item itself)
		private static IEnumerable<int> Members(Recipe recipe, int type)
		{
			foreach (int id in recipe.acceptedGroups)
			{
				if (RecipeGroup.recipeGroups.TryGetValue(id, out RecipeGroup group) && group.ContainsItem(type)) return group.ValidItems;
			}
			return new[] { type };
		}

		private static HashSet<int> Reach(Dictionary<int, HashSet<int>> next, int item)
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

		// No item is made from itself, directly or not, through the plan: the result of a rebuilt recipe (or a live shell) never
		// leads back, through the live recipes, to one of its new ingredients; nor that of a swapped recipe to any of its
		// ingredients (its kept ones give another item now), nor that of a recipe the planner could not rebuild (it keeps its
		// vanilla ingredients only when they make no loop), nor that of a rebuilt recipe to one of its protected ingredients
		// (they lead to its result from the start of the planning). Replaceable vanilla ingredients kept by a rebuilt recipe
		// keeping its result are not the plan's, and neither is a loop the recipes as set up already have (Wood <-> Wood
		// Platform: left as it is)
		private static void RecipeNoCycles()
		{
			RequireRandomIngredients();
			RecipePlan plan = RecipePlanner.Get();
			Dictionary<int, HashSet<int>> next = LiveGraph();
			Dictionary<int, HashSet<int>> vanillaNext = OriginalGraph();
			List<string> failures = new();
			int checkedCount = 0, slots = 0, kept = 0, vanillaLoops = 0;
			for (int i = 0; i < Count; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (recipe.Disabled || RecipeSystem.LeftAlone(i)) continue;
				bool failed = plan.Ingredients[i] == null && !RecipeSystem.IsShell(i) && RecipePlanner.Rebuildable(i);
				if (plan.Ingredients[i] == null && !failed) continue;
				checkedCount++;
				if (failed) kept++;
				int result = recipe.createItem.type;
				bool swapped = plan.SwapPartner[i] >= 0;
				HashSet<int> originals = failed || swapped || plan.Repaired[i] ? new HashSet<int>()
					: RecipeSystem.OriginalIngredients(i).Select(x => x.type).Where(RecipePlanner.IngredientReplaceable).ToHashSet();
				HashSet<int> from = Reach(next, result);
				// What the result already led to as set up (a recipe still giving its own result)
				HashSet<int> vanillaFrom = swapped || RecipeSystem.IsShell(i) ? new HashSet<int>() : Reach(vanillaNext, result);
				foreach (Item ingredient in recipe.requiredItem)
				{
					if (originals.Contains(ingredient.type)) continue;
					if (Members(recipe, ingredient.type).Any(vanillaFrom.Contains))
					{
						vanillaLoops++;
						continue;
					}
					slots++;
					int loop = Members(recipe, ingredient.type).FirstOrDefault(from.Contains, 0);
					if (loop > 0) failures.Add($"{Name(result)} (recipe {i}) needs {Name(ingredient.type)}, made from {Name(result)} again (through {Name(loop)})");
				}
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"RecipeNoCycles: {checkedCount} recipes ({kept} left vanilla by the planner), {slots} ingredients checked, {vanillaLoops} on a loop the recipes as set up have");
			Check.That(checkedCount > 0, "no rebuilt recipe");
			Check.All(failures, "Recipe loops");
		}

		// Every rebuilt vanilla recipe above tier 0 has a key (the planned one, or one LockResults added) and is not doable
		// before its tier T by its ingredients: one of them is had no earlier than T (a recipe that finds no key stays vanilla)
		private static void RecipeKeyed()
		{
			RequireRandomIngredients();
			RecipePlan plan = RecipePlanner.Get();
			List<string> failures = new();
			Dictionary<int, int> byTier = new();
			int checkedCount = 0;
			for (int i = 0; i < Count; i++)
			{
				int tier = plan.IngredientTiers[i];
				// A repaired recipe found no key: it keeps its vanilla ingredients (LockResults locks it at U)
				// A station's recipe takes found materials only, keyed when one is found at its tier (StationsCraftable)
				if (tier <= 0 || plan.Ingredients[i] == null || plan.Repaired[i] || RecipeSystem.IsShell(i) || RecipeSystem.HouseRecipe(i) || SpecialRecipe(plan, i) || RecipePlanner.StationRecipe(i)) continue;
				checkedCount++;
				byTier[tier] = byTier.GetValueOrDefault(tier) + 1;
				string where = $"{Name(RecipeSystem.OriginalResult(i))} -> {Name(plan.Results[i].type)} (tier {tier}: {Ingredients(plan.Ingredients[i])})";
				if (plan.PlannedKeys[i] == 0 && !plan.KeyIngredient[i]) failures.Add($"{where}: no key");
				int highest = plan.Ingredients[i].Select(x => IngredientAvail(plan, i, x.type)).DefaultIfEmpty(0).Max();
				if (highest < tier) failures.Add($"{where}: its ingredients are all had at {highest}");
			}
			if (checkedCount == 0) Check.Skip("no rebuilt recipe above tier 0");
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"RecipeKeyed: {checkedCount} rebuilt recipes above tier 0 ({string.Join(", ", byTier.OrderBy(p => p.Key).Select(p => $"tier {p.Key}: {p.Value}"))})");
			Check.All(failures, "Rebuilt recipes without a key of their tier");
		}

		// A cancelled swap pair leaves the planning as if it never was: when each item is had, as which candidate, and the order
		// of the candidate lists (the picks walk them in order) are exactly as before the pair was tried. Plans built with high
		// swap chances (explicit values, without shells), so some pairs are cancelled; every cancelled recipe gives its own
		// vanilla result again
		private static void RecipeSwapCancelClean()
		{
			RequireRandomIngredients();
			List<string> failures = new();
			int cancelledPairs = 0;
			foreach (float chance in new[] { 30f, 60f })
			{
				RecipePlan plan = RecipePlanner.BuildWith(false, swapChance: chance);
				int[] setting = RecipePlanner.SwappedResults(chance).partners;
				int cancelled = 0;
				for (int i = 0; i < Count; i++)
				{
					if (setting[i] < 0 || plan.SwapPartner[i] >= 0) continue;
					cancelled++;
					if (plan.Results[i] != (RecipeSystem.OriginalResult(i), RecipeSystem.OriginalStack(i)) && failures.Count < 20)
						failures.Add($"{chance}%: {Name(RecipeSystem.OriginalResult(i))}: cancelled but gives {Name(plan.Results[i].type)} x{plan.Results[i].stack}");
				}
				cancelledPairs += cancelled / 2;
				if (plan.RestoreMismatches > 0) failures.Add($"{chance}%: {plan.RestoreMismatches} of the {cancelled / 2} cancelled pairs left the candidates changed");
				ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"RecipeSwapCancelClean: at {chance}%, {cancelled / 2} pairs cancelled, {plan.RestoreMismatches} left the candidates changed");
			}
			if (cancelledPairs == 0 && failures.Count == 0) Check.Skip("no swap pair was cancelled");
			Check.All(failures, "Cancelled swaps");
		}

		// About RecipeResultSwapChance of the swappable recipes swap their results (explicit chances: the config is not read for
		// them), by pairs pointing to each other, of the same tier (any tier in Chaos mode), mostly of different categories,
		// each giving its partner's vanilla result and stack, never one of its own ingredients or group members. A higher chance
		// only adds swaps (10% is a subset of 20%). The live plan has the swaps of the setting, less the few pairs the planner
		// cancelled (no key for one of them)
		private static void RecipeSwapsShare()
		{
			CraftingConfig cfg = CraftingConfig.Instance;
			if (!cfg.RandomizeRecipes || cfg.IngredientMode != IngredientMode.Randomized || RecipeSystem.RecipeCount == 0) Check.Skip("ingredients are not randomized");
			HashSet<int> stationTiles = Stations.RequiredTiles();
			HashSet<int> eligible = Enumerable.Range(0, Count).Where(i => RecipePlanner.Swappable(i, stationTiles)).ToHashSet();
			if (eligible.Count < 200) Check.Skip($"only {eligible.Count} swappable recipes");
			bool anyTier = GeneralConfig.Instance.RandomizerMode == RandomizerMode.Chaos;
			ItemCategory CategoryOf(int item) => ItemPool.IsValid(item) ? ItemPool.Info[item].Category : ItemCategory.Misc;
			List<string> failures = new();
			UltimateRandomizer mod = ModContent.GetInstance<UltimateRandomizer>();
			Dictionary<float, int[]> byChance = new();
			foreach (float chance in new[] { 0f, 10f, 20f, 30f })
			{
				((int type, int stack)[] results, int[] partners) = RecipePlanner.SwappedResults(chance);
				byChance[chance] = partners;
				int swapped = 0, cross = 0;
				for (int i = 0; i < Count; i++)
				{
					int p = partners[i];
					if (p < 0) continue;
					swapped++;
					int own = RecipeSystem.OriginalResult(i), other = RecipeSystem.OriginalResult(p);
					string where = $"{chance}%: {Name(own)} <-> {Name(other)}";
					if (partners[p] != i) failures.Add($"{where}: not a pair");
					if (!eligible.Contains(i)) failures.Add($"{where}: not swappable");
					if (!anyTier && ItemPool.TierOf(own) != ItemPool.TierOf(other)) failures.Add($"{where}: tiers {ItemPool.TierOf(own)} and {ItemPool.TierOf(other)}");
					if (results[i] != (other, RecipeSystem.OriginalStack(p))) failures.Add($"{where}: gives {Name(results[i].type)} x{results[i].stack}");
					if (RecipeSystem.OriginalIngredients(i).Any(x => x.type == results[i].type) || RecipeSystem.InOriginalGroup(i, results[i].type)) failures.Add($"{where}: gives one of its ingredients");
					if (!RecipePlanner.Alike(CategoryOf(own), CategoryOf(other))) cross++;
				}
				double share = swapped / (double)eligible.Count;
				mod.Logger.Info($"RecipeSwapsShare: at {chance}%, {swapped} of {eligible.Count} swappable recipes swapped ({share:P1}), {cross} with another category");
				if (chance <= 0)
				{
					Check.That(swapped == 0, $"{swapped} recipes swapped at 0%");
					continue;
				}
				double expected = chance / 100.0;
				// Each pair is drawn on its own: the share moves around the chance (by tier groups of a few dozen recipes)
				Check.That(share >= expected * 0.5 && share <= expected * 1.3 + 0.01, $"{share:P1} of the recipes swapped at {chance}%");
				Check.That(cross * 2 >= swapped, $"only {cross} of {swapped} swaps change category at {chance}%");
			}
			int lost = 0;
			for (int i = 0; i < Count; i++)
			{
				int low = byChance[10f][i];
				if (low >= 0 && byChance[20f][i] != low && failures.Count < 20) failures.Add($"{Name(RecipeSystem.OriginalResult(i))}: swapped with recipe {low} at 10%, {byChance[20f][i]} at 20%");
				if (low >= 0 && byChance[20f][i] != low) lost++;
			}
			Check.That(lost == 0, $"{lost} swaps at 10% are not swaps at 20%");
			RecipePlan plan = RecipePlanner.Get();
			int[] setting = RecipePlanner.SwappedResults(cfg.RecipeResultSwapChance).partners;
			int cancelled = 0;
			for (int i = 0; i < Count && failures.Count < 20; i++)
			{
				if (plan.SwapPartner[i] == setting[i]) continue;
				if (plan.SwapPartner[i] < 0) cancelled++;
				else failures.Add($"{Name(RecipeSystem.OriginalResult(i))}: plan swapped with {plan.SwapPartner[i]}, {setting[i]} with the setting");
			}
			int planned = setting.Count(p => p >= 0);
			mod.Logger.Info($"RecipeSwapsShare: the plan cancelled {cancelled} of the {planned} swapped recipes of the setting (no key for one of a pair)");
			// A cancel is the exception (a tier without a key material): most swaps of the setting stay
			Check.That(cancelled <= planned / 4, $"the plan cancelled {cancelled} of the {planned} swapped recipes");
			// A few swapped recipes of the live plan, as the crafting menu shows them
			foreach (int i in Enumerable.Range(0, Count).Where(i => plan.SwapPartner[i] >= 0 && i < plan.SwapPartner[i]).Take(6))
			{
				int p = plan.SwapPartner[i];
				mod.Logger.Info($"Recipes: swap {Name(RecipeSystem.OriginalResult(i))} <-> {Name(RecipeSystem.OriginalResult(p))}: {Name(Main.recipe[i].createItem.type)} from "
					+ $"{Ingredients(Main.recipe[i].requiredItem.Select(x => (x.type, x.stack)).ToArray())}; {Name(Main.recipe[p].createItem.type)} from {Ingredients(Main.recipe[p].requiredItem.Select(x => (x.type, x.stack)).ToArray())}");
			}
			Check.All(failures, "Result swaps");
		}

		private static bool Weapon(int type) => ItemPool.IsValid(type) && ItemPool.Info[type].Category is ItemCategory.WeaponMelee or ItemCategory.WeaponRanged
			or ItemCategory.WeaponMagic or ItemCategory.WeaponSummon or ItemCategory.WeaponOther;

		// The Zenith keeps weapons among its ingredients when its recipe keeps it: the first slot after the key follows the
		// vanilla shape's non-material categories (a swapped Zenith recipe has mostly its host's shape: not checked)
		private static void ZenithWeapons()
		{
			RequireRandomIngredients();
			RecipePlan plan = RecipePlanner.Get();
			int i = Enumerable.Range(0, Count).FirstOrDefault(r => !Main.recipe[r].Disabled && plan.Results[r].type == ItemID.Zenith && plan.Ingredients[r] != null, -1);
			if (i < 0) Check.Skip("no rebuilt recipe gives the Zenith");
			if (plan.SwapPartner[i] >= 0) Check.Skip("the Zenith is given by a swapped recipe");
			int vanilla = Enumerable.Range(0, Count).FirstOrDefault(r => RecipeSystem.OriginalResult(r) == ItemID.Zenith && !RecipeSystem.IsShell(r), -1);
			bool shaped = vanilla >= 0 && RecipeSystem.OriginalIngredients(vanilla).Any(x => Weapon(x.type) && RecipePlanner.IngredientReplaceable(x.type));
			WeightTable weights = CraftingConfig.Instance.IngredientWeights;
			bool weighted = new[] { ItemCategory.WeaponMelee, ItemCategory.WeaponRanged, ItemCategory.WeaponMagic, ItemCategory.WeaponSummon, ItemCategory.WeaponOther }.Any(c => weights.Get(c) > 0);
			if (!shaped && !weighted) Check.Skip("no weight leads to weapons");
			(int type, int stack)[] live = Main.recipe[i].requiredItem.Select(x => (x.type, x.stack)).ToArray();
			int weapons = live.Count(x => Weapon(x.type));
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"ZenithWeapons: {Name(ItemID.Zenith)} from {Ingredients(live)} (recipe of {Name(RecipeSystem.OriginalResult(i))}, " +
				$"tier {plan.IngredientTiers[i]}): {weapons} weapons");
			Check.That(weapons > 0, $"{Name(ItemID.Zenith)} has no weapon among its ingredients ({Ingredients(live)})");
		}

		// The reuse penalty spreads the materials within a tier (the counts are live): among the recipes rebuilt for tier 0, with
		// k = 2 the ten most used (non key) materials serve fewer recipes in all than with no penalty, and the most used one no
		// more (plans built with explicit values, without shells)
		private static void IngredientReuse()
		{
			RequireRandomIngredients();
			(int max, int top, string names) Spread(RecipePlan plan)
			{
				Dictionary<int, int> uses = new();
				for (int i = 0; i < Count; i++)
				{
					if (plan.IngredientTiers[i] != 0 || plan.Ingredients[i] == null || plan.Repaired[i] || RecipeSystem.HouseRecipe(i) || RecipeSystem.IsShell(i)) continue;
					int[] groups = RecipePlanner.GroupsOf(plan, i);
					foreach ((int type, _) in plan.Ingredients[i])
					{
						if (type == plan.PlannedKeys[i] || !RecipePlanner.IngredientReplaceable(type) || RecipeSystem.InGroups(groups, type)) continue;
						uses[type] = uses.GetValueOrDefault(type) + 1;
					}
				}
				List<KeyValuePair<int, int>> sorted = uses.OrderByDescending(p => p.Value).ThenBy(p => p.Key).ToList();
				return (sorted.Select(p => p.Value).FirstOrDefault(), sorted.Take(10).Sum(p => p.Value), string.Join(", ", sorted.Take(5).Select(p => $"{Name(p.Key)} x{p.Value}")));
			}
			(int max, int top, string names) none = Spread(RecipePlanner.BuildWith(false, reusePenalty: 0f));
			(int max, int top, string names) strong = Spread(RecipePlanner.BuildWith(false, reusePenalty: 2f));
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"IngredientReuse: tier 0, no penalty: most used {none.max}, ten most used {none.top} ({none.names}); " +
				$"penalty 2: most used {strong.max}, ten most used {strong.top} ({strong.names})");
			if (none.top == 0) Check.Skip("no recipe rebuilt for tier 0");
			Check.That(strong.top < none.top, $"the ten most used materials serve {strong.top} recipes with the penalty, {none.top} without");
			Check.That(strong.max <= none.max, $"the most used material serves {strong.max} recipes with the penalty, {none.max} without");
		}

		// An item made through a randomized recipe sells for at most its ingredients' value, and keeps that value through
		// a save/load, a network round trip, a split and a stack
		// An item crafted cheaply (a crafted unit value) never shimmers into more money than it was worth: whatever the vanilla
		// value of what it gives (a randomized transformation up to 3 times its own, decrafted ingredients), the stack it gives
		// sells for at most the input's value, and an item without crafted value gives vanilla-valued items. Pure
		private static void ShimmerNoMoneyLoop()
		{
			List<string> failures = new();
			foreach (int inputUnit in new[] { 0, 1, 7, 50, 999, 25000 })
			{
				foreach (int stack in new[] { 1, 3, 99, 9999 })
				{
					foreach (int outputVanilla in new[] { 1, 13, 100, 5000, 300000 })
					{
						foreach (int count in new[] { 1, 2, 5 })
						{
							// count output items per input item (decrafting), one for one for a transformation
							long input = (long)inputUnit * stack;
							long outputs = (long)stack * count;
							double share = CraftedValueItem.ShimmerShare(input, outputVanilla * outputs);
							int unit = CraftedValueItem.ShimmerUnit(share, outputVanilla);
							long sold = (unit < 0 ? outputVanilla : unit) * outputs;
							if (sold > input) failures.Add($"unit {inputUnit} x{stack} into {outputs} items of vanilla value {outputVanilla}: sells for {sold}, input worth {input}");
							if (unit > outputVanilla) failures.Add($"unit {unit} above the vanilla value {outputVanilla}");
						}
					}
				}
			}
			if (CraftedValueItem.ShimmerShare(0, 0) != 1 || CraftedValueItem.ShimmerUnit(1, 100) != -1) failures.Add("nothing to cap: the vanilla value should stay");
			Check.All(failures, "Shimmer money loops");
		}

		// The real shimmer (Item.GetShimmered on a world item) of cheap crafted-value items: a transformation, a whole decraft
		// and a partial one (a stack that is no multiple of the recipe's). What comes out never sells for more than what the
		// shimmer consumed. The spawned items are removed
		private static void ShimmerLiveNoMoneyLoop()
		{
			if (Main.netMode != NetmodeID.SinglePlayer) Check.Skip("single player only (the shimmer runs on the server)");
			List<(string what, int type, int stack)> cases = new();
			bool Cheapable(int type) => type > ItemID.None && CraftedValueItem.VanillaValue(type) >= 10 && ItemID.Sets.CoinLuckValue[type] <= 0
				&& ContentSamples.ItemsByType.TryGetValue(type, out Item sample) && sample.makeNPC <= 0 && ItemID.Sets.ShimmerCountsAsItem[type] == -1;
			int transform = Enumerable.Range(1, ItemLoader.ItemCount - 1).FirstOrDefault(type => type < ItemID.Count && Cheapable(type)
				&& ItemID.Sets.ShimmerTransformToItem[type] > 0 && CraftedValueItem.VanillaValue(ItemID.Sets.ShimmerTransformToItem[type]) > 0);
			if (transform > 0) cases.Add(("transformation", transform, 3));
			foreach (bool partial in new[] { false, true })
			{
				int decraft = Enumerable.Range(1, ItemID.Count - 1).FirstOrDefault(type =>
				{
					if (!Cheapable(type) || ItemID.Sets.ShimmerTransformToItem[type] > 0) return false;
					int index = Terraria.GameContent.ShimmerTransforms.GetDecraftingRecipeIndex(type);
					if (index < 0 || index >= Recipe.numRecipes) return false;
					Recipe recipe = Main.recipe[index];
					return (!partial || recipe.createItem.stack > 1) && recipe.requiredItem.Sum(i => (long)CraftedValueItem.VanillaValue(i.type) * i.stack) > 0;
				});
				if (decraft <= 0) continue;
				int made = Math.Max(1, Main.recipe[Terraria.GameContent.ShimmerTransforms.GetDecraftingRecipeIndex(decraft)].createItem.stack);
				cases.Add((partial ? "partial decraft" : "decraft", decraft, partial ? made * 2 + 1 : made * 2));
			}
			if (cases.Count == 0) Check.Skip("no shimmer transformation nor decraft found");
			List<string> failures = new();
			int given = 0;
			foreach ((string what, int type, int stack) in cases)
			{
				bool[] before = Main.item.Select(i => i.active).ToArray();
				int slot = Item.NewItem(new EntitySource_Misc("UltimateRandomizer test"), Main.LocalPlayer.Center, Vector2.Zero, type, stack);
				try
				{
					if (slot < 0 || slot >= Main.maxItems || !Main.item[slot].active)
					{
						failures.Add($"{Name(type)}: could not spawn the item");
						continue;
					}
					Item item = Main.item[slot];
					item.stack = stack;
					int unit = Math.Max(1, CraftedValueItem.VanillaValue(type) / 10);
					CraftedValueItem.SetUnitValue(item, unit);
					if (CraftedValueItem.UnitValueOf(item) != unit)
					{
						failures.Add($"{Name(type)}: the crafted value did not stick");
						continue;
					}
					// Private: called as Item.Update calls it (the detour applies to the method itself)
					typeof(Item).GetMethod("GetShimmered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public).Invoke(item, null);
					bool changed = item.type != type;
					long consumed = (long)unit * (changed ? stack : stack - Math.Max(0, item.active ? item.stack : 0));
					long sold = 0;
					int outputs = 0;
					for (int i = 0; i < Main.maxItems; i++)
					{
						Item output = Main.item[i];
						if (!output.active || output.IsAir || (i < before.Length && before[i] && i != slot)) continue;
						if (i == slot && !changed) continue;
						sold += (long)Math.Max(0, output.value) * output.stack;
						outputs++;
					}
					if (outputs == 0) continue;
					given++;
					if (sold > consumed) failures.Add($"{what} of {stack} {Name(type)} at {unit} each: the outputs sell for {sold}, the shimmer consumed {consumed}");
				}
				finally
				{
					for (int i = 0; i < Main.maxItems; i++)
					{
						if (Main.item[i].active && (i == slot || i >= before.Length || !before[i])) Main.item[i].TurnToAir();
					}
				}
			}
			if (given == 0) Check.Skip("none of the shimmers gave anything");
			Check.All(failures, "Shimmering crafted-value items gives money");
		}

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
				// A key ingredient makes the recipe wait for its result's tier on purpose (a protected result whose ingredients are
				// rebuilt waits for U too: no Key of Night before hardmode)
				int rt = Math.Max(RecipePlanner.RecipeTier(i), plan.ResultTiers[i]);
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
					if (at > rt) failures.Add($"{Name(original)}: needs {Stations.Name(tile)} at {Show(at)} > {rt}");
				}
			}
			Check.That(checkedCount > 0, "no protected recipe");
			Check.All(failures, "Protected recipes not doable at their level");
		}

		// A randomized vanilla recipe can't be made before U, the unlocked tier of its result (a key ingredient, a station or
		// its own ingredients make it wait), and a key ingredient makes it doable at U, not later (U + 1 only when U has no
		// key material). A vanilla source (its vanilla result from its vanilla ingredients and stations) is not checked
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
			int gated = 0, keys = 0, swapped = 0, later = 0, never = 0;
			for (int i = 0; i < Count; i++)
			{
				if (!RecipeSystem.Touchable(i) || Main.recipe[i].Disabled) continue;
				int u = plan.ResultTiers[i];
				// A crafting station is not locked: its recipe is planned for its tier (RecipePlanner.StationRecipe)
				int expectedU = RecipePlanner.StationRecipe(i) ? 0 : ItemPool.IsSpecial(results[i]) ? Protection.UpgradeTier(results[i]) : Tiers.Clamp(ItemPool.UnlockTierOf(results[i]));
				if (u != expectedU) failures.Add($"{Name(RecipeSystem.OriginalResult(i))}: result tier {u}, {expectedU} expected for {Name(results[i])}");
				if (plan.KeyIngredient[i]) keys++;
				if (plan.SwapPartner[i] >= 0) swapped++;
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
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"RecipeKeyIngredient: {gated} gated recipes, {keys} key ingredients, {swapped} swapped results, " +
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
			int copies = 0, loopOff = 0;
			string Key(Recipe recipe) => $"{recipe.createItem.type}x{recipe.createItem.stack}|"
				+ string.Join(",", recipe.requiredItem.Select(item => $"{item.type}x{item.stack}").OrderBy(s => s, StringComparer.Ordinal))
				+ "|" + string.Join(",", recipe.acceptedGroups.OrderBy(g => g)) + "|" + string.Join(",", recipe.Conditions.Select(c => c.Description.Key));
			string Tiles(Recipe recipe) => string.Join(",", recipe.requiredTile.OrderBy(t => t));
			for (int i = 0; i < Count; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (plan.Twin[i])
				{
					// A recipe switched off for a loop (KeepOriginals) is no copy
					if (plan.LoopOff[i]) loopOff++;
					else copies++;
					if (!recipe.Disabled) failures.Add($"{Name(recipe.createItem.type)}: switched off (recipe {i}) but still enabled");
					if (examples.Count < 5 && !plan.LoopOff[i]) examples.Add($"{Name(recipe.createItem.type)} ({Ingredients(recipe.requiredItem.Select(item => (item.type, item.stack)).ToArray())})");
				}
				if (recipe.Disabled || RecipeSystem.LeftAlone(i)) continue;
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
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"RecipeNoStationTwins: {copies} identical copies switched off ({loopOff} other recipes switched off for a loop); e.g. {(examples.Count == 0 ? "none" : string.Join("; ", examples))}");
			Check.All(failures, "Same recipe on different stations");
		}

		// Each life or mana upgrade is the result of exactly one live recipe, its own (its shell, or its vanilla recipe), never
		// another recipe's result. A randomized one is doable from the upgrade's tier, not before and at most one tier later
		// (always for a shell, with tier gating for a vanilla recipe), has a key ingredient, and its ingredients are worth at
		// least the upgrade
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
				List<int> giving = Enumerable.Range(0, Count).Where(i => !Main.recipe[i].Disabled && Main.recipe[i].createItem.type == item).ToList();
				bool shell = RecipeSystem.IsShell(chosen);
				string where = $"{Name(item)} ({(shell ? "its shell" : "its vanilla recipe")}: {Ingredients(ingredients[chosen])})";
				if (giving.Count != 1 || giving[0] != chosen) failures.Add($"{where}: given by {giving.Count} live recipes ({string.Join(", ", giving)}), planned in recipe {chosen}");
				if (plan.Ingredients[chosen] == null)
				{
					// A vanilla recipe left as it is (vanilla ingredients) is the upgrade's vanilla source; a shell is then off
					if (shell) failures.Add($"{where}: shell switched off");
					continue;
				}
				int u = Protection.UpgradeTier(item);
				if (doable[chosen] >= Findability.Never) failures.Add($"{where}: never doable");
				else if ((shell || gated) && (doable[chosen] < u || doable[chosen] > Math.Min(u + 1, Tiers.Max))) failures.Add($"{where}: doable at {doable[chosen]}, tier {u}");
				if (plan.PlannedKeys[chosen] == 0 && !plan.KeyIngredient[chosen]) failures.Add($"{where}: no key ingredient");
				// As the crafting menu reads it
				long value = Main.recipe[chosen].requiredItem.Sum(x => RecipeSystem.EffectiveValue(x.type) * x.stack);
				if (value < StackRules.ValueOf(item)) failures.Add($"{where}: ingredients worth {value}, the upgrade {StackRules.ValueOf(item)}");
				ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"UpgradeRecipes: {where} at {string.Join(", ", tiles[chosen].Select(Stations.Name))}, doable at {doable[chosen]}");
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
						if (Stations.Info(tile).Kind != StationKind.Basic) failures.Add($"{where}: needs {Stations.Name(tile)}, no basic station");
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

		// A rebuilt recipe has its vanilla count of ingredients (a swapped one too: the host's count, so the swap shows), give
		// or take IngredientCountSpread, within [1, IngredientMax] (its protected ingredients always stay, with at least one new
		// one). A recipe keyed by LockResults may get one more (its key added as a new ingredient). A vanilla recipe above
		// IngredientMax (a music box of 12) is planned with IngredientMax, and a slot that finds no candidate stays empty: the
		// spread is counted down from IngredientMax then
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
				int low = Math.Clamp(Math.Min(vanilla, max) - cfg.IngredientCountSpread, 1, max);
				int high = Math.Max(Math.Clamp(vanilla + cfg.IngredientCountSpread, 1, max), kept + 1) + (plan.KeyIngredient[i] ? 1 : 0);
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
				if (tier <= 0 || plan.Ingredients[i] == null || plan.Repaired[i] || SpecialRecipe(plan, i)) continue;
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

		// The new ingredients of an advanced recipe are not all of its top tiers (RecipePlanner.TierFalloff, TopTierBonus, the
		// tier sizes): of the rebuilt recipes of tier T >= 4 (keyed ones, no house, station, shell or special), at least
		// MinLowShare of the new ingredients other than the key are had at T-2 or before. Logs the distribution by T
		private static void IngredientTierSpread()
		{
			const double MinLowShare = 0.2;
			RequireRandomIngredients();
			RecipePlan plan = RecipePlanner.Get();
			// T -> counts by distance below T (0, 1, 2, 3+)
			Dictionary<int, int[]> byTier = new();
			int total = 0, low = 0;
			for (int i = 0; i < Count; i++)
			{
				int tier = plan.IngredientTiers[i];
				if (tier < (int)Core.Tier.Hardmode || plan.Ingredients[i] == null || plan.Repaired[i] || RecipeSystem.IsShell(i) || RecipeSystem.HouseRecipe(i) || SpecialRecipe(plan, i) || RecipePlanner.StationRecipe(i)) continue;
				HashSet<int> vanilla = RecipeSystem.OriginalIngredients(i).Select(x => x.type).ToHashSet();
				foreach ((int type, _) in plan.Ingredients[i])
				{
					if (type == plan.PlannedKeys[i] || vanilla.Contains(type) || !RecipePlanner.IngredientReplaceable(type)) continue;
					int avail = IngredientAvail(plan, i, type);
					if (avail == Findability.Never) continue;
					int below = Math.Clamp(tier - avail, 0, 3);
					if (!byTier.TryGetValue(tier, out int[] counts)) byTier[tier] = counts = new int[4];
					counts[below]++;
					total++;
					if (avail <= tier - 2) low++;
				}
			}
			if (total < 30) Check.Skip($"only {total} new ingredients in rebuilt recipes of tier 4 or more");
			double share = low / (double)total;
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"IngredientTierSpread: {low} of {total} new ingredients of recipes of tier 4+ are had at T-2 or before ({share:P1}); by T (T, T-1, T-2, T-3 or before): " +
				string.Join("; ", byTier.OrderBy(p => p.Key).Select(p => $"{p.Key}: {string.Join("/", p.Value)}")));
			Check.That(share >= MinLowShare, $"only {share:P1} of the new ingredients of advanced recipes come from two tiers or more below (at least {MinLowShare:P0} expected)");
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
				// A repaired recipe's vanilla ingredients are not the plan's
				HashSet<int> vanilla = plan.Repaired[i] ? RecipeSystem.OriginalIngredients(i).Select(x => x.type).ToHashSet() : new HashSet<int>();
				foreach ((int type, _) in plan.Ingredients[i])
				{
					if (!RecipePlanner.IngredientReplaceable(type) || vanilla.Contains(type)) continue;
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
				// A repaired recipe: its replacements only (within the caps too)
				HashSet<int> vanilla = plan.Repaired[i] ? RecipeSystem.OriginalIngredients(i).Select(x => x.type).ToHashSet() : new HashSet<int>();
				foreach ((int type, _) in plan.Ingredients[i])
				{
					if (type == plan.PlannedKeys[i] || vanilla.Contains(type) || !RecipePlanner.IngredientReplaceable(type) || RecipeSystem.InGroups(groups, type)) continue;
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
					(groups.Length > 0 ? $" (groups: {groups})" : "") + $" at {string.Join(", ", Main.recipe[i].requiredTile.Select(Stations.Name))}");
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
				int unlock = ItemPool.UnlockTierOf(item);
				bool upgrade = ItemPool.IsSpecial(item);
				// A station's shell is its recipe, planned for the station's tier (StationsCraftable), keyed when it can be
				bool station = RecipePlanner.StationShell(i);
				if (upgrade && tier != Protection.UpgradeTier(item)) failures.Add($"{where}: tier {tier}, the upgrade's tier is {Protection.UpgradeTier(item)}");
				if (!upgrade && !station && (found < Findability.Never && tier < Math.Min(found, Tiers.Max) || tier < Math.Min(unlock, Tiers.Max))) failures.Add($"{where}: tier {tier}, found at {Show(found)}, unlocked at {unlock}");
				if (doable[i] >= Findability.Never) failures.Add($"{where}: never doable");
				else if (doable[i] < tier && !station) failures.Add($"{where}: doable at {doable[i]}, before its tier {tier}");
				if (Terraria.GameContent.ShimmerTransforms.GetDecraftingRecipeIndex(item) == i || (ItemID.Sets.CraftingRecipeIndices[item]?.Contains(i) ?? false)) failures.Add($"{where}: shimmers back");
			}
			// The upgrades' shells are their recipes (planned whatever the setting): in both
			bool[] noShells = Enumerable.Range(0, Count).Select(i => RecipeSystem.IsShell(i) && !ItemPool.IsSpecial(RecipeSystem.OriginalResult(i)) && !RecipePlanner.StationShell(i) || (i < plan.Twin.Length && plan.Twin[i])).ToArray();
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
				if (!ItemPool.IsSpecial(item) && !RecipePlanner.StationShell(i) && without[item] < Findability.Never && withShells[item] < without[item]) failures.Add($"{Name(item)}: had at {withShells[item]} with its shell, {without[item]} without");
			}
			if (eligible == 0) Check.Skip("no item may get a shell recipe");
			Check.That(active.Count > 0, $"none of the {eligible} eligible shells is switched on");
			UltimateRandomizer mod = ModContent.GetInstance<UltimateRandomizer>();
			mod.Logger.Info($"UncraftableRecipesValid: {active.Count} of {eligible} eligible shells switched on ({string.Join(", ", byCategory.OrderBy(p => p.Key).Select(p => $"{p.Key} {p.Value}"))})");
			for (int k = 0; k < 8 && k < active.Count; k++)
			{
				int i = active[k * active.Count / 8];
				mod.Logger.Info($"Uncraftable: example {Name(RecipeSystem.OriginalResult(i))} x{Main.recipe[i].createItem.stack} (tier {plan.ShellTiers[i]}, doable at {Show(doable[i])}): " +
					$"{Ingredients(ingredients[i])} at {string.Join(", ", Main.recipe[i].requiredTile.Select(Stations.Name))}");
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
					// The upgrades' and the stations' shells are their recipes: planned whatever the setting
					bool upgrade = ItemPool.IsSpecial(RecipeSystem.OriginalResult(i)) || RecipePlanner.StationShell(i);
					if (!upgrade && (RecipePlanner.ShellActive(off, i) || off.Tiles[i] != null)) failures.Add($"{Name(RecipeSystem.OriginalResult(i))}: shell planned with the setting off");
					if (upgrade && RecipePlanner.ShellActive(on, i) != RecipePlanner.ShellActive(off, i)) failures.Add($"{Name(RecipeSystem.OriginalResult(i))}: upgrade shell planned with the setting on only");
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

		// A shell always gives its own item (an upgrade only from its own shell, the upgrade's recipe), is never swapped and
		// never a twin (copies are found among the vanilla recipes only)
		private static void UncraftableNoSpecials()
		{
			if (!CraftingConfig.Instance.RandomizeRecipes || RecipeSystem.RecipeCount == 0) Check.Skip("recipes are not randomized");
			RecipePlan plan = RecipePlanner.Get();
			List<string> failures = new();
			int shells = 0;
			for (int i = 0; i < Count; i++)
			{
				if (!RecipeSystem.IsShell(i)) continue;
				shells++;
				int item = RecipeSystem.OriginalResult(i);
				if (plan.Results[i].type != item) failures.Add($"{Name(item)}: shell planned to give {Name(plan.Results[i].type)}");
				if (plan.Twin[i]) failures.Add($"{Name(item)}: shell switched off as a copy");
				if (plan.SwapPartner[i] >= 0) failures.Add($"{Name(item)}: shell swapped with recipe {plan.SwapPartner[i]}");
				if (ItemPool.IsSpecial(item) && RecipePlanner.ShellActive(plan, i) && RecipePlanner.SpecialRecipe(item) != i) failures.Add($"{Name(item)}: an upgrade shell that is not its recipe");
				if (!ItemPool.IsSpecial(item) && plan.KeyIngredient[i]) failures.Add($"{Name(item)}: shell locked like a randomized recipe");
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

		// A rebuilt (or swapped) recipe is never doable before the tier of the item it gives: its own tier, or its true tier
		// when that is later. U and the vanilla doable tier both come a tier early (TierAllowance, gated drops in Findability):
		// a Chlorophyte Pickaxe (tier 5) was rebuilt for early hardmode with a Cursed Flame key. Not checked: tier 0 items,
		// upgrades (UpgradeRecipes), protected results and stations (their vanilla level), houses, shells, recipes switched off,
		// and repaired ones (vanilla ingredients: the item's own recipe, doable as early as its materials are found)
		private static void RecipeNotBeforeItsTier()
		{
			RequireRandomIngredients();
			if (!ProgressionConfig.Instance.TierGating || GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent) Check.Skip("only with tier gating in Coherent mode");
			RecipePlan plan = RecipePlanner.Get();
			int[] results = plan.Results.Select(r => r.type).ToArray();
			(int type, int stack)[][] ingredients = Enumerable.Range(0, Count).Select(i => plan.Ingredients[i] ?? RecipeSystem.OriginalIngredients(i)).ToArray();
			int[][] tiles = Enumerable.Range(0, Count).Select(i => plan.Tiles[i] ?? RecipeSystem.OriginalTiles(i)).ToArray();
			int[][] groups = Enumerable.Range(0, Count).Select(i => RecipePlanner.GroupsOf(plan, i)).ToArray();
			RecipePlanner.Availability(Findability.Tier, results, new int[results.Length], ingredients, tiles, out int[] doable, plan.Twin, groups);
			List<string> failures = new();
			List<string> named = new();
			int checkedCount = 0;
			for (int i = 0; i < Count; i++)
			{
				int item = results[i];
				if (plan.Ingredients[i] == null || plan.IngredientTiers[i] < 0 || plan.Repaired[i] || plan.Twin[i] || RecipeSystem.IsShell(i) || RecipeSystem.HouseRecipe(i)) continue;
				// Protected results, upgrades and the planned station placers keep their level (RecipePlanner.ResultFloor); the
				// other items placing a basic or progression station are checked (an Obsidian Sink was doable a tier early)
				if (!ItemPool.IsRandomizable(item) || ItemPool.IsSpecial(item)) continue;
				if (RecipePlanner.StationRecipe(i) || RecipePlanner.StationPlacer(item, out _)) continue;
				int floor = Tiers.Clamp(Math.Max(ItemPool.Info[item].Tier, ItemPool.Info[item].TrueTier));
				string where = $"{Name(RecipeSystem.OriginalResult(i))}{(plan.SwapPartner[i] >= 0 ? " -> " + Name(item) : "")} (tier {floor}, T {plan.IngredientTiers[i]}, " +
					$"key {(plan.PlannedKeys[i] > 0 ? Name(plan.PlannedKeys[i]) : "none")}): doable at {Show(doable[i])} ({Ingredients(plan.Ingredients[i])})";
				if (item == ItemID.ChlorophytePickaxe) named.Add(where);
				if (floor == 0) continue;
				checkedCount++;
				if (doable[i] < floor) failures.Add(where);
			}
			if (checkedCount == 0) Check.Skip("no rebuilt recipe above tier 0");
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"RecipeNotBeforeItsTier: {checkedCount} rebuilt recipes checked; Chlorophyte Pickaxe: {(named.Count == 0 ? "not rebuilt" : string.Join("; ", named))}");
			Check.All(failures, "Recipes doable before their item's tier");
		}

		// Planned ingredients are sane: no stack above RecipePlanner.StackCap (its count cap, CountCap, three times a vanilla
		// stack for a plain material), and a rebuilt recipe is worth at most twice its vanilla recipe (the item's own for a
		// swap), value-0 items counting their own recipe (EffectiveValue), each new ingredient rounded up by one unit at most.
		// No minimum since 1.7.2 (the counts follow the vanilla counts, cheaper than vanilla is fine): the share below half
		// and below a quarter is logged. ValueShort (LimitRecipeValue only, outside the upgrades) stays rare; the upgrades are
		// worth at least the upgrade (UpgradeRecipes). The cases of the 1.5.0 run are named
		private static void RecipeValueSane()
		{
			RequireRandomIngredients();
			RecipePlan plan = RecipePlanner.Get();
			List<string> failures = new();
			List<string> low = new();
			List<string> shortOnes = new();
			int rebuilt = 0, valueShort = 0, belowHalf = 0;
			int[] named = { ItemID.ManaCrystal, ItemID.LifeCrystal, ItemID.SoulBottleFright, ItemID.LunarBlockNebula, ItemID.LunarBlockStardust, ItemID.SolarBrick, ItemID.TrueNightsEdge };
			UltimateRandomizer mod = ModContent.GetInstance<UltimateRandomizer>();
			for (int i = 0; i < Count; i++)
			{
				if (plan.Ingredients[i] == null || plan.Twin[i]) continue;
				int original = RecipeSystem.OriginalResult(i);
				string name = Name(original) + (plan.SwapPartner[i] >= 0 ? " -> " + Name(plan.Results[i].type) : "");
				foreach ((int type, int stack) in plan.Ingredients[i])
				{
					int cap = RecipePlanner.StackCap(plan, i, type);
					if (stack > cap) failures.Add($"{name}: {stack} {Name(type)}, at most {cap}");
				}
				long before = RecipeSystem.OriginalIngredientValue(plan.SwapPartner[i] >= 0 ? plan.SwapPartner[i] : i);
				long after = plan.Ingredients[i].Sum(p => RecipeSystem.EffectiveValue(p.type) * p.stack);
				if (named.Contains(original) || named.Contains(plan.Results[i].type)) mod.Logger.Info($"RecipeValueSane: {name}: {before} -> {after} ({Ingredients(plan.Ingredients[i])})");
				if (SpecialRecipe(plan, i) || plan.IngredientTiers[i] < 0 || plan.Repaired[i] || RecipeSystem.HouseRecipe(i)) continue;
				rebuilt++;
				long roundUp = plan.Ingredients[i].Where(p => RecipePlanner.IngredientReplaceable(p.type)).Sum(p => RecipeSystem.EffectiveValue(p.type));
				string where = $"{name}: {before} -> {after} ({Ingredients(plan.Ingredients[i])})";
				if (after > before * RecipePlanner.MaxValueFactor + Item.silver + roundUp) failures.Add(where);
				if (after < before / 2) belowHalf++;
				if (after < before / 4 && low.Count < 10) low.Add(where);
				if (plan.ValueShort[i])
				{
					valueShort++;
					if (shortOnes.Count < 10) shortOnes.Add(where);
				}
			}
			if (rebuilt == 0) Check.Skip("no rebuilt recipe");
			mod.Logger.Info($"RecipeValueSane: {rebuilt} rebuilt recipes, {belowHalf} below half their vanilla value (allowed: counts follow the vanilla counts){(low.Count == 0 ? "" : "; below a quarter: " + string.Join("; ", low))}; " +
				$"{valueShort} short of the value LimitRecipeValue asks{(shortOnes.Count == 0 ? "" : ": " + string.Join("; ", shortOnes))}");
			Check.That(valueShort <= Math.Max(5, rebuilt / 50), $"{valueShort} of {rebuilt} rebuilt recipes short of their value");
			Check.All(failures, "Insane ingredient stacks or values");
		}

		// A protected result whose ingredients are rebuilt waits for U, the unlocked tier of its true tier, like any other
		// (RecipePlanner.Gated): never doable before it. The Key of Night and the Key of Light are never doable before hardmode,
		// whatever their recipe. Protected stations keep their vanilla level (not checked here: ProtectedRecipesDoable)
		private static void ProtectedRecipesGated()
		{
			RequireRandomIngredients();
			if (!ProgressionConfig.Instance.TierGating || GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent) Check.Skip("only with tier gating in Coherent mode");
			RecipePlan plan = RecipePlanner.Get();
			int[] results = plan.Results.Select(r => r.type).ToArray();
			(int type, int stack)[][] ingredients = Enumerable.Range(0, Count).Select(i => plan.Ingredients[i] ?? RecipeSystem.OriginalIngredients(i)).ToArray();
			int[][] tiles = Enumerable.Range(0, Count).Select(i => plan.Tiles[i] ?? RecipeSystem.OriginalTiles(i)).ToArray();
			int[][] groups = Enumerable.Range(0, Count).Select(i => RecipePlanner.GroupsOf(plan, i)).ToArray();
			RecipePlanner.Availability(Findability.Tier, results, new int[results.Length], ingredients, tiles, out int[] doable, plan.Twin, groups);
			List<string> failures = new();
			List<string> keys = new();
			int checkedCount = 0;
			for (int i = 0; i < Count; i++)
			{
				if (Main.recipe[i].Disabled || RecipeSystem.IsShell(i) || RecipeSystem.LeftAlone(i)) continue;
				int item = results[i];
				string where = $"{Name(item)} ({Ingredients(ingredients[i])}): doable at {Show(doable[i])}";
				if (item is ItemID.NightKey or ItemID.LightKey)
				{
					keys.Add(where);
					if (doable[i] < (int)Tier.Hardmode) failures.Add($"{where}, before hardmode");
				}
				if (RecipeSystem.Touchable(i) || !RecipeSystem.IngredientsTouchable(i) || plan.Ingredients[i] == null || ItemPool.IsSpecial(item)) continue;
				if (Stations.PlacedTile(item, out int tile) && Stations.Info(tile).Kind is StationKind.Basic or StationKind.Progression) continue;
				// A hard gate's unlock tier has no allowance (UnlockTierOf), as the planner's
				int u = Tiers.Clamp(ItemPool.UnlockTierOf(item));
				checkedCount++;
				if (plan.ResultTiers[i] != u) failures.Add($"{where}: result tier {plan.ResultTiers[i]}, {u} expected");
				if (doable[i] < u) failures.Add($"{where}, before its tier {u}");
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"ProtectedRecipesGated: {checkedCount} protected results with rebuilt ingredients; keys: {(keys.Count == 0 ? "no recipe" : string.Join("; ", keys))}");
			Check.All(failures, "Protected recipes doable before their tier");
		}

		// A pickaxe or drill recipe never gets a new ingredient (an ore or a bar, or anything made of one) that needs its own pick
		// power or more to be mined (RecipePlanner.MineRequirement): no Orichalcum or Titanium Pickaxe made of Chlorophyte Bars.
		// Its own vanilla ingredients and the protected ones kept stay allowed
		private static void PickaxeIngredientsBelowLevel()
		{
			RequireRandomIngredients();
			RecipePlan plan = RecipePlanner.Get();
			List<string> failures = new();
			List<string> examples = new();
			// Keys (planned, or placed by LockResults) may take an unfit material when their tier has nothing else: logged apart
			List<string> keys = new();
			int tested = 0;
			for (int i = 0; i < Count; i++)
			{
				int item = plan.Results[i].type;
				int pick = RecipePlanner.PickPower(item);
				if (pick <= 0 || plan.Ingredients[i] == null || plan.Twin[i]) continue;
				tested++;
				string where = $"{Name(item)} (pick {pick}: {Ingredients(plan.Ingredients[i])})";
				if (item is ItemID.OrichalcumPickaxe or ItemID.TitaniumPickaxe or ItemID.AdamantitePickaxe or ItemID.ChlorophytePickaxe) examples.Add(where);
				foreach ((int type, _) in plan.Ingredients[i])
				{
					if (!RecipePlanner.IngredientReplaceable(type) || RecipePlanner.PickaxeAllowed(type, item)) continue;
					string bad = $"{where}: {Name(type)} needs pick {RecipePlanner.MineRequirement(type)}";
					if (type == plan.PlannedKeys[i] || plan.LockKeys != null && type == plan.LockKeys[i]) keys.Add(bad);
					else failures.Add(bad);
				}
			}
			if (tested == 0) Check.Skip("no rebuilt pickaxe recipe");
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"PickaxeIngredientsBelowLevel: {tested} pickaxe recipes; {plan.UnfitKeys} unfit keys in the plan, {keys.Count} of them in pickaxes"
				+ $"{(keys.Count == 0 ? "" : ": " + string.Join("; ", keys.Take(10)))}; {string.Join("; ", examples)}");
			Check.All(failures, "Pickaxes made of what they can't mine");
		}

		// The house items (Torch, Wood Platform, Wooden Door) have a simple random recipe: one or two new ingredients, start
		// materials only (never a fish, an ore bar nor anything crafted: a Torch was a Platinum Ore and a Flounder), a few of each
		// (HouseStackMax, or the vanilla amount), worth at most twice the vanilla recipe (stacks at 1 apart), at a basic station
		// or none, never its vanilla recipe again
		private static void HouseRecipesSimple()
		{
			RequireRandomIngredients();
			RecipePlan plan = RecipePlanner.Get();
			HashSet<int> start = RecipePlanner.StartMaterials(true);
			List<string> failures = new();
			List<string> examples = new();
			for (int i = 0; i < Count; i++)
			{
				if (!RecipeSystem.HouseRecipe(i) || RecipeSystem.VanillaDisabled(i) || plan.Twin[i] || plan.Ingredients[i] == null) continue;
				(int type, int stack)[] ingredients = plan.Ingredients[i];
				string where = $"{Name(plan.Results[i].type)} x{plan.Results[i].stack} ({Ingredients(ingredients)})";
				examples.Add(where);
				(int type, int stack)[] original = RecipeSystem.OriginalIngredients(i);
				List<(int type, int stack)> added = ingredients.Where(p => RecipePlanner.IngredientReplaceable(p.type)).ToList();
				if (added.Count == 0) failures.Add($"{where}: no new ingredient");
				// Rebuilt, never back to its vanilla recipe (the same types in the same amounts)
				if (ingredients.Length == original.Length && ingredients.OrderBy(p => p.type).ThenBy(p => p.stack).SequenceEqual(original.OrderBy(p => p.type).ThenBy(p => p.stack)))
				{
					failures.Add($"{where}: its vanilla recipe");
				}
				if (added.Count > RecipePlanner.HouseIngredientMax) failures.Add($"{where}: {added.Count} ingredients, at most {RecipePlanner.HouseIngredientMax}");
				int most = Math.Max(RecipePlanner.HouseStackMax, original.Sum(o => o.stack));
				foreach ((int type, int stack) in added)
				{
					if (!start.Contains(type)) failures.Add($"{where}: {Name(type)} is no start material");
					if (stack > most) failures.Add($"{where}: {stack} {Name(type)}, at most {most}");
				}
				long before = RecipeSystem.OriginalIngredientValue(i);
				long after = ingredients.Sum(p => RecipeSystem.EffectiveValue(p.type) * p.stack);
				if (after > before * 2 + Item.silver && ingredients.Any(p => p.stack > 1)) failures.Add($"{where}: worth {after}, vanilla {before}");
				foreach (int tile in plan.Tiles[i] ?? RecipeSystem.OriginalTiles(i))
				{
					if (Stations.Info(tile).Kind != StationKind.Basic) failures.Add($"{where}: needs {Stations.Name(tile)}");
				}
			}
			if (examples.Count == 0) Check.Skip("no house recipe rebuilt");
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"HouseRecipesSimple: {string.Join("; ", examples)}");
			Check.All(failures, "House recipes not simple");
		}

		// No new ingredient asks for more than its count cap (RecipePlanner.CountCap, through StackCap): a craft-only item
		// (never found: each unit is a craft) CraftedIngredientStackCap (a little more when its recipe is cheap), a slow one
		// (gems, Fallen Stars, fish, critters, shards, souls) SlowIngredientStackCap. The vanilla stacks a recipe keeps are not
		// new. The largest craft-only and slow counts are logged
		private static void IngredientCountCaps()
		{
			RequireRandomIngredients();
			RecipePlan plan = RecipePlanner.Get();
			List<string> failures = new();
			List<(int stack, string where)> crafted = new();
			List<(int stack, string where)> slow = new();
			int checkedCount = 0;
			for (int i = 0; i < Count; i++)
			{
				if (plan.Ingredients[i] == null || plan.Twin[i]) continue;
				HashSet<(int, int)> vanilla = RecipeSystem.OriginalIngredients(i).ToHashSet();
				foreach ((int type, int stack) p in plan.Ingredients[i])
				{
					if (vanilla.Contains(p) || !RecipePlanner.IngredientReplaceable(p.type)) continue;
					checkedCount++;
					int cap = RecipePlanner.StackCap(plan, i, p.type);
					string where = $"{p.stack} {Name(p.type)} in {Name(plan.Results[i].type)}";
					if (p.stack > cap) failures.Add($"{where}, at most {cap}");
					if (RecipePlanner.OneOff(p.type)) continue;
					if (RecipePlanner.CraftOnly(p.type)) crafted.Add((p.stack, where));
					else if (RecipePlanner.SlowIngredient(p.type)) slow.Add((p.stack, where));
				}
			}
			if (checkedCount == 0) Check.Skip("no new ingredient");
			string Top(List<(int stack, string where)> list) => list.Count == 0 ? "none" : string.Join("; ", list.OrderByDescending(x => x.stack).ThenBy(x => x.where, StringComparer.Ordinal).Take(5).Select(x => x.where));
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"IngredientCountCaps: {checkedCount} new ingredients, {crafted.Count} craft-only (largest: {Top(crafted)}), {slow.Count} slow (largest: {Top(slow)})");
			Check.All(failures, "Ingredients above their count cap");
		}

		// The new ingredients of a rebuilt recipe (not a shell, an upgrade nor a house item) count, in all, at most the vanilla
		// count of the ingredients they replace (RecipePlanner.CountBase: the item's own recipe for a swap) times
		// MaxCountFactor, rounded up, plus one per line (each one rounded, at least one); a key added as a new ingredient may
		// add the smallest count. Counts no longer follow the value: the share of new lines at the plain cap
		// (IngredientStackCap: 638 lines at 30 in the 1.7.0 export) is logged, with the Meteorite Bar's recipe (30 Ash Blocks and
		// 29 walls then). A rare ingredient nothing gives may have been replaced on top (a few tolerated)
		private static void IngredientCountsFromVanilla()
		{
			RequireRandomIngredients();
			if (CraftingConfig.Instance.LimitRecipeValue) Check.Skip("LimitRecipeValue raises counts to the result's value");
			RecipePlan plan = RecipePlanner.Get();
			int cap = CraftingConfig.Instance.IngredientStackCap;
			List<string> failures = new();
			List<string> meteorite = new();
			int checkedCount = 0, lines = 0, atCap = 0;
			for (int i = 0; i < Count; i++)
			{
				if (plan.Ingredients[i] == null || plan.Twin[i]) continue;
				int item = plan.Results[i].type;
				if (item == ItemID.MeteoriteBar) meteorite.Add(Ingredients(plan.Ingredients[i]));
				if (RecipeSystem.IsShell(i) || SpecialRecipe(plan, i) || RecipeSystem.HouseRecipe(i)) continue;
				int proxy = plan.SwapPartner[i] >= 0 && !plan.Repaired[i] ? plan.SwapPartner[i] : i;
				List<(int type, int stack)> added = plan.Ingredients[i].Where(p => RecipePlanner.IngredientReplaceable(p.type)).ToList();
				if (added.Count == 0) continue;
				checkedCount++;
				lines += added.Count;
				atCap += added.Count(p => p.stack >= cap);
				int total = added.Sum(p => p.stack);
				int most = (int)Math.Ceiling(RecipePlanner.CountBase(proxy, item) * RecipePlanner.MaxCountFactor) + added.Count + (plan.KeyIngredient[i] ? added.Min(p => p.stack) : 0);
				string where = $"{Name(RecipeSystem.OriginalResult(i))}{(proxy != i ? " -> " + Name(item) : "")}: {Ingredients(plan.Ingredients[i])} (vanilla {Ingredients(RecipeSystem.OriginalIngredients(proxy))})";
				if (total > most) failures.Add($"{where}, {total} > {most}");
				if (added.Any(p => p.stack < 1)) failures.Add($"{where}, an empty stack");
			}
			if (checkedCount == 0) Check.Skip("no rebuilt recipe");
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"IngredientCountsFromVanilla: {checkedCount} recipes, {lines} new lines, {atCap} at the cap of {cap} ({(lines == 0 ? 0 : atCap / (double)lines):P1}); " +
				$"Meteorite Bar: {(meteorite.Count == 0 ? "not rebuilt" : string.Join(" | ", meteorite))}{(failures.Count == 0 ? "" : "; over: " + string.Join("; ", failures.Take(10)))}");
			Check.That(failures.Count <= Math.Max(3, checkedCount / 200), $"{failures.Count} recipes with more new ingredients than their vanilla count allows: {string.Join("; ", failures.Take(10))}");
		}

		// No live planned recipe needs an ingredient that is never had with this plan (a group slot: its best member), nor
		// does a recipe kept with its vanilla ingredients when one of them is randomizable and never had (it gets a
		// replacement, RecipePlanner.KeepOriginals): a Cell Phone of an Ice Mirror, a Cooked Marshmallow of a Marshmallow
		private static void NoNeverIngredients()
		{
			RequireRandomIngredients();
			RecipePlan plan = RecipePlanner.Get();
			List<string> failures = new();
			List<string> keptVanilla = new();
			int checkedCount = 0, leftAlone = 0;
			// Other mods' recipes left alone (Magic Storage's Cooked Marshmallow needs a campfire nearby: a condition of its mod)
			// keep their ingredients: a vanilla ingredient of theirs is had somewhere, a shop included (the plan counts none:
			// a shop-only item's added slot, or any possible vanilla shop entry when shops are not randomized)
			List<ShopPermutation.Place> places = null;
			bool Sold(int type)
			{
				if (ShopPermutation.Seller(type, out _, out _)) return true;
				if (EconomyConfig.Instance.RandomizeShops) return false;
				places ??= ShopPermutation.CollectPlaces(SeedSystem.WorldSeed);
				return places.Any(p => p.Original == type && p.Possible);
			}
			for (int i = 0; i < Count; i++)
			{
				if (Main.recipe[i].Disabled || plan.Twin[i]) continue;
				if (RecipeSystem.LeftAlone(i))
				{
					leftAlone++;
					foreach ((int type, _) in RecipeSystem.OriginalIngredients(i))
					{
						if (type <= 0 || type >= ItemID.Count || !ItemPool.IsRandomizable(type) || IngredientAvail(plan, i, type) < Findability.Never || Sold(type)) continue;
						failures.Add($"{Name(type)} in {Name(RecipeSystem.OriginalResult(i))} (left alone: {Main.recipe[i].Mod?.Name ?? "?"}), had nowhere");
					}
					continue;
				}
				bool planned = plan.Ingredients[i] != null;
				if (!planned && !RecipePlanner.Rebuildable(i)) continue;
				checkedCount++;
				foreach ((int type, _) in plan.Ingredients[i] ?? RecipeSystem.OriginalIngredients(i))
				{
					if (IngredientAvail(plan, i, type) < Findability.Never) continue;
					string where = $"{Name(type)} in {Name(plan.Results[i].type)}{(planned ? "" : " (vanilla ingredients)")}";
					// A protected ingredient keeps its own (vanilla) sources: logged only
					if (!ItemPool.IsRandomizable(type)) keptVanilla.Add(where);
					else failures.Add(where);
				}
			}
			if (checkedCount == 0) Check.Skip("no recipe planned");
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"NoNeverIngredients: {checkedCount} recipes, {leftAlone} left alone; protected ingredients never had: {(keptVanilla.Count == 0 ? "none" : string.Join("; ", keptVanilla.Take(10)))}");
			Check.All(failures, "Recipes needing an ingredient never had");
		}

		// No live recipe has an empty ingredient list (a free craft): a shell is on only with ingredients, a planned recipe
		// always gets some. Other mods' untouched recipes without ingredients are only logged
		private static void NoEmptyLiveRecipes()
		{
			RequireApplied();
			RecipePlan plan = RecipePlanner.Get();
			List<string> failures = new();
			List<string> untouched = new();
			int live = 0;
			for (int i = 0; i < Count; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (recipe.Disabled) continue;
				live++;
				if (plan.Ingredients[i] != null && plan.Ingredients[i].Length == 0) failures.Add($"{Name(plan.Results[i].type)}: planned with no ingredient");
				if (recipe.requiredItem.Count > 0) continue;
				string where = $"{Name(recipe.createItem.type)} (recipe {i}{(RecipeSystem.IsShell(i) ? ", shell" : "")})";
				if (RecipeSystem.IsShell(i) || plan.Ingredients[i] != null || RecipeSystem.IsRandomized(i)) failures.Add(where);
				else untouched.Add(where);
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"NoEmptyLiveRecipes: {live} live recipes; untouched ones without ingredients: {(untouched.Count == 0 ? "none" : string.Join(", ", untouched.Take(10)))}");
			Check.All(failures, "Live recipes without ingredients");
		}

		// A swapped result never lands on a station later than its own tier and the allowance (RecipePlanner.SwapStationCap,
		// StationLevel: a Tungsten Bow at the Lunar Crafting Station, a Thorn Chakram at the Crystal Ball, Glass at the Decay
		// Chamber). The swapped recipes whose station is only had later with this plan are logged
		private static void SwapStationsInTime()
		{
			RequireApplied();
			RecipePlan plan = RecipePlanner.Get();
			List<string> failures = new();
			List<string> late = new();
			int swapped = 0;
			for (int i = 0; i < Count; i++)
			{
				if (plan.SwapPartner[i] < 0 || plan.Twin[i] || Main.recipe[i].Disabled) continue;
				swapped++;
				int item = plan.Results[i].type;
				int cap = RecipePlanner.SwapStationCap(item);
				foreach (int tile in plan.Tiles[i] ?? RecipeSystem.OriginalTiles(i))
				{
					string where = $"{Name(item)} (recipe of {Name(RecipeSystem.OriginalResult(i))}) at {Stations.Name(tile)}";
					if (RecipePlanner.StationLevel(tile) > cap) failures.Add($"{where}: station level {RecipePlanner.StationLevel(tile)}, at most {cap}");
					int had = RecipePlanner.StationTier(tile, plan.Availability);
					if (had > cap) late.Add($"{where}: placed from {Show(had)}");
				}
			}
			if (swapped == 0) Check.Skip("no swapped recipe");
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"SwapStationsInTime: {swapped} swapped recipes, {late.Count} on a station had after their cap: {(late.Count == 0 ? "none" : string.Join("; ", late.Take(10)))}");
			Check.All(failures, "Swapped results on late stations");
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

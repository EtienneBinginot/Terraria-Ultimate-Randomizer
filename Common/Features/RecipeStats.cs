using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	// How much of the vanilla recipes the plan changes, and why the others keep their result (test RecipeRandomizationShare,
	// /rdebug recipes): every recipe keeps its item (no result swap since 1.8.0), most with new ingredients. Read from the
	// plan, the same on every machine.
	public class RecipeShare
	{
		// Vanilla recipes (not disabled, not added by another mod)
		public int Total;
		public int ResultChanged;
		public int AnyChanged;
		public int Modded;
		// Reason -> recipes keeping their result
		public readonly Dictionary<string, List<int>> Kept = new();

		public double ResultShare => Total == 0 ? 1 : ResultChanged / (double)Total;

		public double AnyShare => Total == 0 ? 1 : AnyChanged / (double)Total;

		public string Summary()
		{
			IEnumerable<string> reasons = Kept.OrderByDescending(pair => pair.Value.Count).Select(pair =>
				$"{pair.Key} {pair.Value.Count} ({string.Join(", ", pair.Value.Take(5).Select(i => Lang.GetItemNameValue(RecipeSystem.OriginalResult(i))))})");
			return $"Recipes: result changed {ResultShare:P1} ({ResultChanged}/{Total}), result or ingredients changed {AnyShare:P1} ({AnyChanged}/{Total}); "
				+ $"kept: {string.Join("; ", reasons)}; modded recipes left alone: {Modded}";
		}
	}

	public static class RecipeStats
	{
		public const string Protected = "protected result";
		public const string House = "house item";
		public const string Randomized = "randomized result kept";

		public static RecipeShare Compute()
		{
			RecipeShare share = new();
			RecipePlan plan = RecipePlanner.Get();
			int count = Math.Min(RecipeSystem.RecipeCount, Recipe.numRecipes);
			for (int i = 0; i < count; i++)
			{
				// A shell is a new recipe, not a vanilla one (its item no recipe made)
				if (Main.recipe[i].Disabled || RecipeSystem.IsShell(i)) continue;
				// Other mods' recipes left as they are (RandomizeModdedRecipes off); randomized ones count like the vanilla ones
				if (RecipeSystem.LeftAlone(i))
				{
					share.Modded++;
					continue;
				}
				share.Total++;
				int original = RecipeSystem.OriginalResult(i);
				bool resultChanged = plan.Results[i].type != original;
				if (resultChanged) share.ResultChanged++;
				if (resultChanged || plan.Ingredients[i] != null) share.AnyChanged++;
				if (resultChanged) continue;
				string reason;
				if (!ItemPool.IsRandomizable(original)) reason = Protected;
				else if (RecipeSystem.HouseRecipe(i)) reason = House;
				else reason = Randomized;
				if (!share.Kept.TryGetValue(reason, out List<int> list)) share.Kept[reason] = list = new List<int>();
				list.Add(i);
			}
			return share;
		}
	}
}

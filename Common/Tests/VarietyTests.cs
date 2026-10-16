using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Tests
{
	// Weight tables, surprise rolls, tier window and ore permutation
	public static class VarietyTests
	{
		public static void Register()
		{
			TestRunner.Register("SurpriseZero", SurpriseZero);
			TestRunner.Register("SurpriseHundred", SurpriseHundred);
			TestRunner.Register("SurpriseGatedAndProtected", SurpriseGatedAndProtected);
			TestRunner.Register("RecipeSurpriseRate", RecipeSurpriseRate);
			TestRunner.Register("WeightTablesSeparated", WeightTablesSeparated);
			TestRunner.Register("TierWindowVariety", TierWindowVariety);
			TestRunner.Register("OreMappingBijective", OreMappingBijective);
		}

		private static void RequireCoherent()
		{
			if (GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent) Check.Skip("only in Coherent mode");
		}

		// Randomizable items whose category can be surprised (not bags, boss summons or critters)
		private static int[] Originals(int count, int salt)
		{
			int[] all = Enumerable.Range(1, ItemPool.Info.Length - 1)
				.Where(t => ItemPool.IsRandomizable(t) && ItemPool.Info[t].Category is not (ItemCategory.Bag or ItemCategory.BossSummon or ItemCategory.Critter))
				.ToArray();
			Random rng = new(salt);
			return Enumerable.Range(0, count).Select(_ => all[rng.Next(all.Length)]).ToArray();
		}

		private static RollRequest Request(int original, int key, bool ignoreGating) => new()
		{
			Keys = new[] { 5150, key, original },
			SourceTier = ItemPool.TierOf(original),
			OriginalItem = original,
			Policy = CategoryPolicy.SameAsOriginal,
			TierJitterUp = 1,
			IgnoreGating = ignoreGating
		};

		private static void SurpriseZero()
		{
			RequireCoherent();
			List<string> failures = new();
			Random rng = new(1);
			int i = 0;
			foreach (int original in Originals(500, 11))
			{
				int item = ItemPool.RollReplacement(Request(original, i++, true), 0, rng);
				if (ItemPool.Info[item].Category != ItemPool.Info[original].Category) failures.Add($"{Lang.GetItemNameValue(original)} -> {Lang.GetItemNameValue(item)}");
			}
			Check.All(failures, "A 0% surprise changed the category");
		}

		// At 100%, the categories follow the loot weights
		private static void SurpriseHundred()
		{
			RequireCoherent();
			const int Rolls = 3000;
			CategoryWeights weights = LootConfig.Instance.DropCategoryWeights;
			int[] counts = new int[ItemCategories.Count];
			Random rng = new(2);
			int i = 0;
			foreach (int original in Originals(Rolls, 12))
			{
				int item = ItemPool.RollReplacement(Request(original, i++, true), 100, rng);
				counts[(int)ItemPool.Info[item].Category]++;
			}
			double total = Enumerable.Range(0, ItemCategories.Count).Sum(c => (double)weights.Get((ItemCategory)c));
			List<string> failures = new();
			for (int c = 0; c < ItemCategories.Count; c++)
			{
				double expected = weights.Get((ItemCategory)c) / total * 100;
				double actual = counts[c] * 100.0 / Rolls;
				if (Math.Abs(expected - actual) > 5) failures.Add($"{(ItemCategory)c}: {actual:0.#}% instead of {expected:0.#}%");
			}
			Check.All(failures, "Surprise rolls don't follow the loot weights");
		}

		private static void SurpriseGatedAndProtected()
		{
			RequireCoherent();
			ProgressionConfig progression = ProgressionConfig.Instance;
			int limit = progression.TierGating ? Tiers.Clamp(ProgressionSystem.UnlockedTier + progression.TierAllowance) : Tiers.Max;
			List<string> failures = new();
			Random rng = new(3);
			int i = 0;
			foreach (int original in Originals(1500, 13))
			{
				int item = ItemPool.RollReplacement(Request(original, i++, false), 100, rng);
				if (!ItemPool.IsRandomizable(item)) failures.Add($"{Lang.GetItemNameValue(item)} is protected or excluded");
				// The original's category may go up to its own tier (it already drops there in vanilla)
				int allowed = ItemPool.Info[item].Category == ItemPool.Info[original].Category ? Math.Max(limit, ItemPool.TierOf(original)) : limit;
				if (ItemPool.TierOf(item) > allowed) failures.Add($"{Lang.GetItemNameValue(item)} tier {ItemPool.TierOf(item)} > {allowed}");
			}
			Check.All(failures, "Surprise rolls ignore progression or protection");
		}

		// The share of recipes that change category stays close to the setting
		private static void RecipeSurpriseRate()
		{
			RequireCoherent();
			if (!RecipeSystem.Applied) Check.Skip("recipes are not randomized");
			float chance = CraftingConfig.Instance.RecipeSurpriseChance;
			int randomized = 0;
			int changedCategory = 0;
			for (int r = 0; r < RecipeSystem.RecipeCount && r < Recipe.numRecipes; r++)
			{
				int original = RecipeSystem.OriginalResult(r);
				int result = Main.recipe[r].createItem.type;
				if (result == original || !ItemPool.IsValid(result) || !ItemPool.IsValid(original)) continue;
				randomized++;
				if (ItemPool.Info[result].Category != ItemPool.Info[original].Category) changedCategory++;
			}
			Check.That(randomized >= 100, $"only {randomized} randomized recipes");
			double rate = changedCategory * 100.0 / randomized;
			Check.That(rate <= chance + 10, $"{rate:0.#}% of the recipes changed category, setting {chance}%");
			if (chance >= 10) Check.That(rate >= chance / 3, $"only {rate:0.#}% of the recipes changed category, setting {chance}%");
		}

		private static void WeightTablesSeparated()
		{
			WeightTable loot = LootConfig.Instance.DropCategoryWeights;
			WeightTable ingredients = CraftingConfig.Instance.IngredientWeights;
			double lootTotal = Enumerable.Range(0, ItemCategories.Count).Sum(c => loot.Get((ItemCategory)c));
			double lootGear = Enumerable.Range(0, ItemCategories.Count).Where(c => ItemCategories.IsGear((ItemCategory)c)).Sum(c => loot.Get((ItemCategory)c));
			Check.That(lootTotal > 0 && lootGear / lootTotal > 0.5, $"loot weights give only {lootGear / Math.Max(1, lootTotal):P0} of equipment");
			List<string> gearInIngredients = Enumerable.Range(0, ItemCategories.Count)
				.Where(c => ItemCategories.IsGear((ItemCategory)c) && ingredients.Get((ItemCategory)c) > 0)
				.Select(c => ((ItemCategory)c).ToString()).ToList();
			Check.All(gearInIngredients, "Ingredient weights can pick equipment");
		}

		// Early rolls must not keep giving the same few items
		private static void TierWindowVariety()
		{
			RequireCoherent();
			HashSet<int> gear = new();
			for (int i = 0; i < 600; i++)
			{
				RollRequest request = new() { Keys = new[] { 6160, i }, SourceTier = 0, OriginalItem = -1, Policy = CategoryPolicy.Weighted, TierJitterUp = 1 };
				int item = ItemPool.Roll(request);
				if (ItemCategories.IsGear(ItemPool.Info[item].Category)) gear.Add(item);
			}
			Check.That(gear.Count >= 25, $"only {gear.Count} different pieces of equipment in 600 early rolls");
		}

		private static void OreMappingBijective()
		{
			List<int> ores = Enumerable.Range(1, ItemPool.Info.Length - 1).Where(t => ItemPool.IsRandomizable(t) && OreMap.IsOre(t)).ToList();
			Check.That(ores.Count >= 8, $"only {ores.Count} ores");
			HashSet<int> images = new();
			List<string> failures = new();
			foreach (int ore in ores)
			{
				int image = OreMap.Get(ore);
				if (!images.Add(image)) failures.Add($"{Lang.GetItemNameValue(image)} given by two ores");
				if (!OreMap.IsOre(image)) failures.Add($"{Lang.GetItemNameValue(ore)} gives {Lang.GetItemNameValue(image)}, not an ore");
				if (ItemPool.TierOf(image) != ItemPool.TierOf(ore)) failures.Add($"{Lang.GetItemNameValue(ore)} -> {Lang.GetItemNameValue(image)} changes tier");
			}
			Check.All(failures, "Ore permutation");
		}
	}
}

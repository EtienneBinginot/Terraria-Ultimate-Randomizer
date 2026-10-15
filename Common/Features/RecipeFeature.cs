using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	// Recipe results are rewritten when a world is loaded (or progression changes), and restored when it is left.
	// Every machine computes the same results from the synced seed and progression.
	public class RecipeSystem : ModSystem
	{
		private static int[] originalTypes;
		private static int[] originalStacks;
		private static int[] recipeKeys;
		private static long[] ingredientValues;
		private static int[][] originalDecraft;
		private static bool applied;

		public override void PostSetupRecipes()
		{
			int count = Recipe.numRecipes;
			originalTypes = new int[count];
			originalStacks = new int[count];
			recipeKeys = new int[count];
			ingredientValues = new long[count];
			for (int i = 0; i < count; i++)
			{
				Recipe recipe = Main.recipe[i];
				originalTypes[i] = recipe.createItem.type;
				originalStacks[i] = recipe.createItem.stack;
				// Keyed on names, not on the recipe index, so the mapping survives mod updates
				int key = DetRandom.StableHash(DetRandom.ItemName(recipe.createItem.type));
				long value = 0;
				foreach (Item ingredient in recipe.requiredItem)
				{
					key = DetRandom.Hash(key, DetRandom.StableHash(DetRandom.ItemName(ingredient.type)), ingredient.stack);
					value += (long)Math.Max(StackRules.ValueOf(ingredient.type), 1) * ingredient.stack;
				}
				recipeKeys[i] = key;
				ingredientValues[i] = value;
			}
			int[][] decraft = ItemID.Sets.CraftingRecipeIndices;
			originalDecraft = new int[decraft.Length][];
			Array.Copy(decraft, originalDecraft, decraft.Length);

			SeedSystem.OnSeedChanged += Refresh;
			ProgressionSystem.OnTierChanged += Refresh;
		}

		public override void Unload()
		{
			Restore();
			originalTypes = null;
			originalStacks = null;
			recipeKeys = null;
			ingredientValues = null;
			originalDecraft = null;
		}

		private static void Refresh()
		{
			if (SeedSystem.WorldReady && CraftingConfig.Instance.RandomizeRecipes && ItemPool.Ready) Apply();
			else Restore();
		}

		private static void Apply()
		{
			if (originalTypes == null) return;
			CraftingConfig cfg = CraftingConfig.Instance;
			bool protectPickaxes = ProgressionConfig.Instance.ProtectPickaxeProgression;
			HashSet<int> newResults = new();
			for (int i = 0; i < originalTypes.Length && i < Recipe.numRecipes; i++)
			{
				Recipe recipe = Main.recipe[i];
				int original = originalTypes[i];
				Item result = recipe.createItem;
				bool keep = !ItemPool.IsRandomizable(original)
					|| (protectPickaxes && ContentSamples.ItemsByType.TryGetValue(original, out Item sample) && sample.pick > 0);
				if (keep)
				{
					SetResult(result, original, originalStacks[i]);
					continue;
				}
				int replacement = original;
				int stack = originalStacks[i];
				long maxValue = (long)(ingredientValues[i] * cfg.RecipeValueGuard) + Item.gold;
				for (int attempt = 0; attempt < 6; attempt++)
				{
					RollRequest request = new()
					{
						Keys = new[] { RollKey.Recipe, recipeKeys[i], attempt },
						SourceTier = ItemPool.Info[original].Tier,
						OriginalItem = original,
						Policy = CategoryPolicy.SameAsOriginal,
						TierJitterDown = 0,
						TierJitterUp = 0,
						AllowJackpot = false
					};
					replacement = ItemPool.Roll(request);
					stack = StackRules.Scaled(original, originalStacks[i], replacement);
					// Value guard: crafting must not turn cheap ingredients into a fortune
					if ((long)StackRules.ValueOf(replacement) * stack <= maxValue) break;
				}
				SetResult(result, replacement, stack);
				if (replacement != original) newResults.Add(replacement);
			}
			RestoreDecraft();
			if (cfg.DisableDecraftOfRandomizedResults)
			{
				// Otherwise: craft a random result from cheap ingredients, shimmer it, get its expensive vanilla ingredients
				foreach (int type in newResults)
				{
					if (type < ItemID.Sets.CraftingRecipeIndices.Length) ItemID.Sets.CraftingRecipeIndices[type] = Array.Empty<int>();
				}
			}
			applied = true;
			RefreshCraftingMenu();
		}

		private static void Restore()
		{
			if (!applied || originalTypes == null) return;
			for (int i = 0; i < originalTypes.Length && i < Recipe.numRecipes; i++)
			{
				SetResult(Main.recipe[i].createItem, originalTypes[i], originalStacks[i]);
			}
			RestoreDecraft();
			applied = false;
			RefreshCraftingMenu();
		}

		private static void RestoreDecraft()
		{
			if (originalDecraft != null) Array.Copy(originalDecraft, ItemID.Sets.CraftingRecipeIndices, Math.Min(originalDecraft.Length, ItemID.Sets.CraftingRecipeIndices.Length));
		}

		private static void SetResult(Item result, int type, int stack)
		{
			if (result.type != type) result.SetDefaults(type);
			result.stack = stack;
		}

		private static void RefreshCraftingMenu()
		{
			if (!Main.dedServ && !Main.gameMenu) Recipe.FindRecipes();
		}
	}
}

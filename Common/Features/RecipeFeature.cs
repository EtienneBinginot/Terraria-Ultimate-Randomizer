using System;
using System.Collections.Generic;
using System.Linq;
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

		public static int OriginalResult(int recipeIndex) => originalTypes != null && recipeIndex < originalTypes.Length ? originalTypes[recipeIndex] : 0;

		public static int OriginalStack(int recipeIndex) => originalStacks != null && recipeIndex < originalStacks.Length ? originalStacks[recipeIndex] : 0;

		public static int RecipeCount => originalTypes?.Length ?? 0;

		public static bool Applied => applied;

		public static int[] OriginalDecraft(int type) => originalDecraft != null && type < originalDecraft.Length ? originalDecraft[type] : null;

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
				Obtainability.RecipeResults.Add(recipe.createItem.type);
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
			bool protectMining = ProgressionConfig.Instance.ProtectPickaxeProgression;
			HashSet<int> newResults = new();
			bool[] changed = new bool[originalTypes.Length];
			for (int i = 0; i < originalTypes.Length && i < Recipe.numRecipes; i++)
			{
				Recipe recipe = Main.recipe[i];
				int original = originalTypes[i];
				Item result = recipe.createItem;
				if (KeepRecipe(original, protectMining))
				{
					SetResult(result, original, originalStacks[i]);
					continue;
				}
				(int replacement, int stack) = RollResult(i, recipe, cfg);
				SetResult(result, replacement, stack);
				if (replacement != original || stack != originalStacks[i])
				{
					changed[i] = true;
					newResults.Add(replacement);
				}
			}
			ApplyDecraftRules(changed, newResults, cfg.DisableDecraftOfRandomizedResults);
			applied = true;
			RefreshCraftingMenu();
		}

		// Pickaxes and bars (the mining chain: anvils, Molten Pickaxe...) are never randomized when protected
		private static bool KeepRecipe(int original, bool protectMining)
		{
			if (!ItemPool.IsRandomizable(original)) return true;
			if (!protectMining) return false;
			if (ItemPool.Info[original].Category == ItemCategory.OreBar) return true;
			return ContentSamples.ItemsByType.TryGetValue(original, out Item sample) && sample.pick > 0;
		}

		private static (int type, int stack) RollResult(int i, Recipe recipe, CraftingConfig cfg)
		{
			int original = originalTypes[i];
			int originalStack = originalStacks[i];
			long originalValue = (long)Math.Max(StackRules.ValueOf(original), 0) * originalStack;
			// Value guard: crafting must not turn cheap ingredients into a fortune (1 silver of tolerance)
			long maxValue = (long)(Math.Max(ingredientValues[i], originalValue) * cfg.RecipeValueGuard) + Item.silver;
			int ingredientCount = recipe.requiredItem.Sum(ingredient => ingredient.stack);
			for (int attempt = 0; attempt < 8; attempt++)
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
				int replacement = ItemPool.Roll(request);
				// A recipe must never give back one of its own ingredients (1 Wood -> 4 Wood would be infinite)
				if (recipe.requiredItem.Any(ingredient => ingredient.type == replacement)) continue;
				int stack = StackRules.Scaled(original, originalStack, replacement);
				if (StackRules.ValueOf(replacement) <= 0) stack = Math.Min(stack, Math.Max(1, ingredientCount));
				if ((long)StackRules.ValueOf(replacement) * stack <= maxValue) return (replacement, stack);
			}
			return (original, originalStack);
		}

		// Shimmer decrafting uses CraftingRecipeIndices[item] and the recipe's result stack: it must only point to
		// recipes that still produce that item, otherwise decrafting would duplicate ingredients
		private static void ApplyDecraftRules(bool[] changed, HashSet<int> newResults, bool disableNewResults)
		{
			RestoreDecraft();
			if (originalDecraft == null) return;
			int[][] decraft = ItemID.Sets.CraftingRecipeIndices;
			for (int type = 0; type < decraft.Length && type < originalDecraft.Length; type++)
			{
				int[] indices = originalDecraft[type];
				if (indices == null || indices.Length == 0) continue;
				if (indices.Any(index => index < changed.Length && changed[index]))
				{
					decraft[type] = indices.Where(index => index >= changed.Length || !changed[index]).ToArray();
				}
			}
			if (disableNewResults)
			{
				// Otherwise: craft a random result from cheap ingredients, shimmer it, get its expensive vanilla ingredients
				foreach (int type in newResults)
				{
					if (type < decraft.Length) decraft[type] = Array.Empty<int>();
				}
			}
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

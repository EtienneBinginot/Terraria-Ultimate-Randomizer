using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	// Recipes (results, ingredients, stations) are rewritten when a world is loaded (or progression changes), and restored
	// when it is left. Every machine computes the same recipes from the synced seed and progression.
	public class RecipeSystem : ModSystem
	{
		private static int[] originalTypes;
		private static int[] originalStacks;
		private static (int type, int stack)[][] originalIngredients;
		private static int[][] originalTiles;
		private static int[] recipeKeys;
		private static long[] ingredientValues;
		private static bool[] modded;
		private static int[][] originalDecraft;
		private static bool applied;

		public static int OriginalResult(int recipeIndex) => originalTypes != null && recipeIndex < originalTypes.Length ? originalTypes[recipeIndex] : 0;

		public static int OriginalStack(int recipeIndex) => originalStacks != null && recipeIndex < originalStacks.Length ? originalStacks[recipeIndex] : 0;

		public static (int type, int stack)[] OriginalIngredients(int recipeIndex) => originalIngredients[recipeIndex];

		public static int[] OriginalTiles(int recipeIndex) => originalTiles[recipeIndex];

		public static int RecipeKey(int recipeIndex) => recipeKeys[recipeIndex];

		public static long OriginalIngredientValue(int recipeIndex) => ingredientValues[recipeIndex];

		public static int RecipeCount => originalTypes?.Length ?? 0;

		public static bool Applied => applied;

		public static int[] OriginalDecraft(int type) => originalDecraft != null && type < originalDecraft.Length ? originalDecraft[type] : null;

		public override void PostSetupRecipes()
		{
			if (CreateQuickLookups == null) Mod.Logger.Error("Recipe.CreateRequiredItemQuickLookups not found: randomized ingredients will not show correctly in the crafting menu.");
			int count = Recipe.numRecipes;
			originalTypes = new int[count];
			originalStacks = new int[count];
			originalIngredients = new (int, int)[count][];
			originalTiles = new int[count][];
			recipeKeys = new int[count];
			ingredientValues = new long[count];
			modded = new bool[count];
			for (int i = 0; i < count; i++)
			{
				Recipe recipe = Main.recipe[i];
				originalTypes[i] = recipe.createItem.type;
				originalStacks[i] = recipe.createItem.stack;
				originalIngredients[i] = recipe.requiredItem.Select(ingredient => (ingredient.type, ingredient.stack)).ToArray();
				originalTiles[i] = recipe.requiredTile.ToArray();
				modded[i] = recipe.Mod != null && recipe.Mod.Name != "ModLoader";
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

			GuaranteedSources.ExtraRequirements = RecipePlanner.VanillaRequirements;
			SeedSystem.OnSeedChanged += Refresh;
			ProgressionSystem.OnTierChanged += Refresh;
		}

		public override void Unload()
		{
			Restore();
			GuaranteedSources.ExtraRequirements = null;
			originalTypes = null;
			originalStacks = null;
			originalIngredients = null;
			originalTiles = null;
			recipeKeys = null;
			ingredientValues = null;
			modded = null;
			originalDecraft = null;
		}

		private static void Refresh()
		{
			if (SeedSystem.WorldReady && CraftingConfig.Instance.RandomizeRecipes && ItemPool.Ready) Apply();
			else Restore();
		}

		// Recipes whose result, ingredients and station may change: vanilla recipes of randomizable items, except the mining chain
		public static bool Touchable(int i) => !modded[i] && !KeepRecipe(originalTypes[i], ProgressionConfig.Instance.ProtectPickaxeProgression);

		private static void Apply()
		{
			if (originalTypes == null) return;
			CraftingConfig cfg = CraftingConfig.Instance;
			RecipePlan plan = RecipePlanner.Get();
			HashSet<int> newResults = new();
			bool[] changed = new bool[originalTypes.Length];
			for (int i = 0; i < originalTypes.Length && i < Recipe.numRecipes; i++)
			{
				Recipe recipe = Main.recipe[i];
				SetIngredients(recipe, plan.Ingredients[i] ?? originalIngredients[i]);
				SetTiles(recipe, plan.Tiles[i] ?? originalTiles[i]);
				int original = originalTypes[i];
				if (!Touchable(i))
				{
					SetResult(recipe.createItem, original, originalStacks[i]);
					continue;
				}
				long currentValue = recipe.requiredItem.Sum(item => (long)Math.Max(StackRules.ValueOf(item.type), 1) * item.stack);
				(int replacement, int stack) = RollResult(i, recipe.requiredItem.Select(item => item.type).ToList(), recipe.requiredItem.Sum(item => item.stack), lucky: false, Math.Min(currentValue, ingredientValues[i]));
				SetResult(recipe.createItem, replacement, stack);
				if (replacement != original || stack != originalStacks[i])
				{
					changed[i] = true;
					newResults.Add(replacement);
				}
				// Decrafting gives the current ingredients back: not when they are worth clearly more than before
				if (plan.Ingredients[i] != null && plan.IngredientValues[i] > ingredientValues[i] * 5 / 4) changed[i] = true;
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

		// The result of a randomized recipe. lucky: ignores the progression lock (what the recipe gives once unlocked)
		public static (int type, int stack) RollResult(int i, IList<int> ingredientTypes, int ingredientCount, bool lucky, long ingredientValue = -1)
		{
			if (ingredientValue < 0) ingredientValue = ingredientValues[i];
			CraftingConfig cfg = CraftingConfig.Instance;
			int original = originalTypes[i];
			int originalStack = originalStacks[i];
			long originalValue = (long)Math.Max(StackRules.ValueOf(original), 0) * originalStack;
			// Value guard: crafting must not turn cheap ingredients into a fortune (1 silver of tolerance)
			long maxValue = (long)(Math.Max(ingredientValue, originalValue) * cfg.RecipeValueGuard) + Item.silver;
			bool surprise = ItemPool.IsSurprise(original, cfg.RecipeSurpriseChance, DetRandom.Get(RollKey.Recipe, recipeKeys[i], RollKey.Surprise));
			for (int attempt = 0; attempt < 8; attempt++)
			{
				RollRequest request = new()
				{
					Keys = new[] { RollKey.Recipe, recipeKeys[i], attempt },
					SourceTier = ItemPool.Info[original].Tier,
					OriginalItem = original,
					Policy = surprise ? CategoryPolicy.Weighted : CategoryPolicy.SameAsOriginal,
					TierJitterDown = 0,
					TierJitterUp = 0,
					AllowJackpot = false,
					IgnoreGating = lucky
				};
				int replacement = ItemPool.Roll(request);
				// A recipe must never give back one of its own ingredients (1 Wood -> 4 Wood would be infinite)
				if (ingredientTypes.Contains(replacement)) continue;
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
				Recipe recipe = Main.recipe[i];
				SetResult(recipe.createItem, originalTypes[i], originalStacks[i]);
				SetIngredients(recipe, originalIngredients[i]);
				SetTiles(recipe, originalTiles[i]);
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

		private static void SetIngredients(Recipe recipe, (int type, int stack)[] ingredients)
		{
			bool same = recipe.requiredItem.Count == ingredients.Length;
			for (int k = 0; same && k < ingredients.Length; k++)
			{
				same = recipe.requiredItem[k].type == ingredients[k].type && recipe.requiredItem[k].stack == ingredients[k].stack;
			}
			if (same) return;
			recipe.requiredItem.Clear();
			foreach ((int type, int stack) in ingredients) recipe.requiredItem.Add(new Item(type, stack));
		}

		private static void SetTiles(Recipe recipe, int[] tiles)
		{
			if (recipe.requiredTile.SequenceEqual(tiles)) return;
			recipe.requiredTile.Clear();
			recipe.requiredTile.AddRange(tiles);
		}

		// tML caches each recipe's ingredients (Recipe.requiredItemQuickLookup) for the crafting menu; it is only built at
		// setup. Without a rebuild the menu checks the old ingredients while crafting consumes the new ones.
		private static readonly MethodInfo CreateQuickLookups = typeof(Recipe).GetMethod("CreateRequiredItemQuickLookups", BindingFlags.NonPublic | BindingFlags.Static);

		private static void RefreshCraftingMenu()
		{
			CreateQuickLookups?.Invoke(null, null);
			Recipe.UpdateWhichItemsAreMaterials();
			if (!Main.dedServ && !Main.gameMenu) Recipe.FindRecipes();
		}
	}
}

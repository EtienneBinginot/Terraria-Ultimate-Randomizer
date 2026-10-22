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

			GuaranteedSources.ExtraRequirements.Clear();
			GuaranteedSources.ExtraRequirements.Add(RecipePlanner.VanillaRequirements);
			GuaranteedSources.ExtraRequirements.Add(RecipePlanner.MiningRequirements);
			SeedSystem.OnSeedChanged += Refresh;
			ProgressionSystem.OnTierChanged += Refresh;
		}

		public override void Unload()
		{
			Restore();
			GuaranteedSources.ExtraRequirements.Clear();
			CraftedValueItem.ClearCache();
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
			RefreshOtherMods();
		}

		// Magic Storage indexes the recipes by result, ingredient and station once, when the game loads: without a rebuild its
		// crafting interface would keep the vanilla recipes. Recipe Browser reads Main.recipe live and needs nothing.
		// Its rebuild (MagicCache.RecalculateRecipeCaches) only works once per session as is: WorkManager.StopActiveWork
		// cancels a token it never recreates when its threads exist (they do after loading), and the recursion trees are
		// added again under the same recipes. Both are reset first, as a fresh load would find them
		private static void RefreshOtherMods()
		{
			if (!ModLoader.TryGetMod("MagicStorage", out Mod magicStorage) || magicStorage.Code == null) return;
			try
			{
				const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
				Assembly code = magicStorage.Code;
				code.GetType("MagicStorage.Common.Threading.WorkManager")?.GetField("_threads", Static)?.SetValue(null, null);
				object trees = code.GetType("MagicStorage.Common.Systems.RecurrentRecipes.RecursiveRecipe")?.GetField("recipeToRecursiveRecipe", Static)?.GetValue(null);
				trees?.GetType().GetMethod("Clear", Type.EmptyTypes)?.Invoke(trees, null);
				code.GetType("MagicStorage.Common.Systems.RecurrentRecipes.NodePool")?.GetMethod("ClearNodes", Static, Type.EmptyTypes)?.Invoke(null, null);
				code.GetType("MagicStorage.Common.Systems.MagicCache")?.GetMethod("RecalculateRecipeCaches", Static, Type.EmptyTypes)?.Invoke(null, null);
			}
			catch (Exception e)
			{
				ModContent.GetInstance<RecipeSystem>().Mod.Logger.Warn("Could not refresh Magic Storage's recipe caches", e);
			}
		}

		// Recipes whose result, ingredients and station may change: vanilla recipes of randomizable items, except the mining chain
		public static bool Touchable(int i) => !modded[i] && !KeepRecipe(originalTypes[i], ProgressionConfig.Instance.ProtectPickaxeProgression);

		// Recipes whose ingredients may change: protected results (boss summons, progression stations...) keep their result
		// and stations, but an ingredient that can't be found in time is replaced like in any recipe (protected ingredients
		// stay); modded recipes and excluded results stay as they are
		public static bool IngredientsTouchable(int i) => !modded[i] && ItemPool.IsValid(originalTypes[i]) && !ItemPool.Info[originalTypes[i]].Excluded;

		public static bool IsModded(int i) => modded != null && i >= 0 && i < modded.Length && modded[i];

		// A recipe kept only because the mining chain is protected
		public static bool KeptForMining(int i) => !modded[i] && ItemPool.IsRandomizable(originalTypes[i]) && KeepRecipe(originalTypes[i], ProgressionConfig.Instance.ProtectPickaxeProgression);

		// The live recipe differs from the vanilla one (result or ingredients): what it makes sells for its ingredients
		public static bool IsRandomized(int i)
		{
			if (!applied || originalTypes == null || i < 0 || i >= originalTypes.Length || i >= Recipe.numRecipes || modded[i]) return false;
			Recipe recipe = Main.recipe[i];
			if (recipe.createItem.type != originalTypes[i] || recipe.createItem.stack != originalStacks[i]) return true;
			return !recipe.requiredItem.Select(item => (item.type, item.stack)).SequenceEqual(originalIngredients[i]);
		}

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
				// Decrafting gives the current ingredients back: not when they are worth clearly more than before (protected
				// results too, their ingredients may be replaced)
				if (plan.Ingredients[i] != null && plan.IngredientValues[i] > ingredientValues[i] * 5 / 4) changed[i] = true;
				int original = originalTypes[i];
				if (!Touchable(i))
				{
					SetResult(recipe.createItem, original, originalStacks[i]);
					continue;
				}
				(int replacement, int stack) = AppliedResult(plan, i);
				SetResult(recipe.createItem, replacement, stack);
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

		// The planned result once its tier is unlocked (the plan is built around it); before, a stand-in of an unlocked
		// tier rolled with the same arguments, which the plan never relies on
		public static (int type, int stack) AppliedResult(RecipePlan plan, int i)
		{
			if (ProgressionSystem.UnlockedTier >= plan.ResultTiers[i]) return plan.Results[i];
			return RecipePlanner.ResultFor(i, plan.Ingredients[i] ?? originalIngredients[i], lucky: false);
		}

		// Pickaxes and bars (the mining chain: anvils, Molten Pickaxe...) are never randomized when protected
		private static bool KeepRecipe(int original, bool protectMining)
		{
			if (!ItemPool.IsRandomizable(original)) return true;
			if (!protectMining) return false;
			if (ItemPool.Info[original].Category == ItemCategory.OreBar) return true;
			return ContentSamples.ItemsByType.TryGetValue(original, out Item sample) && sample.pick > 0;
		}

		// Is this item one of the recipe's own ingredients through a group (any Wood -> N Boreal Wood)? Groups are never
		// randomized: the live recipe's are the originals
		public static bool InAcceptedGroup(int i, int item)
		{
			if (i < 0 || i >= Recipe.numRecipes) return false;
			foreach (int id in Main.recipe[i].acceptedGroups)
			{
				if (RecipeGroup.recipeGroups.TryGetValue(id, out RecipeGroup group) && group.ValidItems.Contains(item)) return true;
			}
			return false;
		}

		// The result of a randomized recipe. lucky: ignores the progression lock (what the recipe gives once unlocked)
		// Furniture, blocks and walls have their own surprise chance
		public static bool IsFurnitureLike(int item) => ItemPool.IsValid(item) && ItemPool.Info[item].Category is ItemCategory.Furniture or ItemCategory.Block;

		public static float SurpriseChanceOf(int original)
		{
			CraftingConfig cfg = CraftingConfig.Instance;
			return IsFurnitureLike(original) ? cfg.FurnitureSurpriseChance : cfg.RecipeSurpriseChance;
		}

		// limitValue: null = the LimitRecipeValue setting; surpriseChance: null = the setting of the original's category
		public static (int type, int stack) RollResult(int i, IList<int> ingredientTypes, int ingredientCount, bool lucky, long ingredientValue = -1, bool? limitValue = null, float? surpriseChance = null)
		{
			if (ingredientValue < 0) ingredientValue = ingredientValues[i];
			CraftingConfig cfg = CraftingConfig.Instance;
			int original = originalTypes[i];
			int originalStack = originalStacks[i];
			long maxValue = limitValue ?? cfg.LimitRecipeValue ? MaxResultValue(i, ingredientValue, cfg.RecipeValueGuard) : long.MaxValue;
			bool surprise = ItemPool.IsSurprise(original, surpriseChance ?? SurpriseChanceOf(original), DetRandom.Get(RollKey.Recipe, recipeKeys[i], RollKey.Surprise));
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
				if (ingredientTypes.Contains(replacement) || InAcceptedGroup(i, replacement)) continue;
				int stack = ResultStack(i, replacement, ingredientCount);
				if ((long)StackRules.ValueOf(replacement) * stack <= maxValue) return (replacement, stack);
			}
			// The guaranteed shuffle keeps its permutation
			if (GeneralConfig.Instance.RandomizerMode == RandomizerMode.Shuffle) return (original, originalStack);
			return ValueFallback(i, ingredientTypes, ingredientCount, maxValue, FallbackTier(i, lucky));
		}

		// Value guard: crafting must not turn cheap ingredients into a fortune (1 silver of tolerance)
		public static long MaxResultValue(int i, long ingredientValue, float guard)
		{
			long originalValue = (long)Math.Max(StackRules.ValueOf(originalTypes[i]), 0) * originalStacks[i];
			return (long)(Math.Max(ingredientValue, originalValue) * guard) + Item.silver;
		}

		public static int ResultStack(int i, int replacement, int ingredientCount)
		{
			int stack = StackRules.Scaled(originalTypes[i], originalStacks[i], replacement);
			if (StackRules.ValueOf(replacement) <= 0) stack = Math.Min(stack, Math.Max(1, ingredientCount));
			return stack;
		}

		// The tier of the fallback: the original's, or the highest one progression allows for a stand-in
		public static int FallbackTier(int i, bool lucky)
		{
			ProgressionConfig progression = ProgressionConfig.Instance;
			int tier = ItemPool.TierOf(originalTypes[i]);
			if (!lucky && progression.TierGating && GeneralConfig.Instance.RandomizerMode == RandomizerMode.Coherent)
			{
				tier = Math.Min(tier, Tiers.Clamp(ProgressionSystem.UnlockedTier + progression.TierAllowance));
			}
			return tier;
		}

		// Items of the original's category at a tier (or the closest tier below) that the recipe may give: not one of its
		// ingredients, not accepted by its groups, not the original
		public static List<(int type, int stack, long value)> FallbackCandidates(int i, IList<int> ingredientTypes, int ingredientCount, int tier)
		{
			int original = originalTypes[i];
			List<(int, int, long)> candidates = new();
			if (!ItemPool.IsValid(original)) return candidates;
			ItemCategory category = ItemPool.Info[original].Category;
			for (int t = Tiers.Clamp(tier); t >= 0 && candidates.Count == 0; t--)
			{
				foreach (int item in ItemPool.Bucket(category, t))
				{
					if (item == original || ingredientTypes.Contains(item) || InAcceptedGroup(i, item)) continue;
					int stack = ResultStack(i, item, ingredientCount);
					candidates.Add((item, stack, (long)StackRules.ValueOf(item) * stack));
				}
			}
			return candidates;
		}

		// When the 8 rolls failed (ingredients, or too valuable): a deterministic item of the original's category and tier
		// worth at most maxValue, else the cheapest of them; the original only when there is none
		public static (int type, int stack) ValueFallback(int i, IList<int> ingredientTypes, int ingredientCount, long maxValue, int tier)
		{
			List<(int type, int stack, long value)> candidates = FallbackCandidates(i, ingredientTypes, ingredientCount, tier);
			if (candidates.Count == 0) return (originalTypes[i], originalStacks[i]);
			List<(int type, int stack, long value)> affordable = candidates.Where(c => c.value <= maxValue).ToList();
			if (affordable.Count > 0)
			{
				(int type, int stack, _) = affordable[DetRandom.Get(RollKey.Recipe, recipeKeys[i], RollKey.Fallback).Next(affordable.Count)];
				return (type, stack);
			}
			(int cheapest, int cheapestStack, _) = candidates.OrderBy(c => c.value).ThenBy(c => c.type).First();
			return (cheapest, cheapestStack);
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

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
		private static int[][] originalGroups;
		private static int[] recipeKeys;
		private static long[] ingredientValues;
		private static bool[] modded;
		private static int[][] originalDecraft;
		private static bool[] originalDisabled;
		private static bool[] shell;
		private static bool applied;

		// Shell recipes: one per item no recipe made when this mod added its recipes (walls, blocks, furniture found in the
		// world..., and the life and mana upgrades), with no ingredient and no station, switched off as set up (so the item classification, the guaranteed
		// sources and the decraft sets never see them). RecipePlanner gives the eligible ones ingredients and a station in each
		// world; Apply switches on those it planned. Kept by reference until PostSetupRecipes maps them to their index
		private static readonly HashSet<Recipe> shellRecipes = new();

		// Recipe.Disabled has no public setter (DisableRecipe only works while recipes are set up, for good): twin copies are
		// switched off and back through it
		private static readonly MethodInfo SetDisabledMethod = typeof(Recipe).GetProperty("Disabled")?.GetSetMethod(true);

		// Disabled before this mod changed anything (the planner reads this, never the live flag: twins are switched off live)
		public static bool VanillaDisabled(int i) =>
			originalDisabled != null && i >= 0 && i < originalDisabled.Length ? originalDisabled[i] : i >= 0 && i < Recipe.numRecipes && Main.recipe[i].Disabled;

		public static int OriginalResult(int recipeIndex) => originalTypes != null && recipeIndex < originalTypes.Length ? originalTypes[recipeIndex] : 0;

		public static int OriginalStack(int recipeIndex) => originalStacks != null && recipeIndex < originalStacks.Length ? originalStacks[recipeIndex] : 0;

		public static (int type, int stack)[] OriginalIngredients(int recipeIndex) => originalIngredients[recipeIndex];

		public static int[] OriginalTiles(int recipeIndex) => originalTiles[recipeIndex];

		// Recipe groups as set up (the planner may replace a group slot or add a group: it never reads the live ones)
		public static int[] OriginalGroups(int recipeIndex) => originalGroups[recipeIndex];

		public static int RecipeKey(int recipeIndex) => recipeKeys[recipeIndex];

		public static long OriginalIngredientValue(int recipeIndex) => ingredientValues[recipeIndex];

		public static int RecipeCount => originalTypes?.Length ?? 0;

		public static bool Applied => applied;

		// A shell recipe of this mod (an item no other recipe makes), whether or not this world planned it
		public static bool IsShell(int i) => shell != null && i >= 0 && i < shell.Length && shell[i];

		// Items a shell may be registered for: not gear, ammo, potions, critters, bags or summons (ItemClassifier.Classify;
		// RecipePlanner.ShellEligible decides per world). Item.material is not set yet: a Misc item is registered only when
		// a recipe already uses it (what Recipe.UpdateWhichItemsAreMaterials will make a material), never otherwise (a Misc
		// shell is never planned)
		private static readonly ItemCategory[] ShellRegistered =
		{
			ItemCategory.Block, ItemCategory.Furniture, ItemCategory.Material, ItemCategory.Vanity, ItemCategory.Dye, ItemCategory.Food
		};

		// Items the enabled recipes registered so far use, directly or through one of their groups
		private static HashSet<int> UsedAsIngredient()
		{
			HashSet<int> used = new();
			HashSet<int> groups = new();
			for (int i = 0; i < Recipe.numRecipes; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (recipe == null || recipe.Disabled) continue;
				foreach (Item ingredient in recipe.requiredItem) used.Add(ingredient.type);
				groups.UnionWith(recipe.acceptedGroups);
			}
			foreach (int id in groups)
			{
				if (RecipeGroup.recipeGroups.TryGetValue(id, out RecipeGroup group)) used.UnionWith(group.ValidItems);
			}
			return used;
		}

		// Walls come by four, like the vanilla wall recipes; anything else by one
		private static int ShellStack(Item item) => item.createWall > WallID.None && item.maxStack >= 4 ? 4 : 1;

		// Runs on every machine at load, with the same mods: the shells have the same indices everywhere
		public override void AddRecipes()
		{
			shellRecipes.Clear();
			HashSet<int> made = new();
			for (int i = 0; i < Recipe.numRecipes; i++)
			{
				if (Main.recipe[i]?.createItem != null) made.Add(Main.recipe[i].createItem.type);
			}
			HashSet<int> materials = UsedAsIngredient();
			Dictionary<ItemCategory, int> counts = new();
			for (int type = 1; type < ItemLoader.ItemCount; type++)
			{
				if (made.Contains(type) || !ContentSamples.ItemsByType.TryGetValue(type, out Item item) || item.IsAir || item.type != type) continue;
				if (ItemID.Sets.Deprecated[type] || ItemID.Sets.ItemsThatShouldNotBeInInventory[type] || ItemID.Sets.IsAPickup[type] || ItemID.Sets.CommonCoin[type] || item.questItem) continue;
				if (ItemClassifier.IsBanned(item)) continue;
				ItemCategory category = ItemClassifier.Classify(type, item);
				if (category == ItemCategory.Misc && materials.Contains(type)) category = ItemCategory.Material;
				// The life and mana upgrades get theirs whatever their category: it is their recipe (RecipePlanner.SpecialRecipe)
				if (Array.IndexOf(ShellRegistered, category) < 0 && !Protection.IsUpgrade(type)) continue;
				Recipe recipe = Recipe.Create(type, ShellStack(item));
				// Never shimmered back into its (random) ingredients
				recipe.DisableDecraft();
				recipe.Register();
				recipe.DisableRecipe();
				shellRecipes.Add(recipe);
				counts[category] = counts.GetValueOrDefault(category) + 1;
			}
			Mod.Logger.Info($"{shellRecipes.Count} shell recipes registered for items no recipe makes ({string.Join(", ", counts.OrderBy(p => p.Key).Select(p => $"{p.Key} {p.Value}"))})");
		}


		public override void PostSetupRecipes()
		{
			if (CreateQuickLookups == null) Mod.Logger.Error("Recipe.CreateRequiredItemQuickLookups not found: randomized ingredients will not show correctly in the crafting menu.");
			int count = Recipe.numRecipes;
			originalTypes = new int[count];
			originalStacks = new int[count];
			originalIngredients = new (int, int)[count][];
			originalTiles = new int[count][];
			originalGroups = new int[count][];
			recipeKeys = new int[count];
			ingredientValues = new long[count];
			modded = new bool[count];
			originalDisabled = new bool[count];
			shell = new bool[count];
			if (SetDisabledMethod == null) Mod.Logger.Warn("Recipe.Disabled has no setter: identical recipe copies stay enabled.");
			for (int i = 0; i < count; i++)
			{
				Recipe recipe = Main.recipe[i];
				shell[i] = shellRecipes.Contains(recipe);
				originalDisabled[i] = recipe.Disabled;
				originalTypes[i] = recipe.createItem.type;
				originalStacks[i] = recipe.createItem.stack;
				originalIngredients[i] = recipe.requiredItem.Select(ingredient => (ingredient.type, ingredient.stack)).ToArray();
				originalTiles[i] = recipe.requiredTile.ToArray();
				originalGroups[i] = recipe.acceptedGroups.ToArray();
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
				// A shell has no ingredient: its ingredients are planned to be worth about what it makes
				if (shell[i]) value = (long)Math.Max(StackRules.ValueOf(recipe.createItem.type), 1) * recipe.createItem.stack;
				ingredientValues[i] = value;
				if (!shell[i]) Obtainability.RecipeResults.Add(recipe.createItem.type);
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
			originalGroups = null;
			recipeKeys = null;
			ingredientValues = null;
			modded = null;
			originalDecraft = null;
			originalDisabled = null;
			shell = null;
			shellRecipes.Clear();
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

		// Recipes whose result, ingredients and station may change: vanilla recipes of randomizable items, except the mining chain.
		// A house recipe (HouseRecipe) is touchable but keeps its result (RecipePlanner)
		public static bool Touchable(int i) => !modded[i] && !shell[i] && !KeepRecipe(originalTypes[i], ProgressionConfig.Instance.ProtectPickaxeProgression);

		// What a house needs besides the table and the chair (basic stations, guaranteed in the world): hint targets. Their
		// recipes keep their result but get random ingredients found at tier 0 and a basic station (RecipePlanner), so a
		// house can always be built at once
		public static readonly int[] HouseItems = { ItemID.WoodenDoor, ItemID.WoodPlatform, ItemID.Torch };

		public static bool HouseRecipe(int i) => originalTypes != null && i >= 0 && i < originalTypes.Length && !modded[i] && !shell[i]
			&& Array.IndexOf(HouseItems, originalTypes[i]) >= 0 && ItemPool.IsRandomizable(originalTypes[i]);

		// Recipes whose ingredients may change: protected results (boss summons, progression stations...) keep their result
		// and stations, but an ingredient that can't be found in time is replaced like in any recipe (protected ingredients
		// stay); modded recipes and excluded results stay as they are
		public static bool IngredientsTouchable(int i) => !modded[i] && !shell[i] && ItemPool.IsValid(originalTypes[i]) && !ItemPool.Info[originalTypes[i]].Excluded;

		public static bool IsModded(int i) => modded != null && i >= 0 && i < modded.Length && modded[i];

		// A recipe kept only because the mining chain is protected
		public static bool KeptForMining(int i) => !modded[i] && !shell[i] && ItemPool.IsRandomizable(originalTypes[i]) && KeepRecipe(originalTypes[i], ProgressionConfig.Instance.ProtectPickaxeProgression);

		// The live recipe differs from the vanilla one (result or ingredients): what it makes sells for its ingredients
		public static bool IsRandomized(int i)
		{
			if (!applied || originalTypes == null || i < 0 || i >= originalTypes.Length || i >= Recipe.numRecipes || (modded[i] && !shell[i])) return false;
			Recipe recipe = Main.recipe[i];
			// A shell switched on is a random recipe of its own
			if (shell[i]) return !recipe.Disabled;
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
				SetGroups(recipe, plan.Groups[i] ?? originalGroups[i]);
				SetTiles(recipe, plan.Tiles[i] ?? originalTiles[i]);
				// A copy of another recipe (same result, ingredients and stations) would show twice: switched off. A shell is on
				// only when this world planned it
				SetDisabled(recipe, shell[i] ? !RecipePlanner.ShellActive(plan, i) : originalDisabled[i] || plan.Twin[i]);
				// Decrafting gives the current ingredients back: not when they are worth clearly more than before (protected
				// results too, their ingredients may be replaced), nor when the result is had before the recipe's tier, found or made
				// by another recipe (it would shimmer into that tier's key early)
				if (plan.Ingredients[i] != null && plan.IngredientValues[i] > ingredientValues[i] * 5 / 4) changed[i] = true;
				int recipeTier = Math.Max(plan.IngredientTiers[i], plan.KeyIngredient[i] ? plan.ResultTiers[i] : 0);
				int made = plan.Results[i].type;
				int had = Math.Min(Findability.Tier(made), made > 0 && made < plan.Availability.Length ? plan.Availability[made] : Findability.Never);
				if (plan.Ingredients[i] != null && recipeTier > 0 && had < recipeTier) changed[i] = true;
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

		// Always the planned result, whatever the progression: its key ingredient (or stations) make the recipe wait for its
		// tier, there is no stand-in any more
		public static (int type, int stack) AppliedResult(RecipePlan plan, int i) => plan.Results[i];

		// Pickaxes and bars (the mining chain: anvils, Molten Pickaxe...) are never randomized when protected
		private static bool KeepRecipe(int original, bool protectMining)
		{
			if (!ItemPool.IsRandomizable(original)) return true;
			if (!protectMining) return false;
			if (ItemPool.Info[original].Category == ItemCategory.OreBar) return true;
			return ContentSamples.ItemsByType.TryGetValue(original, out Item sample) && sample.pick > 0;
		}

		// Is this item one of the live recipe's ingredients through a group (any Wood -> N Boreal Wood)?
		public static bool InAcceptedGroup(int i, int item)
		{
			if (i < 0 || i >= Recipe.numRecipes) return false;
			return InGroups(Main.recipe[i].acceptedGroups, item);
		}

		// The same with the recipe's groups as set up: what the results are rolled against (never the live, planned ones)
		public static bool InOriginalGroup(int i, int item) => originalGroups != null && i >= 0 && i < originalGroups.Length && InGroups(originalGroups[i], item);

		public static bool InGroups(IEnumerable<int> groups, int item)
		{
			foreach (int id in groups)
			{
				if (RecipeGroup.recipeGroups.TryGetValue(id, out RecipeGroup group) && group.ValidItems.Contains(item)) return true;
			}
			return false;
		}

		// Value guard: crafting must not turn cheap ingredients into a fortune (1 silver of tolerance)
		public static long MaxResultValue(int i, long ingredientValue, float guard)
		{
			long originalValue = (long)Math.Max(StackRules.ValueOf(originalTypes[i]), 0) * originalStacks[i];
			return (long)(Math.Max(ingredientValue, originalValue) * guard) + Item.silver;
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
			// The life and mana upgrades, made by a random recipe, never shimmer back into anything, whatever the setting
			foreach (int type in Protection.Upgrades)
			{
				if (ItemPool.IsSpecial(type) && type < decraft.Length) decraft[type] = Array.Empty<int>();
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
				SetGroups(recipe, originalGroups[i]);
				SetTiles(recipe, originalTiles[i]);
				SetDisabled(recipe, originalDisabled[i]);
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

		// Read live by the crafting checks (AcceptedByItemGroups) and the quick lookups RefreshCraftingMenu rebuilds
		private static void SetGroups(Recipe recipe, int[] groups)
		{
			if (recipe.acceptedGroups.SequenceEqual(groups)) return;
			recipe.acceptedGroups.Clear();
			recipe.acceptedGroups.AddRange(groups);
		}

		private static void SetDisabled(Recipe recipe, bool disabled)
		{
			if (recipe.Disabled != disabled) SetDisabledMethod?.Invoke(recipe, new object[] { disabled });
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

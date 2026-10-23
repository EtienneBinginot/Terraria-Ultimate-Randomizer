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
		// Another mod's recipe: its mod's name, and whether one of its conditions comes from a mod (ForeignCondition)
		private static string[] modNames;
		private static bool[] moddedCondition;
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

		// What one unit of an item is worth for the recipe plans (at least 1 copper): its value, or for a value-0 item what its
		// cheapest vanilla recipe costs (a Large Diamond is 15 Diamonds, a Luminite Brick Wall a quarter of a brick), else a
		// floor by rarity (a value-0 rare drop is no 1-copper filler). From the recipes as set up only: the same everywhere
		private static long[] effectiveValues;

		public static long EffectiveValue(int type)
		{
			long value = Effective(type, out _);
			return value == Cycle ? ValueFloor(type) : value;
		}

		private const long InProgress = -2;
		// An item whose value is being computed: a recipe needing it is left out (a Gray Brick made back from its walls is
		// not worth 4 walls valued at their floor, 4 copper)
		private const long Cycle = long.MaxValue;
		// The depth of each item being computed, and the current depth
		private static int[] progressDepth;
		private static int depth;

		// low: the smallest depth of the items in progress this value depended on (int.MaxValue: none). A value that depended
		// on an item further up the chain is not cached (it left out a recipe only that chain loops through)
		private static long Effective(int type, out int low)
		{
			low = int.MaxValue;
			if (type <= 0) return 1;
			if (effectiveValues == null || effectiveValues.Length < ItemLoader.ItemCount)
			{
				effectiveValues = new long[ItemLoader.ItemCount];
				Array.Fill(effectiveValues, -1);
				progressDepth = new int[ItemLoader.ItemCount];
				depth = 0;
				madeBy = null;
			}
			if (type >= effectiveValues.Length) return Math.Max(StackRules.ValueOf(type), 1);
			if (effectiveValues[type] >= 0) return Math.Max(effectiveValues[type], 1);
			if (effectiveValues[type] == InProgress)
			{
				low = progressDepth[type];
				return Cycle;
			}
			int value = StackRules.ValueOf(type);
			if (value > 0 || originalTypes == null) return effectiveValues[type] = Math.Max(value, 1);
			int own = depth++;
			progressDepth[type] = own;
			effectiveValues[type] = InProgress;
			long best = Cycle;
			try
			{
				foreach (int i in MadeBy(type))
				{
					long cost = 0;
					bool loops = false;
					foreach ((int ingredient, int stack) in originalIngredients[i])
					{
						long each = Effective(ingredient, out int l);
						low = Math.Min(low, l);
						if (each == Cycle)
						{
							loops = true;
							break;
						}
						cost += each * stack;
					}
					if (!loops) best = Math.Min(best, cost / Math.Max(1, originalStacks[i]));
				}
			}
			finally
			{
				depth--;
			}
			if (low < own)
			{
				// Depends on an item further up: computed again once that one is known
				effectiveValues[type] = -1;
				return best == Cycle ? Cycle : Math.Max(best, 1);
			}
			low = int.MaxValue;
			return effectiveValues[type] = best == Cycle ? ValueFloor(type) : Math.Max(best, 1);
		}

		// The enabled vanilla recipes (not shells, not other mods' ones of vanilla items: shortcuts) making each item; other
		// mods' recipes count for their own items (a storage upgrade is worth its bars)
		private static Dictionary<int, List<int>> madeBy;

		private static List<int> MadeBy(int type)
		{
			if (madeBy == null)
			{
				madeBy = new Dictionary<int, List<int>>();
				for (int i = 0; i < originalTypes.Length; i++)
				{
					if (shell[i] || modded[i] && originalTypes[i] < ItemID.Count || originalDisabled[i]) continue;
					if (!madeBy.TryGetValue(originalTypes[i], out List<int> list)) madeBy[originalTypes[i]] = list = new List<int>();
					list.Add(i);
				}
			}
			return madeBy.TryGetValue(type, out List<int> found) ? found : new List<int>();
		}

		// A value-0 item no recipe makes: 1 copper when common (blocks, walls), a silver times its rarity squared, a gold for
		// an Expert or Master drop
		private static long ValueFloor(int type)
		{
			if (!ContentSamples.ItemsByType.TryGetValue(type, out Item sample)) return 1;
			if (sample.rare is ItemRarityID.Expert or ItemRarityID.Master) return Item.gold;
			return sample.rare <= 0 ? 1 : (long)Item.silver * sample.rare * sample.rare;
		}

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
			// Another mod's internal items get no shell (ItemClassifier.IsInternal: what drops or a bag holds has a source; the shops
			// are only read after the recipes, so a shop-only item without research gets no shell either, which is harmless)
			HashSet<int> sourced = ItemPool.LootSources();
			sourced.UnionWith(made);
			Dictionary<ItemCategory, int> counts = new();
			for (int type = 1; type < ItemLoader.ItemCount; type++)
			{
				if (made.Contains(type) || !ContentSamples.ItemsByType.TryGetValue(type, out Item item) || item.IsAir || item.type != type) continue;
				if (ItemID.Sets.Deprecated[type] || ItemID.Sets.ItemsThatShouldNotBeInInventory[type] || ItemID.Sets.IsAPickup[type] || ItemID.Sets.CommonCoin[type] || item.questItem) continue;
				if (ItemClassifier.IsBanned(item) || ItemClassifier.IsInternal(item, sourced)) continue;
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
			modNames = new string[count];
			moddedCondition = new bool[count];
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
				modNames[i] = modded[i] ? recipe.Mod.Name : null;
				moddedCondition[i] = modded[i] && recipe.Conditions.Any(ForeignCondition);
				// Keyed on names, not on the recipe index, so the mapping survives mod updates
				int key = DetRandom.StableHash(DetRandom.ItemName(recipe.createItem.type));
				foreach (Item ingredient in recipe.requiredItem)
				{
					key = DetRandom.Hash(key, DetRandom.StableHash(DetRandom.ItemName(ingredient.type)), ingredient.stack);
				}
				recipeKeys[i] = key;
				if (!shell[i]) Obtainability.RecipeResults.Add(recipe.createItem.type);
			}
			// The values once every recipe is known: a value-0 ingredient is worth what its own recipe costs (EffectiveValue)
			effectiveValues = null;
			for (int i = 0; i < count; i++)
			{
				// A shell has no ingredient: its ingredients are planned to be worth about what it makes
				ingredientValues[i] = shell[i] ? EffectiveValue(originalTypes[i]) * originalStacks[i] : originalIngredients[i].Sum(p => EffectiveValue(p.type) * p.stack);
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
			modNames = null;
			moddedCondition = null;
			originalDecraft = null;
			originalDisabled = null;
			shell = null;
			effectiveValues = null;
			madeBy = null;
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

		// Recipes whose result, ingredients and station may change: vanilla recipes (and other mods' ones unless LeftAlone) of
		// randomizable items, except the mining chain. A house recipe (HouseRecipe) is touchable but keeps its result (RecipePlanner)
		public static bool Touchable(int i) => !LeftAlone(i) && !shell[i] && !KeepRecipe(originalTypes[i], ProgressionConfig.Instance.ProtectPickaxeProgression);

		// What a house needs besides the table and the chair (basic stations, guaranteed in the world): hint targets. Their
		// recipes keep their result but get random ingredients found at tier 0 and a basic station (RecipePlanner), so a
		// house can always be built at once
		public static readonly int[] HouseItems = { ItemID.WoodenDoor, ItemID.WoodPlatform, ItemID.Torch };

		public static bool HouseRecipe(int i) => originalTypes != null && i >= 0 && i < originalTypes.Length && !LeftAlone(i) && !shell[i]
			&& Array.IndexOf(HouseItems, originalTypes[i]) >= 0 && ItemPool.IsRandomizable(originalTypes[i]);

		// Recipes whose ingredients may change: protected results (boss summons, progression stations...) keep their result
		// and stations, but an ingredient that can't be found in time is replaced like in any recipe (protected ingredients
		// stay); modded recipes left alone (LeftAlone) and excluded results stay as they are
		public static bool IngredientsTouchable(int i) => !LeftAlone(i) && !shell[i] && ItemPool.IsValid(originalTypes[i]) && !ItemPool.Info[originalTypes[i]].Excluded;

		// A recipe of a mod (this one's shells too; tModLoader's own count as vanilla): the stats and logs tell them apart
		public static bool IsModded(int i) => modded != null && i >= 0 && i < modded.Length && modded[i];

		// Another mod's recipe that stays exactly as its mod made it (RandomizeModdedRecipes off; never a shell). On, other mods'
		// recipes are planned like the vanilla ones everywhere (results, swaps, ingredients, groups, stations, twins, house
		// rules): their items' tiers come from these recipes too (ItemClassifier.IsTierRecipe), so each keeps a key of its
		// result's tier. Accepted limitation: a mod's own recipe callbacks (OnCraft, consume hooks) still run for its recipe,
		// whatever it makes now. Always left alone: a recipe with a condition of its mod (Magic Storage's Cooked Marshmallow
		// needs a campfire nearby: the condition would be put on another result, or lost with its ingredients), and the
		// recipes of excluded mods (GeneralConfig.ExcludedMods)
		public static bool LeftAlone(int i) => IsModded(i) && !shell[i]
			&& (!CraftingConfig.Instance.RandomizeModdedRecipes || moddedCondition[i] || ExcludedMod(modNames[i]));

		private static bool ExcludedMod(string mod) => mod != null && GeneralConfig.Instance.ExcludedMods.Any(m => m != null && m.Trim() == mod);

		// A recipe condition defined by another mod: its type or its predicate's code lives outside tModLoader (the vanilla
		// ones, Condition.NearWater..., are tModLoader's)
		private static bool ForeignCondition(Condition condition)
		{
			Assembly own = typeof(Recipe).Assembly;
			return condition != null && (condition.GetType().Assembly != own || condition.Predicate?.Method.Module.Assembly is Assembly code && code != own);
		}

		// A recipe kept only because the mining chain is protected
		public static bool KeptForMining(int i) => !LeftAlone(i) && !shell[i] && ItemPool.IsRandomizable(originalTypes[i]) && KeepRecipe(originalTypes[i], ProgressionConfig.Instance.ProtectPickaxeProgression);

		// The live recipe differs from the vanilla one (result or ingredients): what it makes sells for its ingredients
		public static bool IsRandomized(int i)
		{
			if (!applied || originalTypes == null || i < 0 || i >= originalTypes.Length || i >= Recipe.numRecipes || LeftAlone(i)) return false;
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
				// Shimmer decrafting ignores Recipe.Disabled (it reads CraftingRecipeIndices): a recipe switched off leaves them
				if (plan.Twin[i]) changed[i] = true;
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

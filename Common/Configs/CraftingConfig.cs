using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace UltimateRandomizer.Common.Configs
{
	// "Ingredient" weights: relative chance of each category for a rebuilt recipe's ingredients (0 = never, unless the vanilla
	// recipe used that category: the vanilla shape adds its own weight)
	public class IngredientWeights : WeightTable
	{
		[Range(0, 100)] [DefaultValue(50)] public int Material = 50;
		[Range(0, 100)] [DefaultValue(25)] public int OreBar = 25;
		[Range(0, 100)] [DefaultValue(3)] public int Potion = 3;
		[Range(0, 100)] [DefaultValue(2)] public int Food = 2;
		[Range(0, 100)] [DefaultValue(1)] public int Ammo = 1;
		[Range(0, 100)] [DefaultValue(12)] public int Block = 12;
		[Range(0, 100)] [DefaultValue(3)] public int Critter = 3;
		[Range(0, 100)] [DefaultValue(2)] public int Misc = 2;
	}

	public enum IngredientMode
	{
		// Ingredients that can't be found at the recipe's tier are replaced by findable materials
		Randomized,
		// Vanilla ingredients, each one guaranteed somewhere in the world at the recipe's tier
		Vanilla
	}

	public class CraftingConfig : RandomizerConfig
	{
		public static CraftingConfig Instance => ModContent.GetInstance<CraftingConfig>();

		[Header("Recipes")]
		[DefaultValue(true)]
		public bool RandomizeRecipes;

		// Other mods' recipes are randomized like vanilla ones (results, ingredients, stations, swaps), each keeping a key
		// ingredient of its result's tier (RecipeSystem.LeftAlone). Off: they stay as their mod made them
		[DefaultValue(true)]
		public bool RandomizeModdedRecipes;

		// Weighs a randomized recipe's result against its new ingredients (RecipeValueGuard): ingredients raised when the result
		// is worth too much more
		[DefaultValue(false)]
		public bool LimitRecipeValue;

		// The result may be worth at most this many times the ingredients (when LimitRecipeValue is on)
		[Range(1f, 20f)]
		[Increment(0.5f)]
		[DefaultValue(3f)]
		public float RecipeValueGuard;

		// An item crafted through a randomized recipe sells for what its ingredients were worth (at most its own value)
		[DefaultValue(true)]
		public bool CraftedValueFromIngredients;

		[DefaultValue(true)]
		public bool DisableDecraftOfRandomizedResults;

		// Share of the randomized recipes that swap their result with another recipe of the same tier (by pairs: every item
		// stays craftable), their ingredients rebuilt for the item they now give. Each recipe otherwise keeps its own result
		[Range(0f, 100f)]
		[Increment(1f)]
		[DefaultValue(10f)]
		public float RecipeResultSwapChance;

		[Header("Ingredients")]
		[DefaultValue(IngredientMode.Randomized)]
		[DrawTicks]
		public IngredientMode IngredientMode;

		public IngredientWeights IngredientWeights = new();

		// Chance that an ingredient slot takes a category drawn with the loot weights instead of the ingredient weights
		[Range(0f, 100f)]
		[Increment(1f)]
		[DefaultValue(10f)]
		public float IngredientSurpriseChance;

		// A rebuilt recipe has as many ingredients as the vanilla one, give or take up to this many (0 and 1 more often than 2)
		[Range(0, 4)]
		[DefaultValue(2)]
		public int IngredientCountSpread;

		// Most ingredients a rebuilt recipe may have
		[Range(1, 8)]
		[DefaultValue(8)]
		public int IngredientMax;

		// Items no recipe makes (walls, blocks, furniture, vanity, materials...) get a random recipe, doable from their tier
		[DefaultValue(true)]
		public bool RecipesForUncraftable;

		// Largest amount of a replaced ingredient
		[Range(1, 999)]
		[DefaultValue(30)]
		public int IngredientStackCap;

		// Largest amount of a new ingredient only had through a recipe (each one is a craft: the costs compound)
		[Range(1, 30)]
		[DefaultValue(3)]
		public int CraftedIngredientStackCap;

		// Largest amount of a new ingredient had slowly: gems, Fallen Stars, fish, critters, shards, souls, uncommon drops
		[Range(1, 99)]
		[DefaultValue(10)]
		public int SlowIngredientStackCap;

		// A material already used by n recipes is picked with weight w / (1 + k * n): 0 = no penalty
		[Range(0f, 5f)]
		[Increment(0.1f)]
		[DefaultValue(0.5f)]
		public float IngredientReusePenalty;

		[Header("Stations")]
		[DefaultValue(true)]
		public bool RandomizeStations;

		// Chance that a recipe keeps its own station
		[Range(0f, 100f)]
		[Increment(5f)]
		[DefaultValue(30f)]
		public float StationKeepChance;

		[Header("Shimmer")]
		[DefaultValue(true)]
		public bool RandomizeShimmer;
	}
}

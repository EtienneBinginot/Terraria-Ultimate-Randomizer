using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace UltimateRandomizer.Common.Configs
{
	// "Ingredient" weights: relative chance of each category when a recipe ingredient is replaced (0 = never)
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

		// Caps the value of randomized results (RecipeValueGuard); off: any result of the original's category and tier
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

		// Chance that a recipe gives an item of any category (loot weights) instead of the original's category
		[Range(0f, 100f)]
		[Increment(1f)]
		[DefaultValue(15f)]
		public float RecipeSurpriseChance;

		// The same for recipes making furniture, blocks and walls (most vanilla recipes): otherwise furniture gives furniture
		[Range(0f, 100f)]
		[Increment(1f)]
		[DefaultValue(60f)]
		public float FurnitureSurpriseChance;

		[Header("Ingredients")]
		[DefaultValue(IngredientMode.Randomized)]
		[DrawTicks]
		public IngredientMode IngredientMode;

		public IngredientWeights IngredientWeights = new();

		// Chance that a specific ingredient (not gathered in the world) is replaced even when it can be found
		[Range(0f, 100f)]
		[Increment(5f)]
		[DefaultValue(25f)]
		public float IngredientVarietyChance;

		// Largest amount of a replaced ingredient
		[Range(1, 999)]
		[DefaultValue(30)]
		public int IngredientStackCap;

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

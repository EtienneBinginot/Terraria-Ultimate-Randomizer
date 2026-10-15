using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace UltimateRandomizer.Common.Configs
{
	public class CraftingConfig : RandomizerConfig
	{
		public static CraftingConfig Instance => ModContent.GetInstance<CraftingConfig>();

		[Header("Recipes")]
		[DefaultValue(true)]
		public bool RandomizeRecipes;

		// The result may be worth at most this many times the ingredients
		[Range(1f, 20f)]
		[Increment(0.5f)]
		[DefaultValue(3f)]
		public float RecipeValueGuard;

		[DefaultValue(true)]
		public bool DisableDecraftOfRandomizedResults;

		[Header("Shimmer")]
		[DefaultValue(true)]
		public bool RandomizeShimmer;
	}
}

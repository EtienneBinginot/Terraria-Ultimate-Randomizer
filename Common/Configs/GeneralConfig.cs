using System.Collections.Generic;
using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace UltimateRandomizer.Common.Configs
{
	public enum RandomizerMode
	{
		// Pure chaos: any item of any tier can come from anywhere
		Chaos,
		// Random but coherent: categories and tiers are respected, items unlock with progression
		Coherent,
		// Every original item is swapped with another of the same category and tier: everything stays obtainable
		Shuffle
	}

	public enum SeedSource
	{
		Random,
		FromWorldSeed,
		Fixed
	}

	public class GeneralConfig : RandomizerConfig
	{
		public static GeneralConfig Instance => ModContent.GetInstance<GeneralConfig>();

		[Header("Mode")]
		[DefaultValue(RandomizerMode.Coherent)]
		[DrawTicks]
		public RandomizerMode RandomizerMode;

		[Header("Seed")]
		[DefaultValue(SeedSource.Random)]
		[DrawTicks]
		public SeedSource SeedSource;

		[DefaultValue(0)]
		public int FixedSeed;

		[DefaultValue(true)]
		public bool AllowSeedChange;

		[Header("Exclusions")]
		[ReloadRequired]
		public List<ItemDefinition> ExcludedItems = new();

		[ReloadRequired]
		public List<string> ExcludedMods = new();
	}
}

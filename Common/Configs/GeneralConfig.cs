using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json;
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

		// A button (ResetAllElement), not a setting: never saved, so not part of the config signatures
		[JsonIgnore]
		[ShowDespiteJsonIgnore]
		[CustomModConfigItem(typeof(ResetAllElement))]
		[BackgroundColor(150, 60, 60)]
		[DefaultValue(false)]
		public bool ResetAll;

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

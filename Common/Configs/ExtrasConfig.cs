using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace UltimateRandomizer.Common.Configs
{
	public class ExtrasConfig : RandomizerConfig
	{
		public static ExtrasConfig Instance => ModContent.GetInstance<ExtrasConfig>();

		[Header("StartingKit")]
		[DefaultValue(true)]
		public bool StartingKit;

		[Range(0, 5)]
		[DefaultValue(1)]
		public int KitWeapons;

		[Range(0, 5)]
		[DefaultValue(2)]
		public int KitAccessories;

		[Range(0, 10)]
		[DefaultValue(3)]
		public int KitPotions;

		[Range(0, 5)]
		[DefaultValue(1)]
		public int KitTools;

		[Header("TownNpcs")]
		[DefaultValue(true)]
		public bool RandomizeTownNpcOrder;
	}
}

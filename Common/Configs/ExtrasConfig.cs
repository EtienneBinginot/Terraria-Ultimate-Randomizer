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

		// Gold coins for the first hint level of a tier 0 item (x 1 + 1.5 per tier, x 3 per level); 0: free
		[Header("Hints")]
		[Range(0, 100)]
		[DefaultValue(2)]
		public int HintBasePrice;
	}
}

using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace UltimateRandomizer.Common.Configs
{
	public class ClientConfig : ModConfig
	{
		public override ConfigScope Mode => ConfigScope.ClientSide;

		public static ClientConfig Instance => ModContent.GetInstance<ClientConfig>();

		// Testing tools (K panel, /rdebug, /rtest): off for players, they can cheat and show the whole randomization
		[Header("Debug")]
		[DefaultValue(false)]
		public bool DebugTools;

		// Chat messages when adaptive power makes enemies stronger or weaker
		[Header("Messages")]
		[DefaultValue(true)]
		public bool AdaptiveMessages;

		// Permutation style: the bestiary and Recipe Browser show this world's drops (WorldDropsReport) instead of vanilla's
		[Header("Display")]
		[DefaultValue(true)]
		public bool ShowWorldDrops;
	}
}

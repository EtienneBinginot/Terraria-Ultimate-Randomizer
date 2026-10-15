using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace UltimateRandomizer.Common.Configs
{
	public class ClientConfig : ModConfig
	{
		public override ConfigScope Mode => ConfigScope.ClientSide;

		public static ClientConfig Instance => ModContent.GetInstance<ClientConfig>();

		[Header("Debug")]
		[DefaultValue(true)]
		public bool DebugTools;
	}
}

using Terraria;
using Terraria.Localization;
using Terraria.ModLoader.Config;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Configs
{
	// Shared base of the server configs: only the host may change them, and changes apply right away in the world
	public abstract class RandomizerConfig : ModConfig
	{
		public override ConfigScope Mode => ConfigScope.ServerSide;

		public override bool AcceptClientChanges(ModConfig pendingConfig, int whoAmI, ref NetworkText message)
		{
			if (Main.countsAsHostForGameplay[whoAmI]) return true;
			message = NetworkText.FromKey("Mods.UltimateRandomizer.Chat.ConfigHostOnly");
			return false;
		}

		// A reset of every config refreshes the world once, at its end
		public override void OnChanged()
		{
			if (!ConfigReset.Batching) SeedSystem.NotifyConfigSaved();
		}
	}
}

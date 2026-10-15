using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Commands
{
	// /rseed            shows the world seed
	// /rseed set <n>    sets it (host only)
	// /rseed random     picks a new random one (host only)
	public class SeedCommand : ModCommand
	{
		public override CommandType Type => CommandType.World | CommandType.Console;

		public override string Command => "rseed";

		public override string Usage => "/rseed [set <number> | random]";

		public override string Description => Language.GetTextValue("Mods.UltimateRandomizer.Commands.Seed.Description");

		public override void Action(CommandCaller caller, string input, string[] args)
		{
			if (args.Length == 0 || args[0].Equals("show", StringComparison.OrdinalIgnoreCase))
			{
				caller.Reply(Language.GetTextValue("Mods.UltimateRandomizer.Commands.Seed.Show", SeedSystem.WorldSeed), Color.LightGreen);
				return;
			}
			if (!CanChangeSeed(caller))
			{
				caller.Reply(Language.GetTextValue("Mods.UltimateRandomizer.Commands.Seed.NotAllowed"), Color.OrangeRed);
				return;
			}
			int seed;
			if (args[0].Equals("random", StringComparison.OrdinalIgnoreCase))
			{
				seed = Random.Shared.Next();
			}
			else if (args[0].Equals("set", StringComparison.OrdinalIgnoreCase) && args.Length > 1 && int.TryParse(args[1], out int parsed))
			{
				seed = parsed;
			}
			else
			{
				caller.Reply(Usage, Color.OrangeRed);
				return;
			}
			SeedSystem.ChangeSeed(seed);
			caller.Reply(Language.GetTextValue("Mods.UltimateRandomizer.Commands.Seed.Changed", seed), Color.LightGreen);
		}

		private static bool CanChangeSeed(CommandCaller caller)
		{
			if (!GeneralConfig.Instance.AllowSeedChange) return false;
			if (Main.netMode == NetmodeID.SinglePlayer || caller.CommandType == CommandType.Console) return true;
			return caller.Player != null && Main.countsAsHostForGameplay[caller.Player.whoAmI];
		}
	}
}

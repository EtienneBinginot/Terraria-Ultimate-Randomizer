using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Commands
{
	// /rchests: randomizes the natural chests of a world created without the mod (once, host only)
	public class ChestCommand : ModCommand
	{
		public override CommandType Type => CommandType.World;

		public override string Command => "rchests";

		public override string Usage => "/rchests [confirm]";

		public override string Description => Language.GetTextValue("Mods.UltimateRandomizer.Commands.Chests.Description");

		public override void Action(CommandCaller caller, string input, string[] args)
		{
			bool isHost = Main.netMode == NetmodeID.SinglePlayer || caller.CommandType == CommandType.Console
				|| (caller.Player != null && Main.countsAsHostForGameplay[caller.Player.whoAmI]);
			if (!isHost)
			{
				caller.Reply(Language.GetTextValue("Mods.UltimateRandomizer.Commands.Seed.NotAllowed"), Color.OrangeRed);
				return;
			}
			if (ChestSystem.ChestsRandomized)
			{
				caller.Reply(Language.GetTextValue("Mods.UltimateRandomizer.Commands.Chests.AlreadyDone"), Color.Yellow);
				return;
			}
			if (args.Length == 0 || args[0] != "confirm")
			{
				caller.Reply(Language.GetTextValue("Mods.UltimateRandomizer.Commands.Chests.Warning"), Color.Orange);
				return;
			}
			int count = ChestSystem.RandomizeAllChests();
			caller.Reply(Language.GetTextValue("Mods.UltimateRandomizer.Commands.Chests.Done", count), Color.LightGreen);
		}
	}
}

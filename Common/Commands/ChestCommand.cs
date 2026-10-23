using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Commands
{
	// /rchests: randomizes the natural chests of a world created without the mod (once, host only).
	// /rchests tame: re-rolls again the items above their chest's cap in the chests nobody opened (done once on load)
	public class ChestCommand : ModCommand
	{
		public override CommandType Type => CommandType.World | CommandType.Console;

		public override string Command => "rchests";

		public override string Usage => "/rchests [confirm | tame]";

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
			if (args.Length > 0 && args[0] == "tame")
			{
				if (!ChestSystem.ChestsRandomized)
				{
					caller.Reply(Language.GetTextValue("Mods.UltimateRandomizer.Commands.Chests.NotRandomized"), Color.Yellow);
					return;
				}
				(int chests, int items) = ChestSystem.TameChests();
				ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"/rchests tame: {items} items re-rolled in {chests} chests.");
				caller.Reply(Language.GetTextValue("Mods.UltimateRandomizer.Commands.Chests.Tamed", items, chests), Color.LightGreen);
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
			int count = ChestSystem.RandomizeAllChests(existingWorld: true);
			caller.Reply(Language.GetTextValue("Mods.UltimateRandomizer.Commands.Chests.Done", count), Color.LightGreen);
		}
	}
}

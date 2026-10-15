using Terraria.Localization;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Journal;

namespace UltimateRandomizer.Common.Commands
{
	// /rjournal: opens or closes the discovery journal (also bound to J by default)
	public class JournalCommand : ModCommand
	{
		public override CommandType Type => CommandType.Chat;

		public override string Command => "rjournal";

		public override string Description => Language.GetTextValue("Mods.UltimateRandomizer.Journal.Description");

		public override void Action(CommandCaller caller, string input, string[] args) => JournalSystem.Toggle();
	}
}

using Microsoft.Xna.Framework;
using Terraria.Localization;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Tests;

namespace UltimateRandomizer.Common.Commands
{
	// /rtest: runs the automated tests (results in chat, details in the debug panel and client.log)
	public class TestCommand : ModCommand
	{
		public override CommandType Type => CommandType.Chat;

		public override string Command => "rtest";

		public override string Description => Language.GetTextValue("Mods.UltimateRandomizer.Debug.RunTests");

		public override void Action(CommandCaller caller, string input, string[] args)
		{
			if (!Debug.DebugTools.Allowed)
			{
				caller.Reply(Language.GetTextValue("Mods.UltimateRandomizer.Commands.DebugOff"), Color.Yellow);
				return;
			}
			// /rtest Id1 Id2: only these tests
			var results = TestRunner.RunAll(args);
			int passed = results.FindAll(r => r.Outcome == TestOutcome.Passed).Count;
			int failed = results.FindAll(r => r.Outcome == TestOutcome.Failed).Count;
			caller.Reply(Language.GetTextValue("Mods.UltimateRandomizer.Debug.TestSummary", passed, failed, results.Count - passed - failed), failed > 0 ? Color.OrangeRed : Color.LightGreen);
			foreach (TestResult result in results.FindAll(r => r.Outcome == TestOutcome.Failed))
			{
				caller.Reply($"KO {result.Name}: {result.Detail}", Color.OrangeRed);
			}
		}
	}
}

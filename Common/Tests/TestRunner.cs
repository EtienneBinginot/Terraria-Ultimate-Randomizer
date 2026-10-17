using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Terraria.Localization;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Tests
{
	public enum TestOutcome { Passed, Failed, Skipped }

	public class TestResult
	{
		public string Id;
		public TestOutcome Outcome;
		public string Detail;
		public long Milliseconds;

		public string Name => Language.Exists("Mods.UltimateRandomizer.Tests." + Id) ? Language.GetTextValue("Mods.UltimateRandomizer.Tests." + Id) : Id;
	}

	public class TestFailure : Exception
	{
		public TestFailure(string message) : base(message) { }
	}

	public class TestSkipped : Exception
	{
		public TestSkipped(string reason) : base(reason) { }
	}

	public static class Check
	{
		public static void That(bool condition, string message)
		{
			if (!condition) throw new TestFailure(message);
		}

		public static void Equal<T>(T expected, T actual, string what)
		{
			if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new TestFailure($"{what}: expected {expected}, got {actual}");
		}

		public static void Skip(string reason) => throw new TestSkipped(reason);

		// Collects several failures and reports them together (at most a few lines)
		public static void All(List<string> failures, string summary)
		{
			if (failures.Count == 0) return;
			StringBuilder sb = new($"{summary}: {failures.Count} problem(s). ");
			for (int i = 0; i < Math.Min(5, failures.Count); i++) sb.Append(failures[i]).Append("; ");
			throw new TestFailure(sb.ToString());
		}
	}

	// In-game automated tests: run from the debug panel (Tests tab) or with /rtest, in a loaded world
	public static class TestRunner
	{
		private static readonly List<(string id, Action run)> tests = new();

		public static List<TestResult> LastResults { get; private set; } = new();

		public static void Register(string id, Action run) => tests.Add((id, run));

		public static IEnumerable<string> TestIds => tests.ConvertAll(t => t.id);

		public static void EnsureRegistered()
		{
			if (tests.Count > 0) return;
			CoreTests.Register();
			LootTests.Register();
			WorldTests.Register();
			ConfigTests.Register();
			EnemyTests.Register();
			BiomeTests.Register();
			VarietyTests.Register();
			SourceTests.Register();
			PacingTests.Register();
			GuaranteeTests.Register();
			RecipeTests.Register();
			StatTests.Register();
			AdaptivePowerTests.Register();
			CodexTests.Register();
			CodexUiTests.Register();
			HintTests.Register();
		}

		public static List<TestResult> RunAll()
		{
			EnsureRegistered();
			List<TestResult> results = new();
			if (!SeedSystem.WorldReady || !ItemPool.Ready)
			{
				results.Add(new TestResult { Id = "NoWorld", Outcome = TestOutcome.Skipped, Detail = Language.GetTextValue("Mods.UltimateRandomizer.Debug.NeedWorld") });
				LastResults = results;
				return results;
			}
			foreach ((string id, Action run) in tests)
			{
				Stopwatch watch = Stopwatch.StartNew();
				TestResult result = new() { Id = id, Outcome = TestOutcome.Passed };
				// Rolls must not depend on the tester's equipment: no adaptive power unless a test forces one
				AdaptivePower.PowerOverride = 0;
				try
				{
					run();
				}
				catch (TestSkipped skip)
				{
					result.Outcome = TestOutcome.Skipped;
					result.Detail = skip.Message;
				}
				catch (TestFailure failure)
				{
					result.Outcome = TestOutcome.Failed;
					result.Detail = failure.Message;
				}
				catch (Exception e)
				{
					result.Outcome = TestOutcome.Failed;
					result.Detail = e.GetType().Name + ": " + e.Message;
					ModContent.GetInstance<UltimateRandomizer>().Logger.Error($"Test {id} crashed", e);
				}
				finally
				{
					AdaptivePower.PowerOverride = null;
				}
				result.Milliseconds = watch.ElapsedMilliseconds;
				results.Add(result);
			}
			LastResults = results;
			Log(results);
			return results;
		}

		private static void Log(List<TestResult> results)
		{
			int passed = results.FindAll(r => r.Outcome == TestOutcome.Passed).Count;
			int failed = results.FindAll(r => r.Outcome == TestOutcome.Failed).Count;
			StringBuilder sb = new($"Test results: {passed} passed, {failed} failed, {results.Count - passed - failed} skipped (seed {SeedSystem.WorldSeed}, tier {ProgressionSystem.UnlockedTier})\n");
			foreach (TestResult r in results)
			{
				sb.Append($"  [{r.Outcome}] {r.Id} ({r.Milliseconds} ms){(string.IsNullOrEmpty(r.Detail) ? "" : " - " + r.Detail)}\n");
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info(sb.ToString());
		}

		public static void Unload()
		{
			tests.Clear();
			LastResults = new List<TestResult>();
		}
	}

	public class TestSystem : ModSystem
	{
		public override void Unload() => TestRunner.Unload();
	}
}

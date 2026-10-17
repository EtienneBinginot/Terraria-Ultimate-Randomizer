using System;
using System.Linq;
using Terraria.ID;
using System.ComponentModel;
using System.Reflection;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Tests
{
	// How often random equipment is found in an early hour of play, with the default settings (LootAbundance 3).
	// Measured with 1 to 4 slots (v0.9.3): 29.1 pieces per hour, 2.90 items per kill; about 20 of those pieces and 0.3
	// items per kill come from the extra slots, the rest from chests, surprises, original and small frequent drops.
	// With 2 to 7 slots the average slot count goes from 2.00 to 3.70 per type (x1.85, MobLootFeature.ExpectedSlots):
	// about 46 pieces per hour (target 34 to 62) and 3.2 items per kill
	public static class PacingTests
	{
		public const double MinGearPerHour = 34;
		public const double MaxGearPerHour = 62;
		// Half the expected 3.2 items per kill
		public const double MinItemsPerKill = 1.6;

		public static void Register()
		{
			TestRunner.Register("PacingExpected", PacingExpected);
			TestRunner.Register("PacingSimulation", PacingSimulation);
			TestRunner.Register("PacingVariety", PacingVariety);
			TestRunner.Register("DropFrequency", DropFrequency);
			TestRunner.Register("WorkBenchPacing", WorkBenchPacing);
		}

		private static void Log(string text) => ModContent.GetInstance<UltimateRandomizer>().Logger.Info(text);

		// The target only holds for the default settings: compares the fields that change the pace
		private static bool IsDefault(object config, string field)
		{
			FieldInfo info = config.GetType().GetField(field);
			DefaultValueAttribute attribute = info.GetCustomAttribute<DefaultValueAttribute>();
			if (attribute == null) return false;
			object expected = attribute.Value != null && attribute.Value.GetType() != info.FieldType && !info.FieldType.IsEnum
				? Convert.ChangeType(attribute.Value, info.FieldType)
				: attribute.Value;
			return Equals(expected, info.GetValue(config));
		}

		private static void RequireDefaults()
		{
			LootConfig loot = LootConfig.Instance;
			EnemiesConfig enemies = EnemiesConfig.Instance;
			string[] lootFields = { "SurpriseChance", "DropRateMultiplier", "LootAbundance", "DropSlotsMin", "DropSlotsMax", "DropTableStyle", "RandomizeMobDrops", "RandomizePotDrops", "RandomizeChests", "TierWindow" };
			foreach (string field in lootFields)
			{
				if (!IsDefault(loot, field)) Check.Skip($"{field} is not at its default value");
			}
			if (!loot.DropCategoryWeights.Equals(new CategoryWeights())) Check.Skip("the loot weights are not the default ones");
			if (!IsDefault(enemies, "EnemySwapChance") || !IsDefault(enemies, "EnemySwapMode") || !IsDefault(enemies, "BiomeVanillaChance") || !IsDefault(enemies, "BiomeExtraEnemies")
				|| !IsDefault(GeneralConfig.Instance, "RandomizerMode"))
			{
				Check.Skip("settings are not the default ones");
			}
		}

		private static void PacingExpected()
		{
			PacingResult result = PacingModel.Simulate(10, 42);
			Log($"Pacing: {result.GearPerHour:0.0} pieces of equipment per hour (deviation {result.StandardDeviation:0.0}), {result.DistinctPerHour:0.0} different");
			RequireDefaults();
			Check.That(result.GearPerHour >= MinGearPerHour && result.GearPerHour <= MaxGearPerHour,
				$"{result.GearPerHour:0.0} pieces of equipment per hour, expected {MinGearPerHour} to {MaxGearPerHour} (about 46 with LootAbundance 3)");
		}

		private static void PacingSimulation()
		{
			PacingResult a = PacingModel.Simulate(5, 7);
			PacingResult b = PacingModel.Simulate(5, 7);
			Check.That(Math.Abs(a.GearPerHour - b.GearPerHour) < 0.001, "the simulation is not deterministic");
			Check.That(a.GearPerHour > 0, "no equipment at all");
			Check.That(a.StandardDeviation <= a.GearPerHour, $"too irregular: {a.GearPerHour:0.0} ± {a.StandardDeviation:0.0} per hour");
		}

		// Most kills give something (with the default settings)
		private static void DropFrequency()
		{
			PacingResult result = PacingModel.Simulate(5, 44);
			Log($"Drops: {result.ItemsPerKill:0.00} items per kill");
			RequireDefaults();
			if (!IsDefault(LootConfig.Instance, "CommonDropChance")) Check.Skip("CommonDropChance is not at its default value");
			Check.That(result.ItemsPerKill >= MinItemsPerKill, $"only {result.ItemsPerKill:0.00} items per kill, expected at least {MinItemsPerKill} (about 3.2 with LootAbundance 3)");
		}

		// The first work bench is a small quest: 15 to 30 minutes on average (guaranteed drops: LootAbundance does not change it)
		private static void WorkBenchPacing()
		{
			if (!GuaranteedSources.StationRequirements().Any(r => r.Item == ItemID.WorkBench)) Check.Skip("the work bench is protected");
			double minutes = PacingModel.MinutesToFirstWorkBench(400, 45);
			Log($"First work bench: {minutes:0} minutes on average");
			ProgressionConfig progression = ProgressionConfig.Instance;
			if (!IsDefault(progression, "GuaranteedDropMultiplier") || !IsDefault(progression, "PotGuaranteeChance")) Check.Skip("guarantee settings are not the default ones");
			RequireDefaults();
			Check.That(minutes >= 15 && minutes <= 30, $"first work bench after {minutes:0} minutes on average, expected 15 to 30");
		}

		private static void PacingVariety()
		{
			RequireDefaults();
			PacingResult result = PacingModel.Simulate(10, 43);
			Check.That(result.DistinctPerHour >= 10, $"only {result.DistinctPerHour:0.0} different pieces of equipment per hour");
		}
	}
}

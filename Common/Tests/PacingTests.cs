using System;
using System.ComponentModel;
using System.Reflection;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Tests
{
	// How often random equipment is found in an early hour of play (target with the default settings: 15 to 25)
	public static class PacingTests
	{
		public const double MinGearPerHour = 15;
		public const double MaxGearPerHour = 25;

		public static void Register()
		{
			TestRunner.Register("PacingExpected", PacingExpected);
			TestRunner.Register("PacingSimulation", PacingSimulation);
			TestRunner.Register("PacingVariety", PacingVariety);
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
			string[] lootFields = { "SurpriseChance", "DropRateMultiplier", "DropSlotsMin", "DropSlotsMax", "DropTableStyle", "RandomizeMobDrops", "RandomizePotDrops", "RandomizeChests", "TierWindow" };
			foreach (string field in lootFields)
			{
				if (!IsDefault(loot, field)) Check.Skip($"{field} is not at its default value");
			}
			if (!loot.DropCategoryWeights.Equals(new CategoryWeights())) Check.Skip("the loot weights are not the default ones");
			if (!IsDefault(enemies, "EnemySwapChance") || !IsDefault(GeneralConfig.Instance, "RandomizerMode")) Check.Skip("settings are not the default ones");
		}

		private static void PacingExpected()
		{
			PacingResult result = PacingModel.Simulate(10, 42);
			Log($"Pacing: {result.GearPerHour:0.0} pieces of equipment per hour (deviation {result.StandardDeviation:0.0}), {result.DistinctPerHour:0.0} different");
			RequireDefaults();
			Check.That(result.GearPerHour >= MinGearPerHour && result.GearPerHour <= MaxGearPerHour,
				$"{result.GearPerHour:0.0} pieces of equipment per hour, expected {MinGearPerHour} to {MaxGearPerHour}");
		}

		private static void PacingSimulation()
		{
			PacingResult a = PacingModel.Simulate(5, 7);
			PacingResult b = PacingModel.Simulate(5, 7);
			Check.That(Math.Abs(a.GearPerHour - b.GearPerHour) < 0.001, "the simulation is not deterministic");
			Check.That(a.GearPerHour > 0, "no equipment at all");
			Check.That(a.StandardDeviation <= a.GearPerHour, $"too irregular: {a.GearPerHour:0.0} ± {a.StandardDeviation:0.0} per hour");
		}

		private static void PacingVariety()
		{
			RequireDefaults();
			PacingResult result = PacingModel.Simulate(10, 43);
			Check.That(result.DistinctPerHour >= 10, $"only {result.DistinctPerHour:0.0} different pieces of equipment per hour");
		}
	}
}

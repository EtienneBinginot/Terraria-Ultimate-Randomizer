using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using System.ComponentModel;
using System.Reflection;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Tests
{
	// How often random equipment is found in an early hour of play, with the default settings (LootAbundance 3).
	// Measured with 1 to 4 slots (v0.9.3): 29.1 pieces per hour, 2.90 items per kill; about 20 of those pieces and 0.3
	// items per kill come from the extra slots, the rest from chests, surprises, original and small frequent drops.
	// With 2 to 7 slots the average slot count goes from 2.00 to 3.70 per type (x1.85, MobLootFeature.ExpectedSlots):
	// about 46 pieces per hour (target 34 to 62) and 3.2 items per kill.
	// The spawn map of the world decides which enemies the early kills meet, and how many slots they drop: with the same
	// settings, 1.3.2 measured 47.1 (seed 524614759) and 63.8 (seed 705676219, which gave 64.8 in 1.3.1 before the jackpots
	// went back to 2 % / 5 % and the tiers from recipes): a spread of the seeds, not a regression. The bound allows it; the
	// log gives the sources of each hour to tell a real change apart
	public static class PacingTests
	{
		public const double MinGearPerHour = 34;
		public const double MaxGearPerHour = 68;
		// Half the expected 3.2 items per kill
		public const double MinItemsPerKill = 1.6;

		public static void Register()
		{
			TestRunner.Register("PacingExpected", PacingExpected);
			TestRunner.Register("PacingSimulation", PacingSimulation);
			TestRunner.Register("PacingVariety", PacingVariety);
			TestRunner.Register("DropFrequency", DropFrequency);
			TestRunner.Register("WorkBenchPacing", WorkBenchPacing);
			TestRunner.Register("JackpotPacing", JackpotPacing);
		}

		// Jackpots of a simulated early hour before the first boss, at most this many with the default settings (a sanity
		// bound, not a target: luck and madness of the kills, giants left out)
		public const double MaxJackpotsPerHour = 6;

		private static void Log(string text) => ModContent.GetInstance<UltimateRandomizer>().Logger.Info(text);

		// The target only holds for the default settings: compares the fields that change the pace
		public static bool IsDefault(object config, string field)
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
			Log($"Pacing: {result.GearPerHour:0.0} pieces of equipment per hour (deviation {result.StandardDeviation:0.0}), {result.DistinctPerHour:0.0} different; "
				+ $"from replaced drops {result.FromOriginal:0.0}, extra drops {result.FromExtra:0.0}, guaranteed {result.FromGuaranteed:0.0}, surprises {result.FromSurprise:0.0}, "
				+ $"chests {result.FromChests:0.0}; by tier {string.Join(" / ", result.ByTier.Select(t => t.ToString("0.0")))}");
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
			List<int> hosts = GuaranteedSources.HostsOf(ItemID.WorkBench).ToList();
			float planned = hosts.Count > 0 ? GuaranteedSources.For(hosts[0]).Where(d => d.Item == ItemID.WorkBench).Sum(d => d.Chance) : 0;
			// The same hosts with fixed chances, to see how the time follows the chance (the planned one comes from the model)
			string candidates = string.Join(", ", new[] { 20, 30, 40, 50, 60 }.Select(d => $"1/{d}: {PacingModel.MinutesToFirstWorkBench(400, 45, 1f / d):0}"));
			int biome = GuaranteedSources.BiomeOf(ItemID.WorkBench);
			Log($"First work bench: {minutes:0} minutes on average (chance {planned:P1} on {string.Join(", ", hosts.Select(Lang.GetNPCNameValue))}"
				+ $"{(biome >= 0 ? " in " + BiomeSlots.BiomeName(biome) : "")}, "
				+ $"{PacingModel.EarlyKillShare(hosts.Contains):P1} of the early kills; fixed chances {candidates})");
			ProgressionConfig progression = ProgressionConfig.Instance;
			if (!IsDefault(progression, "GuaranteedDropMultiplier") || !IsDefault(progression, "PotGuaranteeChance")) Check.Skip("guarantee settings are not the default ones");
			RequireDefaults();
			Check.That(minutes >= 15 && minutes <= 30, $"first work bench after {minutes:0} minutes on average, expected 15 to 30");
		}

		// Jackpots per early hour (luck and madness of the kills, with their share of the progression), logged; bounded before
		// the first boss with the default settings
		private static void JackpotPacing()
		{
			int tier = ProgressionSystem.UnlockedTier;
			double per100 = PacingModel.JackpotsPer100Kills(3000, 46);
			double perHour = per100 * PacingModel.KillsPerHour / 100;
			Log($"Jackpots: {perHour:0.00}/h at tier {tier} ({per100:0.00} per 100 kills, {PacingModel.KillsPerHour} kills per hour)");
			RequireDefaults();
			ProgressionConfig progression = ProgressionConfig.Instance;
			foreach (string field in new[] { "RuntimeJackpotChance", "WildDropChance", "WildTierDecay", "JackpotStartShare", "MaxJackpotTiers", "TierAllowance", "TierGating" })
			{
				if (!IsDefault(progression, field)) Check.Skip($"{field} is not at its default value");
			}
			if (tier != 0) Check.Skip($"the world is at tier {tier}, the bound is for the start");
			Check.That(perHour <= MaxJackpotsPerHour, $"{perHour:0.00} jackpots per early hour, at most {MaxJackpotsPerHour}");
		}

		private static void PacingVariety()
		{
			RequireDefaults();
			PacingResult result = PacingModel.Simulate(10, 43);
			Check.That(result.DistinctPerHour >= 10, $"only {result.DistinctPerHour:0.0} different pieces of equipment per hour");
		}
	}
}

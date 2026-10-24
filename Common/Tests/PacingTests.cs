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
	// log gives the sources of each hour to tell a real change apart.
	// 1.5.0: the model no longer counts the entries an early hour can't drop (hardmode, another difficulty: PacingModel's
	// condition filter), which lowers the vanilla-like hour too. These bounds predate it: re-read the "Pacing:" and "Drops:"
	// values of the next in-game run and re-derive them from the log, don't move them blindly
	public static class PacingTests
	{
		public const double MinGearPerHour = 34;
		public const double MaxGearPerHour = 68;
		// Half the expected 3.2 items per kill
		public const double MinItemsPerKill = 1.6;
		public const double MinDistinctPerHour = 10;

		// Permutation style (1.5.0 defaults: 2 extra slots at vanilla-like rates without materials, 10 % surprises), estimated
		// from the model's parts before the first in-game run: about 11 pieces per hour from chests and the surprises of pots,
		// trees, herbs and chests, 6 to 8 from the extra slots (150 kills x 2 slots x about 2 % of equipment), 1 to 2 from the
		// moved original drops and their surprises, about 1 guaranteed: about 20 pieces per hour (the vanilla-like style, about
		// 46, has 3 to 7 extra slots scaled by abundance). Items per kill: about 0.4 from the moved original drops (2 entries
		// per early kill) and 0.2 from the extra slots, about 0.6. The bounds are wide until the logged values are known
		public const double MinPermutationGearPerHour = 12;
		public const double MaxPermutationGearPerHour = 40;
		public const double MinPermutationItemsPerKill = 0.3;
		public const double MinPermutationDistinctPerHour = 6;

		public static void Register()
		{
			TestRunner.Register("PacingExpected", PacingExpected);
			TestRunner.Register("PacingSimulation", PacingSimulation);
			TestRunner.Register("PacingVariety", PacingVariety);
			TestRunner.Register("DropFrequency", DropFrequency);
			TestRunner.Register("WorkBenchPacing", WorkBenchPacing);
			TestRunner.Register("JackpotPacing", JackpotPacing);
		}

		// Jackpots of a simulated early hour before the first boss with the default settings: luck and madness of the kills,
		// the per-kill jackpot, giants and minis. 1.3.4 had 6 with about 0.9 % of jackpot chances early; the permutation style
		// fell to about 0.4 (0.75 with giants and minis); 0.20.0 aims at about 2 with the per-kill jackpot (KillJackpotChance)
		public const double MinJackpotsPerHour = 1.5;
		public const double MaxJackpotsPerHour = 3;

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

		// anyStyle: the test simulates both drop styles itself (PacingModel.Simulate with an explicit style)
		internal static void RequireDefaults(bool anyStyle = false)
		{
			LootConfig loot = LootConfig.Instance;
			EnemiesConfig enemies = EnemiesConfig.Instance;
			string[] lootFields = { "SurpriseChance", "DropRateMultiplier", "LootAbundance", "DropSlotsMin", "DropSlotsMax", "RandomizeMobDrops", "RandomizePotDrops", "RandomizeChests", "TierWindow",
				"PermutationExtraSlots", "PermutationSurpriseChance", "PermutationDuplicateCopies", "PermutationMaterialCopies" };
			foreach (string field in lootFields)
			{
				if (!IsDefault(loot, field)) Check.Skip($"{field} is not at its default value");
			}
			if (!anyStyle && !IsDefault(loot, "DropTableStyle")) Check.Skip("DropTableStyle is not at its default value");
			if (!loot.DropCategoryWeights.Equals(new CategoryWeights())) Check.Skip("the loot weights are not the default ones");
			if (!loot.PermutationExtraWeights.Equals(new PermutationExtraWeights())) Check.Skip("the permutation extra drop weights are not the default ones");
			if (!IsDefault(enemies, "EnemySwapChance") || !IsDefault(enemies, "EnemySwapMode") || !IsDefault(enemies, "BiomeVanillaChance") || !IsDefault(enemies, "BiomeExtraEnemies")
				|| !IsDefault(GeneralConfig.Instance, "RandomizerMode"))
			{
				Check.Skip("settings are not the default ones");
			}
		}

		// Both drop styles, whatever the configured one: each has its own targets. Only the drop plans follow the style: the
		// guaranteed sources (GuaranteedSources, cached per world) are the configured style's for both, close enough for an
		// estimate (they are placed on the same common enemies, pots and chests in both styles)
		private static (PacingResult vanilla, PacingResult permutation) SimulateBoth(int hours, int seed) =>
			(PacingModel.Simulate(hours, seed, DropTableStyle.VanillaLike), PacingModel.Simulate(hours, seed, DropTableStyle.Permutation));

		private static void PacingExpected()
		{
			(PacingResult vanilla, PacingResult permutation) = SimulateBoth(10, 42);
			foreach ((string style, PacingResult result) in new[] { ("vanilla-like", vanilla), ("permutation", permutation) })
			{
				Log($"Pacing: {style} {result.GearPerHour:0.0} pieces of equipment per hour (deviation {result.StandardDeviation:0.0}), {result.DistinctPerHour:0.0} different; "
					+ $"from original drops {result.FromOriginal:0.0}, extra drops {result.FromExtra:0.0}, guaranteed {result.FromGuaranteed:0.0}, surprises {result.FromSurprise:0.0}, "
					+ $"chests {result.FromChests:0.0}; by tier {string.Join(" / ", result.ByTier.Select(t => t.ToString("0.0")))}");
			}
			RequireDefaults(anyStyle: true);
			List<string> failures = new();
			if (vanilla.GearPerHour < MinGearPerHour || vanilla.GearPerHour > MaxGearPerHour)
			{
				failures.Add($"vanilla-like: {vanilla.GearPerHour:0.0} pieces of equipment per hour, expected {MinGearPerHour} to {MaxGearPerHour} (about 46 with LootAbundance 3)");
			}
			if (permutation.GearPerHour < MinPermutationGearPerHour || permutation.GearPerHour > MaxPermutationGearPerHour)
			{
				failures.Add($"permutation: {permutation.GearPerHour:0.0} pieces of equipment per hour, expected {MinPermutationGearPerHour} to {MaxPermutationGearPerHour} (about 20)");
			}
			Check.All(failures, "Equipment per early hour");
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
			(PacingResult vanilla, PacingResult permutation) = SimulateBoth(5, 44);
			Log($"Drops: {vanilla.ItemsPerKill:0.00} items per kill vanilla-like, {permutation.ItemsPerKill:0.00} permutation");
			RequireDefaults(anyStyle: true);
			if (!IsDefault(LootConfig.Instance, "CommonDropChance")) Check.Skip("CommonDropChance is not at its default value");
			List<string> failures = new();
			if (vanilla.ItemsPerKill < MinItemsPerKill) failures.Add($"vanilla-like: only {vanilla.ItemsPerKill:0.00} items per kill, expected at least {MinItemsPerKill} (about 3.2 with LootAbundance 3)");
			if (permutation.ItemsPerKill < MinPermutationItemsPerKill) failures.Add($"permutation: only {permutation.ItemsPerKill:0.00} items per kill, expected at least {MinPermutationItemsPerKill} (about 0.6)");
			Check.All(failures, "Items per kill");
		}

		// The work bench is a small quest: its recipe, rebuilt from materials found at tier 0 and made by hand, is the
		// main way (the first one comes as soon as its ingredients are gathered); its guardian, the Forest enemy the early kills
		// meet most, is the backup. From the model, with StationGuardianChance 12.5 %: a guardian making a third of the early
		// kills (the slimes' slot) gives one in about 6 minutes, one making a tenth in about 20; MaxGuardianBenchMinutes leaves
		// room for a Forest whose most met enemy is rarer. Both are logged (the guardian's time with a few fixed chances)
		public const double MaxGuardianBenchMinutes = 60;

		private static void WorkBenchPacing()
		{
			if (!GuaranteedSources.StationRequirements().Any(r => r.Item == ItemID.WorkBench)) Check.Skip("the work bench is protected");
			double minutes = PacingModel.MinutesToFirstWorkBench(400, 45);
			List<int> hosts = GuaranteedSources.HostsOf(ItemID.WorkBench).ToList();
			float planned = hosts.Count > 0 ? GuaranteedSources.For(hosts[0]).Where(d => d.Item == ItemID.WorkBench).Sum(d => d.Chance) : 0;
			// The same guardian with fixed chances, to see how the time follows the chance
			string candidates = string.Join(", ", new[] { 5f, 12.5f, 25f }.Select(c => $"{c}%: {PacingModel.MinutesToFirstWorkBench(400, 45, c / 100f):0}"));
			int biome = GuaranteedSources.BiomeOf(ItemID.WorkBench);
			// Its recipe: made by hand at tier 0 without any guardian (RecipePlanner.StationRecipeTiers)
			string recipe = "not randomized";
			int crafted = Findability.Never;
			bool byHand = false;
			if (RecipeSystem.Applied)
			{
				RecipePlan plan = RecipePlanner.Get();
				crafted = RecipePlanner.StationRecipeTiers(plan).GetValueOrDefault(ItemID.WorkBench, Findability.Never);
				int count = Math.Min(RecipeSystem.RecipeCount, plan.Results.Length);
				List<int> benches = Enumerable.Range(0, count).Where(i => plan.Results[i].type == ItemID.WorkBench && !Main.recipe[i].Disabled && !plan.Twin[i]).ToList();
				// By hand, and from start materials only (no ore: before any mining)
				HashSet<int> start = RecipePlanner.StartMaterials(false);
				byHand = benches.Any(i => (plan.Tiles[i] ?? RecipeSystem.OriginalTiles(i)).Length == 0
					&& (plan.Ingredients[i] ?? RecipeSystem.OriginalIngredients(i)).All(x => start.Contains(x.type)));
				recipe = benches.Count == 0 ? "none" : string.Join("; ", benches.Select(i => string.Join(", ", (plan.Ingredients[i] ?? RecipeSystem.OriginalIngredients(i)).Select(x => $"{x.stack} {Lang.GetItemNameValue(x.type)}"))
					+ ((plan.Tiles[i] ?? RecipeSystem.OriginalTiles(i)).Length == 0 ? " by hand" : " at " + string.Join("+", (plan.Tiles[i] ?? RecipeSystem.OriginalTiles(i)).Select(Stations.Name)))));
			}
			Log($"First work bench: recipe {recipe} (crafted at tier {(crafted == Findability.Never ? "never" : crafted.ToString())}); guardian alone {minutes:0} minutes on average (chance {planned:P1} on "
				+ $"{string.Join(", ", hosts.Select(Lang.GetNPCNameValue))}{(biome >= 0 ? " in " + BiomeSlots.BiomeName(biome) : "")}, {PacingModel.EarlyKillShare(hosts.Contains):P1} of the early kills; "
				+ $"fixed chances {candidates}); target: by hand at tier 0, guardian within {MaxGuardianBenchMinutes:0} minutes");
			ProgressionConfig progression = ProgressionConfig.Instance;
			if (!IsDefault(progression, "StationGuardianChance")) Check.Skip("StationGuardianChance is not at its default value");
			RequireDefaults();
			List<string> failures = new();
			if (RecipeSystem.Applied && CraftingConfig.Instance.IngredientMode == IngredientMode.Randomized && (crafted != 0 || !byHand)) failures.Add($"the work bench's recipe is not made by hand at tier 0 from start materials ({recipe})");
			if (minutes > MaxGuardianBenchMinutes) failures.Add($"its guardian alone gives one after {minutes:0} minutes on average, at most {MaxGuardianBenchMinutes:0}");
			Check.All(failures, "First work bench");
		}

		// Jackpots per early hour (luck and madness of the kills, the per-kill jackpot, giants and minis, with their share of
		// the progression), logged per hour; between MinJackpotsPerHour and MaxJackpotsPerHour before the first boss with the
		// default settings
		private static void JackpotPacing()
		{
			int tier = ProgressionSystem.UnlockedTier;
			double per100 = PacingModel.JackpotsPer100Kills(3000, 46, out double perKill, out double giantsMinis);
			double hour = PacingModel.KillsPerHour / 100.0;
			double perHour = per100 * hour;
			Log($"Jackpots: {perHour:0.00}/h at tier {tier} ({per100:0.00} per 100 kills, {PacingModel.KillsPerHour} kills per hour): per-kill jackpot {perKill * hour:0.00}/h, "
				+ $"giants and minis {giantsMinis * hour:0.00}/h, drops (luck and madness) {(per100 - perKill - giantsMinis) * hour:0.00}/h");
			RequireDefaults();
			ProgressionConfig progression = ProgressionConfig.Instance;
			foreach (string field in new[] { "RuntimeJackpotChance", "WildDropChance", "WildTierDecay", "KillJackpotChance", "TierAllowance", "TierGating" })
			{
				if (!IsDefault(progression, field)) Check.Skip($"{field} is not at its default value");
			}
			EnemiesConfig enemies = EnemiesConfig.Instance;
			foreach (string field in new[] { "GiantChance", "MiniChance", "GiantBonusDrop", "MiniBonusDrop" })
			{
				if (!IsDefault(enemies, field)) Check.Skip($"{field} is not at its default value");
			}
			if (tier != 0) Check.Skip($"the world is at tier {tier}, the bounds are for the start");
			Check.That(perHour >= MinJackpotsPerHour && perHour <= MaxJackpotsPerHour, $"{perHour:0.00} jackpots per early hour, expected {MinJackpotsPerHour} to {MaxJackpotsPerHour}");
		}

		private static void PacingVariety()
		{
			RequireDefaults(anyStyle: true);
			(PacingResult vanilla, PacingResult permutation) = SimulateBoth(10, 43);
			List<string> failures = new();
			if (vanilla.DistinctPerHour < MinDistinctPerHour) failures.Add($"vanilla-like: only {vanilla.DistinctPerHour:0.0} different pieces of equipment per hour, expected at least {MinDistinctPerHour}");
			if (permutation.DistinctPerHour < MinPermutationDistinctPerHour) failures.Add($"permutation: only {permutation.DistinctPerHour:0.0} different pieces of equipment per hour, expected at least {MinPermutationDistinctPerHour}");
			Check.All(failures, "Equipment variety");
		}
	}
}

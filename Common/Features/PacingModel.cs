using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	public struct PacingResult
	{
		// Pieces of equipment found per simulated hour, on average
		public double GearPerHour;
		public double StandardDeviation;
		// Different pieces of equipment per hour, on average
		public double DistinctPerHour;
		// Items of any kind per kill, on average
		public double ItemsPerKill;
	}

	// Simulates an hour of early game with the current world and settings, to check that random equipment comes
	// often enough (and not too often): kills, pots, chests, shaken trees and herbs. Kills use the real drop plans,
	// so LootAbundance (extra slots, their chances, small frequent drops) is modelled as it is applied in game.
	public static class PacingModel
	{
		// A typical early hour: the enemies killed, before the random enemy swap
		private static readonly (int npc, int count)[] EarlyKills =
		{
			(NPCID.GreenSlime, 25), (NPCID.BlueSlime, 25), (NPCID.Zombie, 30), (NPCID.DemonEye, 25), (NPCID.CaveBat, 15),
			(NPCID.Skeleton, 10), (NPCID.RedSlime, 5), (NPCID.YellowSlime, 5), (NPCID.Vulture, 5), (NPCID.Antlion, 5)
		};

		public const int Pots = 60;
		public const float ItemsPerPot = 1.2f;
		public const int Chests = 5;
		public const int OtherItemsPerChest = 5;
		public const int Trees = 15;
		public const float ItemsPerTree = 0.35f;
		public const int Herbs = 15;

		// Minutes until the first work bench, averaged over many simulated starts: kills (2.5 per minute), pots
		// (1 per minute) and a wooden chest every 12 minutes, which holds a work bench with the world's real odds
		public static double MinutesToFirstWorkBench(int runs, int seed)
		{
			Random rng = new(seed);
			EnemiesConfig enemies = EnemiesConfig.Instance;
			float swapChance = enemies.RandomizeEnemySpawns ? enemies.EnemySwapChance / 100f : 0f;
			List<int> pool = EnemySwapPool.NaturalEnemies(0);
			List<(int npc, int count)> kills = new(EarlyKills);
			int totalKills = kills.Sum(k => k.count);
			Dictionary<int, float> benchChance = new();
			float ChanceOn(int npc)
			{
				if (benchChance.TryGetValue(npc, out float chance)) return chance;
				// Guaranteed drops are per type: a Green Slime (net ID) is a Blue Slime
				int type = ContentSamples.NpcsByNetId.TryGetValue(npc, out NPC sample) ? sample.type : npc;
				chance = GuaranteedSources.For(type).Where(d => d.Item == ItemID.WorkBench).Sum(d => d.Chance);
				benchChance[npc] = chance;
				return chance;
			}
			int potItems = GuaranteedSources.PotItems(0).Count;
			float potChance = potItems == 0 ? 0 : ProgressionConfig.Instance.PotGuaranteeChance / 100f / potItems;
			(int benches, int wooden) = WoodenChestsWithWorkBench();
			double total = 0;
			for (int run = 0; run < runs; run++)
			{
				int minute = 0;
				for (; minute < 240; minute++)
				{
					bool found = false;
					for (int k = 0; k < 5 && !found; k++)
					{
						if (k >= 3 && rng.NextDouble() < 0.5) continue;
						int roll = rng.Next(totalKills);
						int npc = kills[0].npc;
						foreach ((int type, int count) in kills)
						{
							if (roll < count)
							{
								npc = type;
								break;
							}
							roll -= count;
						}
						if (pool.Count > 0 && rng.NextDouble() < swapChance) npc = pool[rng.Next(pool.Count)];
						found = rng.NextDouble() < ChanceOn(npc);
					}
					if (!found) found = rng.NextDouble() < potChance;
					if (!found && minute % 12 == 11 && wooden > 0) found = rng.NextDouble() < benches / (double)wooden;
					if (found) break;
				}
				total += minute + 1;
			}
			return total / runs;
		}

		// In this world (single player: chests are only known there)
		private static (int benches, int wooden) WoodenChestsWithWorkBench()
		{
			int benches = 0, wooden = 0;
			for (int c = 0; c < Main.maxChests; c++)
			{
				Chest chest = Main.chest[c];
				if (chest == null || !WorldGen.InWorld(chest.x, chest.y)) continue;
				Tile tile = Main.tile[chest.x, chest.y];
				if (!tile.HasTile || tile.TileType != TileID.Containers || tile.TileFrameX / 36 != 0) continue;
				wooden++;
				if (chest.item.Any(item => item != null && item.type == ItemID.WorkBench)) benches++;
			}
			return (benches, wooden);
		}

		public static PacingResult Simulate(int hours, int seed)
		{
			Random rng = new(seed);
			LootConfig loot = LootConfig.Instance;
			EnemiesConfig enemies = EnemiesConfig.Instance;
			float surprise = GeneralConfig.Instance.RandomizerMode == RandomizerMode.Coherent ? loot.SurpriseChance / 100f : 0f;
			float swapChance = enemies.RandomizeEnemySpawns ? enemies.EnemySwapChance / 100f : 0f;
			List<int> pool = EnemySwapPool.NaturalEnemies(0);
			Dictionary<int, List<PlannedDrop>> plans = new();
			List<double> perHour = new();
			double distinctTotal = 0;
			long kills = 0;
			long items = 0;
			for (int hour = 0; hour < hours; hour++)
			{
				int found = 0;
				HashSet<int> distinct = new();
				void Found(int item)
				{
					if (!ItemPool.IsValid(item) || !ItemCategories.IsGear(ItemPool.Info[item].Category)) return;
					found++;
					distinct.Add(item);
				}
				int SurpriseItem(int tier) => ItemPool.Roll(new RollRequest
				{
					Keys = new[] { 7171, rng.Next() }, SourceTier = tier, OriginalItem = -1, Policy = CategoryPolicy.Weighted, TierJitterUp = 1
				});

				// Kills
				foreach ((int npc, int count) in EarlyKills)
				{
					for (int k = 0; k < count; k++)
					{
						int killed = pool.Count > 0 && rng.NextDouble() < swapChance ? pool[rng.Next(pool.Count)] : npc;
						if (!plans.TryGetValue(killed, out List<PlannedDrop> plan))
						{
							plan = loot.RandomizeMobDrops ? RandomNpcDropRule.Plan(killed, Main.expertMode) : new List<PlannedDrop>();
							plans[killed] = plan;
						}
						kills++;
						foreach (PlannedDrop drop in plan)
						{
							if (rng.NextDouble() >= drop.Chance) continue;
							items++;
							if (!drop.Extra && rng.NextDouble() < surprise) Found(SurpriseItem(NpcTiers.EffectiveTier(killed)));
							else Found(drop.Item);
						}
					}
				}
				// Pots, trees and herbs only give equipment by surprise
				double surpriseRolls = Pots * ItemsPerPot + (loot.RandomizeTreeShaking ? Trees * ItemsPerTree : 0)
					+ (loot.RandomizeHerbs ? Herbs * (1 - loot.HerbKeepChance / 100f) : 0);
				if (!loot.RandomizePotDrops) surpriseRolls -= Pots * ItemsPerPot;
				for (int i = 0; i < (int)Math.Round(surpriseRolls); i++)
				{
					if (rng.NextDouble() < surprise) Found(SurpriseItem(0));
				}
				// Chests: one main item (usually equipment, replaced by the same kind) and a few others
				for (int c = 0; c < Chests; c++)
				{
					found++;
					for (int i = 0; i < OtherItemsPerChest; i++)
					{
						if (loot.RandomizeChests && rng.NextDouble() < surprise) Found(SurpriseItem(0));
					}
				}
				perHour.Add(found);
				distinctTotal += distinct.Count;
			}
			double mean = perHour.Average();
			double deviation = Math.Sqrt(perHour.Sum(x => (x - mean) * (x - mean)) / Math.Max(1, perHour.Count - 1));
			return new PacingResult { GearPerHour = mean, StandardDeviation = deviation, DistinctPerHour = distinctTotal / hours, ItemsPerKill = kills == 0 ? 0 : items / (double)kills };
		}
	}
}

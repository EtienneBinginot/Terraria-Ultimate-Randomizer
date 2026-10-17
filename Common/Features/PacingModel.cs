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
		// A typical early hour: the enemies killed, before the random enemy swap, and where they spawn. A zombie or a demon
		// eye is one of their kinds at random, as vanilla spawns them (each kind has its own Forest slot)
		private static readonly int[] Zombies = BiomeSlots.ForestZombieKinds;
		private static readonly int[] DemonEyes = BiomeSlots.ForestEyeKinds;

		private static readonly (int[] npcs, int count, SpawnBiome biome)[] EarlyKills =
		{
			(new int[] { NPCID.GreenSlime }, 25, SpawnBiome.Forest), (new int[] { NPCID.BlueSlime }, 25, SpawnBiome.Forest), (Zombies, 30, SpawnBiome.Forest),
			(DemonEyes, 25, SpawnBiome.Forest), (new int[] { NPCID.CaveBat }, 15, SpawnBiome.Caverns), (new int[] { NPCID.Skeleton }, 10, SpawnBiome.Caverns),
			(new int[] { NPCID.RedSlime }, 5, SpawnBiome.Underground), (new int[] { NPCID.YellowSlime }, 5, SpawnBiome.Underground),
			(new int[] { NPCID.Vulture }, 5, SpawnBiome.Desert), (new int[] { NPCID.Antlion }, 5, SpawnBiome.Desert)
		};

		// Kills per simulated minute in MinutesToFirstWorkBench: 3, then 2 more half of the time
		public const double KillsPerMinute = 4;

		private static int TotalKills => EarlyKills.Sum(k => k.count);

		// The kind of an early kill (one of its family)
		private static int Kind(int[] npcs, Random rng) => npcs.Length == 1 ? npcs[0] : npcs[rng.Next(npcs.Length)];

		// The enemy really killed: by biome, the owner of the original's slot (pre-hardmode slots are always open);
		// otherwise a random natural enemy of the first tier
		private static int Killed(int npc, SpawnBiome biome, Random rng, float swapChance, List<int> pool)
		{
			if (rng.NextDouble() >= swapChance) return npc;
			if (BiomeSlots.Active)
			{
				BiomeSlot slot = BiomeSlots.SlotIn(BiomeSlots.Current, (int)biome, npc);
				return slot != null && BiomeSlots.SlotActive(slot) ? BiomeSlots.Pick(slot, rng.NextDouble()) : npc;
			}
			return pool.Count > 0 ? pool[rng.Next(pool.Count)] : npc;
		}

		// The share of the early kills (as Killed draws them, without randomness) that are of the types accepted (types: a
		// Green Slime is a Blue Slime). Same on every machine: spawn map, synced settings. Hardmode slots count as open (the
		// early slots always are), so planning before or after hardmode gives the same chances
		public static double EarlyKillShare(Func<int, bool> isType)
		{
			float swapChance = SwapChance();
			List<int> pool = BiomeSlots.Active ? null : EnemySwapPool.NaturalEnemies(0);
			double poolShare = pool == null || pool.Count == 0 ? 0 : pool.Count(n => isType(BiomeSlots.TypeOf(n))) / (double)pool.Count;
			double total = 0;
			foreach ((int[] npcs, int count, SpawnBiome biome) in EarlyKills)
			{
				foreach (int npc in npcs)
				{
					double kept = isType(BiomeSlots.TypeOf(npc)) ? 1 : 0;
					double swapped;
					if (BiomeSlots.Active)
					{
						BiomeSlot slot = BiomeSlots.SlotIn(BiomeSlots.Current, (int)biome, npc);
						swapped = slot == null ? kept : BiomeSlots.SpawnParts(slot).Where(p => isType(BiomeSlots.TypeOf(p.id))).Sum(p => p.part);
					}
					else swapped = pool.Count > 0 ? poolShare : kept;
					total += count / (double)npcs.Length * ((1 - swapChance) * kept + swapChance * swapped);
				}
			}
			return total / TotalKills;
		}
		private static float SwapChance()
		{
			EnemiesConfig enemies = EnemiesConfig.Instance;
			if (!enemies.RandomizeEnemySpawns) return 0f;
			return BiomeSlots.Active ? 1f - enemies.BiomeVanillaChance / 100f : enemies.EnemySwapChance / 100f;
		}

		public const int Pots = 60;
		public const float ItemsPerPot = 1.2f;
		public const int Chests = 5;
		public const int OtherItemsPerChest = 5;
		public const int Trees = 15;
		public const float ItemsPerTree = 0.35f;
		public const int Herbs = 15;

		// Minutes until the first work bench, averaged over many simulated starts: kills (KillsPerMinute), pots
		// (1 per minute) and a wooden chest every 12 minutes, which holds a work bench with the world's real odds.
		// chanceOverride: the work bench's chance on each of its hosts instead of the planned one (the test logs a few)
		public static double MinutesToFirstWorkBench(int runs, int seed, float? chanceOverride = null)
		{
			Random rng = new(seed);
			float swapChance = SwapChance();
			List<int> pool = EnemySwapPool.NaturalEnemies(0);
			List<(int[] npcs, int count, SpawnBiome biome)> kills = new(EarlyKills);
			int totalKills = TotalKills;
			Dictionary<int, float> benchChance = new();
			float ChanceOn(int npc)
			{
				if (benchChance.TryGetValue(npc, out float chance)) return chance;
				// Guaranteed drops are per type: a Green Slime (net ID) is a Blue Slime
				int type = BiomeSlots.TypeOf(npc);
				List<PlannedDrop> drops = GuaranteedSources.For(type).Where(d => d.Item == ItemID.WorkBench).ToList();
				chance = chanceOverride.HasValue ? (drops.Count > 0 ? chanceOverride.Value : 0) : drops.Sum(d => d.Chance);
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
						int[] npcs = kills[0].npcs;
						SpawnBiome biome = kills[0].biome;
						foreach ((int[] types, int count, SpawnBiome where) in kills)
						{
							if (roll < count)
							{
								npcs = types;
								biome = where;
								break;
							}
							roll -= count;
						}
						int npc = Kind(npcs, rng);
						npc = Killed(npc, biome, rng, swapChance, pool);
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

		// Jackpots (ItemPool.LastJackpotGap) per 100 early kills, expected from the runtime rolls of the kills: the extra drops
		// (one lucky roll per kill, as RandomNpcDropRule does) and the replaced original drops (one lucky roll each), weighted
		// by their chances. Loot bonuses and giants are left out
		public static double JackpotsPer100Kills(int kills, int seed)
		{
			if (!LootConfig.Instance.RandomizeMobDrops || kills <= 0) return 0;
			Random rng = new(seed);
			float swapChance = SwapChance();
			List<int> pool = EnemySwapPool.NaturalEnemies(0);
			int totalKills = TotalKills;
			float luckyChance = ProgressionConfig.Instance.RuntimeJackpotChance / 100f;
			double jackpots = 0;
			for (int k = 0; k < kills; k++)
			{
				int roll = rng.Next(totalKills);
				(int[] npcs, int count, SpawnBiome biome) picked = EarlyKills[0];
				foreach ((int[] npcs, int count, SpawnBiome biome) entry in EarlyKills)
				{
					if (roll < entry.count)
					{
						picked = entry;
						break;
					}
					roll -= entry.count;
				}
				int killed = Killed(Kind(picked.npcs, rng), picked.biome, rng, swapChance, pool);
				int owner = BossParts.Owner(ContentSamples.NpcsByNetId.TryGetValue(killed, out NPC sample) ? sample.type : killed);
				foreach (PlannedDrop drop in RandomNpcDropRule.ExtraDrops(killed, owner, Main.expertMode, rng.NextDouble() < luckyChance, wildRng: new Random(rng.Next())))
				{
					if (drop.JackpotGap > 0) jackpots += Math.Clamp(drop.Chance, 0f, 1f);
				}
				foreach (Terraria.GameContent.ItemDropRules.DropRateInfo entry in LootSnapshot.ForNpc(killed))
				{
					Random wild = new(rng.Next());
					DropHelper.Replacement(RollKey.MobDrop, DetRandom.NpcKey(owner), entry.itemId, NpcTiers.EffectiveTier(owner), 0, rng.NextDouble() < luckyChance, wild, wild);
					if (ItemPool.LastJackpotGap > 0) jackpots += Math.Clamp(entry.dropRate, 0f, 1f);
				}
			}
			ItemPool.ClearJackpot();
			return jackpots * 100 / kills;
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
			float surprise = GeneralConfig.Instance.RandomizerMode == RandomizerMode.Coherent ? loot.SurpriseChance / 100f : 0f;
			float swapChance = SwapChance();
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
				foreach ((int[] npcs, int count, SpawnBiome biome) in EarlyKills)
				{
					for (int k = 0; k < count; k++)
					{
						int killed = Killed(Kind(npcs, rng), biome, rng, swapChance, pool);
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

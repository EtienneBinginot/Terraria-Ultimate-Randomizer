using System;
using System.Collections.Generic;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	// Naturally spawning enemies are turned into another enemy of the same tier that moves the same way
	public class EnemySwapNPC : GlobalNPC
	{
		public override void OnSpawn(NPC npc, IEntitySource source)
		{
			EnemiesConfig cfg = EnemiesConfig.Instance;
			if (source is not EntitySource_SpawnNPC || Main.netMode == NetmodeID.MultiplayerClient) return;
			if (!cfg.RandomizeEnemySpawns || !SeedSystem.WorldReady || npc.SpawnedFromStatue) return;
			int target = EnemySwapPool.Replacement(npc.type);
			if (target > 0 && target != npc.type) npc.Transform(target);
		}
	}

	public static class EnemySwapPool
	{
		private enum Movement { Ground, Flying, Water, Critter }

		private const int MovementCount = 4;

		private static List<int>[,] buckets;
		private static Dictionary<int, Movement> movements;

		public static int Replacement(int npcType)
		{
			Build();
			if (!movements.TryGetValue(npcType, out Movement movement)) return npcType;
			EnemiesConfig cfg = EnemiesConfig.Instance;
			int tier = NpcTiers.BaseTier(npcType);
			Random rng;
			if (cfg.EnemySwapMode == EnemySwapMode.PerType)
			{
				rng = DetRandom.Get(RollKey.EnemySwap, DetRandom.NpcKey(npcType));
				if (rng.NextDouble() * 100 >= cfg.EnemySwapChance) return npcType;
			}
			else
			{
				if (Main.rand.NextFloat() * 100 >= cfg.EnemySwapChance) return npcType;
				rng = new Random(Main.rand.Next());
			}
			// Same tier, or the closest one below then above
			for (int t = tier; t >= 0; t--)
			{
				if (buckets[(int)movement, t].Count > 0) return buckets[(int)movement, t][rng.Next(buckets[(int)movement, t].Count)];
			}
			for (int t = tier + 1; t < Tiers.Count; t++)
			{
				if (buckets[(int)movement, t].Count > 0) return buckets[(int)movement, t][rng.Next(buckets[(int)movement, t].Count)];
			}
			return npcType;
		}

		private static void Build()
		{
			if (buckets != null) return;
			EnemiesConfig cfg = EnemiesConfig.Instance;
			buckets = new List<int>[MovementCount, Tiers.Count];
			for (int m = 0; m < MovementCount; m++)
			{
				for (int t = 0; t < Tiers.Count; t++) buckets[m, t] = new List<int>();
			}
			movements = new Dictionary<int, Movement>();
			List<int> types = new(ContentSamples.NpcsByNetId.Keys);
			types.Sort();
			foreach (int type in types)
			{
				if (type <= 0 || !ContentSamples.NpcsByNetId.TryGetValue(type, out NPC npc)) continue;
				Movement? movement = Classify(type, npc, cfg);
				if (movement == null) continue;
				movements[type] = movement.Value;
				buckets[(int)movement.Value, NpcTiers.BaseTier(type)].Add(type);
			}
		}

		private static Movement? Classify(int type, NPC npc, EnemiesConfig cfg)
		{
			if (npc.boss || NPCID.Sets.ShouldBeCountedAsBoss[type] || npc.townNPC || NPCID.Sets.ActsLikeTownNPC[type]) return null;
			if (NPCID.Sets.ProjectileNPC[type] || npc.lifeMax <= 5 || npc.dontTakeDamage || npc.immortal) return null;
			// Worms and multi-part enemies can't be transformed safely
			if (npc.aiStyle == NPCAIStyleID.Worm || npc.realLife >= 0) return null;
			if (npc.ModNPC != null && !cfg.SwapModdedEnemies) return null;
			if (!cfg.SwapEventEnemies && IsEventEnemy(type)) return null;
			if (NPCID.Sets.CountsAsCritter[type]) return cfg.SwapCritters ? Movement.Critter : null;
			if (npc.friendly) return null;
			if (npc.aiStyle == NPCAIStyleID.Piranha || npc.aiStyle == NPCAIStyleID.Jellyfish) return Movement.Water;
			return npc.noGravity ? Movement.Flying : Movement.Ground;
		}

		private static bool IsEventEnemy(int type) =>
			NPCID.Sets.BelongsToInvasionOldOnesArmy[type] || NPCID.Sets.BelongsToInvasionGoblinArmy[type] || NPCID.Sets.BelongsToInvasionFrostLegion[type]
			|| NPCID.Sets.BelongsToInvasionPirate[type] || NPCID.Sets.BelongsToInvasionMartianMadness[type];

		public static void Unload()
		{
			buckets = null;
			movements = null;
		}
	}

	public class EnemySwapSystem : ModSystem
	{
		public override void Unload() => EnemySwapPool.Unload();
	}
}

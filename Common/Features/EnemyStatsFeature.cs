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
	// Multiplies the base stats of each enemy type by factors fixed for the world.
	// Applied before the expert/master/multiplayer scaling, which then applies on top as usual.
	public class EnemyStatsNPC : GlobalNPC
	{
		public override void SetDefaults(NPC npc)
		{
			// Also called at load for the bestiary and content samples: they must keep the original stats
			if (!SeedSystem.WorldReady) return;
			if (npc.townNPC || npc.friendly || NPCID.Sets.CountsAsCritter[npc.type] || NPCID.Sets.ProjectileNPC[npc.type]) return;
			// Boss parts (worm segments, hands, cannons...) share the boss's multipliers
			int owner = BossParts.Owner(npc.type);
			EnemyMultipliers m = EnemyStatCache.Get(owner, IsBossLike(npc));
			if (m.Life != 1) npc.lifeMax = Math.Max(1, (int)(npc.lifeMax * m.Life));
			npc.life = npc.lifeMax;
			npc.damage = (int)(npc.damage * m.Damage);
			npc.defense = (int)(npc.defense * m.Defense);
			// Worm segments of different sizes would leave gaps between them
			if (Math.Abs(m.Scale - 1) > 0.01 && npc.aiStyle != NPCAIStyleID.Worm && owner == npc.type)
			{
				npc.scale *= m.Scale;
				npc.width = Math.Max(1, (int)(npc.width * m.Scale));
				npc.height = Math.Max(1, (int)(npc.height * m.Scale));
			}
		}

		public static bool IsBossLike(NPC npc) => npc.boss || NPCID.Sets.ShouldBeCountedAsBoss[npc.type] || BossParts.IsPart(npc.type);
	}

	// Boss projectiles get the boss damage multiplier too (contact damage alone would barely matter)
	public class BossProjectileDamage : GlobalProjectile
	{
		public override void OnSpawn(Projectile projectile, IEntitySource source)
		{
			if (!SeedSystem.WorldReady || !projectile.hostile || source is not EntitySource_Parent parent || parent.Entity is not NPC npc) return;
			if (!EnemyStatsNPC.IsBossLike(npc)) return;
			float multiplier = EnemyStatCache.Get(BossParts.Owner(npc.type), true).Damage;
			if (multiplier != 1) projectile.damage = Math.Max(1, (int)(projectile.damage * multiplier));
		}
	}

	public struct EnemyMultipliers
	{
		public float Life;
		public float Damage;
		public float Defense;
		public float Scale;
	}

	public static class EnemyStatCache
	{
		private static readonly Dictionary<int, EnemyMultipliers> cache = new();

		public static EnemyMultipliers Get(int npcType, bool boss)
		{
			if (cache.TryGetValue(npcType, out EnemyMultipliers m)) return m;
			EnemiesConfig cfg = EnemiesConfig.Instance;
			int key = DetRandom.NpcKey(npcType);
			m = new EnemyMultipliers { Life = 1, Damage = 1, Defense = 1, Scale = 1 };
			if (boss)
			{
				if (cfg.RandomizeBossHealth) m.Life = Multiplier(RollKey.BossHealth, key, cfg.BossHealthMin, cfg.BossHealthMax);
				if (cfg.RandomizeBossDamage) m.Damage = Multiplier(RollKey.BossDamage, key, cfg.BossDamageMin, cfg.BossDamageMax);
			}
			else if (cfg.RandomizeEnemyStats)
			{
				m.Life = Multiplier(RollKey.EnemyLife, key, cfg.EnemyHealthMin, cfg.EnemyHealthMax);
				m.Damage = Multiplier(RollKey.EnemyDamage, key, cfg.EnemyDamageMin, cfg.EnemyDamageMax);
				m.Defense = Multiplier(RollKey.EnemyDefense, key, cfg.EnemyDefenseMin, cfg.EnemyDefenseMax);
				m.Scale = Multiplier(RollKey.EnemyScale, key, cfg.EnemyScaleMin, cfg.EnemyScaleMax);
			}
			cache[npcType] = m;
			return m;
		}

		private static float Multiplier(int rollKey, int npcKey, float min, float max)
		{
			if (min > max) (min, max) = (max, min);
			return (float)(min + DetRandom.Get(rollKey, npcKey).NextDouble() * (max - min));
		}

		public static void Clear() => cache.Clear();
	}

	public class EnemyStatSystem : ModSystem
	{
		public override void PostSetupRecipes() => SeedSystem.OnSeedChanged += EnemyStatCache.Clear;

		public override void Unload() => EnemyStatCache.Clear();
	}
}

using System;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	// Multiplies the base stats of each enemy type by a factor fixed for the world.
	// Applied before the expert/master/multiplayer scaling, which then applies on top as usual.
	public class EnemyStatsNPC : GlobalNPC
	{
		public override void SetDefaults(NPC npc)
		{
			// Also called at load for the bestiary and content samples: they must keep the original stats
			if (!SeedSystem.WorldReady) return;
			if (npc.townNPC || npc.friendly || npc.lifeMax <= 5 || NPCID.Sets.CountsAsCritter[npc.type] || NPCID.Sets.ProjectileNPC[npc.type]) return;
			EnemiesConfig cfg = EnemiesConfig.Instance;
			int key = DetRandom.NpcKey(npc.type);
			if (npc.boss || NPCID.Sets.ShouldBeCountedAsBoss[npc.type])
			{
				if (cfg.RandomizeBossHealth) ScaleLife(npc, Multiplier(RollKey.BossHealth, key, cfg.BossHealthMin, cfg.BossHealthMax));
				if (cfg.RandomizeBossDamage) npc.damage = (int)(npc.damage * Multiplier(RollKey.BossDamage, key, cfg.BossDamageMin, cfg.BossDamageMax));
				return;
			}
			if (!cfg.RandomizeEnemyStats) return;
			ScaleLife(npc, Multiplier(RollKey.EnemyLife, key, cfg.EnemyHealthMin, cfg.EnemyHealthMax));
			npc.damage = (int)(npc.damage * Multiplier(RollKey.EnemyDamage, key, cfg.EnemyDamageMin, cfg.EnemyDamageMax));
			npc.defense = (int)(npc.defense * Multiplier(RollKey.EnemyDefense, key, cfg.EnemyDefenseMin, cfg.EnemyDefenseMax));
			double scale = Multiplier(RollKey.EnemyScale, key, cfg.EnemyScaleMin, cfg.EnemyScaleMax);
			if (Math.Abs(scale - 1) > 0.01)
			{
				npc.scale *= (float)scale;
				npc.width = Math.Max(1, (int)(npc.width * scale));
				npc.height = Math.Max(1, (int)(npc.height * scale));
			}
		}

		private static void ScaleLife(NPC npc, double multiplier)
		{
			npc.lifeMax = Math.Max(1, (int)(npc.lifeMax * multiplier));
			npc.life = npc.lifeMax;
		}

		private static double Multiplier(int rollKey, int npcKey, float min, float max)
		{
			if (min > max) (min, max) = (max, min);
			return min + DetRandom.Get(rollKey, npcKey).NextDouble() * (max - min);
		}
	}
}

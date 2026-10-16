using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	// Multiplies the base stats of each enemy type by factors fixed for the world (bell-shaped: extremes are rare).
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

	public enum EnemyKind : byte { Normal, Giant, Mini }

	// Each enemy also varies a little around its type, and a few are giants or minis. Rolled where the enemy spawns
	// (server or single player) and sent to the clients with the NPC.
	public class EnemyIndividualNPC : GlobalNPC
	{
		public float Life = 1;
		public float Damage = 1;
		public float Defense = 1;
		public float Size = 1;
		public EnemyKind Kind;
		private bool rolled;
		private bool applied;

		public bool Rolled => rolled;

		public override bool InstancePerEntity => true;

		public override void OnSpawn(NPC npc, IEntitySource source)
		{
			if (Main.netMode != NetmodeID.MultiplayerClient) RollAndApply(npc);
		}

		// Also after a swap: Transform gives the NPC new globals
		public static void RollAndApply(NPC npc)
		{
			if (!npc.TryGetGlobalNPC(out EnemyIndividualNPC individual)) return;
			individual.Roll(npc, new Random(Main.rand.Next()));
			individual.Apply(npc);
			npc.netUpdate = true;
		}

		public static bool Eligible(NPC npc) =>
			SeedSystem.WorldReady && !npc.townNPC && !npc.friendly && !NPCID.Sets.CountsAsCritter[npc.type] && !NPCID.Sets.ProjectileNPC[npc.type]
			&& npc.lifeMax > 5 && !BossParts.IsPart(npc.type);

		// Worms (segments would come apart) and boss parts keep their size
		public static bool Scalable(NPC npc) => Eligible(npc) && npc.aiStyle != NPCAIStyleID.Worm;

		public void Roll(NPC npc, Random rng)
		{
			Life = Damage = Defense = Size = 1;
			Kind = EnemyKind.Normal;
			rolled = true;
			applied = false;
			if (!Eligible(npc)) return;
			EnemiesConfig cfg = EnemiesConfig.Instance;
			bool boss = npc.boss || NPCID.Sets.ShouldBeCountedAsBoss[npc.type];
			double extremeness = boss ? cfg.BossStatExtremeness : cfg.StatExtremeness;
			float variation = (boss ? cfg.BossIndividualVariation : cfg.RandomizeEnemyStats ? cfg.IndividualVariation : 0) / 100f;
			bool scalable = Scalable(npc);
			if (variation > 0)
			{
				float low = 1 / (1 + variation);
				float high = 1 + variation;
				Life = StatCurve.Sample(rng, low, high, extremeness);
				Damage = StatCurve.Sample(rng, low, high, extremeness);
				Defense = StatCurve.Sample(rng, low, high, extremeness);
				if (scalable) Size = StatCurve.Sample(rng, low, high, extremeness);
			}
			if (!scalable) return;
			double roll = rng.NextDouble() * 100;
			float giantChance = boss ? cfg.BossGiantChance : cfg.GiantChance;
			float miniChance = boss ? 0 : cfg.MiniChance;
			if (roll < giantChance)
			{
				// Mostly small giants, sometimes huge ones
				float size = StatCurve.Between(StatCurve.OneSided(rng, extremeness), cfg.GiantSizeMin, Math.Max(cfg.GiantSizeMin, cfg.GiantSizeMax));
				Kind = EnemyKind.Giant;
				Size *= size;
				Life *= size * size;
				Damage *= (float)Math.Sqrt(size);
				Defense *= (float)Math.Sqrt(size);
			}
			else if (roll < giantChance + miniChance)
			{
				float size = StatCurve.Between(StatCurve.OneSided(rng, extremeness), Math.Max(cfg.MiniSizeMin, cfg.MiniSizeMax), Math.Min(cfg.MiniSizeMin, cfg.MiniSizeMax));
				Kind = EnemyKind.Mini;
				Size *= size;
				Life *= size;
				Damage *= (float)Math.Sqrt(size);
			}
		}

		public void Apply(NPC npc)
		{
			if (applied) return;
			applied = true;
			// Health is synced by vanilla: only the server (or single player) changes it
			if (Main.netMode != NetmodeID.MultiplayerClient && Life != 1)
			{
				npc.lifeMax = Math.Max(1, (int)(npc.lifeMax * Life));
				npc.life = npc.lifeMax;
			}
			npc.damage = (int)(npc.damage * Damage);
			npc.defDamage = npc.damage;
			npc.defense = (int)(npc.defense * Defense);
			npc.defDefense = npc.defense;
			if (Math.Abs(Size - 1) < 0.01f) return;
			// Giants stay small enough to move around
			int cap = EnemiesConfig.Instance.MaxGiantPixels;
			float size = Size;
			if (size > 1) size = Math.Max(1, Math.Min(size, Math.Min(cap / (float)Math.Max(1, npc.width), cap / (float)Math.Max(1, npc.height))));
			Size = size;
			Vector2 bottom = npc.Bottom;
			npc.scale *= size;
			npc.width = Math.Max(2, (int)(npc.width * size));
			npc.height = Math.Max(2, (int)(npc.height * size));
			npc.Bottom = bottom;
		}

		public override void SendExtraAI(NPC npc, BitWriter bitWriter, BinaryWriter binaryWriter)
		{
			bitWriter.WriteBit(rolled);
			if (!rolled) return;
			binaryWriter.Write(Life);
			binaryWriter.Write(Damage);
			binaryWriter.Write(Defense);
			binaryWriter.Write(Size);
			binaryWriter.Write((byte)Kind);
		}

		public override void ReceiveExtraAI(NPC npc, BitReader bitReader, BinaryReader binaryReader)
		{
			if (!bitReader.ReadBit()) return;
			float life = binaryReader.ReadSingle();
			float damage = binaryReader.ReadSingle();
			float defense = binaryReader.ReadSingle();
			float size = binaryReader.ReadSingle();
			EnemyKind kind = (EnemyKind)binaryReader.ReadByte();
			if (applied) return;
			Life = life;
			Damage = damage;
			Defense = defense;
			Size = size;
			Kind = kind;
			rolled = true;
			Apply(npc);
		}

		// Extra drop chance multiplier of a strong enemy (type and individual health x damage above normal)
		public static float LootBonus(NPC npc)
		{
			EnemiesConfig cfg = EnemiesConfig.Instance;
			EnemyMultipliers type = EnemyStatCache.Get(BossParts.Owner(npc.type), EnemyStatsNPC.IsBossLike(npc));
			float factor = type.Life * type.Damage;
			if (npc.TryGetGlobalNPC(out EnemyIndividualNPC individual)) factor *= individual.Life * individual.Damage;
			return Bonus(factor, cfg.LootBonusStrength, cfg.LootBonusCap);
		}

		public static float Bonus(float factor, float strength, float cap) => factor <= 1 ? 1 : Math.Min(Math.Max(1, cap), 1 + strength * (factor - 1));
	}

	// Boss projectiles get the boss damage multiplier too (contact damage alone would barely matter)
	public class BossProjectileDamage : GlobalProjectile
	{
		public override void OnSpawn(Projectile projectile, IEntitySource source)
		{
			if (!SeedSystem.WorldReady || !projectile.hostile || source is not EntitySource_Parent parent || parent.Entity is not NPC npc) return;
			if (!EnemyStatsNPC.IsBossLike(npc)) return;
			float multiplier = EnemyStatCache.Get(BossParts.Owner(npc.type), true).Damage;
			if (npc.TryGetGlobalNPC(out EnemyIndividualNPC individual)) multiplier *= individual.Damage;
			if (Math.Abs(multiplier - 1) > 0.001f) projectile.damage = Math.Max(1, (int)(projectile.damage * multiplier));
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
				if (cfg.RandomizeBossHealth) m.Life = StatCurve.Sample(DetRandom.Get(RollKey.BossHealth, key), cfg.BossHealthMin, cfg.BossHealthMax, cfg.BossStatExtremeness);
				if (cfg.RandomizeBossDamage) m.Damage = StatCurve.Sample(DetRandom.Get(RollKey.BossDamage, key), cfg.BossDamageMin, cfg.BossDamageMax, cfg.BossStatExtremeness);
			}
			else if (cfg.RandomizeEnemyStats)
			{
				double extremeness = cfg.StatExtremeness;
				double lifePosition = StatCurve.Position(DetRandom.Get(RollKey.EnemyLife, key), extremeness);
				m.Life = StatCurve.FromPosition(lifePosition, cfg.EnemyHealthMin, cfg.EnemyHealthMax);
				m.Damage = StatCurve.Sample(DetRandom.Get(RollKey.EnemyDamage, key), cfg.EnemyDamageMin, cfg.EnemyDamageMax, extremeness);
				m.Defense = StatCurve.Sample(DetRandom.Get(RollKey.EnemyDefense, key), cfg.EnemyDefenseMin, cfg.EnemyDefenseMax, extremeness);
				// Tougher types tend to be bigger
				double ownPosition = StatCurve.Position(DetRandom.Get(RollKey.EnemyScale, key), extremeness);
				double follow = Math.Clamp(cfg.ScaleFollowsLife, 0, 1);
				m.Scale = StatCurve.FromPosition(follow * lifePosition + (1 - follow) * ownPosition, cfg.EnemyScaleMin, cfg.EnemyScaleMax);
			}
			cache[npcType] = m;
			return m;
		}

		public static void Clear() => cache.Clear();
	}

	public class EnemyStatSystem : ModSystem
	{
		public override void PostSetupRecipes() => SeedSystem.OnSeedChanged += EnemyStatCache.Clear;

		public override void Unload() => EnemyStatCache.Clear();
	}
}

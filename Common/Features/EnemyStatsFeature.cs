using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
		// Part of Life and Damage that comes from adaptive power (already included in them)
		public float AdaptiveLife = 1;
		public float AdaptiveDamage = 1;
		public EnemyKind Kind;
		// Identifies a roll: a client that already applied another roll of this NPC undoes it first
		public byte RollId;
		private bool rolled;
		private bool applied;
		// Stats before Apply, to undo it
		private int baseLifeMax, baseDamage, baseDefense, baseWidth, baseHeight;
		private float baseScale;

		public bool Rolled => rolled;

		public override bool InstancePerEntity => true;

		// The roll of each NPC slot (server or single player): every SetDefaults (variants right after the spawn, vanilla
		// Transform of fighters, spiders, shimmer, evil conversions...) recreates the globals, and the NPC gets the same roll
		// back instead of a new one. Health ratio of each slot at the end of the last update, kept across those resets.
		private static readonly Stored?[] slotRolls = new Stored?[Main.maxNPCs + 1];
		private static readonly float[] slotRatios = Enumerable.Repeat(-1f, Main.maxNPCs + 1).ToArray();
		private static byte lastRollId;

		private struct Stored
		{
			public float Life, Damage, Defense, Size, AdaptiveLife, AdaptiveDamage;
			public EnemyKind Kind;
			public byte Id;
		}

		public override void OnSpawn(NPC npc, IEntitySource source)
		{
			if (Main.netMode == NetmodeID.MultiplayerClient) return;
			ClearSlot(npc.whoAmI);
			RollAndApply(npc);
		}

		private static void ClearSlot(int slot)
		{
			if (slot < 0 || slot >= slotRolls.Length) return;
			slotRolls[slot] = null;
			slotRatios[slot] = -1;
		}

		public static void ClearAll()
		{
			Array.Clear(slotRolls);
			Array.Fill(slotRatios, -1);
		}

		// A new roll (spawn, swap: Transform gives the NPC new globals), kept for the slot
		public static void RollAndApply(NPC npc) => RollAndApply(npc, new Random(Main.rand.Next()));

		public static void RollAndApply(NPC npc, Random rng)
		{
			if (!npc.TryGetGlobalNPC(out EnemyIndividualNPC individual)) return;
			individual.Roll(npc, rng);
			individual.Store(npc);
			individual.Apply(npc);
			npc.netUpdate = true;
		}

		// Keeps the roll for the NPC's slot (re-applied by RollMissing after a SetDefaults)
		private void Store(NPC npc)
		{
			if (npc.whoAmI < 0 || npc.whoAmI >= slotRolls.Length) return;
			slotRolls[npc.whoAmI] = new Stored
			{
				Life = Life, Damage = Damage, Defense = Defense, Size = Size, AdaptiveLife = AdaptiveLife, AdaptiveDamage = AdaptiveDamage, Kind = Kind, Id = RollId
			};
		}

		private static byte NextRollId()
		{
			lastRollId = (byte)(lastRollId == byte.MaxValue ? 1 : lastRollId + 1);
			return lastRollId;
		}

		// A living boss measured again (AdaptivePower.RaiseBosses, server or single player): the adaptive multipliers only
		// go up. The rise is applied on top of the current stats, keeping the health ratio; the slot's stored roll gets the
		// new values (RollMissing re-applies them once on base stats) and a new RollId makes the clients re-apply it
		public void RaiseAdaptive(NPC npc, float life, float damage)
		{
			if (!rolled || !applied) return;
			float lifeFactor = Math.Max(1, life / Math.Max(0.0001f, AdaptiveLife));
			float damageFactor = Math.Max(1, damage / Math.Max(0.0001f, AdaptiveDamage));
			if (lifeFactor < 1.001f && damageFactor < 1.001f) return;
			AdaptiveLife *= lifeFactor;
			AdaptiveDamage *= damageFactor;
			Life *= lifeFactor;
			// The damage cap may leave less room than the measured rise (never lowers the current damage)
			float oldDamage = Damage;
			Damage = Math.Min(Damage * damageFactor, Math.Max(Damage, DamageCap(npc)));
			damageFactor = Damage / Math.Max(0.0001f, oldDamage);
			float ratio = npc.lifeMax > 0 ? Math.Clamp(npc.life / (float)npc.lifeMax, 0, 1) : 1;
			npc.lifeMax = Math.Max(1, (int)(baseLifeMax * Life));
			npc.life = Math.Clamp((int)Math.Ceiling(npc.lifeMax * ratio), 1, npc.lifeMax);
			// Bosses change their contact damage between phases: scale the current value, and the default from the base
			npc.damage = (int)(npc.damage * damageFactor);
			npc.defDamage = (int)(baseDamage * Damage);
			RollId = NextRollId();
			Store(npc);
			npc.netUpdate = true;
		}

		// After the NPC update (server or single player): NPCs whose globals were recreated get their slot's roll back
		// (a new one if the slot has none), with the health ratio they had; netUpdate sends them to the clients.
		// The stored roll already includes the adaptive multipliers measured at spawn: re-applied once, never compounded
		public static void RollMissing()
		{
			if (Main.netMode == NetmodeID.MultiplayerClient || !SeedSystem.WorldReady) return;
			for (int i = 0; i < Main.maxNPCs; i++) RollMissing(Main.npc[i]);
		}

		public static void RollMissing(NPC npc)
		{
			// Boss parts too: they carry adaptive multipliers
			if (!npc.active || !npc.TryGetGlobalNPC(out EnemyIndividualNPC individual) || individual.rolled || !Rollable(npc)) return;
			int slot = npc.whoAmI;
			bool inRange = slot >= 0 && slot < slotRolls.Length;
			float ratio = npc.lifeMax > 0 ? npc.life / (float)npc.lifeMax : 1;
			if (inRange && slotRatios[slot] > 0) ratio = Math.Min(ratio, slotRatios[slot]);
			if (inRange && slotRolls[slot] is Stored stored)
			{
				individual.Life = stored.Life;
				individual.Damage = stored.Damage;
				individual.Defense = stored.Defense;
				individual.AdaptiveLife = stored.AdaptiveLife;
				individual.AdaptiveDamage = stored.AdaptiveDamage;
				// The new form may not be resizable (worm, boss minion)
				individual.Size = Scalable(npc) ? stored.Size : 1;
				individual.Kind = stored.Kind;
				individual.RollId = stored.Id;
				individual.rolled = true;
				individual.applied = false;
				individual.Apply(npc);
				npc.netUpdate = true;
			}
			else RollAndApply(npc);
			if (ratio < 1) npc.life = Math.Clamp((int)(npc.lifeMax * ratio), 1, npc.lifeMax);
		}

		// End of the server's NPC update: the health ratio of each slot, restored if a SetDefaults resets the NPC
		public static void SnapshotRatios()
		{
			if (Main.netMode == NetmodeID.MultiplayerClient) return;
			for (int i = 0; i < Main.maxNPCs; i++) SnapshotRatio(Main.npc[i]);
		}

		public static void SnapshotRatio(NPC npc)
		{
			if (npc.whoAmI < 0 || npc.whoAmI >= slotRatios.Length) return;
			slotRatios[npc.whoAmI] = npc.active && npc.lifeMax > 0 ? Math.Clamp(npc.life / (float)npc.lifeMax, 0, 1) : -1;
		}

		public static bool Eligible(NPC npc) => Rollable(npc) && !BossParts.IsPart(npc.type);

		// Gets a roll at all: boss parts only get the adaptive multipliers (no variation of their own)
		public static bool Rollable(NPC npc) =>
			SeedSystem.WorldReady && !npc.townNPC && !npc.friendly && !NPCID.Sets.CountsAsCritter[npc.type] && !NPCID.Sets.ProjectileNPC[npc.type]
			&& npc.lifeMax > 5;

		// Worms (segments would come apart) and boss parts keep their size; boss minions are never giants or minis
		// (swarms of giant creepers or bees, and no giant loot from them)
		public static bool Scalable(NPC npc) => Eligible(npc) && npc.aiStyle != NPCAIStyleID.Worm && !BossParts.IsMinion(npc.type);

		// Most the individual damage factor may be on top of the type's: together they never exceed MaxDamageMultiplier
		public static float DamageCap(NPC npc)
		{
			float type = EnemyStatCache.Get(BossParts.Owner(npc.type), EnemyStatsNPC.IsBossLike(npc)).Damage;
			return Math.Max(1, EnemiesConfig.Instance.MaxDamageMultiplier) / Math.Max(0.0001f, type);
		}

		public void Roll(NPC npc, Random rng)
		{
			Life = Damage = Defense = Size = 1;
			Kind = EnemyKind.Normal;
			rolled = true;
			applied = false;
			RollId = NextRollId();
			// Before the Eligible check: boss parts scale too. Uses no rng, so the variation rolls stay the same
			(AdaptiveLife, AdaptiveDamage) = AdaptivePower.ForNpc(npc);
			RollVariation(npc, rng);
			Life *= AdaptiveLife;
			// AdaptiveDamage keeps the measured value: boss re-measures and other projectiles compare to it
			Damage = Math.Min(Damage * AdaptiveDamage, DamageCap(npc));
		}

		private void RollVariation(NPC npc, Random rng)
		{
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
				// Damage grows much slower than health: a size 4 giant hits x1.4, not x2
				Damage *= (float)Math.Pow(size, 0.25);
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

		public void Apply(NPC npc) => Apply(npc, false, true);

		// asClient: applied from ReceiveExtraAI on a multiplayer client (explicit so tests can simulate it);
		// serverFull: the NPC was at full health on the server when it was sent
		public void Apply(NPC npc, bool asClient, bool serverFull)
		{
			if (applied) return;
			applied = true;
			baseLifeMax = npc.lifeMax;
			baseDamage = npc.damage;
			baseDefense = npc.defense;
			baseWidth = npc.width;
			baseHeight = npc.height;
			baseScale = npc.scale;
			if (Life != 1)
			{
				// Packet 23 syncs life but not lifeMax: clients scale lifeMax themselves. For a full-health NPC vanilla sets
				// life to the client's unscaled lifeMax, otherwise the synced life is the server's real value and is kept.
				// The server sends its own "full" flag: comparing on the client cannot tell full from damaged to base health
				bool full = !asClient || serverFull;
				npc.lifeMax = Math.Max(1, (int)(npc.lifeMax * Life));
				npc.life = full ? npc.lifeMax : Math.Min(npc.life, npc.lifeMax);
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
			// Clients already got the server's (resized) position before ReceiveExtraAI
			if (!asClient) npc.Bottom = bottom;
		}

		public override void SendExtraAI(NPC npc, BitWriter bitWriter, BinaryWriter binaryWriter)
		{
			bitWriter.WriteBit(rolled);
			if (!rolled) return;
			bitWriter.WriteBit(npc.life >= npc.lifeMax);
			binaryWriter.Write(Life);
			binaryWriter.Write(Damage);
			binaryWriter.Write(Defense);
			binaryWriter.Write(Size);
			binaryWriter.Write((byte)Kind);
			binaryWriter.Write(RollId);
			binaryWriter.Write(AdaptiveLife);
			binaryWriter.Write(AdaptiveDamage);
		}

		// Back to the stats before Apply (a client receiving another roll for the same NPC: the server rolled again after
		// a SetDefaults that kept the netID, so the client's NPC was not reset)
		private void Undo(NPC npc)
		{
			if (!applied) return;
			applied = false;
			Vector2 center = npc.Center;
			npc.lifeMax = baseLifeMax;
			npc.life = Math.Min(npc.life, npc.lifeMax);
			npc.damage = npc.defDamage = baseDamage;
			npc.defense = npc.defDefense = baseDefense;
			npc.width = baseWidth;
			npc.height = baseHeight;
			npc.scale = baseScale;
			npc.Center = center;
		}

		public override void ReceiveExtraAI(NPC npc, BitReader bitReader, BinaryReader binaryReader)
		{
			if (!bitReader.ReadBit()) return;
			bool full = bitReader.ReadBit();
			float life = binaryReader.ReadSingle();
			float damage = binaryReader.ReadSingle();
			float defense = binaryReader.ReadSingle();
			float size = binaryReader.ReadSingle();
			EnemyKind kind = (EnemyKind)binaryReader.ReadByte();
			byte id = binaryReader.ReadByte();
			float adaptiveLife = binaryReader.ReadSingle();
			float adaptiveDamage = binaryReader.ReadSingle();
			if (applied && id == RollId) return;
			// The life packet 23 just synced is the server's real value: Undo must not clamp it to the unscaled lifeMax
			int syncedLife = npc.life;
			Undo(npc);
			npc.life = syncedLife;
			RollId = id;
			Life = life;
			Damage = damage;
			Defense = defense;
			Size = size;
			AdaptiveLife = adaptiveLife;
			AdaptiveDamage = adaptiveDamage;
			Kind = kind;
			rolled = true;
			Apply(npc, true, full);
		}

		// Extra drop chance multiplier of a strong enemy (type and individual health x damage above normal, adaptive power
		// included since it is part of the individual multipliers)
		public static float LootBonus(NPC npc, bool withAdaptive = true)
		{
			EnemiesConfig cfg = EnemiesConfig.Instance;
			EnemyMultipliers type = EnemyStatCache.Get(BossParts.Owner(npc.type), EnemyStatsNPC.IsBossLike(npc));
			float factor = type.Life * type.Damage;
			if (npc.TryGetGlobalNPC(out EnemyIndividualNPC individual))
			{
				factor *= individual.Life * individual.Damage;
				if (!withAdaptive) factor /= Math.Max(0.0001f, individual.AdaptiveLife * individual.AdaptiveDamage);
			}
			return Bonus(factor, cfg.LootBonusStrength, cfg.LootBonusCap);
		}

		public static float Bonus(float factor, float strength, float cap) => factor <= 1 ? 1 : Math.Min(Math.Max(1, cap), 1 + strength * (factor - 1));
	}

	// Boss projectiles get the boss damage multiplier too (contact damage alone would barely matter); the projectiles of
	// other enemies only follow adaptive power (their random stats stay on contact damage)
	public class BossProjectileDamage : GlobalProjectile
	{
		public override void OnSpawn(Projectile projectile, IEntitySource source)
		{
			if (!SeedSystem.WorldReady || !projectile.hostile || source is not EntitySource_Parent parent || parent.Entity is not NPC npc) return;
			// Their damage comes from npc.damage, which already has the multipliers
			if (projectile.type is ProjectileID.Nail or ProjectileID.AncientDoomProjectile) return;
			bool hasIndividual = npc.TryGetGlobalNPC(out EnemyIndividualNPC individual);
			float multiplier;
			if (EnemyStatsNPC.IsBossLike(npc))
			{
				multiplier = EnemyStatCache.Get(BossParts.Owner(npc.type), true).Damage;
				if (hasIndividual) multiplier *= individual.Damage;
			}
			else multiplier = hasIndividual ? Math.Min(individual.AdaptiveDamage, Math.Max(1, EnemiesConfig.Instance.MaxDamageMultiplier)) : 1;
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
		// Boss parts and their owner share a type key but may be asked with different boss flags
		private static readonly Dictionary<(int, bool), EnemyMultipliers> cache = new();

		public static EnemyMultipliers Get(int npcType, bool boss)
		{
			if (cache.TryGetValue((npcType, boss), out EnemyMultipliers m)) return m;
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
			m.Damage = Math.Min(m.Damage, Math.Max(1, cfg.MaxDamageMultiplier));
			cache[(npcType, boss)] = m;
			return m;
		}

		public static void Clear() => cache.Clear();
	}

	public class EnemyStatSystem : ModSystem
	{
		public override void PostSetupRecipes() => SeedSystem.OnSeedChanged += EnemyStatCache.Clear;

		public override void PostUpdateNPCs()
		{
			EnemyIndividualNPC.RollMissing();
			EnemyIndividualNPC.SnapshotRatios();
		}

		public override void OnWorldLoad() => EnemyIndividualNPC.ClearAll();

		public override void OnWorldUnload() => EnemyIndividualNPC.ClearAll();

		public override void Unload() => EnemyStatCache.Clear();
	}
}

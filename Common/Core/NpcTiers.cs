using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using UltimateRandomizer.Common.Configs;

namespace UltimateRandomizer.Common.Core
{
	// How far in the game an enemy is: bosses from a table, other enemies estimated from their base stats
	public static class NpcTiers
	{
		// A boss by its type (net IDs too): its sample is a boss, or it counts as one (Eater of Worlds segments...)
		public static bool IsBoss(int type) =>
			type > 0 && (NPCID.Sets.ShouldBeCountedAsBoss[type] || (ContentSamples.NpcsByNetId.TryGetValue(type, out NPC sample) && sample.boss));

		// A live NPC fighting as a boss
		public static bool IsBoss(NPC npc) => npc.boss || NPCID.Sets.ShouldBeCountedAsBoss[npc.type];

		private static readonly Dictionary<int, Tier> BossTiers = new()
		{
			[NPCID.KingSlime] = Tier.PostEyeOfCthulhu,
			[NPCID.EyeofCthulhu] = Tier.PostEyeOfCthulhu,
			[NPCID.EaterofWorldsHead] = Tier.PostEvilBoss,
			[NPCID.EaterofWorldsBody] = Tier.PostEvilBoss,
			[NPCID.EaterofWorldsTail] = Tier.PostEvilBoss,
			[NPCID.BrainofCthulhu] = Tier.PostEvilBoss,
			[NPCID.QueenBee] = Tier.PostSkeletron,
			[NPCID.SkeletronHead] = Tier.PostSkeletron,
			[NPCID.Deerclops] = Tier.PostSkeletron,
			[NPCID.DD2DarkMageT1] = Tier.PostSkeletron,
			[NPCID.WallofFlesh] = Tier.Hardmode,
			[NPCID.PirateShip] = Tier.Hardmode,
			[NPCID.QueenSlimeBoss] = Tier.PostMechBoss,
			[NPCID.TheDestroyer] = Tier.PostMechBoss,
			[NPCID.Retinazer] = Tier.PostMechBoss,
			[NPCID.Spazmatism] = Tier.PostMechBoss,
			[NPCID.SkeletronPrime] = Tier.PostMechBoss,
			[NPCID.DD2OgreT2] = Tier.PostMechBoss,
			[NPCID.Plantera] = Tier.PostPlantera,
			[NPCID.MourningWood] = Tier.PostPlantera,
			[NPCID.Pumpking] = Tier.PostPlantera,
			[NPCID.Everscream] = Tier.PostPlantera,
			[NPCID.SantaNK1] = Tier.PostPlantera,
			[NPCID.IceQueen] = Tier.PostPlantera,
			[NPCID.Golem] = Tier.PostGolem,
			[NPCID.GolemHead] = Tier.PostGolem,
			[NPCID.DukeFishron] = Tier.PostGolem,
			[NPCID.HallowBoss] = Tier.PostGolem,
			[NPCID.MartianSaucerCore] = Tier.PostGolem,
			[NPCID.DD2Betsy] = Tier.PostGolem,
			[NPCID.CultistBoss] = Tier.PostCultist,
			[NPCID.LunarTowerSolar] = Tier.PostCultist,
			[NPCID.LunarTowerVortex] = Tier.PostCultist,
			[NPCID.LunarTowerNebula] = Tier.PostCultist,
			[NPCID.LunarTowerStardust] = Tier.PostCultist,
			[NPCID.MoonLordCore] = Tier.PostMoonLord,
			[NPCID.MoonLordHead] = Tier.PostMoonLord,
			[NPCID.MoonLordHand] = Tier.PostMoonLord
		};

		private static readonly Dictionary<int, int> cache = new();

		public static IEnumerable<int> BossTypes => BossTiers.Keys;

		// Its tier comes from the boss table (not estimated from its stats)
		public static bool HasBossTier(int type) => BossTiers.ContainsKey(type);

		public static int BaseTier(int npcType)
		{
			if (cache.TryGetValue(npcType, out int tier)) return tier;
			if (BossTiers.TryGetValue(npcType, out Tier boss))
			{
				tier = (int)boss;
			}
			else if (ContentSamples.NpcsByNetId.TryGetValue(npcType, out NPC sample))
			{
				(int life, int damage) = NormalStats(npcType);
				tier = Math.Max(LifeTier(life), DamageTier(damage));
				if (sample.boss) tier = Math.Max(tier, (int)Tier.PostEyeOfCthulhu);
			}
			else
			{
				tier = 0;
			}
			tier = Tiers.Clamp(tier);
			cache[npcType] = tier;
			return tier;
		}

		// The tier used for rolls: weak enemies stay weak, unless the option makes them follow progression
		public static int EffectiveTier(int npcType)
		{
			int tier = BaseTier(npcType);
			if (ProgressionConfig.Instance.ScaleWeakEnemiesWithProgression)
			{
				tier = Math.Max(tier, ProgressionSystem.UnlockedTier - 1);
			}
			return Tiers.Clamp(tier);
		}

		// Set while NormalStats builds its NPC: EnemyStatsNPC leaves the world's multipliers out
		public static bool Sampling;

		private static readonly FieldInfo ExpertOverride = typeof(Main).GetField("_overrideForExpertMode", BindingFlags.NonPublic | BindingFlags.Static);
		private static readonly FieldInfo MasterOverride = typeof(Main).GetField("_overrideForMasterMode", BindingFlags.NonPublic | BindingFlags.Static);

		// Base life and damage of an NPC in a plain normal-mode world. ContentSamples are scaled by the game mode (and secret
		// seeds) of the last world played before the mods were (re)loaded: after an expert world, a reload made every enemy
		// look stronger, which changed the tiers, the enemy swaps and the drops of a world from one session to the next
		public static (int life, int damage) NormalStats(int netId)
		{
			bool goodWorld = Main.getGoodWorld, remix = Main.remixWorld, zenith = Main.zenithWorld, tenth = Main.tenthAnniversaryWorld, hardMode = Main.hardMode;
			object expert = ExpertOverride?.GetValue(null), master = MasterOverride?.GetValue(null);
			try
			{
				Main.getGoodWorld = Main.remixWorld = Main.zenithWorld = Main.tenthAnniversaryWorld = Main.hardMode = false;
				ExpertOverride?.SetValue(null, (bool?)false);
				MasterOverride?.SetValue(null, (bool?)false);
				Sampling = true;
				NPC npc = new();
				npc.SetDefaults(netId, new NPCSpawnParams { gameModeData = GameModeData.NormalMode, strengthMultiplierOverride = 1f, playerCountForMultiplayerDifficultyOverride = 1 });
				return (npc.lifeMax, npc.damage);
			}
			finally
			{
				Sampling = false;
				Main.getGoodWorld = goodWorld;
				Main.remixWorld = remix;
				Main.zenithWorld = zenith;
				Main.tenthAnniversaryWorld = tenth;
				Main.hardMode = hardMode;
				ExpertOverride?.SetValue(null, expert);
				MasterOverride?.SetValue(null, master);
			}
		}

		private static int LifeTier(int life) => life switch
		{
			<= 60 => 0,
			<= 120 => 1,
			<= 200 => 2,
			<= 350 => 3,
			<= 600 => 4,
			<= 1000 => 5,
			<= 2500 => 6,
			<= 5000 => 7,
			_ => 8
		};

		private static int DamageTier(int damage) => damage switch
		{
			<= 14 => 0,
			<= 20 => 1,
			<= 28 => 2,
			<= 38 => 3,
			<= 55 => 4,
			<= 70 => 5,
			<= 90 => 6,
			<= 110 => 7,
			_ => 8
		};

		public static void Unload() => cache.Clear();
	}
}

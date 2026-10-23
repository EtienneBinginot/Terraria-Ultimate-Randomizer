using System;
using System.Collections.Generic;
using Terraria;
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

		public static int BaseTier(int npcType)
		{
			if (cache.TryGetValue(npcType, out int tier)) return tier;
			if (BossTiers.TryGetValue(npcType, out Tier boss))
			{
				tier = (int)boss;
			}
			else if (ContentSamples.NpcsByNetId.TryGetValue(npcType, out NPC sample))
			{
				tier = Math.Max(LifeTier(sample.lifeMax), DamageTier(sample.damage));
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

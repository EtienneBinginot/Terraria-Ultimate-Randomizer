using System;
using Terraria;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;

namespace UltimateRandomizer.Common.Core
{
	public enum Tier
	{
		PreBoss,
		PostEyeOfCthulhu,
		PostEvilBoss,
		PostSkeletron,
		Hardmode,
		PostMechBoss,
		PostPlantera,
		PostGolem,
		PostCultist,
		PostMoonLord
	}

	public static class Tiers
	{
		public const int Count = 10;
		public const int Max = Count - 1;

		public static int Clamp(int tier) => Math.Clamp(tier, 0, Max);
	}

	// Tracks the highest tier unlocked by the bosses defeated in this world (flags already synced by vanilla)
	public class ProgressionSystem : ModSystem
	{
		public static int UnlockedTier { get; private set; }

		public static event Action OnTierChanged;

		private int timer;

		public static int ComputeUnlockedTier()
		{
			bool mechTier = ProgressionConfig.Instance.MechTierRequiresAllThree
				? NPC.downedMechBoss1 && NPC.downedMechBoss2 && NPC.downedMechBoss3
				: NPC.downedMechBossAny;
			if (NPC.downedMoonlord) return (int)Tier.PostMoonLord;
			if (NPC.downedAncientCultist) return (int)Tier.PostCultist;
			if (NPC.downedGolemBoss) return (int)Tier.PostGolem;
			if (NPC.downedPlantBoss) return (int)Tier.PostPlantera;
			if (mechTier) return (int)Tier.PostMechBoss;
			if (Main.hardMode) return (int)Tier.Hardmode;
			if (NPC.downedBoss3) return (int)Tier.PostSkeletron;
			if (NPC.downedBoss2) return (int)Tier.PostEvilBoss;
			if (NPC.downedBoss1) return (int)Tier.PostEyeOfCthulhu;
			return (int)Tier.PreBoss;
		}

		public static void Refresh()
		{
			int tier = ComputeUnlockedTier();
			if (tier != UnlockedTier)
			{
				UnlockedTier = tier;
				OnTierChanged?.Invoke();
			}
		}

		public override void OnWorldLoad() => UnlockedTier = ComputeUnlockedTier();

		public override void PostUpdateEverything()
		{
			if (!SeedSystem.WorldReady || ++timer < 60) return;
			timer = 0;
			Refresh();
		}

		public override void OnWorldUnload() => UnlockedTier = 0;

		public override void Unload() => OnTierChanged = null;
	}
}

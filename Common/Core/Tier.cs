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

		// The difficulty the planners read (Main.expertMode, masterMode): Journey's slider changes it in a world in progress
		private static (int mode, bool expert, bool master) difficulty;

		private static (int, bool, bool) Difficulty() => (Main.GameMode, Main.expertMode, Main.masterMode);

		public static void Refresh(bool notify = true)
		{
			// Every machine plans by itself (clients get the game mode and the slider synced): a change rebuilds the world
			// caches; the world's own difficulty, read as it activates (notify false), is no change
			(int, bool, bool) now = Difficulty();
			if (now != difficulty)
			{
				difficulty = now;
				if (notify && SeedSystem.WorldReady) SeedSystem.NotifyConfigChanged();
			}
			int tier = ComputeUnlockedTier();
			if (tier != UnlockedTier)
			{
				UnlockedTier = tier;
				if (notify && SeedSystem.WorldReady) OnTierChanged?.Invoke();
			}
		}

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

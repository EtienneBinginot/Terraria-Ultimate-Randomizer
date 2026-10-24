using System;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Creative;
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

		// The difficulty the world caches are planned with (PlannedExpert, PlannedMaster): Journey's slider changes it in a
		// world in progress. Read by the planners instead of Main.expertMode, which only applies the slider once a frame has run
		// outside the menu (Main.UpdateCreativeGameModeOverride: not yet during PostWorldLoad or a client's WorldData)
		private static (int mode, bool expert, bool master) difficulty;
		// A different difficulty seen since that tick: committed (caches rebuilt) once it stayed the same for StableTicks, so
		// dragging the slider across the thresholds rebuilds once
		private static (int mode, bool expert, bool master)? pendingDifficulty;
		private static uint pendingSince;
		private const uint StableTicks = 300;

		public static bool PlannedExpert => difficulty.expert;
		public static bool PlannedMaster => difficulty.master;

		// No difficulty change waiting to be committed
		public static bool DifficultySettled => pendingDifficulty == null;

		// Main.expertMode and Main.masterMode as they are in game, the Journey override computed here (same IL as
		// Main.UpdateCreativeGameModeOverride, then get_expertMode / get_masterMode)
		public static (int mode, bool expert, bool master) Difficulty()
		{
			if (Main.ActiveWorldFileData == null) return (Main.GameMode, false, false);
			GameModeData info = Main.GameModeInfo;
			bool? overrideExpert = null;
			bool? overrideMaster = null;
			if (info.IsJourneyMode)
			{
				CreativePowers.DifficultySliderPower slider = CreativePowerManager.Instance.GetPower<CreativePowers.DifficultySliderPower>();
				if (slider != null && slider.GetIsUnlocked())
				{
					float strength = slider.StrengthMultiplierToGiveNPCs;
					if (Main.getGoodWorld) strength += 1f;
					if (strength >= 2f) overrideExpert = true;
					if (strength >= 3f) overrideMaster = true;
				}
			}
			bool expert = overrideExpert ?? (Main.getGoodWorld || info.IsExpertMode);
			bool master = overrideMaster ?? ((Main.getGoodWorld && info.IsExpertMode) || info.IsMasterMode);
			return (Main.GameMode, expert, master);
		}

		public static void Refresh(bool notify = true)
		{
			// Every machine plans by itself (clients get the game mode and the slider synced): a lasting change rebuilds the
			// world caches; the world's own difficulty, read as it activates (notify false), is no change
			(int, bool, bool) now = Difficulty();
			if (!notify || !SeedSystem.WorldReady)
			{
				difficulty = now;
				pendingDifficulty = null;
			}
			else if (now == difficulty) pendingDifficulty = null;
			else if (pendingDifficulty != now)
			{
				pendingDifficulty = now;
				pendingSince = Main.GameUpdateCount;
			}
			else if (Main.GameUpdateCount - pendingSince >= StableTicks)
			{
				difficulty = now;
				pendingDifficulty = null;
				if (!SeedSystem.NotifyConfigChanged()) SeedSystem.RequestCacheReset();
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

		public override void OnWorldUnload()
		{
			UnlockedTier = 0;
			pendingDifficulty = null;
		}

		public override void Unload() => OnTierChanged = null;
	}
}

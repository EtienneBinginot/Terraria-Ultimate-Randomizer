using System;
using System.Reflection;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace UltimateRandomizer.Common.Features
{
	// A swapped event spawn still counts for its event: invasions have no timeout (invaders turned into other enemies would
	// keep one going forever, saved with the world) and the moons' waves need their points. When an NPC dies, vanilla credits
	// its current type (NPC.checkDead: invasion slots, moon points); if it spawned as an invader or a moon enemy of the running
	// event and its current type doesn't count, its original type is credited exactly as vanilla would.
	// Server and single player only: vanilla sends the progress to the clients (message 78), as for its own kills.
	public class EventProgress : ModSystem
	{
		private static MethodInfo pumpkinProgress;
		private static MethodInfo frostProgress;
		// Stands for the original type in the moon checks (they only read the type and the game mode)
		private static NPC probe;

		public override void Load()
		{
			pumpkinProgress = typeof(NPC).GetMethod("CheckProgressPumpkinMoon", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
			frostProgress = typeof(NPC).GetMethod("CheckProgressFrostMoon", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
			if (pumpkinProgress == null || frostProgress == null) Mod.Logger.Warn("EventProgress: the moon progress methods were not found, swapped moon enemies won't count");
			// Every death goes through checkDead (weapons, traps, lava, town NPCs), not only the loot of player kills
			On_NPC.checkDead += AfterCheckDead;
		}

		public override void Unload()
		{
			pumpkinProgress = null;
			frostProgress = null;
			probe = null;
		}

		private static void AfterCheckDead(On_NPC.orig_checkDead orig, NPC self)
		{
			bool wasActive = self.active;
			orig(self);
			if (!wasActive || self.active || Main.netMode == NetmodeID.MultiplayerClient) return;
			int slot = self.whoAmI;
			if (slot < 0 || slot >= Main.maxNPCs || Main.npc[slot] != self) return;
			int original = EnemySwapSystem.Originals[slot];
			if (original <= 0) return;
			// Once per death
			EnemySwapSystem.Originals[slot] = 0;
			Credit(self, original);
		}

		// Credits a dead swapped NPC's original type to the running invasion or moon, unless its current type already counted
		// there. Returns true when something was credited (the tests call it on a fake death)
		public static bool Credit(NPC npc, int original)
		{
			if (original <= 0 || original == npc.type) return false;
			int group = EnemySwapPool.InvasionGroup(original);
			int current = EnemySwapPool.InvasionGroup(npc.type);
			if (group == 0 || group == current) return false;
			if (group > 0) return group == Main.invasionType && CreditInvasion(original, group);
			if (group == EnemySwapPool.PumpkinMoonGroup && Main.pumpkinMoon) return CreditMoon(pumpkinProgress, original);
			if (group == EnemySwapPool.FrostMoonGroup && Main.snowMoon) return CreditMoon(frostProgress, original);
			return false;
		}

		// As the end of NPC.checkDead: the invasion loses the type's slots, the progress bar is updated and sent
		private static bool CreditInvasion(int original, int group)
		{
			int slots = NPCID.Sets.InvasionSlotCount[original];
			if (slots <= 0) return false;
			Main.invasionSize = Math.Max(0, Main.invasionSize - slots);
			if (Main.netMode != NetmodeID.MultiplayerClient) Main.ReportInvasionProgress(Main.invasionSizeStart - Main.invasionSize, Main.invasionSizeStart, group + 3, 0);
			if (Main.netMode == NetmodeID.Server) NetMessage.SendData(MessageID.InvasionProgressReport, -1, -1, null, Main.invasionProgress, Main.invasionProgressMax, Main.invasionProgressIcon, 0);
			return true;
		}

		// Vanilla's own moon check on a probe of the original type: points by type and game mode, wave advance, wave text,
		// progress report and message 78
		private static bool CreditMoon(MethodInfo progress, int original)
		{
			if (progress == null) return false;
			probe ??= new NPC();
			probe.type = original;
			float before = NPC.totalInvasionPoints;
			progress.Invoke(probe, null);
			return NPC.totalInvasionPoints != before;
		}
	}
}

using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Events;
using Terraria.ID;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Tests
{
	public static class EnemyTests
	{
		public static void Register()
		{
			TestRunner.Register("SwapSurvivesVariantSetDefaults", SwapSurvivesVariantSetDefaults);
			TestRunner.Register("CritterSwapLive", CritterSwapLive);
			TestRunner.Register("CritterPoolSafe", CritterPoolSafe);
			TestRunner.Register("CritterHabitat", CritterHabitat);
			TestRunner.Register("SwapStatsCount", SwapStatsCount);
		}

		// Spawns an NPC as a natural spawn would, off screen next to the player; the caller removes it
		private static NPC SpawnNatural(int type, IEntitySource source = null)
		{
			Player player = Main.LocalPlayer;
			int index = NPC.NewNPC(source ?? new EntitySource_SpawnNPC(), (int)player.Center.X + 1200, (int)player.Center.Y - 200, type);
			return index >= 0 && index < Main.maxNPCs ? Main.npc[index] : null;
		}

		private static void Remove(NPC npc)
		{
			if (npc == null) return;
			EnemySwapSystem.Pending[npc.whoAmI] = false;
			npc.active = false;
		}

		private static void RequireLiveSpawns()
		{
			if (Main.netMode != NetmodeID.SinglePlayer) Check.Skip("single player only");
			if (!EnemiesConfig.Instance.RandomizeEnemySpawns) Check.Skip("enemy spawns are not randomized");
			// Same conditions as EnemySwapNPC.EventInProgress: events keep their own spawns
			if (Main.pumpkinMoon || Main.snowMoon || Main.invasionType > 0 || DD2Event.Ongoing) Check.Skip("an event is running");
			Player player = Main.LocalPlayer;
			if (player.ZoneTowerSolar || player.ZoneTowerVortex || player.ZoneTowerNebula || player.ZoneTowerStardust) Check.Skip("near a celestial pillar");
		}

		// Vanilla sets the variant (Pinky, zombie variants...) with SetDefaults right after spawning: the swap must survive it
		private static void SwapSurvivesVariantSetDefaults()
		{
			RequireLiveSpawns();
			bool swapped = false;
			for (int attempt = 0; attempt < 10 && !swapped; attempt++)
			{
				NPC npc = SpawnNatural(NPCID.BlueSlime);
				Check.That(npc != null, "could not spawn a slime");
				try
				{
					npc.SetDefaults(NPCID.Pinky);
					Check.That(EnemySwapSystem.Pending[npc.whoAmI], "the pending swap was lost by SetDefaults");
					EnemySwapSystem.ProcessSlot(npc.whoAmI, 100);
					swapped = npc.netID != NPCID.Pinky;
				}
				finally
				{
					Remove(npc);
				}
			}
			Check.That(swapped, "the slime was never swapped in 10 tries");
		}

		private static void CritterSwapLive()
		{
			RequireLiveSpawns();
			if (!EnemiesConfig.Instance.SwapCritters) Check.Skip("critters are not swapped");
			List<string> failures = new();
			int changed = 0;
			for (int i = 0; i < 20; i++)
			{
				NPC npc = SpawnNatural(NPCID.Bunny);
				Check.That(npc != null, "could not spawn a bunny");
				try
				{
					string habitat = EnemySwapPool.HabitatOf(npc.type);
					EnemySwapSystem.ProcessSlot(npc.whoAmI, 100);
					if (npc.type == NPCID.Bunny) continue;
					changed++;
					if (!NPCID.Sets.CountsAsCritter[npc.type]) failures.Add($"bunny became {npc.TypeName}, not a critter");
					else if (EnemySwapPool.HabitatOf(npc.type) != habitat) failures.Add($"bunny became {npc.TypeName} ({EnemySwapPool.HabitatOf(npc.type)})");
				}
				finally
				{
					Remove(npc);
				}
			}
			Check.That(changed >= 10, $"only {changed}/20 bunnies were swapped");
			Check.All(failures, "Wrong critter swaps");
		}

		private static void CritterPoolSafe()
		{
			int[] forbidden = { NPCID.EmpressButterfly, NPCID.TruffleWorm, NPCID.TruffleWormDigger, NPCID.Lavafly, NPCID.MagmaSnail, NPCID.HellButterfly, NPCID.GemBunnyDiamond };
			List<string> failures = new();
			foreach (int type in EnemySwapPool.Targets())
			{
				if (forbidden.Contains(type) || NPCID.Sets.GoldCrittersCollection.Contains(type)) failures.Add(ContentSamples.NpcsByNetId[type].TypeName);
			}
			Check.All(failures, "Unsafe critters can appear");
		}

		private static void CritterHabitat()
		{
			List<string> failures = new();
			foreach (int type in EnemySwapPool.Targets())
			{
				string habitat = EnemySwapPool.HabitatOf(type);
				if (habitat == null || !habitat.StartsWith("Critter")) continue;
				NPC sample = ContentSamples.NpcsByNetId[type];
				bool water = sample.aiStyle == NPCAIStyleID.Piranha;
				if (water != (habitat == "CritterWater")) failures.Add($"{sample.TypeName} in {habitat}");
				if (!water && sample.noGravity != (habitat == "CritterFlying")) failures.Add($"{sample.TypeName} in {habitat}");
			}
			Check.All(failures, "Critters in the wrong habitat");
		}

		private static void SwapStatsCount()
		{
			RequireLiveSpawns();
			int before = EnemySwapSystem.Stats.Flagged;
			NPC npc = SpawnNatural(NPCID.Zombie);
			Remove(npc);
			Check.That(EnemySwapSystem.Stats.Flagged == before + 1, "the spawn was not counted");
			NPC statue = SpawnNatural(NPCID.Zombie, new EntitySource_Wiring(0, 0));
			Remove(statue);
			Check.That(EnemySwapSystem.Stats.Flagged == before + 1, "a statue spawn was flagged for a swap");
		}
	}
}

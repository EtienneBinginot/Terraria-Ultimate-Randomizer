using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Events;
using Terraria.ID;
using Terraria.ModLoader;
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
			TestRunner.Register("NpcTiersNormalStats", NpcTiersNormalStats);
		}

		// Enemy tiers come from the normal-mode stats, whatever world was played before the mods were loaded (ContentSamples
		// are scaled by that world's game mode: an expert world made every enemy a tier or two stronger after a reload, and
		// changed the swaps and drops of the next world). The sampling leaves the world's flags as they were
		private static void NpcTiersNormalStats()
		{
			List<string> failures = new();
			bool expert = Main.expertMode, master = Main.masterMode, goodWorld = Main.getGoodWorld, hardMode = Main.hardMode;
			foreach ((int id, int life, int damage, int tier) in new[] { (NPCID.Zombie, 45, 14, 0), (NPCID.DemonEye, 60, 18, 1), (NPCID.BlueSlime, 25, 7, 0), (NPCID.Pinky, 150, 5, 2) })
			{
				(int l, int d) = Core.NpcTiers.NormalStats(id);
				if (l != life || d != damage) failures.Add($"{Lang.GetNPCNameValue(id)}: {l} life, {d} damage, expected {life} and {damage}");
				if (Core.NpcTiers.BaseTier(id) != tier) failures.Add($"{Lang.GetNPCNameValue(id)}: tier {Core.NpcTiers.BaseTier(id)}, expected {tier}");
				NPC sample = ContentSamples.NpcsByNetId[id];
				if (sample.lifeMax != life) ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"NpcTiersNormalStats: the {Lang.GetNPCNameValue(id)} sample has {sample.lifeMax} life (scaled by the game mode of the world before the last reload)");
			}
			if (Main.expertMode != expert || Main.masterMode != master || Main.getGoodWorld != goodWorld || Main.hardMode != hardMode) failures.Add("the sampling changed the world's flags");
			if (Core.NpcTiers.Sampling) failures.Add("still sampling");
			Check.All(failures, "Enemy tiers not from the normal-mode stats");
		}

		private static bool BiomeMode => EnemiesConfig.Instance.EnemySwapMode == EnemySwapMode.ByBiome;

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
				NPC npc = TestRunner.SpawnNear(NPCID.BlueSlime);
				Check.That(npc != null, "could not spawn a slime");
				try
				{
					npc.SetDefaults(NPCID.Pinky);
					Check.That(EnemySwapSystem.Pending[npc.whoAmI], "the pending swap was lost by SetDefaults");
					// By biome, Pinky always becomes the owner of its slot, which may be Pinky itself
					if (BiomeMode && EnemySwapPool.BiomeTarget(npc) == NPCID.Pinky) Check.Skip("Pinky keeps its own slot in this world");
					EnemySwapSystem.ProcessSlot(npc.whoAmI, 100);
					swapped = npc.netID != NPCID.Pinky;
				}
				finally
				{
					TestRunner.RemoveNpc(npc);
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
				NPC npc = TestRunner.SpawnNear(NPCID.Bunny);
				Check.That(npc != null, "could not spawn a bunny");
				try
				{
					string habitat = EnemySwapPool.HabitatOf(npc.type);
					if (BiomeMode && EnemySwapPool.BiomeTarget(npc) == NPCID.Bunny) Check.Skip("the bunny keeps its own slot here in this world");
					EnemySwapSystem.ProcessSlot(npc.whoAmI, 100);
					if (npc.type == NPCID.Bunny) continue;
					changed++;
					if (!NPCID.Sets.CountsAsCritter[npc.type]) failures.Add($"bunny became {npc.TypeName}, not a critter");
					else if (EnemySwapPool.HabitatOf(npc.type) != habitat) failures.Add($"bunny became {npc.TypeName} ({EnemySwapPool.HabitatOf(npc.type)})");
				}
				finally
				{
					TestRunner.RemoveNpc(npc);
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
			NPC npc = TestRunner.SpawnNear(NPCID.Zombie);
			TestRunner.RemoveNpc(npc);
			Check.That(EnemySwapSystem.Stats.Flagged == before + 1, "the spawn was not counted");
			NPC statue = TestRunner.SpawnNear(NPCID.Zombie, new EntitySource_Wiring(0, 0));
			TestRunner.RemoveNpc(statue);
			Check.That(EnemySwapSystem.Stats.Flagged == before + 1, "a statue spawn was flagged for a swap");
		}
	}
}

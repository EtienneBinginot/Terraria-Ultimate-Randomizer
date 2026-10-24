using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Events;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;
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
			TestRunner.Register("EventEnemiesJoinPool", EventEnemiesJoinPool);
			TestRunner.Register("EventEnemiesNeverSwapped", EventEnemiesNeverSwapped);
			TestRunner.Register("SwapKeepsOriginal", SwapKeepsOriginal);
			TestRunner.Register("EventCreditInvasion", EventCreditInvasion);
			TestRunner.Register("EventCreditMoon", EventCreditMoon);
		}

		// Invaders, moon and eclipse enemies are in the swap pool and may take biome slots, never before their event can come:
		// not in a slot below their event's tier, not met earlier in any map, and an event spawn only becomes an enemy met by then
		private static void EventEnemiesJoinPool()
		{
			if (!EnemiesConfig.Instance.SwapEventEnemies) Check.Skip("event enemies are not swapped");
			List<string> failures = new();
			(int type, Tier tier)[] expected =
			{
				(NPCID.GoblinPeon, Tier.PostEyeOfCthulhu), (NPCID.GoblinArcher, Tier.PostEyeOfCthulhu), (NPCID.PirateDeckhand, Tier.Hardmode),
				(NPCID.SnowmanGangsta, Tier.Hardmode), (NPCID.Eyezor, Tier.PostMechBoss), (NPCID.Scarecrow1, Tier.PostPlantera), (NPCID.Splinterling, Tier.PostPlantera),
				(NPCID.ZombieElf, Tier.PostPlantera), (NPCID.Yeti, Tier.PostPlantera), (NPCID.ElfCopter, Tier.PostPlantera)
			};
			foreach ((int type, Tier tier) in expected)
			{
				string name = Lang.GetNPCNameValue(type);
				if (!EnemySwapPool.IsSwappable(type)) failures.Add($"{name} is not in the swap pool");
				if (!BiomeSlots.IsExtraCandidate(type)) failures.Add($"{name} can't take a biome slot");
				if (EnemySwapPool.EventTier(type) < (int)tier) failures.Add($"{name}: event tier {EnemySwapPool.EventTier(type)}, at least {(int)tier} expected");
				if (EnemySwapPool.PoolTier(type) < (int)tier) failures.Add($"{name}: pool tier {EnemySwapPool.PoolTier(type)}, at least {(int)tier} expected");
			}
			int eventExtras = 0;
			for (int i = 0; i < 10; i++)
			{
				int seed = 1000 + i * 7919;
				BiomeSlotMap map = BiomeSlots.Build(seed, 8f);
				foreach (BiomeSlot slot in map.Slots.Where(s => s.Extra > 0 && EnemySwapPool.IsEventEnemy(s.Extra)))
				{
					if (EnemySwapPool.InvasionGroup(slot.Extra) != 0) eventExtras++;
					if (slot.Tier < EnemySwapPool.EventTier(slot.Extra)) failures.Add($"seed {seed}: {Lang.GetNPCNameValue(slot.Extra)} in a tier {slot.Tier} slot, its event comes at tier {EnemySwapPool.EventTier(slot.Extra)}");
				}
				foreach ((int type, int from) in map.SpawnTier)
				{
					if (EnemySwapPool.IsEventEnemy(type) && from < EnemySwapPool.EventTier(type)) failures.Add($"seed {seed}: {Lang.GetNPCNameValue(type)} met from tier {from}, its event comes at tier {EnemySwapPool.EventTier(type)}");
				}
				foreach (int source in new[] { NPCID.GoblinPeon, NPCID.PirateDeckhand, NPCID.Scarecrow1 })
				{
					foreach (bool day in new[] { false, true })
					{
						List<int> targets = EnemySwapPool.EventTargets(map, source, day);
						if (!day && targets.Count == 0) failures.Add($"seed {seed}: no enemy can replace a {Lang.GetNPCNameValue(source)} spawn");
						foreach (int target in targets)
						{
							if (map.SpawnTier[target] > EnemySwapPool.PoolTier(source)) failures.Add($"seed {seed}: a {Lang.GetNPCNameValue(source)} spawn may become {Lang.GetNPCNameValue(target)}, met from tier {map.SpawnTier[target]}");
							if (day && EnemySwapPool.DaylightIntolerant(target)) failures.Add($"seed {seed}: a {Lang.GetNPCNameValue(source)} spawn may become {Lang.GetNPCNameValue(target)} in daylight");
							if (EnemySwapPool.NeverSwapped(target) || NPCID.Sets.CountsAsCritter[target]) failures.Add($"seed {seed}: a {Lang.GetNPCNameValue(source)} spawn may become {Lang.GetNPCNameValue(target)}");
						}
					}
				}
			}
			Check.That(eventExtras > 0, "no invader or moon enemy took a biome slot in 10 seeds");
			Check.All(failures, "Event enemies in the swaps");
		}

		// Martians, the Old One's Army, event bosses and event-bound AIs never take another enemy's place nor get replaced, and
		// their events keep their spawns
		private static void EventEnemiesNeverSwapped()
		{
			HashSet<int> never = new()
			{
				NPCID.MartianWalker, NPCID.BrainScrambler, NPCID.GigaZapper, NPCID.RayGunner, NPCID.DD2GoblinT1, NPCID.DD2WyvernT1, NPCID.Pumpking, NPCID.MourningWood,
				NPCID.IceQueen, NPCID.Everscream, NPCID.SantaNK1, NPCID.PirateShip, NPCID.Mothron, NPCID.Poltergeist, NPCID.PresentMimic
			};
			for (int type = 1; type < NPCLoader.NPCCount; type++)
			{
				if (NPCID.Sets.BelongsToInvasionMartianMadness[type] || NPCID.Sets.BelongsToInvasionOldOnesArmy[type]) never.Add(type);
			}
			List<string> failures = new();
			foreach (int type in never)
			{
				if (EnemySwapPool.IsSwappable(type)) failures.Add($"{Lang.GetNPCNameValue(type)} is in the swap pool");
				if (BiomeSlots.IsExtraCandidate(type)) failures.Add($"{Lang.GetNPCNameValue(type)} can take a biome slot");
				if (EnemySwapPool.EventSpawnSwappable(type)) failures.Add($"{Lang.GetNPCNameValue(type)} spawns can be replaced");
			}
			for (int i = 0; i < 5; i++)
			{
				BiomeSlotMap map = BiomeSlots.Build(1000 + i * 7919, 30f);
				foreach (BiomeSlot slot in map.Slots.Where(s => never.Contains(BiomeSlots.TypeOf(s.Owner)) || (s.Extra != 0 && never.Contains(BiomeSlots.TypeOf(s.Extra)))))
				{
					failures.Add($"{Lang.GetNPCNameValue(slot.Extra != 0 ? slot.Extra : slot.Owner)} in a {BiomeSlots.BiomeName(slot.Biome)} slot");
				}
			}
			int invasion = Main.invasionType;
			try
			{
				Main.invasionType = InvasionID.MartianMadness;
				if (!EnemySwapNPC.SwapsBlocked(new NPC())) failures.Add("spawns are swapped during Martian Madness");
			}
			finally
			{
				Main.invasionType = invasion;
			}
			Check.All(failures, "Event enemies that must never be swapped");
		}

		// The original type of a swapped NPC is kept by slot: SetDefaults and Transform give it new globals
		private static void SwapKeepsOriginal()
		{
			if (Main.netMode != NetmodeID.SinglePlayer) Check.Skip("single player only");
			// A stale original in every free slot (a previous NPC's): the new NPC's OnSpawn must clear its own
			int[] saved = (int[])EnemySwapSystem.Originals.Clone();
			for (int i = 0; i < Main.maxNPCs; i++)
			{
				if (!Main.npc[i].active) EnemySwapSystem.Originals[i] = NPCID.Skeleton;
			}
			int flagged = EnemySwapSystem.Stats.Flagged, swapped = EnemySwapSystem.Stats.SwappedEnemies;
			NPC npc = null;
			try
			{
				npc = TestRunner.SpawnNear(NPCID.GoblinPeon);
				Check.That(npc != null, "could not spawn a goblin");
				Check.Equal(0, EnemySwapSystem.Originals[npc.whoAmI], "original of a new NPC in a slot with a stale one");
				EnemySwapSystem.ApplySwap(npc, NPCID.Zombie);
				Check.Equal(NPCID.GoblinPeon, EnemySwapSystem.Originals[npc.whoAmI], "original after the swap");
				npc.SetDefaults(NPCID.Zombie);
				npc.Transform(NPCID.BaldZombie);
				Check.Equal(NPCID.GoblinPeon, EnemySwapSystem.Originals[npc.whoAmI], "original after SetDefaults and Transform");
			}
			finally
			{
				TestRunner.RemoveNpc(npc);
				System.Array.Copy(saved, EnemySwapSystem.Originals, saved.Length);
				EnemySwapSystem.Stats.Flagged = flagged;
				EnemySwapSystem.Stats.SwappedEnemies = swapped;
			}
		}

		// Everything a credit may change, restored after the fake deaths
		private sealed class InvasionState
		{
			private readonly int type = Main.invasionType, size = Main.invasionSize, sizeStart = Main.invasionSizeStart;
			private readonly int progress = Main.invasionProgress, progressMax = Main.invasionProgressMax, icon = Main.invasionProgressIcon, wave = Main.invasionProgressWave;
			private readonly int mode = Main.invasionProgressMode, displayLeft = Main.invasionProgressDisplayLeft;
			private readonly bool near = Main.invasionProgressNearInvasion, pumpkin = Main.pumpkinMoon, frost = Main.snowMoon;
			private readonly float alpha = Main.invasionProgressAlpha, waveKills = NPC.waveKills, points = NPC.totalInvasionPoints;
			private readonly int waveNumber = NPC.waveNumber;

			public void Restore()
			{
				Main.invasionType = type;
				Main.invasionSize = size;
				Main.invasionSizeStart = sizeStart;
				Main.invasionProgress = progress;
				Main.invasionProgressMax = progressMax;
				Main.invasionProgressIcon = icon;
				Main.invasionProgressWave = wave;
				Main.invasionProgressMode = mode;
				Main.invasionProgressDisplayLeft = displayLeft;
				Main.invasionProgressNearInvasion = near;
				Main.invasionProgressAlpha = alpha;
				Main.pumpkinMoon = pumpkin;
				Main.snowMoon = frost;
				NPC.waveKills = waveKills;
				NPC.totalInvasionPoints = points;
				NPC.waveNumber = waveNumber;
			}
		}

		private static NPC Dead(int type)
		{
			// Outside the world's slots: no per-slot state is touched
			NPC npc = new() { whoAmI = Main.maxNPCs };
			npc.SetDefaults(type);
			return npc;
		}

		// A goblin turned into a zombie and killed during a goblin invasion takes its slots off the invasion, once; one turned
		// into another goblin was already counted by vanilla, and nothing counts for another invasion
		private static void EventCreditInvasion()
		{
			if (Main.netMode != NetmodeID.SinglePlayer) Check.Skip("single player only");
			InvasionState state = new();
			try
			{
				Main.invasionType = InvasionID.GoblinArmy;
				Main.invasionSizeStart = 80;
				Main.invasionSize = 50;
				int slots = NPCID.Sets.InvasionSlotCount[NPCID.GoblinPeon];
				Check.That(slots > 0, "a goblin takes no invasion slot");
				Check.That(EventProgress.Credit(Dead(NPCID.Zombie), NPCID.GoblinPeon), "a swapped goblin was not credited");
				Check.Equal(50 - slots, Main.invasionSize, "invasion size after a swapped goblin");
				Check.Equal(80 - Main.invasionSize, Main.invasionProgress, "reported invasion progress");
				Check.That(!EventProgress.Credit(Dead(NPCID.GoblinWarrior), NPCID.GoblinPeon), "a goblin turned into another goblin was counted twice");
				Check.That(!EventProgress.Credit(Dead(NPCID.Zombie), NPCID.Zombie), "an unswapped zombie was credited");
				Check.Equal(50 - slots, Main.invasionSize, "invasion size after the vanilla-counted deaths");
				Main.invasionType = InvasionID.PirateInvasion;
				Check.That(!EventProgress.Credit(Dead(NPCID.Zombie), NPCID.GoblinPeon), "a goblin counted for the pirates");
				Check.Equal(50 - slots, Main.invasionSize, "invasion size after a goblin during the pirates");
			}
			finally
			{
				state.Restore();
			}
		}

		// A swapped moon enemy gives its vanilla points (game mode scalar) to the running moon, once; not to the other moon
		private static void EventCreditMoon()
		{
			if (Main.netMode != NetmodeID.SinglePlayer) Check.Skip("single player only");
			float scalar = Main.masterMode ? 2.5f : Main.expertMode ? 2f : 1f;
			// Wave 1 must not end with these few points (no wave text)
			if (NPC.MoonEventRequiredPointsPerWaveLookup[1] <= 3 * scalar) Check.Skip("the first wave ends too soon to test");
			InvasionState state = new();
			try
			{
				Main.invasionType = 0;
				Main.pumpkinMoon = true;
				Main.snowMoon = false;
				NPC.waveNumber = 1;
				NPC.waveKills = 0;
				NPC.totalInvasionPoints = 0;
				Check.That(EventProgress.Credit(Dead(NPCID.Zombie), NPCID.Scarecrow1), "a swapped scarecrow was not credited");
				Check.Equal(scalar, NPC.waveKills, "Pumpkin Moon points of a swapped scarecrow");
				Check.That(!EventProgress.Credit(Dead(NPCID.Splinterling), NPCID.Scarecrow1), "a scarecrow turned into a Splinterling was counted twice");
				Check.That(!EventProgress.Credit(Dead(NPCID.Zombie), NPCID.ZombieElf), "a Frost Moon elf counted for the Pumpkin Moon");
				Check.Equal(scalar, NPC.waveKills, "Pumpkin Moon points after the other deaths");
				Main.pumpkinMoon = false;
				Main.snowMoon = true;
				NPC.waveKills = 0;
				Check.That(EventProgress.Credit(Dead(NPCID.Zombie), NPCID.ZombieElf), "a swapped elf was not credited");
				Check.Equal(scalar, NPC.waveKills, "Frost Moon points of a swapped elf");
				Check.Equal(1, NPC.waveNumber, "wave number");
			}
			finally
			{
				state.Restore();
			}
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
			// Live swap tests run outside events (EnemySwapNPC.SwapsBlocked, event spawns: EventSpawnSwapChance)
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

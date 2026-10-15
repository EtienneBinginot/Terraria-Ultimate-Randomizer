using System;
using System.Collections.Generic;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Events;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	// Naturally spawning enemies are turned into another natural enemy of the same tier that moves the same way.
	// The swap happens on the enemy's first update: vanilla still adjusts the NPC right after it spawns (variants, plant anchors).
	public class EnemySwapNPC : GlobalNPC
	{
		private bool pendingSwap;

		public override bool InstancePerEntity => true;

		public override void OnSpawn(NPC npc, IEntitySource source)
		{
			if (source is not EntitySource_SpawnNPC || Main.netMode == NetmodeID.MultiplayerClient) return;
			if (!EnemiesConfig.Instance.RandomizeEnemySpawns || !SeedSystem.WorldReady || npc.SpawnedFromStatue) return;
			// Events count their own enemies (pillar shields, moon waves, invasions): never touch their spawns
			if (EventInProgress(npc)) return;
			pendingSwap = true;
		}

		public override bool PreAI(NPC npc)
		{
			if (!pendingSwap) return true;
			pendingSwap = false;
			int target = EnemySwapPool.Replacement(npc);
			if (target > 0 && target != npc.type) npc.Transform(target);
			return true;
		}

		private static bool EventInProgress(NPC npc)
		{
			if (Main.pumpkinMoon || Main.snowMoon || Main.invasionType > 0 || DD2Event.Ongoing) return true;
			int closest = npc.FindClosestPlayer();
			if (closest < 0 || closest >= Main.maxPlayers) return false;
			Player player = Main.player[closest];
			return player.ZoneTowerSolar || player.ZoneTowerVortex || player.ZoneTowerNebula || player.ZoneTowerStardust;
		}
	}

	public static class EnemySwapPool
	{
		private enum Movement { Ground, Flying, Water, Critter }

		private const int MovementCount = 4;

		// Enemies that only exist during an event or a boss fight, or that would break things elsewhere
		private static readonly HashSet<int> Excluded = new()
		{
			NPCID.DungeonGuardian, NPCID.MartianProbe, NPCID.TargetDummy, NPCID.CultistDevote, NPCID.CultistArcherBlue,
			// Lunar pillars
			NPCID.SolarSolenian, NPCID.SolarSroller, NPCID.SolarCorite, NPCID.SolarDrakomire, NPCID.SolarDrakomireRider, NPCID.SolarSpearman,
			NPCID.VortexHornet, NPCID.VortexHornetQueen, NPCID.VortexLarva, NPCID.VortexRifleman, NPCID.VortexSoldier,
			NPCID.NebulaBrain, NPCID.NebulaHeadcrab, NPCID.NebulaBeast, NPCID.NebulaSoldier,
			NPCID.StardustCellBig, NPCID.StardustCellSmall, NPCID.StardustJellyfishBig, NPCID.StardustSoldier, NPCID.StardustSpiderBig, NPCID.StardustSpiderSmall,
			// Pumpkin Moon
			NPCID.Scarecrow1, NPCID.Scarecrow2, NPCID.Scarecrow3, NPCID.Scarecrow4, NPCID.Scarecrow5, NPCID.Scarecrow6, NPCID.Scarecrow7,
			NPCID.Scarecrow8, NPCID.Scarecrow9, NPCID.Scarecrow10, NPCID.Splinterling, NPCID.Hellhound, NPCID.Poltergeist, NPCID.HeadlessHorseman,
			// Frost Moon
			NPCID.ZombieElf, NPCID.ZombieElfBeard, NPCID.ZombieElfGirl, NPCID.PresentMimic, NPCID.GingerbreadMan, NPCID.ElfArcher,
			NPCID.Nutcracker, NPCID.NutcrackerSpinning, NPCID.Yeti, NPCID.ElfCopter, NPCID.Krampus, NPCID.Flocko,
			// Solar eclipse
			NPCID.Eyezor, NPCID.Frankenstein, NPCID.SwampThing, NPCID.Vampire, NPCID.VampireBat, NPCID.Butcher, NPCID.CreatureFromTheDeep,
			NPCID.Fritz, NPCID.Nailhead, NPCID.Psycho, NPCID.DeadlySphere, NPCID.DrManFly, NPCID.ThePossessed, NPCID.Reaper,
			NPCID.Mothron, NPCID.MothronSpawn, NPCID.MothronEgg
		};

		// AI styles that depend on a parent, an anchor tile or a boss: plants, boss hands, the Hungry, Creepers...
		private static readonly HashSet<int> ExcludedAiStyles = new() { 12, 13, 28, 29, 47, 55 };

		private static List<int>[,] dayBuckets;
		private static List<int>[,] nightBuckets;
		private static Dictionary<int, Movement> movements;

		public static int Replacement(NPC npc)
		{
			Build();
			int npcType = npc.type;
			if (!movements.TryGetValue(npcType, out Movement movement)) return npcType;
			EnemiesConfig cfg = EnemiesConfig.Instance;
			Random rng;
			if (cfg.EnemySwapMode == EnemySwapMode.PerType)
			{
				rng = DetRandom.Get(RollKey.EnemySwap, DetRandom.NpcKey(npcType));
				if (rng.NextDouble() * 100 >= cfg.EnemySwapChance) return npcType;
			}
			else
			{
				if (Main.rand.NextFloat() * 100 >= cfg.EnemySwapChance) return npcType;
				rng = new Random(Main.rand.Next());
			}
			// On the surface by day, enemies that flee daylight (zombies, demon eyes...) would vanish at once
			bool daylight = Main.dayTime && npc.position.Y < Main.worldSurface * 16;
			List<int>[,] buckets = daylight ? dayBuckets : nightBuckets;
			int tier = NpcTiers.BaseTier(npcType);
			for (int t = tier; t >= 0; t--)
			{
				if (buckets[(int)movement, t].Count > 0) return buckets[(int)movement, t][rng.Next(buckets[(int)movement, t].Count)];
			}
			for (int t = tier + 1; t < Tiers.Count; t++)
			{
				if (buckets[(int)movement, t].Count > 0) return buckets[(int)movement, t][rng.Next(buckets[(int)movement, t].Count)];
			}
			return npcType;
		}

		public static bool IsSwappable(int npcType)
		{
			Build();
			return movements.ContainsKey(npcType);
		}

		private static void Build()
		{
			if (movements != null) return;
			EnemiesConfig cfg = EnemiesConfig.Instance;
			dayBuckets = NewBuckets();
			nightBuckets = NewBuckets();
			movements = new Dictionary<int, Movement>();
			List<int> types = new(ContentSamples.NpcsByNetId.Keys);
			types.Sort();
			foreach (int type in types)
			{
				if (type <= 0 || !ContentSamples.NpcsByNetId.TryGetValue(type, out NPC npc)) continue;
				Movement? movement = Classify(type, npc, cfg);
				if (movement == null) continue;
				movements[type] = movement.Value;
				int tier = NpcTiers.BaseTier(type);
				nightBuckets[(int)movement.Value, tier].Add(type);
				if (!FleesDaylight(npc)) dayBuckets[(int)movement.Value, tier].Add(type);
			}
		}

		private static List<int>[,] NewBuckets()
		{
			List<int>[,] buckets = new List<int>[MovementCount, Tiers.Count];
			for (int m = 0; m < MovementCount; m++)
			{
				for (int t = 0; t < Tiers.Count; t++) buckets[m, t] = new List<int>();
			}
			return buckets;
		}

		private static Movement? Classify(int type, NPC npc, EnemiesConfig cfg)
		{
			if (npc.boss || NPCID.Sets.ShouldBeCountedAsBoss[type] || npc.townNPC || NPCID.Sets.ActsLikeTownNPC[type] || NPCID.Sets.IsTownPet[type]) return null;
			if (Excluded.Contains(type) || ExcludedAiStyles.Contains(npc.aiStyle) || NPCID.Sets.ProjectileNPC[type]) return null;
			if (npc.ModNPC != null && !cfg.SwapModdedEnemies) return null;
			if (IsEventEnemy(type)) return cfg.SwapEventEnemies ? MovementOf(npc) : null;
			if (NPCID.Sets.CountsAsCritter[type]) return cfg.SwapCritters ? Movement.Critter : null;
			if (npc.lifeMax <= 5 || npc.dontTakeDamage || npc.immortal || npc.friendly) return null;
			// Worms and multi-part enemies can't be transformed safely
			if (npc.aiStyle == NPCAIStyleID.Worm) return null;
			// Natural enemies have a banner; boss minions and parts don't
			if (Item.NPCtoBanner(npc.BannerID()) <= 0) return null;
			return MovementOf(npc);
		}

		private static Movement MovementOf(NPC npc)
		{
			if (npc.aiStyle == NPCAIStyleID.Piranha || npc.aiStyle == NPCAIStyleID.Jellyfish) return Movement.Water;
			return npc.noGravity ? Movement.Flying : Movement.Ground;
		}

		// Fighters and flying eyes that vanilla sends away during the day on the surface
		private static bool FleesDaylight(NPC npc) =>
			npc.aiStyle == NPCAIStyleID.DemonEye
			|| (npc.aiStyle == NPCAIStyleID.Fighter && !NPC.DespawnEncouragement_AIStyle3_Fighters_NotDiscouraged(npc.type, npc.position, npc));

		private static bool IsEventEnemy(int type) =>
			NPCID.Sets.BelongsToInvasionOldOnesArmy[type] || NPCID.Sets.BelongsToInvasionGoblinArmy[type] || NPCID.Sets.BelongsToInvasionFrostLegion[type]
			|| NPCID.Sets.BelongsToInvasionPirate[type] || NPCID.Sets.BelongsToInvasionMartianMadness[type];

		public static void Reset()
		{
			dayBuckets = null;
			nightBuckets = null;
			movements = null;
		}
	}

	public class EnemySwapSystem : ModSystem
	{
		public override void OnWorldLoad() => EnemySwapPool.Reset();

		public override void Unload() => EnemySwapPool.Reset();
	}
}

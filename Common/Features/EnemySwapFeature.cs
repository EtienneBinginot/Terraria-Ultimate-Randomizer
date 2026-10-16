using System;
using System.Collections.Generic;
using System.Linq;
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
	// The swap happens after the NPC update loop: vanilla still adjusts the NPC right after it spawns (variants, plant anchors),
	// and changing its type inside a hook would hand the other mods' hooks of that tick a stale NPC.
	// The pending swap is kept by NPC slot, not on the NPC: every SetDefaults (variants, blood moon conversions) recreates its globals.
	public class EnemySwapNPC : GlobalNPC
	{
		public override void OnSpawn(NPC npc, IEntitySource source)
		{
			if (Main.netMode == NetmodeID.MultiplayerClient || npc.whoAmI < 0 || npc.whoAmI >= Main.maxNPCs) return;
			bool swap = ShouldSwap(npc, source);
			EnemySwapSystem.Pending[npc.whoAmI] = swap;
			if (swap) EnemySwapSystem.Stats.Flagged++;
		}

		private static bool ShouldSwap(NPC npc, IEntitySource source)
		{
			if (!EnemiesConfig.Instance.RandomizeEnemySpawns || !SeedSystem.WorldReady || npc.SpawnedFromStatue) return false;
			// Critters also come out of shaken trees
			bool natural = source is EntitySource_SpawnNPC || (source is EntitySource_ShakeTree && NPCID.Sets.CountsAsCritter[npc.type]);
			// Events count their own enemies (pillar shields, moon waves, invasions): never touch their spawns
			return natural && !EventInProgress(npc);
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

	// What the swaps did since the world was loaded, for the debug tools
	public class SwapStats
	{
		public int Flagged;
		public int SwappedEnemies;
		public int SwappedCritters;
		public int NotInPool;
		public int ChanceFailed;
		public int NoTarget;
		// By biome: hardmode slots before hardmode
		public int Inactive;
	}

	public static class EnemySwapPool
	{
		internal enum Movement { Ground, Flying, Water, CritterGround, CritterFlying, CritterWater }

		private const int MovementCount = 6;

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
			NPCID.Mothron, NPCID.MothronSpawn, NPCID.MothronEgg
		};

		// Solar eclipse: not natural, but they work outside the eclipse (extras of the by-biome mode)
		private static readonly HashSet<int> EclipseEnemies = new()
		{
			NPCID.Eyezor, NPCID.Frankenstein, NPCID.SwampThing, NPCID.Vampire, NPCID.VampireBat, NPCID.Butcher, NPCID.CreatureFromTheDeep,
			NPCID.Fritz, NPCID.Nailhead, NPCID.Psycho, NPCID.DeadlySphere, NPCID.DrManFly, NPCID.ThePossessed, NPCID.Reaper
		};

		// Enemies that don't flee daylight but die or are useless in it: the Gnome turns to stone in the sun
		private static readonly HashSet<int> ExcludedFromDaySurface = new() { NPCID.Gnome };

		// Critters that must never take another one's place: worth money (gold), summon a boss (Prismatic Lacewing,
		// Truffle Worm), live in lava, or are rare collectibles (gem critters)
		private static readonly HashSet<int> CritterExcluded = new()
		{
			NPCID.EmpressButterfly, NPCID.TruffleWorm, NPCID.TruffleWormDigger, NPCID.HellButterfly, NPCID.Lavafly, NPCID.MagmaSnail,
			NPCID.GemSquirrelAmethyst, NPCID.GemSquirrelTopaz, NPCID.GemSquirrelSapphire, NPCID.GemSquirrelEmerald, NPCID.GemSquirrelRuby,
			NPCID.GemSquirrelDiamond, NPCID.GemSquirrelAmber, NPCID.GemBunnyAmethyst, NPCID.GemBunnyTopaz, NPCID.GemBunnySapphire,
			NPCID.GemBunnyEmerald, NPCID.GemBunnyRuby, NPCID.GemBunnyDiamond, NPCID.GemBunnyAmber
		};

		// AI styles that depend on a parent, an anchor tile or a boss: plants, boss hands, the Hungry, Creepers...
		private static readonly HashSet<int> ExcludedAiStyles = new() { 12, 13, 28, 29, 47, 55 };

		private static List<int>[,] dayBuckets;
		private static List<int>[,] nightBuckets;
		private static Dictionary<int, Movement> movements;

		// forceChance: swap chance to use instead of the setting (tests); stats: counters to update
		// Returns the NPC's own net ID when it stays as it is
		public static int Replacement(NPC npc, float? forceChance = null, SwapStats stats = null)
		{
			EnemiesConfig cfg = EnemiesConfig.Instance;
			if (cfg.EnemySwapMode == EnemySwapMode.ByBiome) return BiomeReplacement(npc, forceChance, stats);
			Build();
			int npcType = npc.type;
			if (!movements.TryGetValue(npcType, out Movement movement))
			{
				if (stats != null) stats.NotInPool++;
				return npc.netID;
			}
			float chance = forceChance ?? cfg.EnemySwapChance;
			Random rng;
			if (cfg.EnemySwapMode == EnemySwapMode.PerType && forceChance == null)
			{
				rng = DetRandom.Get(RollKey.EnemySwap, DetRandom.NpcKey(npcType));
				if (rng.NextDouble() * 100 >= chance)
				{
					if (stats != null) stats.ChanceFailed++;
					return npc.netID;
				}
			}
			else
			{
				if (Main.rand.NextFloat() * 100 >= chance)
				{
					if (stats != null) stats.ChanceFailed++;
					return npc.netID;
				}
				rng = new Random(Main.rand.Next());
			}
			// On the surface by day, enemies that flee daylight (zombies, demon eyes...) would vanish at once
			bool daylight = Main.dayTime && npc.position.Y < Main.worldSurface * 16;
			List<int>[,] buckets = daylight ? dayBuckets : nightBuckets;
			int tier = NpcTiers.BaseTier(npcType);
			for (int t = tier; t >= 0; t--)
			{
				if (buckets[(int)movement, t].Count > 0) return Own(Pick(buckets[(int)movement, t], rng));
			}
			for (int t = tier + 1; t < Tiers.Count; t++)
			{
				if (buckets[(int)movement, t].Count > 0) return Own(Pick(buckets[(int)movement, t], rng));
			}
			if (stats != null) stats.NoTarget++;
			return npc.netID;

			// Picking its own type keeps the NPC as it is (a Pinky stays a Pinky)
			int Own(int pick) => pick == npcType ? npc.netID : pick;
		}

		// By biome: the owner of the slot this spawn fills (BiomeSlots); forceChance replaces 100 - BiomeVanillaChance
		private static int BiomeReplacement(NPC npc, float? forceChance, SwapStats stats)
		{
			BiomeSlotMap map = BiomeSlots.Current;
			int original = BiomeSlots.OriginalOf(map, npc);
			BiomeSlot slot = BiomeSlots.SlotFor(map, npc, original);
			if (slot == null)
			{
				if (stats != null) stats.NotInPool++;
				return npc.netID;
			}
			float keep = forceChance.HasValue ? 100 - forceChance.Value : EnemiesConfig.Instance.BiomeVanillaChance;
			if (keep > 0 && Main.rand.NextFloat() * 100 < keep)
			{
				if (stats != null) stats.ChanceFailed++;
				return npc.netID;
			}
			// Hardmode slots stay vanilla before hardmode (and post-Plantera ones before Plantera)
			if (!BiomeSlots.SlotActive(slot))
			{
				if (stats != null) stats.Inactive++;
				return npc.netID;
			}
			int owner = BiomeSlots.Pick(slot, Main.rand.NextDouble());
			// A spawn outside its usual place (an original met in the sun): never send it a daylight-intolerant owner
			bool daylight = Main.dayTime && npc.position.Y < Main.worldSurface * 16;
			if (daylight && DaylightIntolerant(owner))
			{
				if (stats != null) stats.NoTarget++;
				return npc.netID;
			}
			return owner;
		}

		// What a spawn becomes by biome without the vanilla chance and the extra, for the tests (its own net ID if it stays)
		public static int BiomeTarget(NPC npc)
		{
			BiomeSlotMap map = BiomeSlots.Current;
			BiomeSlot slot = BiomeSlots.SlotFor(map, npc, BiomeSlots.OriginalOf(map, npc));
			return slot == null || !BiomeSlots.SlotActive(slot) ? npc.netID : slot.Owner;
		}

		// Rare enemies and critters (bestiary stars) are picked less often
		private static int Pick(List<int> bucket, Random rng)
		{
			int total = 0;
			foreach (int type in bucket) total += Weight(type);
			int roll = rng.Next(total);
			foreach (int type in bucket)
			{
				roll -= Weight(type);
				if (roll < 0) return type;
			}
			return bucket[^1];
		}

		private static int Weight(int type)
		{
			int stars = ContentSamples.NpcBestiaryRarityStars.TryGetValue(type, out int s) ? s : 1;
			return Math.Max(1, 6 - stars);
		}

		// Natural enemies of a tier (night list: every one of them), for the debug tools
		public static List<int> NaturalEnemies(int tier)
		{
			// By biome: the enemies that really spawn somewhere (types: slime colours are Blue Slimes), at the tier from which
			// they do (never below their own tier, nor below the tier of the slots they spawn in: hardmode-only places stay hardmode)
			if (BiomeSlots.Active)
			{
				return BiomeSlots.Current.SpawnTier.Where(pair => !NPCID.Sets.CountsAsCritter[pair.Key] && Tiers.Clamp(pair.Value) == Tiers.Clamp(tier))
					.Select(pair => pair.Key).OrderBy(t => t).ToList();
			}
			Build();
			List<int> result = new();
			for (int m = 0; m < MovementCount; m++)
			{
				if (m >= (int)Movement.CritterGround) continue;
				result.AddRange(nightBuckets[m, Tiers.Clamp(tier)]);
			}
			return result;
		}

		public static bool IsSwappable(int npcType)
		{
			Build();
			return movements.ContainsKey(npcType);
		}

		// Habitat of a swappable type ("Ground", "CritterWater"...), null if it is never swapped
		public static string HabitatOf(int npcType)
		{
			Build();
			return movements.TryGetValue(npcType, out Movement m) ? m.ToString() : null;
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
				if (!DaylightIntolerant(type)) dayBuckets[(int)movement.Value, tier].Add(type);
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

		// Movement of a natural enemy that owns biome slots (net IDs: slime colours), null if it is never swapped
		internal static int? SlotMovement(int type, NPC sample, EnemiesConfig cfg) => (int?)Classify(type, sample, cfg);

		// Movement of an event enemy that may take a biome slot as an extra (no critters; scripted invasions excluded)
		internal static int? ExtraMovement(int type, NPC npc, EnemiesConfig cfg)
		{
			if (Excluded.Contains(type) || NPCID.Sets.CountsAsCritter[type]) return null;
			if (NPCID.Sets.BelongsToInvasionOldOnesArmy[type] || NPCID.Sets.BelongsToInvasionMartianMadness[type]) return null;
			if (!Swappable(type, npc, cfg)) return null;
			return (int)MovementOf(npc);
		}

		private static Movement? Classify(int type, NPC npc, EnemiesConfig cfg)
		{
			if (Excluded.Contains(type) || EclipseEnemies.Contains(type)) return null;
			if (npc.boss || NPCID.Sets.ShouldBeCountedAsBoss[type] || npc.townNPC || NPCID.Sets.ActsLikeTownNPC[type] || NPCID.Sets.IsTownPet[type]) return null;
			if (ExcludedAiStyles.Contains(npc.aiStyle) || NPCID.Sets.ProjectileNPC[type]) return null;
			if (npc.ModNPC != null && !cfg.SwapModdedEnemies) return null;
			if (IsEventEnemy(type)) return cfg.SwapEventEnemies ? MovementOf(npc) : null;
			if (NPCID.Sets.CountsAsCritter[type]) return cfg.SwapCritters ? CritterMovement(type, npc) : null;
			return Swappable(type, npc, cfg) ? MovementOf(npc) : null;
		}

		// An enemy (not a critter) that can be transformed safely
		private static bool Swappable(int type, NPC npc, EnemiesConfig cfg)
		{
			if (npc.boss || NPCID.Sets.ShouldBeCountedAsBoss[type] || npc.townNPC || NPCID.Sets.ActsLikeTownNPC[type] || NPCID.Sets.IsTownPet[type]) return false;
			if (ExcludedAiStyles.Contains(npc.aiStyle) || NPCID.Sets.ProjectileNPC[type]) return false;
			if (npc.ModNPC != null && !cfg.SwapModdedEnemies) return false;
			if (npc.lifeMax <= 5 || npc.dontTakeDamage || npc.immortal || npc.friendly) return false;
			// Worms and multi-part enemies can't be transformed safely
			if (npc.aiStyle == NPCAIStyleID.Worm) return false;
			// Natural enemies have a banner; boss minions and parts don't
			return Item.NPCtoBanner(npc.BannerID()) > 0;
		}

		private static Movement? CritterMovement(int type, NPC npc)
		{
			if (CritterExcluded.Contains(type) || NPCID.Sets.GoldCrittersCollection.Contains(type) || npc.lavaImmune || npc.ModNPC != null) return null;
			if (npc.aiStyle == NPCAIStyleID.Piranha) return Movement.CritterWater;
			return npc.noGravity ? Movement.CritterFlying : Movement.CritterGround;
		}

		private static Movement MovementOf(NPC npc)
		{
			if (npc.aiStyle == NPCAIStyleID.Piranha || npc.aiStyle == NPCAIStyleID.Jellyfish) return Movement.Water;
			return npc.noGravity ? Movement.Flying : Movement.Ground;
		}

		// Fighters and flying eyes that vanilla sends away during the day on the surface. Vanilla's check looks at the
		// current time, eclipse and invasion: they are forced to a plain surface day so the pool doesn't depend on when it was built.
		public static bool FleesDaylight(NPC npc)
		{
			if (npc.aiStyle == NPCAIStyleID.DemonEye) return true;
			if (npc.aiStyle != NPCAIStyleID.Fighter) return false;
			bool dayTime = Main.dayTime;
			bool eclipse = Main.eclipse;
			int invasion = Main.invasionType;
			int target = npc.target;
			try
			{
				Main.dayTime = true;
				Main.eclipse = false;
				Main.invasionType = 0;
				// Player 255 is never in a graveyard
				npc.target = Main.maxPlayers;
				return !NPC.DespawnEncouragement_AIStyle3_Fighters_NotDiscouraged(npc.type, Microsoft.Xna.Framework.Vector2.Zero, npc);
			}
			finally
			{
				npc.target = target;
				Main.dayTime = dayTime;
				Main.eclipse = eclipse;
				Main.invasionType = invasion;
			}
		}

		private static readonly Dictionary<int, bool> intolerance = new();

		// Flees daylight or dies in it (net IDs accepted; cached: FleesDaylight changes globals for a moment)
		public static bool DaylightIntolerant(int id)
		{
			if (intolerance.TryGetValue(id, out bool cached)) return cached;
			bool result = ContentSamples.NpcsByNetId.TryGetValue(id, out NPC sample) && (ExcludedFromDaySurface.Contains(sample.type) || FleesDaylight(sample));
			intolerance[id] = result;
			return result;
		}

		// Enemies that may replace a surface spawn by day, for the tests
		public static IEnumerable<int> DaylightEnemies()
		{
			Build();
			foreach (List<int> bucket in dayBuckets)
			{
				foreach (int type in bucket) yield return type;
			}
		}

		// Every type that can take another one's place, for the tests
		public static IEnumerable<int> Targets()
		{
			Build();
			return movements.Keys;
		}

		private static bool IsEventEnemy(int type) =>
			NPCID.Sets.BelongsToInvasionOldOnesArmy[type] || NPCID.Sets.BelongsToInvasionGoblinArmy[type] || NPCID.Sets.BelongsToInvasionFrostLegion[type]
			|| NPCID.Sets.BelongsToInvasionPirate[type] || NPCID.Sets.BelongsToInvasionMartianMadness[type];

		public static void Reset()
		{
			intolerance.Clear();
			dayBuckets = null;
			nightBuckets = null;
			movements = null;
		}
	}

	public class EnemySwapSystem : ModSystem
	{
		public static readonly bool[] Pending = new bool[Main.maxNPCs + 1];

		public static SwapStats Stats { get; private set; } = new();

		// The guaranteed hosts come from the pool: rebuilt with the other world caches (its swap settings may have changed)
		public override void Load()
		{
			WorldCaches.Reset += EnemySwapPool.Reset;
			WorldCaches.Reset += BiomeSlots.Reset;
		}

		public override void OnWorldLoad()
		{
			EnemySwapPool.Reset();
			BiomeSlots.Reset();
			Array.Clear(Pending);
			Stats = new SwapStats();
		}

		public override void PostUpdateNPCs() => ProcessPending(null);

		public static void ProcessPending(float? forceChance)
		{
			if (Main.netMode == NetmodeID.MultiplayerClient) return;
			for (int i = 0; i < Main.maxNPCs; i++) ProcessSlot(i, forceChance);
		}

		// forceChance: used by the tests to swap whatever the settings say (on their own slots only)
		public static void ProcessSlot(int i, float? forceChance)
		{
			if (Main.netMode == NetmodeID.MultiplayerClient || i < 0 || i >= Main.maxNPCs || !Pending[i]) return;
			Pending[i] = false;
			NPC npc = Main.npc[i];
			if (!npc.active) return;
			int target = EnemySwapPool.Replacement(npc, forceChance, Stats);
			// Net IDs: a slot may hold a slime colour (Pinky...)
			if (target == 0 || target == npc.netID || !ContentSamples.NpcsByNetId.ContainsKey(target)) return;
			ApplySwap(npc, target);
		}

		// target is a net ID: sets are indexed by its type (slime colours are Blue Slimes)
		public static void ApplySwap(NPC npc, int target)
		{
			if (!ContentSamples.NpcsByNetId.TryGetValue(target, out NPC sample)) return;
			if (NPCID.Sets.CountsAsCritter[sample.type]) Stats.SwappedCritters++;
			else Stats.SwappedEnemies++;
			npc.Transform(target);
			// Transform gives the NPC new globals: its individual stats (giant, mini...) are rolled again
			EnemyIndividualNPC.RollAndApply(npc);
		}

		public override void Unload()
		{
			EnemySwapPool.Reset();
			BiomeSlots.Unload();
		}
	}
}

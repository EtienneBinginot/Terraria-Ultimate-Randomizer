using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.GameContent.Bestiary;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	// Items that must be findable in the world (basic crafting stations; vanilla recipe ingredients when chosen):
	// each one is placed as an extra drop of two common enemies of its tier, in the pot pool and, for stations, in wooden chests.
	// Computed from the seed and the settings only, so every machine gets the same result.
	public static class GuaranteedSources
	{
		public struct Requirement
		{
			public int Item;
			public int Tier;
			public bool Station;
			public bool Critical;
			// A tool (pickaxe of a mining level): dropped one at a time, as rarely as a station
			public bool Tool;
		}

		public const int HostsPerItem = 2;
		public const float CriticalStationChance = 1 / 15f;
		public const float StationChance = 1 / 40f;
		public const float MaterialChance = 1 / 6f;
		public const int MaxPerHost = 6;

		// Extra requirement providers (vanilla recipe ingredients, a pickaxe per mining level), added by the recipe system
		public static readonly List<Func<IEnumerable<Requirement>>> ExtraRequirements = new();

		private static Dictionary<int, List<PlannedDrop>> byNpc;
		private static List<Requirement> requirements;

		static GuaranteedSources()
		{
			WorldCaches.Reset += Reset;
		}

		public static void Reset()
		{
			byNpc = null;
			requirements = null;
		}

		public static List<Requirement> Requirements
		{
			get
			{
				Build();
				return requirements;
			}
		}

		// The guaranteed drops of an enemy type
		public static List<PlannedDrop> For(int npcType)
		{
			Build();
			return byNpc.TryGetValue(npcType, out List<PlannedDrop> drops) ? drops : Empty;
		}

		private static readonly List<PlannedDrop> Empty = new();

		public static IEnumerable<int> HostsOf(int item)
		{
			Build();
			return byNpc.Where(pair => pair.Value.Any(d => d.Item == item)).Select(pair => pair.Key);
		}

		// Basic stations that the randomizer may take away from their recipe
		public static IEnumerable<Requirement> StationRequirements()
		{
			if (!CraftingConfig.Instance.RandomizeRecipes) yield break;
			foreach ((_, Stations.StationInfo info) in Stations.Basic())
			{
				if (ItemPool.IsRandomizable(info.Item)) yield return new Requirement { Item = info.Item, Tier = info.Tier, Station = true, Critical = info.Critical };
			}
		}

		// Items a pot may also give, among those already unlocked
		public static List<int> PotItems(int unlockedTier) =>
			Requirements.Where(r => r.Tier <= unlockedTier && (r.Station || r.Tool || r.Tier == 0)).Select(r => r.Item).ToList();

		private static void Build()
		{
			if (byNpc != null) return;
			Dictionary<int, List<PlannedDrop>> result = new();
			List<Requirement> all = StationRequirements().ToList();
			foreach (Func<IEnumerable<Requirement>> provider in ExtraRequirements) all.AddRange(provider());
			// One requirement per item: the earliest tier, keeping every flag (a pickaxe also needed as an ingredient stays a tool)
			all = all.GroupBy(r => r.Item).Select(g => new Requirement
			{
				Item = g.Key, Tier = g.Min(r => r.Tier), Station = g.Any(r => r.Station), Critical = g.Any(r => r.Critical), Tool = g.Any(r => r.Tool)
			}).OrderBy(r => r.Tier).ThenBy(r => DetRandom.ItemKey(r.Item)).ToList();

			List<int>[] hosts = new List<int>[Tiers.Count];
			for (int t = 0; t < Tiers.Count; t++) hosts[t] = Hosts(t);
			Dictionary<int, int> load = new();
			float multiplier = ProgressionConfig.Instance.GuaranteedDropMultiplier;
			foreach (Requirement requirement in all)
			{
				Random rng = DetRandom.Get(RollKey.Guaranteed, DetRandom.ItemKey(requirement.Item));
				HashSet<int> chosen = new();
				for (int h = 0; h < HostsPerItem; h++)
				{
					int host;
					List<int> common = requirement.Critical && h < CriticalHostSets
						? CriticalHosts(h).Where(n => !chosen.Contains(n) && load.GetValueOrDefault(n) < MaxPerHost && IsNaturalHost(n, requirement.Tier)).ToList()
						: null;
					if (common != null && common.Count > 0) host = common[rng.Next(common.Count)];
					else
					{
						List<int> candidates = null;
						for (int t = Tiers.Clamp(requirement.Tier); t >= 0; t--)
						{
							candidates = hosts[t].Where(n => !chosen.Contains(n) && load.GetValueOrDefault(n) < MaxPerHost).ToList();
							if (candidates.Count > 0) break;
						}
						if (candidates == null || candidates.Count == 0) break;
						// Spread the load: among the least used hosts
						List<int> best = candidates.OrderBy(n => load.GetValueOrDefault(n)).ThenBy(DetRandom.NpcKey).Take(3).ToList();
						host = best[rng.Next(best.Count)];
					}
					chosen.Add(host);
					load[host] = load.GetValueOrDefault(host) + 1;
					// Rare enough to be a small quest (first work bench in 15 to 30 minutes, see PacingTests)
					float chance = requirement.Station ? (requirement.Critical ? CriticalStationChance : StationChance) : requirement.Tool ? StationChance : MaterialChance;
					(int min, int max) = requirement.Station || requirement.Tool ? (1, 1) : StackRules.Range(requirement.Item);
					if (!result.TryGetValue(host, out List<PlannedDrop> drops)) result[host] = drops = new List<PlannedDrop>();
					drops.Add(new PlannedDrop { Item = requirement.Item, StackMin = min, StackMax = Math.Max(min, max), Chance = Math.Min(1f, chance * multiplier), Extra = true, Guaranteed = true });
				}
			}
			requirements = all;
			byNpc = result;
		}

		// Critical stations (work bench, furnace, anvil, table, chair) are carried by the enemies met most in the first hour:
		// slimes by day (every colour shares the Blue Slime's type), then zombies or demon eyes by night. Other hosts are
		// common by bestiary rarity only, and may be enemies seldom met at first (first work bench after 15 to 30 minutes,
		// see PacingTests)
		private static readonly int[][] VanillaCriticalHosts = { new int[] { NPCID.BlueSlime }, new int[] { NPCID.Zombie, NPCID.DemonEye } };

		private const int CriticalHostSets = 2;

		// Enemies redistributed by biome: the enemies that now spawn in the Forest slots of the slimes (day) and of the
		// zombies and demon eyes (night)
		public static int[] CriticalHosts(int set) => BiomeSlots.Active ? BiomeSlots.CriticalHosts(set == 0).ToArray() : VanillaCriticalHosts[set];

		private static bool IsNaturalHost(int npc, int tier)
		{
			if (NPCID.Sets.CountsAsCritter[npc] || IsEventEnemy(npc)) return false;
			for (int t = 0; t <= Tiers.Clamp(tier); t++)
			{
				if (EnemySwapPool.NaturalEnemies(t).Contains(npc)) return true;
			}
			return false;
		}

		// Common natural enemies of a tier (bestiary rarity 1, else 2, else any)
		private static List<int> Hosts(int tier)
		{
			List<int> natural = EnemySwapPool.NaturalEnemies(tier).Where(n => !NPCID.Sets.CountsAsCritter[n] && !IsEventEnemy(n)).ToList();
			for (int stars = 1; stars <= 5; stars++)
			{
				List<int> common = natural.Where(n => Stars(n) <= stars).OrderBy(DetRandom.NpcKey).ToList();
				if (common.Count > 0) return common;
			}
			return natural.OrderBy(DetRandom.NpcKey).ToList();
		}

		private static HashSet<IBestiaryInfoElement> eventTags;

		// Halloween enemies whose bestiary entry has no event tag (they also come to graveyards, rarely)
		private static readonly int[] UntaggedSeasonal = { NPCID.Raven, NPCID.Ghost };

		// Enemies that only come with an event, a moon, a season or a weather (invasions, blood moon, eclipse, pumpkin and
		// frost moons, Halloween, Christmas, rain, windy day...): a guaranteed drop on them could be out of reach for weeks.
		// Read from the bestiary spawn conditions (every event tag of CommonTags), plus the invasion sets for modded enemies.
		public static bool IsEventEnemy(int npcType)
		{
			if (npcType <= 0 || npcType >= NPCID.Sets.BelongsToInvasionOldOnesArmy.Length) return false;
			if (NPCID.Sets.BelongsToInvasionOldOnesArmy[npcType] || NPCID.Sets.BelongsToInvasionGoblinArmy[npcType] || NPCID.Sets.BelongsToInvasionFrostLegion[npcType]
				|| NPCID.Sets.BelongsToInvasionPirate[npcType] || NPCID.Sets.BelongsToInvasionMartianMadness[npcType] || UntaggedSeasonal.Contains(npcType))
			{
				return true;
			}
			eventTags ??= typeof(BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions.Events).GetFields(BindingFlags.Public | BindingFlags.Static)
				.Concat(typeof(BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions.Invasions).GetFields(BindingFlags.Public | BindingFlags.Static))
				.Select(f => f.GetValue(null)).OfType<IBestiaryInfoElement>().ToHashSet();
			List<IBestiaryInfoElement> info = Main.BestiaryDB?.FindEntryByNPCID(npcType)?.Info;
			return info != null && info.Any(eventTags.Contains);
		}

		private static int Stars(int npcType) => ContentSamples.NpcBestiaryRarityStars.TryGetValue(npcType, out int stars) ? stars : 1;
	}

	// The tier at which each item can be found without crafting it: world materials, ores, enemy and boss drops,
	// bags, herbs and guaranteed sources. Independent of the current progression (gated drops count from the tier
	// where they are unlocked).
	public static class Findability
	{
		public const int Never = int.MaxValue;

		// Gathered directly in the world
		private static readonly (int item, int tier)[] WorldMaterials =
		{
			(ItemID.DirtBlock, 0), (ItemID.StoneBlock, 0), (ItemID.Wood, 0), (ItemID.BorealWood, 0), (ItemID.PalmWood, 0), (ItemID.RichMahogany, 0),
			(ItemID.Ebonwood, 0), (ItemID.Shadewood, 0), (ItemID.Cactus, 0), (ItemID.ClayBlock, 0), (ItemID.MudBlock, 0), (ItemID.SandBlock, 0),
			(ItemID.SnowBlock, 0), (ItemID.IceBlock, 0), (ItemID.SiltBlock, 0), (ItemID.SlushBlock, 0), (ItemID.Cobweb, 0), (ItemID.GraniteBlock, 0),
			(ItemID.MarbleBlock, 0), (ItemID.Sandstone, 0), (ItemID.HardenedSand, 0), (ItemID.EbonstoneBlock, 0), (ItemID.CrimstoneBlock, 0),
			(ItemID.Hive, 0), (ItemID.DesertFossil, 0), (ItemID.Amethyst, 0), (ItemID.Topaz, 0), (ItemID.Sapphire, 0), (ItemID.Emerald, 0),
			(ItemID.Ruby, 0), (ItemID.Diamond, 0), (ItemID.Amber, 0), (ItemID.FallenStar, 0), (ItemID.Seashell, 0), (ItemID.Coral, 0),
			(ItemID.Starfish, 0), (ItemID.Acorn, 0), (ItemID.Pumpkin, 0), (ItemID.Vine, 0), (ItemID.Daybloom, 0), (ItemID.Moonglow, 0),
			(ItemID.Blinkroot, 0), (ItemID.Waterleaf, 0), (ItemID.Shiverthorn, 0), (ItemID.Deathweed, 1), (ItemID.Fireblossom, 2),
			(ItemID.Mushroom, 0), (ItemID.GlowingMushroom, 0), (ItemID.VileMushroom, 0), (ItemID.ViciousMushroom, 0), (ItemID.JungleSpores, 0),
			(ItemID.AshBlock, 2), (ItemID.Obsidian, 2), (ItemID.Pearlwood, 4), (ItemID.PearlstoneBlock, 4), (ItemID.CrystalShard, 4)
		};

		private static readonly int[] Herbs =
		{
			ItemID.Daybloom, ItemID.Moonglow, ItemID.Blinkroot, ItemID.Deathweed, ItemID.Waterleaf, ItemID.Fireblossom, ItemID.Shiverthorn,
			ItemID.Mushroom, ItemID.GlowingMushroom, ItemID.VileMushroom, ItemID.ViciousMushroom, ItemID.JungleSpores
		};

		private static int[] baseTiers;
		private static int[] tiers;

		static Findability()
		{
			WorldCaches.Reset += Reset;
		}

		// Materials gathered directly in the world (never replaced for variety's sake), and ores
		public static HashSet<int> WorldMaterialSet()
		{
			HashSet<int> set = WorldMaterials.Select(m => m.item).ToHashSet();
			for (int type = 1; type < ItemPool.Info.Length; type++)
			{
				if (OreMap.IsOre(type) && OreMap.IsFindable(type)) set.Add(type);
			}
			return set;
		}

		// Including the guaranteed sources
		public static int Tier(int item)
		{
			if (tiers == null)
			{
				int[] result = (int[])BaseTiers().Clone();
				foreach (GuaranteedSources.Requirement requirement in GuaranteedSources.Requirements)
				{
					foreach (int host in GuaranteedSources.HostsOf(requirement.Item))
					{
						result[requirement.Item] = Math.Min(result[requirement.Item], NpcTiers.BaseTier(host));
					}
				}
				tiers = result;
			}
			return item > 0 && item < tiers.Length ? tiers[item] : Never;
		}

		// Without the guaranteed sources (used to decide what must be guaranteed)
		public static int BaseTier(int item)
		{
			int[] values = BaseTiers();
			return item > 0 && item < values.Length ? values[item] : Never;
		}

		public static void Reset()
		{
			baseTiers = null;
			tiers = null;
		}

		private static int[] BaseTiers()
		{
			if (baseTiers != null) return baseTiers;
			int[] result = new int[ItemPool.Info.Length];
			Array.Fill(result, Never);
			void Add(int item, int tier)
			{
				if (item > 0 && item < result.Length) result[item] = Math.Min(result[item], Math.Max(0, tier));
			}
			LootConfig loot = LootConfig.Instance;
			ProgressionConfig progression = ProgressionConfig.Instance;
			int Gated(int item) => progression.TierGating ? Math.Max(0, ItemPool.TierOf(item) - progression.TierAllowance) : 0;

			foreach ((int item, int tier) in WorldMaterials) Add(item, tier);
			if (loot.RandomizeHerbs)
			{
				foreach (int herb in Herbs.Where(ItemPool.IsRandomizable))
				{
					int herbTier = WorldMaterials.First(m => m.item == herb).tier;
					foreach (int item in WorldSources.HerbTable(herb)) Add(item, Math.Max(herbTier, Gated(item)));
				}
			}
			for (int type = 1; type < ItemPool.Info.Length; type++)
			{
				// Only the ores of this world can be mined (Tin is not in a Copper world). Randomized or not, mining gives
				// the ores of the world of each tier (a permutation inside the tier), so the mapping itself doesn't matter.
				if (OreMap.IsOre(type) && OreMap.IsFindable(type)) Add(type, ItemPool.TierOf(type));
			}

			float minChance = progression.MinFindableChance / 100f;
			void AddPlan(List<PlannedDrop> plan, int sourceTier)
			{
				foreach (PlannedDrop drop in plan)
				{
					if (drop.Chance >= minChance && WorldAllows(drop.Conditions)) Add(drop.Item, Math.Max(sourceTier, Gated(drop.Item)));
				}
			}
			for (int t = 0; t < Tiers.Count; t++)
			{
				// Event, moon and seasonal enemies (in the pool when SwapEventEnemies is on) are no reliable source
				foreach (int npc in EnemySwapPool.NaturalEnemies(t).Where(n => !GuaranteedSources.IsEventEnemy(n))) AddPlan(RandomNpcDropRule.Plan(npc, Main.expertMode, lucky: true, guaranteed: false), t);
			}
			foreach (int boss in NpcTiers.BossTypes) AddPlan(RandomNpcDropRule.Plan(boss, Main.expertMode, lucky: true, guaranteed: false), NpcTiers.BaseTier(boss));
			// A bag's content is found once the bag is (bags may hold bags: repeat until nothing changes)
			Dictionary<int, List<PlannedDrop>> bagPlans = LootSnapshot.Items.Keys.Where(b => !SeasonalBags.Contains(b)).OrderBy(b => b)
				.ToDictionary(b => b, b => BagPlan.Plan(b, lucky: true));
			bool changed = true;
			for (int pass = 0; pass < 8 && changed; pass++)
			{
				changed = false;
				foreach ((int bag, List<PlannedDrop> plan) in bagPlans)
				{
					int tier = BagContentTier(bag, result);
					if (tier == Never) continue;
					foreach (PlannedDrop drop in plan)
					{
						if (drop.Chance < minChance || drop.Item <= 0 || drop.Item >= result.Length || !WorldAllows(drop.Conditions)) continue;
						int dropTier = Math.Max(tier, Gated(drop.Item));
						if (dropTier < result[drop.Item])
						{
							result[drop.Item] = dropTier;
							changed = true;
						}
					}
				}
			}
			baseTiers = result;
			return result;
		}

		// Drop conditions fixed by the world (difficulty, world evil): an expert-only treasure bag is no source in Classic.
		// Their CanDrop only reads Main.expertMode, Main.masterMode and WorldGen.crimson (synced in message 7); in
		// drunk worlds both evils exist. Other conditions (events, biomes...) are left to the drop chance.
		public static bool WorldAllows(List<IItemDropRuleCondition> conditions)
		{
			if (conditions == null) return true;
			bool Evil(bool crimson) => Main.drunkWorld || WorldGen.crimson == crimson;
			foreach (IItemDropRuleCondition condition in conditions)
			{
				bool allowed = condition switch
				{
					Conditions.IsExpert => Main.expertMode,
					Conditions.NotExpert => !Main.expertMode,
					Conditions.IsMasterMode => Main.masterMode,
					Conditions.NotMasterMode => !Main.masterMode,
					Conditions.IsCrimson => Evil(true),
					Conditions.IsCorruption => Evil(false),
					Conditions.IsCrimsonAndNotExpert => Evil(true) && !Main.expertMode,
					Conditions.IsCorruptionAndNotExpert => Evil(false) && !Main.expertMode,
					_ => true
				};
				if (!allowed) return false;
			}
			return true;
		}

		// Only given during seasonal events: never counted as a source
		public static readonly HashSet<int> SeasonalBags = new() { ItemID.Present, ItemID.GoodieBag };

		// Fishing is possible from the start (a Wooden Fishing Pole, Angler rewards)
		public const int FishingTier = 0;

		// Where a crate is caught, conservatively: hardmode crates in hardmode, lava crates once lava fishing gear is
		// reachable, dungeon crates after Skeletron, sky crates once floating islands are reachable
		public static int CrateAccessTier(int crate)
		{
			// Evil crates need their evil (both in drunk worlds); the Hallow only exists before hardmode in drunk and remix worlds
			bool Evil(bool crimson) => Main.drunkWorld || WorldGen.crimson == crimson;
			if (crate is ItemID.CorruptFishingCrate or ItemID.CorruptFishingCrateHard && !Evil(false)) return Never;
			if (crate is ItemID.CrimsonFishingCrate or ItemID.CrimsonFishingCrateHard && !Evil(true)) return Never;
			if (crate is ItemID.HallowedFishingCrate && !Main.drunkWorld && !Main.remixWorld) return Never;
			if (ItemID.Sets.IsFishingCrateHardmode[crate] || crate is ItemID.LavaCrate or ItemID.LavaCrateHard) return (int)Core.Tier.Hardmode;
			if (crate is ItemID.DungeonFishingCrate) return (int)Core.Tier.PostSkeletron;
			if (crate is ItemID.FloatingIslandFishingCrate) return (int)Core.Tier.PostEvilBoss;
			return FishingTier;
		}

		// Crates are caught as crates unless fishing turns them into something else
		public static bool CrateFished(int bag)
		{
			LootConfig loot = LootConfig.Instance;
			return ItemID.Sets.IsFishingCrate[bag] && (!loot.RandomizeFishing || loot.CratesStayCrates || !ItemPool.IsRandomizable(bag));
		}

		// The tier from which a bag's content counts as found: the bag's own tier, once the bag itself can be obtained
		// without crafting (dropped, or fished for crates); never for seasonal bags
		public static int BagContentTier(int bag) => BagContentTier(bag, BaseTiers());

		private static int BagContentTier(int bag, int[] found)
		{
			if (SeasonalBags.Contains(bag)) return Never;
			int tier = bag > 0 && bag < found.Length ? found[bag] : Never;
			if (CrateFished(bag)) tier = Math.Min(tier, CrateAccessTier(bag));
			if (tier == Never) return Never;
			int bagTier = LootSnapshot.BagTier(bag);
			return Math.Max(tier, bagTier >= 0 ? bagTier : ItemPool.TierOf(bag));
		}
	}
}

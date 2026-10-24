using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.GameContent.Bestiary;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	// Items that must be findable in the world (crafted and dropped boss summons, progression tools; vanilla recipe
	// ingredients when chosen): each one is placed as an extra drop of the common enemies of one biome of its tier (so that
	// the biome gives it in about StationFindKills kills by day and by night); tools and early materials also in the pot pool.
	// With randomized recipes, crafting stations are a small quest instead: their recipe is the main way (RecipePlanner
	// rebuilds it, doable by their tier), and one "guardian" enemy of their tier carries each of them as a backup
	// (StationGuardianChance; PlaceGuardian), never pots nor chests; with recipes kept, the unprotected progression and
	// themed stations are guaranteed like the summons. Never before its tier: without natural enemies of its tier, the progression bosses of that tier
	// carry it (with neither, an ordinary item goes to the closest lower tier, a hard gate to the next higher one: Assign).
	// Computed from the seed and the settings only, so every machine gets the same result.
	public static class GuaranteedSources
	{
		public struct Requirement
		{
			public int Item;
			public int Tier;
			// Dropped one at a time (a station, summon or tool)
			public bool Station;
			// Places a crafting station: carried by one guardian only (PlaceGuardian), its recipe rebuilt for its tier
			public bool Placer;
			public bool Critical;
			// A tool (pickaxe of a mining level): dropped one at a time, as rarely as a station
			public bool Tool;
			// A biome whose enemies never carry it (ExcludedBiomeOf: the Temple for the Temple Key, which opens it); null: none
			public SpawnBiome? ExcludedBiome;
			// The biomes whose enemies carry it when one of them can (BiomeKeyRequirements: a biome key in its own biome); null: any
			public SpawnBiome[] Biomes;
		}

		// The biome an item can't come from: the Temple Key opens the Jungle Temple, so a Temple enemy (or Golem, summoned
		// inside) carrying it would lock it away for good
		public static SpawnBiome? ExcludedBiomeOf(int item) => item == ItemID.TempleKey ? SpawnBiome.Temple : null;

		// Is this enemy met only in that biome (this world's spawn map, every tier)? Golem counts as a Temple one (its altar
		// is inside). An enemy without any planned biome is not
		public static bool OnlyIn(int npc, SpawnBiome biome)
		{
			int type = BiomeSlots.TypeOf(npc);
			if (biome == SpawnBiome.Temple && BossParts.Owner(type) == NPCID.Golem) return true;
			List<(int biome, double share, SlotTime time)> biomes = BiomeSlots.PlannedBiomes(type);
			return biomes.Count > 0 && biomes.All(b => b.biome == (int)biome);
		}

		// A source that can't give this item in time: met only in the biome it opens (ExcludedBiomeOf)
		public static bool ExcludedHost(int item, int npc) => ExcludedBiomeOf(item) is SpawnBiome biome && OnlyIn(npc, biome);

		// Each item goes to one biome of its tier: the enemies met there steadily (at least MinBiomeHostShare each, no rare
		// enemy, no event enemy), most met first, up to GuaranteedHostsPerItem. A biome qualifies with at least MinBiomeHosts
		// of them making MinBiomeShare of its spawns together, in each period it is kept for. 1.3.2 asked for 3 hosts and 20 %:
		// only 3 biomes qualified at tier 0 and 1 at tier 1, so items piled up on the same enemies or fell a tier
		public const int MinBiomeHosts = 2;
		public const double MinBiomeShare = 0.1;
		public const double MinBiomeHostShare = 0.01;
		// The hosts met in a period share its target: the biome gives a station, summon or tool in about StationFindKills kills
		// of that period (1 / (sum of shares x chance)), a material in MaterialTargetKills, within these bounds
		public const float MaxStationChance = 0.5f;
		public const double MaterialTargetKills = 8;
		public const float MaterialChance = 1 / 20f;
		public const float MaxMaterialChance = 0.75f;
		// Without a known spawn share (enemies swapped another way, no biome with enough hosts): common hosts of the tier,
		// with the total chance two hosts had before 1.3.0
		public const float UnknownShareStationChance = 1 / 25f;
		public const float UnknownShareMaterialChance = 1 / 6f;
		public const int UnknownShareHosts = 2;
		// Stations, summons and tools are never rarer than this, whatever GuaranteedDropMultiplier says
		public const float MinStationChance = 0.01f;
		// GuaranteedFindTime: a summon or tool comes within StationFindKills x FindKillsMargin kills of its biome
		public const double FindKillsMargin = 1.5;
		// A frequent creature of the hints makes at least this share of the spawns of its best biome, or half the average
		// share of that biome's enemies when it holds many (MinShare)
		public const double MinHostShare = 0.05;

		public static double MaxFindKills => ProgressionConfig.Instance.StationFindKills * FindKillsMargin;

		// The chance of a station on its guardian (StationGuardianChance, whatever GuaranteedDropMultiplier says: the recipe
		// is the main way); a boss guardian gives it at every kill
		public static float GuardianChance => Math.Clamp(ProgressionConfig.Instance.StationGuardianChance / 100f, MinStationChance, 1f);
		// What a guardian weighs in the host loads (HostLoads.Single): the setting's default, never the live value, so that
		// tuning StationGuardianChance never moves the other items' hosts of a world
		public const float NominalGuardianChance = 0.125f;
		// A guardian makes at least this share of its biome's spawns in each period it is met in (PlaceGuardian): a station's
		// backup never hangs on an enemy met once in a hundred kills. GuaranteedFindTime: its biome gives the station within
		// GuardianKillsFactor x StationFindKills kills (1 / (share x chance), 160 at the defaults)
		public const double MinGuardianShare = MinHostShare;
		public const double GuardianKillsFactor = 3;
		// Once a station has been obtained in this world (the codex: server or single player), its guardian drops it this much
		// less often: a backup, not a pile of tables (RandomNpcDropRule.TryDroppingItem)
		public const float FoundGuardianFactor = 0.1f;

		// Enemies carrying an item at most (GuaranteedHostsPerItem), and at least in its biome (fewer only when the setting is
		// lower than MinBiomeHosts)
		public static int HostLimit => Math.Max(2, ProgressionConfig.Instance.GuaranteedHostsPerItem);
		public static int MinHostsInBiome => Math.Min(MinBiomeHosts, HostLimit);

		// Requirements left without any host (no natural enemy at all at or below their tier): logged, and 0 in the tests
		// with the default settings
		public static int Missing { get; private set; }

		// Extra requirement providers (vanilla recipe ingredients, a pickaxe per mining level), added by the recipe system
		public static readonly List<Func<IEnumerable<Requirement>>> ExtraRequirements = new();

		private static Dictionary<int, List<PlannedDrop>> byNpc;
		private static List<Requirement> requirements;
		// The biome each item was placed in and the tier its shares were read at (biome -1: common hosts without a biome)
		private static Dictionary<int, Placement> placements;
		private static Dictionary<int, int> hostTiers;
		// The spawn map the frequent cache was computed with (rebuilt when the world evil arrives, or the settings change)
		private static BiomeSlotMap frequentMap;

		static GuaranteedSources()
		{
			WorldCaches.Reset += Reset;
		}

		public static void Reset()
		{
			byNpc = null;
			requirements = null;
			placements = null;
			Missing = 0;
			hostTiers = null;
			frequentMap = null;
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
		// Vanilla turns some enemies into another type during a fight (NPC.AI_003_Fighters: a Lihzahrd under 55 % of its life
		// becomes a Lihzahrd Crawler, the wall crawlers switch between their ground and wall forms; NPC.VanillaAI_Inner): a
		// kill of either form gives the guaranteed drops of both, else a Lihzahrd host almost never gave its item
		private static readonly Dictionary<int, int[]> OtherForms = new()
		{
			[NPCID.LihzahrdCrawler] = new int[] { NPCID.Lihzahrd },
			[NPCID.NutcrackerSpinning] = new int[] { NPCID.Nutcracker },
			[NPCID.WallCreeper] = new int[] { NPCID.WallCreeperWall }, [NPCID.WallCreeperWall] = new int[] { NPCID.WallCreeper },
			[NPCID.JungleCreeper] = new int[] { NPCID.JungleCreeperWall }, [NPCID.JungleCreeperWall] = new int[] { NPCID.JungleCreeper },
			[NPCID.BlackRecluse] = new int[] { NPCID.BlackRecluseWall }, [NPCID.BlackRecluseWall] = new int[] { NPCID.BlackRecluse },
			[NPCID.BloodCrawler] = new int[] { NPCID.BloodCrawlerWall }, [NPCID.BloodCrawlerWall] = new int[] { NPCID.BloodCrawler },
			[NPCID.DesertScorpionWalk] = new int[] { NPCID.DesertScorpionWall }, [NPCID.DesertScorpionWall] = new int[] { NPCID.DesertScorpionWalk }
		};

		public static IEnumerable<int> FormsOf(int npcType) => OtherForms.TryGetValue(npcType, out int[] forms) ? forms : Array.Empty<int>();

		// The guaranteed drops a kill of this type rolls (server): its own, and those of its other forms (OtherForms) for the
		// items it does not carry itself. Plans, hints and findability read For: the host stays the planned type
		public static List<PlannedDrop> ForKill(int npcType)
		{
			List<PlannedDrop> own = For(npcType);
			if (!OtherForms.TryGetValue(npcType, out int[] forms)) return own;
			List<PlannedDrop> all = new(own);
			foreach (int form in forms)
			{
				foreach (PlannedDrop drop in For(form))
				{
					if (!all.Any(d => d.Item == drop.Item)) all.Add(drop);
				}
			}
			return all;
		}

		public static List<PlannedDrop> For(int npcType)
		{
			Build();
			return byNpc.TryGetValue(npcType, out List<PlannedDrop> drops) ? drops : Empty;
		}

		private static readonly List<PlannedDrop> Empty = new();

		// Most met first (the order they were planned in), then by type. Read at once: a later Build (another spawn map) must
		// not change a list being read
		public static IEnumerable<int> HostsOf(int item)
		{
			Build();
			Dictionary<int, List<int>> index = HostIndex();
			List<int> order = placements.TryGetValue(item, out Placement p) ? p.Hosts : null;
			return index.TryGetValue(item, out List<int> hosts) ? hosts.OrderBy(n => order?.IndexOf(n) ?? 0).ThenBy(n => n).ToList() : new List<int>();
		}

		// The hosts of each item, for the drops it was built from (rebuilt with them)
		private static Dictionary<int, List<int>> hostIndex;
		private static Dictionary<int, List<PlannedDrop>> hostIndexFor;

		private static Dictionary<int, List<int>> HostIndex()
		{
			Dictionary<int, List<PlannedDrop>> drops = byNpc;
			if (hostIndexFor == drops) return hostIndex;
			Dictionary<int, List<int>> index = new();
			foreach ((int npc, List<PlannedDrop> list) in drops)
			{
				foreach (int item in list.Select(d => d.Item).Distinct())
				{
					if (!index.TryGetValue(item, out List<int> hosts)) index[item] = hosts = new List<int>();
					hosts.Add(npc);
				}
			}
			hostIndexFor = drops;
			return hostIndex = index;
		}

		public struct Placement
		{
			public int Biome;
			// The tier the shares were read at (the search may go below the requirement's)
			public int Tier;
			// Most met first
			public List<int> Hosts;
			// When the hosts are met in the biome: Day or Night when it only holds hosts (enough of them) at that time, Any when
			// both periods hold some
			public SlotTime Time;
			// When each host is met in the biome (parallel to Hosts)
			public List<SlotTime> HostTimes;
			// Hosts kept although they were already full (no biome of the tier had enough others), each once at most
			public List<int> Forced;
			// A critical station on the Forest's critical hosts: no early biome qualified
			public bool Fallback;
			// On the progression bosses of its tier (BossHosts): no natural enemy of its tier can carry it
			public bool Boss;
			// Last resort: nothing of its tier could carry it, placed on the closest tier with hosts (Tier): a lower one for
			// an ordinary item, a higher one for a hard gate
			public bool Shifted;
			// A crafting station on its single guardian (PlaceGuardian)
			public bool Guardian;
		}

		// The enemy (or boss) carrying a crafting station in this world, 0 when none (not a station, or protected)
		public static int GuardianOf(int item)
		{
			Build();
			return placements.TryGetValue(item, out Placement p) && p.Guardian && p.Hosts.Count > 0 ? p.Hosts[0] : 0;
		}

		// The placement of a guaranteed item in this world (tests)
		public static bool TryPlacement(int item, out Placement placement)
		{
			Build();
			return placements.TryGetValue(item, out placement);
		}

		// When the hosts of a guaranteed item are met in its biome (Any: by day and by night, or unknown)
		public static SlotTime PlacedTime(int item)
		{
			Build();
			return placements.TryGetValue(item, out Placement p) ? p.Time : SlotTime.Any;
		}

		// When a host of a guaranteed item is met in its biome (Any: both, or unknown)
		public static SlotTime HostTime(int item, int host)
		{
			Build();
			if (!placements.TryGetValue(item, out Placement p) || p.HostTimes == null) return SlotTime.Any;
			int index = p.Hosts.IndexOf(host);
			return index >= 0 && index < p.HostTimes.Count ? p.HostTimes[index] : SlotTime.Any;
		}

		// The biome whose enemies carry a guaranteed item, -1 when unknown (not guaranteed, or common hosts without a biome)
		public static int BiomeOf(int item)
		{
			Build();
			return placements.TryGetValue(item, out Placement p) ? p.Biome : -1;
		}

		// The tier at which the hosts of a guaranteed item were chosen (-1: not guaranteed)
		public static int PlacedTier(int item)
		{
			Build();
			return placements.TryGetValue(item, out Placement p) ? p.Tier : -1;
		}

		// Steady share of the spawns of the item's biome that its hosts make together, at the tier and in the period(s) they
		// were chosen for (0: no biome)
		public static double BiomeShare(int item)
		{
			Build();
			if (!placements.TryGetValue(item, out Placement p) || p.Biome < 0) return 0;
			return p.Hosts.Sum(h => BiomeSlots.SteadyShareAt(p.Biome, h, p.Tier, p.Time));
		}

		// Basic stations that the randomizer may take away from their recipe
		public static IEnumerable<Requirement> StationRequirements()
		{
			if (!CraftingConfig.Instance.RandomizeRecipes) yield break;
			foreach ((_, Stations.StationInfo info) in Stations.Basic())
			{
				if (ItemPool.IsRandomizable(info.Item)) yield return new Requirement { Item = info.Item, Tier = info.Tier, Station = true, Placer = true, Critical = info.Critical };
			}
		}

		// The item guaranteed for a progression or themed station tile (one per tile: the Mythril Anvil stands for the
		// Orichalcum Anvil), 0 when none is needed: a protected placer keeps its vanilla sources (recipe, shop, drop). Chosen
		// by a fixed rule (vanilla first, then lowest tier, then lowest ID: a modded station has modded placers only): the
		// recipe plan can't be read here, it depends on these guarantees
		public static int StationItem(int tile)
		{
			Stations.StationInfo info = Stations.Info(tile);
			if (info.World || info.Kind is not (StationKind.Progression or StationKind.Thematic)) return 0;
			List<int> placers = Stations.Placers(tile);
			if (placers.Any(p => ItemPool.IsValid(p) && ItemPool.Info[p].Protected)) return 0;
			return placers.Where(p => ItemPool.IsRandomizable(p) && !ItemPool.IsBanned(p)).OrderBy(p => p < ItemID.Count ? 0 : 1).ThenBy(ItemPool.TierOf).ThenBy(p => p).FirstOrDefault();
		}

		// Progression stations (Hellforge, hardmode anvils and forges, Tinkerer's Workshop, Ancient Manipulator...) at their
		// tier, and themed ones at their item's true tier (the Flesh Cloning Vat is a hardmode station whatever its rarity
		// says), when unprotected: their recipe, shop or drop may be randomized away. With randomized recipes they are small
		// quests (Placer: a rebuilt recipe, one guardian); with recipes kept, their vanilla recipe may still need an item
		// randomized away, so they go back to the ordinary guarantee (several hosts of one biome: Assign)
		public static IEnumerable<Requirement> OtherStationRequirements()
		{
			bool quests = CraftingConfig.Instance.RandomizeRecipes;
			foreach (int tile in Stations.RequiredTiles().OrderBy(t => t))
			{
				int item = StationItem(tile);
				if (item <= 0) continue;
				Stations.StationInfo info = Stations.Info(tile);
				int tier = info.Kind == StationKind.Progression ? info.Tier : ItemPool.TrueTierOf(item);
				yield return new Requirement { Item = item, Tier = Tiers.Clamp(tier), Station = true, Placer = quests };
			}
		}

		// Every crafting station item with a guardian and a rebuilt recipe, at its tier (static: read by the recipe planner
		// before the guaranteed sources exist). None when recipes are not randomized
		public static IEnumerable<Requirement> PlacerRequirements() => StationRequirements().Concat(OtherStationRequirements()).Where(r => r.Placer);

		// Boss summons (Guide Voodoo Doll included; those found in the world too: Truffle Worm, Prismatic Lacewing,
		// Lihzahrd Power Cell, Gelatin Crystal, guaranteed as items) when unprotected, at the tier before their boss's; dropped
		// one at a time, as rarely as a station. Only the summon of the world's evil (both in drunk and zenith worlds:
		// BiomeSlots.CurrentEvil, from the synced world data). Other mods' summons at their own tier (Protection.SummonTier)
		public static IEnumerable<Requirement> SummonRequirements()
		{
			WorldEvil evil = BiomeSlots.CurrentEvil;
			// Modded summons too (at their estimated tier: Protection.ModdedSummonTierKnown, else protected and not randomizable)
			for (int type = 1; type < ItemPool.Info.Length; type++)
			{
				if (!Protection.IsBossSummon(type) || !ItemPool.IsRandomizable(type)) continue;
				if (type == ItemID.WormFood && evil == WorldEvil.Crimson || type == ItemID.BloodySpine && evil == WorldEvil.Corruption) continue;
				// The Truffle Worm on the glowing mushroom enemies when they can carry it, as in vanilla (Requirement.Biomes)
				SpawnBiome[] biomes = type == ItemID.TruffleWorm ? new[] { SpawnBiome.Mushroom, SpawnBiome.UndergroundMushroom } : null;
				yield return new Requirement { Item = type, Tier = Protection.SummonTier(type), Station = true, Biomes = biomes };
			}
		}

		// The progression tools a boss or a container gives in vanilla (Protection.ProgressionTools: the Pwnhammer from hardmode,
		// the Shadow Key after Skeletron, the Temple Key after Plantera) when unprotected: their boss's drop is permuted away,
		// or randomized, their chests and lock boxes randomized. Dropped one at a time, as rarely as a station, by the enemies
		// of their tier (hardmode biomes, the Dungeon...), never by those met only in the biome they open (ExcludedBiomeOf)
		public static IEnumerable<Requirement> ProgressionToolRequirements()
		{
			foreach ((int item, Core.Tier tier) in Protection.ProgressionTools)
			{
				if (ItemPool.IsRandomizable(item)) yield return new Requirement { Item = item, Tier = (int)tier, Station = true, ExcludedBiome = ExcludedBiomeOf(item) };
			}
		}

		// The biome keys (Jungle, Corruption, Crimson, Hallowed, Frozen and Desert), when unprotected: vanilla only gives them
		// as a 1 in 2500 global drop of hardmode enemies in their biome, no reliable source. Guaranteed from hardmode, like the
		// Shadow Key, on the hardmode enemies of their own biome when they can carry them (Requirement.Biomes; else any biome
		// of the tier). Only the keys of the evils this world has (both in drunk and zenith worlds: BiomeSlots.CurrentEvil),
		// whose Biome Chest the Dungeon holds (https://terraria.wiki.gg/wiki/Biome_Key)
		public static IEnumerable<Requirement> BiomeKeyRequirements()
		{
			WorldEvil evil = BiomeSlots.CurrentEvil;
			foreach ((int item, SpawnBiome[] biomes) in BiomeKeys)
			{
				if (!ItemPool.IsRandomizable(item)) continue;
				if (item == ItemID.CorruptionKey && evil == WorldEvil.Crimson || item == ItemID.CrimsonKey && evil == WorldEvil.Corruption) continue;
				yield return new Requirement { Item = item, Tier = (int)Core.Tier.Hardmode, Station = true, Biomes = biomes };
			}
		}

		public static readonly (int item, SpawnBiome[] biomes)[] BiomeKeys =
		{
			(ItemID.JungleKey, new[] { SpawnBiome.Jungle, SpawnBiome.UndergroundJungle }),
			(ItemID.CorruptionKey, new[] { SpawnBiome.Corruption, SpawnBiome.UndergroundCorruption }),
			(ItemID.CrimsonKey, new[] { SpawnBiome.Crimson, SpawnBiome.UndergroundCrimson }),
			(ItemID.HallowedKey, new[] { SpawnBiome.Hallow, SpawnBiome.UndergroundHallow }),
			(ItemID.FrozenKey, new[] { SpawnBiome.Snow, SpawnBiome.UndergroundSnow }),
			(ItemID.DungeonDesertKey, new[] { SpawnBiome.Desert, SpawnBiome.UndergroundDesert })
		};

		// Items a pot may also give, among those already unlocked: tools and early materials (never a station: each one is a
		// small quest, its recipe or its guardian; nor the summons)
		// Read per pot broken: cached per tier for the requirements it was built from. Do not modify the list
		public static List<int> PotItems(int unlockedTier)
		{
			List<Requirement> all = Requirements;
			if (potItemsFor != all)
			{
				potItems.Clear();
				potItemsFor = all;
			}
			if (potItems.TryGetValue(unlockedTier, out List<int> cached)) return cached;
			return potItems[unlockedTier] = all.Where(r => r.Tier <= unlockedTier && !r.Placer && (r.Tool || r.Tier == 0 && !r.Station)).Select(r => r.Item).ToList();
		}

		private static readonly Dictionary<int, List<int>> potItems = new();
		private static List<Requirement> potItemsFor;

		private static void Build()
		{
			if (byNpc != null) return;
			List<Requirement> all = CollectRequirements();
			Dictionary<int, List<PlannedDrop>> planned = Assign(all, SeedSystem.WorldSeed, out int missing, out Dictionary<int, Placement> places);
			byNpc = planned;
			placements = places;
			requirements = all;
			Missing = missing;
			if (missing > 0) ModContent.GetInstance<UltimateRandomizer>().Logger.Warn($"Guaranteed sources: {missing} item(s) without any host");
			HostLoad load = Load(planned, places);
			int below = all.Count(r => places.TryGetValue(r.Item, out Placement p) && p.Tier < Tiers.Clamp(r.Tier));
			List<string> shifted = all.Where(r => places.TryGetValue(r.Item, out Placement p) && p.Shifted)
				.Select(r => $"{Lang.GetItemNameValue(r.Item)} (tier {Tiers.Clamp(r.Tier)} on tier {places[r.Item].Tier})").ToList();
			string shiftLine = shifted.Count == 0 ? "" : $"Guaranteed sources: {shifted.Count} item(s) without hosts of their tier, placed on the closest tier: {string.Join(", ", shifted)}";
			if (shiftLine.Length > 0 && shiftLine != lastShiftLine) ModContent.GetInstance<UltimateRandomizer>().Logger.Info(shiftLine);
			lastShiftLine = shiftLine;
			string line = load.MaxItemsHost == 0 ? "" : $"Guaranteed load: at most {load.MaxItems} items on {Lang.GetNPCNameValue(load.MaxItemsHost)}, busiest "
				+ $"{Lang.GetNPCNameValue(load.MaxDropsHost)} with {load.MaxDrops:0.000} guaranteed drops per kill, {load.Forced} host(s) kept although full, "
				+ $"{below} item(s) placed below their tier, {places.Values.Count(p => p.Fallback)} critical station(s) on the Forest's critical hosts, "
				+ $"{places.Values.Count(p => p.Boss)} item(s) on bosses";
			// Once per plan of this world: the plan is made again after a test plans another seed, with the same result
			if (line.Length > 0 && line != lastLoadLine) ModContent.GetInstance<UltimateRandomizer>().Logger.Info(line);
			lastLoadLine = line;
			List<(int item, Placement place)> guarded = all.Where(r => places.TryGetValue(r.Item, out Placement p) && p.Guardian).Select(r => (r.Item, places[r.Item])).ToList();
			string guardLine = guarded.Count == 0 ? "" : $"Station guardians ({guarded.Count} stations, {guarded.Select(g => g.place.Hosts[0]).Distinct().Count()} different enemies, "
				+ $"{GuardianChance:P1} each): " + string.Join(", ", guarded.Select(g => $"{Lang.GetItemNameValue(g.item)} on {Lang.GetNPCNameValue(g.place.Hosts[0])}"
					+ (g.place.Biome >= 0 ? $" ({BiomeSlots.BiomeName(g.place.Biome)})" : g.place.Boss ? " (boss)" : "")));
			if (guardLine.Length > 0 && guardLine != lastGuardLine) ModContent.GetInstance<UltimateRandomizer>().Logger.Info(guardLine);
			lastGuardLine = guardLine;
		}

		private static string lastLoadLine;
		private static string lastGuardLine;
		private static string lastShiftLine;

		public struct HostLoad
		{
			// The most guaranteed items on one host, and that host
			public int MaxItems;
			public int MaxItemsHost;
			// The most guaranteed drops one kill of a host gives on average (the sum of its guaranteed chances), and that host
			public double MaxDrops;
			public int MaxDropsHost;
			// Hosts kept although full, over every placement
			public int Forced;
		}

		// How loaded the hosts of a plan are (log and tests)
		public static HostLoad Load(Dictionary<int, List<PlannedDrop>> plan, Dictionary<int, Placement> places)
		{
			HostLoad load = new();
			foreach ((int host, List<PlannedDrop> drops) in plan.OrderBy(pair => pair.Key))
			{
				int items = drops.Select(d => d.Item).Distinct().Count();
				double expected = drops.Sum(d => (double)d.Chance);
				if (items > load.MaxItems)
				{
					load.MaxItems = items;
					load.MaxItemsHost = host;
				}
				if (expected > load.MaxDrops)
				{
					load.MaxDrops = expected;
					load.MaxDropsHost = host;
				}
			}
			load.Forced = places.Values.Sum(p => p.Forced?.Count ?? 0);
			return load;
		}

		// Every requirement once: the earliest tier, keeping every flag (a pickaxe also needed as an ingredient stays a tool)
		private static List<Requirement> CollectRequirements()
		{
			List<Requirement> all = StationRequirements().Concat(OtherStationRequirements()).Concat(SummonRequirements()).Concat(ProgressionToolRequirements())
				.Concat(BiomeKeyRequirements()).ToList();
			foreach (Func<IEnumerable<Requirement>> provider in ExtraRequirements) all.AddRange(provider());
			// A hard gate (no allowance) is never required before its true tier
			return all.GroupBy(r => r.Item).Select(g => new Requirement
			{
				Item = g.Key, Tier = ItemPool.IsHardGate(g.Key) ? Math.Max(g.Min(r => r.Tier), ItemPool.TrueTierOf(g.Key)) : g.Min(r => r.Tier), Station = g.Any(r => r.Station), Placer = g.Any(r => r.Placer),
				Critical = g.Any(r => r.Critical), Tool = g.Any(r => r.Tool),
				ExcludedBiome = ExcludedBiomeOf(g.Key), Biomes = g.Select(r => r.Biomes).FirstOrDefault(b => b != null)
			}).OrderBy(r => r.Tier).ThenBy(r => DetRandom.ItemKey(r.Item)).ToList();
		}

		// Who picks its biome first (biomes holding fewer items are preferred): the critical stations, the other basic
		// stations, the pickaxes, the materials, the progression and themed stations, the summons, then the biome keys (added
		// later: last, so that the hosts of everything else stay as they were)
		private static int Priority(Requirement r)
		{
			if (r.Biomes != null) return 6;
			// The work bench first: it takes the Forest enemy the early kills meet most (PlaceGuardian)
			if (r.Critical && r.Item == ItemID.WorkBench) return -1;
			if (r.Critical) return 0;
			if (r.Station && Stations.Basic().Any(s => s.info.Item == r.Item)) return 1;
			if (r.Tool) return 2;
			if (!r.Station) return 3;
			return Protection.IsBossSummon(r.Item) ? 5 : 4;
		}

		// How loaded the hosts are while a plan is made: guaranteed items and the single drops (stations, summons, tools) one
		// kill gives, before GuaranteedDropMultiplier
		private sealed class HostLoads
		{
			public readonly Dictionary<int, int> Items = new();
			public readonly Dictionary<int, double> Single = new();
			// Hosts kept once although full (never a second time)
			public readonly HashSet<int> Forced = new();

			public bool Full(int npc) => Items.GetValueOrDefault(npc) >= MaxItemsPerHost || Single.GetValueOrDefault(npc) >= MaxHostSingleChance - 1e-9;

			// Beyond even what a full host may take when its tier has no other way (ChooseHosts with force)
			public bool Overloaded(int npc) => Items.GetValueOrDefault(npc) >= MaxItemsPerHost + 1 || Single.GetValueOrDefault(npc) >= MaxForcedSingleChance - 1e-9;

			public void Add(int npc, bool single, double chance)
			{
				Items[npc] = Items.GetValueOrDefault(npc) + 1;
				if (single) Single[npc] = Single.GetValueOrDefault(npc) + chance;
			}
		}

		// The hosts of each requirement with a world seed and the current spawn map (the tests plan other seeds with
		// BiomeSlots.WithMap): one biome per item at the highest tier at or below its own that has a qualifying biome
		// (BiomeCandidates, ChooseHosts), picked at random among them, less likely the more items it already holds
		// (BiomeWeight), so that the items of a tier spread over its biomes. Full hosts (HostLoads) are left aside: they are
		// kept (never Overloaded) only when no biome of the tier works without them. One chance on every host (PeriodChances).
		// Crafting stations (Placer: randomized recipes) go to their single guardian (PlaceGuardian). Without any biome, common hosts of the tier; without any natural enemy of the tier (nothing
		// spawns naturally after Golem), the progression bosses of the tier (BossHosts: Golem, Lunatic Cultist, Moon Lord).
		// An item is never placed below its tier: until 1.5.0 it went to lower hosts, and the Celestial Sigil and the Ancient
		// Manipulator dropped from post-Plantera enemies. Only when nothing of its tier can carry it (Shifted): an ordinary item
		// goes to the closest lower tier's common hosts, a hard gate to the next tier with hosts
		public static Dictionary<int, List<PlannedDrop>> Assign(List<Requirement> all, int seed, out int missing, out Dictionary<int, Placement> places)
		{
			missing = 0;
			places = new Dictionary<int, Placement>();
			Dictionary<int, List<PlannedDrop>> result = new();
			ProgressionConfig cfg = ProgressionConfig.Instance;
			int perItem = HostLimit;
			float multiplier = cfg.GuaranteedDropMultiplier;
			Dictionary<int, List<BiomeEligible>> eligibleByTier = new();
			List<BiomeEligible> Eligible(int t)
			{
				if (!eligibleByTier.TryGetValue(t, out List<BiomeEligible> eligible)) eligibleByTier[t] = eligible = BiomeCandidates(t);
				return eligible;
			}
			Dictionary<int, int> biomeItems = new();
			HostLoads load = new();
			List<int>[] common = new List<int>[Tiers.Count];
			for (int t = 0; t < Tiers.Count; t++) common[t] = Hosts(t);
			// How many stations each guardian already carries (distinct guardians while others are left)
			Dictionary<int, int> guardians = new();
			foreach (Requirement requirement in all.OrderBy(Priority).ThenBy(r => r.Tier).ThenBy(r => DetRandom.ItemKey(r.Item)))
			{
				Random rng = new(DetRandom.Hash(seed, RollKey.Guaranteed, DetRandom.ItemKey(requirement.Item)));
				if (requirement.Placer)
				{
					Placement guarded = PlaceGuardian(requirement, rng, Eligible, common, load, biomeItems, guardians);
					if (guarded.Hosts == null || guarded.Hosts.Count == 0)
					{
						missing++;
						continue;
					}
					int guardian = guarded.Hosts[0];
					float guardChance = guarded.Boss ? 1f : GuardianChance;
					if (!result.TryGetValue(guardian, out List<PlannedDrop> guardDrops)) result[guardian] = guardDrops = new List<PlannedDrop>();
					guardDrops.Add(new PlannedDrop { Item = requirement.Item, StackMin = 1, StackMax = 1, Chance = guardChance, Extra = true, Guaranteed = true, Single = true, Guardian = true });
					// Its nominal weight, never the live setting (NominalGuardianChance)
					if (!guarded.Boss) load.Add(guardian, true, NominalGuardianChance);
					guardians[guardian] = guardians.GetValueOrDefault(guardian) + 1;
					places[requirement.Item] = guarded;
					if (guarded.Biome >= 0) biomeItems[guarded.Biome] = biomeItems.GetValueOrDefault(guarded.Biome) + 1;
					continue;
				}
				bool single = requirement.Station || requirement.Tool;
				// Never in the biome it opens, nor on the enemies met only there (the Temple Key)
				SpawnBiome? excluded = requirement.ExcludedBiome;
				bool Excluded(int host) => excluded is SpawnBiome biome && OnlyIn(host, biome);
				Placement place = new() { Biome = -1, Tier = Tiers.Clamp(requirement.Tier), Time = SlotTime.Any, Forced = new List<int>() };
				List<int> chosen = null;
				List<float> chances = null;
				BiomeHosts? picked = null;
				{
					// A biome of the item's own tier, never a lower one (the guaranteed drop has no progression lock); one of its
					// own biomes first (Requirement.Biomes), else any
					int t = Tiers.Clamp(requirement.Tier);
					List<BiomeHosts> candidates = new();
					foreach (bool own in requirement.Biomes != null ? new[] { true, false } : new[] { false })
					{
						List<BiomeEligible> eligible = Eligible(t).Where(b => (excluded is not SpawnBiome biome || b.Biome != (int)biome)
							&& (!own || requirement.Biomes.Any(x => (int)x == b.Biome))).ToList();
						candidates = eligible.Select(b => ChooseHosts(b, perItem, load, false)).Where(c => c != null).Select(c => c.Value).ToList();
						// Full hosts only when no biome of the tier works without them
						if (candidates.Count == 0) candidates = eligible.Select(b => ChooseHosts(b, perItem, load, true)).Where(c => c != null).Select(c => c.Value).ToList();
						if (candidates.Count > 0) break;
					}
					if (candidates.Count > 0)
					{
						int index = PickWeighted(Enumerable.Range(0, candidates.Count).ToList(), i => BiomeWeight(biomeItems.GetValueOrDefault(candidates[i].Biome)), rng);
						picked = candidates[index];
						place.Tier = t;
						chances = PeriodChances(picked.Value, single);
					}
				}
				if (picked != null)
				{
					BiomeHosts hosts = picked.Value;
					chosen = hosts.Hosts;
					place.Biome = hosts.Biome;
					place.Time = hosts.Time;
					place.HostTimes = hosts.HostTimes;
					place.Forced = hosts.Forced;
				}
				bool boss = false;
				// No biome: common hosts of a tier, the total chance split between them (the hosts not full yet first)
				bool UseCommon(int t)
				{
					List<int> open = common[t].Where(h => !Excluded(h)).ToList();
					if (open.Count == 0) return false;
					chosen = open.Select(h => (h, roll: rng.Next())).OrderBy(x => load.Full(x.h) ? 1 : 0).ThenBy(x => x.roll)
						.Select(x => x.h).Take(perItem).ToList();
					place.Forced = chosen.Where(load.Full).ToList();
					float chance = (single ? UnknownShareStationChance : UnknownShareMaterialChance) * UnknownShareHosts / chosen.Count;
					chances = chosen.Select(_ => chance).ToList();
					place.Tier = t;
					return true;
				}
				// No natural enemy of a tier: its progression bosses, at every kill (a boss is fought a few times only).
				// The Lunatic Cultist is also where the Ancient Manipulator comes from in vanilla
				bool UseBosses(int t)
				{
					List<int> bosses = BossHosts(t).Where(b => !Excluded(b)).ToList();
					if (bosses.Count == 0) return false;
					chosen = bosses;
					chances = bosses.Select(_ => 1f).ToList();
					place.Tier = t;
					boss = place.Boss = true;
					return true;
				}
				if (chosen == null)
				{
					int t = Tiers.Clamp(requirement.Tier);
					if (!UseCommon(t)) UseBosses(t);
				}
				if (chosen == null)
				{
					// Last resort (no natural enemy nor progression boss of the tier: tier 2 has no boss). A hard gate goes up
					// to the next tier with hosts (it has no allowance: never early); anything else to the closest lower tier's
					// common hosts (an early item beats a missing one, the guaranteed drop has no progression lock)
					bool gate = ItemPool.IsHardGate(requirement.Item);
					int from = Tiers.Clamp(requirement.Tier);
					for (int step = 1; step < Tiers.Count && chosen == null; step++)
					{
						int t = gate ? from + step : from - step;
						if (t < 0 || t >= Tiers.Count) break;
						if (!UseCommon(t) && gate) UseBosses(t);
					}
					if (chosen != null) place.Shifted = true;
				}
				if (chosen == null || chosen.Count == 0)
				{
					missing++;
					continue;
				}
				(int min, int max) = single ? (1, 1) : StackRules.Range(requirement.Item);
				for (int i = 0; i < chosen.Count; i++)
				{
					int host = chosen[i];
					float baseChance = chances[i];
					// A boss gives it at every kill, whatever GuaranteedDropMultiplier says; bosses are no hosts of HostLoads
					float chance = boss ? 1f : Math.Min(1f, baseChance * multiplier);
					if (single) chance = Math.Max(MinStationChance, chance);
					if (!result.TryGetValue(host, out List<PlannedDrop> drops)) result[host] = drops = new List<PlannedDrop>();
					drops.Add(new PlannedDrop { Item = requirement.Item, StackMin = min, StackMax = Math.Max(min, max), Chance = chance, Extra = true, Guaranteed = true, Single = single });
					if (!boss) load.Add(host, single, baseChance);
				}
				load.Forced.UnionWith(place.Forced);
				place.Hosts = chosen;
				place.HostTimes ??= chosen.Select(_ => SlotTime.Any).ToList();
				places[requirement.Item] = place;
				if (place.Biome >= 0) biomeItems[place.Biome] = biomeItems.GetValueOrDefault(place.Biome) + 1;
			}
			return result;
		}

		// The guardian of a crafting station: exactly one common natural enemy of its tier (never rare, never an event one),
		// met steadily in a biome open at that tier (BiomeCandidates), one that guards no other station while others are left,
		// then one not full yet. The biome is drawn like the other items' (BiomeWeight: the stations of a tier spread over its
		// biomes), the enemy by how often it is met there, never under MinGuardianShare of the biome's spawns in the period(s)
		// kept for it. Critical stations (work bench, furnace, anvil, table, chair) stay in the early biomes of the pacing
		// model, on an enemy of their early slots (the work bench in the Forest); without one, the Forest's critical enemies. Without any biome, a common enemy of the tier; without natural enemies (late tiers),
		// a progression boss of the tier; with neither, the closest tier as in Assign (Shifted). Hosts null: none at all
		private static Placement PlaceGuardian(Requirement requirement, Random rng, Func<int, List<BiomeEligible>> eligible, List<int>[] common, HostLoads load,
			Dictionary<int, int> biomeItems, Dictionary<int, int> guardians)
		{
			int tier = Tiers.Clamp(requirement.Tier);
			Placement place = new() { Biome = -1, Tier = tier, Time = SlotTime.Any, Forced = new List<int>(), Guardian = true };
			SpawnBiome? excluded = requirement.ExcludedBiome;
			bool Excluded(int host) => excluded is SpawnBiome biome && OnlyIn(host, biome);
			// Hosts guarding no station first, then the hosts not full yet
			int Rank(int host) => guardians.GetValueOrDefault(host) * 2 + (load.Full(host) ? 1 : 0);
			// Never an enemy this world never spawns (only in the absent evil's slots): the biome options read the spawn shares,
			// the fallbacks below too
			List<int> Best(IEnumerable<int> hosts)
			{
				List<int> list = hosts.Where(h => !Excluded(h) && !BiomeSlots.NeverSpawns(BiomeSlots.TypeOf(h))).Distinct().ToList();
				if (list.Count == 0) return list;
				int best = list.Min(Rank);
				return list.Where(h => Rank(h) == best).ToList();
			}
			void Take(int host, int t, SlotTime? time = null)
			{
				place.Hosts = new List<int> { host };
				place.HostTimes = new List<SlotTime> { time ?? (place.Biome >= 0 ? TimeIn(place.Biome, host, t) : SlotTime.Any) };
				place.Time = place.HostTimes[0];
				place.Tier = t;
				if (load.Full(host)) place.Forced.Add(host);
			}
			// A biome of the station's tier (never a lower one: the guaranteed drop has no progression lock)
			List<BiomeEligible> biomes = eligible(tier).Where(b => excluded is not SpawnBiome x || b.Biome != (int)x).ToList();
			bool critical = requirement.Critical && BiomeSlots.SpawnsPlanned;
			// The hosts a biome may give, each met steadily (MinGuardianShare) in the period(s) kept for it: for a critical
			// station, those of its early slots the early kills meet. When the distinct guardians run out, a frequent enemy
			// guards a second station rather than a rare one the first
			Dictionary<int, List<(int npc, double weight, SlotTime time)>> options = new();
			foreach (BiomeEligible biome in biomes)
			{
				HashSet<int> early = null, slots = null;
				if (critical)
				{
					early = requirement.Item == ItemID.WorkBench ? new HashSet<int> { (int)SpawnBiome.Forest } : PacingModel.EarlyBiomes.Select(b => (int)b).ToHashSet();
					if (!early.Contains(biome.Biome)) continue;
					slots = EarlySlotHosts(biome.Biome);
				}
				List<(int npc, double weight, SlotTime time)> hosts = new();
				foreach ((int npc, double day, double night) h in biome.Hosts)
				{
					if (Excluded(h.npc) || GuardianTime(h.day, h.night) is not SlotTime time) continue;
					if (critical && !slots.Contains(h.npc)) continue;
					double weight = critical ? PacingModel.EarlyKillShare(k => k == h.npc) : h.day + h.night;
					if (weight > 0) hosts.Add((h.npc, weight, time));
				}
				if (hosts.Count > 0) options[biome.Biome] = hosts;
			}
			if (options.Count > 0)
			{
				int best = options.Values.SelectMany(h => h).Min(h => Rank(h.npc));
				List<int> open = options.Keys.Where(b => options[b].Any(h => Rank(h.npc) == best)).OrderBy(b => b).ToList();
				int biome = PickWeighted(open, b => (critical ? PacingModel.EarlyBiomeShare((SpawnBiome)b) : 1) * BiomeWeight(biomeItems.GetValueOrDefault(b)), rng);
				List<(int npc, double weight, SlotTime time)> hosts = options[biome].Where(h => Rank(h.npc) == best).ToList();
				// The work bench: the enemy the early kills meet most (its backup must not hang on a rare zombie kind)
				int host = requirement.Item == ItemID.WorkBench && critical ? hosts.OrderByDescending(h => h.weight).ThenBy(h => DetRandom.NpcKey(h.npc)).First().npc
					: PickWeighted(hosts.Select(h => h.npc).ToList(), n => hosts.First(h => h.npc == n).weight, rng);
				place.Biome = biome;
				Take(host, tier, hosts.First(h => h.npc == host).time);
				return place;
			}
			// A critical station without an early biome (or without planned spawns): one of the Forest's critical enemies
			if (requirement.Critical)
			{
				List<int> forest = Best(CriticalForestHosts(tier));
				if (forest.Count > 0)
				{
					place.Biome = BiomeSlots.SpawnsPlanned ? (int)SpawnBiome.Forest : -1;
					place.Fallback = true;
					Take(PickWeighted(forest, n => PacingModel.EarlyKillShare(k => k == n), rng), 0);
					return place;
				}
			}
			bool UseCommon(int t)
			{
				List<int> open = Best(common[t]);
				if (open.Count == 0) return false;
				Take(open[rng.Next(open.Count)], t);
				return true;
			}
			bool UseBosses(int t)
			{
				List<int> bosses = Best(BossHosts(t));
				if (bosses.Count == 0) return false;
				Take(bosses[rng.Next(bosses.Count)], t);
				place.Boss = true;
				return true;
			}
			if (UseCommon(tier) || UseBosses(tier)) return place;
			// Last resort, as in Assign: a hard gate up to the next tier with hosts, anything else down
			bool gate = ItemPool.IsHardGate(requirement.Item);
			for (int step = 1; step < Tiers.Count; step++)
			{
				int t = gate ? tier + step : tier - step;
				if (t < 0 || t >= Tiers.Count) break;
				if (UseCommon(t) || gate && UseBosses(t))
				{
					place.Shifted = true;
					return place;
				}
			}
			return place;
		}

		// The enemies spawning in the slots of a biome's early kills (PacingModel.EarlyOriginals) in the current spawn map
		private static HashSet<int> EarlySlotHosts(int biome)
		{
			if (biome >= BiomeSlots.ModdedBiomeStart) return new HashSet<int>();
			HashSet<int> early = PacingModel.EarlyOriginals((SpawnBiome)biome).ToHashSet();
			return BiomeSlots.SpawnMap.Slots.Where(s => s.Biome == biome && early.Contains(s.Original) && !s.AbsentEvil).SelectMany(BiomeSlots.Spawners).Select(BiomeSlots.TypeOf).ToHashSet();
		}

		// The tier from which the guaranteed hosts of a requirement give it (Findability.Never: none): a natural host from the
		// tier it is met at (its drop has no progression lock), a boss host from the requirement's tier at the earliest (its
		// drop waits for the tier: the Twins give it once the last one dies)
		public static int FoundTier(Requirement requirement)
		{
			bool boss = TryPlacement(requirement.Item, out Placement place) && place.Boss;
			int found = Findability.Never;
			foreach (int host in HostsOf(requirement.Item))
			{
				// Met only in the biome the item opens: no way in (Assign never picks one, a test plan's map might)
				if (requirement.ExcludedBiome is SpawnBiome biome && OnlyIn(host, biome)) continue;
				int from = boss ? Math.Max(HostTier(host), Tiers.Clamp(requirement.Tier)) : HostTier(host);
				found = Math.Min(found, from);
			}
			return found;
		}

		// The bosses every world must beat to reach a tier, which drop the loot themselves (the Twins: the last one alive,
		// RandomNpcDropRule.ExtraDropsAllowedNow). Hosts of the items no natural enemy of their tier can carry: from Golem on
		// nothing spawns naturally (Martian, pillar and moon enemies are event enemies). Not the evil boss (the Eater of Worlds
		// drops from whichever segment dies last) nor the optional ones (Duke Fishron, Empress of Light, Martian Saucer...)
		private static readonly Dictionary<int, int[]> ProgressionBosses = new()
		{
			[(int)Core.Tier.PostEyeOfCthulhu] = new int[] { NPCID.EyeofCthulhu },
			[(int)Core.Tier.PostSkeletron] = new int[] { NPCID.SkeletronHead },
			[(int)Core.Tier.Hardmode] = new int[] { NPCID.WallofFlesh },
			[(int)Core.Tier.PostMechBoss] = new int[] { NPCID.TheDestroyer, NPCID.Retinazer, NPCID.Spazmatism, NPCID.SkeletronPrime },
			[(int)Core.Tier.PostPlantera] = new int[] { NPCID.Plantera },
			[(int)Core.Tier.PostGolem] = new int[] { NPCID.Golem },
			[(int)Core.Tier.PostCultist] = new int[] { NPCID.CultistBoss },
			[(int)Core.Tier.PostMoonLord] = new int[] { NPCID.MoonLordCore }
		};

		// The progression bosses of exactly this tier (none for tiers 0 and 2)
		public static List<int> BossHosts(int tier) => ProgressionBosses.TryGetValue(Tiers.Clamp(tier), out int[] bosses) ? bosses.ToList() : new List<int>();

		// A host is full with this many guaranteed items, or once one kill gives MaxHostSingleChance stations, summons or tools
		// on average (before GuaranteedDropMultiplier): it takes no more while another host or biome of the tier works
		public const int MaxItemsPerHost = 4;
		public const double MaxHostSingleChance = 0.25;
		// A full host is still taken when no biome of the item's tier works without it (rather than placing the item a tier
		// early), up to one more item and this many single drops per kill
		public const double MaxForcedSingleChance = 0.4;
		// A biome met by day and by night gives each period at least this many hosts (when it has them)
		public const int MinPeriodHosts = 2;

		// The tier from which a biome's enemies may carry guaranteed items (-1: never): the biomes a new character explores
		// from the start (BiomeSlots.StartBiomes), the jungle and the sky islands after the Eye, the mushroom, granite and
		// marble caves, the spider nests and the Underworld after the evil boss, the Dungeon after Skeletron, the Hallow in
		// hardmode, the Temple after Plantera; never the Graveyard nor the Meteor (made by the player or an event). Modded
		// biomes follow their hosts' tier
		public static int AllowedTier(int biome)
		{
			if (biome >= BiomeSlots.ModdedBiomeStart || BiomeSlots.IsStartBiome(biome)) return 0;
			return (SpawnBiome)biome switch
			{
				SpawnBiome.Jungle or SpawnBiome.UndergroundJungle or SpawnBiome.Sky => (int)Core.Tier.PostEyeOfCthulhu,
				SpawnBiome.Mushroom or SpawnBiome.UndergroundMushroom or SpawnBiome.Granite or SpawnBiome.Marble or SpawnBiome.SpiderNest
					or SpawnBiome.Underworld => (int)Core.Tier.PostEvilBoss,
				SpawnBiome.Dungeon => (int)Core.Tier.PostSkeletron,
				SpawnBiome.Hallow or SpawnBiome.UndergroundHallow => (int)Core.Tier.Hardmode,
				SpawnBiome.Temple => (int)Core.Tier.PostPlantera,
				_ => -1
			};
		}

		private struct BiomeEligible
		{
			public int Biome;
			// The natural hosts met steadily there, the most met first, with their share of its spawns by day and by night
			public List<(int npc, double day, double night)> Hosts;
		}

		private struct BiomeHosts
		{
			public int Biome;
			public List<int> Hosts;
			public List<SlotTime> HostTimes;
			public List<int> Forced;
			public SlotTime Time;
			// Steady share of the biome's spawns the hosts make together in each period (0: the item is not met then)
			public double DayShare;
			public double NightShare;
		}

		private static bool InDay((int npc, double day, double night) h) => h.day >= MinBiomeHostShare;

		private static bool InNight((int npc, double day, double night) h) => h.night >= MinBiomeHostShare;

		// The period(s) a guardian with these steady shares is met in enough (MinGuardianShare), null when neither
		public static SlotTime? GuardianTime(double day, double night)
		{
			bool d = day >= MinGuardianShare - 1e-9, n = night >= MinGuardianShare - 1e-9;
			return d && n ? SlotTime.Any : d ? SlotTime.Day : n ? SlotTime.Night : null;
		}

		private static SlotTime TimeOf(double day, double night) => day > 0 && night > 0 ? SlotTime.Any : day > 0 ? SlotTime.Day : night > 0 ? SlotTime.Night : SlotTime.Any;

		// When a type is met in a biome at a tier, from its steady slots there
		public static SlotTime TimeIn(int biome, int type, int tier) =>
			TimeOf(BiomeSlots.SteadyShareAt(biome, type, tier, SlotTime.Day), BiomeSlots.SteadyShareAt(biome, type, tier, SlotTime.Night));

		// The biomes that may carry the items of a tier: open at that tier (AllowedTier), with at least MinBiomeHosts natural
		// hosts of exactly that tier met steadily there through slots of that tier or below (BiomeSlots.SteadyShareAt), the
		// most met first. Never the enemies of the tier before: the guaranteed drop has no progression lock, and until 1.5.1 a
		// 200 pickaxe guaranteed after the mechanical bosses dropped from hardmode enemies before them
		private static List<BiomeEligible> BiomeCandidates(int tier)
		{
			List<BiomeEligible> result = new();
			if (!BiomeSlots.SpawnsPlanned) return result;
			List<int> natural = NaturalHosts(tier).Where(n => !(ContentSamples.NpcsByNetId.TryGetValue(n, out NPC sample) && sample.rarity > 0)).ToList();
			if (natural.Count < MinBiomeHosts) return result;
			foreach (int biome in BiomeSlots.BiomesAt(tier))
			{
				int allowed = AllowedTier(biome);
				if (allowed < 0 || allowed > tier) continue;
				List<(int npc, double day, double night)> eligible = natural
					.Select(n => (npc: n, day: BiomeSlots.SteadyShareAt(biome, n, tier, SlotTime.Day), night: BiomeSlots.SteadyShareAt(biome, n, tier, SlotTime.Night)))
					.Where(h => InDay(h) || InNight(h)).OrderByDescending(h => h.day + h.night).ThenBy(h => DetRandom.NpcKey(h.npc)).ToList();
				if (eligible.Count < MinBiomeHosts) continue;
				result.Add(new BiomeEligible { Biome = biome, Hosts = eligible });
			}
			return result;
		}

		// The hosts of one item in a biome (null when the biome doesn't qualify). Full hosts are left aside, or with force kept
		// (the least loaded first, never an Overloaded one) while too few others remain (MinHostsInBiome, and MinPeriodHosts in each period); then each period gets
		// its MinPeriodHosts hosts first (a host met at any hour counts for both), the rest goes to the most met. A period
		// without MinPeriodHosts hosts, or whose hosts make less than MinBiomeShare of its spawns, is given up (the item is
		// only met in the other one)
		private static BiomeHosts? ChooseHosts(BiomeEligible biome, int perItem, HostLoads load, bool force)
		{
			List<(int npc, double day, double night)> all = biome.Hosts;
			int dayAll = all.Count(InDay), nightAll = all.Count(InNight);
			List<(int npc, double day, double night)> free = all.Where(h => !load.Full(h.npc)).ToList();
			List<int> forced = new();
			if (force)
			{
				foreach ((int npc, double day, double night) h in all.Where(h => load.Full(h.npc) && !load.Overloaded(h.npc))
					.OrderBy(h => load.Items.GetValueOrDefault(h.npc)).ThenBy(h => load.Single.GetValueOrDefault(h.npc)).ThenBy(h => all.IndexOf(h)).ToList())
				{
					bool needed = free.Count < MinHostsInBiome || InDay(h) && free.Count(InDay) < Math.Min(MinPeriodHosts, dayAll)
						|| InNight(h) && free.Count(InNight) < Math.Min(MinPeriodHosts, nightAll);
					if (!needed) continue;
					free.Add(h);
					forced.Add(h.npc);
				}
				free = all.Where(free.Contains).ToList();
			}
			// A period counts with at least MinPeriodHosts hosts (one of them has them: every host is met in one)
			bool wantDay = free.Count(InDay) >= MinPeriodHosts, wantNight = free.Count(InNight) >= MinPeriodHosts;
			if (!wantDay && !wantNight) return null;
			// Both periods, then the one that worked alone
			for (int attempt = 0; attempt < 2; attempt++)
			{
				// One period only: its hosts only
				List<(int npc, double day, double night)> options = wantDay && wantNight ? free : free.Where(h => wantDay ? InDay(h) : InNight(h)).ToList();
				if (options.Count < MinHostsInBiome) return null;
				List<(int npc, double day, double night)> chosen = Pick(options, perItem, wantDay ? Math.Min(MinPeriodHosts, options.Count(InDay)) : 0,
					wantNight ? Math.Min(MinPeriodHosts, options.Count(InNight)) : 0);
				double day = chosen.Sum(h => h.day), night = chosen.Sum(h => h.night);
				bool dayOk = wantDay && day >= MinBiomeShare, nightOk = wantNight && night >= MinBiomeShare;
				if (!dayOk && !nightOk) return null;
				if (dayOk == wantDay && nightOk == wantNight)
				{
					return new BiomeHosts
					{
						Biome = biome.Biome,
						Hosts = chosen.Select(h => h.npc).ToList(),
						HostTimes = chosen.Select(h => TimeOf(h.day, h.night)).ToList(),
						Forced = forced.Where(f => chosen.Any(h => h.npc == f)).ToList(),
						Time = dayOk && nightOk ? SlotTime.Any : dayOk ? SlotTime.Day : SlotTime.Night,
						DayShare = dayOk ? day : 0,
						NightShare = nightOk ? night : 0
					};
				}
				// A period too thin: the item goes to the other one only
				wantDay = dayOk;
				wantNight = nightOk;
			}
			return null;
		}

		// Up to perItem hosts, in the order given (most met first): the hosts each period needs first, then the most met
		private static List<(int npc, double day, double night)> Pick(List<(int npc, double day, double night)> free, int perItem, int needDay, int needNight)
		{
			List<(int npc, double day, double night)> chosen = new();
			while (chosen.Count < perItem)
			{
				int dayShort = needDay - chosen.Count(InDay), nightShort = needNight - chosen.Count(InNight);
				int best = -1, bestScore = -1;
				for (int i = 0; i < free.Count; i++)
				{
					if (chosen.Contains(free[i])) continue;
					int score = (dayShort > 0 && InDay(free[i]) ? 1 : 0) + (nightShort > 0 && InNight(free[i]) ? 1 : 0);
					if (score > bestScore)
					{
						best = i;
						bestScore = score;
					}
				}
				if (best < 0) break;
				chosen.Add(free[best]);
			}
			// Back in the most met first order
			return free.Where(chosen.Contains).ToList();
		}

		// The chance on each host (before GuaranteedDropMultiplier): the same on all, so that the hints name one chance, from
		// the period where the hosts make the smallest share (each period kept gives the item within its target; the richer
		// one a little sooner). HostLoads keeps the hosts from piling such chances up
		private static List<float> PeriodChances(BiomeHosts hosts, bool single)
		{
			double share = hosts.DayShare > 0 && hosts.NightShare > 0 ? Math.Min(hosts.DayShare, hosts.NightShare) : Math.Max(hosts.DayShare, hosts.NightShare);
			float chance = BiomeChance(single, share);
			return hosts.Hosts.Select(_ => chance).ToList();
		}

		// How likely a biome is to be picked for an item among those that qualify: about the same for all, much less once it
		// holds items, so that the items of a tier land in many different biomes
		private static double BiomeWeight(int items) => 1.0 / ((1 + items) * (1 + items));

		// How many biomes may carry the items of a tier in this world, hosts not loaded yet (tests)
		public static int CandidateBiomes(int tier) => CandidateBiomeList(tier).Count;

		// The enemies that may guard a station of a tier: met steadily enough (GuardianTime) in a biome open at that tier
		// (PlaceGuardian's options; a tier without any keeps none). excluded: the station's ExcludedBiome (neither that biome
		// nor an enemy met only there)
		public static HashSet<int> GuardianCandidates(int tier, SpawnBiome? excluded = null) =>
			BiomeCandidates(Tiers.Clamp(tier)).Where(b => excluded is not SpawnBiome x || b.Biome != (int)x).SelectMany(b => b.Hosts)
				.Where(h => GuardianTime(h.day, h.night) != null && !(excluded is SpawnBiome x && OnlyIn(h.npc, x))).Select(h => h.npc).ToHashSet();

		// The biomes that may carry the items of a tier in the current spawn map, whatever the hosts' load (tests)
		public static List<int> CandidateBiomeList(int tier) => BiomeCandidates(tier).Where(b => ChooseHosts(b, HostLimit, new HostLoads(), false) != null).Select(b => b.Biome).ToList();

		// The chance on each host of a biome whose hosts make this share of its spawns (before GuaranteedDropMultiplier)
		public static float BiomeChance(bool single, double share)
		{
			if (share <= 0) return single ? UnknownShareStationChance : UnknownShareMaterialChance;
			return single
				? (float)Math.Clamp(1 / (ProgressionConfig.Instance.StationFindKills * share), MinStationChance, MaxStationChance)
				: (float)Math.Clamp(1 / (MaterialTargetKills * share), MaterialChance, MaxMaterialChance);
		}

		// Every enemy of the Forest slots of the day slimes and of the night zombies and demon eyes that may carry a tier's items
		private static List<int> CriticalForestHosts(int tier)
		{
			HashSet<int> set = new();
			set.UnionWith(BiomeSlots.ForestFamilyHosts(BiomeSlots.SpawnMap, true));
			set.UnionWith(BiomeSlots.ForestFamilyHosts(BiomeSlots.SpawnMap, false));
			return set.Where(n => IsNaturalHost(n, tier) && NpcTiers.BaseTier(n) <= tier).OrderByDescending(n => PacingModel.EarlyKillShare(k => k == n))
				.ThenBy(DetRandom.NpcKey).ToList();
		}

		// A random candidate with these weights (the list is in a fixed order)
		private static int PickWeighted(List<int> candidates, Func<int, double> weight, Random rng)
		{
			double[] weights = candidates.Select(n => Math.Max(0, weight(n))).ToArray();
			double total = weights.Sum();
			if (total <= 0) return candidates[rng.Next(candidates.Count)];
			double roll = rng.NextDouble() * total;
			for (int i = 0; i < candidates.Count; i++)
			{
				roll -= weights[i];
				if (roll < 0) return candidates[i];
			}
			return candidates[^1];
		}

		// Minutes of the pacing model's early kills before a critical station drops (infinity: none of them meets its hosts)
		public static double FindMinutes(int item)
		{
			List<int> hosts = HostsOf(item).ToList();
			double rate = 0;
			foreach (int host in hosts)
			{
				float chance = For(host).Where(d => d.Item == item).Sum(d => d.Chance);
				rate += PacingModel.KillsPerMinute * PacingModel.EarlyKillShare(k => k == host) * chance;
			}
			return rate > 0 ? 1 / rate : double.PositiveInfinity;
		}

		// Plans the hosts again with another seed and its spawn map, without touching this world's (tests)
		public static Dictionary<int, List<PlannedDrop>> PlanWith(int seed, BiomeSlotMap map, out int missing, out Dictionary<int, Placement> places)
		{
			Build();
			List<Requirement> all = requirements;
			// The share caches follow the spawn map: they are rebuilt for the other map, then for this world's again
			using (BiomeSlots.WithMap(map)) return Assign(all, seed, out missing, out places);
		}

		// Critical stations (work bench, furnace, anvil, table, chair) are carried by the enemies met most in the first hour:
		// slimes by day (every colour shares the Blue Slime's type), zombies and demon eyes of every kind by night (each kind
		// has its own Forest slot: vanilla picks one at random), from the Forest slots (redistributed by biome, else the vanilla
		// ones). Other hosts are judged by their spawn share

		// The caches below follow the spawn map (rebuilt when the world evil arrives, the settings change, or a test plans
		// another map)
		private static void FollowMap()
		{
			BiomeSlotMap map = BiomeSlots.SpawnMap;
			if (frequentMap == map) return;
			// Another map than the one the hosts were planned with (the world evil arrived, a test planned another seed):
			// the hosts and the tiers found through them are planned again
			if (frequentMap != null)
			{
				byNpc = null;
				placements = null;
				// The shop-only places too (Findability.Reset)
				Findability.Reset();
				// The world's own map changed (the world evil arrived; a test map has this world's evil): everything planned
				// from the old one (recipe plan, applied recipes, shop permutation, decraft rules) is rebuilt too, on the next
				// tick, not inside the build that noticed it. The reset clears frequentMap: no loop
				if (frequentMap.Evil != map.Evil) SeedSystem.RequestCacheReset();
			}
			hostTiers = null;
			frequentMap = map;
		}

		// Met often in its best biome at a tier (MinShare): the vanilla spawns mixed in count from there (HostTiers)
		private static bool FrequentAt(int npc, int tier)
		{
			(int biome, _, _) = BiomeSlots.BestBiome(npc, tier);
			return biome >= 0 && BiomeSlots.SteadyShare(biome, npc, tier) >= MinShare(biome, tier);
		}

		// The steady share a frequent creature needs in a biome at a tier: MinHostShare, lower in a biome with many enemies
		public static double MinShare(int biome, int tier) => BiomeSlots.FrequentShare(biome, tier, MinHostShare);

		private static bool IsNaturalHost(int npc, int tier)
		{
			if (NPCID.Sets.CountsAsCritter[npc] || MetOnlyInEvents(npc) || NeverMet(npc)) return false;
			return HostTiers().TryGetValue(npc, out int from) && from <= Tiers.Clamp(tier);
		}

		// The tier from which a natural enemy can carry guaranteed items: the tier it spawns at (EnemySwapPool.NaturalEnemies),
		// or the earlier one at which it is met in its slots (BiomeSlots.MeetTier: the starting Forest's enemies, Demon Eyes
		// included, are all met from the first night). Its own tier for an enemy that never spawns naturally
		public static int HostTier(int npc) => HostTiers().TryGetValue(npc, out int tier) ? tier : NpcTiers.BaseTier(npc);

		private static Dictionary<int, int> HostTiers()
		{
			FollowMap();
			if (hostTiers != null) return hostTiers;
			Dictionary<int, int> result = new();
			for (int t = 0; t < Tiers.Count; t++)
			{
				foreach (int npc in EnemySwapPool.NaturalEnemies(t)) result.TryAdd(npc, t);
			}
			if (BiomeSlots.SpawnsPlanned)
			{
				foreach (int npc in result.Keys.ToList())
				{
					int met = BiomeSlots.MeetTier(npc);
					if (met >= 0 && met < result[npc]) result[npc] = met;
					// The vanilla spawns mixed in (BiomeVanillaChance) only count where the enemy is met often there
					int vanilla = BiomeSlots.VanillaMeetTier(npc);
					if (vanilla >= 0 && vanilla < result[npc] && FrequentAt(npc, vanilla)) result[npc] = vanilla;
				}
			}
			hostTiers = result;
			return result;
		}

		// Without the redistribution by biome, the swap pool also holds enemies of the evil this world lacks (Crimson ones in
		// a Corruption world): they never spawn (by biome, the pool only holds enemies that really spawn somewhere)
		private static bool NeverMet(int npc) => !BiomeSlots.Active && BiomeSlots.NeverSpawns(npc);

		// Natural enemies that may carry a guaranteed drop from a tier on (no critter, no event enemy, met in this world)
		private static List<int> NaturalHosts(int tier) => HostTiers().Where(pair => pair.Value == Tiers.Clamp(tier)).Select(pair => pair.Key)
			.Where(n => !NPCID.Sets.CountsAsCritter[n] && !MetOnlyInEvents(n) && !NeverMet(n)).OrderBy(n => n).ToList();

		// Common natural enemies of a tier (bestiary rarity 1, else 2...). Never a rare enemy (NPC.rarity: the Rune Wizard, the
		// biome mimics): met a few times per world at best. A tier with only rare natural enemies (a world whose tier 8 slots
		// went to them) uses its progression bosses (Assign)
		private static List<int> Hosts(int tier)
		{
			List<int> natural = NaturalHosts(tier).Where(n => !ContentSamples.NpcsByNetId.TryGetValue(n, out NPC sample) || sample.rarity == 0).ToList();
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

		// An event enemy met only during its event: no reliable source. One that holds ordinary slots of the spawn map (an extra
		// by biome: goblins, moon enemies... with SwapEventEnemies) spawns there every day like a natural enemy
		public static bool MetOnlyInEvents(int npcType) => IsEventEnemy(npcType) && !BiomeSlots.HasSteadySlot(npcType);

		private static int Stars(int npcType) => ContentSamples.NpcBestiaryRarityStars.TryGetValue(npcType, out int stars) ? stars : 1;
	}

	// The tier at which each item can be found without crafting it: world materials, ores, enemy and boss drops,
	// bags, herbs and guaranteed sources. Independent of the current progression (gated drops count from the tier
	// where they are unlocked).
	public static class Findability
	{
		public const int Never = int.MaxValue;

		// What set an item's base findability tier (tests, export): the world (materials, herbs, ores), an NPC's plan (netId)
		// or a bag's content
		public enum SourceKind { None, World, Npc, Bag }

		// Does this world have that evil biome? Both in drunk worlds
		private static bool HasEvil(bool crimson) => Main.drunkWorld || WorldGen.crimson == crimson;

		// Gathered directly in the world
		private static readonly (int item, int tier)[] WorldMaterials =
		{
			(ItemID.DirtBlock, 0), (ItemID.StoneBlock, 0), (ItemID.Wood, 0), (ItemID.BorealWood, 0), (ItemID.PalmWood, 0), (ItemID.RichMahogany, 0),
			(ItemID.Ebonwood, 0), (ItemID.Shadewood, 0), (ItemID.Cactus, 0), (ItemID.ClayBlock, 0), (ItemID.MudBlock, 0), (ItemID.SandBlock, 0),
			(ItemID.SnowBlock, 0), (ItemID.IceBlock, 0), (ItemID.SiltBlock, 0), (ItemID.SlushBlock, 0), (ItemID.Cobweb, 0), (ItemID.GraniteBlock, 0),
			(ItemID.MarbleBlock, 0), (ItemID.Sandstone, 0), (ItemID.HardenedSand, 0), (ItemID.EbonstoneBlock, 2), (ItemID.CrimstoneBlock, 2),
			(ItemID.Hive, 0), (ItemID.DesertFossil, 0), (ItemID.Amethyst, 0), (ItemID.Topaz, 0), (ItemID.Sapphire, 0), (ItemID.Emerald, 0),
			(ItemID.Ruby, 0), (ItemID.Diamond, 0), (ItemID.Amber, 0), (ItemID.FallenStar, 0), (ItemID.Seashell, 0), (ItemID.Coral, 0),
			(ItemID.Starfish, 0), (ItemID.Acorn, 0), (ItemID.Pumpkin, 0), (ItemID.Vine, 0), (ItemID.Daybloom, 0), (ItemID.Moonglow, 0),
			(ItemID.Blinkroot, 0), (ItemID.Waterleaf, 0), (ItemID.Shiverthorn, 0), (ItemID.Deathweed, 1), (ItemID.Fireblossom, 2),
			(ItemID.Mushroom, 0), (ItemID.GlowingMushroom, 0), (ItemID.VileMushroom, 0), (ItemID.ViciousMushroom, 0), (ItemID.JungleSpores, 0),
			(ItemID.AshBlock, 2), (ItemID.Obsidian, 0), (ItemID.Pearlwood, 4), (ItemID.PearlstoneBlock, 4), (ItemID.CrystalShard, 4)
		};

		public static IReadOnlyList<int> HerbPlants => Herbs;

		// The materials gathered directly in the world, ores apart, whatever this world is (the tiers from sources)
		public static IEnumerable<int> GatheredMaterials => WorldMaterials.Select(m => m.item);

		private static readonly int[] Herbs =
		{
			ItemID.Daybloom, ItemID.Moonglow, ItemID.Blinkroot, ItemID.Deathweed, ItemID.Waterleaf, ItemID.Fireblossom, ItemID.Shiverthorn,
			ItemID.Mushroom, ItemID.GlowingMushroom, ItemID.VileMushroom, ItemID.ViciousMushroom, ItemID.JungleSpores
		};

		private static int[] baseTiers;
		private static (SourceKind kind, int id)[] baseSources;
		// BaseTiers without mining (ores, blocks that need a pickaxe), and what mining alone gives (no pickaxe considered)
		private static int[] baseNonMined;
		private static int[] baseMined;
		private static int[] tiers;
		// Tier without mining (guaranteed sources included)
		private static int[] nonMinedTiers;
		// Moved by each reset: a result computed across one (a reset during the computation) is returned but not kept. The
		// caches built from findability (RecipePlanner, ShopPermutation) check it the same way before storing theirs
		private static int version;

		public static int Version => version;

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

		// Gathered directly in this world (blocks, wood, gems, herbs a plant still gives...), ores apart
		public static bool IsWorldMaterial(int item) => WorldMaterials.Any(m => m.item == item) && InThisWorld(item) && (Array.IndexOf(Herbs, item) < 0 || HerbTier(item) < Never);

		// The tier from which the plants of this world give a herb (Never: none). Herbs not randomized: its plant's tier. Randomized:
		// its own plant when it keeps it often enough (HerbKeepChance, read as a drop chance against MinFindableChance), or any
		// plant whose table holds it, from that plant's tier and gated like the other table items. Fireblossom is craft-only
		// when no plant gives it (the craft-only caps of the recipes then apply)
		public static int HerbTier(int herb)
		{
			int index = Array.FindIndex(WorldMaterials, m => m.item == herb);
			if (index < 0 || Array.IndexOf(Herbs, herb) < 0) return Never;
			int own = WorldMaterials[index].tier;
			LootConfig loot = LootConfig.Instance;
			if (!loot.RandomizeHerbs || !ItemPool.IsRandomizable(herb)) return InThisWorld(herb) ? own : Never;
			int tier = InThisWorld(herb) && loot.HerbKeepChance >= ProgressionConfig.Instance.MinFindableChance ? own : Never;
			foreach (int plant in Herbs.Where(p => ItemPool.IsRandomizable(p) && InThisWorld(p)))
			{
				if (!WorldSources.HerbTable(plant).Contains(herb)) continue;
				int plantTier = WorldMaterials.First(m => m.item == plant).tier;
				tier = Math.Min(tier, Math.Max(plantTier, ItemPool.UnlockTierOf(herb)));
			}
			return tier;
		}

		// The materials of one evil only grow in its worlds (both in drunk worlds), until hardmode spreads them
		private static bool InThisWorld(int item)
		{
			return item switch
			{
				ItemID.Ebonwood or ItemID.EbonstoneBlock or ItemID.VileMushroom => HasEvil(false),
				ItemID.Shadewood or ItemID.CrimstoneBlock or ItemID.ViciousMushroom => HasEvil(true),
				_ => true
			};
		}

		// Including the guaranteed sources (GuaranteedSources.FoundTier). A mined ore or block counts from its tier once a
		// pickaxe of the power it needs is found (dropped, in a bag, guaranteed: RecipePlanner.MiningRequirements gives one for
		// every mining level), or from its other sources if earlier. Never from a crafted pickaxe: a pickaxe recipe may then
		// need that ore without a cycle (its level's ore always comes from a pickaxe found without crafting)
		public static int Tier(int item)
		{
			int[] values = tiers;
			if (values == null)
			{
				int started = version;
				BaseTiers(out int[] baseOnly, out int[] mined);
				values = (int[])baseOnly.Clone();
				foreach (GuaranteedSources.Requirement requirement in GuaranteedSources.Requirements)
				{
					values[requirement.Item] = Math.Min(values[requirement.Item], GuaranteedSources.FoundTier(requirement));
				}
				int[] nonMined = (int[])values.Clone();
				List<(int power, int tier)> pickaxes = PickaxeTiers(values);
				for (int type = 1; type < values.Length; type++)
				{
					if (mined[type] >= Never) continue;
					int need = MiningNeed(type);
					int pick = need <= 0 ? 0 : pickaxes.Where(p => p.power >= need).Select(p => p.tier).DefaultIfEmpty(Never).Min();
					values[type] = Math.Min(values[type], Math.Max(mined[type], pick));
				}
				if (started == version)
				{
					nonMinedTiers = nonMined;
					tiers = values;
				}
			}
			return item > 0 && item < values.Length ? values[item] : Never;
		}

		// Tier without mining: drops, bags and guaranteed sources only
		public static int NonMiningTier(int item)
		{
			Tier(item);
			int[] values = nonMinedTiers;
			if (values == null) BaseTiers(out values, out _);
			return item > 0 && item < values.Length ? values[item] : Never;
		}

		// The tier mining alone gives an item (its ore's or block's tier, no pickaxe considered; Never: not mined)
		public static int MinedTier(int item)
		{
			BaseTiers(out _, out int[] values);
			return item > 0 && item < values.Length ? values[item] : Never;
		}

		// The first tier a pickaxe (or drill) of at least this power is found at without crafting (Never: none)
		public static int PickaxeTier(int power)
		{
			int best = Never;
			foreach ((int item, Item sample) in ContentSamples.ItemsByType)
			{
				if (sample.pick >= power) best = Math.Min(best, Tier(item));
			}
			return best;
		}

		// Each found pickaxe's power and tier
		private static List<(int power, int tier)> PickaxeTiers(int[] values)
		{
			List<(int, int)> list = new();
			foreach ((int item, Item sample) in ContentSamples.ItemsByType)
			{
				if (sample.pick > 0 && item > 0 && item < values.Length && values[item] < Never) list.Add((sample.pick, values[item]));
			}
			return list;
		}

		// Pick power needed to mine an item: its tile's (RecipePlanner.PickToMine). A randomized ore may come from any ore tile
		// of its tier (OreMap: a permutation inside the tier), so the strongest of them, as RecipePlanner.OresFrom counts them;
		// each of those tiles is a mining level of that tier or earlier (RecipePlanner.WorldMiningLevels), a modded one too. A
		// need no pickaxe can meet (a modded tile above every guaranteeable pickaxe) stays as it is: no pickaxe is found for it,
		// so mining never finds that item (its level gets no guarantee: MiningRequirements)
		public static int MiningNeed(int item)
		{
			int need = RecipePlanner.PickToMine(item);
			if (OreMap.IsOre(item) && LootConfig.Instance.RandomizeOreDrops && ItemPool.IsRandomizable(item))
			{
				int tier = ItemPool.Info[item].Tier;
				for (int type = 1; type < ItemPool.Info.Length; type++)
				{
					if (OreMap.IsOre(type) && ItemPool.IsRandomizable(type) && ItemPool.Info[type].Tier == tier && OreMap.IsFindable(type)) need = Math.Max(need, RecipePlanner.PickToMine(type));
				}
			}
			return need;
		}

		// Without the guaranteed sources (used to decide what must be guaranteed)
		public static int BaseTier(int item)
		{
			int[] values = BaseTiers();
			return item > 0 && item < values.Length ? values[item] : Never;
		}

		// The source that gave BaseTier (the first one met at that tier); None when never found
		public static (SourceKind kind, int id) BaseSource(int item)
		{
			BaseTiers();
			(SourceKind kind, int id)[] values = baseSources;
			return values != null && item > 0 && item < values.Length ? values[item] : (SourceKind.None, 0);
		}

		// The evil boss this world lacks, its segments and its minions (Creepers): their drops and bag are never a source, as
		// its summon is not (its biome is missing; both evils in drunk worlds)
		public static bool AbsentEvilSource(int npc) => BossPermutation.AbsentEvil(BossParts.Leader(npc), BiomeSlots.CurrentEvil);

		public static void Reset()
		{
			version++;
			baseTiers = null;
			baseSources = null;
			baseNonMined = null;
			baseMined = null;
			tiers = null;
			nonMinedTiers = null;
			// The shop-only items are the ones not found in time
			ShopPermutation.Reset();
		}

		private static int[] BaseTiers() => BaseTiers(out _, out _);

		// nonMinedOut, minedOut: the parts without mining and of mining alone
		private static int[] BaseTiers(out int[] nonMinedOut, out int[] minedOut)
		{
			if (baseTiers != null && baseNonMined != null && baseMined != null)
			{
				nonMinedOut = baseNonMined;
				minedOut = baseMined;
				return baseTiers;
			}
			int started = version;
			int[] result = new int[ItemPool.Info.Length];
			Array.Fill(result, Never);
			(SourceKind kind, int id)[] sources = new (SourceKind, int)[result.Length];
			// Mining, apart (Tier counts it once a pickaxe of the power needed is found)
			int[] mined = new int[result.Length];
			Array.Fill(mined, Never);
			void Mine(int item, int tier)
			{
				if (item > 0 && item < mined.Length) mined[item] = Math.Min(mined[item], Math.Max(0, tier));
			}
			void Add(int item, int tier, SourceKind kind = SourceKind.World, int id = 0)
			{
				if (item <= 0 || item >= result.Length) return;
				tier = Math.Max(0, tier);
				if (tier >= result[item]) return;
				result[item] = tier;
				sources[item] = (kind, id);
			}
			LootConfig loot = LootConfig.Instance;
			// Drops and bag contents are locked by the item's true tier (DropHelper.LockGap), herbs by the same progression
			// checks. Mined ores keep their tier: mining is not gated, and the mining levels use it (RecipePlanner.MiningTier)
			int Gated(int item) => ItemPool.UnlockTierOf(item);
			// A drop at its own vanilla source keeps the allowance of a high-tier item (DropHelper.LockGap)
			int GatedDrop(PlannedDrop drop) => ItemPool.UnlockTierOf(drop.Item, drop.VanillaPlace);

			// A herb only when a plant still gives it (HerbTier: its own plant may give other items only). A block that needs a
			// pickaxe (Obsidian 55, Ebonstone and Pearlstone 65) is mined
			foreach ((int item, int tier) in WorldMaterials.Where(m => InThisWorld(m.item)))
			{
				if (RecipePlanner.PickToMine(item) >= RecipePlanner.MiningLevels[0].pick) Mine(item, tier);
				else Add(item, Array.IndexOf(Herbs, item) >= 0 ? HerbTier(item) : tier);
			}
			if (loot.RandomizeHerbs)
			{
				// The plants of this world only (no Vile Mushroom in a crimson world)
				foreach (int herb in Herbs.Where(h => ItemPool.IsRandomizable(h) && InThisWorld(h)))
				{
					int herbTier = WorldMaterials.First(m => m.item == herb).tier;
					foreach (int item in WorldSources.HerbTable(herb)) Add(item, Math.Max(herbTier, Gated(item)));
				}
			}
			for (int type = 1; type < ItemPool.Info.Length; type++)
			{
				// Only the ores of this world can be mined (Tin is not in a Copper world). Randomized or not, mining gives
				// the ores of the world of each tier (a permutation inside the tier), so the mapping itself doesn't matter.
				if (OreMap.IsOre(type) && OreMap.IsFindable(type)) Mine(type, ItemPool.TierOf(type));
			}

			float minChance = ProgressionConfig.Instance.MinFindableChance / 100f;
			void AddPlan(List<PlannedDrop> plan, int sourceTier, int npc)
			{
				foreach (PlannedDrop drop in plan)
				{
					// The Temple Key on a Temple enemy or Golem: behind the door it opens (GuaranteedSources.ExcludedHost)
					if (drop.Chance < minChance || !WorldAllows(drop.Conditions) || GuaranteedSources.ExcludedHost(drop.Item, npc)) continue;
					// The conditions of the place (permuted drops take the receiving enemy's: hardmode, Plantera...)
					int from = SourceConditionTier(drop.Conditions, sourceTier);
					if (from == Never) continue;
					Add(drop.Item, Math.Max(from, GatedDrop(drop)), SourceKind.Npc, npc);
				}
			}
			for (int t = 0; t < Tiers.Count; t++)
			{
				// Event, moon and seasonal enemies met only during their event are no reliable source (those holding ordinary slots are)
				foreach (int npc in EnemySwapPool.NaturalEnemies(t).Where(n => !GuaranteedSources.MetOnlyInEvents(n) && !AbsentEvilSource(n))) AddPlan(RandomNpcDropRule.Plan(npc, ProgressionSystem.PlannedExpert, lucky: true, guaranteed: false), t, npc);
			}
			// Not the evil boss this world lacks (BossPermutation.AbsentEvil): its summon needs its biome
			foreach (int boss in NpcTiers.BossTypes.Where(b => !AbsentEvilSource(b)))
			{
				AddPlan(RandomNpcDropRule.Plan(boss, ProgressionSystem.PlannedExpert, lucky: true, guaranteed: false), NpcTiers.BaseTier(boss), boss);
			}
			// A bag's content is found once the bag is (bags may hold bags: repeat until nothing changes). Never a fishing crate:
			// fishing is a bonus, nothing relies on it (BagContentTier still tells when a crate's content can be had)
			Dictionary<int, List<PlannedDrop>> bagPlans = LootSnapshot.Items.Keys.Where(b => !SeasonalBags.Contains(b) && !ItemID.Sets.IsFishingCrate[b]).OrderBy(b => b)
				.ToDictionary(b => b, b => BagPlan.Plan(b, lucky: true));
			bool changed = true;
			for (int pass = 0; pass < 8 && changed; pass++)
			{
				changed = false;
				foreach ((int bag, List<PlannedDrop> plan) in bagPlans)
				{
					int unit = BossPermutation.BagUnit(bag);
					// The absent evil boss's bag (Shadow Scales in a Crimson world): no source, should a drop give the bag itself
					if (unit > 0 && AbsentEvilSource(unit)) continue;
					int tier = BagContentTier(bag, result);
					if (tier == Never) continue;
					foreach (PlannedDrop drop in plan)
					{
						if (drop.Chance < minChance || drop.Item <= 0 || drop.Item >= result.Length || !WorldAllows(drop.Conditions)) continue;
						if (unit > 0 && GuaranteedSources.ExcludedHost(drop.Item, unit)) continue;
						int from = SourceConditionTier(drop.Conditions, tier);
						if (from == Never) continue;
						int dropTier = Math.Max(from, GatedDrop(drop));
						if (dropTier < result[drop.Item])
						{
							result[drop.Item] = dropTier;
							sources[drop.Item] = (SourceKind.Bag, bag);
							changed = true;
						}
					}
				}
			}
			// Bags are never mined: what is had without mining is complete here. Mining then counts as a world source (it came
			// first, so it keeps a tie)
			int[] nonMined = (int[])result.Clone();
			for (int item = 1; item < result.Length; item++)
			{
				if (mined[item] >= Never || mined[item] > result[item]) continue;
				result[item] = mined[item];
				sources[item] = (SourceKind.World, 0);
			}
			if (started == version)
			{
				baseSources = sources;
				baseNonMined = nonMined;
				baseMined = mined;
				baseTiers = result;
			}
			nonMinedOut = nonMined;
			minedOut = mined;
			return result;
		}

		// The tier from which a drop of a source of this tier can happen, given the progression conditions of the entry
		// (hardmode, mechanical bosses, Plantera; the remix seed's hardmode drops, such as the pre-hardmode Mimic's). Never:
		// a pre-hardmode only drop of a hardmode source. Other conditions (events, biomes...) are left to the drop chance; so is
		// a boss fight condition kept on a permuted place (a genuinely enraged Empress: reachable at the boss's tier). The
		// first-Plantera-kill one is dropped by the boss permutation. As a findability source, see SourceConditionTier
		public static int ConditionTier(List<IItemDropRuleCondition> conditions, int sourceTier)
		{
			int tier = sourceTier;
			if (conditions == null) return tier;
			foreach (IItemDropRuleCondition condition in conditions)
			{
				if (condition is Conditions.IsPreHardmode && sourceTier >= (int)Core.Tier.Hardmode) return Never;
				Core.Tier? needed = condition switch
				{
					Conditions.IsHardmode or Conditions.RemixSeedHardmode => Core.Tier.Hardmode,
					Conditions.BeatAnyMechBoss or Conditions.DownedAllMechBosses => Core.Tier.PostMechBoss,
					Conditions.DownedPlantera or Conditions.FirstTimeKillingPlantera => Core.Tier.PostPlantera,
					_ => null
				};
				if (needed != null) tier = Math.Max(tier, (int)needed.Value);
			}
			return tier;
		}

		// ConditionTier for a findability source: a date-bound entry (Christmas, Halloween: Main.xMas, Main.halloween) is no
		// reliable source, Never. Seasonal entries are permuted among the seasonal places (DropPermutation.Seasonal) and keep
		// their place's condition, so wherever they land they only drop on those dates
		public static int SourceConditionTier(List<IItemDropRuleCondition> conditions, int sourceTier) =>
			conditions != null && conditions.Any(DateBound) ? Never : ConditionTier(conditions, sourceTier);

		// Conditions true only on some dates (Christmas, Halloween, or their forced events)
		public static bool DateBound(IItemDropRuleCondition condition) =>
			condition is Conditions.IsChristmas or Conditions.XmasPresentDrop or Conditions.HalloweenWeapons or Conditions.HalloweenGoodieBagDrop;

		// Drop conditions fixed by the world (difficulty, world evil, special seeds): an expert-only treasure bag is no source
		// in Classic. Their CanDrop only reads Main.expertMode, Main.masterMode (here the planned ones, ProgressionSystem), WorldGen.crimson and the special seed flags
		// (tenthAnniversaryWorld, dontStarveWorld, remixWorld: all synced in message 7), plus npc.boss for the legacy boss
		// checks (their drops are a boss's); in drunk worlds both evils exist. The remix seed's easy and hard mode drops count
		// in a remix world (each in its part of the game). Other conditions (events, biomes...) are left to the drop chance.
		public static bool WorldAllows(List<IItemDropRuleCondition> conditions)
		{
			if (conditions == null) return true;
			foreach (IItemDropRuleCondition condition in conditions)
			{
				if (WorldVerdict(condition) == false) return false;
			}
			return true;
		}

		// What the world (difficulty, evil, secret seeds) decides about a drop condition; null when it does not decide it
		public static bool? WorldVerdict(IItemDropRuleCondition condition) => condition switch
		{
			Conditions.IsExpert => ProgressionSystem.PlannedExpert,
			Conditions.NotExpert => !ProgressionSystem.PlannedExpert,
			Conditions.IsMasterMode => ProgressionSystem.PlannedMaster,
			Conditions.NotMasterMode => !ProgressionSystem.PlannedMaster,
			Conditions.IsCrimson => HasEvil(true),
			Conditions.IsCorruption => HasEvil(false),
			Conditions.IsCrimsonAndNotExpert => HasEvil(true) && !ProgressionSystem.PlannedExpert,
			Conditions.IsCorruptionAndNotExpert => HasEvil(false) && !ProgressionSystem.PlannedExpert,
			Conditions.LegacyHack_IsBossAndExpert => ProgressionSystem.PlannedExpert,
			Conditions.LegacyHack_IsBossAndNotExpert => !ProgressionSystem.PlannedExpert,
			Conditions.TenthAnniversaryIsUp => Main.tenthAnniversaryWorld,
			Conditions.TenthAnniversaryIsNotUp => !Main.tenthAnniversaryWorld,
			Conditions.DontStarveIsUp => Main.dontStarveWorld,
			Conditions.DontStarveIsNotUp => !Main.dontStarveWorld,
			Conditions.RemixSeed or Conditions.RemixSeedEasymode or Conditions.RemixSeedHardmode => Main.remixWorld,
			Conditions.NotRemixSeed => !Main.remixWorld,
			_ => null
		};

		// Only given during seasonal events: never counted as a source
		public static readonly HashSet<int> SeasonalBags = new() { ItemID.Present, ItemID.GoodieBag };

		// Fishing is possible from the start (a Wooden Fishing Pole, Angler rewards)
		public const int FishingTier = 0;

		// Where a crate is caught, conservatively: hardmode crates in hardmode, lava crates once lava fishing gear is
		// reachable, dungeon crates after Skeletron, sky crates once floating islands are reachable
		public static int CrateAccessTier(int crate)
		{
			// Evil crates need their evil (both in drunk worlds); the Hallow only exists before hardmode in drunk and remix worlds
			if (crate is ItemID.CorruptFishingCrate or ItemID.CorruptFishingCrateHard && !HasEvil(false)) return Never;
			if (crate is ItemID.CrimsonFishingCrate or ItemID.CrimsonFishingCrateHard && !HasEvil(true)) return Never;
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

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
	// Items that must be findable in the world (crafting stations, crafted and dropped boss summons; vanilla recipe
	// ingredients when chosen): each one is placed as an extra drop of the common enemies of one biome of its tier (the same
	// chance on each, so that the whole biome gives it in about StationFindKills kills) and in the pot pool; basic stations
	// also in wooden chests.
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

		// The critical stations' chance follows from the pacing model (CriticalChance): the kills of a simulated early hour
		// that land on the item's hosts give the first one after this many minutes on average, before pots and chests
		// shorten it a little (PacingTests: 15 to 30). Fixed chances (1/28, 1/45, 1/55) went from 12 to 55 minutes as the
		// hosts changed
		public const double CriticalTargetMinutes = 22;
		// Each item goes to one biome of its tier: the enemies met there steadily (at least MinBiomeHostShare each, no rare
		// enemy, no event enemy), most met first, up to GuaranteedHostsPerItem. A biome qualifies with at least MinBiomeHosts
		// of them making MinBiomeShare of its spawns together
		public const int MinBiomeHosts = 3;
		public const double MinBiomeShare = 0.2;
		public const double MinBiomeHostShare = 0.01;
		// The same chance on every host: the biome gives a station, summon or tool in about StationFindKills kills
		// (1 / (sum of shares x chance)), a material in MaterialTargetKills, within these bounds
		public const float MaxStationChance = 0.5f;
		public const double MaterialTargetKills = 8;
		public const float MaterialChance = 1 / 20f;
		public const float MaxMaterialChance = 0.75f;
		// Critical stations follow the pacing model instead, up to this (their hosts may be few among the early kills)
		public const float MaxCriticalChance = 0.3f;
		// Without a known spawn share (enemies swapped another way, no biome with enough hosts): common hosts of the tier,
		// with the total chance two hosts had before 1.3.0
		public const float UnknownShareStationChance = 1 / 25f;
		public const float UnknownShareMaterialChance = 1 / 6f;
		public const int UnknownShareHosts = 2;
		// Stations, summons and tools are never rarer than this, whatever GuaranteedDropMultiplier says
		public const float MinStationChance = 0.01f;
		// GuaranteedFindTime: a station, summon or tool comes within StationFindKills x FindKillsMargin kills of its biome, a
		// critical station within MaxCriticalMinutes of the pacing model
		public const double FindKillsMargin = 1.5;
		public const double MaxCriticalMinutes = 30;
		// A frequent creature of the hints makes at least this share of the spawns of its best biome, or half the average
		// share of that biome's enemies when it holds many (MinShare)
		public const double MinHostShare = 0.05;

		public static double MaxFindKills => ProgressionConfig.Instance.StationFindKills * FindKillsMargin;

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
		private static readonly Dictionary<(int npc, int tier), double> shares = new();
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
			shares.Clear();
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
			Dictionary<int, List<PlannedDrop>> drops = byNpc;
			List<int> order = placements.TryGetValue(item, out Placement p) ? p.Hosts : null;
			return drops.Where(pair => pair.Value.Any(d => d.Item == item)).Select(pair => pair.Key)
				.OrderBy(n => order?.IndexOf(n) ?? 0).ThenBy(n => n).ToList();
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
			// Hosts kept although they already carried MaxItemsPerHost items (the biome had too few others)
			public List<int> Forced;
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

		// Steady share of the spawns of the item's biome that its hosts make together (0: no biome)
		public static double BiomeShare(int item)
		{
			Build();
			if (!placements.TryGetValue(item, out Placement p) || p.Biome < 0) return 0;
			return p.Hosts.Sum(h => BiomeSlots.SteadyShare(p.Biome, h, p.Tier));
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

		// The vanilla item guaranteed for a progression or themed station tile (one per tile: the Mythril Anvil stands for the
		// Orichalcum Anvil), 0 when none is needed: a protected placer keeps its vanilla sources (recipe, shop, drop). Chosen
		// by a fixed rule (lowest tier, then lowest ID): the recipe plan can't be read here, it depends on these guarantees
		public static int StationItem(int tile)
		{
			Stations.StationInfo info = Stations.Info(tile);
			if (tile == TileID.DemonAltar || info.Kind is not (StationKind.Progression or StationKind.Thematic)) return 0;
			List<int> placers = ContentSamples.ItemsByType.Where(pair => pair.Value.createTile == tile).Select(pair => pair.Key).ToList();
			if (placers.Any(p => ItemPool.IsValid(p) && ItemPool.Info[p].Protected)) return 0;
			return placers.Where(p => p < ItemID.Count && ItemPool.IsRandomizable(p)).OrderBy(ItemPool.TierOf).ThenBy(p => p).FirstOrDefault();
		}

		// Progression stations (Hellforge, hardmode anvils and forges, Tinkerer's Workshop, Ancient Manipulator...) at their
		// tier, and themed ones at their item's tier, when unprotected: their recipe, shop or drop may be randomized away
		public static IEnumerable<Requirement> OtherStationRequirements()
		{
			foreach (int tile in Stations.RequiredTiles().OrderBy(t => t))
			{
				int item = StationItem(tile);
				if (item <= 0) continue;
				Stations.StationInfo info = Stations.Info(tile);
				int tier = info.Kind == StationKind.Progression ? info.Tier : ItemPool.TierOf(item);
				yield return new Requirement { Item = item, Tier = Tiers.Clamp(tier), Station = true };
			}
		}

		// Crafted and dropped vanilla boss summons (Guide Voodoo Doll included) when unprotected, at the tier before their
		// boss's; dropped one at a time, as rarely as a station. Only the summon of the world's evil (both in drunk and zenith
		// worlds: BiomeSlots.CurrentEvil, from the synced world data). Modded summons stay protected (Protection)
		public static IEnumerable<Requirement> SummonRequirements()
		{
			WorldEvil evil = BiomeSlots.CurrentEvil;
			for (int type = 1; type < ItemPool.Info.Length && type < ItemID.Count; type++)
			{
				if (!Protection.IsBossSummon(type) || !ItemPool.IsRandomizable(type)) continue;
				if (type == ItemID.WormFood && evil == WorldEvil.Crimson || type == ItemID.BloodySpine && evil == WorldEvil.Corruption) continue;
				yield return new Requirement { Item = type, Tier = Protection.SummonTier(type), Station = true };
			}
		}

		// Items a pot may also give, among those already unlocked: basic stations, tools and early materials (not the
		// progression stations nor the summons: they would dilute the work bench, see PacingModel)
		public static List<int> PotItems(int unlockedTier)
		{
			HashSet<int> basic = Stations.Basic().Select(s => s.info.Item).ToHashSet();
			return Requirements.Where(r => r.Tier <= unlockedTier && (r.Tool || basic.Contains(r.Item) || r.Tier == 0 && !r.Station)).Select(r => r.Item).ToList();
		}

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
			if (load.MaxItemsHost != 0)
			{
				ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Guaranteed load: at most {load.MaxItems} items on {Lang.GetNPCNameValue(load.MaxItemsHost)}, busiest "
					+ $"{Lang.GetNPCNameValue(load.MaxDropsHost)} with {load.MaxDrops:0.000} guaranteed drops per kill, {load.Forced} host(s) kept over {MaxItemsPerHost} items");
			}
		}

		public struct HostLoad
		{
			// The most guaranteed items on one host, and that host
			public int MaxItems;
			public int MaxItemsHost;
			// The most guaranteed drops one kill of a host gives on average (the sum of its guaranteed chances), and that host
			public double MaxDrops;
			public int MaxDropsHost;
			// Hosts kept over MaxItemsPerHost items, over every placement
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
			List<Requirement> all = StationRequirements().Concat(OtherStationRequirements()).Concat(SummonRequirements()).ToList();
			foreach (Func<IEnumerable<Requirement>> provider in ExtraRequirements) all.AddRange(provider());
			return all.GroupBy(r => r.Item).Select(g => new Requirement
			{
				Item = g.Key, Tier = g.Min(r => r.Tier), Station = g.Any(r => r.Station), Critical = g.Any(r => r.Critical), Tool = g.Any(r => r.Tool)
			}).OrderBy(r => r.Tier).ThenBy(r => DetRandom.ItemKey(r.Item)).ToList();
		}

		// Who picks its biome first (biomes holding fewer items are preferred): the critical stations (always the Forest),
		// the other basic stations, the pickaxes, the materials, the progression and themed stations, then the summons
		private static int Priority(Requirement r)
		{
			if (r.Critical) return 0;
			if (r.Station && Stations.Basic().Any(s => s.info.Item == r.Item)) return 1;
			if (r.Tool) return 2;
			if (!r.Station) return 3;
			return Protection.IsBossSummon(r.Item) ? 5 : 4;
		}

		// The hosts of each requirement with a world seed and the current spawn map (the tests plan other seeds with
		// BiomeSlots.WithMap): one biome per item at the highest tier at or below its own that has a qualifying biome
		// (BiomeCandidates, ChooseHosts), picked at random weighted by the share its hosts make (up to MaxBiomeWeightShare),
		// by the size of the biome, and less in a biome already holding items, so that the items of a tier spread over its
		// biomes. Hosts already carrying MaxItemsPerHost items are left aside while the biome has enough others. Every host
		// gets the same chance (BiomeChance), so that each period alone (day, night) gives the item in about
		// StationFindKills kills. Critical stations go to the Forest's critical hosts with the pacing model's chance
		// (CriticalChance). Without any biome, common hosts of the tier
		public static Dictionary<int, List<PlannedDrop>> Assign(List<Requirement> all, int seed, out int missing, out Dictionary<int, Placement> places)
		{
			missing = 0;
			places = new Dictionary<int, Placement>();
			Dictionary<int, List<PlannedDrop>> result = new();
			ProgressionConfig cfg = ProgressionConfig.Instance;
			int perItem = HostLimit;
			float multiplier = cfg.GuaranteedDropMultiplier;
			Dictionary<int, List<BiomeEligible>> eligibleByTier = new();
			Dictionary<int, int> biomeItems = new();
			// Guaranteed items on each host so far (critical stations included)
			Dictionary<int, int> load = new();
			List<int>[] common = new List<int>[Tiers.Count];
			for (int t = 0; t < Tiers.Count; t++) common[t] = Hosts(t);
			foreach (Requirement requirement in all.OrderBy(Priority).ThenBy(r => r.Tier).ThenBy(r => DetRandom.ItemKey(r.Item)))
			{
				Random rng = new(DetRandom.Hash(seed, RollKey.Guaranteed, DetRandom.ItemKey(requirement.Item)));
				bool single = requirement.Station || requirement.Tool;
				Placement place = new() { Biome = -1, Tier = Tiers.Clamp(requirement.Tier), Time = SlotTime.Any, Forced = new List<int>() };
				List<int> chosen = null;
				float chance = 0f;
				List<int> forest = requirement.Critical ? CriticalForestHosts(requirement.Tier) : null;
				if (forest != null && forest.Count > 0)
				{
					// Critical stations: one chance for all the Forest enemies the early kills meet, from the pacing model
					chosen = forest;
					chance = CriticalChance(chosen);
					place.Biome = BiomeSlots.SpawnsPlanned ? (int)SpawnBiome.Forest : -1;
					place.Tier = 0;
					place.HostTimes = chosen.Select(h => place.Biome >= 0 ? TimeIn(place.Biome, h, 0) : SlotTime.Any).ToList();
				}
				else
				{
					// At the highest tier that has a biome (nothing spawns from tier 8, post-Cultist, on: such items go to tier 7
					// or lower hosts, a little earlier than their tier)
					for (int t = Tiers.Clamp(requirement.Tier); t >= 0 && chosen == null; t--)
					{
						if (!eligibleByTier.TryGetValue(t, out List<BiomeEligible> eligible)) eligibleByTier[t] = eligible = BiomeCandidates(t);
						List<BiomeHosts> candidates = eligible.Select(b => ChooseHosts(b, perItem, load)).Where(c => c != null).Select(c => c.Value).ToList();
						if (candidates.Count == 0) continue;
						int index = PickWeighted(Enumerable.Range(0, candidates.Count).ToList(), i => BiomeWeight(candidates[i], biomeItems.GetValueOrDefault(candidates[i].Biome)), rng);
						BiomeHosts picked = candidates[index];
						chosen = picked.Hosts;
						chance = BiomeChance(single, picked.Share);
						place.Biome = picked.Biome;
						place.Tier = t;
						place.Time = picked.Time;
						place.HostTimes = picked.HostTimes;
						place.Forced = picked.Forced;
					}
					if (chosen == null)
					{
						// No biome: common hosts of the tier (or below), the total chance split between them
						for (int t = Tiers.Clamp(requirement.Tier); t >= 0 && chosen == null; t--)
						{
							if (common[t].Count == 0) continue;
							chosen = common[t].OrderBy(_ => rng.Next()).Take(perItem).ToList();
							chance = (single ? UnknownShareStationChance : UnknownShareMaterialChance) * UnknownShareHosts / chosen.Count;
							place.Tier = t;
						}
					}
				}
				if (chosen == null || chosen.Count == 0)
				{
					missing++;
					continue;
				}
				chance = Math.Min(1f, chance * multiplier);
				if (single) chance = Math.Max(MinStationChance, chance);
				(int min, int max) = single ? (1, 1) : StackRules.Range(requirement.Item);
				foreach (int host in chosen)
				{
					if (!result.TryGetValue(host, out List<PlannedDrop> drops)) result[host] = drops = new List<PlannedDrop>();
					drops.Add(new PlannedDrop { Item = requirement.Item, StackMin = min, StackMax = Math.Max(min, max), Chance = chance, Extra = true, Guaranteed = true });
					load[host] = load.GetValueOrDefault(host) + 1;
				}
				place.Hosts = chosen;
				place.HostTimes ??= chosen.Select(_ => SlotTime.Any).ToList();
				places[requirement.Item] = place;
				if (place.Biome >= 0) biomeItems[place.Biome] = biomeItems.GetValueOrDefault(place.Biome) + 1;
			}
			return result;
		}

		// A host carries at most this many guaranteed items (critical stations included), unless its biome would keep fewer
		// than MinHostsInBiome hosts, or a period fewer than MinPeriodHosts, without it
		public const int MaxItemsPerHost = 4;
		// A biome met by day and by night gives each period at least this many hosts (when it has them)
		public const int MinPeriodHosts = 2;
		// The share of a biome's spawns its hosts make weighs in its choice up to this: beyond, a small special biome full of
		// one tier's enemies would take most items
		public const double MaxBiomeWeightShare = 0.5;

		// The tier from which a biome can be farmed (-1: never: the Graveyard and the Meteor exist only through the player or
		// an event). The Dungeon needs Skeletron, the Underworld a way through the evil biome's depths, the Temple Plantera,
		// the sky islands a little gear
		public static int AccessTier(int biome) => biome >= BiomeSlots.ModdedBiomeStart ? 0 : (SpawnBiome)biome switch
		{
			SpawnBiome.Dungeon => (int)Core.Tier.PostSkeletron,
			SpawnBiome.Underworld => (int)Core.Tier.PostEvilBoss,
			SpawnBiome.Temple => (int)Core.Tier.PostPlantera,
			SpawnBiome.Sky => (int)Core.Tier.PostEyeOfCthulhu,
			SpawnBiome.Graveyard or SpawnBiome.Meteor => -1,
			_ => 0
		};

		private struct BiomeEligible
		{
			public int Biome;
			// The natural hosts met steadily there, the most met first, with their share of its spawns by day and by night
			public List<(int npc, double day, double night)> Hosts;
			// Its slots open at the tier (BiomeSlots.SlotCountAt)
			public int Slots;
		}

		private struct BiomeHosts
		{
			public int Biome;
			public List<int> Hosts;
			public List<SlotTime> HostTimes;
			public List<int> Forced;
			public SlotTime Time;
			// Steady share of the biome's spawns the hosts make together, in the period where they make the least (the only
			// one of a one-period biome)
			public double Share;
			public int Slots;
		}

		private static bool InDay((int npc, double day, double night) h) => h.day >= MinBiomeHostShare;

		private static bool InNight((int npc, double day, double night) h) => h.night >= MinBiomeHostShare;

		private static SlotTime TimeOf(double day, double night) => day > 0 && night > 0 ? SlotTime.Any : day > 0 ? SlotTime.Day : night > 0 ? SlotTime.Night : SlotTime.Any;

		// When a type is met in a biome at a tier, from its steady slots there
		public static SlotTime TimeIn(int biome, int type, int tier) =>
			TimeOf(BiomeSlots.SteadyShareAt(biome, type, tier, SlotTime.Day), BiomeSlots.SteadyShareAt(biome, type, tier, SlotTime.Night));

		// The biomes that may carry the items of a tier: reachable at that tier (AccessTier), with at least MinBiomeHosts
		// natural hosts of exactly that tier (a tier 3 station never comes from tier 0 enemies) met steadily there through
		// slots of that tier or below (BiomeSlots.SteadyShareAt), the most met first
		private static List<BiomeEligible> BiomeCandidates(int tier)
		{
			List<BiomeEligible> result = new();
			if (!BiomeSlots.SpawnsPlanned) return result;
			List<int> natural = NaturalHosts(tier).Where(n => !(ContentSamples.NpcsByNetId.TryGetValue(n, out NPC sample) && sample.rarity > 0)).ToList();
			if (natural.Count < MinBiomeHosts) return result;
			foreach (int biome in BiomeSlots.BiomesAt(tier))
			{
				int access = AccessTier(biome);
				if (access < 0 || access > tier) continue;
				List<(int npc, double day, double night)> eligible = natural
					.Select(n => (npc: n, day: BiomeSlots.SteadyShareAt(biome, n, tier, SlotTime.Day), night: BiomeSlots.SteadyShareAt(biome, n, tier, SlotTime.Night)))
					.Where(h => InDay(h) || InNight(h)).OrderByDescending(h => h.day + h.night).ThenBy(h => DetRandom.NpcKey(h.npc)).ToList();
				if (eligible.Count < MinBiomeHosts) continue;
				result.Add(new BiomeEligible { Biome = biome, Hosts = eligible, Slots = BiomeSlots.SlotCountAt(biome, tier) });
			}
			return result;
		}

		// The hosts of one item in a biome (null when the biome doesn't qualify): hosts already carrying MaxItemsPerHost items
		// are left aside while enough others remain (MinHostsInBiome, and MinPeriodHosts in each period); then each period
		// gets its MinPeriodHosts hosts first (a host met at any hour counts for both), the rest goes to the most met. A
		// period whose hosts make less than MinBiomeShare of its spawns is given up (the item is only met in the other one)
		private static BiomeHosts? ChooseHosts(BiomeEligible biome, int perItem, Dictionary<int, int> load)
		{
			List<(int npc, double day, double night)> all = biome.Hosts;
			int dayAll = all.Count(InDay), nightAll = all.Count(InNight);
			List<(int npc, double day, double night)> free = all.Where(h => load.GetValueOrDefault(h.npc) < MaxItemsPerHost).ToList();
			List<int> forced = new();
			foreach ((int npc, double day, double night) h in all.Where(h => load.GetValueOrDefault(h.npc) >= MaxItemsPerHost).OrderBy(h => load[h.npc]).ThenBy(h => all.IndexOf(h)).ToList())
			{
				bool needed = free.Count < MinHostsInBiome || InDay(h) && free.Count(InDay) < Math.Min(MinPeriodHosts, dayAll)
					|| InNight(h) && free.Count(InNight) < Math.Min(MinPeriodHosts, nightAll);
				if (!needed) continue;
				free.Add(h);
				forced.Add(h.npc);
			}
			free = all.Where(free.Contains).ToList();
			bool wantDay = free.Any(InDay), wantNight = free.Any(InNight);
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
						Share = dayOk && nightOk ? Math.Min(day, night) : dayOk ? day : night,
						Slots = biome.Slots
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

		// How likely a biome is to be picked for an item: the share its hosts make (up to MaxBiomeWeightShare), its size, less
		// when it already holds items
		private static double BiomeWeight(BiomeHosts biome, int items) => Math.Min(biome.Share, MaxBiomeWeightShare) * Math.Sqrt(Math.Max(1, biome.Slots)) / (1 + items);

		// How many biomes may carry the items of a tier in this world, hosts not loaded yet (tests)
		public static int CandidateBiomes(int tier) => BiomeCandidates(tier).Count(b => ChooseHosts(b, HostLimit, new Dictionary<int, int>()) != null);

		// The biomes that may carry the items of a tier in the current spawn map, whatever the hosts' load (tests)
		public static List<int> CandidateBiomeList(int tier) => BiomeCandidates(tier).Where(b => ChooseHosts(b, HostLimit, new Dictionary<int, int>()) != null).Select(b => b.Biome).ToList();

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
			for (int s = 0; s < CriticalHostSets; s++) set.UnionWith(CriticalHosts(s));
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

		// Steady share of the spawns of a host's best biome at a tier (BiomeSlots.SteadyShare), -1 when unknown (no slot, or
		// spawns swapped another way)
		public static double HostShare(int npc, int tier)
		{
			FollowMap();
			if (shares.TryGetValue((npc, tier), out double cached)) return cached;
			double share = -1;
			if (BiomeSlots.SpawnsPlanned && BiomeSlots.HasAnySlot(npc))
			{
				(int biome, _, _) = BiomeSlots.BestBiome(npc, tier);
				if (biome >= 0) share = BiomeSlots.SteadyShare(biome, npc, tier);
			}
			shares[(npc, tier)] = share;
			return share;
		}

		// The chance of a critical station on its hosts (before GuaranteedDropMultiplier): the early kills of the pacing
		// model that land on them give it after CriticalTargetMinutes on average (PacingModel.KillsPerMinute). Read from the
		// spawn map and the synced settings only
		public static float CriticalChance(IEnumerable<int> hosts)
		{
			HashSet<int> set = hosts.ToHashSet();
			double share = PacingModel.EarlyKillShare(set.Contains);
			if (share <= 0) return MaxCriticalChance;
			return (float)Math.Clamp(1 / (CriticalTargetMinutes * PacingModel.KillsPerMinute * share), MinStationChance, MaxCriticalChance);
		}

		// Expected kills in the item's biome before it drops (1 / sum of share x chance over its hosts), in the period where
		// that takes longest (the only one of a one-period placement), and the bound GuaranteedFindTime allows (infinity: no
		// biome)
		public static (double kills, double bound) FindKills(int item, int tier)
		{
			Build();
			double bound = MaxFindKills;
			if (!placements.TryGetValue(item, out Placement place) || place.Biome < 0) return (double.PositiveInfinity, bound);
			double worst = double.PositiveInfinity;
			foreach (SlotTime period in place.Time == SlotTime.Any ? new[] { SlotTime.Day, SlotTime.Night } : new[] { place.Time })
			{
				double rate = 0;
				foreach (int host in place.Hosts)
				{
					float chance = For(host).Where(d => d.Item == item).Sum(d => d.Chance);
					rate += BiomeSlots.SteadyShareAt(place.Biome, host, place.Tier, period) * chance;
				}
				worst = Math.Min(worst, rate);
			}
			return (worst > 0 && !double.IsInfinity(worst) ? 1 / worst : double.PositiveInfinity, bound);
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
		// has its own Forest slot: vanilla picks one at random). Other hosts are judged by their spawn share
		private const int CriticalHostSets = 2;

		// The enemies that spawn in those Forest slots (redistributed by biome, else the vanilla ones)
		public static int[] CriticalHosts(int set) => BiomeSlots.ForestFamilyHosts(BiomeSlots.SpawnMap, set == 0).ToArray();

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
				Findability.Reset();
			}
			shares.Clear();
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
			if (NPCID.Sets.CountsAsCritter[npc] || IsEventEnemy(npc) || NeverMet(npc)) return false;
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
			.Where(n => !NPCID.Sets.CountsAsCritter[n] && !IsEventEnemy(n) && !NeverMet(n)).OrderBy(n => n).ToList();

		// Common natural enemies of a tier (bestiary rarity 1, else 2, else any)
		private static List<int> Hosts(int tier)
		{
			List<int> natural = NaturalHosts(tier);
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

		public static IReadOnlyList<int> HerbPlants => Herbs;

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

		// Gathered directly in this world (blocks, wood, gems, herbs...), ores apart
		public static bool IsWorldMaterial(int item) => WorldMaterials.Any(m => m.item == item) && InThisWorld(item);

		// The materials of one evil only grow in its worlds (both in drunk worlds), until hardmode spreads them
		private static bool InThisWorld(int item)
		{
			bool Evil(bool crimson) => Main.drunkWorld || WorldGen.crimson == crimson;
			return item switch
			{
				ItemID.Ebonwood or ItemID.EbonstoneBlock or ItemID.VileMushroom => Evil(false),
				ItemID.Shadewood or ItemID.CrimstoneBlock or ItemID.ViciousMushroom => Evil(true),
				_ => true
			};
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
						result[requirement.Item] = Math.Min(result[requirement.Item], GuaranteedSources.HostTier(host));
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

			foreach ((int item, int tier) in WorldMaterials.Where(m => InThisWorld(m.item))) Add(item, tier);
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

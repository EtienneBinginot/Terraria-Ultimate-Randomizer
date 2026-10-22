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
	// ingredients when chosen): each one is placed as an extra drop of two common enemies of its tier and in the pot pool;
	// basic stations also in wooden chests.
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
		// The critical stations' chance follows from the pacing model (CriticalChance): the kills of a simulated early hour
		// that land on the item's hosts give the first one after this many minutes on average, before pots and chests
		// shorten it a little (PacingTests: 15 to 30). Fixed chances (1/28, 1/45, 1/55) went from 12 to 55 minutes as the
		// hosts changed
		public const double CriticalTargetMinutes = 22;
		// The chance of a station, summon or tool follows from how often its host is met: about this many kills in the host's
		// best biome before it drops (1 / (share x chance)), within StationChance and MaxStationChance. Stations stay a small
		// quest: a host of a crowded biome (the Forest: 2 to 4 % of its spawns each) hits the cap and takes 170 to 330 kills
		// there, one of a small biome (10 % and more) 50 to 70
		public const double StationTargetKills = 50;
		public const float StationChance = 1 / 50f;
		public const float MaxStationChance = 0.15f;
		// Critical stations follow the pacing model instead, up to this (their hosts may be few among the early kills)
		public const float MaxCriticalChance = 0.3f;
		// Materials: a few biome kills per drop, from MaterialChance to MaxMaterialChance
		public const double MaterialTargetKills = 8;
		public const float MaterialChance = 1 / 6f;
		public const float MaxMaterialChance = 0.5f;
		// Without a known spawn share (modded enemies without a biome, enemies swapped another way)
		public const float UnknownShareStationChance = 1 / 25f;
		// Few items per host: the guarantees are spread over most common enemies (beyond, the least loaded takes one more)
		public const int MaxPerHost = 3;
		// Stations, summons and tools are never rarer than this, whatever GuaranteedDropMultiplier says
		public const float MinStationChance = 0.01f;
		// GuaranteedFindTime: the easiest host of a station, summon or tool gives it within this many kills of its best biome,
		// or within 1 / (MaxStationChance x MinShare) for a host of a crowded biome (frequent there with a smaller share);
		// a critical station within MaxCriticalMinutes of the pacing model
		public const double MaxFindKills = 150;
		public const double MaxCriticalMinutes = 30;
		// A frequent host makes at least this share of the spawns of its best biome (BiomeSlots.PlannedShare), or half the
		// average share of that biome's enemies when it holds many (MinShare)
		public const double MinHostShare = 0.05;

		// Hosts given beyond MaxPerHost (every host of their tiers was full), and requirements left without enough hosts
		// (no natural enemy at all at or below their tier): logged, and 0 in the tests with the default settings
		public static int Overflows { get; private set; }
		public static int Missing { get; private set; }

		// Extra requirement providers (vanilla recipe ingredients, a pickaxe per mining level), added by the recipe system
		public static readonly List<Func<IEnumerable<Requirement>>> ExtraRequirements = new();

		private static Dictionary<int, List<PlannedDrop>> byNpc;
		private static List<Requirement> requirements;
		private static readonly Dictionary<(int npc, int tier), bool> frequent = new();
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
			Overflows = 0;
			Missing = 0;
			frequent.Clear();
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
			byNpc = Assign(all, SeedSystem.WorldSeed, out int overflows, out int missing);
			requirements = all;
			Overflows = overflows;
			Missing = missing;
			if (overflows > 0 || missing > 0)
			{
				ModContent.GetInstance<UltimateRandomizer>().Logger.Warn($"Guaranteed sources: {overflows} host(s) beyond {MaxPerHost} guaranteed items, {missing} host(s) missing");
			}
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

		// Who picks its hosts first: the critical stations (they need the Forest's critical hosts, which every other item
		// would fill otherwise), the other basic stations, the pickaxes, the materials, the progression and themed stations,
		// then the summons
		private static int Priority(Requirement r)
		{
			if (r.Critical) return 0;
			if (r.Station && Stations.Basic().Any(s => s.info.Item == r.Item)) return 1;
			if (r.Tool) return 2;
			if (!r.Station) return 3;
			return Protection.IsBossSummon(r.Item) ? 5 : 4;
		}

		// The hosts of each requirement with a world seed and the current spawn map (the tests plan other seeds with
		// BiomeSlots.WithMap). A host carries at most MaxPerHost items while some other host of the tier has room; when every
		// one is full, the least loaded takes one more (counted in overflows): a requirement never loses a host silently.
		// Among the least loaded candidates, the enemies met most (steady share of their best biome) are picked more often;
		// the chances follow from the hosts (Chance, CriticalChance)
		public static Dictionary<int, List<PlannedDrop>> Assign(List<Requirement> all, int seed, out int overflows, out int missing)
		{
			overflows = 0;
			missing = 0;
			Dictionary<int, List<PlannedDrop>> result = new();
			List<int>[] hosts = new List<int>[Tiers.Count];
			List<int>[] natural = new List<int>[Tiers.Count];
			for (int t = 0; t < Tiers.Count; t++)
			{
				hosts[t] = Hosts(t);
				natural[t] = NaturalHosts(t).OrderBy(DetRandom.NpcKey).ToList();
			}
			Dictionary<int, int> load = new();
			float multiplier = ProgressionConfig.Instance.GuaranteedDropMultiplier;
			foreach (Requirement requirement in all.OrderBy(Priority).ThenBy(r => r.Tier).ThenBy(r => DetRandom.ItemKey(r.Item)))
			{
				Random rng = new(DetRandom.Hash(seed, RollKey.Guaranteed, DetRandom.ItemKey(requirement.Item)));
				List<int> chosen = new();
				bool single = requirement.Station || requirement.Tool;
				bool Free(int n) => !chosen.Contains(n) && load.GetValueOrDefault(n) < MaxPerHost;
				bool Other(int n) => !chosen.Contains(n);
				bool Frequent(int n) => IsFrequentHost(n, requirement.Tier);
				// At the highest tier that has some (nothing spawns from tier 8, post-Cultist, on: such items go to tier 7 or
				// lower hosts, a little earlier than their tier)
				List<int> Search(List<int>[] lists, Func<int, bool> filter)
				{
					for (int t = Tiers.Clamp(requirement.Tier); t >= 0; t--)
					{
						List<int> found = Matching(lists[t], filter);
						if (found != null) return found;
					}
					return null;
				}
				for (int h = 0; h < HostsPerItem; h++)
				{
					int host;
					List<int> forest = requirement.Critical && h < CriticalHostSets
						? CriticalHosts(h).Where(n => Free(n) && IsNaturalHost(n, requirement.Tier) && NpcTiers.BaseTier(n) <= requirement.Tier).ToList()
						: null;
					if (forest != null && forest.Count > 0)
					{
						// The Forest enemies the early kills meet most, a little less once loaded (the owner of the Purple
						// Slime slot is seldom met by day, those of the Blue and Green Slimes are)
						host = PickWeighted(forest, n => (PacingModel.EarlyKillShare(k => k == n) + 0.002) / (1 + load.GetValueOrDefault(n)), rng);
					}
					else
					{
						// Frequent hosts first at any lower tier (a station or a summon must not hide on a rare enemy: when they
						// are all full, the least loaded takes one more), then any common one; when all are full, the least
						// loaded of them takes one more
						List<int> candidates = Search(natural, n => Free(n) && Frequent(n));
						if (candidates == null && single)
						{
							candidates = Search(natural, n => Other(n) && Frequent(n));
							if (candidates != null) overflows++;
						}
						candidates ??= Search(hosts, Free);
						if (candidates == null)
						{
							candidates = Search(natural, n => Other(n) && Frequent(n)) ?? Search(hosts, Other);
							if (candidates != null) overflows++;
						}
						if (candidates == null)
						{
							missing += HostsPerItem - h;
							break;
						}
						// Spread the load: the least used hosts, the most met of them more often (never a fixed order, or the
						// same enemies would take the guarantees in every world)
						int least = candidates.Min(n => load.GetValueOrDefault(n));
						List<int> best = candidates.Where(n => load.GetValueOrDefault(n) == least).ToList();
						host = PickWeighted(best, n => Math.Max(0.005, HostShare(n, requirement.Tier)), rng);
					}
					chosen.Add(host);
					load[host] = load.GetValueOrDefault(host) + 1;
				}
				if (chosen.Count == 0) continue;
				// Critical stations: one chance for all their hosts, from the early kills that meet them
				float critical = requirement.Critical ? CriticalChance(chosen) : 0f;
				foreach (int host in chosen)
				{
					float chance = requirement.Critical ? critical : Chance(requirement, host);
					chance = Math.Min(1f, chance * multiplier);
					if (single) chance = Math.Max(MinStationChance, chance);
					(int min, int max) = single ? (1, 1) : StackRules.Range(requirement.Item);
					if (!result.TryGetValue(host, out List<PlannedDrop> drops)) result[host] = drops = new List<PlannedDrop>();
					drops.Add(new PlannedDrop { Item = requirement.Item, StackMin = min, StackMax = Math.Max(min, max), Chance = chance, Extra = true, Guaranteed = true });
				}
			}
			return result;
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

		// The chance of a guaranteed item on a host (before GuaranteedDropMultiplier): about StationTargetKills (stations,
		// summons, tools) or MaterialTargetKills kills of the host's best biome per drop, within the bounds
		public static float Chance(Requirement requirement, int host)
		{
			bool single = requirement.Station || requirement.Tool;
			double share = HostShare(host, requirement.Tier);
			if (share <= 0) return single ? UnknownShareStationChance : MaterialChance;
			return single
				? (float)Math.Clamp(1 / (StationTargetKills * share), StationChance, MaxStationChance)
				: (float)Math.Clamp(1 / (MaterialTargetKills * share), MaterialChance, MaxMaterialChance);
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

		// Expected kills in the best biome of the easiest host before the item drops, and the bound GuaranteedFindTime allows
		// that host (infinity: no host with a known share)
		public static (double kills, double bound) FindKills(int item, int tier)
		{
			(double kills, double bound) best = (double.PositiveInfinity, MaxFindKills);
			foreach (int host in HostsOf(item))
			{
				double share = HostShare(host, tier);
				float chance = For(host).Where(d => d.Item == item).Sum(d => d.Chance);
				if (share <= 0 || chance <= 0 || 1 / (share * chance) >= best.kills) continue;
				(int biome, _, _) = BiomeSlots.BestBiome(host, tier);
				best = (1 / (share * chance), Math.Max(MaxFindKills, biome >= 0 ? 1 / (MaxStationChance * MinShare(biome, tier)) : MaxFindKills));
			}
			return best;
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
		public static Dictionary<int, List<PlannedDrop>> PlanWith(int seed, BiomeSlotMap map, out int overflows, out int missing)
		{
			Build();
			// The frequency cache follows the spawn map: it is rebuilt for the other map, then for this world's again
			using (BiomeSlots.WithMap(map)) return Assign(requirements, seed, out overflows, out missing);
		}

		// Critical stations (work bench, furnace, anvil, table, chair) are carried by the enemies met most in the first hour:
		// slimes by day (every colour shares the Blue Slime's type), zombies and demon eyes of every kind by night (each kind
		// has its own Forest slot: vanilla picks one at random). Other hosts are judged by their spawn share
		private const int CriticalHostSets = 2;

		// The enemies that spawn in those Forest slots (redistributed by biome, else the vanilla ones)
		public static int[] CriticalHosts(int set) => BiomeSlots.ForestFamilyHosts(BiomeSlots.SpawnMap, set == 0).ToArray();

		// The hosts that pass the filter, or null
		private static List<int> Matching(List<int> hosts, Func<int, bool> filter)
		{
			List<int> candidates = hosts.Where(filter).ToList();
			return candidates.Count > 0 ? candidates : null;
		}

		// A natural host met often at a tier: not rare, no event enemy, and at least MinShare of the spawns of its best
		// biome, with a slot that is not rare (BiomeSlots.PlannedShare: the redistributed slots, else the vanilla ones).
		// An enemy without any slot (modded, without a bestiary biome) only needs bestiary rarity 0.
		public static bool IsFrequentHost(int npc, int tier)
		{
			FollowMap();
			if (!frequent.TryGetValue((npc, tier), out bool result)) frequent[(npc, tier)] = result = ComputeFrequent(npc, tier);
			return result;
		}

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
				Findability.Reset();
			}
			frequent.Clear();
			shares.Clear();
			hostTiers = null;
			frequentMap = map;
		}

		private static bool ComputeFrequent(int npc, int tier)
		{
			if (!IsNaturalHost(npc, tier)) return false;
			if (ContentSamples.NpcsByNetId.TryGetValue(npc, out NPC sample) && sample.rarity > 0) return false;
			// Only an enemy without any slot is judged by its rarity (or any enemy when spawns are swapped another way); one
			// whose slots never open in this world is never met
			if (!BiomeSlots.SpawnsPlanned || !BiomeSlots.HasAnySlot(npc)) return true;
			// Met on an ordinary day: the share of its best biome from slots neither rare nor conditional
			(int biome, _, _) = BiomeSlots.BestBiome(npc, tier);
			return biome >= 0 && BiomeSlots.SteadyShare(biome, npc, tier) >= MinShare(biome, tier);
		}

		// The share part of ComputeFrequent, without the natural-host check (HostTiers relies on it)
		private static bool FrequentAt(int npc, int tier)
		{
			(int biome, _, _) = BiomeSlots.BestBiome(npc, tier);
			return biome >= 0 && BiomeSlots.SteadyShare(biome, npc, tier) >= MinShare(biome, tier);
		}

		// The steady share a frequent host needs in a biome at a tier: MinHostShare, lower in a biome with many enemies
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

		// Frequent natural enemies that may carry guaranteed items from a tier on (tests)
		public static List<int> FrequentHosts(int tier) => NaturalHosts(tier).Where(n => IsFrequentHost(n, tier)).ToList();

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

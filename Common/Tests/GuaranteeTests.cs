using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Tests
{
	// Station protection, guaranteed sources and findability of items
	public static class GuaranteeTests
	{
		public static void Register()
		{
			TestRunner.Register("BasicStationsUnprotected", BasicStationsUnprotected);
			TestRunner.Register("ProgressionStationsProtected", ProgressionStationsProtected);
			TestRunner.Register("ProgressionStationsGuaranteed", ProgressionStationsGuaranteed);
			TestRunner.Register("WorldSummonsProtected", WorldSummonsProtected);
			TestRunner.Register("BasicStationsFindable", BasicStationsFindable);
			TestRunner.Register("GuaranteedReachability", GuaranteedReachability);
			TestRunner.Register("GuaranteedLoadBalanced", GuaranteedLoadBalanced);
			TestRunner.Register("GuaranteedSeveralSeeds", GuaranteedSeveralSeeds);
			TestRunner.Register("GuaranteedSpread", GuaranteedSpread);
			TestRunner.Register("GuaranteedFindTime", GuaranteedFindTime);
			TestRunner.Register("GuaranteedHostsNotEvents", GuaranteedHostsNotEvents);
			TestRunner.Register("GuaranteedDeterminism", GuaranteedDeterminism);
			TestRunner.Register("FindabilityDeterminism", FindabilityDeterminism);
			TestRunner.Register("WorldMaterialsFindable", WorldMaterialsFindable);
			TestRunner.Register("BagContentFindable", BagContentFindable);
			TestRunner.Register("CrateAccessWorld", CrateAccessWorld);
			TestRunner.Register("HardmodeOresOfWorld", HardmodeOresOfWorld);
			TestRunner.Register("AltarDecisionKeepsCaches", AltarDecisionKeepsCaches);
		}

		private static string Name(int item) => Lang.GetItemNameValue(item);

		// Guaranteed drops are never on event, moon, seasonal or weather enemies, whatever the swap settings
		private static void GuaranteedHostsNotEvents()
		{
			int[] events =
			{
				NPCID.BloodZombie, NPCID.Drippler, NPCID.CorruptBunny, NPCID.Raven, NPCID.Ghost, NPCID.ZombieElf, NPCID.GingerbreadMan, NPCID.Eyezor,
				NPCID.Frankenstein, NPCID.Splinterling, NPCID.GoblinPeon, NPCID.PirateDeckhand, NPCID.SnowmanGangsta, NPCID.FlyingFish, NPCID.UmbrellaSlime
			};
			int[] common = { NPCID.Zombie, NPCID.BlueSlime, NPCID.DemonEye, NPCID.Skeleton, NPCID.CaveBat };
			List<string> failures = new();
			foreach (int type in events)
			{
				if (!GuaranteedSources.IsEventEnemy(type)) failures.Add($"{Lang.GetNPCNameValue(type)} not seen as an event enemy");
			}
			foreach (int type in common)
			{
				if (GuaranteedSources.IsEventEnemy(type)) failures.Add($"{Lang.GetNPCNameValue(type)} seen as an event enemy");
			}
			int hosts = 0;
			foreach (GuaranteedSources.Requirement requirement in GuaranteedSources.Requirements)
			{
				foreach (int host in GuaranteedSources.HostsOf(requirement.Item))
				{
					hosts++;
					if (events.Contains(host) || GuaranteedSources.IsEventEnemy(host)) failures.Add($"{Name(requirement.Item)} on {Lang.GetNPCNameValue(host)}");
				}
			}
			if (GuaranteedSources.Requirements.Count > 0) Check.That(hosts > 0, "no guaranteed host at all");
			Check.All(failures, "Guaranteed drops on event enemies");
		}

		private static void BasicStationsUnprotected()
		{
			if (ProgressionConfig.Instance.StationProtection == StationProtection.AllStations) Check.Skip("every station is protected");
			List<string> failures = new();
			foreach ((_, Stations.StationInfo info) in Stations.Basic())
			{
				if (!ItemPool.IsRandomizable(info.Item)) failures.Add(Name(info.Item));
			}
			Check.All(failures, "Basic stations still protected");
		}

		// Progression and themed stations are protected unless StationProtection is None; then they are randomized (and
		// guaranteed: ProgressionStationsGuaranteed)
		private static void ProgressionStationsProtected()
		{
			int[] items =
			{
				ItemID.Hellforge, ItemID.MythrilAnvil, ItemID.OrichalcumAnvil, ItemID.AdamantiteForge, ItemID.TitaniumForge, ItemID.TinkerersWorkshop,
				ItemID.ImbuingStation, ItemID.CrystalBall, ItemID.Autohammer, ItemID.LihzahrdFurnace, ItemID.LunarCraftingStation, ItemID.GlassKiln
			};
			bool protect = ProgressionConfig.Instance.StationProtection != StationProtection.None;
			HashSet<int> listed = ProgressionConfig.Instance.ProtectedItems.Where(d => d != null && !d.IsUnloaded).Select(d => d.Type).ToHashSet();
			List<string> failures = items.Where(i => !listed.Contains(i) && ItemPool.IsRandomizable(i) == protect)
				.Select(i => $"{Name(i)} {(protect ? "not protected" : "protected")}").ToList();
			Check.All(failures, "Stations against StationProtection");
		}

		// Every unprotected progression or themed station and every crafted or dropped boss summon is dropped by a frequent
		// enemy (not rare, no event, at least 5 % of its best biome) at or below its tier, with a chance of at least 1 %, and is
		// available by then in the recipe plan; every station a recipe needs can be placed
		private static void ProgressionStationsGuaranteed()
		{
			Check.Equal(0, Protection.SummonTier(ItemID.SuspiciousLookingEye), "tier of the Suspicious Looking Eye");
			Check.That(Protection.SummonTier(ItemID.GuideVoodooDoll) <= (int)Core.Tier.PostSkeletron, $"Guide Voodoo Doll at tier {Protection.SummonTier(ItemID.GuideVoodooDoll)}, after the Wall of Flesh");
			List<GuaranteedSources.Requirement> wanted = GuaranteedSources.OtherStationRequirements().Concat(GuaranteedSources.SummonRequirements()).ToList();
			if (wanted.Count == 0) Check.Skip("progression stations and boss summons are protected");
			RecipePlan plan = RecipePlanner.Get();
			List<string> failures = new();
			int summons = 0, stations = 0;
			slotShares.Clear();
			foreach (GuaranteedSources.Requirement want in wanted)
			{
				int item = want.Item;
				if (Protection.IsBossSummon(item)) summons++;
				else stations++;
				GuaranteedSources.Requirement merged = GuaranteedSources.Requirements.FirstOrDefault(r => r.Item == item);
				if (merged.Item != item)
				{
					failures.Add($"{Name(item)}: not among the requirements");
					continue;
				}
				int tier = merged.Tier;
				List<int> hosts = GuaranteedSources.HostsOf(item).ToList();
				if (hosts.Count < GuaranteedSources.HostsPerItem) failures.Add($"{Name(item)}: {hosts.Count} host(s), expected {GuaranteedSources.HostsPerItem}");
				// Recomputed here: the share from the slots through Pick, the chance from the enemy's real drop plan
				bool Good(int host)
				{
					if (GuaranteedSources.HostTier(host) > tier || GuaranteedSources.IsEventEnemy(host) || ContentSamples.NpcsByNetId[host].rarity > 0) return false;
					(double share, int biome, bool common, bool anySlot) = SlotShare(host, tier);
					if (anySlot && BiomeSlots.SpawnsPlanned && (biome < 0 || share < GuaranteedSources.MinShare(biome, tier) - 0.005 || !common)) return false;
					return RandomNpcDropRule.Plan(host, Main.expertMode).Any(d => d.Guaranteed && d.Item == item && d.Chance >= 0.01f - 1e-6f);
				}
				if (!hosts.Any(Good))
				{
					string list = hosts.Count == 0 ? "none" : string.Join(", ", hosts.Select(h => $"{Lang.GetNPCNameValue(h)} (tier {GuaranteedSources.HostTier(h)}, best biome "
						+ $"{SlotShare(h, tier).share:P1})"));
					failures.Add($"{Name(item)} (tier {tier}): no frequent host, hosts: {list}");
				}
				if (Findability.Tier(item) > tier) failures.Add($"{Name(item)} found at tier {Findability.Tier(item)} > {tier}");
				if (item < plan.Availability.Length && plan.Availability[item] > tier) failures.Add($"{Name(item)} available at tier {plan.Availability[item]} > {tier}");
			}
			// Every station a live recipe needs can be placed (modded stations keep their own sources)
			HashSet<int> tiles = new();
			for (int i = 0; i < Recipe.numRecipes; i++)
			{
				if (!Main.recipe[i].Disabled) tiles.UnionWith(Main.recipe[i].requiredTile.Where(t => t >= 0));
			}
			foreach (int tile in tiles.OrderBy(t => t))
			{
				if (Stations.Info(tile).Kind == StationKind.Unknown) continue;
				if (RecipePlanner.StationTier(tile, plan.Availability) >= Findability.Never) failures.Add($"{TileID.Search.GetName(tile)} can never be placed");
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Guaranteed: {stations} progression or themed stations, {summons} boss summons");
			Check.All(failures, "Stations and summons without a frequent source");
		}

		private const int ShareRolls = 200;
		private static readonly Dictionary<(BiomeSlotMap map, int band), Dictionary<int, Dictionary<int, double>>> slotShares = new();

		// Biome, then type, then share of the biome's enemy spawns, from the slots open in a band: each slot weighs 1 (rare 0.1,
		// conditional 0.5) and is split by sampling Pick
		private static Dictionary<int, Dictionary<int, double>> SlotShares(BiomeSlotMap map, int band)
		{
			if (slotShares.TryGetValue((map, band), out Dictionary<int, Dictionary<int, double>> cached)) return cached;
			Dictionary<int, Dictionary<int, double>> result = new();
			Dictionary<int, double> totals = new();
			foreach (BiomeSlot slot in map.Slots)
			{
				if (slot.Critter || slot.AbsentEvil || slot.Band > band) continue;
				double weight = (slot.Rare ? 0.1 : 1) * (slot.Conditional ? 0.5 : 1);
				totals[slot.Biome] = totals.GetValueOrDefault(slot.Biome) + weight;
				if (!result.TryGetValue(slot.Biome, out Dictionary<int, double> byType)) result[slot.Biome] = byType = new Dictionary<int, double>();
				for (int k = 0; k < ShareRolls; k++)
				{
					int type = BiomeSlots.TypeOf(BiomeSlots.Pick(slot, (k + 0.5) / ShareRolls));
					byType[type] = byType.GetValueOrDefault(type) + weight / ShareRolls;
				}
			}
			foreach ((int biome, Dictionary<int, double> byType) in result)
			{
				foreach (int type in byType.Keys.ToList()) byType[type] /= totals[biome];
			}
			slotShares[(map, band)] = result;
			return result;
		}

		// A type's best share among the biomes at a tier (BiomeVanillaChance mixed in by biome) and that biome, whether a slot
		// that is not rare holds it, and whether any slot mentions it at all
		private static (double share, int biome, bool common, bool anySlot) SlotShare(int type, int tier)
		{
			int band = BiomeSlots.BandOf(tier);
			BiomeSlotMap map = BiomeSlots.Active ? BiomeSlots.Current : BiomeSlots.Vanilla;
			double keep = BiomeSlots.Active ? EnemiesConfig.Instance.BiomeVanillaChance / 100.0 : 0;
			Dictionary<int, Dictionary<int, double>> planned = SlotShares(map, band);
			Dictionary<int, Dictionary<int, double>> vanilla = keep > 0 ? SlotShares(BiomeSlots.Vanilla, band) : null;
			double best = 0;
			int bestBiome = -1;
			foreach ((int biome, Dictionary<int, double> byType) in planned.OrderBy(pair => pair.Key))
			{
				double share = (1 - keep) * byType.GetValueOrDefault(type);
				if (vanilla != null && vanilla.TryGetValue(biome, out Dictionary<int, double> v)) share += keep * v.GetValueOrDefault(type);
				if (share > best)
				{
					best = share;
					bestBiome = biome;
				}
			}
			bool Holds(BiomeSlot s) => BiomeSlots.TypeOf(s.Owner) == type || s.Extra != 0 && BiomeSlots.TypeOf(s.Extra) == type
				|| s.EvilShared != null && s.EvilShared.Any(id => BiomeSlots.TypeOf(id) == type);
			bool common = map.Slots.Any(s => !s.Critter && !s.AbsentEvil && s.Band <= band && !s.Rare && Holds(s));
			bool anySlot = map.Slots.Any(s => Holds(s) || BiomeSlots.TypeOf(s.Original) == type);
			return (best, bestBiome, common, anySlot);
		}

		// The summons found in the world (Truffle Worm, Lihzahrd Power Cell, Gelatin Crystal, Prismatic Lacewing) and the
		// items sorted with the summons (life and mana crystals, mirrors) stay protected; with the default settings the crafted
		// and dropped ones (Guide Voodoo Doll, Suspicious Looking Eye) are randomized
		private static void WorldSummonsProtected()
		{
			List<string> failures = new();
			foreach (int item in Protection.WorldSummons.Concat(new int[] { ItemID.LifeCrystal, ItemID.ManaCrystal, ItemID.LifeFruit, ItemID.MagicMirror }))
			{
				if (ItemPool.IsValid(item) && !ItemPool.Info[item].Protected) failures.Add($"{Name(item)} is not protected");
			}
			if (!Protection.IsBossSummon(ItemID.GuideVoodooDoll) || Protection.IsBossSummon(ItemID.LifeCrystal)) failures.Add("wrong boss summon set");
			Check.All(failures, "World summons");
			if (ProgressionConfig.Instance.AutoProtectBossSummons) Check.Skip("every boss summon is protected");
			HashSet<int> listed = ProgressionConfig.Instance.ProtectedItems.Where(d => d != null && !d.IsUnloaded).Select(d => d.Type).ToHashSet();
			foreach (int item in new[] { ItemID.GuideVoodooDoll, ItemID.SuspiciousLookingEye })
			{
				if (listed.Contains(item)) Check.Skip($"{Name(item)} is in the protected items (an old list: reset it)");
				if (!ItemPool.IsRandomizable(item)) failures.Add($"{Name(item)} is protected");
			}
			Check.All(failures, "Crafted summons");
		}

		private static void BasicStationsFindable()
		{
			List<GuaranteedSources.Requirement> stations = GuaranteedSources.StationRequirements().ToList();
			if (stations.Count == 0) Check.Skip("no basic station is randomized");
			List<int> pots = GuaranteedSources.PotItems(0);
			List<string> failures = new();
			foreach (GuaranteedSources.Requirement station in stations)
			{
				if (Findability.Tier(station.Item) > station.Tier) failures.Add($"{Name(station.Item)} findable at tier {Findability.Tier(station.Item)}");
				if (!GuaranteedSources.HostsOf(station.Item).Any()) failures.Add($"{Name(station.Item)} has no enemy");
				if (!pots.Contains(station.Item)) failures.Add($"{Name(station.Item)} not in pots");
			}
			Check.All(failures, "Basic stations can't be found");
		}

		private static void GuaranteedReachability()
		{
			List<string> failures = new();
			foreach (GuaranteedSources.Requirement requirement in GuaranteedSources.Requirements)
			{
				List<int> hosts = GuaranteedSources.HostsOf(requirement.Item).ToList();
				if (hosts.Count == 0) failures.Add($"{Name(requirement.Item)}: no enemy");
				foreach (int host in hosts)
				{
					if (GuaranteedSources.HostTier(host) > requirement.Tier) failures.Add($"{Name(requirement.Item)} on {ContentSamples.NpcsByNetId[host].TypeName}, tier {GuaranteedSources.HostTier(host)} > {requirement.Tier}");
					if (!EnemySwapPool.IsSwappable(host)) failures.Add($"{ContentSamples.NpcsByNetId[host].TypeName} is not a natural enemy");
					if (!RandomNpcDropRule.Plan(host, Main.expertMode).Any(d => d.Guaranteed && d.Item == requirement.Item)) failures.Add($"{Name(requirement.Item)} missing from the drops of {ContentSamples.NpcsByNetId[host].TypeName}");
				}
			}
			Check.All(failures, "Guaranteed items unreachable");
		}

		// At most MaxPerHost items per enemy, except the overflows the planner counted (every host of a tier was full); and
		// every requirement has all its hosts
		private static void GuaranteedLoadBalanced()
		{
			Dictionary<int, int> load = new();
			List<string> failures = new();
			foreach (GuaranteedSources.Requirement requirement in GuaranteedSources.Requirements)
			{
				List<int> hosts = GuaranteedSources.HostsOf(requirement.Item).ToList();
				if (hosts.Count != GuaranteedSources.HostsPerItem) failures.Add($"{Name(requirement.Item)}: {hosts.Count} host(s)");
				foreach (int host in hosts) load[host] = load.GetValueOrDefault(host) + 1;
			}
			int excess = load.Values.Sum(v => System.Math.Max(0, v - GuaranteedSources.MaxPerHost));
			if (excess > GuaranteedSources.Overflows)
			{
				failures.AddRange(load.Where(pair => pair.Value > GuaranteedSources.MaxPerHost).Select(pair => $"{ContentSamples.NpcsByNetId[pair.Key].TypeName}: {pair.Value}"));
			}
			if (GuaranteedSources.Missing > 0) failures.Add($"{GuaranteedSources.Missing} host(s) missing");
			if (GuaranteedSources.Overflows > 0) ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Guaranteed: {GuaranteedSources.Overflows} host(s) above {GuaranteedSources.MaxPerHost} items");
			Check.All(failures, "Enemies carrying too many guaranteed items");
		}

		// The guarantees are spread over the common enemies: few items per host, most frequent hosts of each tier used once
		// there are enough items, and by biome the enemies of the Forest's zombie and demon eye slots among the hosts
		private static void GuaranteedSpread()
		{
			List<GuaranteedSources.Requirement> requirements = GuaranteedSources.Requirements;
			if (requirements.Count == 0) Check.Skip("nothing is guaranteed");
			Dictionary<int, int> load = new();
			foreach (GuaranteedSources.Requirement requirement in requirements)
			{
				foreach (int host in GuaranteedSources.HostsOf(requirement.Item)) load[host] = load.GetValueOrDefault(host) + 1;
			}
			List<string> failures = new();
			List<string> summary = new();
			for (int t = 0; t < Tiers.Count; t++)
			{
				List<int> frequent = GuaranteedSources.FrequentHosts(t);
				List<int> used = load.Keys.Where(h => GuaranteedSources.HostTier(h) == t).ToList();
				int placements = used.Sum(h => load[h]);
				if (placements == 0) continue;
				int usedFrequent = frequent.Count(load.ContainsKey);
				summary.Add($"tier {t}: {placements} on {used.Count} hosts ({usedFrequent}/{frequent.Count} frequent), max {used.Max(h => load[h])}");
				// Least loaded first: every frequent host gets one before any gets a second (a few critical Forest hosts aside)
				int expected = System.Math.Min(frequent.Count, placements);
				if (usedFrequent < expected * 3 / 4) failures.Add($"tier {t}: {usedFrequent} of {frequent.Count} frequent hosts used for {placements} items");
				// Enough frequent hosts: none carries more than one beyond MaxPerHost
				if (frequent.Count * GuaranteedSources.MaxPerHost >= placements)
				{
					foreach (int host in used.Where(h => load[h] > GuaranteedSources.MaxPerHost + 1)) failures.Add($"{Lang.GetNPCNameValue(host)}: {load[host]} items");
				}
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info("Guaranteed spread: " + string.Join("; ", summary));
			if (BiomeSlots.Active && requirements.Any(r => r.Critical))
			{
				List<int> night = GuaranteedSources.CriticalHosts(1).Where(n => !GuaranteedSources.IsEventEnemy(n) && GuaranteedSources.HostTier(n) == 0).ToList();
				int carrying = night.Count(load.ContainsKey);
				if (carrying < System.Math.Min(3, night.Count)) failures.Add($"only {carrying} of the {night.Count} enemies of the Forest's zombie and demon eye slots carry guaranteed items");
			}
			Check.All(failures, "Guaranteed items concentrated on a few enemies");
		}

		// Every station, summon and tool drops from one of its hosts within MaxFindKills kills of that host's best biome
		// (1 / (share x chance); more in a crowded biome, see FindKills), critical stations within MaxCriticalMinutes of
		// early kills; the worst ones are logged
		private static void GuaranteedFindTime()
		{
			if (!BiomeSlots.SpawnsPlanned) Check.Skip("enemies are swapped without biomes");
			if (ProgressionConfig.Instance.GuaranteedDropMultiplier < 1) Check.Skip("GuaranteedDropMultiplier is below 1");
			List<(string name, double kills)> times = new();
			List<string> failures = new();
			foreach (GuaranteedSources.Requirement requirement in GuaranteedSources.Requirements.Where(r => r.Station || r.Tool))
			{
				// Critical stations get their chance from the pacing model: judged in its minutes
				if (requirement.Critical)
				{
					double minutes = GuaranteedSources.FindMinutes(requirement.Item);
					if (minutes > GuaranteedSources.MaxCriticalMinutes) failures.Add($"{Name(requirement.Item)}: {minutes:0} minutes of early kills");
					continue;
				}
				(double kills, double bound) = GuaranteedSources.FindKills(requirement.Item, requirement.Tier);
				// Hosts without any slot (modded enemies without a biome) have no share to judge
				if (double.IsInfinity(kills)) continue;
				times.Add((Name(requirement.Item), kills));
				if (kills > bound) failures.Add($"{Name(requirement.Item)}: {kills:0} kills, at most {bound:0} ({string.Join(", ", GuaranteedSources.HostsOf(requirement.Item).Select(h => $"{Lang.GetNPCNameValue(h)} {GuaranteedSources.HostShare(h, requirement.Tier):P1}"))})");
			}
			if (times.Count == 0) Check.Skip("no station, summon or tool is guaranteed");
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Guaranteed find time: median {times.Select(t => t.kills).OrderBy(k => k).ElementAt(times.Count / 2):0} biome kills, worst "
				+ string.Join(", ", times.OrderByDescending(t => t.kills).Take(5).Select(t => $"{t.name} {t.kills:0}")));
			Check.All(failures, "Stations, summons or tools too long to find");
		}

		private static int Seed(int i) => 2000 + i * 7919;

		// Planned with other seeds (and their spawn maps) without touching this world: every requirement gets all its hosts,
		// none later than its tier, and the critical stations a host in the Forest's critical slots by day and by night
		private static void GuaranteedSeveralSeeds()
		{
			List<GuaranteedSources.Requirement> requirements = GuaranteedSources.Requirements;
			if (requirements.Count == 0) Check.Skip("nothing is guaranteed");
			const int Seeds = 4;
			List<string> failures = new();
			int overflows = 0;
			for (int i = 0; i < Seeds; i++)
			{
				int seed = Seed(i);
				BiomeSlotMap map = BiomeSlots.Active ? BiomeSlots.Build(seed, EnemiesConfig.Instance.BiomeExtraEnemies) : BiomeSlots.SpawnMap;
				Dictionary<int, List<PlannedDrop>> plan = GuaranteedSources.PlanWith(seed, map, out int over, out int missing);
				overflows += over;
				if (missing > 0) failures.Add($"seed {seed}: {missing} host(s) missing");
				HashSet<int> day = new(), night = new();
				if (BiomeSlots.Active)
				{
					day = BiomeSlots.ForestFamilyHosts(map, true).ToHashSet();
					night = BiomeSlots.ForestFamilyHosts(map, false).ToHashSet();
				}
				foreach (GuaranteedSources.Requirement requirement in requirements)
				{
					List<int> hosts = plan.Where(pair => pair.Value.Any(d => d.Item == requirement.Item)).Select(pair => pair.Key).ToList();
					if (hosts.Count != GuaranteedSources.HostsPerItem) failures.Add($"seed {seed}: {Name(requirement.Item)} has {hosts.Count} host(s)");
					// Host tiers follow the spawn map: read with this seed's
					using (BiomeSlots.WithMap(map))
					{
						if (hosts.Any(h => GuaranteedSources.HostTier(h) > requirement.Tier)) failures.Add($"seed {seed}: {Name(requirement.Item)} on a later enemy");
					}
					if (!requirement.Critical || !BiomeSlots.Active) continue;
					if (day.Count > 0 && !hosts.Any(day.Contains)) failures.Add($"seed {seed}: {Name(requirement.Item)} without a Forest host by day");
					if (night.Count > 0 && !hosts.Any(night.Contains)) failures.Add($"seed {seed}: {Name(requirement.Item)} without a Forest host by night");
				}
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Guaranteed over {Seeds} seeds: {requirements.Count} requirements, {overflows} host(s) above {GuaranteedSources.MaxPerHost} items");
			Check.All(failures, "Guaranteed sources with other seeds");
		}

		private static string Snapshot() => string.Join(";", GuaranteedSources.Requirements.Select(r => r.Item + ":" + string.Join(",", GuaranteedSources.HostsOf(r.Item).OrderBy(h => h))));

		private static void GuaranteedDeterminism()
		{
			string first = Snapshot();
			GuaranteedSources.Reset();
			Findability.Reset();
			Check.Equal(first, Snapshot(), "guaranteed hosts after a rebuild");
		}

		private static void FindabilityDeterminism()
		{
			int[] sample = { ItemID.Gel, ItemID.Lens, ItemID.WorkBench, ItemID.Wood, ItemID.CopperOre, ItemID.IronOre, ItemID.Daybloom };
			int[] first = sample.Select(Findability.Tier).ToArray();
			Findability.Reset();
			int[] second = sample.Select(Findability.Tier).ToArray();
			Check.That(first.SequenceEqual(second), "findability changes after a rebuild");
		}

		// A bag's content counts only once the bag can be obtained: never seasonal bags, and treasure bags only in expert
		// mode (their boss drop is expert-only)
		// Crates the world can't give are no source: the other evil's crates (except in drunk worlds), the pre-hardmode
		// Hallowed crate (except drunk and remix worlds); the hardmode Divine crate comes in hardmode
		private static void CrateAccessWorld()
		{
			List<string> failures = new();
			void Expect(int crate, bool reachable)
			{
				bool actual = Findability.CrateAccessTier(crate) < Findability.Never;
				if (actual != reachable) failures.Add($"{Name(crate)}: {(actual ? "counted" : "not counted")}");
			}
			bool crimson = WorldGen.crimson;
			Expect(ItemID.CorruptFishingCrate, Main.drunkWorld || !crimson);
			Expect(ItemID.CorruptFishingCrateHard, Main.drunkWorld || !crimson);
			Expect(ItemID.CrimsonFishingCrate, Main.drunkWorld || crimson);
			Expect(ItemID.CrimsonFishingCrateHard, Main.drunkWorld || crimson);
			Expect(ItemID.HallowedFishingCrate, Main.drunkWorld || Main.remixWorld);
			Expect(ItemID.WoodenCrate, true);
			Check.Equal((int)Core.Tier.Hardmode, Findability.CrateAccessTier(ItemID.HallowedFishingCrateHard), "tier of the Divine crate");
			Check.All(failures, "Crates counted wrongly for this world");
		}

		// Hardmode pair ores always count as findable (the altars must not change the world caches) and are never picked as
		// new recipe ingredients, so an ore the altars did not choose is never required
		private static void HardmodeOresOfWorld()
		{
			List<string> failures = new();
			int[] ores = { ItemID.CobaltOre, ItemID.PalladiumOre, ItemID.MythrilOre, ItemID.OrichalcumOre, ItemID.AdamantiteOre, ItemID.TitaniumOre };
			foreach (int ore in ores)
			{
				if (!OreMap.IsHardmodePairOre(ore)) failures.Add($"{Name(ore)} not seen as a hardmode pair ore");
				if (!OreMap.IsFindable(ore)) failures.Add($"{Name(ore)} not findable");
			}
			if (OreMap.IsHardmodePairOre(ItemID.CopperOre) || OreMap.IsHardmodePairOre(ItemID.DemoniteOre)) failures.Add("a pre-hardmode ore is seen as a hardmode pair ore");
			if (RecipeSystem.Applied && CraftingConfig.Instance.IngredientMode == IngredientMode.Randomized)
			{
				RecipePlan plan = RecipePlanner.Get();
				for (int i = 0; i < plan.Ingredients.Length; i++)
				{
					if (plan.Ingredients[i] == null) continue;
					HashSet<int> originals = RecipeSystem.OriginalIngredients(i).Select(o => o.type).ToHashSet();
					foreach ((int type, _) in plan.Ingredients[i])
					{
						if (!originals.Contains(type) && OreMap.IsHardmodePairOre(type)) failures.Add($"{Name(RecipeSystem.OriginalResult(i))} now needs {Name(type)}");
					}
				}
			}
			Check.All(failures, "Hardmode ores");
		}

		// An altar deciding Cobalt or Palladium changes nothing the world caches hold: same requirements, same guaranteed
		// hosts, same findability. The world's choice is changed only for the test and restored
		private static void AltarDecisionKeepsCaches()
		{
			if (Main.netMode == NetmodeID.MultiplayerClient) Check.Skip("the server decides the ores");
			string Describe()
			{
				ItemPool.OnSeedChanged();
				string requirements = string.Join(",", GuaranteedSources.Requirements.Select(r => $"{r.Item}@{r.Tier}:{string.Join("/", GuaranteedSources.HostsOf(r.Item).OrderBy(h => h))}"));
				string tiers = string.Join(",", Enumerable.Range(1, ItemPool.Info.Length - 1).Select(Findability.Tier));
				return requirements + "|" + tiers;
			}
			int saved = WorldGen.SavedOreTiers.Cobalt;
			try
			{
				WorldGen.SavedOreTiers.Cobalt = -1;
				string undecided = Describe();
				WorldGen.SavedOreTiers.Cobalt = TileID.Cobalt;
				string cobalt = Describe();
				WorldGen.SavedOreTiers.Cobalt = TileID.Palladium;
				string palladium = Describe();
				Check.Equal(undecided.GetHashCode(), cobalt.GetHashCode(), "guaranteed sources and findability once Cobalt is chosen");
				Check.Equal(undecided.GetHashCode(), palladium.GetHashCode(), "guaranteed sources and findability once Palladium is chosen");
			}
			finally
			{
				WorldGen.SavedOreTiers.Cobalt = saved;
				ItemPool.OnSeedChanged();
			}
		}

		private static void BagContentFindable()
		{
			List<string> failures = new();
			foreach (int bag in Findability.SeasonalBags)
			{
				if (LootSnapshot.Items.ContainsKey(bag) && Findability.BagContentTier(bag) != Findability.Never) failures.Add($"{Name(bag)} counts as a source");
			}
			HashSet<int> bossBags = new();
			foreach (int boss in NpcTiers.BossTypes)
			{
				foreach (DropRateInfo entry in LootSnapshot.ForNpc(boss))
				{
					if (!ItemID.Sets.BossBag[entry.itemId]) continue;
					bossBags.Add(entry.itemId);
					if (Findability.WorldAllows(entry.conditions) != Main.expertMode) failures.Add($"{Name(entry.itemId)} from {Lang.GetNPCNameValue(boss)}: allowed = {!Main.expertMode} in {(Main.expertMode ? "expert" : "classic")}");
				}
			}
			Check.That(bossBags.Count >= 10, $"only {bossBags.Count} treasure bags found in boss drops");
			List<int> counted = bossBags.Where(b => Findability.BagContentTier(b) != Findability.Never).ToList();
			if (Main.expertMode)
			{
				failures.AddRange(bossBags.Except(counted).Select(b => $"{Name(b)} doesn't count in expert mode"));
			}
			// In Classic a treasure bag may still come from a random drop of another source, but never through an entry the
			// world rejects (its boss's expert-only drop): each counted bag needs a drop allowed in this world
			else
			{
				HashSet<int> allowed = AllowedDrops();
				failures.AddRange(counted.Where(b => !allowed.Contains(b)).Select(b => $"{Name(b)} counts in Classic without an allowed source"));
			}
			Check.All(failures, "Bag contents");
		}

		// Items some source counted by Findability drops with an allowed condition and a findable chance: natural non-event
		// enemies, bosses and bags (lucky plans, as Findability reads them)
		private static HashSet<int> AllowedDrops()
		{
			float minChance = ProgressionConfig.Instance.MinFindableChance / 100f;
			HashSet<int> items = new();
			void Add(List<PlannedDrop> plan)
			{
				foreach (PlannedDrop drop in plan)
				{
					if (drop.Chance >= minChance && Findability.WorldAllows(drop.Conditions)) items.Add(drop.Item);
				}
			}
			for (int t = 0; t < Tiers.Count; t++)
			{
				foreach (int npc in EnemySwapPool.NaturalEnemies(t).Where(n => !GuaranteedSources.IsEventEnemy(n))) Add(RandomNpcDropRule.Plan(npc, Main.expertMode, lucky: true, guaranteed: false));
			}
			foreach (int boss in NpcTiers.BossTypes) Add(RandomNpcDropRule.Plan(boss, Main.expertMode, lucky: true, guaranteed: false));
			foreach (int bag in LootSnapshot.Items.Keys.Where(b => !Findability.SeasonalBags.Contains(b))) Add(BagPlan.Plan(bag, lucky: true));
			return items;
		}

		private static void WorldMaterialsFindable()
		{
			int[] early = { ItemID.Wood, ItemID.StoneBlock, ItemID.DirtBlock, ItemID.SandBlock, ItemID.Cobweb, ItemID.Amethyst, ItemID.FallenStar };
			List<string> failures = early.Where(i => Findability.Tier(i) > 0).Select(i => $"{Name(i)} at tier {Findability.Tier(i)}").ToList();
			int ores = Enumerable.Range(1, ItemPool.Info.Length - 1).Count(t => OreMap.IsOre(t) && Findability.Tier(t) == 0);
			if (ores < 4) failures.Add($"only {ores} ores at tier 0");
			Check.All(failures, "World materials");
		}
	}
}

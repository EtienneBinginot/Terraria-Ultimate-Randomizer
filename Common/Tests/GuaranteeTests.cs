using System;
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
			TestRunner.Register("UpgradesOutOfPools", UpgradesOutOfPools);
			TestRunner.Register("BasicStationsFindable", BasicStationsFindable);
			TestRunner.Register("GuaranteedReachability", GuaranteedReachability);
			TestRunner.Register("GuaranteedBiomeHosts", GuaranteedBiomeHosts);
			TestRunner.Register("GuaranteedSeveralSeeds", GuaranteedSeveralSeeds);
			TestRunner.Register("GuaranteedBiomesSpread", GuaranteedBiomesSpread);
			TestRunner.Register("GuaranteedBiomesVaried", GuaranteedBiomesVaried);
			TestRunner.Register("GuaranteedFindTime", GuaranteedFindTime);
			TestRunner.Register("GuaranteedDayAndNight", GuaranteedDayAndNight);
			TestRunner.Register("GuaranteedHostLoad", GuaranteedHostLoad);
			TestRunner.Register("GuaranteedReachable", GuaranteedReachable);
			TestRunner.Register("GuaranteedHostsNotEvents", GuaranteedHostsNotEvents);
			TestRunner.Register("GuaranteedDeterminism", GuaranteedDeterminism);
			TestRunner.Register("FindabilityDeterminism", FindabilityDeterminism);
			TestRunner.Register("WorldMaterialsFindable", WorldMaterialsFindable);
			TestRunner.Register("BagContentFindable", BagContentFindable);
			TestRunner.Register("CrateAccessWorld", CrateAccessWorld);
			TestRunner.Register("HardmodeOresOfWorld", HardmodeOresOfWorld);
			TestRunner.Register("AltarDecisionKeepsCaches", AltarDecisionKeepsCaches);
			TestRunner.Register("PotOnlyUnfound", PotOnlyUnfound);
			TestRunner.Register("GuaranteesNotBelowTier", GuaranteesNotBelowTier);
		}

		// Carried by the progression bosses of its tier (GuaranteedSources.BossHosts), not by natural enemies
		private static bool OnBoss(int item) => GuaranteedSources.TryPlacement(item, out GuaranteedSources.Placement place) && place.Boss;

		// The latest tier a host of a guaranteed item may have: its own, or the tier it was shifted up to (a hard gate without
		// hosts of its tier)
		private static int MaxHostTier(GuaranteedSources.Placement place, int tier) => place.Shifted ? Math.Max(tier, place.Tier) : tier;

		private static int MaxHostTier(int item, int tier) => GuaranteedSources.TryPlacement(item, out GuaranteedSources.Placement place) ? MaxHostTier(place, tier) : tier;

		// No guaranteed item comes before its tier: placed at its own tier (never on lower hosts, the guaranteed drop has no
		// progression lock), each host met from that tier at the earliest (until 1.5.1 a biome of a tier also held the enemies
		// of the tier before, and a 200 pickaxe dropped before the mechanical bosses), a boss host of its own tier, and the
		// guaranteed hosts give it (GuaranteedSources.FoundTier, not Findability.Tier: random drops with the tier allowance
		// may come earlier) at its tier at the earliest. Only an ordinary item without any host of its tier may go lower
		// (Shifted, logged); a hard gate never. The Ancient Manipulator and the Celestial Sigil, when guaranteed, come from the
		// Lunatic Cultist's tier on: until 1.5.0 they fell on post-Plantera enemies and the Moon Lord could be summoned right
		// after Plantera. Also over other seeds
		private static void GuaranteesNotBelowTier()
		{
			List<GuaranteedSources.Requirement> requirements = GuaranteedSources.Requirements;
			if (requirements.Count == 0) Check.Skip("nothing is guaranteed");
			List<string> failures = new();
			int shiftedDown = 0;
			foreach (GuaranteedSources.Requirement requirement in requirements)
			{
				string name = Name(requirement.Item);
				int tier = Tiers.Clamp(requirement.Tier);
				bool gate = ItemPool.IsHardGate(requirement.Item);
				GuaranteedSources.TryPlacement(requirement.Item, out GuaranteedSources.Placement place);
				int placed = GuaranteedSources.PlacedTier(requirement.Item);
				bool lower = placed >= 0 && placed < tier;
				if (lower && (!place.Shifted || gate)) failures.Add($"{name} (tier {tier}{(gate ? ", hard gate" : "")}): placed at tier {placed}");
				if (lower && place.Shifted && !gate)
				{
					shiftedDown++;
					continue;
				}
				int found = GuaranteedSources.FoundTier(requirement);
				if (found < tier) failures.Add($"{name} (tier {tier}): its guaranteed hosts give it at tier {found}");
				bool boss = OnBoss(requirement.Item);
				foreach (int host in GuaranteedSources.HostsOf(requirement.Item))
				{
					int hostTier = GuaranteedSources.HostTier(host);
					if (hostTier < tier) failures.Add($"{name} (tier {tier}): on {Lang.GetNPCNameValue(host)} (tier {hostTier})");
					if (boss && !NpcTiers.IsBoss(host)) failures.Add($"{name}: {Lang.GetNPCNameValue(host)} is not a boss");
				}
				// A hard gate is never found before its true tier, whatever gives it
				if (gate && ProgressionConfig.Instance.TierGating && Findability.Tier(requirement.Item) < ItemPool.TrueTierOf(requirement.Item))
				{
					failures.Add($"{name} (hard gate): found at tier {Findability.Tier(requirement.Item)}, true tier {ItemPool.TrueTierOf(requirement.Item)}");
				}
			}
			// Named: tier 8 at least (the Cultist's), found by then
			foreach (int item in new[] { ItemID.LunarCraftingStation, ItemID.CelestialSigil })
			{
				if (!requirements.Any(r => r.Item == item)) continue;
				int tier = requirements.First(r => r.Item == item).Tier;
				if (tier < (int)Core.Tier.PostCultist) failures.Add($"{Name(item)} required at tier {tier}, before the Lunatic Cultist");
				if (Findability.Tier(item) < (int)Core.Tier.PostCultist) failures.Add($"{Name(item)} found at tier {Findability.Tier(item)}, before the Lunatic Cultist");
			}
			// Other seeds (and their spawn maps): never placed below the tier either
			for (int i = 0; i < 3; i++)
			{
				int seed = Seed(i);
				BiomeSlotMap map = BiomeSlots.Active ? BiomeSlots.Build(seed, EnemiesConfig.Instance.BiomeExtraEnemies) : BiomeSlots.SpawnMap;
				GuaranteedSources.PlanWith(seed, map, out _, out Dictionary<int, GuaranteedSources.Placement> places);
				foreach (GuaranteedSources.Requirement requirement in requirements)
				{
					if (places.TryGetValue(requirement.Item, out GuaranteedSources.Placement place) && place.Tier < Tiers.Clamp(requirement.Tier)
						&& (!place.Shifted || ItemPool.IsHardGate(requirement.Item)))
					{
						failures.Add($"seed {seed}: {Name(requirement.Item)} placed at tier {place.Tier} < {requirement.Tier}");
					}
				}
			}
			int onBosses = requirements.Count(r => OnBoss(r.Item));
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Guaranteed below their tier (no host of their tier): {shiftedDown} item(s)");
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Guaranteed on bosses: {onBosses} item(s): "
				+ string.Join(", ", requirements.Where(r => OnBoss(r.Item)).Select(r => $"{Name(r.Item)} on {string.Join("/", GuaranteedSources.HostsOf(r.Item).Select(Lang.GetNPCNameValue))}")));
			Check.All(failures, "Guaranteed items before their tier");
		}

		// A pot only gives a guaranteed item nobody has found yet: the filter keeps exactly the items not obtained, in order,
		// and never changes the cached list of pot items
		private static void PotOnlyUnfound()
		{
			List<string> failures = new();
			List<int> fixedList = new() { ItemID.WorkBench, ItemID.Furnace, ItemID.IronAnvil };
			List<int> left = TileDropItem.Unfound(fixedList, item => item == ItemID.WorkBench);
			if (!left.SequenceEqual(new int[] { ItemID.Furnace, ItemID.IronAnvil })) failures.Add($"work bench obtained: {string.Join(", ", left.Select(Name))} left");
			if (fixedList.Count != 3) failures.Add("the list given was modified");
			if (TileDropItem.Unfound(fixedList, _ => true).Count != 0) failures.Add("items left although everything is obtained");
			List<int> pots = GuaranteedSources.PotItems(Tiers.Max);
			int before = pots.Count;
			HashSet<int> obtained = pots.Where((_, i) => i % 2 == 0).ToHashSet();
			List<int> unfound = TileDropItem.Unfound(pots, obtained.Contains);
			if (unfound.Any(obtained.Contains)) failures.Add("an obtained pot item is still given");
			if (unfound.Count != pots.Count(i => !obtained.Contains(i))) failures.Add($"{unfound.Count} pot items left, expected {pots.Count(i => !obtained.Contains(i))}");
			if (pots.Count != before || !ReferenceEquals(pots, GuaranteedSources.PotItems(Tiers.Max))) failures.Add("the cached pot items were modified");
			Check.All(failures, "Pot items");
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
				// The progression bosses of a tier without natural enemies (the Lunatic Cultist...) are no event
				if (OnBoss(requirement.Item)) continue;
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

		// Every unprotected progression or themed station and every crafted or dropped boss summon is dropped by the common
		// enemies (not rare, no event) of one biome at or below its tier, which make together at least MinBiomeShare of its
		// spawns, with a chance of at least 1 %, and is available by then in the recipe plan; every station a recipe needs can
		// be placed
		private static void ProgressionStationsGuaranteed()
		{
			Check.Equal(0, Protection.SummonTier(ItemID.SuspiciousLookingEye), "tier of the Suspicious Looking Eye");
			Check.That(Protection.SummonTier(ItemID.GuideVoodooDoll) <= (int)Core.Tier.PostSkeletron, $"Guide Voodoo Doll at tier {Protection.SummonTier(ItemID.GuideVoodooDoll)}, after the Wall of Flesh");
			List<GuaranteedSources.Requirement> wanted = GuaranteedSources.OtherStationRequirements().Concat(GuaranteedSources.SummonRequirements()).ToList();
			if (wanted.Count == 0) Check.Skip("progression stations and boss summons are protected");
			RecipePlan plan = RecipePlanner.Get();
			List<string> failures = new();
			int summons = 0, stations = 0;
			periodShares.Clear();
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
				int biome = GuaranteedSources.BiomeOf(item);
				int placed = GuaranteedSources.PlacedTier(item);
				if (hosts.Count < (biome >= 0 ? GuaranteedSources.MinHostsInBiome : 1)) failures.Add($"{Name(item)}: {hosts.Count} host(s)");
				// Recomputed here: the share from the slots through Pick, the chance from the enemy's real drop plan
				bool onBoss = OnBoss(item);
				bool Good(int host)
				{
					// A progression boss of the item's tier gives it at every kill
					if (onBoss) return NpcTiers.IsBoss(host) && GuaranteedSources.HostTier(host) == Tiers.Clamp(MaxHostTier(item, tier))
						&& RandomNpcDropRule.Plan(host, Main.expertMode).Any(d => d.Guaranteed && d.Item == item && d.Chance >= 1f - 1e-6f);
					if (GuaranteedSources.HostTier(host) > MaxHostTier(item, tier) || GuaranteedSources.IsEventEnemy(host) || ContentSamples.NpcsByNetId[host].rarity > 0) return false;
					return RandomNpcDropRule.Plan(host, Main.expertMode).Any(d => d.Guaranteed && d.Item == item && d.Chance >= 0.01f - 1e-6f);
				}
				List<int> bad = hosts.Where(h => !Good(h)).ToList();
				if (bad.Count > 0) failures.Add($"{Name(item)} (tier {tier}): on {string.Join(", ", bad.Select(h => $"{Lang.GetNPCNameValue(h)} (tier {GuaranteedSources.HostTier(h)})"))}");
				if (biome >= 0 && BiomeSlots.SpawnsPlanned)
				{
					// In each period the item is met in
					foreach (SlotTime period in Periods(GuaranteedSources.PlacedTime(item)))
					{
						double together = hosts.Sum(h => PeriodShareFromSlots(h, biome, placed, period));
						if (together < GuaranteedSources.MinBiomeShare - 0.005) failures.Add($"{Name(item)}: its enemies make {together:P1} of {BiomeSlots.BiomeName(biome)} ({period})");
					}
				}
				if (Findability.Tier(item) > tier) failures.Add($"{Name(item)} found at tier {Findability.Tier(item)} > {tier}");
				if (item < plan.Availability.Length && plan.Availability[item] > tier) failures.Add($"{Name(item)} available at tier {plan.Availability[item]} > {tier}");
			}
			// Every station a live recipe needs can be placed (other mods' ones too: Stations.ModdedInfo)
			HashSet<int> tiles = new();
			for (int i = 0; i < Recipe.numRecipes; i++)
			{
				if (!Main.recipe[i].Disabled) tiles.UnionWith(Main.recipe[i].requiredTile.Where(t => t >= 0));
			}
			foreach (int tile in tiles.OrderBy(t => t))
			{
				if (Stations.Info(tile).Kind == StationKind.Unknown) continue;
				if (RecipePlanner.StationTier(tile, plan.Availability) >= Findability.Never) failures.Add($"{Stations.Name(tile)} can never be placed");
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Guaranteed: {stations} progression or themed stations, {summons} boss summons");
			Check.All(failures, "Stations and summons without a frequent source");
		}

		private const int ShareRolls = 200;
		private static readonly Dictionary<(BiomeSlotMap map, int band, SlotTime period), Dictionary<int, Dictionary<int, double>>> periodShares = new();

		// The periods a placement's hosts are met in (Any: both)
		private static SlotTime[] Periods(SlotTime time) => time == SlotTime.Any ? new[] { SlotTime.Day, SlotTime.Night } : new[] { time };

		private static bool OpenIn(SlotTime slot, SlotTime period) => slot == SlotTime.Any || slot == period;

		// Biome, then type, then steady share of the biome's enemy spawns in one period (day or night), from the slots open in
		// a band and at that time: each slot weighs 1 (rare 0.1, conditional 0.5) in the total, only those neither rare nor
		// conditional count for the type, split by sampling Pick
		private static Dictionary<int, Dictionary<int, double>> PeriodShares(BiomeSlotMap map, int band, SlotTime period)
		{
			if (periodShares.TryGetValue((map, band, period), out Dictionary<int, Dictionary<int, double>> cached)) return cached;
			Dictionary<int, Dictionary<int, double>> result = new();
			Dictionary<int, double> totals = new();
			foreach (BiomeSlot slot in map.Slots)
			{
				if (slot.Critter || slot.AbsentEvil || slot.Band > band || !OpenIn(slot.Time, period)) continue;
				double weight = (slot.Rare ? 0.1 : 1) * (slot.Conditional ? 0.5 : 1);
				totals[slot.Biome] = totals.GetValueOrDefault(slot.Biome) + weight;
				if (slot.Rare || slot.Conditional) continue;
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
			periodShares[(map, band, period)] = result;
			return result;
		}

		// A type's steady share of a biome's spawns at a tier in one period (BiomeVanillaChance mixed in by biome)
		private static double PeriodShareFromSlots(int type, int biome, int tier, SlotTime period)
		{
			int band = BiomeSlots.BandOf(tier);
			BiomeSlotMap map = BiomeSlots.Active ? BiomeSlots.Current : BiomeSlots.Vanilla;
			double keep = BiomeSlots.Active ? EnemiesConfig.Instance.BiomeVanillaChance / 100.0 : 0;
			double share = (1 - keep) * (PeriodShares(map, band, period).TryGetValue(biome, out Dictionary<int, double> planned) ? planned.GetValueOrDefault(type) : 0);
			if (keep > 0 && PeriodShares(BiomeSlots.Vanilla, band, period).TryGetValue(biome, out Dictionary<int, double> vanilla)) share += keep * vanilla.GetValueOrDefault(type);
			return share;
		}

		// A biome met by day and by night gives each period at least MinPeriodHosts hosts of the item; one met at one time only
		// says so (its hosts all met then). Recomputed from the slots: a host counts for a period when it spawns in the biome
		// in a steady slot open then
		private static void GuaranteedDayAndNight()
		{
			if (!BiomeSlots.SpawnsPlanned) Check.Skip("enemies are swapped without biomes");
			if (GuaranteedSources.HostLimit < 2 * GuaranteedSources.MinPeriodHosts) Check.Skip("GuaranteedHostsPerItem is too low for hosts by day and by night");
			periodShares.Clear();
			List<string> failures = new();
			int both = 0, one = 0;
			foreach (GuaranteedSources.Requirement requirement in GuaranteedSources.Requirements.Where(r => !r.Critical))
			{
				int item = requirement.Item;
				int biome = GuaranteedSources.BiomeOf(item);
				if (biome < 0) continue;
				int tier = GuaranteedSources.PlacedTier(item);
				SlotTime time = GuaranteedSources.PlacedTime(item);
				List<int> hosts = GuaranteedSources.HostsOf(item).ToList();
				int day = hosts.Count(h => PeriodShareFromSlots(h, biome, tier, SlotTime.Day) > 0), night = hosts.Count(h => PeriodShareFromSlots(h, biome, tier, SlotTime.Night) > 0);
				string where = $"{Name(item)} in {BiomeSlots.BiomeName(biome)}";
				if (time == SlotTime.Any)
				{
					both++;
					if (day < GuaranteedSources.MinPeriodHosts) failures.Add($"{where}: {day} host(s) by day");
					if (night < GuaranteedSources.MinPeriodHosts) failures.Add($"{where}: {night} host(s) at night");
				}
				else
				{
					one++;
					int met = time == SlotTime.Day ? day : night;
					if (met < hosts.Count) failures.Add($"{where} ({time}): {hosts.Count - met} host(s) not met then");
				}
			}
			if (both + one == 0) Check.Skip("no item placed in a biome");
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Guaranteed periods: {both} items met by day and by night, {one} at one time only");
			Check.All(failures, "Guaranteed items missing a period");
		}

		// No host carries more than MaxItemsPerHost guaranteed items, plus one for a host kept although full (at most a
		// MaxForcedShare of the hosts), plus the items placed without the biome rules (critical stations of the Forest
		// fallback, common hosts without a biome); the busiest host gives
		// less than MaxDropsPerKill guaranteed stations, summons and tools per kill (materials, guaranteed only with vanilla
		// ingredients, come in stacks and much more often: logged)
		public const double MaxDropsPerKill = 0.6;
		public const double MaxForcedShare = 0.25;

		private static void GuaranteedHostLoad()
		{
			// The bound follows the default find time and chances
			ProgressionConfig progression = ProgressionConfig.Instance;
			if (!PacingTests.IsDefault(progression, "StationFindKills") || !PacingTests.IsDefault(progression, "GuaranteedDropMultiplier")) Check.Skip("guarantee settings are not the default ones");
			List<GuaranteedSources.Requirement> requirements = GuaranteedSources.Requirements;
			if (requirements.Count == 0) Check.Skip("nothing is guaranteed");
			HashSet<int> single = requirements.Where(r => r.Station || r.Tool).Select(r => r.Item).ToHashSet();
			Dictionary<int, int> forced = new();
			// Items placed without the biome rules: critical stations on the Forest fallback, common hosts without a biome
			Dictionary<int, int> fallback = new();
			HashSet<int> hosts = new();
			foreach (GuaranteedSources.Requirement requirement in requirements)
			{
				hosts.UnionWith(GuaranteedSources.HostsOf(requirement.Item));
				if (!GuaranteedSources.TryPlacement(requirement.Item, out GuaranteedSources.Placement place)) continue;
				// Bosses give their guaranteed items at every kill: no load to judge
				if (place.Boss)
				{
					hosts.ExceptWith(place.Hosts);
					continue;
				}
				if (place.Fallback || place.Biome < 0)
				{
					foreach (int host in place.Hosts) fallback[host] = fallback.GetValueOrDefault(host) + 1;
					continue;
				}
				foreach (int host in place.Forced ?? new List<int>()) forced[host] = forced.GetValueOrDefault(host) + 1;
			}
			List<string> failures = new();
			int maxForced = System.Math.Max(4, (int)(hosts.Count * MaxForcedShare));
			if (forced.Count > maxForced) failures.Add($"{forced.Count} of {hosts.Count} hosts kept although full, at most {maxForced}");
			int maxItems = 0, busiest = 0;
			double maxDrops = 0, maxAll = 0;
			foreach (int host in hosts.OrderBy(h => h))
			{
				List<PlannedDrop> drops = GuaranteedSources.For(host);
				int items = drops.Select(d => d.Item).Distinct().Count();
				int allowed = GuaranteedSources.MaxItemsPerHost + (forced.ContainsKey(host) ? 1 : 0) + fallback.GetValueOrDefault(host);
				if (items > allowed) failures.Add($"{Lang.GetNPCNameValue(host)}: {items} items, at most {allowed}");
				maxItems = System.Math.Max(maxItems, items);
				maxAll = System.Math.Max(maxAll, drops.Sum(d => (double)d.Chance));
				double expected = drops.Where(d => single.Contains(d.Item)).Sum(d => (double)d.Chance);
				if (expected > maxDrops)
				{
					maxDrops = expected;
					busiest = host;
				}
			}
			double bound = MaxDropsPerKill * System.Math.Max(1f, ProgressionConfig.Instance.GuaranteedDropMultiplier);
			if (maxDrops > bound) failures.Add($"{Lang.GetNPCNameValue(busiest)}: {maxDrops:0.00} guaranteed stations, summons or tools per kill, at most {bound:0.00}");
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Guaranteed load: {hosts.Count} hosts, at most {maxItems} items on one, {forced.Count} kept although full, "
				+ $"{fallback.Count} host(s) of items without the biome rules, busiest {(busiest != 0 ? Lang.GetNPCNameValue(busiest) : "none")} {maxDrops:0.000} stations, summons or tools per kill ({maxAll:0.000} with materials)");
			Check.All(failures, "Guaranteed hosts overloaded");
		}

		// Planned with other seeds (and this world): no item lands in a biome before it should (jungle and sky islands after
		// the Eye; mushroom, granite and marble caves, spider nests and the Underworld after the evil boss; the Dungeon after
		// Skeletron; the Hallow in hardmode; the Temple after Plantera), never in the Graveyard or the Meteor, and each host is
		// met in its biome through a steady slot of the placement's tier or below
		private static readonly Dictionary<SpawnBiome, int> Access = new()
		{
			[SpawnBiome.Jungle] = (int)Core.Tier.PostEyeOfCthulhu,
			[SpawnBiome.UndergroundJungle] = (int)Core.Tier.PostEyeOfCthulhu,
			[SpawnBiome.Sky] = (int)Core.Tier.PostEyeOfCthulhu,
			[SpawnBiome.Mushroom] = (int)Core.Tier.PostEvilBoss,
			[SpawnBiome.UndergroundMushroom] = (int)Core.Tier.PostEvilBoss,
			[SpawnBiome.Granite] = (int)Core.Tier.PostEvilBoss,
			[SpawnBiome.Marble] = (int)Core.Tier.PostEvilBoss,
			[SpawnBiome.SpiderNest] = (int)Core.Tier.PostEvilBoss,
			[SpawnBiome.Underworld] = (int)Core.Tier.PostEvilBoss,
			[SpawnBiome.Dungeon] = (int)Core.Tier.PostSkeletron,
			[SpawnBiome.Hallow] = (int)Core.Tier.Hardmode,
			[SpawnBiome.UndergroundHallow] = (int)Core.Tier.Hardmode,
			[SpawnBiome.Temple] = (int)Core.Tier.PostPlantera,
			[SpawnBiome.Graveyard] = int.MaxValue,
			[SpawnBiome.Meteor] = int.MaxValue
		};

		private static void GuaranteedReachable()
		{
			List<GuaranteedSources.Requirement> requirements = GuaranteedSources.Requirements;
			if (requirements.Count == 0) Check.Skip("nothing is guaranteed");
			if (!BiomeSlots.SpawnsPlanned) Check.Skip("enemies are swapped without biomes");
			const int Seeds = 4;
			List<string> failures = new();
			int placed = 0;
			bool mixed = BiomeSlots.Active && EnemiesConfig.Instance.BiomeVanillaChance > 0;
			for (int i = -1; i < Seeds; i++)
			{
				// -1: this world
				int seed = i < 0 ? SeedSystem.WorldSeed : Seed(i);
				BiomeSlotMap map = i >= 0 && BiomeSlots.Active ? BiomeSlots.Build(seed, EnemiesConfig.Instance.BiomeExtraEnemies) : BiomeSlots.SpawnMap;
				GuaranteedSources.PlanWith(seed, map, out _, out Dictionary<int, GuaranteedSources.Placement> places);
				foreach ((int item, GuaranteedSources.Placement place) in places.OrderBy(p => p.Key))
				{
					if (place.Fallback || place.Biome < 0) continue;
					placed++;
					string where = $"seed {seed}: {Name(item)} in {BiomeSlots.BiomeName(place.Biome)} at tier {place.Tier}";
					if (place.Biome < BiomeSlots.ModdedBiomeStart && Access.TryGetValue((SpawnBiome)place.Biome, out int access) && place.Tier < access)
					{
						failures.Add(access == int.MaxValue ? $"{where}: never farmable" : $"{where}: reached at tier {access}");
					}
					foreach (int host in place.Hosts)
					{
						bool Met(BiomeSlotMap m) => m.Slots.Any(s => s.Biome == place.Biome && !s.Critter && !s.AbsentEvil && !s.Rare && !s.Conditional && BiomeSlots.EffectiveTier(s) <= place.Tier
							&& BiomeSlots.Spawners(s).Any(id => BiomeSlots.TypeOf(id) == host));
						if (!Met(map) && !(mixed && Met(BiomeSlots.Vanilla))) failures.Add($"{where}: {Lang.GetNPCNameValue(host)} only met through stronger slots");
					}
				}
			}
			if (placed == 0) Check.Skip("no item placed in a biome");
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Guaranteed reachable: {placed} placements in a biome over {Seeds + 1} plans");
			Check.All(failures, "Guaranteed items in biomes out of reach");
		}

		// Life and mana upgrades are replaceable (Misc) but never rolled: in no bucket, no ingredient candidate, no guaranteed
		// source; drops and bags keep them (unless the player protected them)
		private static void UpgradesOutOfPools()
		{
			if (!ItemPool.Ready) Check.Skip("no item pool");
			HashSet<int> listed = ProgressionConfig.Instance.ProtectedItems.Where(d => d != null && !d.IsUnloaded).Select(d => d.Type).ToHashSet();
			List<string> failures = new();
			List<int>[,] candidates = RecipePlanner.IngredientCandidates();
			HashSet<int> guaranteed = GuaranteedSources.Requirements.Select(r => r.Item).ToHashSet();
			foreach (int item in Protection.Upgrades)
			{
				if (!ItemPool.IsValid(item) || listed.Contains(item)) continue;
				ItemInfo info = ItemPool.Info[item];
				if (info.Protected || !info.Special || !ItemPool.IsRandomizable(item)) failures.Add($"{Name(item)}: protected {info.Protected}, special {info.Special}");
				if (info.Category != ItemCategory.Misc) failures.Add($"{Name(item)} in {info.Category}");
				if (!DropHelper.KeepOriginal(item)) failures.Add($"{Name(item)} would be replaced in drops");
				if (guaranteed.Contains(item)) failures.Add($"{Name(item)} is guaranteed on enemies");
				for (int c = 0; c < ItemCategories.Count; c++)
				{
					for (int t = 0; t < Tiers.Count; t++)
					{
						if (ItemPool.Bucket((ItemCategory)c, t).Contains(item)) failures.Add($"{Name(item)} in the {(ItemCategory)c} bucket of tier {t}");
						if (candidates[c, t].Contains(item)) failures.Add($"{Name(item)} is an ingredient candidate");
					}
				}
			}
			Check.All(failures, "Upgrades");
		}

		// The summons found in the world (Truffle Worm, Lihzahrd Power Cell, Gelatin Crystal, Prismatic Lacewing) and the
		// items sorted with the summons (mirrors, phones) stay protected; with the default settings the crafted and dropped
		// ones (Guide Voodoo Doll, Suspicious Looking Eye) are randomized. Life and mana upgrades: UpgradesOutOfPools
		private static void WorldSummonsProtected()
		{
			List<string> failures = new();
			foreach (int item in Protection.WorldSummons.Concat(new int[] { ItemID.MagicMirror, ItemID.CellPhone }))
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
					if (GuaranteedSources.HostTier(host) > MaxHostTier(requirement.Item, requirement.Tier)) failures.Add($"{Name(requirement.Item)} on {ContentSamples.NpcsByNetId[host].TypeName}, tier {GuaranteedSources.HostTier(host)} > {requirement.Tier}");
					if (!EnemySwapPool.IsSwappable(host) && !OnBoss(requirement.Item)) failures.Add($"{ContentSamples.NpcsByNetId[host].TypeName} is not a natural enemy");
					if (!RandomNpcDropRule.Plan(host, Main.expertMode).Any(d => d.Guaranteed && d.Item == requirement.Item)) failures.Add($"{Name(requirement.Item)} missing from the drops of {ContentSamples.NpcsByNetId[host].TypeName}");
				}
			}
			Check.All(failures, "Guaranteed items unreachable");
		}

		// Every requirement is carried by the enemies of one biome: at least MinHostsInBiome of them (any count for the critical
		// Forest fallback), at most GuaranteedHostsPerItem, all spawning in that biome, with the same chance on each. Without a
		// biome, common hosts of the tier, no more than GuaranteedHostsPerItem; without natural enemies of its tier, the
		// progression bosses of that tier. No item is ever placed below its tier
		private static void GuaranteedBiomeHosts()
		{
			List<GuaranteedSources.Requirement> requirements = GuaranteedSources.Requirements;
			if (requirements.Count == 0) Check.Skip("nothing is guaranteed");
			int perItem = GuaranteedSources.HostLimit;
			List<string> failures = new();
			int withBiome = 0, below = 0;
			foreach (GuaranteedSources.Requirement requirement in requirements)
			{
				string name = Name(requirement.Item);
				List<int> hosts = GuaranteedSources.HostsOf(requirement.Item).ToList();
				int biome = GuaranteedSources.BiomeOf(requirement.Item);
				int tier = GuaranteedSources.PlacedTier(requirement.Item);
				if (hosts.Count == 0)
				{
					failures.Add($"{name}: no host");
					continue;
				}
				GuaranteedSources.TryPlacement(requirement.Item, out GuaranteedSources.Placement place);
				if (!place.Fallback && !place.Boss && hosts.Count > perItem) failures.Add($"{name}: {hosts.Count} hosts, at most {perItem}");
				if (biome >= 0)
				{
					withBiome++;
					if (!place.Fallback && hosts.Count < GuaranteedSources.MinHostsInBiome) failures.Add($"{name}: {hosts.Count} host(s) in {BiomeSlots.BiomeName(biome)}");
					foreach (int host in hosts.Where(h => BiomeSlots.PlannedShare(biome, h, tier) <= 0)) failures.Add($"{name}: {Lang.GetNPCNameValue(host)} is not met in {BiomeSlots.BiomeName(biome)}");
				}
				else if (BiomeSlots.SpawnsPlanned && requirement.Critical) failures.Add($"{name}: a critical station without a biome");
				List<float> chances = hosts.Select(h => GuaranteedSources.For(h).Where(d => d.Item == requirement.Item).Sum(d => d.Chance)).ToList();
				if (chances.Max() - chances.Min() > 1e-5f) failures.Add($"{name}: chances from {chances.Min():P2} to {chances.Max():P2}");
				if (tier >= 0 && tier < Tiers.Clamp(requirement.Tier))
				{
					below++;
					failures.Add($"{name}: placed at tier {tier} below its tier {requirement.Tier}");
				}
			}
			if (GuaranteedSources.Missing > 0) failures.Add($"{GuaranteedSources.Missing} item(s) without a host");
			if (BiomeSlots.SpawnsPlanned && withBiome == 0) failures.Add("no item placed in a biome");
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Guaranteed: {withBiome} of {requirements.Count} items placed in a biome, {below} below their tier");
			Check.All(failures, "Guaranteed items on their biome's enemies");
		}

		// The items of a tier spread over its biomes: the tier 0 items that are not critical stations use at least two biomes
		// when the map has two that qualify; the spread of every tier is logged
		private static void GuaranteedBiomesSpread()
		{
			List<GuaranteedSources.Requirement> requirements = GuaranteedSources.Requirements;
			if (requirements.Count == 0) Check.Skip("nothing is guaranteed");
			if (!BiomeSlots.SpawnsPlanned) Check.Skip("enemies are swapped without biomes");
			List<string> summary = new();
			List<string> failures = new();
			foreach (IGrouping<int, GuaranteedSources.Requirement> tier in requirements.Where(r => !r.Critical && GuaranteedSources.BiomeOf(r.Item) >= 0)
				.GroupBy(r => GuaranteedSources.PlacedTier(r.Item)).OrderBy(g => g.Key))
			{
				List<int> biomes = tier.Select(r => GuaranteedSources.BiomeOf(r.Item)).ToList();
				summary.Add($"tier {tier.Key}: " + string.Join(", ", biomes.GroupBy(b => b).OrderByDescending(g => g.Count()).Select(g => $"{BiomeSlots.BiomeName(g.Key)} {g.Count()}")));
				if (tier.Key == 0 && biomes.Count >= 2 && GuaranteedSources.CandidateBiomes(0) >= 2 && biomes.Distinct().Count() < 2)
				{
					failures.Add($"{biomes.Count} tier 0 items all in {BiomeSlots.BiomeName(biomes[0])} ({GuaranteedSources.CandidateBiomes(0)} biomes qualify)");
				}
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info("Guaranteed biomes: " + string.Join("; ", summary));
			Check.All(failures, "Guaranteed items gathered in one biome");
		}

		// The tier 0 stations (the critical ones included) of this world and of four other seeds land in at least
		// MinTierZeroBiomes different biomes (fewer only when fewer qualify or fewer stations exist) in all plans but
		// MaxNarrowPlans (biomes are picked at random, weighted: one plan may gather them), always among the biomes a new
		// character explores (Forest, caves, snow, desert, ocean, the world's evil: never the jungle, the mushroom caves, the
		// spider nests, the Underworld...)
		public const int MinTierZeroBiomes = 4;
		public const int MaxNarrowPlans = 1;

		private static readonly HashSet<SpawnBiome> StartBiomes = new()
		{
			SpawnBiome.Forest, SpawnBiome.Underground, SpawnBiome.Caverns, SpawnBiome.Snow, SpawnBiome.UndergroundSnow, SpawnBiome.Desert,
			SpawnBiome.UndergroundDesert, SpawnBiome.Ocean, SpawnBiome.Corruption, SpawnBiome.Crimson, SpawnBiome.UndergroundCorruption,
			SpawnBiome.UndergroundCrimson
		};

		private static void GuaranteedBiomesVaried()
		{
			List<GuaranteedSources.Requirement> stations = GuaranteedSources.Requirements.Where(r => r.Station && r.Tier == 0).ToList();
			if (stations.Count == 0) Check.Skip("no tier 0 station is guaranteed");
			if (!BiomeSlots.SpawnsPlanned) Check.Skip("enemies are swapped without biomes");
			const int Seeds = 4;
			List<string> failures = new();
			List<string> narrow = new();
			List<string> summary = new();
			for (int i = -1; i < Seeds; i++)
			{
				int seed = i < 0 ? SeedSystem.WorldSeed : Seed(i);
				BiomeSlotMap map = i >= 0 && BiomeSlots.Active ? BiomeSlots.Build(seed, EnemiesConfig.Instance.BiomeExtraEnemies) : BiomeSlots.SpawnMap;
				GuaranteedSources.PlanWith(seed, map, out _, out Dictionary<int, GuaranteedSources.Placement> places);
				List<int> biomes = new();
				foreach (GuaranteedSources.Requirement station in stations)
				{
					if (!places.TryGetValue(station.Item, out GuaranteedSources.Placement place) || place.Biome < 0) continue;
					biomes.Add(place.Biome);
					if (place.Biome >= BiomeSlots.ModdedBiomeStart) continue;
					if (!StartBiomes.Contains((SpawnBiome)place.Biome)) failures.Add($"seed {seed}: {Name(station.Item)} in {BiomeSlots.BiomeName(place.Biome)}");
				}
				int qualify;
				using (BiomeSlots.WithMap(map)) qualify = GuaranteedSources.CandidateBiomes(0);
				int wanted = System.Math.Min(MinTierZeroBiomes, System.Math.Min(qualify, biomes.Count));
				int distinct = biomes.Distinct().Count();
				summary.Add($"seed {seed}: {distinct} biomes for {biomes.Count} stations ({qualify} qualify)");
				if (distinct < wanted) narrow.Add($"seed {seed}: {biomes.Count} tier 0 stations in {distinct} biome(s) ({string.Join(", ", biomes.Distinct().Select(BiomeSlots.BiomeName))}), {qualify} qualify");
			}
			if (narrow.Count > MaxNarrowPlans) failures.AddRange(narrow);
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info("Guaranteed tier 0 biomes: " + string.Join("; ", summary));
			Check.All(failures, "Tier 0 stations in too few or far biomes");
		}

		// Every station, summon and tool drops within MaxFindKills kills of its biome (1 / sum of share x chance over its
		// hosts), critical stations within MaxCriticalMinutes of early kills; the worst ones are logged
		private static void GuaranteedFindTime()
		{
			if (!BiomeSlots.SpawnsPlanned) Check.Skip("enemies are swapped without biomes");
			if (ProgressionConfig.Instance.GuaranteedDropMultiplier < 1) Check.Skip("GuaranteedDropMultiplier is below 1");
			List<(string name, double kills)> times = new();
			List<string> failures = new();
			periodShares.Clear();
			foreach (GuaranteedSources.Requirement requirement in GuaranteedSources.Requirements.Where(r => r.Station || r.Tool))
			{
				// Critical stations get their chance from the pacing model: judged in its minutes
				if (requirement.Critical)
				{
					double minutes = GuaranteedSources.FindMinutes(requirement.Item);
					if (minutes > GuaranteedSources.MaxCriticalMinutes) failures.Add($"{Name(requirement.Item)}: {minutes:0} minutes of early kills");
					continue;
				}
				int biome = GuaranteedSources.BiomeOf(requirement.Item);
				if (biome < 0) continue;
				// Recomputed from the slots: in the period where it takes longest (the only one of a one-period placement),
				// 1 / sum of the hosts' steady share then x their chance
				int tier = GuaranteedSources.PlacedTier(requirement.Item);
				List<int> hosts = GuaranteedSources.HostsOf(requirement.Item).ToList();
				double kills = 0;
				SlotTime worst = SlotTime.Any;
				foreach (SlotTime period in Periods(GuaranteedSources.PlacedTime(requirement.Item)))
				{
					double rate = hosts.Sum(h => PeriodShareFromSlots(h, biome, tier, period) * GuaranteedSources.For(h).Where(d => d.Item == requirement.Item).Sum(d => d.Chance));
					double periodKills = rate > 0 ? 1 / rate : double.PositiveInfinity;
					if (periodKills > kills)
					{
						kills = periodKills;
						worst = period;
					}
				}
				// Hosts without any slot (modded enemies without a biome) have no share to judge
				if (hosts.All(h => !BiomeSlots.HasAnySlot(h))) continue;
				double bound = GuaranteedSources.MaxFindKills;
				times.Add((Name(requirement.Item), kills));
				if (kills > bound) failures.Add($"{Name(requirement.Item)}: {kills:0} kills ({worst}), at most {bound:0} ({BiomeSlots.BiomeName(biome)}: {string.Join(", ", hosts.Select(h => $"{Lang.GetNPCNameValue(h)} {PeriodShareFromSlots(h, biome, tier, worst):P1}"))})");
			}
			if (times.Count == 0) Check.Skip("no station, summon or tool is guaranteed");
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Guaranteed find time: median {times.Select(t => t.kills).OrderBy(k => k).ElementAt(times.Count / 2):0} biome kills, worst "
				+ string.Join(", ", times.OrderByDescending(t => t.kills).Take(5).Select(t => $"{t.name} {t.kills:0}")));
			Check.All(failures, "Stations, summons or tools too long to find");
		}

		private static int Seed(int i) => 2000 + i * 7919;

		// Planned with other seeds (and their spawn maps) without touching this world: every requirement gets its hosts (at
		// least MinHostsInBiome in a biome, all met there), none later than its tier, and the critical stations hosts in one of
		// the pacing model's early biomes that its early kills meet (the Forest fallback: a host in the Forest's critical
		// slots by day and by night)
		private static void GuaranteedSeveralSeeds()
		{
			List<GuaranteedSources.Requirement> requirements = GuaranteedSources.Requirements;
			if (requirements.Count == 0) Check.Skip("nothing is guaranteed");
			const int Seeds = 4;
			List<string> failures = new();
			int placedInBiome = 0;
			for (int i = 0; i < Seeds; i++)
			{
				int seed = Seed(i);
				BiomeSlotMap map = BiomeSlots.Active ? BiomeSlots.Build(seed, EnemiesConfig.Instance.BiomeExtraEnemies) : BiomeSlots.SpawnMap;
				Dictionary<int, List<PlannedDrop>> plan = GuaranteedSources.PlanWith(seed, map, out int missing, out Dictionary<int, GuaranteedSources.Placement> places);
				if (missing > 0) failures.Add($"seed {seed}: {missing} item(s) without a host");
				HashSet<int> day = new(), night = new();
				if (BiomeSlots.Active)
				{
					day = BiomeSlots.ForestFamilyHosts(map, true).ToHashSet();
					night = BiomeSlots.ForestFamilyHosts(map, false).ToHashSet();
				}
				foreach (GuaranteedSources.Requirement requirement in requirements)
				{
					List<int> hosts = plan.Where(pair => pair.Value.Any(d => d.Item == requirement.Item)).Select(pair => pair.Key).ToList();
					GuaranteedSources.Placement place = places.GetValueOrDefault(requirement.Item);
					if (hosts.Count == 0) failures.Add($"seed {seed}: {Name(requirement.Item)} has no host");
					else if (place.Biome >= 0)
					{
						placedInBiome++;
						if (!place.Fallback && hosts.Count < GuaranteedSources.MinHostsInBiome) failures.Add($"seed {seed}: {Name(requirement.Item)} has {hosts.Count} host(s)");
					}
					// Host tiers and shares follow the spawn map: read with this seed's
					using (BiomeSlots.WithMap(map))
					{
						if (hosts.Any(h => GuaranteedSources.HostTier(h) > MaxHostTier(place, requirement.Tier))) failures.Add($"seed {seed}: {Name(requirement.Item)} on a later enemy");
						if (place.Biome >= 0 && hosts.Any(h => BiomeSlots.PlannedShare(place.Biome, h, place.Tier) <= 0)) failures.Add($"seed {seed}: {Name(requirement.Item)} on an enemy outside {BiomeSlots.BiomeName(place.Biome)}");
						if (requirement.Critical && !place.Fallback)
						{
							if (place.Biome < 0 || !PacingModel.EarlyBiomes.Contains((SpawnBiome)place.Biome)) failures.Add($"seed {seed}: {Name(requirement.Item)} outside the early biomes");
							if (PacingModel.EarlyKillShare(hosts.Contains) <= 0) failures.Add($"seed {seed}: {Name(requirement.Item)} on enemies the early kills never meet");
						}
					}
					if (!requirement.Critical || !place.Fallback || !BiomeSlots.Active) continue;
					if (day.Count > 0 && !hosts.Any(day.Contains)) failures.Add($"seed {seed}: {Name(requirement.Item)} without a Forest host by day");
					if (night.Count > 0 && !hosts.Any(night.Contains)) failures.Add($"seed {seed}: {Name(requirement.Item)} without a Forest host by night");
				}
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Guaranteed over {Seeds} seeds: {requirements.Count} requirements, {placedInBiome} placements in a biome");
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

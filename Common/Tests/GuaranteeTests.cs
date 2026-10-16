using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
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
			TestRunner.Register("BasicStationsFindable", BasicStationsFindable);
			TestRunner.Register("GuaranteedReachability", GuaranteedReachability);
			TestRunner.Register("GuaranteedLoadBalanced", GuaranteedLoadBalanced);
			TestRunner.Register("GuaranteedDeterminism", GuaranteedDeterminism);
			TestRunner.Register("FindabilityDeterminism", FindabilityDeterminism);
			TestRunner.Register("WorldMaterialsFindable", WorldMaterialsFindable);
		}

		private static string Name(int item) => Lang.GetItemNameValue(item);

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

		private static void ProgressionStationsProtected()
		{
			if (ProgressionConfig.Instance.StationProtection == StationProtection.None) Check.Skip("no station is protected");
			int[] items =
			{
				ItemID.Hellforge, ItemID.MythrilAnvil, ItemID.OrichalcumAnvil, ItemID.AdamantiteForge, ItemID.TitaniumForge, ItemID.TinkerersWorkshop,
				ItemID.ImbuingStation, ItemID.CrystalBall, ItemID.Autohammer, ItemID.LihzahrdFurnace, ItemID.LunarCraftingStation, ItemID.GlassKiln
			};
			List<string> failures = items.Where(ItemPool.IsRandomizable).Select(Name).ToList();
			Check.All(failures, "Progression stations not protected");
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
					if (NpcTiers.BaseTier(host) > requirement.Tier) failures.Add($"{Name(requirement.Item)} on {ContentSamples.NpcsByNetId[host].TypeName}, tier {NpcTiers.BaseTier(host)} > {requirement.Tier}");
					if (!EnemySwapPool.IsSwappable(host)) failures.Add($"{ContentSamples.NpcsByNetId[host].TypeName} is not a natural enemy");
					if (!RandomNpcDropRule.Plan(host, Main.expertMode).Any(d => d.Guaranteed && d.Item == requirement.Item)) failures.Add($"{Name(requirement.Item)} missing from the drops of {ContentSamples.NpcsByNetId[host].TypeName}");
				}
			}
			Check.All(failures, "Guaranteed items unreachable");
		}

		private static void GuaranteedLoadBalanced()
		{
			Dictionary<int, int> load = new();
			foreach (GuaranteedSources.Requirement requirement in GuaranteedSources.Requirements)
			{
				foreach (int host in GuaranteedSources.HostsOf(requirement.Item)) load[host] = load.GetValueOrDefault(host) + 1;
			}
			List<string> failures = load.Where(pair => pair.Value > GuaranteedSources.MaxPerHost)
				.Select(pair => $"{ContentSamples.NpcsByNetId[pair.Key].TypeName}: {pair.Value}").ToList();
			Check.All(failures, "Enemies carrying too many guaranteed items");
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

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
				bool Good(int host) => NpcTiers.BaseTier(host) <= tier && GuaranteedSources.IsFrequentHost(host, tier)
					&& GuaranteedSources.For(host).Any(d => d.Item == item && d.Chance >= GuaranteedSources.MinStationChance - 1e-6f);
				if (!hosts.Any(Good))
				{
					string list = hosts.Count == 0 ? "none" : string.Join(", ", hosts.Select(h => $"{Lang.GetNPCNameValue(h)} (tier {NpcTiers.BaseTier(h)}, best biome "
						+ $"{BiomeSlots.BestBiome(h, tier).share:P1})"));
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

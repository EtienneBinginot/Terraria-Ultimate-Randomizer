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
	// The boss permutation (BossPermutation): boss drops and treasure bag contents shuffled between the bosses of a group
	public static class BossLootTests
	{
		public static void Register()
		{
			TestRunner.Register("BossPermutationConserves", BossPermutationConserves);
			TestRunner.Register("BossBagMatchesBoss", BossBagMatchesBoss);
			TestRunner.Register("BossPermutationTiers", BossPermutationTiers);
			TestRunner.Register("BossRuntime", BossRuntime);
		}

		private static int[] Seeds() => new[] { SeedSystem.WorldSeed, 12345, -98765 };

		private static BossPermutation.WorldPlan Plan(int seed)
		{
			if (!LootConfig.Instance.RandomizeMobDrops) Check.Skip("enemy drops are not randomized");
			if (!ItemPool.Ready) Check.Skip("the item pool is not built");
			BossPermutation.WorldPlan plan = BossPermutation.Build(seed);
			if (plan.Places.Count < 20 || plan.BossGroups.Count < 3) Check.Skip($"only {plan.Places.Count} permutable boss entries in {plan.BossGroups.Count} groups");
			return plan;
		}

		private static string NpcName(int netId) => ContentSamples.NpcsByNetId.TryGetValue(netId, out NPC sample) ? sample.TypeName : netId.ToString();

		private static string ItemName(int item) => Lang.GetItemNameValue(item);

		private static string Where(DropPermutation.Place place) =>
			BossPermutation.IsBagOnly(place) ? $"{ItemName(BossPermutation.BagOf(place.Owner))} entry {place.Index - BossPermutation.BagIndexOffset}" : $"{NpcName(place.Owner)} entry {place.Index}";

		// The permutation loses and duplicates nothing: ContentOf is a permutation of the places, every place holds the content
		// the plan says (boss tables and bag tables), and every vanilla boss drop or bag content that is not kept is in some
		// boss or bag table
		private static void BossPermutationConserves()
		{
			List<string> failures = new();
			void Fail(string message)
			{
				if (failures.Count < 25) failures.Add(message);
			}
			int places = 0;
			foreach (int seed in Seeds())
			{
				BossPermutation.WorldPlan plan = Plan(seed);
				places = plan.Places.Count;
				if (plan.ContentOf.Distinct().Count() != plan.Places.Count) Fail($"seed {seed}: a content given twice or never");
				HashSet<(int, int)> given = new();
				for (int p = 0; p < plan.Places.Count; p++)
				{
					DropPermutation.Place place = plan.Places[p];
					DropPermutation.Place source = plan.Places[plan.ContentOf[p]];
					if (BossPermutation.IsBagOnly(place)) continue;
					DropPermutation.Placed placed = plan.ByUnit[place.Owner][place.Index];
					if (!placed.Permuted || !placed.Rolled) Fail($"seed {seed}: {Where(place)} not permuted");
					if (placed.Entry.itemId != source.Entry.itemId || placed.FromNetId != source.Owner || placed.FromIndex != source.Index) Fail($"seed {seed}: {Where(place)} does not hold the content of {Where(source)}");
					if (!given.Add((placed.FromNetId, placed.FromIndex))) Fail($"seed {seed}: the content of {Where(source)} given twice");
				}
				HashSet<int> everywhere = plan.ByUnit.Values.Concat(plan.ByBag.Values).SelectMany(t => t).Select(p => p.Entry.itemId).ToHashSet();
				foreach (int unit in BossPermutation.Units())
				{
					List<DropRateInfo> entries = LootSnapshot.ForNpc(unit);
					bool[] rolled = DropPermutation.RolledEntries(entries, LootSnapshot.RulesOf(unit));
					if (!plan.ByUnit.TryGetValue(unit, out DropPermutation.Placed[] table) || table.Length != entries.Count) Fail($"seed {seed}: {NpcName(unit)} has no table of {entries.Count} entries");
					for (int i = 0; i < entries.Count; i++)
					{
						if (rolled[i] && !everywhere.Contains(entries[i].itemId)) Fail($"seed {seed}: {ItemName(entries[i].itemId)} of {NpcName(unit)} is nowhere");
					}
				}
				foreach (int bag in BossPermutation.Bags())
				{
					List<DropRateInfo> entries = LootSnapshot.ForItem(bag);
					bool[] rolled = DropPermutation.RolledEntries(entries, LootSnapshot.RulesOfItem(bag));
					for (int i = 0; i < entries.Count; i++)
					{
						if (rolled[i] && !everywhere.Contains(entries[i].itemId)) Fail($"seed {seed}: {ItemName(entries[i].itemId)} of {ItemName(bag)} is nowhere");
					}
				}
			}
			// The boss plans really use the tables in the permutation style
			if (BossPermutation.InStyle(NPCID.EyeofCthulhu))
			{
				foreach (int unit in BossPermutation.Units())
				{
					DropPermutation.Placed[] table = BossPermutation.For(unit);
					List<PlannedDrop> drops = RandomNpcDropRule.OriginalDrops(unit, BossParts.Owner(ContentSamples.NpcsByNetId[unit].type));
					if (drops.Count != table.Length || drops.Where((d, i) => d.Item != table[i].Entry.itemId).Any()) Fail($"{NpcName(unit)}: its plan is not its permuted table");
				}
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Boss permutation: {places} places");
			Check.All(failures, "Boss permutation");
		}

		// The bag of a boss holds the contents of that boss's not-expert places, each with its chance and stack in the bag it
		// comes from (BagVersion), and every not-expert place of the boss is carried
		private static void BossBagMatchesBoss()
		{
			List<string> failures = new();
			void Fail(string message)
			{
				if (failures.Count < 25) failures.Add(message);
			}
			int carriedCount = 0;
			foreach (int seed in Seeds())
			{
				BossPermutation.WorldPlan plan = Plan(seed);
				foreach (int bag in BossPermutation.Bags())
				{
					int unit = BossPermutation.BagUnit(bag);
					if (!plan.ByBag.TryGetValue(bag, out DropPermutation.Placed[] bagTable) || !plan.Carried.TryGetValue(bag, out List<int> carried))
					{
						Fail($"seed {seed}: {ItemName(bag)} has no table");
						continue;
					}
					List<int> expected = Enumerable.Range(0, plan.Places.Count)
						.Where(p => plan.Places[p].Owner == unit && !BossPermutation.IsBagOnly(plan.Places[p]) && BossPermutation.CarriedByBag(plan.Places[p].Access)).ToList();
					if (!expected.SequenceEqual(carried)) Fail($"seed {seed}: {ItemName(bag)} carries {carried.Count} of the {expected.Count} not-expert places of {NpcName(unit)}");
					for (int k = 0; k < carried.Count && k < bagTable.Length; k++)
					{
						carriedCount++;
						DropPermutation.Place place = plan.Places[carried[k]];
						DropPermutation.Placed onBoss = plan.ByUnit[unit][place.Index];
						DropPermutation.Placed inBag = bagTable[k];
						DropRateInfo version = plan.BagVersion[plan.ContentOf[carried[k]]];
						if (inBag.Entry.itemId != onBoss.Entry.itemId || inBag.FromNetId != onBoss.FromNetId || inBag.FromIndex != onBoss.FromIndex)
						{
							Fail($"seed {seed}: {ItemName(bag)} entry {k} is {ItemName(inBag.Entry.itemId)}, {NpcName(unit)} drops {ItemName(onBoss.Entry.itemId)} there");
						}
						else if (inBag.Entry.dropRate != version.dropRate || inBag.Entry.stackMin != version.stackMin || inBag.Entry.stackMax != version.stackMax)
						{
							Fail($"seed {seed}: {ItemName(inBag.Entry.itemId)} in {ItemName(bag)}: not its chance and stack in its own bag");
						}
						if (inBag.Entry.conditions != null && inBag.Entry.conditions.Any(c => c is Conditions.NotExpert or Conditions.LegacyHack_IsBossAndNotExpert)) Fail($"seed {seed}: {ItemName(inBag.Entry.itemId)} in {ItemName(bag)} keeps a not-expert condition");
					}
				}
			}
			if (carriedCount == 0) Check.Skip("no bag carries a boss drop");
			Check.All(failures, "Boss bags");
		}

		// Contents move only within their boss group and access class; the groups are runs of consecutive boss tiers, each of
		// at least MinGroupBosses bosses (unless there is a single group)
		private static void BossPermutationTiers()
		{
			List<string> failures = new();
			void Fail(string message)
			{
				if (failures.Count < 25) failures.Add(message);
			}
			BossPermutation.WorldPlan logged = null;
			foreach (int seed in Seeds())
			{
				BossPermutation.WorldPlan plan = Plan(seed);
				logged ??= plan;
				for (int p = 0; p < plan.Places.Count; p++)
				{
					DropPermutation.Place place = plan.Places[p];
					DropPermutation.Place source = plan.Places[plan.ContentOf[p]];
					if (plan.GroupOfUnit[place.Owner] != plan.GroupOfUnit[source.Owner]) Fail($"seed {seed}: {Where(place)} (tier {place.Tier}) gets the content of {Where(source)} (tier {source.Tier}), another group");
					if (place.Access != source.Access) Fail($"seed {seed}: {Where(place)} ({place.Access}) gets a content of {source.Access}");
				}
				int previousMax = -1;
				foreach (List<int> group in plan.BossGroups)
				{
					List<int> tiers = group.Select(BossPermutation.TierOf).ToList();
					if (tiers.Min() <= previousMax) Fail($"seed {seed}: the groups overlap at tier {tiers.Min()}");
					previousMax = tiers.Max();
					if (group.Count < BossPermutation.MinGroupBosses && plan.BossGroups.Count > 1) Fail($"seed {seed}: a group of {group.Count} boss ({string.Join(", ", group.Select(NpcName))})");
				}
			}
			if (logged != null)
			{
				string groups = string.Join(" | ", logged.BossGroups.Select(g => string.Join(", ", g.Select(u => $"{NpcName(u)} {BossPermutation.TierOf(u)}"))));
				ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Boss groups: {groups}");
			}
			Check.All(failures, "Boss permutation groups");
		}

		// The registered rules follow the plan: an entry whose wrapped rule is silenced is rolled by us (its place, or moved to
		// another table), an entry we roll is never also rolled by its own rule, for every boss net ID and every boss bag;
		// every permuted bag has its roller
		private static void BossRuntime()
		{
			BossPermutation.WorldPlan plan = Plan(SeedSystem.WorldSeed);
			if (!BossPermutation.InStyle(NPCID.EyeofCthulhu)) Check.Skip("bosses don't use the permutation style");
			HashSet<int> everywhere = plan.ByUnit.Values.Concat(plan.ByBag.Values).SelectMany(t => t).Where(p => p.Rolled).Select(p => p.Entry.itemId).ToHashSet();
			List<string> failures = new();
			int silencedEntries = 0;
			void Fail(string message)
			{
				if (failures.Count < 25) failures.Add(message);
			}
			void CheckTable(string name, List<IItemDropRule> registered, List<DropRateInfo> entries, int[] rules, DropPermutation.Placed[] table, int bagUnit)
			{
				Dictionary<int, PermutableRule> wrappers = new();
				foreach (PermutableRule wrapper in registered.SelectMany(r => BossPermutation.Wrappers(r)))
				{
					if (!wrappers.TryAdd(wrapper.RuleIndex, wrapper)) Fail($"{name}: two wrappers of index {wrapper.RuleIndex}");
				}
				for (int i = 0; i < entries.Count; i++)
				{
					int item = entries[i].itemId;
					int r = i < rules.Length ? rules[i] : -1;
					if (r >= 0 && !wrappers.ContainsKey(r)) Fail($"{name}: the rule of {ItemName(item)} is not wrapped");
					bool silenced = r >= 0 && wrappers.TryGetValue(r, out PermutableRule rule) && DropPermutation.Silences(rule.Items);
					// A bag's own entries: in its table unless they travel with a boss drop (matched): then rolled elsewhere
					bool Own(DropPermutation.Placed p) => p.FromNetId == bagUnit && p.FromIndex == BossPermutation.BagIndexOffset + i;
					bool rolledHere = bagUnit > 0 ? table.Any(p => p.Rolled && Own(p)) : i < table.Length && table[i].Rolled;
					bool keptHere = bagUnit > 0 ? table.Any(p => !p.Rolled && Own(p)) : i < table.Length && !table[i].Rolled;
					if (silenced && keptHere) Fail($"{name}: {ItemName(item)} silenced, its place left to its own rule");
					if (!silenced && rolledHere) Fail($"{name}: {ItemName(item)} rolled by us and by its own rule");
					if (!silenced) continue;
					silencedEntries++;
					if (!everywhere.Contains(item)) Fail($"{name}: {ItemName(item)} silenced and rolled nowhere");
				}
			}
			foreach (int netId in ContentSamples.NpcsByNetId.Keys.OrderBy(k => k).Where(BossPermutation.IsSource))
			{
				int unit = BossPermutation.UnitOf(netId);
				if (!plan.ByUnit.TryGetValue(unit, out DropPermutation.Placed[] table))
				{
					Fail($"{NpcName(netId)} ({netId}): no table");
					continue;
				}
				List<IItemDropRule> registered = Main.ItemDropsDB.GetRulesForNPCID(netId, false);
				if (registered.SelectMany(r => BossPermutation.Wrappers(r)).Any(w => w.NetId != netId)) Fail($"{NpcName(netId)} ({netId}): a wrapper of another net ID");
				CheckTable($"{NpcName(netId)} ({netId})", registered, LootSnapshot.ForNpc(netId), LootSnapshot.RulesOf(netId), table, 0);
			}
			foreach (int bag in BossPermutation.Bags())
			{
				List<IItemDropRule> registered = Main.ItemDropsDB.GetRulesForItemID(bag);
				if (!registered.OfType<BossBagRule>().Any()) Fail($"{ItemName(bag)}: no bag roller");
				if (registered.SelectMany(r => BossPermutation.Wrappers(r)).Any(w => w.BagType != bag)) Fail($"{ItemName(bag)}: a wrapper of another bag");
				if (!BossPermutation.BagActive(bag)) continue;
				CheckTable(ItemName(bag), registered, LootSnapshot.ForItem(bag), LootSnapshot.RulesOfItem(bag), plan.ByBag[bag], BossPermutation.BagUnit(bag));
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Boss permutation runtime: {silencedEntries} silenced entries");
			Check.All(failures, "Boss permutation runtime");
			if (silencedEntries == 0) Check.Skip("no silenced boss drop entry to check");
		}
	}
}

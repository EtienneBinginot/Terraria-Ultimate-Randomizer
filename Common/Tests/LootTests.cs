using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Codex;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Tests
{
	public static class LootTests
	{
		public static void Register()
		{
			TestRunner.Register("SnapshotsCaptured", SnapshotsCaptured);
			TestRunner.Register("MobPlansValid", MobPlansValid);
			TestRunner.Register("LootAbundanceScales", LootAbundanceScales);
			TestRunner.Register("ProtectedDropsKept", ProtectedDropsKept);
			TestRunner.Register("NoMinionFarms", NoMinionFarms);
			TestRunner.Register("EventItemsKept", EventItemsKept);
			TestRunner.Register("BagPlansValid", BagPlansValid);
			TestRunner.Register("BagsKeepCoins", BagsKeepCoins);
			TestRunner.Register("CrittersDrop", CrittersDrop);
			TestRunner.Register("WildTail", WildTail);
			TestRunner.Register("PlansIgnoreWild", PlansIgnoreWild);
			TestRunner.Register("DropTableLength", DropTableLength);
			TestRunner.Register("JackpotUseful", JackpotUseful);
			TestRunner.Register("JackpotNotInEnemyDrops", JackpotNotInEnemyDrops);
			TestRunner.Register("JackpotRatesLower", JackpotRatesLower);
			TestRunner.Register("JackpotDropsMarked", JackpotDropsMarked);
			TestRunner.Register("GiantBonusUseful", GiantBonusUseful);
			TestRunner.Register("MiniBonusDrop", MiniBonusDrop);
			TestRunner.Register("JackpotWorthScales", JackpotWorthScales);
			TestRunner.Register("SpawnedEnemyNoExtraDrops", SpawnedEnemyNoExtraDrops);
			TestRunner.Register("CrittersNoJackpot", CrittersNoJackpot);
			TestRunner.Register("BagNeverJackpot", BagNeverJackpot);
			TestRunner.Register("JackpotTail", JackpotTail);
			TestRunner.Register("GuaranteeScalesWithPlayers", GuaranteeScalesWithPlayers);
			TestRunner.Register("LockedDropsRedistributed", LockedDropsRedistributed);
			TestRunner.Register("LockedVanillaDropRemoved", LockedVanillaDropRemoved);
			TestRunner.Register("DropPermutationConserves", DropPermutationConserves);
			TestRunner.Register("DropPermutationMoves", DropPermutationMoves);
			TestRunner.Register("DropPermutationAccess", DropPermutationAccess);
			TestRunner.Register("DropPermutationRuntime", DropPermutationRuntime);
			TestRunner.Register("DropPermutationStable", DropPermutationStable);
			TestRunner.Register("DropPermutationSurprise", DropPermutationSurprise);
			TestRunner.Register("PermutationDuplicatesCapped", PermutationDuplicatesCapped);
			TestRunner.Register("PermutationKeepsStartDrops", PermutationKeepsStartDrops);
			TestRunner.Register("ExtraStacksSmall", ExtraStacksSmall);
			TestRunner.Register("GatesNotOnLowerOwners", GatesNotOnLowerOwners);
			TestRunner.Register("StrongEnemiesDropEarly", StrongEnemiesDropEarly);
		}

		// Seeds of the permutation tests: the world's and two fixed ones (DropPermutation.Build is pure in the seed)
		private static int[] PermutationSeeds() => new[] { SeedSystem.WorldSeed, 12345, -98765 };

		private static DropPermutation.WorldPlan PermutationPlan(int seed)
		{
			if (!LootConfig.Instance.RandomizeMobDrops) Check.Skip("enemy drops are not randomized");
			if (!ItemPool.Ready) Check.Skip("the item pool is not built");
			// Without surprises nor copy caps: these tests check the permutation itself (DropPermutationSurprise and
			// PermutationDuplicatesCapped check the surprises)
			DropPermutation.WorldPlan plan = DropPermutation.Build(seed, 0f, 0, 0);
			if (plan.Places.Count < 30) Check.Skip($"only {plan.Places.Count} permutable drop entries");
			return plan;
		}

		// The length of a table: its snapshot's, one more for a synthetic entry
		private static int TableLength(DropPermutation.WorldPlan plan, int unit) =>
			LootSnapshot.ForNpc(unit).Count + (plan.Places.Any(p => p.Synthetic && p.Owner == unit) ? 1 : 0);

		private static string ContentKey(DropRateInfo entry) => $"{entry.itemId}:{entry.stackMin}-{entry.stackMax}@{entry.dropRate:0.000000}";

		// The permutation loses and duplicates nothing: in each group the contents of the final tables are the original
		// entries (item, stack, own chance), each place gives its content exactly once, every enemy keeps its entry count, and
		// with the permutation style every item of a permuted vanilla entry is in the drops of some enemy (OriginalDrops), at
		// the place the plan says, with that place's conditions
		private static void DropPermutationConserves()
		{
			List<string> failures = new();
			int places = 0, groups = 0;
			foreach (int seed in PermutationSeeds())
			{
				DropPermutation.WorldPlan plan = PermutationPlan(seed);
				places = plan.Places.Count;
				groups = plan.Groups.Count;
				foreach (List<int> group in plan.Groups)
				{
					List<string> before = group.Select(i => ContentKey(plan.Places[i].Entry)).OrderBy(k => k, StringComparer.Ordinal).ToList();
					List<string> after = group.Select(i => ContentKey(plan.ByUnit[plan.Places[i].Owner][plan.Places[i].Index].Entry)).OrderBy(k => k, StringComparer.Ordinal).ToList();
					if (!before.SequenceEqual(after) && failures.Count < 10) failures.Add($"seed {seed}: a group of {group.Count} places (tier {plan.Places[group[0]].Tier}, '{plan.Places[group[0]].Access}') changed its contents");
				}
				if (plan.Groups.Sum(g => g.Count) != plan.Places.Count) failures.Add($"seed {seed}: {plan.Groups.Sum(g => g.Count)} places in groups, {plan.Places.Count} places");
				HashSet<(int, int)> given = new();
				foreach (DropPermutation.Place place in plan.Places)
				{
					DropPermutation.Placed placed = plan.ByUnit[place.Owner][place.Index];
					if (!placed.Permuted || !placed.Rolled) failures.Add($"seed {seed}: {NpcName(place.Owner)} entry {place.Index} not permuted");
					if (!given.Add((placed.FromNetId, placed.FromIndex))) failures.Add($"seed {seed}: the content of {NpcName(placed.FromNetId)} entry {placed.FromIndex} given twice");
				}
				foreach ((int unit, DropPermutation.Placed[] table) in plan.ByUnit)
				{
					if (table.Length != TableLength(plan, unit)) failures.Add($"seed {seed}: {NpcName(unit)} has {table.Length} entries, {TableLength(plan, unit)} expected");
				}
			}
			// The live plan, through what the kills and the codex read
			if (SeedSystem.WorldReady)
			{
				DropPermutation.WorldPlan current = DropPermutation.Current;
				HashSet<int> planned = new();
				foreach (int unit in DropPermutation.Units())
				{
					List<PlannedDrop> drops = RandomNpcDropRule.OriginalDrops(unit, unit, DropTableStyle.Permutation);
					planned.UnionWith(drops.Select(d => d.Item));
					if (drops.Count != TableLength(current, unit)) failures.Add($"{NpcName(unit)}: {drops.Count} planned original entries, {TableLength(current, unit)} expected");
				}
				for (int p = 0; p < current.Places.Count; p++)
				{
					DropPermutation.Place place = current.Places[p];
					// Surprises may take every copy of a block, critter or ore, never the keeper copy of another item
					if (!planned.Contains(place.Entry.itemId) && !DropPermutation.FoundInWorld(place.Entry.itemId) && failures.Count < 20)
					{
						failures.Add($"{Lang.GetItemNameValue(place.Entry.itemId)} (from {NpcName(place.Owner)}) is in no enemy's drops");
					}
					List<PlannedDrop> drops = RandomNpcDropRule.OriginalDrops(place.Owner, place.Owner, DropTableStyle.Permutation);
					DropPermutation.Place source = current.Places[current.ContentOf[p]];
					if (place.Index >= drops.Count) continue;
					PlannedDrop drop = drops[place.Index];
					int expected = current.Surprised[p] ? current.ByUnit[place.Owner][place.Index].Entry.itemId : source.Entry.itemId;
					if ((drop.Item != expected || drop.Original != source.Entry.itemId || drop.Surprise != current.Surprised[p] || !SameConditions(drop.Conditions, place.Entry.conditions) || !drop.Rolled) && failures.Count < 20)
					{
						failures.Add($"{NpcName(place.Owner)} entry {place.Index}: {Lang.GetItemNameValue(drop.Item)} planned, {Lang.GetItemNameValue(expected)} expected with the place's conditions");
					}
				}
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Drop permutation: {places} places in {groups} groups");
			Check.All(failures, "Drop permutation");
		}

		// What the kills really do, per net ID (variants such as Pinky have rules of their own): every registered wrapper
		// belongs to its net ID; an entry is rolled by our rule exactly when its wrapped rule is silenced (nothing both rolled
		// and left running); every item of a silenced rule is in the net ID's own table, or, permuted away, in some table;
		// the items that did not move are the net ID's own; every eligible enemy without vanilla drops rolls something. Without
		// surprises, and on the live plan (with them: only blocks, critters and ores may lose every copy)
		private static void DropPermutationRuntime()
		{
			List<(string, DropPermutation.WorldPlan, bool)> plans = new() { ("without surprises", PermutationPlan(SeedSystem.WorldSeed), false) };
			if (SeedSystem.WorldReady && DropPermutation.InStyle(NPCID.BlueSlime)) plans.Add(("live", DropPermutation.Current, true));
			List<string> failures = new();
			int silencedTotal = 0;
			foreach ((string label, DropPermutation.WorldPlan plan, bool live) in plans)
			{
				int netIds = 0, ownTables = 0, silencedEntries = 0, withoutDrops = 0;
				HashSet<int> everywhere = plan.ByUnit.Values.SelectMany(t => t).Where(p => p.Rolled).Select(p => p.Entry.itemId).ToHashSet();
				void Fail(string message)
				{
					if (failures.Count < 25) failures.Add($"{label}: {message}");
				}
				foreach (int netId in ContentSamples.NpcsByNetId.Keys.OrderBy(k => k))
				{
					if (!DropPermutation.IsPermutable(netId)) continue;
					netIds++;
					int unit = DropPermutation.UnitOf(netId);
					if (unit == netId && ContentSamples.NpcsByNetId[netId].type != netId) ownTables++;
					if (!plan.ByUnit.TryGetValue(unit, out DropPermutation.Placed[] table))
					{
						Fail($"{NpcName(netId)} ({netId}): no table");
						continue;
					}
					List<DropRateInfo> entries = LootSnapshot.ForNpc(netId);
					int[] rules = LootSnapshot.RulesOf(netId);
					if (table.Length != TableLength(plan, unit) || table.Length < entries.Count) Fail($"{NpcName(netId)} ({netId}): a table of {table.Length} entries for {entries.Count} vanilla entries");
					Dictionary<int, PermutableRule> wrappers = new();
					foreach (PermutableRule wrapper in Main.ItemDropsDB.GetRulesForNPCID(netId, false).OfType<PermutableRule>())
					{
						if (wrapper.NetId != netId) Fail($"{NpcName(netId)} ({netId}): a wrapper of net ID {wrapper.NetId}");
						wrappers[wrapper.RuleIndex] = wrapper;
					}
					for (int i = 0; i < entries.Count; i++)
					{
						int item = entries[i].itemId;
						int r = i < rules.Length ? rules[i] : -1;
						if (r >= 0 && !wrappers.ContainsKey(r)) Fail($"{NpcName(netId)} ({netId}): the rule of {Lang.GetItemNameValue(item)} is not wrapped");
						bool silenced = r >= 0 && wrappers.TryGetValue(r, out PermutableRule rule) && DropPermutation.Silences(rule.Items);
						bool rolled = i < table.Length && table[i].Rolled;
						if (rolled && !silenced) Fail($"{NpcName(netId)} ({netId}): {Lang.GetItemNameValue(item)} rolled by us and by its own rule");
						if (silenced && !rolled) Fail($"{NpcName(netId)} ({netId}): {Lang.GetItemNameValue(item)} silenced, its place not rolled");
						if (i < table.Length && !table[i].Permuted && table[i].Entry.itemId != item) Fail($"{NpcName(netId)} ({netId}): entry {i} is {Lang.GetItemNameValue(table[i].Entry.itemId)}, {Lang.GetItemNameValue(item)} originally");
						if (!silenced) continue;
						silencedEntries++;
						bool here = i < table.Length && !table[i].Permuted && table[i].Entry.itemId == item;
						if (!here && !everywhere.Contains(item) && !(live && DropPermutation.FoundInWorld(item))) Fail($"{NpcName(netId)} ({netId}): {Lang.GetItemNameValue(item)} silenced and rolled nowhere");
					}
					if (entries.Count == 0)
					{
						withoutDrops++;
						if (!table.Any(p => p.Rolled && p.Entry.itemId > ItemID.None)) Fail($"{NpcName(netId)} ({netId}): no vanilla drop and no rolled entry");
					}
				}
				silencedTotal += silencedEntries;
				ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Drop permutation runtime ({label}): {netIds} net IDs, {ownTables} variants with their own table, {silencedEntries} silenced entries, {withoutDrops} enemies without vanilla drops");
			}
			Check.All(failures, "Drop permutation runtime");
			if (silencedTotal == 0) Check.Skip("no silenced drop entry to check");
		}

		// The assignment is stable: removing one place from a group (a setting keeps one more item, say) changes the content of
		// very few other places (the one before it in the group's cycle), not the whole group
		private static void DropPermutationStable()
		{
			if (!ItemPool.Ready) Check.Skip("the item pool is not built");
			List<DropPermutation.Place> places = DropPermutation.CollectPlaces(SeedSystem.WorldSeed);
			if (places.Count < 30) Check.Skip($"only {places.Count} permutable drop entries");
			List<string> failures = new();
			int removals = 0, changedTotal = 0;
			foreach (int seed in PermutationSeeds())
			{
				int[] full = DropPermutation.Permute(places, seed, out List<List<int>> groups);
				Random rng = new(seed);
				foreach (List<int> group in groups.Where(g => g.Count >= DropPermutation.MinGroupSize * 3).Take(8))
				{
					int removed = group[rng.Next(group.Count)];
					List<DropPermutation.Place> fewer = places.Where((_, i) => i != removed).ToList();
					int[] after = DropPermutation.Permute(fewer, seed, out _);
					int changed = 0;
					for (int i = 0; i < places.Count; i++)
					{
						if (i == removed) continue;
						int j = i < removed ? i : i - 1;
						DropPermutation.Place was = places[full[i]], now = fewer[after[j]];
						if (was.Owner != now.Owner || was.Index != now.Index) changed++;
					}
					removals++;
					changedTotal += changed;
					if (changed > Math.Max(2, group.Count / 20)) failures.Add($"seed {seed}: removing one place of a group of {group.Count} changed {changed} other assignments");
				}
			}
			if (removals == 0) Check.Skip("no group large enough");
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Drop permutation stability: {removals} removals, {changedTotal} assignments changed in all");
			Check.All(failures, "Drop permutation stability");
		}

		// The surprises of the permutation, with explicit chances (not the config): about that share of the candidates becomes a
		// random item with at most the content's chance and the place's conditions; never a material, ore, bar or synthetic content
		// (but the forced copies past an item's cap, which are never candidates and don't count in the share);
		// every item but blocks, critters and ores keeps a copy even when every candidate is surprised; a higher chance only adds
		// surprises (each place has its own roll), with the same items
		// The same conditions (the same objects, in order): DropRateInfo's constructor copies its conditions list, so the
		// lists themselves are never the same
		private static bool SameConditions(List<IItemDropRuleCondition> a, List<IItemDropRuleCondition> b) =>
			(a?.Count ?? 0) == (b?.Count ?? 0) && (a == null || a.Count == 0 || a.SequenceEqual(b));

		private static void DropPermutationSurprise()
		{
			List<string> failures = new();
			void Fail(string message)
			{
				if (failures.Count < 25) failures.Add(message);
			}
			const float low = 0.1f, high = 0.25f;
			int checkedSeeds = 0;
			string shares = "";
			foreach (int seed in PermutationSeeds())
			{
				DropPermutation.WorldPlan none = PermutationPlan(seed);
				const int copies = 2, materials = 4;
				DropPermutation.WorldPlan few = DropPermutation.Build(seed, low, copies, materials);
				DropPermutation.WorldPlan many = DropPermutation.Build(seed, high, copies, materials);
				DropPermutation.WorldPlan all = DropPermutation.Build(seed, 1f, copies, materials);
				if (none.Surprised.Any(s => s)) Fail($"seed {seed}: surprises at 0 %");
				foreach ((float chance, DropPermutation.WorldPlan plan) in new[] { (low, few), (high, many), (1f, all) })
				{
					int candidates = 0, surprised = 0;
					for (int p = 0; p < plan.Places.Count; p++)
					{
						DropPermutation.Place place = plan.Places[p];
						DropPermutation.Place source = plan.Places[plan.ContentOf[p]];
						DropPermutation.Placed placed = plan.ByUnit[place.Owner][place.Index];
						DropRateInfo content = source.Entry;
						bool forced = plan.Forced[p];
						if (plan.SurpriseCandidate[p]) candidates++;
						if (forced && plan.SurpriseCandidate[p]) Fail($"seed {seed} at {chance:P0}: {NpcName(place.Owner)} entry {place.Index} both forced and a candidate");
						if (!plan.Surprised[p])
						{
							if (placed.Surprise || placed.Entry.itemId != content.itemId) Fail($"seed {seed} at {chance:P0}: {NpcName(place.Owner)} entry {place.Index} changed without being a surprise");
							continue;
						}
						if (!forced) surprised++;
						string where = $"seed {seed} at {chance:P0}: {NpcName(place.Owner)} entry {place.Index} ({Lang.GetItemNameValue(content.itemId)})";
						if (!plan.SurpriseCandidate[p] && !forced) Fail($"{where} surprised but neither a candidate nor forced");
						if (!DropPermutation.SurpriseKind(content.itemId) && !forced) Fail($"{where}: a material, ore or bar surprised");
						if (source.Synthetic) Fail($"{where}: a synthetic content surprised");
						// The chance is the content's, capped by a vanilla-like rate for the new item (DropRates.Denominator)
						if (!placed.Surprise || placed.Replaced != content.itemId || placed.Entry.dropRate <= 0f || placed.Entry.dropRate > content.dropRate || !SameConditions(placed.Entry.conditions, place.Entry.conditions))
						{
							Fail($"{where}: the surprise {Lang.GetItemNameValue(placed.Entry.itemId)} lost the content's chance or the place's conditions");
						}
						// Monotone in the chance, with the same item
						if (plan == few && (!many.Surprised[p] || many.ByUnit[place.Owner][place.Index].Entry.itemId != placed.Entry.itemId))
						{
							Fail($"{where}: a surprise at {low:P0} is not the same at {high:P0}");
						}
					}
					shares += $" {seed}@{chance:P0}: {surprised}/{candidates}";
					if (candidates < 50) continue;
					checkedSeeds++;
					double share = surprised / (double)candidates;
					double tolerance = 4 * Math.Sqrt(chance * (1 - chance) / candidates) + 0.02;
					if (Math.Abs(share - chance) > tolerance) Fail($"seed {seed}: {share:P1} of {candidates} candidates surprised at {chance:P0}");
				}
				// Every candidate surprised: every item but blocks, critters and ores still drops somewhere (its keeper copy),
				// whatever its other sources (randomized too)
				HashSet<int> before = none.ByUnit.Values.SelectMany(t => t).Select(x => x.Entry.itemId).ToHashSet();
				HashSet<int> after = all.ByUnit.Values.SelectMany(t => t).Select(x => x.Entry.itemId).ToHashSet();
				foreach (int item in before.Where(i => !after.Contains(i) && !DropPermutation.FoundInWorld(i)))
				{
					Fail($"seed {seed}: the surprises took every copy of {Lang.GetItemNameValue(item)}");
				}
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Drop permutation surprises (surprised/candidates):{shares}");
			Check.All(failures, "Drop permutation surprises");
			if (checkedSeeds == 0) Check.Skip("fewer than 50 surprise candidates");
		}

		// The copy caps (explicit, not the config), per access class (a NormalvsExpert drop's Classic and Expert copies count
		// apart): no item keeps more unsurprised permuted copies there than its cap (materials theirs, ores, bars, blocks and
		// critters none), every forced copy that could be rolled became a surprise, and every item that had a permuted copy in
		// an access class keeps one there. In this world (Findability.WorldAllows: difficulty, evil, seeds), an item that had a
		// copy able to drop keeps one able to drop. Logs the tier-0 share of the items vanilla repeats on every zombie
		// and slime variant, with and without the caps
		private static void PermutationDuplicatesCapped()
		{
			if (!DropPermutation.InStyle(NPCID.BlueSlime)) Check.Skip("the drop table style is not Permutation");
			List<string> failures = new();
			void Fail(string message)
			{
				if (failures.Count < 25) failures.Add(message);
			}
			const int copies = 2, materials = 4;
			int[] repeated = { ItemID.SlimeStaff, ItemID.SpiffoPlush, ItemID.ZombieArm, ItemID.Shackle, ItemID.Gel, ItemID.Lens };
			string shares = "";
			int cappedItems = 0;
			foreach (int seed in PermutationSeeds())
			{
				DropPermutation.WorldPlan plain = PermutationPlan(seed);
				DropPermutation.WorldPlan capped = DropPermutation.Build(seed, 0f, copies, materials);
				Dictionary<(int item, string access), int> kept = new(), before = new();
				HashSet<int> droppable = new(), keptDroppable = new();
				for (int p = 0; p < capped.Places.Count; p++)
				{
					DropPermutation.Place place = capped.Places[p];
					DropPermutation.Place source = capped.Places[capped.ContentOf[p]];
					if (source.Synthetic) continue;
					int item = source.Entry.itemId;
					(int, string) key = (item, place.Access ?? "");
					before[key] = before.GetValueOrDefault(key) + 1;
					if (!capped.Surprised[p]) kept[key] = kept.GetValueOrDefault(key) + 1;
					if (capped.Forced[p] && !capped.Surprised[p] && capped.ByUnit[place.Owner][place.Index].Entry.itemId != item) Fail($"seed {seed}: a forced place changed without a surprise");
					// Able to drop in this world: allowed by its difficulty, evil and seeds, at a reachable condition tier
					if (Findability.WorldAllows(place.Entry.conditions) && Findability.ConditionTier(place.Entry.conditions, place.Tier) != Findability.Never)
					{
						droppable.Add(item);
						if (!capped.Surprised[p]) keptDroppable.Add(item);
					}
				}
				foreach (((int item, string access), int count) in before)
				{
					int left = kept.GetValueOrDefault((item, access));
					int cap = DropPermutation.CopyCap(item, copies, materials);
					string where = access == "" ? "" : $" ({access})";
					if (left == 0) Fail($"seed {seed}: {Lang.GetItemNameValue(item)}{where} lost every permuted copy ({count} before)");
					if (cap > 0 && count > cap) cappedItems++;
					// A forced copy may stay when its surprise could not be rolled (TrySurprise failed): only a few
					if (cap > 0 && left > cap + Math.Max(1, (count - cap) / 10)) Fail($"seed {seed}: {Lang.GetItemNameValue(item)}{where} keeps {left} copies, cap {cap}");
				}
				foreach (int item in droppable.Where(i => !keptDroppable.Contains(i)).OrderBy(i => i))
				{
					Fail($"seed {seed}: {Lang.GetItemNameValue(item)} keeps no copy able to drop in this world");
				}
				// Tier 0: the share of the places holding the repeated items, without and with the caps
				int Share(DropPermutation.WorldPlan plan, out int total)
				{
					total = 0;
					int count = 0;
					for (int p = 0; p < plan.Places.Count; p++)
					{
						DropPermutation.Place place = plan.Places[p];
						if (place.Tier != 0) continue;
						total++;
						if (repeated.Contains(plan.ByUnit[place.Owner][place.Index].Entry.itemId)) count++;
					}
					return count;
				}
				int was = Share(plain, out int totalWas), now = Share(capped, out int totalNow);
				shares += $" {seed}: {was}/{totalWas} -> {now}/{totalNow}";
				if (totalNow > 0 && now > was) Fail($"seed {seed}: the caps raised the tier-0 repeated items from {was} to {now}");
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Permutation duplicates (tier-0 places holding Slime Staff, Spiffo Plush, Zombie Arm, Shackle, Gel, Lens, uncapped -> capped):{shares}");
			Check.All(failures, "Permutation duplicates");
			if (cappedItems == 0) Check.Skip("no item has more copies than its cap");
		}

		// With the default caps, the copies of Gel and Lens kept are those of the enemies the early kills meet: both stay start
		// materials (RecipePlanner.StartMaterials: within StartDropKills early kills on average), so the critical stations'
		// recipes may take them
		private static void PermutationKeepsStartDrops()
		{
			if (!LootConfig.Instance.RandomizeMobDrops) Check.Skip("enemy drops are not randomized");
			if (!DropPermutation.InStyle(NPCID.BlueSlime)) Check.Skip("the drop table style is not Permutation");
			if (!ItemPool.Ready) Check.Skip("the item pool is not built");
			PacingTests.RequireDefaults();
			HashSet<int> start = RecipePlanner.StartMaterials(false);
			List<string> failures = new();
			string rates = "";
			foreach (int item in new[] { ItemID.Gel, ItemID.Lens })
			{
				double rate = PacingModel.EarlyDropRate(item);
				rates += $" {Lang.GetItemNameValue(item)} {rate:0.000}/kill";
				if (!start.Contains(item)) failures.Add($"{Lang.GetItemNameValue(item)} is not a start material ({rate:0.000} per early kill, at least {1 / RecipePlanner.StartDropKills:0.000})");
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Permutation start drops:{rates}");
			Check.All(failures, "Start drops");
		}

		// Contents only move within their tier group (the origin's tier is one of the group's tiers, always the base tier of
		// the enemy), and most of them really move: few stay at their own place, many go to another enemy
		private static void DropPermutationMoves()
		{
			List<string> failures = new();
			foreach (int seed in PermutationSeeds())
			{
				DropPermutation.WorldPlan plan = PermutationPlan(seed);
				int stayed = 0, otherEnemy = 0;
				HashSet<int> grouped = new();
				foreach (List<int> group in plan.Groups)
				{
					HashSet<int> tiers = group.Select(i => plan.Places[i].Tier).ToHashSet();
					HashSet<int> members = group.ToHashSet();
					foreach (int i in group)
					{
						grouped.Add(i);
						DropPermutation.Place place = plan.Places[i];
						int source = plan.ContentOf[i];
						if (place.Tier != DropPermutation.PlaceTierOf(place.Owner)) failures.Add($"seed {seed}: {NpcName(place.Owner)} placed at tier {place.Tier}, place tier {DropPermutation.PlaceTierOf(place.Owner)}");
						if (!members.Contains(source) && failures.Count < 20) failures.Add($"seed {seed}: {NpcName(place.Owner)} gets the content of {NpcName(plan.Places[source].Owner)} from another group");
						if (!tiers.Contains(plan.Places[source].Tier) && failures.Count < 20) failures.Add($"seed {seed}: a tier {plan.Places[source].Tier} content in a group of tiers {string.Join(",", tiers.OrderBy(t => t))}");
						if (source == i) stayed++;
						if (plan.Places[source].Owner != place.Owner) otherEnemy++;
					}
					// Tiny groups are merged: only the last group of an access class may be small
					if (group.Count < DropPermutation.MinGroupSize && plan.Groups.Count(g => plan.Places[g[0]].Access == plan.Places[group[0]].Access) > 1)
					{
						failures.Add($"seed {seed}: a group of {group.Count} places not merged");
					}
				}
				if (grouped.Count != plan.Places.Count) failures.Add($"seed {seed}: {plan.Places.Count - grouped.Count} places in no group");
				double stayShare = stayed / (double)plan.Places.Count;
				double moveShare = otherEnemy / (double)plan.Places.Count;
				ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Drop permutation seed {seed}: {plan.Places.Count} places, {stayShare:P1} stay in place, {moveShare:P1} go to another enemy");
				if (stayShare > 0.15) failures.Add($"seed {seed}: {stayShare:P1} of the contents stay at their own place");
				if (moveShare < 0.7) failures.Add($"seed {seed}: only {moveShare:P1} of the contents go to another enemy");
			}
			Check.All(failures, "Drop permutation moves");
		}

		// Restricted contents (expert or master only, special seeds, never...) only go to places with the same restrictive
		// conditions; ordinary conditions (hardmode, biome...) don't make a class of their own. Findability counts the
		// progression conditions of a place (hardmode, mechanical bosses, Plantera)
		private static void DropPermutationAccess()
		{
			List<string> failures = new();
			string Access(params IItemDropRuleCondition[] conditions) => DropPermutation.AccessOf(conditions.ToList());
			if (Access(new Conditions.IsExpert(), new Conditions.IsHardmode()) != "IsExpert") failures.Add($"expert + hardmode is '{Access(new Conditions.IsExpert(), new Conditions.IsHardmode())}'");
			if (Access(new Conditions.IsHardmode(), new Conditions.NotFromStatue()) != "") failures.Add("hardmode + not from a statue is restricted");
			if (Access(new Conditions.NotExpert(), new Conditions.IsMasterMode()) != "IsMasterMode+NotExpert") failures.Add($"not expert + master is '{Access(new Conditions.NotExpert(), new Conditions.IsMasterMode())}'");
			if (Access() != "") failures.Add("no condition is restricted");
			int Found(int sourceTier, params IItemDropRuleCondition[] conditions) => Findability.ConditionTier(conditions.ToList(), sourceTier);
			if (Found(0, new Conditions.IsHardmode()) != (int)Tier.Hardmode) failures.Add("a hardmode-only drop of a pre-boss enemy is not counted from hardmode");
			if (Found(0, new Conditions.DownedPlantera()) != (int)Tier.PostPlantera) failures.Add("a post-Plantera drop is not counted from Plantera");
			if (Found(0, new Conditions.BeatAnyMechBoss()) != (int)Tier.PostMechBoss) failures.Add("a post-mechanical boss drop is not counted from the mechanical bosses");
			if (Found((int)Tier.PostGolem, new Conditions.IsHardmode()) != (int)Tier.PostGolem) failures.Add("a condition lowered a source's tier");
			if (Found((int)Tier.Hardmode, new Conditions.IsPreHardmode()) != Findability.Never) failures.Add("a pre-hardmode drop of a hardmode enemy is counted");
			if (Found(2, new Conditions.NotFromStatue()) != 2) failures.Add("an ordinary condition changed the tier");
			int restricted = 0;
			foreach (int seed in PermutationSeeds())
			{
				DropPermutation.WorldPlan plan = PermutationPlan(seed);
				for (int p = 0; p < plan.Places.Count; p++)
				{
					DropPermutation.Place place = plan.Places[p];
					DropPermutation.Place source = plan.Places[plan.ContentOf[p]];
					string here = DropPermutation.AccessOf(place.Entry.conditions);
					string from = DropPermutation.AccessOf(source.Entry.conditions);
					if (here != "") restricted++;
					if (here != from && failures.Count < 20) failures.Add($"seed {seed}: {Lang.GetItemNameValue(source.Entry.itemId)} ('{from}') at a '{here}' place of {NpcName(place.Owner)}");
				}
			}
			Check.All(failures, "Drop permutation access");
			if (restricted == 0) Check.Skip("no restricted drop entry to check");
		}

		// RandomNpcDropRule.Redistribute on explicit entries: the chances of the locked entries (original drops included) go
		// to the unlocked extra entries in proportion, each capped at 1; unlocked original drops and guaranteed drops receive
		// nothing, locked entries keep their own chance, and nothing moves without a locked entry or without a receiver
		private static void LockedDropsRedistributed()
		{
			PlannedDrop Entry(float chance, bool extra, bool locked = false, bool guaranteed = false) =>
				new() { Item = ItemID.Gel, StackMin = 1, StackMax = 1, Chance = chance, Extra = extra, LockGap = locked ? 2 : 0, Guaranteed = guaranteed };
			List<PlannedDrop> drops = new()
			{
				Entry(0.1f, true), Entry(0.3f, true), Entry(0.9f, true),
				Entry(0.2f, true, locked: true), Entry(0.2f, false, locked: true),
				Entry(0.5f, false),
				Entry(0.05f, true, guaranteed: true)
			};
			RandomNpcDropRule.Redistribute(drops);
			float factor = (1.3f + 0.4f) / 1.3f;
			float[] expected = { 0.1f * factor, 0.3f * factor, 1f, 0.2f, 0.2f, 0.5f, 0.05f };
			List<string> failures = new();
			for (int i = 0; i < expected.Length; i++)
			{
				if (Math.Abs(drops[i].Chance - expected[i]) > 1e-4f) failures.Add($"entry {i}: {drops[i].Chance:0.0000}, expected {expected[i]:0.0000}");
			}
			List<PlannedDrop> noLock = new() { Entry(0.25f, true), Entry(0.5f, false) };
			RandomNpcDropRule.Redistribute(noLock);
			if (noLock[0].Chance != 0.25f || noLock[1].Chance != 0.5f) failures.Add("chances moved without a locked entry");
			List<PlannedDrop> noReceiver = new() { Entry(0.25f, true, locked: true), Entry(0.5f, false), Entry(0.4f, true, guaranteed: true) };
			RandomNpcDropRule.Redistribute(noReceiver);
			if (noReceiver[0].Chance != 0.25f || noReceiver[1].Chance != 0.5f || noReceiver[2].Chance != 0.4f) failures.Add("chances moved without a receiver");
			Check.All(failures, "Redistribution");
		}

		// A replaced original drop that progression still locks does not drop: it becomes air in OnSpawn, before its slot is
		// synced (no luck: RuntimeJackpotChance 0); an unlocked one drops as its final replacement (no surprise, no madness).
		// Real Item.NewItem calls with the loot source of a detached dead enemy; the settings are restored, the items removed
		private static void LockedVanillaDropRemoved()
		{
			if (Main.netMode != NetmodeID.SinglePlayer) Check.Skip("single player only (drops spawn on the server)");
			LootConfig cfg = LootConfig.Instance;
			if (!cfg.RandomizeMobDrops || GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent) Check.Skip("enemy drops are not randomized in Coherent mode");
			int lockedNpc = 0, unlockedNpc = 0;
			PlannedDrop locked = default, unlocked = default;
			foreach (int netId in LootSnapshot.Npcs.Keys.Where(k => k > 0 && ContentSamples.NpcsByNetId.ContainsKey(k)).OrderBy(k => k))
			{
				if (NpcTiers.IsBoss(BossParts.Owner(netId))) continue;
				foreach (PlannedDrop drop in RandomNpcDropRule.Plan(netId, Main.expertMode, guaranteed: false, includeLocked: true))
				{
					// Permutation surprises are rolled by our rule, not swapped as they spawn
					if (drop.Extra || drop.Surprise || drop.Original <= ItemID.None || DropHelper.KeepOriginal(drop.Original) || drop.Item == drop.Original) continue;
					if (drop.Locked && lockedNpc == 0)
					{
						lockedNpc = netId;
						locked = drop;
					}
					else if (!drop.Locked && unlockedNpc == 0)
					{
						unlockedNpc = netId;
						unlocked = drop;
					}
				}
				if (lockedNpc != 0 && unlockedNpc != 0) break;
			}
			if (lockedNpc == 0) Check.Skip("no original drop is locked at this progression");
			ProgressionConfig progression = ProgressionConfig.Instance;
			float savedLuck = progression.RuntimeJackpotChance;
			float savedSurprise = cfg.SurpriseChance;
			List<int> slots = new();
			List<string> failures = new();
			int Spawn(int netId, int original)
			{
				NPC npc = new();
				npc.SetDefaults(netId);
				npc.Center = Main.LocalPlayer.Center;
				npc.life = 0;
				int slot = Item.NewItem(new Terraria.DataStructures.EntitySource_Loot(npc), npc.Hitbox, original, 1);
				slots.Add(slot);
				return slot;
			}
			try
			{
				progression.RuntimeJackpotChance = 0f;
				cfg.SurpriseChance = 0f;
				ItemPool.WildChanceOverride = 0f;
				for (int i = 0; i < 10; i++)
				{
					int slot = Spawn(lockedNpc, locked.Original);
					if (slot >= 0 && slot < Main.maxItems && Main.item[slot].active && !Main.item[slot].IsAir)
					{
						failures.Add($"{NpcName(lockedNpc)}: {Lang.GetItemNameValue(locked.Original)} dropped as {Lang.GetItemNameValue(Main.item[slot].type)} (planned {Lang.GetItemNameValue(locked.Item)}, locked by {locked.LockGap} tiers)");
						break;
					}
				}
				if (unlockedNpc != 0)
				{
					int slot = Spawn(unlockedNpc, unlocked.Original);
					int type = slot >= 0 && slot < Main.maxItems && Main.item[slot].active ? Main.item[slot].type : 0;
					if (type != unlocked.Item) failures.Add($"{NpcName(unlockedNpc)}: {Lang.GetItemNameValue(unlocked.Original)} dropped as {(type > 0 ? Lang.GetItemNameValue(type) : "nothing")}, planned {Lang.GetItemNameValue(unlocked.Item)}");
				}
			}
			finally
			{
				progression.RuntimeJackpotChance = savedLuck;
				cfg.SurpriseChance = savedSurprise;
				ItemPool.WildChanceOverride = null;
				ItemPool.ClearJackpot();
				foreach (int slot in slots.Distinct())
				{
					if (slot < 0 || slot >= Main.maxItems) continue;
					Main.item[slot].TurnToAir();
					Main.item[slot].active = false;
				}
			}
			Check.All(failures, "Locked original drops");
		}

		// Opened bags never give a jackpot, even with both jackpot chances at 100 %: their items go through the real
		// replacement of an opened bag (LootSubstitutionItem.BagReplacement), surprises included
		private static void BagNeverJackpot()
		{
			int[] bags = { ItemID.KingSlimeBossBag, ItemID.EyeOfCthulhuBossBag, ItemID.SkeletronBossBag, ItemID.WoodenCrate, ItemID.IronCrate, ItemID.GoldenCrate };
			ProgressionConfig progression = ProgressionConfig.Instance;
			float savedLuck = progression.RuntimeJackpotChance;
			List<string> failures = new();
			int rolls = 0;
			try
			{
				ItemPool.WildChanceOverride = 100f;
				progression.RuntimeJackpotChance = 100f;
				foreach (int bag in bags)
				{
					foreach (DropRateInfo entry in LootSnapshot.ForItem(bag))
					{
						for (int i = 0; i < 20; i++)
						{
							int item = LootSubstitutionItem.BagReplacement(bag, entry.itemId, new Random(bag * 1000 + i));
							rolls++;
							if (ItemPool.LastJackpotGap != 0 && failures.Count < 10) failures.Add($"{Lang.GetItemNameValue(bag)}: {Lang.GetItemNameValue(item)} is a jackpot (gap {ItemPool.LastJackpotGap})");
						}
					}
				}
			}
			finally
			{
				progression.RuntimeJackpotChance = savedLuck;
				ItemPool.WildChanceOverride = null;
				ItemPool.ClearJackpot();
			}
			if (rolls == 0) Check.Skip("no bag content captured");
			Check.All(failures, "Bag jackpots");
		}

		// With both jackpot chances forced (madness at 100 %, lucky rolls) and seeded rngs: the jackpot gaps fall off with each
		// extra tier (WildTierDecay, no cap), the gap is the item's true tier above the limit, and nothing else goes above the
		// progression limit (the hardmode wall included)
		private static void JackpotTail()
		{
			ProgressionConfig progression = ProgressionConfig.Instance;
			if (Main.hardMode) Check.Skip("the world is in hardmode");
			if (!progression.TierGating) Check.Skip("no progression limit");
			if (GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent) Check.Skip("only the coherent mode rolls jackpots");
			int limit = ItemPool.GatingLimit(0);
			if (limit >= Tiers.Max - 2) Check.Skip("not enough tiers above the limit");
			List<string> failures = new();
			int[] gaps = new int[Tiers.Count];
			try
			{
				ItemPool.WildChanceOverride = 100f;
				for (int i = 0; i < 6000; i++)
				{
					bool lucky = i % 2 == 1;
					RollRequest request = new()
					{
						Keys = new[] { 4343, i },
						SourceTier = lucky ? Tiers.Max : 0,
						OriginalItem = -1,
						Policy = CategoryPolicy.Weighted,
						TierJitterDown = 1,
						IgnoreGating = lucky,
						Wild = !lucky,
						WildRng = new Random(i)
					};
					int item = ItemPool.Roll(request);
					int gap = ItemPool.LastJackpotGap;
					int tier = ItemPool.TrueTierOf(item);
					// A hard gate (chlorophyte, luminite...) gets no tier allowance: its gap is counted from its own, lower limit
					int itemLimit = Math.Min(ItemPool.GatingLimitFor(item, 0), limit);
					if (gap > 0)
					{
						gaps[Math.Min(gap, Tiers.Max)]++;
						if (gap != tier - itemLimit) failures.Add($"{Lang.GetItemNameValue(item)}: gap {gap}, true tier {tier}, limit {itemLimit}");
					}
					else if (tier > itemLimit) failures.Add($"{Lang.GetItemNameValue(item)} (true tier {tier}) above the limit {itemLimit} without a jackpot");
					else if (tier >= (int)Tier.Hardmode) failures.Add($"{Lang.GetItemNameValue(item)} (true tier {tier}): a hardmode item before hardmode without a jackpot");
					if (failures.Count > 20) break;
				}
			}
			finally
			{
				ItemPool.WildChanceOverride = null;
				ItemPool.ClearJackpot();
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Jackpot tail: limit {limit}, gaps 1-4: {gaps[1]} {gaps[2]} {gaps[3]} {gaps[4]}, beyond {gaps.Skip(5).Sum()}");
			if (gaps[1] == 0) failures.Add("no jackpot one tier above the limit");
			if (gaps[2] >= gaps[1]) failures.Add($"{gaps[2]} jackpots two tiers above, not fewer than one tier above ({gaps[1]})");
			if (gaps[3] > gaps[2]) failures.Add($"{gaps[3]} jackpots three tiers above, more than two tiers above ({gaps[2]})");
			// Loose (expected ratio: decay, 3 by default): the buckets and the usefulness filter bend the tail, but each step
			// stays clearly rarer
			float decay = Math.Max(1.01f, progression.WildTierDecay);
			if (gaps[2] > 0 && gaps[1] < gaps[2] * decay / 2f) failures.Add($"one tier above only {gaps[1] / (float)gaps[2]:0.0} times as frequent as two tiers above (decay {decay})");
			Check.All(failures, "Jackpot tail");
		}

		// Guaranteed stations, summons and tools are shared between the players of a kill; materials are not; random extra
		// drops placing a basic station come RandomStationChanceFactor as often; other drops keep their chance
		private static void GuaranteeScalesWithPlayers()
		{
			List<string> failures = new();
			void Expect(float expected, float actual, string what)
			{
				if (Math.Abs(expected - actual) > 0.0001f) failures.Add($"{what}: {actual:0.0000}, expected {expected:0.0000}");
			}
			Expect(0.2f, RandomNpcDropRule.GuaranteeChance(0.2f, 1, true), "one player");
			Expect(0.2f, RandomNpcDropRule.GuaranteeChance(0.2f, 0, true), "no player counted");
			Expect(0.05f, RandomNpcDropRule.GuaranteeChance(0.2f, 4, true), "four players");
			Expect(0.2f, RandomNpcDropRule.GuaranteeChance(0.2f, 4, false), "four players, setting off");
			PlannedDrop station = new() { Item = ItemID.Furnace, Chance = 0.3f, Extra = true, Guaranteed = true, Single = true };
			PlannedDrop material = new() { Item = ItemID.Gel, Chance = 0.3f, Extra = true, Guaranteed = true };
			PlannedDrop randomBench = new() { Item = ItemID.WorkBench, Chance = 0.4f, Extra = true };
			PlannedDrop randomTable = new() { Item = ItemID.WoodenTable, Chance = 0.4f, Extra = true };
			PlannedDrop randomGear = new() { Item = ItemID.Minishark, Chance = 0.4f, Extra = true };
			Expect(0.1f, RandomNpcDropRule.KillChance(station, 1f, 3, true), "guaranteed furnace, three players");
			Expect(0.5f, RandomNpcDropRule.KillChance(station with { Chance = 0.8f }, 2f, 2, true), "guaranteed furnace with a bonus, capped at 1 then shared");
			Expect(0.3f, RandomNpcDropRule.KillChance(material, 1f, 3, true), "guaranteed material, three players");
			Expect(0.4f * RandomNpcDropRule.RandomStationChanceFactor, RandomNpcDropRule.KillChance(randomBench, 1f, 1, true), "random work bench");
			Expect(0.4f * RandomNpcDropRule.RandomStationChanceFactor, RandomNpcDropRule.KillChance(randomTable, 1f, 1, true), "random table");
			Expect(0.4f, RandomNpcDropRule.KillChance(randomGear, 1f, 3, true), "random Minishark");
			Check.All(failures, "Kill chances");
		}

		// The share of the jackpot chances follows the base life against the median of the tier, in [0.1, 1]: 1 for a typical
		// enemy, 0.1 for a 5-HP swarm enemy, never more for a weaker enemy of the same tier; bosses 1, critters 0
		private static void JackpotWorthScales()
		{
			List<string> failures = new();
			if (Math.Abs(RandomNpcDropRule.WorthOf(5, 50) - 0.1f) > 0.001f) failures.Add($"5 HP against 50: {RandomNpcDropRule.WorthOf(5, 50):0.00}, expected 0.1");
			if (Math.Abs(RandomNpcDropRule.WorthOf(1, 50) - RandomNpcDropRule.MinJackpotWorth) > 0.001f) failures.Add("no floor at 0.1");
			if (Math.Abs(RandomNpcDropRule.WorthOf(25, 50) - 0.5f) > 0.001f) failures.Add($"25 HP against 50: {RandomNpcDropRule.WorthOf(25, 50):0.00}, expected 0.5");
			if (Math.Abs(RandomNpcDropRule.WorthOf(50, 50) - 1f) > 0.001f || Math.Abs(RandomNpcDropRule.WorthOf(400, 50) - 1f) > 0.001f) failures.Add("a typical or sturdy enemy is not worth 1");
			if (RandomNpcDropRule.JackpotWorth(NPCID.Bunny) != 0f) failures.Add("a bunny may give a jackpot");
			if (RandomNpcDropRule.JackpotWorth(NPCID.EyeofCthulhu) != 1f) failures.Add("the Eye of Cthulhu is not worth 1");
			List<int> enemies = EnemySwapPool.NaturalEnemies(0).Where(ContentSamples.NpcsByNetId.ContainsKey).Where(n => NpcTiers.EffectiveTier(n) == 0).ToList();
			if (enemies.Count < 3) Check.Skip("fewer than 3 natural enemies at tier 0");
			float median = MobLootSystem.MedianLife(0);
			if (median <= 0f) failures.Add("no median life at tier 0");
			float previous = 0f;
			int previousLife = 0;
			int atOne = 0;
			foreach (int npc in enemies.OrderBy(n => ContentSamples.NpcsByNetId[n].lifeMax))
			{
				int life = ContentSamples.NpcsByNetId[npc].lifeMax;
				float worth = RandomNpcDropRule.JackpotWorth(npc);
				if (worth < RandomNpcDropRule.MinJackpotWorth - 0.0001f || worth > 1.0001f) failures.Add($"{NpcName(npc)}: worth {worth:0.00}");
				if (worth + 0.0001f < previous && life > previousLife) failures.Add($"{NpcName(npc)} ({life} HP) worth {worth:0.00}, less than a weaker enemy ({previous:0.00})");
				if (worth > 0.999f) atOne++;
				// At or above the median life: a typical enemy, worth 1; below, its share of the median
				float expected = RandomNpcDropRule.WorthOf(life, median);
				if (median > 0f && Math.Abs(worth - expected) > 0.001f) failures.Add($"{NpcName(npc)} ({life} HP, median {median:0}): worth {worth:0.00}, expected {expected:0.00}");
				previous = worth;
				previousLife = life;
			}
			if (atOne == 0) failures.Add($"no tier 0 enemy is worth 1 (median {median:0} HP)");
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"JackpotWorth: median life {median:0} at tier 0, {atOne} of {enemies.Count} enemies worth 1");
			Check.All(failures, "Jackpot worth");
		}

		// An NPC spawned by another NPC (Mother Slime babies...) is flagged in its slot, even after SetDefaults, and its kill
		// gets nothing from the extra-drop rule but its original entries (permutation style); the flag does not stick to the next NPC of the slot. Worm segments and bosses
		// are never flagged. Spawned NPCs and items are removed
		private static void SpawnedEnemyNoExtraDrops()
		{
			if (Main.netMode != NetmodeID.SinglePlayer) Check.Skip("single player only (NPCs spawn on the server)");
			Player player = Main.LocalPlayer;
			NPC Spawn(Terraria.DataStructures.IEntitySource source, int type)
			{
				int index = NPC.NewNPC(source, (int)player.Center.X + 1200, (int)player.Center.Y - 200, type);
				return index >= 0 && index < Main.maxNPCs ? Main.npc[index] : null;
			}
			NPC parent = null, child = null;
			List<int> slots = new();
			List<string> failures = new();
			try
			{
				parent = Spawn(new Terraria.DataStructures.EntitySource_DebugCommand("UltimateRandomizer test"), NPCID.Zombie);
				Check.That(parent != null, "could not spawn the parent");
				child = Spawn(new Terraria.DataStructures.EntitySource_Parent(parent), NPCID.BlueSlime);
				Check.That(child != null, "could not spawn the child");
				if (MobLootSystem.IsSpawnedByNpc(parent)) failures.Add("the parent is flagged");
				if (!MobLootSystem.IsSpawnedByNpc(child)) failures.Add("the child is not flagged");
				// Vanilla sets variants with SetDefaults right after spawning: the flag stays with the slot
				child.SetDefaults(NPCID.BlueSlime);
				if (!MobLootSystem.IsSpawnedByNpc(child)) failures.Add("the flag is lost by SetDefaults");
				CodexSystem.SpawnLog = slots;
				RandomNpcDropRule rule = new(child.netID);
				DropAttemptInfo info = new() { npc = child, player = player, rng = Main.rand, IsExpertMode = Main.expertMode, IsMasterMode = Main.masterMode };
				// In permutation style the rule also rolls the enemy's original entries (vanilla drops them for spawned enemies
				// too): those may drop, nothing else (no extra, guaranteed or bonus drop)
				bool permuted = DropPermutation.Active(child.netID) || BossPermutation.Active(child.netID);
				HashSet<int> originals = permuted
					? RandomNpcDropRule.OriginalDrops(child.netID, BossParts.Owner(child.type)).Where(d => d.Rolled).Select(d => d.Item).ToHashSet()
					: new HashSet<int>();
				ItemDropAttemptResultState state = rule.TryDroppingItem(info).State;
				if (!permuted && state != ItemDropAttemptResultState.DidNotRunCode) failures.Add($"the child's extra-drop rule ran ({state})");
				foreach (int slot in slots)
				{
					if (slot < 0 || slot >= Main.maxItems || originals.Contains(Main.item[slot].type)) continue;
					failures.Add($"the child dropped {Lang.GetItemNameValue(Main.item[slot].type)}, not one of its original entries");
				}
				// A new NPC in the slot (here: the same one spawned again) gets its own flag
				new SpawnOriginNPC().OnSpawn(child, new Terraria.DataStructures.EntitySource_SpawnNPC());
				if (MobLootSystem.IsSpawnedByNpc(child)) failures.Add("the flag sticks to the next NPC of the slot");
				else if (LootConfig.Instance.RandomizeMobDrops && rule.TryDroppingItem(info).State == ItemDropAttemptResultState.DidNotRunCode) failures.Add("an ordinary slime's extra-drop rule did not run");
				CodexSystem.SpawnLog = null;
				foreach (int type in new[] { NPCID.GiantWormBody, NPCID.CultistBoss, NPCID.DungeonSpirit })
				{
					NPC sample = new();
					sample.SetDefaults(type);
					if (SpawnOriginNPC.IsNpcChild(sample, new Terraria.DataStructures.EntitySource_Parent(parent))) failures.Add($"{NpcName(type)} would be flagged");
				}
			}
			finally
			{
				CodexSystem.SpawnLog = null;
				ItemPool.ClearJackpot();
				foreach (int slot in slots.Distinct())
				{
					if (slot < 0 || slot >= Main.maxItems) continue;
					Main.item[slot].TurnToAir();
					Main.item[slot].active = false;
				}
				TestRunner.RemoveNpc(child);
				TestRunner.RemoveNpc(parent);
			}
			Check.All(failures, "Enemies spawned by another enemy");
		}

		// Critters never give a jackpot: not lucky, no madness, even with both chances at 100 %, through the real drop rule on
		// detached critters (the items it spawns are removed, the chances restored)
		private const int KillsPerCritter = 20;

		private static void CrittersNoJackpot()
		{
			LootConfig cfg = LootConfig.Instance;
			List<string> failures = new();
			foreach (int critter in new[] { NPCID.Bunny, NPCID.Bird, NPCID.Butterfly })
			{
				if (RandomNpcDropRule.JackpotWorth(critter) != 0f) failures.Add($"{NpcName(critter)} is worth {RandomNpcDropRule.JackpotWorth(critter):0.00}");
			}
			for (int i = 0; i < 1000; i++)
			{
				if (DropHelper.RollLucky(0f))
				{
					failures.Add("a lucky roll with no worth");
					break;
				}
			}
			if (!cfg.RandomizeMobDrops || cfg.DropTableStyle == DropTableStyle.MirrorOriginal || !cfg.CrittersCanDrop || GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent)
			{
				Check.All(failures, "Critter jackpots");
				Check.Skip("critters get no random drops");
			}
			if (!ProgressionConfig.Instance.TierGating || ItemPool.GatingLimit(0) >= Tiers.Max) Check.Skip("no progression limit");
			if (Main.netMode != NetmodeID.SinglePlayer) Check.Skip("single player only (drops spawn on the server)");
			ProgressionConfig progression = ProgressionConfig.Instance;
			float savedLuck = progression.RuntimeJackpotChance;
			List<int> slots = new();
			int drops = 0;
			try
			{
				ItemPool.WildChanceOverride = 100f;
				progression.RuntimeJackpotChance = 100f;
				CodexSystem.SpawnLog = slots;
				foreach (int critter in new[] { NPCID.Bunny, NPCID.Bird })
				{
					NPC npc = new();
					npc.SetDefaults(critter);
					npc.Center = Main.LocalPlayer.Center;
					npc.life = 0;
					RandomNpcDropRule rule = new(critter);
					for (int i = 0; i < KillsPerCritter; i++)
					{
						rule.TryDroppingItem(new DropAttemptInfo { npc = npc, player = Main.LocalPlayer, rng = Main.rand, IsExpertMode = Main.expertMode, IsMasterMode = Main.masterMode });
						// Read, then removed right away: a later kill must not reuse a slot before it is read
						foreach (int slot in slots.Distinct())
						{
							if (slot < 0 || slot >= Main.maxItems || !Main.item[slot].active) continue;
							drops++;
							Item item = Main.item[slot];
							if (JackpotItem.GapOf(item) > 0 && failures.Count < 10) failures.Add($"{Lang.GetItemNameValue(item.type)} dropped by a critter is a jackpot");
							item.TurnToAir();
							item.active = false;
						}
						slots.Clear();
					}
				}
				CodexSystem.SpawnLog = null;
			}
			finally
			{
				CodexSystem.SpawnLog = null;
				progression.RuntimeJackpotChance = savedLuck;
				ItemPool.WildChanceOverride = null;
				ItemPool.ClearJackpot();
				foreach (int slot in slots.Distinct())
				{
					if (slot < 0 || slot >= Main.maxItems) continue;
					Main.item[slot].TurnToAir();
					Main.item[slot].active = false;
				}
			}
			if (drops == 0) Check.Skip($"critters dropped nothing in {2 * KillsPerCritter} kills");
			Check.All(failures, "Critter jackpots");
		}

		// Critters get a share (CritterDropFactor) of the small frequent drop, here at abundance 3
		// Permutation style: the enemies' PermutationExtraSlots slots, each at CritterDropFactor times an enemy slot's rate (at
		// most DropRateMultiplier / 1 before it), and no small frequent drop. Checked with the style given explicitly
		private static void CrittersDrop()
		{
			LootConfig cfg = LootConfig.Instance;
			if (!cfg.RandomizeMobDrops || cfg.DropTableStyle == DropTableStyle.MirrorOriginal || !cfg.CrittersCanDrop) Check.Skip("critters get no random drops");
			if (GeneralConfig.Instance.RandomizerMode == RandomizerMode.Shuffle) Check.Skip("shuffle mode has no extra drops");
			List<string> permuted = new();
			int slots = Math.Clamp(cfg.PermutationExtraSlots, 0, 3);
			float factor = Math.Max(0f, cfg.CritterDropFactor);
			// Plus one small frequent drop at CritterCommonDropChance (none at 0), last, whatever the abundance
			float commonChance = Math.Clamp(cfg.CritterCommonDropChance / 100f, 0f, 1f);
			int commons = commonChance > 0.0001f ? 1 : 0;
			foreach (int npc in new[] { NPCID.Bunny, NPCID.Mouse, NPCID.Butterfly })
			{
				foreach (float abundance in new[] { 1f, 3f })
				{
					List<PlannedDrop> extras = RandomNpcDropRule.ExtraDrops(npc, npc, Main.expertMode, abundance, style: DropTableStyle.Permutation);
					if (extras.Count != slots + commons) permuted.Add($"{NpcName(npc)}: {extras.Count} random drops in the permutation style at abundance {abundance}, {slots + commons} expected");
					// The frequent drop: the same item at any chance (explicit chances, not the config), none at 0 %, no jackpot
					List<PlannedDrop> sure = RandomNpcDropRule.ExtraDrops(npc, npc, Main.expertMode, abundance, style: DropTableStyle.Permutation, critterCommon: 100f);
					List<PlannedDrop> none = RandomNpcDropRule.ExtraDrops(npc, npc, Main.expertMode, abundance, style: DropTableStyle.Permutation, critterCommon: 0f);
					if (none.Count != slots) permuted.Add($"{NpcName(npc)}: {none.Count} random drops with no frequent drop, {slots} expected");
					if (sure.Count != slots + 1 || Math.Abs(sure[^1].Chance - 1f) > 1e-4f) permuted.Add($"{NpcName(npc)}: no sure frequent drop at 100 %");
					else
					{
						ItemCategory category = ItemPool.IsValid(sure[^1].Item) ? ItemPool.Info[sure[^1].Item].Category : ItemCategory.Misc;
						if (GeneralConfig.Instance.RandomizerMode != RandomizerMode.Chaos && CommonDropWeights.Instance.Get(category) <= 0)
							permuted.Add($"{NpcName(npc)}: frequent drop {Lang.GetItemNameValue(sure[^1].Item)} ({category}) has no common drop weight");
						if (commons == 1 && (extras.Count != slots + 1 || extras[^1].Item != sure[^1].Item || Math.Abs(extras[^1].Chance - commonChance) > 1e-4f || extras[^1].JackpotGap > 0))
							permuted.Add($"{NpcName(npc)}: frequent drop not {Lang.GetItemNameValue(sure[^1].Item)} at {commonChance:P0}");
					}
					// Each slot is the same roll as without the factor (same item), at CritterDropFactor times its chance (capped at
					// 100 %: a sure slot without the factor stays at least min(1, factor))
					List<PlannedDrop> unscaled = RandomNpcDropRule.ExtraDrops(npc, npc, Main.expertMode, abundance, style: DropTableStyle.Permutation, critterFactor: 1f);
					for (int s = 0; s < Math.Min(slots, Math.Min(extras.Count, unscaled.Count)); s++)
					{
						PlannedDrop with = extras[s];
						PlannedDrop without = unscaled[s];
						if (with.Item != without.Item)
						{
							permuted.Add($"{NpcName(npc)} slot {s}: {Lang.GetItemNameValue(with.Item)} with the critter factor, {Lang.GetItemNameValue(without.Item)} without");
							continue;
						}
						bool ok = without.Chance < 1f - 1e-4f
							? Math.Abs(with.Chance - Math.Min(1f, without.Chance * factor)) < 1e-4f
							: with.Chance >= Math.Min(1f, factor) - 1e-4f;
						if (!ok) permuted.Add($"{NpcName(npc)} slot {s} at abundance {abundance}: {with.Chance:P2}, {without.Chance:P2} without the factor {factor}");
					}
				}
			}
			if (cfg.DropTableStyle == DropTableStyle.Permutation)
			{
				Check.All(permuted, "Critters in the permutation style");
				return;
			}
			float first = Math.Min(1f, cfg.CommonDropChance / 100f * 3f * cfg.CritterDropFactor);
			if (first <= 0.0001f)
			{
				// No small frequent drop with these settings: the permutation slots are still checked
				Check.All(permuted, "Critters in the permutation style");
				return;
			}
			List<string> failures = new();
			foreach (int npc in new[] { NPCID.Bunny, NPCID.Mouse, NPCID.Butterfly })
			{
				// The final plan: the plan of now gives the chances of its locked entries to the unlocked ones (Redistribute), so
				// the frequent drop may be locked or raised there; the critter still drops at least as often
				List<PlannedDrop> plan = RandomNpcDropRule.Plan(npc, Main.expertMode, lucky: true, abundance: 3f);
				if (!plan.Any(d => d.Extra && Math.Abs(d.Chance - first) < 0.001f)) failures.Add($"{NpcName(npc)}: no frequent drop at {first * 100:0} % ({plan.Count(d => d.Extra)} random drops)");
				List<PlannedDrop> now = RandomNpcDropRule.Plan(npc, Main.expertMode, abundance: 3f);
				if (now.Any(d => d.Extra) && now.Where(d => d.Extra).Max(d => d.Chance) < first - 0.001f) failures.Add($"{NpcName(npc)}: no drop at {first * 100:0} % or more now");
			}
			failures.AddRange(permuted);
			Check.All(failures, "Critters without their frequent drop");
		}

		// The madness tail, with explicit parameters: about `chance` % above the limit, each tier `decay` times rarer
		private static void WildTail()
		{
			const int rolls = 100000;
			const int limit = 2;
			Random rng = new(1234);
			int[] counts = new int[Tiers.Count];
			int beyond = 0;
			for (int i = 0; i < rolls; i++)
			{
				int tier = ItemPool.WildTier(limit, 8f, 3f, rng);
				if (tier > Tiers.Max) beyond++;
				else counts[tier]++;
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"WildTail: limit {limit}, tiers {string.Join(" / ", counts)} out of {rolls}");
			List<string> failures = new();
			int above = rolls - counts[limit] - beyond;
			double share = above * 100.0 / rolls;
			if (beyond > 0) failures.Add($"{beyond} rolls above tier {Tiers.Max}");
			if (counts.Take(limit).Sum() > 0) failures.Add("rolls below the limit");
			if (share < 7.3 || share > 8.7) failures.Add($"{share:0.00} % above the limit, expected about 8");
			for (int t = limit + 1; t <= limit + 2; t++)
			{
				double ratio = counts[t + 1] == 0 ? double.MaxValue : counts[t] / (double)counts[t + 1];
				if (ratio < 2.4 || ratio > 3.75) failures.Add($"tier {t} is {ratio:0.00} times as frequent as tier {t + 1}, expected about 3");
			}
			// Near the top, the tail stops at tier 9
			for (int i = 0; i < 2000; i++)
			{
				if (ItemPool.WildTier(7, 50f, 1.5f, rng) > Tiers.Max) { failures.Add("a roll from tier 7 went above tier 9"); break; }
			}
			int none = 0;
			for (int i = 0; i < 10000; i++) if (ItemPool.WildTier(limit, 0f, 3f, rng) != limit) none++;
			if (none > 0) failures.Add($"{none} rolls above the limit with a chance of 0 %");
			Check.All(failures, "Wild tier tail");
		}

		// Every runtime roll above the progression limit (madness or luck) is useful: equipment or a progression station,
		// never an ore, a block or a material; its gap is its tier above the limit
		private static void JackpotUseful()
		{
			ProgressionConfig progression = ProgressionConfig.Instance;
			if (!progression.TierGating) Check.Skip("no progression limit");
			if (GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent) Check.Skip("only the coherent mode rolls jackpots");
			int limit = ItemPool.GatingLimit(0);
			if (limit >= Tiers.Max) Check.Skip("the limit is already the last tier");
			List<string> failures = new();
			int jackpots = 0;
			try
			{
				ItemPool.WildChanceOverride = 100f;
				for (int i = 0; i < 3000; i++)
				{
					bool lucky = i % 2 == 1;
					RollRequest request = new()
					{
						Keys = new[] { 4242, i },
						SourceTier = lucky ? Tiers.Max : 0,
						OriginalItem = -1,
						Policy = CategoryPolicy.Weighted,
						TierJitterDown = 1,
						IgnoreGating = lucky,
						Wild = !lucky,
						WildRng = new Random(i)
					};
					int item = ItemPool.Roll(request);
					int gap = ItemPool.LastJackpotGap;
					if (ItemPool.TrueTierOf(item) > limit && gap == 0) failures.Add($"{Lang.GetItemNameValue(item)} above the limit without a jackpot mark");
					if (gap == 0) continue;
					jackpots++;
					if (!ItemPool.IsJackpotWorthy(item)) failures.Add($"{Lang.GetItemNameValue(item)} ({ItemPool.Info[item].Category}) as a jackpot");
					// A hard gate's gap counts from the limit without the allowance (ItemPool.GatingLimitFor)
					if (gap != ItemPool.TrueTierOf(item) - ItemPool.GatingLimitFor(item, 0)) failures.Add($"{Lang.GetItemNameValue(item)}: gap {gap}, true tier {ItemPool.TrueTierOf(item)}, limit {limit}");
					if (failures.Count > 20) break;
				}
				// Plans never roll jackpots
				RollRequest planned = new() { Keys = new[] { 4243, 1 }, SourceTier = Tiers.Max, OriginalItem = -1, Policy = CategoryPolicy.Weighted, IgnoreGating = true };
				ItemPool.Roll(planned);
				if (ItemPool.LastJackpotGap != 0) failures.Add("a roll without the runtime rng is a jackpot");
			}
			finally
			{
				ItemPool.WildChanceOverride = null;
				ItemPool.ClearJackpot();
			}
			Check.That(jackpots > 0, "no jackpot in 3000 rolls above the limit");
			// Never something without effect: music boxes (accessories), pets and light pets, dyes, vanity; real equipment is
			int[] useless = { ItemID.MusicBoxOverworldDay, ItemID.Carrot, ItemID.FairyBell, ItemID.WispinaBottle, ItemID.BlackDye, ItemID.SummerHat };
			foreach (int item in useless.Where(ItemPool.IsValid))
			{
				if (ItemPool.IsJackpotWorthy(item)) failures.Add($"{Lang.GetItemNameValue(item)} counts as a jackpot");
			}
			foreach (int item in new int[] { ItemID.Minishark, ItemID.HermesBoots }.Where(ItemPool.IsValid))
			{
				if (!ItemPool.IsJackpotWorthy(item)) failures.Add($"{Lang.GetItemNameValue(item)} is not worth a jackpot");
			}
			Check.All(failures, "Useless jackpots");
		}

		// A jackpot is not recorded as the enemy's (or the bag's) drop, its mark follows a copy of the item, and no planned
		// drop (journal, codex, hints) is a jackpot
		private static void JackpotNotInEnemyDrops()
		{
			Item item = new(ItemID.Zenith);
			Check.That(CodexSystem.CountsAsDrop(item), "an ordinary drop is not recorded");
			Check.That(item.TryGetGlobalItem(out JackpotItem jackpot), "no jackpot data on items");
			// Set directly: Mark would also show the effects
			jackpot.Gap = 3;
			Check.Equal(3, JackpotItem.GapOf(item), "gap of a marked item");
			Check.That(!CodexSystem.CountsAsDrop(item), "a jackpot is recorded as a drop");
			Check.Equal(3, JackpotItem.GapOf(item.Clone()), "gap of a copy (picked up)");
			List<string> failures = new();
			foreach (int npc in LootSnapshot.Npcs.Keys.OrderBy(k => k).Where((_, i) => i % 10 == 0))
			{
				if (RandomNpcDropRule.Plan(npc, Main.expertMode, lucky: true).Any(d => d.JackpotGap > 0)) failures.Add(Lang.GetNPCNameValue(npc));
			}
			Check.All(failures, "Planned drops marked as jackpots");
		}

		// Kills through the real extra-drop rule with madness at 100 %: the drops above the limit are marked with their gap,
		// useful, and the others are not marked. Batches of 40 kills (each batch read, then its items removed: Main.item would
		// recycle them) until a jackpot shows up: the permutation style has few extra slots at vanilla-like rates
		private static void JackpotDropsMarked()
		{
			if (Main.netMode != NetmodeID.SinglePlayer) Check.Skip("single player only (drops spawn on the server)");
			LootConfig cfg = LootConfig.Instance;
			if (!cfg.RandomizeMobDrops || cfg.DropTableStyle == DropTableStyle.MirrorOriginal || GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent) Check.Skip("no random extra drops");
			if (!ProgressionConfig.Instance.TierGating) Check.Skip("no progression limit");
			int limit = ItemPool.GatingLimit(0);
			if (limit >= Tiers.Max) Check.Skip("the limit is already the last tier");
			if (RandomNpcDropRule.ExtraDrops(NPCID.Zombie, NPCID.Zombie, Main.expertMode).Count == 0) Check.Skip("the zombie has no random extra drop");
			NPC npc = new();
			npc.SetDefaults(NPCID.Zombie);
			npc.Center = Main.LocalPlayer.Center;
			npc.life = 0;
			List<int> slots = new();
			List<string> failures = new();
			int marked = 0, drops = 0, kills = 0;
			void Clear()
			{
				foreach (int slot in slots.Distinct())
				{
					if (slot < 0 || slot >= Main.maxItems) continue;
					Main.item[slot].TurnToAir();
					Main.item[slot].active = false;
				}
				drops += slots.Count;
				slots.Clear();
			}
			try
			{
				ItemPool.WildChanceOverride = 100f;
				RandomNpcDropRule rule = new(NPCID.Zombie);
				for (int batch = 0; batch < 50 && marked == 0; batch++)
				{
					CodexSystem.SpawnLog = slots;
					for (int i = 0; i < 40; i++, kills++)
					{
						rule.TryDroppingItem(new DropAttemptInfo { npc = npc, player = Main.LocalPlayer, rng = Main.rand, IsExpertMode = Main.expertMode, IsMasterMode = Main.masterMode });
					}
					CodexSystem.SpawnLog = null;
					foreach (int slot in slots.Distinct())
					{
						if (slot < 0 || slot >= Main.maxItems || !Main.item[slot].active) continue;
						Item item = Main.item[slot];
						int gap = JackpotItem.GapOf(item);
						if (gap == 0) continue;
						marked++;
						if (!ItemPool.IsJackpotWorthy(item.type)) failures.Add($"{Lang.GetItemNameValue(item.type)} marked but not useful");
						if (gap != ItemPool.TrueTierOf(item.type) - ItemPool.GatingLimitFor(item.type, 0)) failures.Add($"{Lang.GetItemNameValue(item.type)}: gap {gap}, true tier {ItemPool.TrueTierOf(item.type)}, limit {limit}");
						if (CodexSystem.CountsAsDrop(item)) failures.Add($"{Lang.GetItemNameValue(item.type)} would be recorded as the zombie's drop");
					}
					Clear();
				}
			}
			finally
			{
				CodexSystem.SpawnLog = null;
				ItemPool.WildChanceOverride = null;
				ItemPool.ClearJackpot();
				Clear();
			}
			Check.That(marked > 0, $"no marked jackpot in {drops} drops of {kills} kills");
			Check.All(failures, "Jackpot drops");
		}

		// A giant's bonus item is useful (equipment that does something, a progression station) and within the limit unless it
		// is a jackpot, which comes about GiantWildMultiplier times as often as with the usual madness chance (seeded rngs)
		private static void GiantBonusUseful()
		{
			if (!LootConfig.Instance.RandomizeMobDrops || GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent) Check.Skip("no random extra drops");
			List<string> failures = new();
			int limit = ItemPool.GatingLimit(0);
			int useful = 0;
			const int Rolls = 2000;
			try
			{
				for (int i = 0; i < Rolls; i++)
				{
					int item = RandomNpcDropRule.GiantDrop(NPCID.Zombie, 5000 + i, false);
					int gap = ItemPool.LastJackpotGap;
					if (ItemPool.IsJackpotWorthy(item)) useful++;
					if (gap == 0 && ProgressionConfig.Instance.TierGating && ItemPool.TrueTierOf(item) > limit && failures.Count < 10) failures.Add($"{Lang.GetItemNameValue(item)} above the limit without a jackpot");
				}
				if (useful < Rolls * 0.98) failures.Add($"only {useful} useful items out of {Rolls}");
				// The jackpot rate against the usual one, on the same rolls
				if (ProgressionConfig.Instance.TierGating && ProgressionConfig.Instance.WildDropChance > 0 && limit < Tiers.Max)
				{
					int Jackpots(float multiplier)
					{
						int count = 0;
						for (int i = 0; i < 4000; i++)
						{
							RandomNpcDropRule.GiantDrop(NPCID.Zombie, 9000 + i, false, multiplier);
							if (ItemPool.LastJackpotGap > 0) count++;
						}
						return count;
					}
					int normal = Jackpots(1f);
					int giant = Jackpots(RandomNpcDropRule.GiantWildMultiplier);
					ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Giant jackpots: {giant} per 4000 bonus drops, {normal} with the usual madness chance");
					double expected = RandomNpcDropRule.GiantWildMultiplier * normal;
					if (normal >= 20 && (giant < expected * 0.7 || giant > expected * 1.3)) failures.Add($"{giant} giant jackpots, expected about {expected:0} ({normal} x {RandomNpcDropRule.GiantWildMultiplier})");
				}
			}
			finally
			{
				ItemPool.ClearJackpot();
			}
			Check.All(failures, "Giant bonus drops");
		}

		// A mini gives one bonus item, a giant one useful item, an ordinary enemy none; a boss minion never does
		private static void MiniBonusDrop()
		{
			if (!LootConfig.Instance.RandomizeMobDrops) Check.Skip("enemy drops are not randomized");
			EnemiesConfig cfg = EnemiesConfig.Instance;
			List<string> failures = new();
			int Count(int type, EnemyKind kind, out int item)
			{
				NPC npc = new();
				npc.SetDefaults(type);
				item = 0;
				if (!npc.TryGetGlobalNPC(out EnemyIndividualNPC individual)) return -1;
				individual.Kind = kind;
				List<(int item, int stack, int gap)> drops = RandomNpcDropRule.BonusDrops(npc, npc.netID, 77, false);
				if (drops.Count > 0) item = drops[0].item;
				return drops.Count;
			}
			try
			{
				if (cfg.MiniBonusDrop && Count(NPCID.Zombie, EnemyKind.Mini, out _) != 1) failures.Add("a mini zombie gives no bonus item");
				if (cfg.GiantBonusDrop)
				{
					if (Count(NPCID.Zombie, EnemyKind.Giant, out int item) != 1) failures.Add("a giant zombie gives no bonus item");
					// Only the coherent mode rolls by category (Chaos picks anything)
					else if (GeneralConfig.Instance.RandomizerMode == RandomizerMode.Coherent && !ItemPool.IsJackpotWorthy(item)) failures.Add($"a giant zombie gives {Lang.GetItemNameValue(item)}");
				}
				if (Count(NPCID.Zombie, EnemyKind.Normal, out _) != 0) failures.Add("an ordinary zombie gives a bonus item");
				foreach (int minion in new int[] { NPCID.ServantofCthulhu, NPCID.Creeper, NPCID.Bee })
				{
					if (Count(minion, EnemyKind.Mini, out _) > 0 || Count(minion, EnemyKind.Giant, out _) > 0) failures.Add($"{Lang.GetNPCNameValue(minion)} gives a bonus item");
				}
			}
			finally
			{
				ItemPool.ClearJackpot();
			}
			if (!cfg.MiniBonusDrop && !cfg.GiantBonusDrop) Check.Skip("giant and mini bonus drops are off");
			Check.All(failures, "Giant and mini bonus drops");
		}

		// Jackpots rarer than in 1.2.0 (madness 12 %, luck 4 %) and 1.2.1 (9 %, 3 %, decay 3, allowance 3): items up to two
		// tiers ahead already come normally. 1.3.0 raised 5 % / 2 % to 7 % / 3 % (weak enemies only get a share of them:
		// JackpotWorth); 1.3.2 went back to 5 % / 2 %, the far jackpots were too many; after 1.3.4, 2 % / 1 %, a share of them
		// early (JackpotStartShare 30 %) and at most one tier above the limit; then back to 5 % / 2 % at every tier, no cap, each
		// extra tier 3 times rarer, and an allowance of 1 (2 before)
		private static void JackpotRatesLower()
		{
			float Default(string field) => Convert.ToSingle(typeof(ProgressionConfig).GetField(field).GetCustomAttributes(typeof(System.ComponentModel.DefaultValueAttribute), false)
				.OfType<System.ComponentModel.DefaultValueAttribute>().First().Value);
			Check.Equal(5f, Default("WildDropChance"), "default WildDropChance");
			Check.Equal(2f, Default("RuntimeJackpotChance"), "default RuntimeJackpotChance");
			Check.Equal(3f, Default("WildTierDecay"), "default WildTierDecay");
			Check.Equal(1f, Default("TierAllowance"), "default TierAllowance");
		}

		// Plans, findability and guarantees never use the madness tail: identical at 0 % and 50 %
		private static void PlansIgnoreWild()
		{
			int[] npcs = LootSnapshot.Npcs.Keys.OrderBy(k => k).Where((_, i) => i % 8 == 0).ToArray();
			string Snapshot(float chance)
			{
				ItemPool.WildChanceOverride = chance;
				GuaranteedSources.Reset();
				Findability.Reset();
				IEnumerable<string> plans = npcs.Select(n => string.Join(",", RandomNpcDropRule.Plan(n, Main.expertMode).Select(d => $"{d.Item}:{d.Chance:0.0000}")));
				IEnumerable<string> tiers = Enumerable.Range(1, ItemPool.Info.Length - 1).Select(t => Findability.Tier(t).ToString());
				IEnumerable<string> hosts = GuaranteedSources.Requirements.Select(r => r.Item + ">" + string.Join(",", GuaranteedSources.HostsOf(r.Item).OrderBy(h => h)));
				return string.Join("|", plans) + "#" + string.Join(",", tiers) + "#" + string.Join(";", hosts);
			}
			try
			{
				string calm = Snapshot(0f);
				string wild = Snapshot(50f);
				Check.That(calm == wild, "a plan, the findability or the guaranteed sources change with WildDropChance");
			}
			finally
			{
				ItemPool.WildChanceOverride = null;
				GuaranteedSources.Reset();
				Findability.Reset();
			}
		}

		private static string NpcName(int netId) => ContentSamples.NpcsByNetId.TryGetValue(netId, out NPC sample) ? sample.TypeName : netId.ToString();

		// What is wrong with the stack range of a random extra drop or surprise (null: nothing): single use openers and summons
		// one at a time, potions and food a few, ammo and throwing weapons by dozens at most (StackRules.ExtraRange)
		private static string ExtraStackProblem(int item, int min, int max)
		{
			if (!ItemPool.IsValid(item)) return null;
			string what = $"{Lang.GetItemNameValue(item)} {min}-{max}";
			if (min < 1 || max < min) return what + ": empty range";
			if (StackRules.IsSingleUse(item) && max != 1) return what + ": a single use item";
			ItemCategory category = ItemPool.Info[item].Category;
			if (category is ItemCategory.Potion or ItemCategory.Food && max > StackRules.ExtraConsumableCap) return what + ": too many potions or food";
			if (category == ItemCategory.Ammo && max > StackRules.ExtraAmmoCap) return what + ": too much ammo";
			if (StackRules.IsThrown(item) && max > StackRules.ExtraThrownCap) return what + ": too many throwing weapons";
			return null;
		}

		// Random extra drops and surprises keep their chances but come in small stacks: pure checks of StackRules.ExtraRange
		// (fewer ammo above the source's tier, materials as their caps say), then every extra slot of every enemy and boss
		// (bags carry their boss's) and every surprise and synthetic entry of this world's permutation
		private static void ExtraStacksSmall()
		{
			if (!ItemPool.Ready) Check.Skip("the item pool is not built");
			List<string> failures = new();
			void Fail(string message)
			{
				if (failures.Count < 30) failures.Add(message);
			}
			foreach (int item in new[] { ItemID.SuperHealingPotion, ItemID.InfernoPotion, ItemID.IchorBullet, ItemID.ChlorophyteArrow, ItemID.TempleKey, ItemID.FrozenKey,
				ItemID.Shuriken, ItemID.MechanicalSkull, ItemID.CelestialSigil, ItemID.BBQRibs })
			{
				foreach (int tier in new[] { 0, Tiers.Max })
				{
					(int min, int max) = StackRules.ExtraRange(item, tier);
					if (ExtraStackProblem(item, min, max) is string problem) Fail($"pure, source tier {tier}: {problem}");
				}
			}
			if (StackRules.ExtraRange(ItemID.Gel, 0) != StackRules.Range(ItemID.Gel)) Fail($"Gel: {StackRules.ExtraRange(ItemID.Gel, 0)} instead of {StackRules.Range(ItemID.Gel)}");
			if (StackRules.Cap(ItemID.ChlorophyteArrow) >= StackRules.ExtraAmmoCap && StackRules.ExtraRange(ItemID.ChlorophyteArrow, 0).max >= StackRules.ExtraRange(ItemID.ChlorophyteArrow, Tiers.Max).max)
			{
				Fail("Chlorophyte Arrows: no fewer from a source far below them");
			}
			if (!LootConfig.Instance.RandomizeMobDrops)
			{
				Check.All(failures, "Extra stacks");
				return;
			}
			int extras = 0, surprises = 0;
			foreach (int netId in ContentSamples.NpcsByNetId.Keys.OrderBy(n => n))
			{
				int type = BossParts.Owner(ContentSamples.NpcsByNetId[netId].type);
				foreach (PlannedDrop drop in RandomNpcDropRule.ExtraDrops(netId, type, false, planning: true))
				{
					extras++;
					if (ExtraStackProblem(drop.Item, drop.StackMin, drop.StackMax) is string problem) Fail($"{NpcName(netId)} extra: {problem}");
				}
			}
			if (SeedSystem.WorldReady)
			{
				DropPermutation.WorldPlan plan = DropPermutation.Current;
				for (int p = 0; p < plan.Places.Count; p++)
				{
					DropPermutation.Place place = plan.Places[p];
					if (!plan.Surprised[p] && !place.Synthetic) continue;
					DropRateInfo entry = plan.ByUnit[place.Owner][place.Index].Entry;
					surprises++;
					if (ExtraStackProblem(entry.itemId, entry.stackMin, entry.stackMax) is string problem) Fail($"{NpcName(place.Owner)} surprise: {problem}");
				}
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"ExtraStacksSmall: {extras} extra slots, {surprises} surprises and synthetic entries checked");
			Check.All(failures, "Extra stacks");
		}

		// No random extra drop nor surprise gives a hard gate or a single use opener or summon (Temple Key, Lihzahrd Power
		// Cell, biome keys, boss summons...) above the tier of its source, but a jackpot (an extra aimed beyond its jitter):
		// every enemy's and boss's extra slots, this world's surprises and synthetic entries
		private static void GatesNotOnLowerOwners()
		{
			if (!LootConfig.Instance.RandomizeMobDrops) Check.Skip("enemy drops are not randomized");
			if (!ItemPool.Ready || !SeedSystem.WorldReady) Check.Skip("no world");
			if (!RandomNpcDropRule.GateAbove(ItemID.TempleKey, (int)Core.Tier.PostMechBoss) || RandomNpcDropRule.GateAbove(ItemID.TempleKey, Tiers.Max))
			{
				Check.That(false, "GateAbove is wrong for the Temple Key");
			}
			List<string> failures = new();
			void Fail(string message)
			{
				if (failures.Count < 30) failures.Add(message);
			}
			int checkedDrops = 0;
			foreach (int netId in ContentSamples.NpcsByNetId.Keys.OrderBy(n => n))
			{
				int type = BossParts.Owner(ContentSamples.NpcsByNetId[netId].type);
				int sourceTier = RandomNpcDropRule.SourceTier(type, true);
				foreach (PlannedDrop drop in RandomNpcDropRule.ExtraDrops(netId, type, false, planning: true))
				{
					checkedDrops++;
					bool jackpot = ItemPool.TierOf(drop.Item) > sourceTier + 1;
					if (RandomNpcDropRule.GateAbove(drop.Item, sourceTier) && !jackpot)
					{
						Fail($"{NpcName(netId)} (tier {sourceTier}) extra: {Lang.GetItemNameValue(drop.Item)} (true tier {ItemPool.TrueTierOf(drop.Item)})");
					}
				}
			}
			DropPermutation.WorldPlan plan = DropPermutation.Current;
			for (int p = 0; p < plan.Places.Count; p++)
			{
				DropPermutation.Place place = plan.Places[p];
				if (!plan.Surprised[p] && !place.Synthetic) continue;
				int item = plan.ByUnit[place.Owner][place.Index].Entry.itemId;
				checkedDrops++;
				if (RandomNpcDropRule.GateAbove(item, place.Tier)) Fail($"{NpcName(place.Owner)} (tier {place.Tier}) surprise: {Lang.GetItemNameValue(item)} (true tier {ItemPool.TrueTierOf(item)})");
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"GatesNotOnLowerOwners: {checkedDrops} random entries checked");
			Check.All(failures, "Gates on lower owners");
		}

		// Strong enemies that come with hardmode (mimics, the Wyvern, Giant Tortoises, Moss Hornets: tier 6 to 8 by their stats)
		// get places of their spawn tier (DropPermutation.PlaceTierOf), so their permuted table drops from hardmode on; an
		// event enemy weaker than its event gets the event's tier (Martian Madness after Golem), its extra slots too
		private static void StrongEnemiesDropEarly()
		{
			if (!ItemPool.Ready || !SeedSystem.WorldReady) Check.Skip("no world");
			List<string> failures = new();
			int hardmode = (int)Core.Tier.Hardmode;
			foreach (int martian in new[] { NPCID.MartianEngineer, NPCID.RayGunner, NPCID.BrainScrambler, NPCID.GrayGrunt })
			{
				int tier = DropPermutation.PlaceTierOf(martian);
				if (tier != (int)Core.Tier.PostGolem) failures.Add($"{NpcName(martian)}: place tier {tier}, {(int)Core.Tier.PostGolem} expected");
				if (RandomNpcDropRule.SourceTier(martian, true) < (int)Core.Tier.PostGolem) failures.Add($"{NpcName(martian)}: extras rolled at tier {RandomNpcDropRule.SourceTier(martian, true)}");
			}
			if (!DropPermutation.InStyle(NPCID.Mimic))
			{
				Check.All(failures, "Strong enemies");
				return;
			}
			DropPermutation.WorldPlan plan = DropPermutation.Current;
			int tested = 0;
			foreach (int npc in new[] { NPCID.Mimic, NPCID.IceMimic, NPCID.WyvernHead, NPCID.GiantTortoise, NPCID.MossHornet, NPCID.RuneWizard })
			{
				if (!DropPermutation.IsPermutable(npc)) continue;
				int unit = DropPermutation.UnitOf(npc);
				int spawn = NpcTiers.SpawnTier(npc);
				int placeTier = DropPermutation.PlaceTierOf(unit);
				if (spawn >= 0 && placeTier > spawn) failures.Add($"{NpcName(npc)}: place tier {placeTier} above its spawn tier {spawn}");
				foreach (DropPermutation.Place place in plan.Places.Where(p => p.Owner == unit && p.Tier != placeTier)) failures.Add($"{NpcName(npc)}: a place at tier {place.Tier}");
				List<DropPermutation.Placed> rolled = DropPermutation.For(npc).Where(p => p.Rolled && p.Entry.itemId > ItemID.None && !DropHelper.KeepOriginal(p.Entry.itemId)).ToList();
				if (rolled.Count == 0) continue;
				tested++;
				int early = rolled.Count(p => ItemPool.UnlockTierOf(p.Entry.itemId) <= hardmode && Findability.ConditionTier(p.Entry.conditions, placeTier) <= hardmode);
				if (early == 0) failures.Add($"{NpcName(npc)} (stats tier {NpcTiers.BaseTier(npc)}): none of its {rolled.Count} permuted entries drops at tier {hardmode}");
			}
			if (tested == 0) Check.Skip("none of the strong hardmode enemies has a permuted table");
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"StrongEnemiesDropEarly: {tested} strong enemies checked");
			Check.All(failures, "Strong enemies");
		}

		private static void SnapshotsCaptured()
		{
			Check.That(LootSnapshot.Npcs.Count > 300, $"only {LootSnapshot.Npcs.Count} enemy drop tables captured");
			Check.That(LootSnapshot.Items.Count > 30, $"only {LootSnapshot.Items.Count} bag tables captured");
			Check.That(LootSnapshot.ForNpc(NPCID.Zombie).Count > 0, "Zombie has no captured drops");
			Check.That(LootSnapshot.ForItem(ItemID.EyeOfCthulhuBossBag).Count > 0, "Eye of Cthulhu bag has no captured content");
		}

		private static void ValidatePlan(string source, List<PlannedDrop> plan, List<string> failures)
		{
			foreach (PlannedDrop drop in plan)
			{
				if (drop.Item <= ItemID.None || drop.Item >= ItemLoader.ItemCount) failures.Add($"{source}: invalid item {drop.Item}");
				else if (drop.Chance <= 0 || drop.Chance > 1.0001f) failures.Add($"{source}: chance {drop.Chance} for {Lang.GetItemNameValue(drop.Item)}");
				else if (drop.StackMin < 1 || drop.StackMax < drop.StackMin) failures.Add($"{source}: stack {drop.StackMin}-{drop.StackMax}");
				else if (drop.Extra && !ItemPool.IsRandomizable(drop.Item) && drop.Item != ItemID.Gel) failures.Add($"{source}: extra drop {Lang.GetItemNameValue(drop.Item)} is protected/excluded");
			}
		}

		private static void MobPlansValid()
		{
			List<string> failures = new();
			foreach (int netId in LootSnapshot.Npcs.Keys)
			{
				ValidatePlan(NpcName(netId), RandomNpcDropRule.Plan(netId, Main.expertMode), failures);
			}
			Check.All(failures, "Invalid enemy drop tables");
		}

		// Long drop tables: with explicit ranges (2 to 7 against the former 1 to 4, abundance 3), the average slot count of
		// the common enemies follows the skewed roll (about 1.85 times more), and the plans really use that count
		private static void DropTableLength()
		{
			float factor = (float)Math.Sqrt(3);
			List<int> enemies = new();
			for (int type = 1; type < NPCID.Count; type++)
			{
				if (NPCID.Sets.CountsAsCritter[type] || NpcTiers.IsBoss(type)) continue;
				if (RandomNpcDropRule.CanHaveExtraDrops(type, false, LootSnapshot.ForNpc(type))) enemies.Add(type);
			}
			if (enemies.Count < 50) Check.Skip($"only {enemies.Count} enemies can get extra drops with these settings");
			double before = enemies.Average(t => RandomNpcDropRule.SlotCount(DetRandom.NpcKey(t), false, false, 1, 4, DropTableStyle.VanillaLike, factor));
			double after = enemies.Average(t => RandomNpcDropRule.SlotCount(DetRandom.NpcKey(t), false, false, 2, 7, DropTableStyle.VanillaLike, factor));
			double skew = RandomNpcDropRule.SlotSkew(false, DropTableStyle.VanillaLike);
			double expected = RandomNpcDropRule.ExpectedSlots(2, 7, skew) * factor;
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Drop tables: {before:0.00} slots per enemy with 1-4, {after:0.00} with 2-7 (expected {expected:0.00}), {enemies.Count} enemies");
			List<string> failures = new();
			if (Math.Abs(after - expected) > expected * 0.15) failures.Add($"{after:0.00} slots per enemy with 2 to 7, expected about {expected:0.00}");
			if (after < before * 1.6) failures.Add($"2 to 7 slots give {after / before:0.00} times the slots of 1 to 4, expected about 1.85");

			// The plans use that count: the extra drops are the slots plus the small frequent drops
			LootConfig cfg = LootConfig.Instance;
			if (cfg.RandomizeMobDrops && cfg.DropTableStyle == DropTableStyle.Permutation && GeneralConfig.Instance.RandomizerMode != RandomizerMode.Shuffle)
			{
				// Permutation: PermutationExtraSlots slots whatever the abundance, at vanilla-like rates (never above
				// DropRateMultiplier / 1), no small frequent drops, and no materials, ores or bars when their weights are 0 (not
				// in Chaos: ItemPool.Roll ignores the weights there). enemies only holds those that CanHaveExtraDrops (without
				// vanilla drops: only with EnemiesWithoutDropsCanDrop)
				int expectedSlots = Math.Clamp(cfg.PermutationExtraSlots, 0, 3);
				bool weighted = GeneralConfig.Instance.RandomizerMode != RandomizerMode.Chaos;
				foreach (int type in enemies.Where(DropPermutation.IsPermutable).Take(40))
				{
					List<PlannedDrop> extras = RandomNpcDropRule.ExtraDrops(type, type, Main.expertMode, 3f);
					if (extras.Count != expectedSlots || RandomNpcDropRule.ExtraDrops(type, type, Main.expertMode, 1f).Count != expectedSlots)
					{
						failures.Add($"{NpcName(type)}: {extras.Count} random drops planned in the permutation style, {expectedSlots} expected");
						continue;
					}
					foreach (PlannedDrop extra in extras)
					{
						if (extra.Chance <= 0f || extra.Chance > Math.Min(1f, cfg.DropRateMultiplier) + 1e-4f) failures.Add($"{NpcName(type)}: extra drop at {extra.Chance:P1}, at most {Math.Min(1f, cfg.DropRateMultiplier):P0}");
						ItemCategory category = ItemPool.IsValid(extra.Item) ? ItemPool.Info[extra.Item].Category : ItemCategory.Misc;
						if (weighted && cfg.PermutationExtraWeights.Get(category) == 0 && category is ItemCategory.Material or ItemCategory.OreBar)
						{
							failures.Add($"{NpcName(type)}: {Lang.GetItemNameValue(extra.Item)} ({category}) as an extra drop, its weight is 0");
						}
					}
				}
			}
			else if (cfg.RandomizeMobDrops && cfg.DropTableStyle != DropTableStyle.MirrorOriginal && GeneralConfig.Instance.RandomizerMode != RandomizerMode.Shuffle)
			{
				int commons = 0;
				for (float total = cfg.CommonDropChance / 100f * 3f; total > 0.0001f && commons < 20; total -= 1f) commons++;
				foreach (int type in enemies.Take(40))
				{
					int slots = RandomNpcDropRule.SlotCount(DetRandom.NpcKey(type), false, false, cfg.DropSlotsMin, cfg.DropSlotsMax, cfg.DropTableStyle, factor);
					int planned = RandomNpcDropRule.ExtraDrops(type, type, Main.expertMode, 3f).Count - commons;
					if (planned != slots) failures.Add($"{NpcName(type)}: {planned} random slots planned, {slots} expected");
				}
			}
			Check.All(failures, "Drop table length");
		}

		// Explicit abundance values (not the config): more random items per kill, chances at most 100 %, guaranteed drops unchanged
		private static void LootAbundanceScales()
		{
			LootConfig cfg = LootConfig.Instance;
			if (!cfg.RandomizeMobDrops || cfg.DropTableStyle == DropTableStyle.MirrorOriginal) Check.Skip("random enemy drops are off");
			if (GeneralConfig.Instance.RandomizerMode == RandomizerMode.Shuffle) Check.Skip("shuffle mode has no extra drops");
			if (cfg.DropTableStyle == DropTableStyle.Permutation) Check.Skip("the permutation style's extra drops are not scaled by abundance");
			int[] enemies = { NPCID.BlueSlime, NPCID.Zombie, NPCID.DemonEye, NPCID.CaveBat, NPCID.Skeleton, NPCID.Vulture, NPCID.Antlion, NPCID.Harpy, NPCID.GoblinScout, NPCID.Piranha };
			float[] factors = { 1f, 2f, 4f };
			double[] expected = new double[factors.Length];
			List<string> failures = new();
			foreach (int npc in enemies)
			{
				string guaranteedAt1 = null;
				for (int f = 0; f < factors.Length; f++)
				{
					// The final plan: what abundance scales (the chances of locked entries move with progression)
					List<PlannedDrop> plan = RandomNpcDropRule.Plan(npc, Main.expertMode, lucky: true, abundance: factors[f]);
					foreach (PlannedDrop drop in plan)
					{
						if (drop.Chance > 1.0001f) failures.Add($"{NpcName(npc)} x{factors[f]}: {Lang.GetItemNameValue(drop.Item)} at {drop.Chance * 100:0} %");
						if (drop.Extra) expected[f] += drop.Chance;
					}
					string guaranteed = string.Join(";", plan.Where(d => d.Guaranteed).Select(d => $"{d.Item}:{d.Chance:0.0000}"));
					if (f == 0) guaranteedAt1 = guaranteed;
					else if (guaranteed != guaranteedAt1) failures.Add($"{NpcName(npc)} x{factors[f]}: guaranteed drops changed");
				}
			}
			if (expected[0] <= 0) Check.Skip("these enemies have no random drops with the current settings");
			double ratio2 = expected[1] / expected[0];
			double ratio4 = expected[2] / expected[1];
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Abundance: {expected[0] / enemies.Length:0.00} / {expected[1] / enemies.Length:0.00} / {expected[2] / enemies.Length:0.00} random items per kill at x1 / x2 / x4");
			if (ratio2 < 1.4 || ratio2 > 2.6) failures.Add($"x2 gives {ratio2:0.00} times the random items of x1, expected about 2");
			if (ratio4 < 1.4 || ratio4 > 2.6) failures.Add($"x4 gives {ratio4:0.00} times the random items of x2, expected about 2");
			Check.All(failures, "Loot abundance");
		}

		private static void ProtectedDropsKept()
		{
			List<string> failures = new();
			foreach ((int netId, List<DropRateInfo> drops) in LootSnapshot.Npcs)
			{
				foreach (DropRateInfo entry in drops)
				{
					if (!DropHelper.KeepOriginal(entry.itemId)) continue;
					int replacement = DropHelper.Replacement(RollKey.MobDrop, DetRandom.NpcKey(netId), entry.itemId, 0, 0, false);
					if (replacement != entry.itemId) failures.Add($"{NpcName(netId)}: {Lang.GetItemNameValue(entry.itemId)} replaced");
				}
			}
			// Coins, bags and upgrades (the Pwnhammer and the Temple Key are randomized and guaranteed: ProtectionsRemoved)
			int[] mustKeep = { ItemID.CopperCoin, ItemID.EyeOfCthulhuBossBag, ItemID.LifeCrystal };
			foreach (int item in mustKeep)
			{
				if (!DropHelper.KeepOriginal(item)) failures.Add($"{Lang.GetItemNameValue(item)} is not kept");
			}
			Check.All(failures, "Progression drops changed");
		}

		private static void NoMinionFarms()
		{
			if (!LootConfig.Instance.RandomizeMobDrops) Check.Skip("enemy drops are not randomized");
			int[] noExtra = { NPCID.Creeper, NPCID.ServantofCthulhu, NPCID.TheHungry, NPCID.Probe, NPCID.Bee, NPCID.SkeletronHand, NPCID.Guide, NPCID.Merchant, NPCID.TorchGod };
			List<string> failures = new();
			foreach (int type in noExtra)
			{
				// The number of extra drops is random and may be 0: check the rule itself
				if (RandomNpcDropRule.CanHaveExtraDrops(type, NpcTiers.IsBoss(type), LootSnapshot.ForNpc(type))) failures.Add($"{NpcName(type)} can get extra drops");
			}
			Check.All(failures, "Minions or town NPCs get random drops");
		}

		// Old One's Army medals and mana, and banners must never be replaced
		private static void EventItemsKept()
		{
			List<string> failures = new();
			foreach (int item in new[] { ItemID.DefenderMedal, ItemID.DD2EnergyCrystal })
			{
				if (ItemPool.IsRandomizable(item)) failures.Add($"{Lang.GetItemNameValue(item)} is randomizable");
			}
			Check.All(failures, "Event items can be replaced");
			Check.Equal(NPCID.EaterofWorldsHead, BossParts.Owner(NPCID.EaterofWorldsTail), "owner of the Eater of Worlds tail");
			Check.That(NpcTiers.IsBoss(BossParts.Owner(NPCID.EaterofWorldsBody)), "Eater of Worlds segments count as the boss");
		}

		private static void BagPlansValid()
		{
			List<string> failures = new();
			foreach (int bag in LootSnapshot.Items.Keys)
			{
				ValidatePlan(Lang.GetItemNameValue(bag), BagPlan.Plan(bag), failures);
			}
			Check.All(failures, "Invalid bag contents");
		}

		private static void BagsKeepCoins()
		{
			List<string> failures = new();
			foreach (int bag in LootSnapshot.Items.Keys.Where(b => ItemID.Sets.BossBag[b]))
			{
				List<DropRateInfo> original = LootSnapshot.ForItem(bag);
				// Locked entries kept: one entry per original one
				List<PlannedDrop> plan = BagPlan.Plan(bag, includeLocked: true);
				// A permuted bag's table is in another order (BossPermutation): its coins are still all there, as they are
				if (BossPermutation.BagInStyle(bag))
				{
					foreach (int coin in original.Where(o => ItemID.Sets.CommonCoin[o.itemId]).Select(o => o.itemId).Distinct())
					{
						if (plan.Count(d => d.Item == coin) < original.Count(o => o.itemId == coin)) failures.Add($"{Lang.GetItemNameValue(bag)} loses its coins");
					}
					continue;
				}
				for (int i = 0; i < original.Count && i < plan.Count; i++)
				{
					if (ItemID.Sets.CommonCoin[original[i].itemId] && plan[i].Item != original[i].itemId) failures.Add($"{Lang.GetItemNameValue(bag)} loses its coins");
				}
			}
			Check.All(failures, "Boss bag coins changed");
		}
	}
}

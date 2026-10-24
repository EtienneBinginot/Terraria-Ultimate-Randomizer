using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.GameContent.Bestiary;
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
			TestRunner.Register("WorldDropsReported", WorldDropsReported);
			TestRunner.Register("BossRuntime", BossRuntime);
			TestRunner.Register("PermutationConservation", PermutationConservation);
			TestRunner.Register("PermutedConditions", PermutedConditions);
			TestRunner.Register("SeasonalDropsPermuted", SeasonalDropsPermuted);
			TestRunner.Register("EaterOfWorldsParts", EaterOfWorldsParts);
			TestRunner.Register("BossGroupsMerge", BossGroupsMerge);
		}

		// With the permutation, what the bestiary and Recipe Browser read shows the world's drops (WorldDropsReport). The
		// bestiary entry is compared with an expectation computed apart from the report: what the enemy drops now (its plan
		// without the entries progression locks) filtered to the entries our rules roll, each shown at least as many times as
		// planned; an entry progression still locks is not shown (unless the same item also drops now or from a vanilla
		// rule); and a vanilla drop of the enemy that moved away (not kept, not in its final plan) is not shown. Its silenced
		// vanilla rules report nothing, and the entry lists exactly the live report (the old elements removed). Shown for the
		// test when the option is off, then restored
		private static void WorldDropsReported()
		{
			if (Main.dedServ) Check.Skip("no bestiary on a server");
			if (!LootConfig.Instance.RandomizeMobDrops || LootConfig.Instance.DropTableStyle != DropTableStyle.Permutation) Check.Skip("the enemy drop style is not Permutation");
			// Protected in ItemDropBestiaryInfoElement
			System.Reflection.FieldInfo rateField = typeof(ItemDropBestiaryInfoElement).GetField("_droprateInfo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
			Check.That(rateField != null, "ItemDropBestiaryInfoElement._droprateInfo not found");
			// A rebuild spread over frames is finished first: the entries are read now
			WorldDropsReport.Complete();
			bool was = WorldDropsReport.Showing;
			List<string> failures = new();
			int checkedIds = 0, movedAway = 0, lockedChecked = 0;
			try
			{
				if (!was) WorldDropsReport.Rebuild(true);
				int[] samples = { NPCID.BlueSlime, NPCID.Zombie, NPCID.DemonEye, NPCID.Skeleton, NPCID.EyeofCthulhu, NPCID.QueenBee, NPCID.WallofFlesh };
				foreach (int netId in samples.Concat(ContentSamples.NpcsByNetId.Keys.OrderBy(k => k)).Distinct())
				{
					if (checkedIds >= 12) break;
					if (!WorldDropsReport.Shows(netId)) continue;
					string name = $"{NpcName(netId)} ({netId})";
					// Independent of WorldDropsReport: the plan, filtered here
					List<PlannedDrop> plan = RandomNpcDropRule.Plan(netId, Main.expertMode, lucky: true);
					List<PlannedDrop> withLocked = RandomNpcDropRule.Plan(netId, Main.expertMode, includeLocked: true);
					List<int> expected = withLocked.Where(d => !d.Locked && d.Item > ItemID.None && (d.Rolled || d.Extra || d.Guaranteed)).Select(d => d.Item).ToList();
					HashSet<int> vanillaItems = LootSnapshot.ForNpc(netId).Select(d => d.itemId).ToHashSet();
					List<int> locked = withLocked.Where(d => d.Locked && d.Item > ItemID.None).Select(d => d.Item)
						.Where(i => !expected.Contains(i) && !vanillaItems.Contains(i)).Distinct().ToList();
					if (expected.Count == 0 && locked.Count == 0) continue;
					// A net ID without a page of its own (the Hornet's size variants) is shown on none
					BestiaryEntry entry = WorldDropsReport.EntryOf(netId);
					if (entry == null) continue;
					checkedIds++;
					List<int> shown = entry.Info.OfType<ItemDropBestiaryInfoElement>().Select(e => ((DropRateInfo)rateField.GetValue(e)).itemId).ToList();
					lockedChecked += locked.Count;
					foreach (int item in locked.Where(shown.Contains).Take(3)) failures.Add($"{name}: {ItemName(item)} still locked by progression but shown in the bestiary");
					foreach (IGrouping<int, int> item in expected.GroupBy(i => i).Where(g => shown.Count(s => s == g.Key) < g.Count()).Take(3))
					{
						failures.Add($"{name}: {ItemName(item.Key)} planned {item.Count()} time(s), shown {shown.Count(s => s == item.Key)} in the bestiary");
					}
					// Vanilla drops of this enemy that went elsewhere (not kept, nowhere in its plan, entries left to their
					// rule included): not on its page
					HashSet<int> planned = plan.Select(d => d.Item).ToHashSet();
					List<int> gone = LootSnapshot.ForNpc(netId).Select(d => d.itemId).Where(i => i > ItemID.None && !DropHelper.KeepOriginal(i) && !planned.Contains(i)).Distinct().ToList();
					movedAway += gone.Count;
					foreach (int item in gone.Where(shown.Contains).Take(3)) failures.Add($"{name}: {ItemName(item)} moved away but still in the bestiary");
					// The silenced wrappers report nothing, the entry is the live report
					List<IItemDropRule> registered = Main.ItemDropsDB.GetRulesForNPCID(netId, false);
					foreach (PermutableRule wrapper in registered.SelectMany(r => BossPermutation.Wrappers(r)).Where(w => DropPermutation.Silences(w.Items)))
					{
						if (LootSnapshot.Capture(new List<IItemDropRule> { wrapper }).Count > 0) failures.Add($"{name}: a silenced vanilla rule still reports its drops");
					}
					int reported = LootSnapshot.Capture(registered).Count;
					if (shown.Count != reported) failures.Add($"{name}: the bestiary shows {shown.Count} drops for {reported} reported");
				}
			}
			finally
			{
				if (!was) WorldDropsReport.Rebuild(false);
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"World drops reported: {checkedIds} net IDs checked, {movedAway} moved-away vanilla drops checked, {lockedChecked} locked entries checked");
			if (checkedIds == 0) Check.Skip("no enemy follows the permutation");
			Check.All(failures, "World drops reported");
		}

		private static string Key((int netId, int index) k) => k.index >= BossPermutation.BagIndexOffset ? $"{NpcName(k.netId)} bag entry {k.index - BossPermutation.BagIndexOffset}" : $"{NpcName(k.netId)} entry {k.index}";

		// Every vanilla entry, of every enemy table, boss table and boss bag, is somewhere exactly once (by where its content
		// comes from): kept at its place (left to its rule, or a kept item), or moved to exactly one place. The Twins' mirrored
		// entries are Retinazer's, and a bag's carried entries are its boss's (BossBagMatchesBoss checks both)
		private static void PermutationConservation()
		{
			List<string> failures = new();
			void Fail(string message)
			{
				if (failures.Count < 25) failures.Add(message);
			}
			void Compare(string what, int seed, Dictionary<(int, int), int> found, HashSet<(int, int)> expected)
			{
				foreach ((int, int) key in expected)
				{
					int n = found.TryGetValue(key, out int c) ? c : 0;
					if (n != 1) Fail($"seed {seed}: {what}: the content of {Key(key)} is found {n} times");
				}
				foreach ((int, int) key in found.Keys.Where(k => !expected.Contains(k))) Fail($"seed {seed}: {what}: a content from {Key(key)}, no vanilla entry");
			}
			int checkedEntries = 0;
			foreach (int seed in Seeds())
			{
				BossPermutation.WorldPlan plan = Plan(seed);
				// Enemies: each table holds its vanilla entries (and its synthetic one) once, wherever they went
				DropPermutation.WorldPlan enemies = DropPermutation.Build(seed, 0f);
				Dictionary<(int, int), int> found = new();
				HashSet<(int, int)> expected = new();
				foreach ((int unit, DropPermutation.Placed[] table) in enemies.ByUnit)
				{
					for (int i = 0; i < table.Length; i++)
					{
						expected.Add((unit, i));
						found[(table[i].FromNetId, table[i].FromIndex)] = found.GetValueOrDefault((table[i].FromNetId, table[i].FromIndex)) + 1;
					}
				}
				checkedEntries += expected.Count;
				Compare("enemies", seed, found, expected);
				// Bosses
				found = new();
				expected = new();
				foreach ((int unit, DropPermutation.Placed[] table) in plan.ByUnit)
				{
					int mirror = BossPermutation.MirrorOf(unit);
					for (int i = 0; i < table.Length; i++)
					{
						if (mirror > 0 && table[i].Rolled && !DropHelper.KeepOriginal(table[i].Entry.itemId) && BossPermutation.Mirrors(unit, mirror, i)) continue;
						expected.Add((unit, i));
						found[(table[i].FromNetId, table[i].FromIndex)] = found.GetValueOrDefault((table[i].FromNetId, table[i].FromIndex)) + 1;
					}
				}
				checkedEntries += expected.Count;
				Compare("bosses", seed, found, expected);
				// Bags: their own entries (not travelling with a boss drop) once among all the bags
				found = new();
				expected = new();
				foreach ((int bag, DropPermutation.Placed[] table) in plan.ByBag)
				{
					int unit = BossPermutation.BagUnit(bag);
					int carried = plan.Carried.TryGetValue(bag, out List<int> c) ? c.Count : 0;
					bool[] matched = plan.Matched.TryGetValue(bag, out bool[] m) ? m : Array.Empty<bool>();
					List<DropRateInfo> entries = LootSnapshot.ForItem(bag);
					for (int j = 0; j < entries.Count; j++)
					{
						if (j >= matched.Length || !matched[j]) expected.Add((unit, BossPermutation.BagIndexOffset + j));
					}
					if (matched.Count(x => x) > carried) Fail($"seed {seed}: {ItemName(bag)}: {matched.Count(x => x)} entries travel with {carried} boss drops");
					for (int k = carried; k < table.Length; k++) found[(table[k].FromNetId, table[k].FromIndex)] = found.GetValueOrDefault((table[k].FromNetId, table[k].FromIndex)) + 1;
				}
				checkedEntries += expected.Count;
				Compare("bags", seed, found, expected);
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Permutation conservation: {checkedEntries} entries");
			Check.All(failures, "Permutation conservation");
		}

		private static bool HasType(List<IItemDropRuleCondition> conditions, IItemDropRuleCondition condition) =>
			conditions != null && conditions.Any(c => c != null && c.GetType() == condition.GetType());

		// Moved contents carry only conditions they can satisfy: a seasonal one only from their place (SeasonalDropsPermuted),
		// never the first-Plantera-kill one; a boss place keeps its own conditions (restrictive, Legacy "is a boss", the Twins'
		// MissingTwin, a fight condition) plus the item's own ones (the Demon Heart, the Portal Gun), and a content freed from
		// a fight or one-time condition has a capped chance; a bag-only content keeps its own bag conditions
		private static void PermutedConditions()
		{
			List<string> failures = new();
			void Fail(string message)
			{
				if (failures.Count < 25) failures.Add(message);
			}
			void ItemSelfOnItsItem(string where, DropRateInfo entry)
			{
				foreach (IItemDropRuleCondition c in entry.conditions ?? new List<IItemDropRuleCondition>())
				{
					if (c is Conditions.NotUsedDemonHeart && entry.itemId != ItemID.DemonHeart || c is Conditions.NoPortalGun && entry.itemId != ItemID.PortalGun)
					{
						Fail($"{where}: {ItemName(entry.itemId)} needs {c.GetType().Name}");
					}
					if (DropPermutation.ClassOf(c) == DropPermutation.ConditionClass.OneTime) Fail($"{where}: {ItemName(entry.itemId)} moved with {c.GetType().Name}");
				}
			}
			int moved = 0;
			foreach (int seed in Seeds())
			{
				BossPermutation.WorldPlan plan = Plan(seed);
				for (int p = 0; p < plan.Places.Count; p++)
				{
					DropPermutation.Place place = plan.Places[p];
					int s = plan.ContentOf[p];
					DropPermutation.Place source = plan.Places[s];
					if (BossPermutation.IsBagOnly(place) || s == p) continue;
					moved++;
					DropPermutation.Placed placed = plan.ByUnit[place.Owner][place.Index];
					string where = $"seed {seed}: {Where(place)}";
					ItemSelfOnItsItem(where, placed.Entry);
					foreach (IItemDropRuleCondition c in placed.Entry.conditions ?? new List<IItemDropRuleCondition>())
					{
						DropPermutation.ConditionClass kind = DropPermutation.ClassOf(c);
						if (kind == DropPermutation.ConditionClass.ItemSelf ? !HasType(source.Entry.conditions, c) : !HasType(place.Entry.conditions, c))
						{
							Fail($"{where}: {ItemName(placed.Entry.itemId)} needs {c.GetType().Name}, from neither the place nor the item");
						}
						if (c is Conditions.MissingTwin && ContentSamples.NpcsByNetId[place.Owner].type is not (NPCID.Retinazer or NPCID.Spazmatism)) Fail($"{where}: MissingTwin outside the Twins");
					}
					if (BossPermutation.FreedRate(source, place, seed, out float cap) && placed.Entry.dropRate > cap + 1e-6f)
					{
						Fail($"{where}: {ItemName(placed.Entry.itemId)} freed from its condition drops at {placed.Entry.dropRate:0.###}, more than {cap:0.###}");
					}
				}
				Dictionary<(int, int), int> placeOf = new();
				for (int p = 0; p < plan.Places.Count; p++) placeOf[(plan.Places[p].Owner, plan.Places[p].Index)] = p;
				foreach ((int bag, DropPermutation.Placed[] table) in plan.ByBag)
				{
					foreach (DropPermutation.Placed placed in table.Where(x => x.Permuted))
					{
						string where = $"seed {seed}: {ItemName(bag)}";
						ItemSelfOnItsItem(where, placed.Entry);
						if (placed.Entry.conditions != null && placed.Entry.conditions.Any(c => DropPermutation.ClassOf(c) == DropPermutation.ConditionClass.Fight || c is Conditions.MissingTwin or Conditions.LegacyHack_IsABoss))
						{
							Fail($"{where}: {ItemName(placed.Entry.itemId)} keeps a boss fight condition in a bag");
						}
						if (placed.FromIndex < BossPermutation.BagIndexOffset || !placeOf.TryGetValue((placed.FromNetId, placed.FromIndex), out int q)) continue;
						IEnumerable<string> Types(List<IItemDropRuleCondition> list) => (list ?? new List<IItemDropRuleCondition>()).Select(c => c?.GetType().FullName ?? "");
						if (!Types(placed.Entry.conditions).SequenceEqual(Types(plan.BagVersion[q].conditions))) Fail($"{where}: {ItemName(placed.Entry.itemId)} does not keep its own bag conditions");
					}
				}
			}
			if (moved == 0) Check.Skip("no moved boss content");
			Check.All(failures, "Permuted conditions");
		}

		// The Eater of Worlds' body and tail segments use the head's table, floor and redistribution (the last segment killed
		// drops the loot, whichever it is)
		private static void EaterOfWorldsParts()
		{
			if (!LootConfig.Instance.RandomizeMobDrops) Check.Skip("enemy drops are not randomized");
			if (BossPermutation.LeftVanilla(NPCID.EaterofWorldsHead)) Check.Skip("no Corruption in this world: the Eater of Worlds keeps its vanilla table");
			if (!BossPermutation.InStyle(NPCID.EaterofWorldsHead)) Check.Skip("bosses don't use the permutation style");
			int head = NPCID.EaterofWorldsHead;
			List<PlannedDrop> expected = RandomNpcDropRule.OriginalDrops(head, BossParts.Owner(head));
			if (expected.Count == 0) Check.Skip("the Eater of Worlds has no drop");
			// Independent of the cached plan and of UnitOf: the head's table of a fresh plan of this world
			Check.That(BossPermutation.Build(SeedSystem.WorldSeed).ByUnit.TryGetValue(head, out DropPermutation.Placed[] fresh), "the head has no table of its own in a fresh plan");
			Check.Equal(fresh.Length, expected.Count, "head drop count against a fresh plan");
			for (int i = 0; i < fresh.Length; i++)
			{
				Check.That(fresh[i].Entry.itemId == expected[i].Item && fresh[i].Entry.dropRate == expected[i].Chance && fresh[i].Rolled == expected[i].Rolled,
					$"head entry {i}: {ItemName(expected[i].Item)}, a fresh plan has {ItemName(fresh[i].Entry.itemId)}");
			}
			foreach (int part in new[] { NPCID.EaterofWorldsBody, NPCID.EaterofWorldsTail })
			{
				Check.Equal(head, BossParts.Owner(part), $"owner of {NpcName(part)}");
				if (BossPermutation.UnitOf(part) != BossPermutation.UnitOf(head)) Check.Skip($"{NpcName(part)} has a table of its own");
				List<PlannedDrop> drops = RandomNpcDropRule.OriginalDrops(part, BossParts.Owner(part));
				Check.Equal(expected.Count, drops.Count, $"{NpcName(part)} drop count");
				for (int i = 0; i < drops.Count; i++)
				{
					Check.That(drops[i].Item == expected[i].Item && drops[i].Chance == expected[i].Chance && drops[i].LockGap == expected[i].LockGap,
						$"{NpcName(part)} entry {i}: {ItemName(drops[i].Item)} (lock {drops[i].LockGap}), the head has {ItemName(expected[i].Item)} (lock {expected[i].LockGap})");
				}
			}
		}

		// Pure: small consecutive tiers merge first, then a group still too small joins the one below (the lowest the one
		// above); the Moon Lord's loot stays with the Lunatic Cultist's
		private static void BossGroupsMerge()
		{
			string Show(List<List<int>> groups) => string.Join(" | ", groups.Select(g => string.Join(",", g)));
			void Expect((int unit, int tier)[] tables, string expected)
			{
				string actual = Show(BossPermutation.BossGroups(tables));
				Check.That(actual == expected, $"groups of {string.Join(" ", tables.Select(t => $"{t.unit}@{t.tier}"))}: {actual}, expected {expected}");
			}
			Expect(new[] { (1, 1), (2, 1), (3, 2), (4, 2), (5, 4), (6, 5), (7, 5), (8, 7), (9, 8) }, "1,2 | 3,4,5 | 6,7 | 8,9");
			Expect(new[] { (1, 0), (2, 1), (3, 1) }, "1,2,3");
			Expect(new[] { (1, 0), (2, 1), (3, 2) }, "1,2,3");
			Expect(new[] { (1, 0), (2, 0), (3, 1) }, "1,2,3");
			Expect(new[] { (1, 0), (2, 0), (3, 1), (4, 2), (5, 3) }, "1,2 | 3,4,5");
			Expect(new[] { (1, 3) }, "1");
		}

		private static int[] Seeds() => new[] { SeedSystem.WorldSeed, 12345, -98765 };

		// Seasonal entries (Halloween, Christmas, windy days, a named NPC...) are permuted, but only among themselves: in the
		// enemies' and the bosses' plans, every place of a seasonal class takes a content of the same class (the original
		// entry it came from has the same seasonal and restrictive condition types), keeps its own seasonal conditions, and
		// some seasonal contents really move. Read from the snapshots (the original entries), not from the plans' own fields
		private static void SeasonalDropsPermuted()
		{
			if (!ItemPool.Ready || !SeedSystem.WorldReady) Check.Skip("no world");
			List<string> failures = new();
			void Fail(string message)
			{
				if (failures.Count < 25) failures.Add(message);
			}
			IEnumerable<string> Seasonal(List<IItemDropRuleCondition> conditions) => (conditions ?? new List<IItemDropRuleCondition>())
				.Where(c => c != null && DropPermutation.ClassOf(c) == DropPermutation.ConditionClass.Seasonal).Select(c => c.GetType().FullName).OrderBy(n => n, StringComparer.Ordinal);
			DropRateInfo? Original(int owner, int index, bool bag)
			{
				List<DropRateInfo> entries = bag ? LootSnapshot.ForItem(BossPermutation.BagOf(owner)) : LootSnapshot.ForNpc(owner);
				int i = bag ? index - BossPermutation.BagIndexOffset : index;
				return i >= 0 && i < entries.Count ? entries[i] : null;
			}
			int seasonalPlaces = 0, moved = 0;
			foreach (int seed in Seeds())
			{
				DropPermutation.WorldPlan enemies = DropPermutation.Build(seed, surpriseChance: 0f, duplicateCopies: 0, materialCopies: 0);
				BossPermutation.WorldPlan bosses = Plan(seed);
				foreach ((List<DropPermutation.Place> places, int[] contentOf, string kind) in new[] { (enemies.Places, enemies.ContentOf, "enemy"), (bosses.Places, bosses.ContentOf, "boss") })
				{
					for (int p = 0; p < places.Count; p++)
					{
						DropPermutation.Place place = places[p];
						if (place.Synthetic) continue;
						bool bag = BossPermutation.IsBagOnly(place);
						DropRateInfo? own = Original(place.Owner, place.Index, bag);
						if (own == null) continue;
						bool seasonal = DropPermutation.IsSeasonal(own.Value.conditions);
						DropPermutation.Place source = places[contentOf[p]];
						DropRateInfo? from = source.Synthetic ? null : Original(source.Owner, source.Index, BossPermutation.IsBagOnly(source));
						string where = $"seed {seed}: {kind} {NpcName(place.Owner)} #{place.Index}";
						if (from == null)
						{
							if (seasonal) Fail($"{where}: a seasonal place got a synthetic content");
							continue;
						}
						if (!Seasonal(own.Value.conditions).SequenceEqual(Seasonal(from.Value.conditions)))
						{
							Fail($"{where}: {ItemName(from.Value.itemId)} [{string.Join("+", Seasonal(from.Value.conditions))}] moved to a place [{string.Join("+", Seasonal(own.Value.conditions))}]");
						}
						if (!seasonal) continue;
						seasonalPlaces++;
						if (contentOf[p] != p) moved++;
						// The place keeps its seasonal conditions (the enemies' entries are rolled with the place's conditions)
						if (kind == "enemy" && enemies.ByUnit.TryGetValue(place.Owner, out DropPermutation.Placed[] table) && place.Index < table.Length
							&& !Seasonal(table[place.Index].Entry.conditions).SequenceEqual(Seasonal(own.Value.conditions)))
						{
							Fail($"{where}: lost its seasonal condition");
						}
					}
				}
			}
			// The seasonal entries of the ordinary enemies' tables (whether their rule is wrapped or not): some must be places
			int seasonalEntries = DropPermutation.Units().Sum(u => LootSnapshot.ForNpc(u).Count(d => DropPermutation.IsSeasonal(d.conditions) && !DropHelper.KeepOriginal(d.itemId)));
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"SeasonalDropsPermuted: {seasonalEntries} seasonal enemy entries, {seasonalPlaces} seasonal places over {Seeds().Length} seeds, {moved} with a moved content");
			if (seasonalEntries == 0 && seasonalPlaces == 0) Check.Skip("no seasonal drop entry");
			Check.That(seasonalPlaces > 0, $"none of the {seasonalEntries} seasonal entries is permuted");
			Check.That(moved > 0, "no seasonal content moved");
			Check.All(failures, "Seasonal drops");
		}

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
						// A content freed from a fight or one-time condition is capped (BossPermutation.FreedRate)
						if (BossPermutation.FreedRate(plan.Places[plan.ContentOf[carried[k]]], place, seed, out float cap))
						{
							version = new DropRateInfo(version.itemId, version.stackMin, version.stackMax, Math.Min(version.dropRate, cap), version.conditions);
						}
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
					List<int> tiers = group.Select(BossPermutation.GroupTierOf).ToList();
					if (tiers.Min() <= previousMax) Fail($"seed {seed}: the groups overlap at tier {tiers.Min()}");
					previousMax = tiers.Max();
					if (group.Count < BossPermutation.MinGroupBosses && plan.BossGroups.Count > 1) Fail($"seed {seed}: a group of {group.Count} boss ({string.Join(", ", group.Select(NpcName))})");
				}
				// Queen Bee and Deerclops are grouped with Skeletron (never Skeletron and the Wall of Flesh alone)
				int skeletron = plan.GroupOfUnit.Keys.FirstOrDefault(u => BossParts.Owner(u) == NPCID.SkeletronHead);
				foreach (int unit in plan.GroupOfUnit.Keys.Where(u => BossParts.Owner(u) is NPCID.QueenBee or NPCID.Deerclops))
				{
					if (skeletron != 0 && plan.GroupOfUnit[unit] != plan.GroupOfUnit[skeletron]) Fail($"seed {seed}: {NpcName(unit)} is not in Skeletron's group");
				}
				// With enough bosses from the Lunatic Cultist on, the Moon Lord's contents never land on an earlier boss
				List<int> late = plan.GroupOfUnit.Keys.Where(u => BossPermutation.TierOf(u) >= (int)Tier.PostCultist).ToList();
				if (late.Count >= BossPermutation.MinGroupBosses)
				{
					for (int p = 0; p < plan.Places.Count; p++)
					{
						DropPermutation.Place source = plan.Places[plan.ContentOf[p]];
						if (source.Tier >= (int)Tier.PostMoonLord && plan.Places[p].Tier < (int)Tier.PostCultist) Fail($"seed {seed}: {Where(plan.Places[p])} (tier {plan.Places[p].Tier}) gets the Moon Lord's {ItemName(source.Entry.itemId)}");
					}
					foreach (List<int> group in plan.BossGroups.Where(g => g.Any(late.Contains)))
					{
						if (group.Any(u => !late.Contains(u))) Fail($"seed {seed}: the late bosses share a group with {string.Join(", ", group.Where(u => !late.Contains(u)).Select(NpcName))}");
					}
				}
			}
			if (logged != null)
			{
				string groups = string.Join(" | ", logged.BossGroups.Select(g => string.Join(", ", g.Select(u => $"{NpcName(u)} {BossPermutation.GroupTierOf(u)}"))));
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

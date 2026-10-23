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
		}

		// Seeds of the permutation tests: the world's and two fixed ones (DropPermutation.Build is pure in the seed)
		private static int[] PermutationSeeds() => new[] { SeedSystem.WorldSeed, 12345, -98765 };

		private static DropPermutation.WorldPlan PermutationPlan(int seed)
		{
			if (!LootConfig.Instance.RandomizeMobDrops) Check.Skip("enemy drops are not randomized");
			DropPermutation.WorldPlan plan = DropPermutation.Build(seed);
			if (plan.Places.Count < 30) Check.Skip($"only {plan.Places.Count} permutable drop entries");
			return plan;
		}

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
					List<string> after = group.Select(i => ContentKey(plan.ByType[plan.Places[i].Owner][plan.Places[i].Index].Entry)).OrderBy(k => k, StringComparer.Ordinal).ToList();
					if (!before.SequenceEqual(after) && failures.Count < 10) failures.Add($"seed {seed}: a group of {group.Count} places (tier {plan.Places[group[0]].Tier}, '{plan.Places[group[0]].Access}') changed its contents");
				}
				if (plan.Groups.Sum(g => g.Count) != plan.Places.Count) failures.Add($"seed {seed}: {plan.Groups.Sum(g => g.Count)} places in groups, {plan.Places.Count} places");
				HashSet<(int, int)> given = new();
				foreach (DropPermutation.Place place in plan.Places)
				{
					DropPermutation.Placed placed = plan.ByType[place.Owner][place.Index];
					if (!placed.Permuted || !placed.Rolled) failures.Add($"seed {seed}: {NpcName(place.Owner)} entry {place.Index} not permuted");
					if (!given.Add((placed.FromType, placed.FromIndex))) failures.Add($"seed {seed}: the content of {NpcName(placed.FromType)} entry {placed.FromIndex} given twice");
				}
				foreach ((int type, DropPermutation.Placed[] table) in plan.ByType)
				{
					if (table.Length != DropPermutation.SourceOf(type).entries.Count) failures.Add($"seed {seed}: {NpcName(type)} has {table.Length} entries, {DropPermutation.SourceOf(type).entries.Count} originally");
				}
			}
			// The live plan, through what the kills and the codex read
			if (SeedSystem.WorldReady)
			{
				DropPermutation.WorldPlan current = DropPermutation.Current;
				HashSet<int> planned = new();
				foreach (int type in DropPermutation.PermutableTypes())
				{
					List<PlannedDrop> drops = RandomNpcDropRule.OriginalDrops(type, type, DropTableStyle.Permutation);
					planned.UnionWith(drops.Select(d => d.Item));
					if (drops.Count != DropPermutation.SourceOf(type).entries.Count) failures.Add($"{NpcName(type)}: {drops.Count} planned original entries, {DropPermutation.SourceOf(type).entries.Count} originally");
				}
				for (int p = 0; p < current.Places.Count; p++)
				{
					DropPermutation.Place place = current.Places[p];
					if (!planned.Contains(place.Entry.itemId) && failures.Count < 20) failures.Add($"{Lang.GetItemNameValue(place.Entry.itemId)} (from {NpcName(place.Owner)}) is in no enemy's drops");
					List<PlannedDrop> drops = RandomNpcDropRule.OriginalDrops(place.Owner, place.Owner, DropTableStyle.Permutation);
					DropPermutation.Place source = current.Places[current.ContentOf[p]];
					if (place.Index >= drops.Count) continue;
					PlannedDrop drop = drops[place.Index];
					if ((drop.Item != source.Entry.itemId || drop.Conditions != place.Entry.conditions || !drop.Rolled) && failures.Count < 20)
					{
						failures.Add($"{NpcName(place.Owner)} entry {place.Index}: {Lang.GetItemNameValue(drop.Item)} planned, {Lang.GetItemNameValue(source.Entry.itemId)} expected with the place's conditions");
					}
				}
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Drop permutation: {places} places in {groups} groups");
			Check.All(failures, "Drop permutation");
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
						if (place.Tier != NpcTiers.BaseTier(place.Owner)) failures.Add($"seed {seed}: {NpcName(place.Owner)} placed at tier {place.Tier}, base tier {NpcTiers.BaseTier(place.Owner)}");
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
		// conditions; ordinary conditions (hardmode, biome...) don't make a class of their own
		private static void DropPermutationAccess()
		{
			List<string> failures = new();
			string Access(params IItemDropRuleCondition[] conditions) => DropPermutation.AccessOf(conditions.ToList());
			if (Access(new Conditions.IsExpert(), new Conditions.IsHardmode()) != "IsExpert") failures.Add($"expert + hardmode is '{Access(new Conditions.IsExpert(), new Conditions.IsHardmode())}'");
			if (Access(new Conditions.IsHardmode(), new Conditions.NotFromStatue()) != "") failures.Add("hardmode + not from a statue is restricted");
			if (Access(new Conditions.NotExpert(), new Conditions.IsMasterMode()) != "IsMasterMode+NotExpert") failures.Add($"not expert + master is '{Access(new Conditions.NotExpert(), new Conditions.IsMasterMode())}'");
			if (Access() != "") failures.Add("no condition is restricted");
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
					if (drop.Extra || drop.Original <= ItemID.None || DropHelper.KeepOriginal(drop.Original) || drop.Item == drop.Original) continue;
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
					if (gap > 0)
					{
						gaps[Math.Min(gap, Tiers.Max)]++;
						if (gap != tier - limit) failures.Add($"{Lang.GetItemNameValue(item)}: gap {gap}, true tier {tier}, limit {limit}");
					}
					else if (tier > limit) failures.Add($"{Lang.GetItemNameValue(item)} (true tier {tier}) above the limit {limit} without a jackpot");
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
		// gets nothing from the extra-drop rule; the flag does not stick to the next NPC of the slot. Worm segments and bosses
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
				ItemDropAttemptResultState state = rule.TryDroppingItem(info).State;
				if (state != ItemDropAttemptResultState.DidNotRunCode) failures.Add($"the child's extra-drop rule ran ({state})");
				if (slots.Count > 0) failures.Add($"the child dropped {slots.Count} items");
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
		private static void CrittersDrop()
		{
			LootConfig cfg = LootConfig.Instance;
			if (!cfg.RandomizeMobDrops || cfg.DropTableStyle == DropTableStyle.MirrorOriginal || !cfg.CrittersCanDrop) Check.Skip("critters get no random drops");
			if (GeneralConfig.Instance.RandomizerMode == RandomizerMode.Shuffle) Check.Skip("shuffle mode has no extra drops");
			if (cfg.DropTableStyle == DropTableStyle.Permutation) Check.Skip("the permutation style has no small frequent drop");
			float first = Math.Min(1f, cfg.CommonDropChance / 100f * 3f * cfg.CritterDropFactor);
			if (first <= 0.0001f) Check.Skip("critters get no small frequent drop with these settings");
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
					if (gap != ItemPool.TrueTierOf(item) - limit) failures.Add($"{Lang.GetItemNameValue(item)}: gap {gap}, true tier {ItemPool.TrueTierOf(item)}, limit {limit}");
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
		// useful, and the others are not marked. The spawned items are removed
		private static void JackpotDropsMarked()
		{
			if (Main.netMode != NetmodeID.SinglePlayer) Check.Skip("single player only (drops spawn on the server)");
			LootConfig cfg = LootConfig.Instance;
			if (!cfg.RandomizeMobDrops || cfg.DropTableStyle == DropTableStyle.MirrorOriginal || GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent) Check.Skip("no random extra drops");
			if (!ProgressionConfig.Instance.TierGating) Check.Skip("no progression limit");
			int limit = ItemPool.GatingLimit(0);
			if (limit >= Tiers.Max) Check.Skip("the limit is already the last tier");
			NPC npc = new();
			npc.SetDefaults(NPCID.Zombie);
			npc.Center = Main.LocalPlayer.Center;
			npc.life = 0;
			List<int> slots = new();
			List<string> failures = new();
			int marked = 0;
			try
			{
				ItemPool.WildChanceOverride = 100f;
				CodexSystem.SpawnLog = slots;
				RandomNpcDropRule rule = new(NPCID.Zombie);
				for (int i = 0; i < 40; i++)
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
					if (gap != ItemPool.TrueTierOf(item.type) - limit) failures.Add($"{Lang.GetItemNameValue(item.type)}: gap {gap}, true tier {ItemPool.TrueTierOf(item.type)}, limit {limit}");
					if (CodexSystem.CountsAsDrop(item)) failures.Add($"{Lang.GetItemNameValue(item.type)} would be recorded as the zombie's drop");
				}
			}
			finally
			{
				CodexSystem.SpawnLog = null;
				ItemPool.WildChanceOverride = null;
				ItemPool.ClearJackpot();
				foreach (int slot in slots.Distinct())
				{
					if (slot < 0 || slot >= Main.maxItems) continue;
					Main.item[slot].TurnToAir();
					Main.item[slot].active = false;
				}
			}
			Check.That(marked > 0, $"no marked jackpot in {slots.Count} drops of 40 kills");
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
				// Permutation: one extra slot (none at 0 %), no small frequent drops
				int expectedSlots = cfg.PermutationExtraChance > 0 ? 1 : 0;
				foreach (int type in enemies.Where(DropPermutation.IsPermutable).Take(40))
				{
					List<PlannedDrop> extras = RandomNpcDropRule.ExtraDrops(type, type, Main.expertMode, 3f);
					if (extras.Count != expectedSlots) failures.Add($"{NpcName(type)}: {extras.Count} random drops planned in the permutation style, {expectedSlots} expected");
					else if (extras.Count == 1 && Math.Abs(extras[0].Chance - Math.Min(1f, cfg.PermutationExtraChance / 100f)) > 1e-4f) failures.Add($"{NpcName(type)}: extra drop at {extras[0].Chance:P1}, {cfg.PermutationExtraChance} % expected");
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
			if (cfg.DropTableStyle == DropTableStyle.Permutation) Check.Skip("the permutation style has one extra drop of fixed chance, not scaled by abundance");
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
			int[] mustKeep = { ItemID.Pwnhammer, ItemID.TempleKey, ItemID.CopperCoin, ItemID.EyeOfCthulhuBossBag };
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
				for (int i = 0; i < original.Count && i < plan.Count; i++)
				{
					if (ItemID.Sets.CommonCoin[original[i].itemId] && plan[i].Item != original[i].itemId) failures.Add($"{Lang.GetItemNameValue(bag)} loses its coins");
				}
			}
			Check.All(failures, "Boss bag coins changed");
		}
	}
}

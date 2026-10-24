using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	// Bag rules keep running; their items are swapped when they spawn (LootSubstitutionItem). We remember the original
	// content, for the journal, the tiers and the shuffle. The complete rules of boss bags are wrapped (PermutableRule, never
	// changing the shared rule itself), silent when the bag follows the boss permutation (BossPermutation), whose plan
	// BossBagRule rolls instead
	public class BagLootItem : GlobalItem
	{
		public override void ModifyItemLoot(Item item, ItemLoot itemLoot)
		{
			List<IItemDropRule> rules = itemLoot.Get(false).Where(r => r != null).Distinct().ToList();
			if (rules.Count == 0) return;
			bool bossBag = item.type < ItemID.Sets.BossBag.Length && ItemID.Sets.BossBag[item.type];
			HashSet<int> running = bossBag ? RunningOnBoss(item.type) : null;
			List<DropRateInfo> drops = new();
			List<int> ruleOf = new();
			List<List<DropRateInfo>> parts = rules.Select(rule => LootSnapshot.Capture(new List<IItemDropRule> { rule })).ToList();
			// Boss bags only, vanilla or modded (their vanilla rule types); a rule holding an item its boss's own rules still
			// drop as vanilla (Deerclops' weapons) keeps running too, so that normal and expert mode give the same
			bool[] wraps = rules.Select((rule, r) => bossBag && DropPermutation.Permutable(rule, parts[r], item.type, true) && !parts[r].Any(d => running.Contains(d.itemId))).ToArray();
			// Another mod's bag joins the permutation whole or not at all: its own rule classes may hand out what the wrapped
			// ones would no longer give (a half-permuted bag could lose or double its loot)
			if (item.type >= ItemID.Count && wraps.Contains(false)) wraps = new bool[rules.Count];
			for (int r = 0; r < rules.Count; r++)
			{
				IItemDropRule rule = rules[r];
				List<DropRateInfo> part = parts[r];
				bool wrap = wraps[r];
				drops.AddRange(part);
				ruleOf.AddRange(Enumerable.Repeat(wrap ? r : -1, part.Count));
				if (!wrap) continue;
				while (itemLoot.Get(false).Contains(rule)) itemLoot.Remove(rule);
				itemLoot.Add(new PermutableRule(rule, 0, r, part.Select(d => d.itemId), item.type));
			}
			LootSnapshot.Items[item.type] = drops;
			if (!bossBag) return;
			LootSnapshot.ItemRules[item.type] = ruleOf.ToArray();
			itemLoot.Add(new BossBagRule(item.type));
		}

		// Load time (tML modifies the NPCs' loot before the items': the boss snapshots are complete): the items of the boss
		// tables dropping this bag (BossPermutation.WrapsLoot) whose entry is left to its vanilla rule (the item pool, hence
		// DropHelper.KeepOriginal, does not exist yet: kept items count too; at worst a bag rule holding one keeps running)
		private static HashSet<int> RunningOnBoss(int bag)
		{
			HashSet<int> running = new();
			foreach ((int netId, List<DropRateInfo> entries) in LootSnapshot.Npcs)
			{
				if (!ContentSamples.NpcsByNetId.TryGetValue(netId, out NPC sample) || !BossPermutation.WrapsLoot(sample.type) || !entries.Any(d => d.itemId == bag)) continue;
				int[] rules = LootSnapshot.RulesOf(netId);
				for (int i = 0; i < entries.Count; i++)
				{
					if ((i >= rules.Length || rules[i] < 0) && entries[i].itemId != bag) running.Add(entries[i].itemId);
				}
			}
			return running;
		}
	}

	public static class BagPlan
	{
		// A boss bag's floor is its own tier: its own loot is never locked
		public static int MinLimit(int bagType) => ItemID.Sets.BossBag[bagType] ? LootSnapshot.BagTier(bagType) : 0;

		// The content of a bag in this world, from its final plan (everything unlocked). lucky: that final plan as it is.
		// Otherwise what the bag gives now: the entries progression still locks give nothing and are left out (includeLocked:
		// kept, flagged). No redistribution: vanilla rules roll the bag (the boss permutation: PermutedPlan)
		public static List<PlannedDrop> Plan(int bagType, bool lucky = false, bool includeLocked = false)
		{
			if (BossPermutation.BagInStyle(bagType)) return PermutedPlan(bagType, lucky, includeLocked);
			List<PlannedDrop> drops = new();
			bool enabled = LootSubstitutionItem.BagEnabled(bagType, LootConfig.Instance);
			int bagTier = LootSnapshot.BagTier(bagType);
			int minLimit = MinLimit(bagType);
			foreach (DropRateInfo entry in LootSnapshot.ForItem(bagType))
			{
				int item = entry.itemId;
				int min = entry.stackMin;
				int max = entry.stackMax;
				int lockGap = 0;
				if (enabled)
				{
					item = DropHelper.Replacement(RollKey.BagDrop, DetRandom.ItemKey(bagType), entry.itemId, bagTier, minLimit, true);
					(min, max) = DropHelper.ScaledRange(entry.itemId, entry.stackMin, entry.stackMax, item);
					if (!lucky && !DropHelper.KeepOriginal(entry.itemId)) lockGap = DropHelper.LockGap(item, minLimit);
				}
				if (lockGap > 0 && !includeLocked) continue;
				drops.Add(new PlannedDrop { Item = item, StackMin = min, StackMax = max, Chance = entry.dropRate, Conditions = entry.conditions, Original = entry.itemId, LockGap = lockGap });
			}
			return drops;
		}

		// A boss bag following the boss permutation: its permuted table (the contents of its boss's not-expert places with
		// their bag chances, its own permuted bag-only places, its kept entries) and its boss's random extra drops (an expert
		// boss with a bag has none of its own). The chances of the locked entries go to the extras (Redistribute), as on a kill
		public static List<PlannedDrop> PermutedPlan(int bagType, bool lucky, bool includeLocked)
		{
			List<PlannedDrop> drops = new();
			int minLimit = MinLimit(bagType);
			foreach (DropPermutation.Placed placed in BossPermutation.ForBag(bagType))
			{
				DropRateInfo entry = placed.Entry;
				int lockGap = placed.Rolled && !lucky && !DropHelper.KeepOriginal(entry.itemId) ? DropHelper.LockGap(entry.itemId, minLimit) : 0;
				drops.Add(new PlannedDrop { Item = entry.itemId, StackMin = entry.stackMin, StackMax = entry.stackMax, Chance = entry.dropRate, Conditions = entry.conditions,
					Original = entry.itemId, LockGap = lockGap, Rolled = placed.Rolled });
			}
			int unit = BossPermutation.BagUnit(bagType);
			int type = BossParts.Owner(ContentSamples.NpcsByNetId.TryGetValue(unit, out NPC sample) ? sample.type : unit);
			List<PlannedDrop> extras = RandomNpcDropRule.ExtraDrops(unit, type, false, planning: lucky);
			if (lucky)
			{
				for (int i = 0; i < extras.Count; i++)
				{
					PlannedDrop extra = extras[i];
					extra.LockGap = 0;
					extras[i] = extra;
				}
			}
			drops.AddRange(extras);
			if (lucky) return drops;
			RandomNpcDropRule.Redistribute(drops);
			return includeLocked ? drops : drops.Where(d => !d.Locked).ToList();
		}
	}
}

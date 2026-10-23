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
	// Bag rules are kept untouched; their items are swapped when they spawn (LootSubstitutionItem).
	// We only remember the original content, for the journal, the tiers and the shuffle.
	public class BagLootItem : GlobalItem
	{
		public override void ModifyItemLoot(Item item, ItemLoot itemLoot)
		{
			List<IItemDropRule> rules = itemLoot.Get(false).Where(r => r != null).Distinct().ToList();
			if (rules.Count > 0) LootSnapshot.Items[item.type] = LootSnapshot.Capture(rules);
		}
	}

	public static class BagPlan
	{
		// A boss bag's floor is its own tier: its own loot is never locked
		public static int MinLimit(int bagType) => ItemID.Sets.BossBag[bagType] ? LootSnapshot.BagTier(bagType) : 0;

		// The content of a bag in this world, from its final plan (everything unlocked). lucky: that final plan as it is.
		// Otherwise what the bag gives now: the entries progression still locks give nothing and are left out (includeLocked:
		// kept, flagged). No redistribution: vanilla rules roll the bag
		public static List<PlannedDrop> Plan(int bagType, bool lucky = false, bool includeLocked = false)
		{
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
	}
}

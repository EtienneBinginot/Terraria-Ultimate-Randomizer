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
		// The content of a bag in this world
		public static List<PlannedDrop> Plan(int bagType, bool lucky = false)
		{
			List<PlannedDrop> drops = new();
			bool enabled = LootSubstitutionItem.BagEnabled(bagType, LootConfig.Instance);
			int bagTier = LootSnapshot.BagTier(bagType);
			foreach (DropRateInfo entry in LootSnapshot.ForItem(bagType))
			{
				int item = entry.itemId;
				int min = entry.stackMin;
				int max = entry.stackMax;
				if (enabled)
				{
					item = DropHelper.Replacement(RollKey.BagDrop, DetRandom.ItemKey(bagType), entry.itemId, bagTier, ItemID.Sets.BossBag[bagType] ? bagTier : 0, lucky);
					(min, max) = DropHelper.ScaledRange(entry.itemId, entry.stackMin, entry.stackMax, item);
				}
				drops.Add(new PlannedDrop { Item = item, StackMin = min, StackMax = max, Chance = entry.dropRate, Conditions = entry.conditions });
			}
			return drops;
		}
	}
}

using System.Collections.Generic;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	public class BagLootItem : GlobalItem
	{
		public override void ModifyItemLoot(Item item, ItemLoot itemLoot)
		{
			LootConfig cfg = LootConfig.Instance;
			int type = item.type;
			bool enabled = ItemID.Sets.BossBag[type] ? cfg.RandomizeBossBags
				: ItemID.Sets.IsFishingCrate[type] ? cfg.RandomizeCrates
				: cfg.RandomizeOtherBags;
			if (!enabled) return;
			List<IItemDropRule> rules = itemLoot.Get(false);
			if (rules.Count == 0) return;
			LootSnapshot.Items[type] = LootSnapshot.Capture(rules);
			foreach (IItemDropRule rule in rules)
			{
				if (rule != null) itemLoot.Remove(rule);
			}
			itemLoot.Add(new RandomBagRule(type));
		}
	}

	// Same contents layout as the original bag (same chances), each item replaced by one of the same category
	public class RandomBagRule : IItemDropRule
	{
		private readonly int bagType;

		public List<IItemDropRuleChainAttempt> ChainedRules { get; } = new();

		public RandomBagRule(int bagType)
		{
			this.bagType = bagType;
		}

		public bool CanDrop(DropAttemptInfo info) => true;

		public ItemDropAttemptResult TryDroppingItem(DropAttemptInfo info)
		{
			if (!SeedSystem.WorldReady || !ItemPool.Ready)
			{
				return new ItemDropAttemptResult { State = ItemDropAttemptResultState.DidNotRunCode };
			}
			foreach (PlannedDrop drop in Plan(bagType))
			{
				if (!DropHelper.ConditionsMet(drop.Conditions, info) || info.rng.NextFloat() >= drop.Chance) continue;
				DropHelper.Drop(info, drop.Item, drop.StackMin, drop.StackMax);
			}
			return new ItemDropAttemptResult { State = ItemDropAttemptResultState.Success };
		}

		// The content of a bag in this world (also used by the journal)
		public static List<PlannedDrop> Plan(int bagType)
		{
			List<PlannedDrop> drops = new();
			List<DropRateInfo> original = LootSnapshot.ForItem(bagType);
			int bagKey = DetRandom.ItemKey(bagType);
			int sourceTier = LootSnapshot.BagTier(bagType);
			for (int i = 0; i < original.Count; i++)
			{
				DropRateInfo entry = original[i];
				int item = entry.itemId;
				int min = entry.stackMin;
				int max = entry.stackMax;
				if (!DropHelper.KeepOriginal(entry.itemId))
				{
					RollRequest request = new()
					{
						Keys = new[] { RollKey.BagDrop, bagKey, i },
						SourceTier = sourceTier,
						OriginalItem = entry.itemId,
						Policy = CategoryPolicy.SameAsOriginal,
						TierJitterDown = 0,
						TierJitterUp = 0,
						AllowJackpot = true
					};
					item = ItemPool.Roll(request);
					(min, max) = DropHelper.ScaledRange(entry, item);
				}
				drops.Add(new PlannedDrop { Item = item, StackMin = min, StackMax = max, Chance = entry.dropRate, Conditions = entry.conditions });
			}
			return drops;
		}

		public void ReportDroprates(List<DropRateInfo> drops, DropRateInfoChainFeed ratesInfo)
		{
			DropHelper.ReportKept(LootSnapshot.ForItem(bagType), drops, ratesInfo);
			Chains.ReportDroprates(ChainedRules, 1f, drops, ratesInfo);
		}
	}
}

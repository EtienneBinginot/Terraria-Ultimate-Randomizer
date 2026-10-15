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
			List<DropRateInfo> original = LootSnapshot.ForItem(bagType);
			int bagKey = DetRandom.ItemKey(bagType);
			int sourceTier = LootSnapshot.BagTier(bagType);
			for (int i = 0; i < original.Count; i++)
			{
				DropRateInfo entry = original[i];
				if (DropHelper.KeepOriginal(entry.itemId))
				{
					DropHelper.TryDropEntry(info, entry, entry.itemId, entry.stackMin, entry.stackMax);
					continue;
				}
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
				int replacement = ItemPool.Roll(request);
				(int min, int max) = DropHelper.ScaledRange(entry, replacement);
				DropHelper.TryDropEntry(info, entry, replacement, min, max);
			}
			return new ItemDropAttemptResult { State = ItemDropAttemptResultState.Success };
		}

		public void ReportDroprates(List<DropRateInfo> drops, DropRateInfoChainFeed ratesInfo)
		{
			DropHelper.ReportKept(LootSnapshot.ForItem(bagType), drops, ratesInfo);
			Chains.ReportDroprates(ChainedRules, 1f, drops, ratesInfo);
		}
	}
}

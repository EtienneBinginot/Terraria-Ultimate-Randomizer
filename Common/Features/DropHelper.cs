using System;
using System.Collections.Generic;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	public static class DropHelper
	{
		// Items kept as they were: progression items, excluded items (coins, hearts...) and bags (their content is randomized instead)
		public static bool KeepOriginal(int itemType) =>
			!ItemPool.IsRandomizable(itemType) || ItemPool.Info[itemType].Category == ItemCategory.Bag;

		public static bool ConditionsMet(in DropRateInfo entry, DropAttemptInfo info)
		{
			if (entry.conditions == null) return true;
			foreach (IItemDropRuleCondition condition in entry.conditions)
			{
				if (condition != null && !condition.CanDrop(info)) return false;
			}
			return true;
		}

		// Drops an entry of an original drop table, with its original conditions and chance
		public static void TryDropEntry(DropAttemptInfo info, in DropRateInfo entry, int itemType, int stackMin, int stackMax)
		{
			if (!ConditionsMet(entry, info) || info.rng.NextFloat() >= entry.dropRate) return;
			Drop(info, itemType, stackMin, stackMax);
		}

		public static void Drop(DropAttemptInfo info, int itemType, int stackMin, int stackMax)
		{
			if (itemType <= ItemID.None) return;
			stackMin = Math.Max(1, stackMin);
			stackMax = Math.Max(stackMin, stackMax);
			CommonCode.DropItem(info, itemType, info.rng.Next(stackMin, stackMax + 1));
		}

		// Stack range of a replacement item, from the original entry's range
		public static (int min, int max) ScaledRange(in DropRateInfo entry, int newType)
		{
			int min = StackRules.Scaled(entry.itemId, entry.stackMin, newType);
			int max = StackRules.Scaled(entry.itemId, entry.stackMax, newType);
			return (min, Math.Max(min, max));
		}

		public static void ReportKept(List<DropRateInfo> source, List<DropRateInfo> drops, DropRateInfoChainFeed ratesInfo)
		{
			foreach (DropRateInfo entry in source)
			{
				if (!KeepOriginal(entry.itemId)) continue;
				DropRateInfo copy = new(entry.itemId, entry.stackMin, entry.stackMax, entry.dropRate * ratesInfo.parentDroprateChance, entry.conditions);
				drops.Add(copy);
			}
		}
	}
}

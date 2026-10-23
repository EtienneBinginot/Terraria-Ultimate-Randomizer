using System;
using System.Collections.Generic;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;

namespace UltimateRandomizer.Common.Core
{
	// The original drop tables, flattened, captured before our rules replace them
	public static class LootSnapshot
	{
		public static readonly Dictionary<int, List<DropRateInfo>> Npcs = new();
		public static readonly Dictionary<int, List<DropRateInfo>> Items = new();

		private static readonly Dictionary<int, int> bagTiers = new();

		public static List<DropRateInfo> Capture(List<IItemDropRule> rules)
		{
			List<DropRateInfo> drops = new();
			foreach (IItemDropRule rule in rules)
			{
				if (rule == null) continue;
				try
				{
					rule.ReportDroprates(drops, new DropRateInfoChainFeed(1f));
				}
				catch (Exception e)
				{
					ModContent.GetInstance<UltimateRandomizer>().Logger.Warn($"Could not read a drop rule ({rule.GetType().Name}): {e.Message}");
				}
			}
			return drops;
		}

		// Shared, do not modify (read per kill)
		private static readonly List<DropRateInfo> None = new();

		public static List<DropRateInfo> ForNpc(int npcType) => Npcs.TryGetValue(npcType, out List<DropRateInfo> drops) ? drops : None;

		public static List<DropRateInfo> ForItem(int itemType) => Items.TryGetValue(itemType, out List<DropRateInfo> drops) ? drops : None;

		// A boss bag is as advanced as the weakest boss dropping it
		public static int BagTier(int bagType)
		{
			if (bagTiers.TryGetValue(bagType, out int tier)) return tier;
			tier = -1;
			if (ItemID.Sets.BossBag[bagType])
			{
				foreach ((int npcType, List<DropRateInfo> drops) in Npcs)
				{
					if (drops.Exists(d => d.itemId == bagType))
					{
						int npcTier = NpcTiers.BaseTier(npcType);
						tier = tier < 0 ? npcTier : Math.Min(tier, npcTier);
					}
				}
			}
			if (tier < 0) tier = ItemPool.IsValid(bagType) ? ItemPool.Info[bagType].Tier : 0;
			bagTiers[bagType] = tier;
			return tier;
		}

		public static void Unload()
		{
			Npcs.Clear();
			Items.Clear();
			bagTiers.Clear();
		}
	}
}

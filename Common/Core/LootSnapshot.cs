using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
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
		// Per enemy net ID, for each entry of Npcs: the index of the registered rule it comes from when that rule was wrapped
		// (DropPermutation's PermutableRule), -1 when the rule stays as it is (per-player drops)
		public static readonly Dictionary<int, int[]> NpcRules = new();
		// The same per boss bag, for each entry of Items (BossPermutation)
		public static readonly Dictionary<int, int[]> ItemRules = new();

		private static readonly Dictionary<int, int> bagTiers = new();

		private static List<DropRateInfo> globalDrops;

		// The global drop rules (any enemy: Pirate Map, biome keys, souls of light and night...), flattened once: what
		// GetRulesForNPCID adds with its global drops (ItemDropDatabase._globalEntries). Our own rules are never global
		public static List<DropRateInfo> GlobalDrops()
		{
			if (globalDrops != null) return globalDrops;
			List<IItemDropRule> local = Main.ItemDropsDB.GetRulesForNPCID(0, false);
			return globalDrops = Capture(Main.ItemDropsDB.GetRulesForNPCID(0, true).Where(r => !local.Contains(r)).ToList());
		}

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

		private static readonly int[] NoRules = Array.Empty<int>();

		public static int[] RulesOf(int npcType) => NpcRules.TryGetValue(npcType, out int[] rules) ? rules : NoRules;

		public static List<DropRateInfo> ForItem(int itemType) => Items.TryGetValue(itemType, out List<DropRateInfo> drops) ? drops : None;

		public static int[] RulesOfItem(int itemType) => ItemRules.TryGetValue(itemType, out int[] rules) ? rules : NoRules;

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
			NpcRules.Clear();
			Items.Clear();
			ItemRules.Clear();
			bagTiers.Clear();
			globalDrops = null;
		}
	}
}

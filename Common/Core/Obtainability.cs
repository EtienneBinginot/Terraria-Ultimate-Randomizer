using System.Collections.Generic;
using Terraria.GameContent.ItemDropRules;

namespace UltimateRandomizer.Common.Core
{
	// Items that have at least one source in the base game (drop, bag, recipe, shop, or gathered in the world)
	public static class Obtainability
	{
		public static readonly HashSet<int> ShopItems = new();
		public static readonly HashSet<int> RecipeResults = new();
		// Every shop entry at load (selling NPC, item, its conditions), in load order: the tiers from sources
		// (ItemClassifier.RaiseBySources) read when each item is first sold
		public static readonly List<(int npc, int item, Terraria.Condition[] conditions)> ShopEntries = new();

		public static HashSet<int> Collect()
		{
			HashSet<int> set = new(ShopItems);
			set.UnionWith(RecipeResults);
			foreach (List<DropRateInfo> drops in LootSnapshot.Npcs.Values) AddDrops(set, drops);
			foreach (List<DropRateInfo> drops in LootSnapshot.Items.Values) AddDrops(set, drops);
			for (int type = 1; type < ItemPool.Info.Length; type++)
			{
				ItemCategory category = ItemPool.Info[type].Category;
				// Ores are mined; bars only count when a recipe makes them or something drops them (above)
				if (category is ItemCategory.Block or ItemCategory.Critter || OreMap.IsOre(type)) set.Add(type);
			}
			return set;
		}

		private static void AddDrops(HashSet<int> set, List<DropRateInfo> drops)
		{
			foreach (DropRateInfo drop in drops) set.Add(drop.itemId);
		}

		public static void Unload()
		{
			ShopItems.Clear();
			RecipeResults.Clear();
			ShopEntries.Clear();
		}
	}
}

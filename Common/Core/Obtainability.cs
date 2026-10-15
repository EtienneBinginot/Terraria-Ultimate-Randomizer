using System.Collections.Generic;
using Terraria.GameContent.ItemDropRules;

namespace UltimateRandomizer.Common.Core
{
	// Items that have at least one source in the base game (drop, bag, recipe, shop, or placeable from the world)
	public static class Obtainability
	{
		public static readonly HashSet<int> ShopItems = new();
		public static readonly HashSet<int> RecipeResults = new();

		public static HashSet<int> Collect()
		{
			HashSet<int> set = new(ShopItems);
			set.UnionWith(RecipeResults);
			foreach (List<DropRateInfo> drops in LootSnapshot.Npcs.Values) AddDrops(set, drops);
			foreach (List<DropRateInfo> drops in LootSnapshot.Items.Values) AddDrops(set, drops);
			for (int type = 1; type < ItemPool.Info.Length; type++)
			{
				ItemCategory category = ItemPool.Info[type].Category;
				if (category is ItemCategory.Block or ItemCategory.OreBar or ItemCategory.Critter) set.Add(type);
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
		}
	}
}

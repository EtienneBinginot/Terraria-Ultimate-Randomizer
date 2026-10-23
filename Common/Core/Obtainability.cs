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

		// withRecipes: false leaves out what only a recipe makes. anyBlock: every block and critter counts as gathered (the
		// obtainability checks); false keeps only the real sources: the world materials, ores and caught critters (a
		// crafted-only brick or wall is no spare result)
		public static HashSet<int> Collect(bool withRecipes = true, bool anyBlock = true)
		{
			HashSet<int> set = new(ShopItems);
			if (withRecipes) set.UnionWith(RecipeResults);
			foreach (List<DropRateInfo> drops in LootSnapshot.Npcs.Values) AddDrops(set, drops);
			foreach (List<DropRateInfo> drops in LootSnapshot.Items.Values) AddDrops(set, drops);
			for (int type = 1; type < ItemPool.Info.Length; type++)
			{
				ItemCategory category = ItemPool.Info[type].Category;
				// Ores are mined; bars only count when a recipe makes them or something drops them (above)
				if ((anyBlock && category is ItemCategory.Block or ItemCategory.Critter) || OreMap.IsOre(type)) set.Add(type);
			}
			if (!anyBlock)
			{
				set.UnionWith(Features.Findability.GatheredMaterials);
				foreach (Terraria.NPC npc in Terraria.ID.ContentSamples.NpcsByNetId.Values)
				{
					if (npc.catchItem > 0) set.Add(npc.catchItem);
				}
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

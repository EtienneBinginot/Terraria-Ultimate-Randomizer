using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;

namespace UltimateRandomizer.Common.Core
{
	// Mined ores: a permutation inside each tier, so every ore of the world still exists (as another ore's drop)
	public static class OreMap
	{
		private static Dictionary<int, int> map;

		public static bool IsOre(int type) =>
			ContentSamples.ItemsByType.TryGetValue(type, out Item sample) && sample.createTile >= TileID.Dirt && TileID.Sets.Ore[sample.createTile];

		public static int Get(int ore)
		{
			map ??= Build();
			return map.TryGetValue(ore, out int replacement) ? replacement : ore;
		}

		private static Dictionary<int, int> Build()
		{
			Dictionary<int, int> result = new();
			List<int>[] byTier = new List<int>[Tiers.Count];
			for (int t = 0; t < Tiers.Count; t++) byTier[t] = new List<int>();
			for (int type = 1; type < ItemPool.Info.Length; type++)
			{
				if (ItemPool.IsRandomizable(type) && IsOre(type)) byTier[ItemPool.Info[type].Tier].Add(type);
			}
			for (int t = 0; t < Tiers.Count; t++)
			{
				int[] permuted = byTier[t].ToArray();
				Random rng = DetRandom.Get(RollKey.OreShuffle, t);
				for (int i = permuted.Length - 1; i > 0; i--)
				{
					int j = rng.Next(i + 1);
					(permuted[i], permuted[j]) = (permuted[j], permuted[i]);
				}
				for (int i = 0; i < permuted.Length; i++) result[byTier[t][i]] = permuted[i];
			}
			return result;
		}

		public static void Reset() => map = null;
	}
}

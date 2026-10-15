using System;
using Terraria;
using UltimateRandomizer.Common.Configs;

namespace UltimateRandomizer.Common.Core
{
	// Shop prices derived from the item's real value, so a shop never becomes a money machine
	public static class PriceRules
	{
		// Used for items without a value (copper coins), by tier
		private static readonly int[] TierBasePrice =
		{
			Item.silver * 50, Item.gold, Item.gold * 2, Item.gold * 4, Item.gold * 8,
			Item.gold * 12, Item.gold * 18, Item.gold * 25, Item.gold * 35, Item.gold * 50
		};

		public static int Price(int itemType, Random rng)
		{
			EconomyConfig cfg = EconomyConfig.Instance;
			int value = StackRules.ValueOf(itemType);
			if (value <= 0)
			{
				int tier = ItemPool.IsValid(itemType) ? ItemPool.Info[itemType].Tier : 0;
				value = TierBasePrice[Tiers.Clamp(tier)];
			}
			float min = Math.Min(cfg.PriceMultiplierMin, cfg.PriceMultiplierMax);
			float max = Math.Max(cfg.PriceMultiplierMin, cfg.PriceMultiplierMax);
			double multiplier = min + rng.NextDouble() * (max - min);
			return (int)Math.Clamp(Math.Round(value * multiplier), Math.Max(1, cfg.MinimumPrice), int.MaxValue / 2);
		}
	}
}

using System;
using Terraria;
using UltimateRandomizer.Common.Configs;

namespace UltimateRandomizer.Common.Core
{
	// Shop prices derived from the item's real value, kept affordable for its tier: money is no obstacle, only the item is
	// random. Never below half the value, so buying and reselling never makes money (the best happiness and the Discount
	// Card take 40 % off a price at most, reselling pays at most value / 5 / 0.75)
	public static class PriceRules
	{
		// Gear without a value (copper coins), and the base of the price ceiling, by tier
		private static readonly int[] TierBasePrice =
		{
			Item.silver * 50, Item.gold, Item.gold * 2, Item.gold * 4, Item.gold * 8,
			Item.gold * 12, Item.gold * 18, Item.gold * 25, Item.gold * 35, Item.gold * 50
		};

		// Gear may cost this many times the ceiling of the other items: a vanilla-ish price for what the money is for
		public const int GearCeilingFactor = 4;

		// Weapons, armor, accessories and tools. Mounts and pets are priced like the other items
		public static bool IsPricedAsGear(int itemType) =>
			ItemPool.IsValid(itemType) && ItemPool.Info[itemType].Category != ItemCategory.MountPet && ItemCategories.IsGear(ItemPool.Info[itemType].Category);

		private static int TierOf(int itemType) => Tiers.Clamp(ItemPool.TierOf(itemType));

		// The value the price starts from: the item's own, else a small one for its category (a block, a wall or a rocket without
		// a value costs a few copper, a piece of furniture a few silver), the tier base for gear only
		public static int BaseValue(int itemType)
		{
			int value = StackRules.ValueOf(itemType);
			if (value > 0) return value;
			int tier = TierOf(itemType);
			ItemCategory category = ItemPool.IsValid(itemType) ? ItemPool.Info[itemType].Category : ItemCategory.Misc;
			return category switch
			{
				ItemCategory.Ammo => 2 * (tier + 1),
				ItemCategory.Block => 5 * (tier + 1),
				ItemCategory.Material or ItemCategory.OreBar => 20 * (tier + 1),
				ItemCategory.Furniture => 50 * (tier + 1),
				_ when IsPricedAsGear(itemType) => TierBasePrice[tier],
				_ => TierBasePrice[tier] / 10
			};
		}

		// The highest price of an item of its tier (int.MaxValue / 2: no ceiling). The floor still wins over it
		public static int Ceiling(int itemType)
		{
			float factor = EconomyConfig.Instance.PriceCeiling;
			if (factor <= 0) return int.MaxValue / 2;
			double ceiling = TierBasePrice[TierOf(itemType)] * (double)factor * (IsPricedAsGear(itemType) ? GearCeilingFactor : 1);
			return (int)Math.Min(ceiling, int.MaxValue / 2);
		}

		// No gear, and worth more than its tier affords (vanity, furniture, pets, pearls...): the shop rolls avoid it
		public static bool Overpriced(int itemType) => !IsPricedAsGear(itemType) && StackRules.ValueOf(itemType) > Ceiling(itemType);

		// The lowest price: half the value (no money exploit), and the minimum price; a stackable item (an arrow, a block,
		// bought one at a time) has its own, lower minimum
		public static int Floor(int itemType)
		{
			EconomyConfig cfg = EconomyConfig.Instance;
			int minimum = StackRules.MaxStackOf(itemType) > 1 ? cfg.MinimumStackablePrice : cfg.MinimumPrice;
			return Math.Max(Math.Max(1, minimum), (StackRules.ValueOf(itemType) + 1) / 2);
		}

		public static int Price(int itemType, Random rng)
		{
			EconomyConfig cfg = EconomyConfig.Instance;
			int value = BaseValue(itemType);
			float min = Math.Min(cfg.PriceMultiplierMin, cfg.PriceMultiplierMax);
			float max = Math.Max(cfg.PriceMultiplierMin, cfg.PriceMultiplierMax);
			double multiplier = min + rng.NextDouble() * (max - min);
			double price = Math.Min(value * multiplier, Ceiling(itemType));
			return (int)Math.Clamp(Math.Round(Math.Max(price, Floor(itemType))), 1, int.MaxValue / 2);
		}
	}
}

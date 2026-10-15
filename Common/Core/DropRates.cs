using System;

namespace UltimateRandomizer.Common.Core
{
	// Vanilla-like drop rates: materials are common, equipment is rare, and items above the enemy's level are even rarer
	public static class DropRates
	{
		private static readonly int[] Common = { 1, 1, 2, 2, 3, 4, 5, 8, 10 };
		private static readonly int[] Uncommon = { 5, 8, 10, 15, 20, 25, 33, 50 };
		private static readonly int[] Rare = { 15, 20, 25, 33, 40, 50, 75, 100, 150 };

		// Returns N for a "1 in N" chance
		public static int Denominator(int itemType, int itemTier, int sourceTier, bool isBoss, Random rng)
		{
			ItemCategory category = ItemPool.IsValid(itemType) ? ItemPool.Info[itemType].Category : ItemCategory.Misc;
			int[] rates = category switch
			{
				ItemCategory.Material or ItemCategory.OreBar or ItemCategory.Potion or ItemCategory.Food or ItemCategory.Ammo or ItemCategory.Block => Common,
				ItemCategory.WeaponMelee or ItemCategory.WeaponRanged or ItemCategory.WeaponMagic or ItemCategory.WeaponSummon or ItemCategory.WeaponOther
					or ItemCategory.Armor or ItemCategory.Accessory or ItemCategory.Tool or ItemCategory.MountPet or ItemCategory.Bag => Rare,
				_ => Uncommon
			};
			int denominator = rates[rng.Next(rates.Length)] * (1 + 2 * Math.Max(0, itemTier - sourceTier));
			if (isBoss) denominator = Math.Max(1, denominator / 5);
			return denominator;
		}
	}
}

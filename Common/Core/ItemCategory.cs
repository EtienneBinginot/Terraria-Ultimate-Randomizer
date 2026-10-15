namespace UltimateRandomizer.Common.Core
{
	// Weapons are split by damage class so a melee weapon is always replaced by a melee weapon
	public enum ItemCategory : byte
	{
		Misc,
		BossSummon,
		Bag,
		Critter,
		Dye,
		Vanity,
		Accessory,
		Armor,
		Tool,
		WeaponMelee,
		WeaponRanged,
		WeaponMagic,
		WeaponSummon,
		WeaponOther,
		Ammo,
		MountPet,
		Potion,
		Food,
		OreBar,
		Block,
		Furniture,
		Material
	}

	public static class ItemCategories
	{
		public const int Count = (int)ItemCategory.Material + 1;
	}
}

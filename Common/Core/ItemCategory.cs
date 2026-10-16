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

		// Equipment: what makes a "good find" (weapons, armor, accessories, tools, mounts and pets)
		public static bool IsGear(ItemCategory category) => category is ItemCategory.WeaponMelee or ItemCategory.WeaponRanged
			or ItemCategory.WeaponMagic or ItemCategory.WeaponSummon or ItemCategory.WeaponOther or ItemCategory.Armor
			or ItemCategory.Accessory or ItemCategory.Tool or ItemCategory.MountPet;
	}
}

using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Configs
{
	// Maximum stack given at once for each category (the item's own max stack still applies)
	public class StackCaps
	{
		[Range(1, 9999)] [DefaultValue(20)] public int Material = 20;
		[Range(1, 9999)] [DefaultValue(30)] public int OreBar = 30;
		[Range(1, 9999)] [DefaultValue(5)] public int Potion = 5;
		[Range(1, 9999)] [DefaultValue(3)] public int Food = 3;
		[Range(1, 9999)] [DefaultValue(150)] public int Ammo = 150;
		[Range(1, 9999)] [DefaultValue(1)] public int Weapon = 1;
		[Range(1, 9999)] [DefaultValue(1)] public int Equipment = 1;
		[Range(1, 9999)] [DefaultValue(50)] public int Block = 50;
		[Range(1, 9999)] [DefaultValue(3)] public int Furniture = 3;
		[Range(1, 9999)] [DefaultValue(3)] public int Dye = 3;
		[Range(1, 9999)] [DefaultValue(3)] public int Critter = 3;
		[Range(1, 9999)] [DefaultValue(2)] public int Bag = 2;
		[Range(1, 9999)] [DefaultValue(3)] public int BossSummon = 3;
		[Range(1, 9999)] [DefaultValue(10)] public int Misc = 10;

		public int Get(ItemCategory category) => category switch
		{
			ItemCategory.Material => Material,
			ItemCategory.OreBar => OreBar,
			ItemCategory.Potion => Potion,
			ItemCategory.Food => Food,
			ItemCategory.Ammo => Ammo,
			ItemCategory.WeaponMelee or ItemCategory.WeaponRanged or ItemCategory.WeaponMagic or ItemCategory.WeaponSummon or ItemCategory.WeaponOther => Weapon,
			ItemCategory.Armor or ItemCategory.Accessory or ItemCategory.Tool or ItemCategory.Vanity or ItemCategory.MountPet => Equipment,
			ItemCategory.Block => Block,
			ItemCategory.Furniture => Furniture,
			ItemCategory.Dye => Dye,
			ItemCategory.Critter => Critter,
			ItemCategory.Bag => Bag,
			ItemCategory.BossSummon => BossSummon,
			_ => Misc
		};

		public override bool Equals(object obj) => obj is StackCaps other && GetHashCode() == other.GetHashCode();

		public override int GetHashCode()
		{
			int hash = 17;
			for (int i = 0; i < ItemCategories.Count; i++) hash = hash * 31 + Get((ItemCategory)i);
			return hash;
		}
	}

	public class EconomyConfig : RandomizerConfig
	{
		public static EconomyConfig Instance => ModContent.GetInstance<EconomyConfig>();

		[Header("Shops")]
		[DefaultValue(true)]
		public bool RandomizeShops;

		// At least 1: below 0.2, buying then selling back would make infinite money
		[Range(1f, 10f)]
		[Increment(0.1f)]
		[DefaultValue(1f)]
		public float PriceMultiplierMin;

		[Range(1f, 10f)]
		[Increment(0.1f)]
		[DefaultValue(2.5f)]
		public float PriceMultiplierMax;

		[Range(0, 1000000)]
		[DefaultValue(100)]
		public int MinimumPrice;

		// Ammo, blocks, potions... are bought one at a time: their own small price, not the minimum price
		[Range(1, 1000000)]
		[DefaultValue(1)]
		public int MinimumStackablePrice;

		// Price ceiling = a base price of the item's tier x this (gear: x 4 more); 0: no ceiling. Items worth more than it
		// (no gear) are seldom rolled
		[Range(0f, 50f)]
		[Increment(0.5f)]
		[DefaultValue(3f)]
		public float PriceCeiling;

		[DefaultValue(true)]
		public bool NoDuplicatesInShop;

		// World-wide: one item is rolled by at most this many merchants (0: no limit)
		[Range(0, 50)]
		[DefaultValue(3)]
		public int MaxShopsPerItem;

		[DefaultValue(true)]
		public bool ShopsFollowProgression;

		// Share of the rolled shop slots selling an item of any category (loot weights) instead of the original's
		[Range(0f, 100f)]
		[Increment(1f)]
		[DefaultValue(10f)]
		public float ShopSurpriseChance;

		[Header("Stacks")]
		public StackCaps StackCaps = new();

		[Range(0.1f, 10f)]
		[Increment(0.1f)]
		[DefaultValue(1.5f)]
		public float StackMultiplier;

		[DefaultValue(true)]
		public bool ValueBasedStacks;
	}
}

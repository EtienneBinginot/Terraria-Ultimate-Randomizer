using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Configs
{
	// Relative chance of each category when a random drop is rolled (0 = never)
	public class CategoryWeights
	{
		[Range(0, 100)] [DefaultValue(30)] public int Material = 30;
		[Range(0, 100)] [DefaultValue(10)] public int OreBar = 10;
		[Range(0, 100)] [DefaultValue(10)] public int Potion = 10;
		[Range(0, 100)] [DefaultValue(4)] public int Food = 4;
		[Range(0, 100)] [DefaultValue(6)] public int Ammo = 6;
		[Range(0, 100)] [DefaultValue(4)] public int WeaponMelee = 4;
		[Range(0, 100)] [DefaultValue(4)] public int WeaponRanged = 4;
		[Range(0, 100)] [DefaultValue(4)] public int WeaponMagic = 4;
		[Range(0, 100)] [DefaultValue(4)] public int WeaponSummon = 4;
		[Range(0, 100)] [DefaultValue(1)] public int WeaponOther = 1;
		[Range(0, 100)] [DefaultValue(6)] public int Armor = 6;
		[Range(0, 100)] [DefaultValue(8)] public int Accessory = 8;
		[Range(0, 100)] [DefaultValue(4)] public int Tool = 4;
		[Range(0, 100)] [DefaultValue(6)] public int Block = 6;
		[Range(0, 100)] [DefaultValue(3)] public int Furniture = 3;
		[Range(0, 100)] [DefaultValue(2)] public int MountPet = 2;
		[Range(0, 100)] [DefaultValue(2)] public int Vanity = 2;
		[Range(0, 100)] [DefaultValue(1)] public int Dye = 1;
		[Range(0, 100)] [DefaultValue(1)] public int Critter = 1;
		[Range(0, 100)] [DefaultValue(1)] public int Bag = 1;
		[Range(0, 100)] [DefaultValue(1)] public int Misc = 1;

		public int Get(ItemCategory category) => category switch
		{
			ItemCategory.Material => Material,
			ItemCategory.OreBar => OreBar,
			ItemCategory.Potion => Potion,
			ItemCategory.Food => Food,
			ItemCategory.Ammo => Ammo,
			ItemCategory.WeaponMelee => WeaponMelee,
			ItemCategory.WeaponRanged => WeaponRanged,
			ItemCategory.WeaponMagic => WeaponMagic,
			ItemCategory.WeaponSummon => WeaponSummon,
			ItemCategory.WeaponOther => WeaponOther,
			ItemCategory.Armor => Armor,
			ItemCategory.Accessory => Accessory,
			ItemCategory.Tool => Tool,
			ItemCategory.Block => Block,
			ItemCategory.Furniture => Furniture,
			ItemCategory.MountPet => MountPet,
			ItemCategory.Vanity => Vanity,
			ItemCategory.Dye => Dye,
			ItemCategory.Critter => Critter,
			ItemCategory.Bag => Bag,
			ItemCategory.Misc => Misc,
			_ => 0 // Boss summons are never rolled at random
		};

		public override bool Equals(object obj) => obj is CategoryWeights other && GetHashCode() == other.GetHashCode();

		public override int GetHashCode()
		{
			int hash = 17;
			for (int i = 0; i < ItemCategories.Count; i++) hash = hash * 31 + Get((ItemCategory)i);
			return hash;
		}
	}

	public class LootConfig : ModConfig
	{
		public override ConfigScope Mode => ConfigScope.ServerSide;

		public static LootConfig Instance => ModContent.GetInstance<LootConfig>();

		[Header("Mobs")]
		[DefaultValue(true)]
		[ReloadRequired]
		public bool RandomizeMobDrops;

		[Range(0, 10)]
		[DefaultValue(1)]
		public int DropSlotsMin;

		[Range(0, 10)]
		[DefaultValue(3)]
		public int DropSlotsMax;

		[Range(1f, 100f)]
		[Increment(1f)]
		[DefaultValue(25f)]
		public float DropChance;

		[Range(0, 20)]
		[DefaultValue(4)]
		public int BossDropSlotsMin;

		[Range(0, 20)]
		[DefaultValue(8)]
		public int BossDropSlotsMax;

		public CategoryWeights DropCategoryWeights = new();

		[Header("Bags")]
		[DefaultValue(true)]
		[ReloadRequired]
		public bool RandomizeBossBags;

		[DefaultValue(true)]
		[ReloadRequired]
		public bool RandomizeCrates;

		[DefaultValue(true)]
		[ReloadRequired]
		public bool RandomizeOtherBags;
	}
}

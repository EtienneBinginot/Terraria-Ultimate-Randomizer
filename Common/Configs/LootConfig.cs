using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Configs
{
	// Relative chance of each category when a random drop is rolled (0 = never)
	public class CategoryWeights
	{
		[Range(0, 100)] [DefaultValue(20)] public int Material = 20;
		[Range(0, 100)] [DefaultValue(8)] public int OreBar = 8;
		[Range(0, 100)] [DefaultValue(10)] public int Potion = 10;
		[Range(0, 100)] [DefaultValue(4)] public int Food = 4;
		[Range(0, 100)] [DefaultValue(5)] public int Ammo = 5;
		[Range(0, 100)] [DefaultValue(6)] public int WeaponMelee = 6;
		[Range(0, 100)] [DefaultValue(6)] public int WeaponRanged = 6;
		[Range(0, 100)] [DefaultValue(6)] public int WeaponMagic = 6;
		[Range(0, 100)] [DefaultValue(6)] public int WeaponSummon = 6;
		[Range(0, 100)] [DefaultValue(2)] public int WeaponOther = 2;
		[Range(0, 100)] [DefaultValue(7)] public int Armor = 7;
		[Range(0, 100)] [DefaultValue(10)] public int Accessory = 10;
		[Range(0, 100)] [DefaultValue(5)] public int Tool = 5;
		[Range(0, 100)] [DefaultValue(3)] public int Block = 3;
		[Range(0, 100)] [DefaultValue(3)] public int Furniture = 3;
		[Range(0, 100)] [DefaultValue(4)] public int MountPet = 4;
		[Range(0, 100)] [DefaultValue(3)] public int Vanity = 3;
		[Range(0, 100)] [DefaultValue(2)] public int Dye = 2;
		[Range(0, 100)] [DefaultValue(1)] public int Critter = 1;
		[Range(0, 100)] [DefaultValue(3)] public int Bag = 3;
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

	public enum DropTableStyle
	{
		// Random number of drops per enemy (usually few, sometimes many) with vanilla-like rates (1/1, 1/3, 1/10, 1/50...)
		VanillaLike,
		// Same number of drops and same rates as the original enemy, each item replaced by one of the same category
		MirrorOriginal,
		// Fixed chance for every slot
		Simple
	}

	public class LootConfig : RandomizerConfig
	{
		public static LootConfig Instance => ModContent.GetInstance<LootConfig>();

		[Header("Mobs")]
		[DefaultValue(true)]
		public bool RandomizeMobDrops;

		[DefaultValue(DropTableStyle.VanillaLike)]
		[DrawTicks]
		public DropTableStyle DropTableStyle;

		[DefaultValue(true)]
		public bool EnemiesWithoutDropsCanDrop;

		[DefaultValue(true)]
		public bool CrittersCanDrop;

		[Range(0, 20)]
		[DefaultValue(1)]
		public int DropSlotsMin;

		[Range(0, 20)]
		[DefaultValue(4)]
		public int DropSlotsMax;

		[Range(0.1f, 10f)]
		[Increment(0.1f)]
		[DefaultValue(2.5f)]
		public float DropRateMultiplier;

		[Range(1f, 100f)]
		[Increment(1f)]
		[DefaultValue(40f)]
		public float DropChance;

		[Range(0, 30)]
		[DefaultValue(3)]
		public int BossDropSlotsMin;

		[Range(0, 30)]
		[DefaultValue(8)]
		public int BossDropSlotsMax;

		public CategoryWeights DropCategoryWeights = new();

		[Header("Bags")]
		[DefaultValue(true)]
		public bool RandomizeBossBags;

		[DefaultValue(true)]
		public bool RandomizeCrates;

		[DefaultValue(true)]
		public bool RandomizeOtherBags;

		[Header("World")]
		[DefaultValue(true)]
		public bool RandomizeChests;

		[Range(0, 5)]
		[DefaultValue(1)]
		public int ChestTierBonus;

		[DefaultValue(true)]
		public bool RandomizeFishing;

		[DefaultValue(true)]
		public bool CratesStayCrates;

		[DefaultValue(true)]
		public bool RandomizeOreDrops;

		[DefaultValue(true)]
		public bool RandomizePotDrops;
	}
}

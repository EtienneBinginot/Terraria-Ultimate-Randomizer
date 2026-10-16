using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Configs
{
	// "Loot" weights: relative chance of each category when something gives a random item (0 = never)
	public class CategoryWeights : WeightTable
	{
		[Range(0, 100)] [DefaultValue(6)] public int Material = 6;
		[Range(0, 100)] [DefaultValue(4)] public int OreBar = 4;
		[Range(0, 100)] [DefaultValue(8)] public int Potion = 8;
		[Range(0, 100)] [DefaultValue(2)] public int Food = 2;
		[Range(0, 100)] [DefaultValue(3)] public int Ammo = 3;
		[Range(0, 100)] [DefaultValue(10)] public int WeaponMelee = 10;
		[Range(0, 100)] [DefaultValue(10)] public int WeaponRanged = 10;
		[Range(0, 100)] [DefaultValue(10)] public int WeaponMagic = 10;
		[Range(0, 100)] [DefaultValue(8)] public int WeaponSummon = 8;
		[Range(0, 100)] [DefaultValue(3)] public int WeaponOther = 3;
		[Range(0, 100)] [DefaultValue(10)] public int Armor = 10;
		[Range(0, 100)] [DefaultValue(14)] public int Accessory = 14;
		[Range(0, 100)] [DefaultValue(6)] public int Tool = 6;
		[Range(0, 100)] [DefaultValue(1)] public int Block = 1;
		[Range(0, 100)] [DefaultValue(3)] public int Furniture = 3;
		[Range(0, 100)] [DefaultValue(4)] public int MountPet = 4;
		[Range(0, 100)] [DefaultValue(3)] public int Vanity = 3;
		[Range(0, 100)] [DefaultValue(1)] public int Dye = 1;
		[Range(0, 100)] [DefaultValue(1)] public int Critter = 1;
		[Range(0, 100)] [DefaultValue(2)] public int Bag = 2;
		[Range(0, 100)] [DefaultValue(1)] public int Misc = 1;
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

		[Header("Variety")]
		// Chance that a replaced item (drop, chest, pot, bag, fish...) is rolled with the loot weights instead of keeping its category
		[Range(0f, 100f)]
		[Increment(1f)]
		[DefaultValue(10f)]
		public float SurpriseChance;

		// Items are picked among this many tiers below the target too: more variety early on
		[Range(0, 2)]
		[DefaultValue(1)]
		public int TierWindow;

		public CategoryWeights DropCategoryWeights = new();

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

		[DefaultValue(true)]
		public bool RandomizeTreeShaking;

		[DefaultValue(true)]
		public bool KeepAcorns;

		[DefaultValue(true)]
		public bool RandomizeHerbs;

		// Chance that a herb or plant still gives its usual item
		[Range(0f, 100f)]
		[Increment(5f)]
		[DefaultValue(40f)]
		public float HerbKeepChance;

		// Number of different items each herb can turn into in a world
		[Range(1, 6)]
		[DefaultValue(3)]
		public int HerbVariety;

		[DefaultValue(true)]
		public bool RandomizeExtractinator;

		[DefaultValue(true)]
		public bool RandomizeAnglerRewards;
	}
}

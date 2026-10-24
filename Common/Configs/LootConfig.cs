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

	// Weights of the random extra drops of the permutation style: the loot weights without materials, ores and bars (the
	// shuffled original drops already bring plenty of them)
	public class PermutationExtraWeights : WeightTable
	{
		[Range(0, 100)] [DefaultValue(0)] public int Material = 0;
		[Range(0, 100)] [DefaultValue(0)] public int OreBar = 0;
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
		Simple,
		// The original drops of the enemies are shuffled between enemies of the same tier (each keeps its own chance and
		// stack, the conditions are the receiving enemy's), a few of them turned into surprises (PermutationSurpriseChance,
		// and every copy of an item beyond PermutationDuplicateCopies / PermutationMaterialCopies),
		// plus random extra drops at vanilla-like rates (PermutationExtraSlots)
		Permutation
	}

	public class LootConfig : RandomizerConfig
	{
		public static LootConfig Instance => ModContent.GetInstance<LootConfig>();

		[Header("Variety")]
		// Chance that a replaced item (drop, chest, pot, bag, fish...) is rolled with the loot weights instead of keeping its category
		[Range(0f, 100f)]
		[Increment(1f)]
		// 8: with DropRateMultiplier 1.2, about 22 pieces of equipment per early hour (PacingModel, target 15-25)
		[DefaultValue(8f)]
		public float SurpriseChance;

		// Items are picked among this many tiers below the target too: more variety early on
		[Range(0, 2)]
		[DefaultValue(1)]
		public int TierWindow;

		public CategoryWeights DropCategoryWeights = new();

		[Header("Mobs")]
		[DefaultValue(true)]
		public bool RandomizeMobDrops;

		[DefaultValue(DropTableStyle.Permutation)]
		[DrawTicks]
		public DropTableStyle DropTableStyle;

		// Permutation style: random extra drops of each enemy, at vanilla-like rates (DropRates x DropRateMultiplier, not
		// scaled by loot abundance), rolled with PermutationExtraWeights
		[Range(0, 3)]
		[DefaultValue(2)]
		public int PermutationExtraSlots;

		public PermutationExtraWeights PermutationExtraWeights = new();

		// Permutation style: share (%) of the shuffled drops (not materials, ores or bars) that become a random item of the
		// enemy's level, keeping the conditions of the entry and its chance (capped by a vanilla-like rate for the new item);
		// never the keeper copy every item but blocks, critters and ores keeps (DropPermutation.SurpriseCandidates). The
		// copies beyond PermutationDuplicateCopies / PermutationMaterialCopies are always surprises, whatever this chance
		[Range(0f, 100f)]
		[Increment(1f)]
		[DefaultValue(15f)]
		public float PermutationSurpriseChance;

		// Permutation style: the permuted copies an item keeps in each access class (the most reachable ones); every other copy
		// becomes a surprise. Vanilla gives each zombie variant its Shackle and each slime its Slime Staff: without a cap the
		// early loot looks vanilla. 0: no limit. Not materials (below), ores, bars, blocks or critters
		[Range(0, 20)]
		[DefaultValue(2)]
		public int PermutationDuplicateCopies;

		// The same cap for materials (Gel, Lens...), higher: recipes expect several enemies to give them. 0: no limit
		[Range(0, 20)]
		[DefaultValue(4)]
		public int PermutationMaterialCopies;

		[DefaultValue(true)]
		public bool EnemiesWithoutDropsCanDrop;

		[DefaultValue(true)]
		public bool CrittersCanDrop;

		// Share of the small frequent drop critters get: they come in swarms and die in one hit
		[Range(0f, 2f)]
		[Increment(0.1f)]
		[DefaultValue(0.5f)]
		public float CritterDropFactor;

		// Permutation style: chance (%) of the one small frequent drop critters get on top of their slots (enemies get none there:
		// the moved original drops feed them; critters have no place in the permutation)
		[Range(0f, 100f)]
		[Increment(5f)]
		[DefaultValue(35f)]
		public float CritterCommonDropChance;

		// 2 to 7 (skewed towards the minimum): about 3.7 slots per enemy type before abundance, long drop tables
		[Range(0, 20)]
		[DefaultValue(2)]
		public int DropSlotsMin;

		[Range(0, 20)]
		[DefaultValue(7)]
		public int DropSlotsMax;

		[Range(0.1f, 10f)]
		[Increment(0.1f)]
		[DefaultValue(1.2f)]
		public float DropRateMultiplier;

		[Range(1f, 100f)]
		[Increment(1f)]
		[DefaultValue(40f)]
		public float DropChance;

		// Chance of the small frequent drop (material, potion, ammo...) of every enemy
		[Range(0f, 100f)]
		[Increment(5f)]
		[DefaultValue(50f)]
		public float CommonDropChance;

		// Scales the random enemy drops: slots and their chances by its square root each (about x abundance items),
		// the small frequent drop by the full factor (above 100 %: more of them). Guaranteed drops are not scaled.
		// 3 (with 2 to 7 slots): about 46 pieces of equipment per early hour and 3.2 items per kill (PacingModel, targets
		// 34-62 and at least 1.6)
		[Range(0.5f, 5f)]
		[Increment(0.25f)]
		[DefaultValue(3f)]
		public float LootAbundance;

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

		// Share (%) of the natural chests that become jackpot chests (at least 2 per world): useful items above the chest's
		// tier, glowing until opened. Placed at world creation, or once in a world randomized before them
		[Range(0f, 10f)]
		[Increment(0.5f)]
		[DefaultValue(1.5f)]
		public float JackpotChestChance;

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

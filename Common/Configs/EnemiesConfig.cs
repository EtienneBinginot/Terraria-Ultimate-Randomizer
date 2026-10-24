using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace UltimateRandomizer.Common.Configs
{
	public enum EnemySwapMode
	{
		// Each enemy type is always replaced by the same other type in a world
		PerType,
		// Every spawn picks a new replacement
		PerSpawn,
		// Natural enemies are redistributed between the biomes: each one keeps its number of spawn places, elsewhere
		ByBiome
	}

	public class EnemiesConfig : RandomizerConfig
	{
		public static EnemiesConfig Instance => ModContent.GetInstance<EnemiesConfig>();

		// Most the mod multiplies the damage of an enemy, boss or boss part by (type x variation x giant x adaptive power).
		// Defense is subtracted from damage: x10 damage turns a 1 damage hit into a one-shot, unlike x10 health
		[Header("DamageLimit")]
		[Range(1f, 20f)]
		[Increment(0.1f)]
		[DefaultValue(3f)]
		public float MaxDamageMultiplier;

		[Header("Bosses")]
		[DefaultValue(true)]
		public bool RandomizeBossHealth;

		[Range(0.05f, 20f)]
		[Increment(0.05f)]
		[DefaultValue(0.7f)]
		public float BossHealthMin;

		[Range(0.05f, 20f)]
		[Increment(0.05f)]
		[DefaultValue(2f)]
		public float BossHealthMax;

		[DefaultValue(true)]
		public bool RandomizeBossDamage;

		[Range(0.05f, 20f)]
		[Increment(0.05f)]
		[DefaultValue(0.75f)]
		public float BossDamageMin;

		[Range(0.05f, 20f)]
		[Increment(0.05f)]
		[DefaultValue(1.35f)]
		public float BossDamageMax;

		// How rare values far from normal are for bosses (1 = as likely as normal ones)
		[Range(1f, 5f)]
		[Increment(0.1f)]
		[DefaultValue(2.5f)]
		public float BossStatExtremeness;

		// Small variation between two fights of the same boss (%)
		[Range(0f, 50f)]
		[Increment(1f)]
		[DefaultValue(0f)]
		public float BossIndividualVariation;

		// Chance that a boss is a giant (%)
		[Range(0f, 100f)]
		[Increment(1f)]
		[DefaultValue(0f)]
		public float BossGiantChance;

		[Header("Enemies")]
		[DefaultValue(true)]
		public bool RandomizeEnemyStats;

		[Range(0.05f, 20f)]
		[Increment(0.05f)]
		[DefaultValue(0.5f)]
		public float EnemyHealthMin;

		[Range(0.05f, 20f)]
		[Increment(0.05f)]
		[DefaultValue(3f)]
		public float EnemyHealthMax;

		[Range(0.05f, 20f)]
		[Increment(0.05f)]
		[DefaultValue(0.5f)]
		public float EnemyDamageMin;

		[Range(0.05f, 20f)]
		[Increment(0.05f)]
		[DefaultValue(1.6f)]
		public float EnemyDamageMax;

		[Range(0.05f, 20f)]
		[Increment(0.05f)]
		[DefaultValue(0.6f)]
		public float EnemyDefenseMin;

		[Range(0.05f, 20f)]
		[Increment(0.05f)]
		[DefaultValue(2f)]
		public float EnemyDefenseMax;

		[Range(0.3f, 3f)]
		[Increment(0.05f)]
		[DefaultValue(0.7f)]
		public float EnemyScaleMin;

		[Range(0.3f, 3f)]
		[Increment(0.05f)]
		[DefaultValue(1.4f)]
		public float EnemyScaleMax;

		// How rare values far from normal are (1 = every value as likely; higher = extremes rarer)
		[Range(1f, 5f)]
		[Increment(0.1f)]
		[DefaultValue(2.2f)]
		public float StatExtremeness;

		// How much a tougher enemy type is also bigger (0 = unrelated, 1 = size follows health)
		[Range(0f, 1f)]
		[Increment(0.1f)]
		[DefaultValue(0.5f)]
		public float ScaleFollowsLife;

		// Variation between two enemies of the same type (%)
		[Range(0f, 50f)]
		[Increment(1f)]
		[DefaultValue(15f)]
		public float IndividualVariation;

		[Header("GiantsAndMinis")]
		// Chance that an enemy is a giant (%): giants are strong, rare enough not to be in every fight
		[Range(0f, 100f)]
		[Increment(0.5f)]
		[DefaultValue(2f)]
		public float GiantChance;

		[Range(1f, 6f)]
		[Increment(0.1f)]
		[DefaultValue(2f)]
		public float GiantSizeMin;

		[Range(1f, 6f)]
		[Increment(0.1f)]
		[DefaultValue(4f)]
		public float GiantSizeMax;

		// Chance that an enemy is a mini (%)
		[Range(0f, 100f)]
		[Increment(0.5f)]
		[DefaultValue(4f)]
		public float MiniChance;

		[Range(0.1f, 1f)]
		[Increment(0.05f)]
		[DefaultValue(0.25f)]
		public float MiniSizeMin;

		[Range(0.1f, 1f)]
		[Increment(0.05f)]
		[DefaultValue(0.5f)]
		public float MiniSizeMax;

		// Largest width or height of a giant, in pixels (a tile is 16)
		[Range(100, 2000)]
		[DefaultValue(600)]
		public int MaxGiantPixels;

		[Header("LootBonus")]
		// Stronger enemies (health x damage squared, EnemyIndividualNPC.Strength) drop more: extra drop chances x (1 + strength x (factor - 1))
		[Range(0f, 2f)]
		[Increment(0.1f)]
		[DefaultValue(0.5f)]
		public float LootBonusStrength;

		[Range(1f, 5f)]
		[Increment(0.1f)]
		[DefaultValue(3f)]
		public float LootBonusCap;

		// Giants always drop one more useful item, minis one more item of their level (MobLootFeature.BonusDrops)
		[DefaultValue(true)]
		public bool GiantBonusDrop;

		[DefaultValue(true)]
		public bool MiniBonusDrop;

		[Header("EnemySwap")]
		[DefaultValue(true)]
		public bool RandomizeEnemySpawns;

		[DefaultValue(EnemySwapMode.ByBiome)]
		[DrawTicks]
		public EnemySwapMode EnemySwapMode;

		// Per type and per spawn modes only
		[Range(0f, 100f)]
		[Increment(5f)]
		[DefaultValue(75f)]
		public float EnemySwapChance;

		// By biome: chance that a spawn stays the vanilla enemy (%)
		[Range(0f, 100f)]
		[Increment(5f)]
		[DefaultValue(0f)]
		public float BiomeVanillaChance;

		// By biome: share of the spawn places given to enemies that normally only come with an event (%)
		[Range(0f, 30f)]
		[Increment(1f)]
		[DefaultValue(8f)]
		public float BiomeExtraEnemies;

		[DefaultValue(true)]
		[ReloadRequired]
		public bool SwapCritters;

		// Invaders and moon enemies join the swaps both ways (EventProgress keeps their events counting)
		[DefaultValue(true)]
		[ReloadRequired]
		public bool SwapEventEnemies;

		// Share of the event spawns (invasions, moons, eclipse, Blood Moon) turned into another enemy (%)
		[Range(0f, 100f)]
		[Increment(5f)]
		[DefaultValue(50f)]
		public float EventSpawnSwapChance;

		[DefaultValue(true)]
		[ReloadRequired]
		public bool SwapModdedEnemies;
	}
}

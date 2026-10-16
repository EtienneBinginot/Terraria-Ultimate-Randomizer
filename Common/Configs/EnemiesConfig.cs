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
		PerSpawn
	}

	public class EnemiesConfig : RandomizerConfig
	{
		public static EnemiesConfig Instance => ModContent.GetInstance<EnemiesConfig>();

		[Header("Bosses")]
		[DefaultValue(true)]
		public bool RandomizeBossHealth;

		[Range(0.05f, 20f)]
		[Increment(0.05f)]
		[DefaultValue(0.75f)]
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
		[DefaultValue(1.5f)]
		public float BossDamageMax;

		[Header("Enemies")]
		[DefaultValue(true)]
		public bool RandomizeEnemyStats;

		[Range(0.05f, 20f)]
		[Increment(0.05f)]
		[DefaultValue(0.8f)]
		public float EnemyHealthMin;

		[Range(0.05f, 20f)]
		[Increment(0.05f)]
		[DefaultValue(1.3f)]
		public float EnemyHealthMax;

		[Range(0.05f, 20f)]
		[Increment(0.05f)]
		[DefaultValue(0.8f)]
		public float EnemyDamageMin;

		[Range(0.05f, 20f)]
		[Increment(0.05f)]
		[DefaultValue(1.3f)]
		public float EnemyDamageMax;

		[Range(0.05f, 20f)]
		[Increment(0.05f)]
		[DefaultValue(0.8f)]
		public float EnemyDefenseMin;

		[Range(0.05f, 20f)]
		[Increment(0.05f)]
		[DefaultValue(1.3f)]
		public float EnemyDefenseMax;

		[Range(0.5f, 2f)]
		[Increment(0.05f)]
		[DefaultValue(0.85f)]
		public float EnemyScaleMin;

		[Range(0.5f, 2f)]
		[Increment(0.05f)]
		[DefaultValue(1.2f)]
		public float EnemyScaleMax;

		[Header("EnemySwap")]
		[DefaultValue(true)]
		public bool RandomizeEnemySpawns;

		[DefaultValue(EnemySwapMode.PerSpawn)]
		[DrawTicks]
		public EnemySwapMode EnemySwapMode;

		[Range(0f, 100f)]
		[Increment(5f)]
		[DefaultValue(75f)]
		public float EnemySwapChance;

		[DefaultValue(true)]
		[ReloadRequired]
		public bool SwapCritters;

		[DefaultValue(false)]
		[ReloadRequired]
		public bool SwapEventEnemies;

		[DefaultValue(true)]
		[ReloadRequired]
		public bool SwapModdedEnemies;
	}
}

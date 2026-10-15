using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace UltimateRandomizer.Common.Configs
{
	public class EnemiesConfig : ModConfig
	{
		public override ConfigScope Mode => ConfigScope.ServerSide;

		public static EnemiesConfig Instance => ModContent.GetInstance<EnemiesConfig>();

		[Header("Bosses")]
		[DefaultValue(true)]
		public bool RandomizeBossHealth;

		[Range(0.05f, 20f)]
		[Increment(0.05f)]
		[DefaultValue(0.5f)]
		public float BossHealthMin;

		[Range(0.05f, 20f)]
		[Increment(0.05f)]
		[DefaultValue(3f)]
		public float BossHealthMax;

		[DefaultValue(true)]
		public bool RandomizeBossDamage;

		[Range(0.05f, 20f)]
		[Increment(0.05f)]
		[DefaultValue(0.5f)]
		public float BossDamageMin;

		[Range(0.05f, 20f)]
		[Increment(0.05f)]
		[DefaultValue(2f)]
		public float BossDamageMax;

		[Header("Enemies")]
		[DefaultValue(false)]
		public bool RandomizeEnemyStats;

		[Range(0.05f, 20f)]
		[Increment(0.05f)]
		[DefaultValue(0.75f)]
		public float EnemyHealthMin;

		[Range(0.05f, 20f)]
		[Increment(0.05f)]
		[DefaultValue(1.5f)]
		public float EnemyHealthMax;

		[Range(0.05f, 20f)]
		[Increment(0.05f)]
		[DefaultValue(0.75f)]
		public float EnemyDamageMin;

		[Range(0.05f, 20f)]
		[Increment(0.05f)]
		[DefaultValue(1.5f)]
		public float EnemyDamageMax;

		[Range(0.05f, 20f)]
		[Increment(0.05f)]
		[DefaultValue(0.75f)]
		public float EnemyDefenseMin;

		[Range(0.05f, 20f)]
		[Increment(0.05f)]
		[DefaultValue(1.5f)]
		public float EnemyDefenseMax;

		[Range(0.5f, 2f)]
		[Increment(0.05f)]
		[DefaultValue(1f)]
		public float EnemyScaleMin;

		[Range(0.5f, 2f)]
		[Increment(0.05f)]
		[DefaultValue(1f)]
		public float EnemyScaleMax;
	}
}

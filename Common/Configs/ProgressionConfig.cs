using System.Collections.Generic;
using System.ComponentModel;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Configs
{
	public class TierOverride
	{
		public ItemDefinition Item = new();

		[DrawTicks]
		public Tier Tier;

		public override bool Equals(object obj) => obj is TierOverride other && Equals(Item, other.Item) && Tier == other.Tier;

		public override int GetHashCode() => (Item?.GetHashCode() ?? 0) * 31 + (int)Tier;
	}

	public enum StationProtection
	{
		// Every crafting station keeps its vanilla recipe and is never given at random
		AllStations,
		// Only progression stations are protected; basic ones (work bench, furnace, anvil...) are found in the world
		ProgressionOnly,
		// No station is protected
		None
	}

	public class ProgressionConfig : RandomizerConfig
	{
		public static ProgressionConfig Instance => ModContent.GetInstance<ProgressionConfig>();

		// Read at spawn only: left out of the world caches' signature (SeedSystem.CacheSignature)
		public static readonly string[] AdaptiveFields =
		{
			nameof(AdaptivePower), nameof(AdaptiveGrace), nameof(LifeGrowthPerTier), nameof(DamageGrowthPerTier), nameof(BossLifeGrowthPerTier),
			nameof(BossDamageGrowthPerTier), nameof(AdaptiveCap)
		};

		[Header("TierGating")]
		[DefaultValue(true)]
		public bool TierGating;

		[Range(0, 9)]
		[DefaultValue(3)]
		[Slider]
		public int TierAllowance;

		[Range(0f, 100f)]
		[Increment(0.5f)]
		[DefaultValue(10f)]
		public float JackpotTierChance;

		[Range(0f, 100f)]
		[Increment(0.1f)]
		[DefaultValue(4f)]
		public float RuntimeJackpotChance;

		[DefaultValue(false)]
		public bool MechTierRequiresAllThree;

		[DefaultValue(false)]
		public bool ScaleWeakEnemiesWithProgression;

		// Enemies scale with the player's equipment when it gets ahead of the world (AdaptivePower)
		[Header("AdaptivePower")]
		[DefaultValue(true)]
		public bool AdaptivePower;

		[Range(0, 9)]
		[DefaultValue(1)]
		[Slider]
		public int AdaptiveGrace;

		[Range(1f, 4f)]
		[Increment(0.1f)]
		[DefaultValue(1.8f)]
		public float LifeGrowthPerTier;

		[Range(1f, 4f)]
		[Increment(0.1f)]
		[DefaultValue(1.5f)]
		public float DamageGrowthPerTier;

		[Range(1f, 4f)]
		[Increment(0.1f)]
		[DefaultValue(2f)]
		public float BossLifeGrowthPerTier;

		[Range(1f, 4f)]
		[Increment(0.1f)]
		[DefaultValue(1.6f)]
		public float BossDamageGrowthPerTier;

		[Range(1f, 50f)]
		[Increment(0.5f)]
		[DefaultValue(10f)]
		public float AdaptiveCap;

		[Header("Protection")]
		[ReloadRequired]
		public List<ItemDefinition> ProtectedItems = new()
		{
			// Boss summons, the hardmode ore chain (Pwnhammer) and keys. The ingredients of protected results that can't be
			// found in time are replaced by findable ones (or guaranteed in Vanilla mode), so their materials (souls of light and
			// night...) need no protection; event summons are optional (the Goblin army and eclipses also come by themselves)
			new(ItemID.GuideVoodooDoll), new(ItemID.Pwnhammer), new(ItemID.TempleKey), new(ItemID.LihzahrdPowerCell),
			new(ItemID.GoldenKey), new(ItemID.ShadowKey), new(ItemID.MechanicalEye), new(ItemID.MechanicalWorm),
			new(ItemID.MechanicalSkull), new(ItemID.SuspiciousLookingEye), new(ItemID.WormFood), new(ItemID.BloodySpine),
			new(ItemID.SlimeCrown), new(ItemID.Abeemination), new(ItemID.DeerThing), new(ItemID.QueenSlimeCrystal),
			new(ItemID.TruffleWorm), new(ItemID.CelestialSigil), new(ItemID.LihzahrdAltar)
		};

		[DefaultValue(true)]
		[ReloadRequired]
		public bool ProtectProgressionMaterials;

		[DefaultValue(true)]
		[ReloadRequired]
		public bool AutoProtectBossSummons;

		[DefaultValue(StationProtection.ProgressionOnly)]
		[DrawTicks]
		[ReloadRequired]
		public StationProtection StationProtection;

		// Off: pickaxe and bar recipes are randomized like the others, and the recipe planner guarantees a pickaxe for each
		// mining level instead (RecipePlanner.MiningRequirements)
		[DefaultValue(false)]
		[ReloadRequired]
		public bool ProtectPickaxeProgression;

		[Header("Guarantees")]
		// Chance multiplier of the drops placed on common enemies so that unprotected stations can be found
		[Range(0.25f, 4f)]
		[Increment(0.25f)]
		[DefaultValue(1f)]
		public float GuaranteedDropMultiplier;

		// Chance that a pot also gives one of the guaranteed items (basic stations...) already unlocked
		[Range(0f, 20f)]
		[Increment(0.5f)]
		[DefaultValue(1f)]
		public float PotGuaranteeChance;

		// Copies of each basic station hidden in wooden chests when the world is created
		[Range(0, 5)]
		[DefaultValue(1)]
		public int ChestStationGuarantee;

		// A drop counts as a way to find an item (recipe ingredients...) when its chance is at least this (%)
		[Range(0.1f, 20f)]
		[Increment(0.1f)]
		[DefaultValue(2f)]
		public float MinFindableChance;

		[Header("Tiers")]
		[ReloadRequired]
		public List<TierOverride> TierOverrides = new();
	}
}

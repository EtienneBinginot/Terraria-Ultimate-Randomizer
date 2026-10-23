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
		// Progression and themed stations are protected; basic ones (work bench, furnace, anvil...) are found in the world
		ProgressionOnly,
		// No station is protected (except modded ones): each one is guaranteed on common enemies of its tier
		None
	}

	public class ProgressionConfig : RandomizerConfig
	{
		public static ProgressionConfig Instance => ModContent.GetInstance<ProgressionConfig>();

		// Read at spawn only: left out of the world caches' signature (SeedSystem.CacheSignature)
		public static readonly string[] AdaptiveFields =
		{
			nameof(AdaptivePower), nameof(AdaptiveGrace), nameof(LifeGrowthPerTier), nameof(DamageGrowthPerTier), nameof(BossLifeGrowthPerTier),
			nameof(BossDamageGrowthPerTier), nameof(AdaptiveCap), nameof(AdaptiveDamageCap)
		};

		[Header("TierGating")]
		[DefaultValue(true)]
		public bool TierGating;

		[Range(0, 9)]
		[DefaultValue(2)]
		[Slider]
		public int TierAllowance;

		[Range(0f, 100f)]
		[Increment(0.5f)]
		[DefaultValue(10f)]
		public float JackpotTierChance;

		[Range(0f, 100f)]
		[Increment(0.1f)]
		[DefaultValue(1f)]
		public float RuntimeJackpotChance;

		// Madness: share of the random drops (at kill or bag opening) that go above the progression limit, each extra
		// tier WildTierDecay times rarer. Never used by plans, findability or guarantees. Both jackpot chances are scaled by
		// RandomNpcDropRule.JackpotWorth (weak swarms less). 1.3.0 raised the defaults to 7 % / 3 %: too many far jackpots, back
		// to 5 % / 2 % in 1.3.2; still too many early ones (and in every bag): 2 % / 1 % after 1.3.4, scaled by JackpotStartShare
		[Range(0f, 50f)]
		[Increment(0.5f)]
		[DefaultValue(2f)]
		public float WildDropChance;

		[Range(1.5f, 10f)]
		[Increment(0.5f)]
		[DefaultValue(4f)]
		public float WildTierDecay;

		// Share (%) of both jackpot chances before the first boss, rising linearly to all of them after Plantera
		// (ItemPool.JackpotShare): jackpots are rare at the start and come more often as the world progresses
		[Range(0f, 100f)]
		[Increment(5f)]
		[DefaultValue(30f)]
		public float JackpotStartShare;

		// A jackpot is never more than this many tiers above the progression limit (and never a hardmode item before
		// hardmode: ItemPool.JackpotCeiling)
		[Range(1, 9)]
		[DefaultValue(1)]
		[Slider]
		public int MaxJackpotTiers;

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
		[Increment(0.01f)]
		[DefaultValue(1.12f)]
		public float DamageGrowthPerTier;

		[Range(1f, 4f)]
		[Increment(0.1f)]
		[DefaultValue(2f)]
		public float BossLifeGrowthPerTier;

		[Range(1f, 4f)]
		[Increment(0.01f)]
		[DefaultValue(1.15f)]
		public float BossDamageGrowthPerTier;

		[Range(1f, 50f)]
		[Increment(0.5f)]
		[DefaultValue(10f)]
		public float AdaptiveCap;

		// Damage has its own, much lower cap: health makes fights longer, damage makes defense useless
		[Range(1f, 10f)]
		[Increment(0.1f)]
		[DefaultValue(1.5f)]
		public float AdaptiveDamageCap;

		[Header("Protection")]
		[ReloadRequired]
		public List<ItemDefinition> ProtectedItems = new()
		{
			// The hardmode ore chain (Pwnhammer), keys and the summons found in the world. Crafted and dropped boss summons are
			// randomized and guaranteed on common enemies (GuaranteedSources.SummonRequirements). The ingredients of protected
			// results that can't be found in time are replaced by findable ones (or guaranteed in Vanilla mode), so their
			// materials (souls of light and night...) need no protection
			new(ItemID.Pwnhammer), new(ItemID.TempleKey), new(ItemID.LihzahrdPowerCell), new(ItemID.GoldenKey), new(ItemID.ShadowKey),
			new(ItemID.QueenSlimeCrystal), new(ItemID.TruffleWorm), new(ItemID.LihzahrdAltar)
		};

		[DefaultValue(true)]
		[ReloadRequired]
		public bool ProtectProgressionMaterials;

		[DefaultValue(false)]
		[ReloadRequired]
		public bool AutoProtectBossSummons;

		[DefaultValue(StationProtection.None)]
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

		// Each guaranteed item is carried by the enemies of one biome, the most met first, up to this many (the same chance
		// on each, see GuaranteedSources.Assign)
		[Range(2, 12)]
		[DefaultValue(6)]
		public int GuaranteedHostsPerItem;

		// Kills in its biome to find a guaranteed station, summon or tool on average, all its enemies together
		[Range(20, 300)]
		[DefaultValue(60)]
		public int StationFindKills;

		// Chance that a pot also gives one of the guaranteed items (basic stations...) already unlocked and not found yet
		[Range(0f, 20f)]
		[Increment(0.1f)]
		[DefaultValue(0.3f)]
		public float PotGuaranteeChance;

		// Multiplayer: a guaranteed station, summon or tool drops for the whole team, so its chance per kill is divided by
		// the number of players (more players kill more enemies)
		[DefaultValue(true)]
		public bool ScaleGuaranteesWithPlayers;

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

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Newtonsoft.Json;
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

		// 1 since 1.3.5 (2 before): items two tiers ahead came too early. Before hardmode, the limit never goes past
		// PostSkeletron whatever the allowance (ItemPool.GatingLimit): only a jackpot gives a hardmode item there
		[Range(0, 9)]
		[DefaultValue(1)]
		[Slider]
		public int TierAllowance;

		[Range(0f, 100f)]
		[Increment(0.5f)]
		[DefaultValue(10f)]
		public float JackpotTierChance;

		[Range(0f, 100f)]
		[Increment(0.1f)]
		[DefaultValue(2f)]
		public float RuntimeJackpotChance;

		// Madness: share of the random drops (at kill or bag opening) that go above the progression limit, each extra
		// tier WildTierDecay times rarer (lucky drops too). Never used by plans, findability or guarantees. Both jackpot chances
		// are scaled by RandomNpcDropRule.JackpotWorth (weak swarms less). 1.3.0 raised the defaults to 7 % / 3 %: too many far
		// jackpots, back to 5 % / 2 % in 1.3.2; 2 % / 1 % in 1.3.4 with a share early on and at most one tier above the limit;
		// after 1.3.4 back to 5 % / 2 % at every tier, with no cap: each extra tier WildTierDecay (3) times rarer. A jackpot is
		// the only way to get a hardmode item before hardmode. The permutation drop style (default since 0.13.x) left the
		// madness only two sparse extra slots per enemy: jackpots fell from about 1.9 to 0.4 per early hour (0.75 with giants
		// and minis). 0.20.0 adds a per-kill roll of its own (KillJackpotChance) that works in every drop style, and
		// WildTierDecay goes from 3 to 4 so that more jackpots do not bring back far hardmode items
		[Range(0f, 50f)]
		[Increment(0.5f)]
		[DefaultValue(5f)]
		public float WildDropChance;

		// At tier 0 (limit 1), decay 4: of the madness jackpots, about 75 % are one tier above, 19 % two, 4.7 % three (the
		// first hardmode tier), 1.2 % four, 0.4 % more. Lucky items are kept 4^-(gap-1) of the time (ItemPool.LuckyKeepChance)
		[Range(1.5f, 10f)]
		[Increment(0.5f)]
		[DefaultValue(4f)]
		public float WildTierDecay;

		// Per-kill jackpot (%): on top of the drops, whatever the drop style, one useful item above the progression limit
		// (the madness tail: WildTierDecay), scaled by RandomNpcDropRule.JackpotWorth. Runtime only, never planned. 0.20.0 had 1.2 %:
		// 2.71 jackpots per early hour measured (1.51 from this roll, PacingModel.JackpotsPer100Kills); 0.8 % gives about 2.2
		[Range(0f, 20f)]
		[Increment(0.1f)]
		[DefaultValue(0.8f)]
		public float KillJackpotChance;

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
		// Empty by default: boss summons (crafted, dropped or found in the world), the Pwnhammer and the Temple Key are
		// randomized and guaranteed on common enemies of their tier (GuaranteedSources), the keys move with the drop
		// permutation, the Lihzahrd Altar is never given (ItemClassifier.BannedVanilla)
		public List<ItemDefinition> ProtectedItems = new();

		// Off by default: the materials are randomized, recipes give each crafted item a key of its tier, and hard gates
		// (Luminite, mechanical souls, Ectoplasm...) never move below their true tier
		[DefaultValue(false)]
		[ReloadRequired]
		public bool ProtectProgressionMaterials;

		[DefaultValue(false)]
		[ReloadRequired]
		public bool AutoProtectBossSummons;

		[DefaultValue(StationProtection.None)]
		[DrawTicks]
		[ReloadRequired]
		public StationProtection StationProtection;

		[Header("Guarantees")]
		// Chance multiplier of the drops placed on common enemies so that unprotected summons and tools can be found (not the
		// station guardians: StationGuardianChance)
		[Range(0.25f, 4f)]
		[Increment(0.25f)]
		[DefaultValue(1f)]
		public float GuaranteedDropMultiplier;

		// With randomized recipes, each crafting station is made from its recipe, or dropped by its single guardian enemy with
		// this chance per kill (%), a tenth of it once the station has been obtained in the world (GuaranteedSources)
		[Range(1f, 100f)]
		[Increment(0.5f)]
		[DefaultValue(12.5f)]
		public float StationGuardianChance;

		// Each guaranteed item is carried by the enemies of one biome, the most met first, up to this many (the same chance
		// on each, see GuaranteedSources.Assign)
		[Range(2, 12)]
		[DefaultValue(6)]
		public int GuaranteedHostsPerItem;

		// Kills in its biome to find a guaranteed summon or tool on average, all its enemies together
		[Range(20, 300)]
		[DefaultValue(60)]
		public int StationFindKills;

		// Chance that a pot also gives one of the guaranteed items (pickaxes, early materials) already unlocked and not found yet
		[Range(0f, 20f)]
		[Increment(0.1f)]
		[DefaultValue(0.3f)]
		public float PotGuaranteeChance;

		// Multiplayer: a guaranteed station, summon or tool drops for the whole team, so its chance per kill is divided by
		// the number of players (more players kill more enemies)
		[DefaultValue(true)]
		public bool ScaleGuaranteesWithPlayers;

		// A drop counts as a way to find an item (recipe ingredients...) when its chance is at least this (%)
		[Range(0.1f, 20f)]
		[Increment(0.1f)]
		[DefaultValue(2f)]
		public float MinFindableChance;

		[Header("Tiers")]
		[ReloadRequired]
		public List<TierOverride> TierOverrides = new();

		// Saved settings migration (Migrate). Not public: hidden from the config menu, still saved by tML's serializer
		// (Newtonsoft keeps non-public members marked JsonProperty). 0 in files saved before the migration existed
		public const int CurrentVersion = 1;

		[JsonProperty]
		internal int ConfigVersion;

		// The defaults ProtectedItems had before 06c5f48 (newest first). tML only saves a value that differs from its default
		// (DefaultValueHandling.IgnoreAndPopulate, ReferenceDefaultsPreservingResolver), so an untouched list already loads
		// as the new empty default; a list the player added items to was saved whole, old defaults included
		public static readonly int[][] OldProtectedDefaults =
		{
			new int[]
			{
				ItemID.Pwnhammer, ItemID.TempleKey, ItemID.LihzahrdPowerCell, ItemID.GoldenKey, ItemID.ShadowKey, ItemID.QueenSlimeCrystal, ItemID.TruffleWorm,
				ItemID.LihzahrdAltar
			},
			new int[]
			{
				ItemID.GuideVoodooDoll, ItemID.Pwnhammer, ItemID.TempleKey, ItemID.LihzahrdPowerCell, ItemID.GoldenKey, ItemID.ShadowKey, ItemID.MechanicalEye,
				ItemID.MechanicalWorm, ItemID.MechanicalSkull, ItemID.SuspiciousLookingEye, ItemID.WormFood, ItemID.BloodySpine, ItemID.SlimeCrown,
				ItemID.Abeemination, ItemID.DeerThing, ItemID.QueenSlimeCrystal, ItemID.TruffleWorm, ItemID.CelestialSigil, ItemID.LihzahrdAltar
			},
			new int[]
			{
				ItemID.GuideVoodooDoll, ItemID.ClothierVoodooDoll, ItemID.Pwnhammer, ItemID.TempleKey, ItemID.LihzahrdPowerCell, ItemID.SoulofLight,
				ItemID.SoulofNight, ItemID.GoldenKey, ItemID.ShadowKey, ItemID.MechanicalEye, ItemID.MechanicalWorm, ItemID.MechanicalSkull,
				ItemID.SuspiciousLookingEye, ItemID.WormFood, ItemID.BloodySpine, ItemID.SlimeCrown, ItemID.Abeemination, ItemID.DeerThing,
				ItemID.QueenSlimeCrystal, ItemID.TruffleWorm, ItemID.CelestialSigil, ItemID.GoblinBattleStandard, ItemID.PirateMap, ItemID.SnowGlobe,
				ItemID.PumpkinMoonMedallion, ItemID.NaughtyPresent, ItemID.SolarTablet, ItemID.LihzahrdAltar
			}
		};

		// Runs on every machine when the mod loads (a client joining a server gets the server's migrated values, version
		// included). Applied in memory: the file is rewritten the next time the player saves the settings, and until then
		// the migration (idempotent) runs again at each load
		public override void OnLoaded() => Migrate(this);

		// Pure: brings a config loaded from an older file to the current defaults, never touching what the player chose.
		// Version 1: a protected items list still holding every item of an old default list (the player added items to it)
		// loses those old defaults (randomized and guaranteed now: Protection), keeping the additions. The other defaults
		// changed then (ProtectProgressionMaterials, LootConfig.DropTableStyle) need nothing: a value equal to its old default
		// was never saved, a saved one is the player's choice. True when something changed
		public static bool Migrate(ProgressionConfig cfg)
		{
			if (cfg.ConfigVersion >= CurrentVersion) return false;
			bool changed = false;
			if (cfg.ProtectedItems != null)
			{
				HashSet<int> listed = cfg.ProtectedItems.Where(d => d != null && !d.IsUnloaded).Select(d => d.Type).ToHashSet();
				// The largest old list held whole
				int[] old = OldProtectedDefaults.Where(list => list.All(listed.Contains)).OrderByDescending(list => list.Length).FirstOrDefault();
				if (old != null)
				{
					changed = cfg.ProtectedItems.RemoveAll(d => d != null && !d.IsUnloaded && Array.IndexOf(old, d.Type) >= 0) > 0;
				}
			}
			cfg.ConfigVersion = CurrentVersion;
			return changed;
		}
	}
}

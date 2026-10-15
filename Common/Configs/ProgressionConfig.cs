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

	public class ProgressionConfig : ModConfig
	{
		public override ConfigScope Mode => ConfigScope.ServerSide;

		public static ProgressionConfig Instance => ModContent.GetInstance<ProgressionConfig>();

		[Header("TierGating")]
		[DefaultValue(true)]
		public bool TierGating;

		[Range(0, 9)]
		[DefaultValue(1)]
		[Slider]
		public int TierAllowance;

		[Range(0f, 100f)]
		[Increment(0.5f)]
		[DefaultValue(2f)]
		public float JackpotTierChance;

		[Range(0f, 100f)]
		[Increment(0.1f)]
		[DefaultValue(0.5f)]
		public float RuntimeJackpotChance;

		[DefaultValue(false)]
		public bool MechTierRequiresAllThree;

		[DefaultValue(false)]
		public bool ScaleWeakEnemiesWithProgression;

		[Header("Protection")]
		[ReloadRequired]
		public List<ItemDefinition> ProtectedItems = new()
		{
			new(ItemID.GuideVoodooDoll), new(ItemID.ClothierVoodooDoll), new(ItemID.Pwnhammer), new(ItemID.TempleKey),
			new(ItemID.LihzahrdPowerCell), new(ItemID.SoulofLight), new(ItemID.SoulofNight), new(ItemID.GoldenKey),
			new(ItemID.ShadowKey), new(ItemID.MechanicalEye), new(ItemID.MechanicalWorm), new(ItemID.MechanicalSkull),
			new(ItemID.SuspiciousLookingEye), new(ItemID.WormFood), new(ItemID.BloodySpine), new(ItemID.SlimeCrown),
			new(ItemID.Abeemination), new(ItemID.DeerThing), new(ItemID.QueenSlimeCrystal), new(ItemID.TruffleWorm),
			new(ItemID.CelestialSigil), new(ItemID.GoblinBattleStandard), new(ItemID.PirateMap), new(ItemID.SnowGlobe),
			new(ItemID.PumpkinMoonMedallion), new(ItemID.NaughtyPresent), new(ItemID.SolarTablet), new(ItemID.LihzahrdAltar)
		};

		[DefaultValue(true)]
		[ReloadRequired]
		public bool AutoProtectBossSummons;

		[DefaultValue(true)]
		[ReloadRequired]
		public bool ProtectCraftingStations;

		[DefaultValue(true)]
		[ReloadRequired]
		public bool ProtectPickaxeProgression;

		[Header("Tiers")]
		[ReloadRequired]
		public List<TierOverride> TierOverrides = new();
	}
}

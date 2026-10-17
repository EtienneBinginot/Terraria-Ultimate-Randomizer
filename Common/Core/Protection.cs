using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using UltimateRandomizer.Common.Configs;

namespace UltimateRandomizer.Common.Core
{
	// Items required for progression: never replaced, and never given as a random replacement
	public static class Protection
	{
		// Materials needed for the pickaxe and progression chain, and biome keys
		private static readonly int[] ProgressionMaterials =
		{
			ItemID.ShadowScale, ItemID.TissueSample, ItemID.HallowedBar, ItemID.SoulofMight, ItemID.SoulofSight, ItemID.SoulofFright,
			ItemID.FragmentSolar, ItemID.FragmentVortex, ItemID.FragmentNebula, ItemID.FragmentStardust, ItemID.LunarOre, ItemID.Ectoplasm,
			ItemID.JungleKey, ItemID.CorruptionKey, ItemID.CrimsonKey, ItemID.HallowedKey, ItemID.FrozenKey, ItemID.DungeonDesertKey,
			ItemID.LightKey, ItemID.NightKey
		};

		// Boss summons found in the world rather than crafted or dropped: always protected (nothing would guarantee them)
		public static readonly int[] WorldSummons = { ItemID.TruffleWorm, ItemID.EmpressButterfly, ItemID.LihzahrdPowerCell, ItemID.QueenSlimeCrystal };

		// Sorted with the boss summons (SortingPriorityBossSpawns) without summoning anything: life and mana crystals,
		// mirrors, phones, conches. They stay protected like before, whatever AutoProtectBossSummons says
		private static readonly HashSet<int> SortedAsSummons = new()
		{
			ItemID.LifeCrystal, ItemID.LifeFruit, ItemID.ManaCrystal, ItemID.MagicMirror, ItemID.IceMirror, ItemID.CellPhone, ItemID.Shellphone,
			ItemID.ShellphoneDummy, ItemID.ShellphoneSpawn, ItemID.ShellphoneOcean, ItemID.ShellphoneHell, ItemID.MagicConch, ItemID.DemonConch
		};

		// Items that summon a boss or an event (the Guide Voodoo Doll is not in the vanilla set)
		public static bool IsBossSummon(int type) => type == ItemID.GuideVoodooDoll
			|| type > ItemID.None && type < ItemID.Sets.SortingPriorityBossSpawns.Length && ItemID.Sets.SortingPriorityBossSpawns[type] >= 0 && !SortedAsSummons.Contains(type);

		// The boss each vanilla summon calls
		private static readonly Dictionary<int, int> SummonedBoss = new()
		{
			[ItemID.SlimeCrown] = NPCID.KingSlime, [ItemID.SuspiciousLookingEye] = NPCID.EyeofCthulhu, [ItemID.WormFood] = NPCID.EaterofWorldsHead,
			[ItemID.BloodySpine] = NPCID.BrainofCthulhu, [ItemID.Abeemination] = NPCID.QueenBee, [ItemID.DeerThing] = NPCID.Deerclops,
			[ItemID.GuideVoodooDoll] = NPCID.WallofFlesh, [ItemID.QueenSlimeCrystal] = NPCID.QueenSlimeBoss, [ItemID.MechanicalEye] = NPCID.Retinazer,
			[ItemID.MechanicalWorm] = NPCID.TheDestroyer, [ItemID.MechanicalSkull] = NPCID.SkeletronPrime, [ItemID.MechdusaSummon] = NPCID.Retinazer,
			[ItemID.TruffleWorm] = NPCID.DukeFishron, [ItemID.LihzahrdPowerCell] = NPCID.Golem, [ItemID.EmpressButterfly] = NPCID.HallowBoss,
			[ItemID.CelestialSigil] = NPCID.MoonLordCore
		};

		// The tier a summon is needed at: its own tier, but never later than the tier before the one its boss opens (the
		// Suspicious Looking Eye is needed before the Eye of Cthulhu is beaten)
		public static int SummonTier(int item)
		{
			int tier = ItemPool.TierOf(item);
			if (SummonedBoss.TryGetValue(item, out int boss)) tier = Math.Min(tier, NpcTiers.BaseTier(boss) - 1);
			return Tiers.Clamp(tier);
		}

		public static HashSet<int> BuildProtectedSet()
		{
			ProgressionConfig cfg = ProgressionConfig.Instance;
			HashSet<int> set = new();
			foreach (ItemDefinition def in cfg.ProtectedItems)
			{
				if (def != null && !def.IsUnloaded && def.Type > ItemID.None) set.Add(def.Type);
			}
			if (cfg.ProtectProgressionMaterials) set.UnionWith(ProgressionMaterials);
			int count = ItemLoader.ItemCount;
			// Off: crafted and dropped summons are randomized, with guaranteed drops (GuaranteedSources.SummonRequirements)
			set.UnionWith(WorldSummons);
			set.UnionWith(SortedAsSummons);
			if (cfg.AutoProtectBossSummons)
			{
				for (int type = 1; type < count; type++)
				{
					if (IsBossSummon(type)) set.Add(type);
				}
			}
			{
				// Any item placing a tile that some recipe requires (work bench, anvils, furnaces...). Unprotected stations,
				// progression ones included, get guaranteed drops (GuaranteedSources); modded and unknown stations keep their
				// own sources: nothing would guarantee them
				HashSet<int> stationTiles = Stations.RequiredTiles();
				foreach ((int type, Item item) in ContentSamples.ItemsByType)
				{
					if (item.createTile < 0 || !stationTiles.Contains(item.createTile)) continue;
					StationKind kind = Stations.Info(item.createTile).Kind;
					bool keep = kind == StationKind.Unknown || cfg.StationProtection switch
					{
						StationProtection.AllStations => true,
						StationProtection.ProgressionOnly => kind != StationKind.Basic,
						_ => false
					};
					if (keep) set.Add(type);
				}
			}
			return set;
		}
	}
}

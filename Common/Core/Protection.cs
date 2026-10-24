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
		// Materials needed for the pickaxe and progression chain, and biome keys: protected only when ProtectProgressionMaterials
		// is on (off by default: the recipes give each crafted item a key of its tier, hard gates never move below their true
		// tier, and the drop and boss permutations keep every content dropping somewhere)
		private static readonly int[] ProgressionMaterials =
		{
			ItemID.ShadowScale, ItemID.TissueSample, ItemID.HallowedBar, ItemID.SoulofMight, ItemID.SoulofSight, ItemID.SoulofFright,
			ItemID.FragmentSolar, ItemID.FragmentVortex, ItemID.FragmentNebula, ItemID.FragmentStardust, ItemID.LunarOre, ItemID.Ectoplasm,
			ItemID.JungleKey, ItemID.CorruptionKey, ItemID.CrimsonKey, ItemID.HallowedKey, ItemID.FrozenKey, ItemID.DungeonDesertKey,
			ItemID.LightKey, ItemID.NightKey
		};

		// Boss summons found in the world rather than crafted or dropped (caught critters, a crystal growing in the Hallow, the
		// Temple's chests): randomized like the other summons and guaranteed on enemies of their tier as items
		// (GuaranteedSources.SummonRequirements); the critters can still be caught and the crystal broken as in vanilla
		public static readonly int[] WorldSummons = { ItemID.TruffleWorm, ItemID.EmpressButterfly, ItemID.LihzahrdPowerCell, ItemID.QueenSlimeCrystal };

		// Progression tools a boss or a randomized container gives in vanilla (the Pwnhammer for the altars, the Shadow Key for
		// the Underworld's Shadow Chests, found in the Dungeon's locked chests and Golden Lock Boxes, the Temple Key for the
		// Jungle Temple's door), at the tier they are needed from: randomized, and guaranteed on the enemies of that tier
		// (GuaranteedSources.ProgressionToolRequirements). The other openers keep a source of their own: the Golden Key moves
		// with the Dungeon enemies' drops, the biome keys are global drops (left running), the Light and Night Keys are crafted
		public static readonly (int item, Tier tier)[] ProgressionTools =
		{
			(ItemID.Pwnhammer, Tier.Hardmode), (ItemID.ShadowKey, Tier.PostSkeletron), (ItemID.TempleKey, Tier.PostPlantera)
		};

		// Sorted with the boss summons (SortingPriorityBossSpawns) without summoning anything: life and mana crystals,
		// mirrors, phones, conches. Not summons (never guaranteed as one, nor protected by AutoProtectBossSummons); the
		// mirrors, phones and conches are randomized like any item (the Shellphone's other modes are banned: ItemClassifier)
		private static readonly HashSet<int> SortedAsSummons = new()
		{
			ItemID.LifeCrystal, ItemID.LifeFruit, ItemID.ManaCrystal, ItemID.MagicMirror, ItemID.IceMirror, ItemID.CellPhone, ItemID.Shellphone,
			ItemID.ShellphoneDummy, ItemID.ShellphoneSpawn, ItemID.ShellphoneOcean, ItemID.ShellphoneHell, ItemID.MagicConch, ItemID.DemonConch
		};

		// Life and mana upgrades: replaceable (the Mana Crystal's vanilla recipe is randomized) but never in a roll's buckets,
		// never guaranteed on enemies; each one is the result of one random recipe of its own (RecipePlanner.SpecialRecipe), and the
		// drops and bags that give them keep them (ItemPool.IsSpecial)
		public static readonly int[] Upgrades = { ItemID.LifeCrystal, ItemID.ManaCrystal, ItemID.LifeFruit };

		public static bool IsUpgrade(int type) => Array.IndexOf(Upgrades, type) >= 0;

		// The tier an upgrade's recipe is doable from: the Life Fruit after a mechanical boss, like its vanilla jungle plants
		public static int UpgradeTier(int type) => type == ItemID.LifeFruit ? (int)Tier.PostMechBoss : (int)Tier.PreBoss;

		// Items that summon a boss or an event (the Guide Voodoo Doll and the Prismatic Lacewing are not in the vanilla set)
		public static bool IsBossSummon(int type) => type is ItemID.GuideVoodooDoll or ItemID.EmpressButterfly
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

		// Event summons (and the Guide Voodoo Doll, useless before the Underworld) are needed once vanilla makes their event
		// or boss relevant, not from the start: the classifier puts the Pirate Map or the Snow Globe at tier 0, and their
		// guaranteed drops then filled the early hosts the basic stations need
		private static readonly Dictionary<int, Tier> NeededFrom = new()
		{
			[ItemID.GoblinBattleStandard] = Tier.PostEyeOfCthulhu, [ItemID.BloodMoonStarter] = Tier.PostEyeOfCthulhu,
			[ItemID.GuideVoodooDoll] = Tier.PostEvilBoss, [ItemID.PirateMap] = Tier.Hardmode, [ItemID.SnowGlobe] = Tier.Hardmode,
			[ItemID.SolarTablet] = Tier.PostMechBoss, [ItemID.PumpkinMoonMedallion] = Tier.PostPlantera, [ItemID.NaughtyPresent] = Tier.PostPlantera
		};

		// The tier a summon is needed at. A boss summon: the tier before the one its boss opens, when that boss is the next
		// step (the Suspicious Looking Eye from the start, the Lihzahrd Power Cell after Plantera), whatever its rarity: a
		// cheap-looking summon was offered by the Guide long before its boss. Other summons: their own tier, not before their
		// event matters (NeededFrom, which also keeps the Guide Voodoo Doll until the Underworld is in reach)
		public static int SummonTier(int item)
		{
			int tier = SummonedBoss.TryGetValue(item, out int boss) ? NpcTiers.BaseTier(boss) - 1 : ItemPool.TierOf(item);
			if (NeededFrom.TryGetValue(item, out Tier from)) tier = Math.Max(tier, (int)from);
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
			// Off: vanilla summons (crafted, dropped or found in the world) are randomized, with guaranteed drops
			// (GuaranteedSources.SummonRequirements); modded ones keep their own sources (their recipes and drops may hold rules
			// the planners don't know)
			for (int type = 1; type < count; type++)
			{
				if (IsBossSummon(type) && (cfg.AutoProtectBossSummons || type >= ItemID.Count)) set.Add(type);
			}
			{
				// Any item placing a tile that some recipe requires (work bench, anvils, furnaces...). Unprotected stations,
				// progression ones included, get guaranteed drops (GuaranteedSources); unknown vanilla stations keep their own
				// sources: nothing would guarantee them. Other mods' stations: a progression-like one (many recipes need it,
				// Stations.ModdedInfo) keeps its own sources, a themed one is guaranteed like the vanilla themed stations
				HashSet<int> stationTiles = Stations.RequiredTiles();
				foreach ((int type, Item item) in ContentSamples.ItemsByType)
				{
					if (item.createTile < 0 || !stationTiles.Contains(item.createTile)) continue;
					Stations.StationInfo info = Stations.Info(item.createTile);
					StationKind kind = info.Kind;
					bool keep = kind == StationKind.Unknown || info.Modded && kind == StationKind.Progression || cfg.StationProtection switch
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

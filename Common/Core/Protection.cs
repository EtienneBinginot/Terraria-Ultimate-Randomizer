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
			if (cfg.AutoProtectBossSummons)
			{
				for (int type = 1; type < count; type++)
				{
					if (ItemID.Sets.SortingPriorityBossSpawns[type] >= 0) set.Add(type);
				}
			}
			if (cfg.StationProtection != StationProtection.None)
			{
				// Any item placing a tile that some recipe requires (work bench, anvils, furnaces...); basic stations
				// are left to the randomizer when only progression stations are protected
				HashSet<int> stationTiles = Stations.RequiredTiles();
				foreach ((int type, Item item) in ContentSamples.ItemsByType)
				{
					if (item.createTile < 0 || !stationTiles.Contains(item.createTile)) continue;
					if (cfg.StationProtection == StationProtection.ProgressionOnly && Stations.Info(item.createTile).Kind == StationKind.Basic) continue;
					set.Add(type);
				}
			}
			return set;
		}
	}
}

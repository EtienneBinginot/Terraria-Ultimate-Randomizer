using System.Collections.Generic;
using Terraria;
using Terraria.ID;

namespace UltimateRandomizer.Common.Core
{
	public enum StationKind
	{
		// Everyday stations: found in the world (guaranteed sources), may be required by any recipe
		Basic,
		// Needed to progress (Hellforge, hardmode anvils and forges...): always protected
		Progression,
		// Themed furniture stations (Glass Kiln, Bone Welder...): protected, never given to other recipes
		Thematic,
		// Modded or unknown stations: protected
		Unknown
	}

	// Crafting stations, by the tile they place
	public static class Stations
	{
		public struct StationInfo
		{
			public StationKind Kind;
			public int Tier;
			// The item that places it, guaranteed somewhere in the world when the station is not protected
			public int Item;
			// Needed for almost everything: found sooner
			public bool Critical;
		}

		private static readonly Dictionary<int, StationInfo> Known = new()
		{
			[TileID.WorkBenches] = new StationInfo { Kind = StationKind.Basic, Item = ItemID.WorkBench, Critical = true },
			[TileID.Furnaces] = new StationInfo { Kind = StationKind.Basic, Item = ItemID.Furnace, Critical = true },
			[TileID.Anvils] = new StationInfo { Kind = StationKind.Basic, Item = ItemID.IronAnvil, Critical = true },
			[TileID.Tables] = new StationInfo { Kind = StationKind.Basic, Item = ItemID.WoodenTable, Critical = true },
			[TileID.Chairs] = new StationInfo { Kind = StationKind.Basic, Item = ItemID.WoodenChair, Critical = true },
			[TileID.Sawmill] = new StationInfo { Kind = StationKind.Basic, Item = ItemID.Sawmill },
			[TileID.Loom] = new StationInfo { Kind = StationKind.Basic, Item = ItemID.Loom },
			[TileID.Bottles] = new StationInfo { Kind = StationKind.Basic, Item = ItemID.Bottle },
			[TileID.Sinks] = new StationInfo { Kind = StationKind.Basic, Item = ItemID.WoodenSink },
			[TileID.Kegs] = new StationInfo { Kind = StationKind.Basic, Item = ItemID.Keg },
			[TileID.CookingPots] = new StationInfo { Kind = StationKind.Basic, Item = ItemID.CookingPot },
			[TileID.HeavyWorkBench] = new StationInfo { Kind = StationKind.Basic, Item = ItemID.HeavyWorkBench },
			[TileID.Bookcases] = new StationInfo { Kind = StationKind.Basic, Item = ItemID.Bookcase },

			[TileID.TinkerersWorkbench] = new StationInfo { Kind = StationKind.Progression, Tier = 1 },
			[TileID.Hellforge] = new StationInfo { Kind = StationKind.Progression, Tier = 2 },
			[TileID.ImbuingStation] = new StationInfo { Kind = StationKind.Progression, Tier = 3 },
			[TileID.AlchemyTable] = new StationInfo { Kind = StationKind.Progression, Tier = 3 },
			[TileID.MythrilAnvil] = new StationInfo { Kind = StationKind.Progression, Tier = 4 },
			[TileID.AdamantiteForge] = new StationInfo { Kind = StationKind.Progression, Tier = 4 },
			[TileID.CrystalBall] = new StationInfo { Kind = StationKind.Progression, Tier = 4 },
			[TileID.Autohammer] = new StationInfo { Kind = StationKind.Progression, Tier = 6 },
			[TileID.LihzahrdFurnace] = new StationInfo { Kind = StationKind.Progression, Tier = 7 },
			[TileID.LunarCraftingStation] = new StationInfo { Kind = StationKind.Progression, Tier = 8 },
			[TileID.DemonAltar] = new StationInfo { Kind = StationKind.Progression, Tier = 0 },

			[TileID.GlassKiln] = new StationInfo { Kind = StationKind.Thematic },
			[TileID.BoneWelder] = new StationInfo { Kind = StationKind.Thematic },
			[TileID.LivingLoom] = new StationInfo { Kind = StationKind.Thematic },
			[TileID.IceMachine] = new StationInfo { Kind = StationKind.Thematic },
			[TileID.SkyMill] = new StationInfo { Kind = StationKind.Thematic },
			[TileID.HoneyDispenser] = new StationInfo { Kind = StationKind.Thematic },
			[TileID.Solidifier] = new StationInfo { Kind = StationKind.Thematic },
			[TileID.Blendomatic] = new StationInfo { Kind = StationKind.Thematic },
			[TileID.MeatGrinder] = new StationInfo { Kind = StationKind.Thematic },
			[TileID.LesionStation] = new StationInfo { Kind = StationKind.Thematic },
			[TileID.FleshCloningVat] = new StationInfo { Kind = StationKind.Thematic },
			[TileID.SteampunkBoiler] = new StationInfo { Kind = StationKind.Thematic },
			[TileID.DyeVat] = new StationInfo { Kind = StationKind.Thematic },
			[TileID.Tables2] = new StationInfo { Kind = StationKind.Thematic }
		};

		public static StationInfo Info(int tile) =>
			Known.TryGetValue(tile, out StationInfo info) ? info : new StationInfo { Kind = StationKind.Unknown, Tier = Tiers.Max };

		// Every tile some recipe requires
		public static HashSet<int> RequiredTiles()
		{
			HashSet<int> tiles = new();
			for (int i = 0; i < Recipe.numRecipes; i++)
			{
				foreach (int tile in Main.recipe[i].requiredTile)
				{
					if (tile >= 0) tiles.Add(tile);
				}
			}
			return tiles;
		}

		// Basic stations: their tile and the item guaranteed in the world, in a fixed order
		public static IEnumerable<(int tile, StationInfo info)> Basic()
		{
			foreach ((int tile, StationInfo info) in Known)
			{
				if (info.Kind == StationKind.Basic) yield return (tile, info);
			}
		}

		// The tile an item places, if any
		public static bool PlacedTile(int item, out int tile)
		{
			tile = -1;
			if (!ContentSamples.ItemsByType.TryGetValue(item, out Item sample) || sample.createTile < TileID.Dirt) return false;
			tile = sample.createTile;
			return true;
		}
	}
}

using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace UltimateRandomizer.Common.Core
{
	public enum StationKind
	{
		// Everyday stations: found in the world (guaranteed sources), may be required by any recipe
		Basic,
		// Needed to progress (Hellforge, hardmode anvils and forges...): guaranteed on enemies of their tier unless protected
		Progression,
		// Themed furniture stations (Glass Kiln, Bone Welder...): never given to other recipes; guaranteed unless protected.
		// Also the stations of other mods that few recipes need
		Thematic,
		// Vanilla stations this catalog does not know: protected
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
			// Stands in the world and has no item (Demon Altar): never guaranteed, rolled or given to other recipes
			public bool World;
			// Another mod's tile (ModdedInfo)
			public bool Modded;
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
			[TileID.DemonAltar] = new StationInfo { Kind = StationKind.Progression, Tier = 0, World = true },

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

		public static StationInfo Info(int tile)
		{
			if (Known.TryGetValue(tile, out StationInfo info)) return info;
			return IsModded(tile) ? ModdedInfo(tile) : new StationInfo { Kind = StationKind.Unknown, Tier = Tiers.Max };
		}

		public static bool IsModded(int tile) => tile >= TileID.Count;

		// A modded station required by at least this many recipes is a progression one (a mod's anvil or forge), any other a
		// themed one
		public const int ModdedProgressionRecipes = 10;

		private static readonly Dictionary<int, StationInfo> moddedInfo = new();
		private static Dictionary<int, int> recipeCounts;

		// Another mod's station (best effort: nothing says what a modded tile is for): a progression station when many recipes
		// need it, else a themed one; its tier is its placing items' earliest true tier, known once the item pool is built
		// (Tiers.Max before: the tiers from recipes read a modded station by its placers, ItemClassifier.StationTier). A
		// progression one may be given to recipes of its tier or above (RecipePlanner.Assignable), a themed one to recipes of
		// its tier or later (RecipePlanner.Offered); both are station quests like the vanilla ones when unprotected
		// (Protection, GuaranteedSources.StationItem).
		// Load-time facts only (original recipes, content samples, item tiers): the same on every machine
		private static StationInfo ModdedInfo(int tile)
		{
			if (moddedInfo.TryGetValue(tile, out StationInfo known)) return known;
			List<int> placers = Placers(tile);
			StationInfo info = new()
			{
				Kind = RecipesRequiring(tile) >= ModdedProgressionRecipes ? StationKind.Progression : StationKind.Thematic,
				Tier = Tiers.Max,
				World = placers.Count == 0,
				Modded = true
			};
			if (!ItemPool.Ready) return info;
			List<int> tiers = placers.Where(ItemPool.IsValid).Select(ItemPool.TrueTierOf).ToList();
			if (tiers.Count > 0) info.Tier = Tiers.Clamp(tiers.Min());
			return moddedInfo[tile] = info;
		}

		// How many enabled recipes originally require a tile
		private static int RecipesRequiring(int tile)
		{
			if (recipeCounts == null)
			{
				Dictionary<int, int> counts = new();
				int stored = Features.RecipeSystem.RecipeCount;
				for (int i = 0; i < Recipe.numRecipes; i++)
				{
					if (Features.RecipeSystem.VanillaDisabled(i)) continue;
					IEnumerable<int> required = i < stored ? Features.RecipeSystem.OriginalTiles(i) : Main.recipe[i].requiredTile;
					foreach (int t in required.Distinct())
					{
						if (t >= 0) counts[t] = counts.GetValueOrDefault(t) + 1;
					}
				}
				recipeCounts = counts;
			}
			return recipeCounts.GetValueOrDefault(tile);
		}

		// Cleared when the item pool is (re)built: the modded stations' tiers follow it
		public static void Reset()
		{
			moddedInfo.Clear();
			recipeCounts = null;
		}

		// A stable name for a tile (sorting keys): vanilla by its TileID name, modded by its full name
		public static string Name(int tile) => !IsModded(tile) ? TileID.Search.GetName(tile) : TileLoader.GetTile(tile)?.FullName ?? tile.ToString();

		// Every tile some recipe originally requires (the live recipes may already be randomized: reading them would make
		// the result depend on the previous randomization, and differ on a client that just joined)
		public static HashSet<int> RequiredTiles()
		{
			HashSet<int> tiles = new();
			int stored = Features.RecipeSystem.RecipeCount;
			for (int i = 0; i < Recipe.numRecipes; i++)
			{
				IEnumerable<int> required = i < stored ? Features.RecipeSystem.OriginalTiles(i) : Main.recipe[i].requiredTile;
				foreach (int tile in required)
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

		private static Dictionary<int, List<int>> placers;
		// Tiles some item places itself (createTile), not only through another mod's tile acting as them
		private static HashSet<int> placedDirectly;

		// Items placing each tile (content samples are fixed once loaded), and items placing another mod's tile that acts as
		// this vanilla one (ActsAs: Magic Storage's combined stations count as a Hellforge, a Tinkerer's Workshop...). Do not
		// modify the lists
		public static Dictionary<int, List<int>> PlacersByTile()
		{
			if (placers != null) return placers;
			Dictionary<int, List<int>> result = new();
			HashSet<int> direct = new();
			void Add(int tile, int type)
			{
				if (!result.TryGetValue(tile, out List<int> list)) result[tile] = list = new List<int>();
				if (!list.Contains(type)) list.Add(type);
			}
			foreach ((int type, Item sample) in ContentSamples.ItemsByType)
			{
				if (sample == null || sample.createTile < TileID.Dirt) continue;
				Add(sample.createTile, type);
				direct.Add(sample.createTile);
				foreach (int tile in ActsAs(sample.createTile)) Add(tile, type);
			}
			placedDirectly = direct;
			return placers = result;
		}

		// A tile no item places itself is part of the world (the Demon Altar), even when another mod's tile acts as it (Magic
		// Storage's portable altars): those placers only add a way, they never make the world's own tiles later
		public static bool WorldTile(int tile)
		{
			PlacersByTile();
			return !placedDirectly.Contains(tile);
		}

		// The vanilla tiles another mod's tile also counts as for crafting (ModTile.AdjTiles), in a stable order
		public static IEnumerable<int> ActsAs(int tile)
		{
			if (!IsModded(tile) || TileLoader.GetTile(tile)?.AdjTiles is not int[] adj) return Enumerable.Empty<int>();
			return adj.Where(t => t >= TileID.Dirt && t < TileID.Count).Distinct().OrderBy(t => t);
		}

		// The floor of an item placing another mod's tile that acts as vanilla progression stations: the highest of their tiers
		// (a combined station acting as an Ancient Manipulator is a post-Lunatic item, whatever its recipe or rarity say). 0:
		// none. Load-time facts only
		public static int ActingFloor(int item)
		{
			if (!PlacedTile(item, out int tile)) return 0;
			int floor = 0;
			foreach (int acted in ActsAs(tile))
			{
				if (Known.TryGetValue(acted, out StationInfo info) && info.Kind == StationKind.Progression && !info.World) floor = System.Math.Max(floor, info.Tier);
			}
			return floor;
		}

		public static List<int> Placers(int tile) => PlacersByTile().TryGetValue(tile, out List<int> list) ? list : new List<int>();

		// Places a basic station (any work bench, table, chair...)
		public static bool IsBasicPlacer(int item) => PlacedTile(item, out int tile) && Known.TryGetValue(tile, out StationInfo info) && info.Kind == StationKind.Basic;

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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	// Chests are filled at world creation: their content is randomized right after, once.
	// For worlds created before the mod, the host can run /rchests.
	public class ChestSystem : ModSystem
	{
		public static bool ChestsRandomized { get; private set; }

		public override void PostWorldGen()
		{
			if (LootConfig.Instance.RandomizeChests) RandomizeAllChests(existingWorld: false);
		}

		// Stations really hidden in chests (hints only point to chests for these), saved and sent with the world data
		private static readonly HashSet<int> placedStations = new();

		public static bool StationInChests(int item) => placedStations.Contains(item);

		// Changes what the hints know (HintFacts listens)
		public static event Action OnStationsChanged;

		public override void ClearWorld()
		{
			ChestsRandomized = false;
			placedStations.Clear();
		}

		public override void Unload() => OnStationsChanged = null;

		public override void SaveWorldData(TagCompound tag)
		{
			tag["chestsRandomized"] = ChestsRandomized;
			tag["chestStations"] = placedStations.Select(DetRandom.ItemName).ToList();
		}

		public override void LoadWorldData(TagCompound tag)
		{
			ChestsRandomized = tag.GetBool("chestsRandomized");
			placedStations.Clear();
			foreach (string name in tag.GetList<string>("chestStations"))
			{
				if (ContentSamples.ItemNetIdsByPersistentIds.TryGetValue(name, out int type)) placedStations.Add(type);
			}
			OnStationsChanged?.Invoke();
		}

		public override void NetSend(BinaryWriter writer)
		{
			writer.Write7BitEncodedInt(placedStations.Count);
			foreach (int type in placedStations) writer.Write7BitEncodedInt(type);
		}

		public override void NetReceive(BinaryReader reader)
		{
			int count = reader.Read7BitEncodedInt();
			HashSet<int> received = new();
			for (int i = 0; i < count; i++) received.Add(reader.Read7BitEncodedInt());
			if (received.SetEquals(placedStations)) return;
			placedStations.Clear();
			placedStations.UnionWith(received);
			OnStationsChanged?.Invoke();
		}

		// Returns the number of chests changed. On an existing world, chests near houses and the spawn point are skipped:
		// they are probably the players' storage
		public static int RandomizeAllChests(bool existingWorld)
		{
			if (!ItemPool.Ready) return 0;
			int changed = 0;
			for (int c = 0; c < Main.maxChests; c++)
			{
				Chest chest = Main.chest[c];
				if (chest == null || !string.IsNullOrEmpty(chest.name)) continue;
				int chestTier = ChestTier(chest.x, chest.y);
				if (chestTier < 0 || (existingWorld && NearPlayerBase(chest.x, chest.y))) continue;
				for (int slot = 0; slot < chest.item.Length; slot++)
				{
					Item item = chest.item[slot];
					if (item == null || item.IsAir || DropHelper.KeepOriginal(item.type)) continue;
					int original = item.type;
					RollRequest request = new()
					{
						Keys = new[] { RollKey.Chest, chest.x, chest.y, slot },
						SourceTier = Tiers.Clamp(Math.Max(chestTier, ItemPool.Info[original].Tier) + LootConfig.Instance.ChestTierBonus),
						OriginalItem = original,
						Policy = CategoryPolicy.SameAsOriginal,
						TierJitterDown = 0,
						TierJitterUp = 1,
						AllowJackpot = true,
						// The world does not exist yet: progression can't lock chest content
						IgnoreGating = true
					};
					int replacement = ItemPool.RollReplacement(request, LootConfig.Instance.SurpriseChance, DetRandom.Get(RollKey.Chest, chest.x, chest.y, slot, RollKey.Surprise));
					int stack = StackRules.Scaled(original, item.stack, replacement);
					item.SetDefaults(replacement);
					item.stack = stack;
					item.Prefix(-1);
				}
				changed++;
			}
			PlaceGuaranteedStations(existingWorld);
			ChestsRandomized = true;
			OnStationsChanged?.Invoke();
			if (Main.netMode == NetmodeID.Server) NetMessage.SendData(MessageID.WorldData);
			return changed;
		}

		// Hides a few copies of each basic station (work bench, furnace...) in wooden chests, in a fixed random order
		private static void PlaceGuaranteedStations(bool existingWorld)
		{
			int copies = ProgressionConfig.Instance.ChestStationGuarantee;
			List<int> stations = GuaranteedSources.StationRequirements().Select(r => r.Item).ToList();
			if (copies <= 0 || stations.Count == 0) return;
			List<Chest> chests = new();
			for (int c = 0; c < Main.maxChests; c++)
			{
				Chest chest = Main.chest[c];
				if (chest == null || !string.IsNullOrEmpty(chest.name) || !IsWoodenChest(chest.x, chest.y)) continue;
				if (existingWorld && NearPlayerBase(chest.x, chest.y)) continue;
				chests.Add(chest);
			}
			if (chests.Count == 0) return;
			chests.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
			Random rng = DetRandom.Get(RollKey.Guaranteed, RollKey.Chest);
			for (int i = chests.Count - 1; i > 0; i--)
			{
				int j = rng.Next(i + 1);
				(chests[i], chests[j]) = (chests[j], chests[i]);
			}
			int next = 0;
			foreach (int station in stations)
			{
				for (int copy = 0; copy < copies; copy++)
				{
					for (int tries = 0; tries < chests.Count; tries++)
					{
						Chest chest = chests[next++ % chests.Count];
						int slot = Array.FindIndex(chest.item, item => item == null || item.IsAir);
						if (slot < 0) continue;
						chest.item[slot] = new Item(station);
						placedStations.Add(station);
						break;
					}
				}
			}
		}

		private static bool IsWoodenChest(int x, int y)
		{
			if (!WorldGen.InWorld(x, y)) return false;
			Tile tile = Main.tile[x, y];
			return tile.HasTile && tile.TileType == TileID.Containers && tile.TileFrameX / 36 == 0;
		}

		private static bool NearPlayerBase(int x, int y)
		{
			const int Distance = 60;
			if (Math.Abs(x - Main.spawnTileX) < Distance && Math.Abs(y - Main.spawnTileY) < Distance) return true;
			for (int i = 0; i < Main.maxNPCs; i++)
			{
				NPC npc = Main.npc[i];
				if (npc.active && npc.townNPC && !npc.homeless && Math.Abs(x - npc.homeTileX) < Distance && Math.Abs(y - npc.homeTileY) < Distance) return true;
			}
			return false;
		}

		// How advanced a natural chest is, from its tile style. -1 = not a natural chest
		private static int ChestTier(int x, int y)
		{
			if (!WorldGen.InWorld(x, y)) return -1;
			Tile tile = Main.tile[x, y];
			if (!tile.HasTile) return -1;
			int style = tile.TileFrameX / 36;
			int depthTier = y > Main.worldSurface ? 1 : 0;
			if (tile.TileType == TileID.Containers)
			{
				return style switch
				{
					0 => depthTier,                     // Wooden
					1 => 1,                             // Gold
					2 or 4 => 3,                        // Locked gold (dungeon), locked shadow
					3 => 3,                             // Shadow (underworld)
					10 or 11 or 12 or 13 or 17 => 1,    // Ivy, frozen, living wood, skyware, water
					16 => 7,                            // Lihzahrd
					23 or 24 or 25 or 26 or 27 => 6,    // Biome chests (jungle, corruption, crimson, hallowed, frozen)
					32 or 50 or 51 => 1,                // Mushroom, granite, marble
					_ => depthTier
				};
			}
			if (tile.TileType == TileID.Containers2)
			{
				return style == 13 ? 6 : 1;             // Desert biome chest, otherwise sandstone/dead man's
			}
			return -1;
		}
	}
}

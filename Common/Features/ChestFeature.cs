using System;
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
			if (LootConfig.Instance.RandomizeChests) RandomizeAllChests();
		}

		public override void ClearWorld() => ChestsRandomized = false;

		public override void SaveWorldData(TagCompound tag) => tag["chestsRandomized"] = ChestsRandomized;

		public override void LoadWorldData(TagCompound tag) => ChestsRandomized = tag.GetBool("chestsRandomized");

		// Returns the number of chests changed
		public static int RandomizeAllChests()
		{
			if (!ItemPool.Ready) return 0;
			int changed = 0;
			for (int c = 0; c < Main.maxChests; c++)
			{
				Chest chest = Main.chest[c];
				if (chest == null || !string.IsNullOrEmpty(chest.name)) continue;
				int chestTier = ChestTier(chest.x, chest.y);
				if (chestTier < 0) continue;
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
					int replacement = ItemPool.Roll(request);
					int stack = StackRules.Scaled(original, item.stack, replacement);
					item.SetDefaults(replacement);
					item.stack = stack;
					item.Prefix(-1);
				}
				changed++;
			}
			ChestsRandomized = true;
			return changed;
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

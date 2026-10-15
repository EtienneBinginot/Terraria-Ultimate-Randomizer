using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	// Mined ores give another ore or bar of the same tier; pots give random items of the same kind as their usual loot
	public class TileDropItem : GlobalItem
	{
		public override void OnSpawn(Item item, IEntitySource source)
		{
			if (source is not EntitySource_TileBreak tileBreak || !SeedSystem.WorldReady || !ItemPool.Ready) return;
			if (!ItemPool.IsRandomizable(item.type)) return;
			LootConfig cfg = LootConfig.Instance;
			Point coords = tileBreak.TileCoords;
			bool isPot = WorldGen.InWorld(coords.X, coords.Y) && Main.tile[coords.X, coords.Y].HasTile && Main.tile[coords.X, coords.Y].TileType == TileID.Pots;
			bool isOre = item.createTile >= TileID.Dirt && TileID.Sets.Ore[item.createTile];
			int key;
			if (isPot && cfg.RandomizePotDrops)
			{
				key = DetRandom.Hash(RollKey.TileDrop, 1, DetRandom.ItemKey(item.type), Main.rand.Next(4));
			}
			else if (isOre && !isPot && cfg.RandomizeOreDrops)
			{
				// Always the same ore for a given ore in this world
				key = DetRandom.Hash(RollKey.TileDrop, 2, DetRandom.ItemKey(item.type));
			}
			else
			{
				return;
			}
			int original = item.type;
			RollRequest request = new()
			{
				Keys = new[] { key },
				SourceTier = ItemPool.Info[original].Tier,
				OriginalItem = original,
				Policy = CategoryPolicy.SameAsOriginal,
				TierJitterDown = 0,
				TierJitterUp = 0,
				AllowJackpot = false
			};
			int replacement = ItemPool.Roll(request);
			if (replacement == original) return;
			// SetDefaults resets the position: keep where the item was spawned
			Vector2 position = item.position;
			Vector2 velocity = item.velocity;
			int stack = StackRules.Scaled(original, item.stack, replacement);
			item.SetDefaults(replacement);
			item.position = position;
			item.velocity = velocity;
			item.stack = stack;
			item.Prefix(-1);
		}
	}
}

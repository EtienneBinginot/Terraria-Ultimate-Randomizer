using System;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	// Mined ores give another ore of the same tier (1 for 1); pots give items of the same kind as their usual loot
	public class TileDropItem : GlobalItem
	{
		// Vanilla removes the pot tile before spawning its loot: this flag tells us the loot comes from a pot
		[ThreadStatic]
		private static bool inPotDrop;

		public override void Load() => On_WorldGen.SpawnThingsFromPot += MarkPotDrop;

		private static void MarkPotDrop(On_WorldGen.orig_SpawnThingsFromPot orig, int i, int j, int x2, int y2, int style)
		{
			inPotDrop = true;
			try
			{
				orig(i, j, x2, y2, style);
			}
			finally
			{
				inPotDrop = false;
			}
		}

		public override void OnSpawn(Item item, IEntitySource source)
		{
			if (source is not EntitySource_TileBreak || !SeedSystem.WorldReady || !ItemPool.Ready) return;
			if (!ItemPool.IsRandomizable(item.type)) return;
			LootConfig cfg = LootConfig.Instance;
			if (inPotDrop)
			{
				if (cfg.RandomizePotDrops) ReplacePotItem(item);
			}
			else if (cfg.RandomizeOreDrops && IsOre(item.type))
			{
				ReplaceOre(item);
			}
		}

		private static bool IsOre(int type) =>
			ContentSamples.ItemsByType.TryGetValue(type, out Item sample) && sample.createTile >= TileID.Dirt && TileID.Sets.Ore[sample.createTile];

		private static void ReplacePotItem(Item item)
		{
			int original = item.type;
			RollRequest request = new()
			{
				Keys = new[] { RollKey.TileDrop, 1, DetRandom.ItemKey(original), Main.rand.Next(4) },
				SourceTier = ItemPool.Info[original].Tier,
				OriginalItem = original,
				Policy = CategoryPolicy.SameAsOriginal,
				TierJitterDown = 0,
				TierJitterUp = 0,
				AllowJackpot = false
			};
			int replacement = ItemPool.Roll(request);
			DropHelper.ChangeSpawnedItem(item, replacement, StackRules.Scaled(original, item.stack, replacement));
		}

		// Always the same ore for a given ore in a world, and only ores: mining placed ore can't create value
		private static void ReplaceOre(Item item)
		{
			int original = item.type;
			for (int attempt = 0; attempt < 10; attempt++)
			{
				RollRequest request = new()
				{
					Keys = new[] { RollKey.TileDrop, 2, DetRandom.ItemKey(original), attempt },
					SourceTier = ItemPool.Info[original].Tier,
					OriginalItem = original,
					Policy = CategoryPolicy.SameAsOriginal,
					TierJitterDown = 0,
					TierJitterUp = 0,
					AllowJackpot = false
				};
				int replacement = ItemPool.Roll(request);
				if (IsOre(replacement) && ItemPool.TierOf(replacement) == ItemPool.TierOf(original))
				{
					DropHelper.ChangeSpawnedItem(item, replacement, item.stack);
					return;
				}
			}
		}
	}
}

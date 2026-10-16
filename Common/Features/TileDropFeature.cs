using System;
using System.Collections.Generic;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	// Mined ores give another ore of the same tier (1 for 1, each ore still exists); pots give items of the same kind
	// as their usual loot, or a surprise
	public class TileDropItem : GlobalItem
	{
		// Vanilla removes the pot tile before spawning its loot: this flag tells us the loot comes from a pot
		[ThreadStatic]
		private static bool inPotDrop;

		// Everything a pot gives, its guaranteed item included (the codex records it as pot loot)
		[ThreadStatic]
		private static bool potLoot;

		public static bool PotLoot => potLoot;

		public override void Load() => On_WorldGen.SpawnThingsFromPot += MarkPotDrop;

		private static void MarkPotDrop(On_WorldGen.orig_SpawnThingsFromPot orig, int i, int j, int x2, int y2, int style)
		{
			inPotDrop = true;
			potLoot = true;
			try
			{
				try
				{
					orig(i, j, x2, y2, style);
				}
				finally
				{
					inPotDrop = false;
				}
				if (SeedSystem.WorldReady && ItemPool.Ready && Main.netMode != NetmodeID.MultiplayerClient
					&& Main.rand.NextFloat() * 100 < ProgressionConfig.Instance.PotGuaranteeChance)
				{
					List<int> items = GuaranteedSources.PotItems(ProgressionSystem.UnlockedTier);
					if (items.Count > 0) Item.NewItem(new EntitySource_TileBreak(i, j), i * 16, j * 16, 16, 16, items[Main.rand.Next(items.Count)]);
				}
			}
			finally
			{
				potLoot = false;
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
			else if (cfg.RandomizeOreDrops && OreMap.IsOre(item.type))
			{
				DropHelper.ChangeSpawnedItem(item, OreMap.Get(item.type), item.stack);
			}
		}

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
			int replacement = ItemPool.RollReplacement(request, LootConfig.Instance.SurpriseChance, new Random(Main.rand.Next()));
			DropHelper.ChangeSpawnedItem(item, replacement, StackRules.Scaled(original, item.stack, replacement));
		}
	}
}

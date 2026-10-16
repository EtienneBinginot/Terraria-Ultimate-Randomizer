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
	// Shaken trees, herbs and plants: what they give is replaced when it spawns (server or single player)
	public class WorldSourcesItem : GlobalItem
	{
		// Tiles of herbs and wild plants (grass, jungle, mushrooms, evil and hallowed plants, dye plants)
		private static readonly HashSet<int> PlantTiles = new()
		{
			TileID.MatureHerbs, TileID.BloomingHerbs, TileID.Plants, TileID.Plants2, TileID.JunglePlants, TileID.JunglePlants2,
			TileID.MushroomPlants, TileID.CorruptPlants, TileID.CrimsonPlants, TileID.HallowedPlants, TileID.HallowedPlants2,
			TileID.DyePlants, TileID.OasisPlants, TileID.AshPlants
		};

		public override void OnSpawn(Item item, IEntitySource source)
		{
			if (!SeedSystem.WorldReady || !ItemPool.Ready || item.IsAir || !ItemPool.IsRandomizable(item.type)) return;
			LootConfig cfg = LootConfig.Instance;
			switch (source)
			{
				case EntitySource_ShakeTree when cfg.RandomizeTreeShaking:
					if (cfg.KeepAcorns && item.type == ItemID.Acorn) return;
					Replace(item, WorldSources.TreeShakeReplacement(item.type, new Random(Main.rand.Next())));
					break;
				case EntitySource_TileBreak tileBreak when cfg.RandomizeHerbs && IsPlant(tileBreak):
					// Seeds and anything that places a tile stay: they keep the plants growing
					if (item.createTile >= TileID.Dirt) return;
					Replace(item, WorldSources.HerbReplacement(item.type, new Random(Main.rand.Next())));
					break;
			}
		}

		// Vanilla spawns the drops before removing the tile: its type is still there
		private static bool IsPlant(EntitySource_TileBreak source)
		{
			if (!WorldGen.InWorld(source.TileCoords.X, source.TileCoords.Y)) return false;
			Tile tile = Main.tile[source.TileCoords.X, source.TileCoords.Y];
			return tile.HasTile && PlantTiles.Contains(tile.TileType);
		}

		private static void Replace(Item item, int replacement) =>
			DropHelper.ChangeSpawnedItem(item, replacement, StackRules.Scaled(item.type, item.stack, replacement));

		// Extractinator: runs on the client that uses it, before the item is given
		public override void ExtractinatorUse(int extractType, int extractinatorBlockType, ref int resultType, ref int resultStack)
		{
			if (!LootConfig.Instance.RandomizeExtractinator || !SeedSystem.WorldReady || !ItemPool.Ready || !ItemPool.IsRandomizable(resultType)) return;
			int replacement = WorldSources.ExtractinatorReplacement(resultType, new Random(Main.rand.Next()));
			resultStack = StackRules.Scaled(resultType, resultStack, replacement);
			resultType = replacement;
		}
	}

	// Angler quest rewards: replaced on the client that turns the fish in
	public class AnglerRewardsPlayer : ModPlayer
	{
		public override void AnglerQuestReward(float rareMultiplier, List<Item> rewardItems)
		{
			if (!LootConfig.Instance.RandomizeAnglerRewards || !SeedSystem.WorldReady || !ItemPool.Ready) return;
			WorldSources.ReplaceAnglerRewards(rewardItems, Player.anglerQuestsFinished, new Random(Main.rand.Next()));
		}
	}

	// The rolls of these sources, also used by the tests and the findability of items
	public static class WorldSources
	{
		public static int TreeShakeReplacement(int original, Random surpriseRng) =>
			ItemPool.RollReplacement(Request(RollKey.TreeShake, original, 0), LootConfig.Instance.SurpriseChance, surpriseRng);

		// A herb has a few fixed replacements in a world (HerbVariety); it is sometimes kept (HerbKeepChance)
		public static int HerbReplacement(int original, Random rng)
		{
			LootConfig cfg = LootConfig.Instance;
			if (rng.NextDouble() * 100 < cfg.HerbKeepChance) return original;
			int index = rng.Next(Math.Max(1, cfg.HerbVariety));
			return ItemPool.RollReplacement(Request(RollKey.Herb, original, index), cfg.SurpriseChance, rng);
		}

		// The fixed replacements of a herb (ignoring progression), for the findability of items
		public static List<int> HerbTable(int original)
		{
			List<int> table = new();
			for (int index = 0; index < Math.Max(1, LootConfig.Instance.HerbVariety); index++)
			{
				RollRequest request = Request(RollKey.Herb, original, index);
				request.IgnoreGating = true;
				table.Add(ItemPool.Roll(request));
			}
			return table;
		}

		public static int ExtractinatorReplacement(int original, Random surpriseRng)
		{
			if (OreMap.IsOre(original)) return OreMap.Get(original);
			return ItemPool.RollReplacement(Request(RollKey.Extractinator, original, 0), LootConfig.Instance.SurpriseChance, surpriseRng);
		}

		public static void ReplaceAnglerRewards(List<Item> rewards, int questsFinished, Random surpriseRng)
		{
			for (int i = 0; i < rewards.Count; i++)
			{
				Item reward = rewards[i];
				// Bait is kept: it is what the Angler is for
				if (reward == null || reward.IsAir || reward.bait > 0 || !ItemPool.IsRandomizable(reward.type)) continue;
				RollRequest request = Request(RollKey.Angler, reward.type, questsFinished * 16 + i);
				request.TierJitterUp = 1;
				request.AllowJackpot = true;
				int replacement = ItemPool.RollReplacement(request, LootConfig.Instance.SurpriseChance, surpriseRng);
				if (replacement == reward.type) continue;
				Item item = new(replacement, StackRules.Scaled(reward.type, reward.stack, replacement));
				item.Prefix(-1);
				rewards[i] = item;
			}
		}

		private static RollRequest Request(int rollKind, int original, int index) => new()
		{
			Keys = new[] { rollKind, DetRandom.ItemKey(original), index },
			SourceTier = ItemPool.TierOf(original),
			OriginalItem = original,
			Policy = CategoryPolicy.SameAsOriginal
		};
	}
}

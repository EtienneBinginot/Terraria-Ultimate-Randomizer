using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Tests
{
	// Shaken trees, herbs, Extractinator and Angler rewards
	public static class SourceTests
	{
		private static readonly int[] Herbs =
		{
			ItemID.Daybloom, ItemID.Moonglow, ItemID.Blinkroot, ItemID.Deathweed, ItemID.Waterleaf, ItemID.Fireblossom, ItemID.Shiverthorn,
			ItemID.Mushroom, ItemID.GlowingMushroom, ItemID.VileMushroom, ItemID.ViciousMushroom, ItemID.JungleSpores
		};

		public static void Register()
		{
			TestRunner.Register("TreeShakeReplacement", TreeShakeReplacement);
			TestRunner.Register("HerbReplacementTable", HerbReplacementTable);
			TestRunner.Register("ExtractinatorRules", ExtractinatorRules);
			TestRunner.Register("AnglerRewardsValid", AnglerRewardsValid);
			TestRunner.Register("TreeCritterSwap", TreeCritterSwap);
		}

		private static void TreeShakeReplacement()
		{
			LootConfig cfg = LootConfig.Instance;
			if (!cfg.RandomizeTreeShaking) Check.Skip("shaken trees are not randomized");
			WorldSourcesItem global = ModContent.GetInstance<WorldSourcesItem>();
			int changed = 0;
			List<string> failures = new();
			for (int i = 0; i < 40; i++)
			{
				Item apple = new(ItemID.Apple);
				global.OnSpawn(apple, new EntitySource_ShakeTree(100, 100));
				if (!ItemPool.IsRandomizable(apple.type)) failures.Add($"gave {Lang.GetItemNameValue(apple.type)}");
				if (apple.type != ItemID.Apple) changed++;
			}
			Item acorn = new(ItemID.Acorn);
			global.OnSpawn(acorn, new EntitySource_ShakeTree(100, 100));
			if (cfg.KeepAcorns && acorn.type != ItemID.Acorn) failures.Add("the acorn was replaced");
			Check.That(changed > 0, "apples are never replaced");
			Check.All(failures, "Shaken trees");
		}

		private static void HerbReplacementTable()
		{
			LootConfig cfg = LootConfig.Instance;
			List<string> failures = new();
			foreach (int herb in Herbs.Where(ItemPool.IsRandomizable))
			{
				List<int> table = WorldSources.HerbTable(herb);
				if (table.Count != Math.Max(1, cfg.HerbVariety)) failures.Add($"{Lang.GetItemNameValue(herb)}: {table.Count} items");
				if (!table.SequenceEqual(WorldSources.HerbTable(herb))) failures.Add($"{Lang.GetItemNameValue(herb)}: the table changes");
				foreach (int item in table)
				{
					if (!ItemPool.IsRandomizable(item)) failures.Add($"{Lang.GetItemNameValue(herb)} -> {Lang.GetItemNameValue(item)} not randomizable");
					else if (ItemPool.Info[item].Category != ItemPool.Info[herb].Category) failures.Add($"{Lang.GetItemNameValue(herb)} -> {Lang.GetItemNameValue(item)} changes category");
				}
			}
			Check.All(failures, "Herb tables");
		}

		private static void ExtractinatorRules()
		{
			if (!LootConfig.Instance.RandomizeExtractinator) Check.Skip("the Extractinator is not randomized");
			WorldSourcesItem global = ModContent.GetInstance<WorldSourcesItem>();
			int type = ItemID.SilverCoin;
			int stack = 5;
			global.ExtractinatorUse(ItemID.SiltBlock, TileID.Extractinator, ref type, ref stack);
			Check.Equal(ItemID.SilverCoin, type, "coins");
			type = ItemID.CopperOre;
			stack = 3;
			global.ExtractinatorUse(ItemID.SiltBlock, TileID.Extractinator, ref type, ref stack);
			Check.Equal(OreMap.Get(ItemID.CopperOre), type, "copper ore");
			Check.Equal(3, stack, "ore stack");
			type = ItemID.Amethyst;
			stack = 1;
			global.ExtractinatorUse(ItemID.SiltBlock, TileID.Extractinator, ref type, ref stack);
			Check.That(ItemPool.IsRandomizable(type) && stack >= 1, $"gem gave {Lang.GetItemNameValue(type)} x{stack}");
		}

		private static void AnglerRewardsValid()
		{
			List<Item> rewards = new() { new Item(ItemID.FuzzyCarrot), new Item(ItemID.GoldCoin, 5), new Item(ItemID.ApprenticeBait, 3), new Item(ItemID.HealingPotion, 5) };
			WorldSources.ReplaceAnglerRewards(rewards, 7, new Random(4));
			List<string> failures = new();
			if (rewards[1].type != ItemID.GoldCoin || rewards[1].stack != 5) failures.Add("coins changed");
			if (rewards[2].type != ItemID.ApprenticeBait) failures.Add("bait changed");
			foreach (Item reward in rewards)
			{
				if (reward == null || reward.IsAir || reward.stack < 1) failures.Add("empty reward");
				else if (!ItemPool.IsValid(reward.type)) failures.Add($"invalid item {reward.type}");
			}
			Check.All(failures, "Angler rewards");
		}

		private static void TreeCritterSwap()
		{
			if (Main.netMode != NetmodeID.SinglePlayer) Check.Skip("single player only");
			EnemiesConfig cfg = EnemiesConfig.Instance;
			if (!cfg.RandomizeEnemySpawns || !cfg.SwapCritters) Check.Skip("critters are not swapped");
			Player player = Main.LocalPlayer;
			int index = NPC.NewNPC(new EntitySource_ShakeTree(0, 0), (int)player.Center.X + 1200, (int)player.Center.Y - 200, NPCID.Bird);
			Check.That(index >= 0 && index < Main.maxNPCs, "could not spawn a bird");
			bool pending = EnemySwapSystem.Pending[index];
			EnemySwapSystem.Pending[index] = false;
			Main.npc[index].active = false;
			Check.That(pending, "a bird from a tree is not swapped");
		}
	}
}

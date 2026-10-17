using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using UltimateRandomizer.Common.Codex;
using UltimateRandomizer.Common.Codex.Interface;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Tests
{
	// The Guide's hints: their facts come from the planners, their prices, buying, syncing and texts
	public static class HintTests
	{
		public static void Register()
		{
			TestRunner.Register("HintFactsTrue", HintFactsTrue);
			TestRunner.Register("HintPriceScales", HintPriceScales);
			TestRunner.Register("HintPurchaseFlow", HintPurchaseFlow);
			TestRunner.Register("HintSyncRoundTrip", HintSyncRoundTrip);
			TestRunner.Register("HintTextsResolved", HintTextsResolved);
		}

		private static List<HintTarget> TargetsOrSkip()
		{
			if (!HintFacts.Ready) Check.Skip("recipes not loaded");
			List<HintTarget> targets = HintFacts.AllTargets();
			if (targets.Count == 0) Check.Skip("no hint target with these settings");
			return targets;
		}

		// Level 1, 2 and 4 of every target say what the planners do: the host really carries it, the recipe really makes it
		private static void HintFactsTrue()
		{
			List<HintTarget> targets = TargetsOrSkip();
			List<string> failures = new();
			RecipePlan plan = RecipePlanner.Get();
			Check.That(targets.Any(t => t.Kind == HintTargetKind.Station), "no station among the targets");
			foreach (HintTarget target in targets)
			{
				int item = target.Item;
				HintFact fact = HintFacts.For(item);
				string name = $"{HintFacts.ItemName(item)} ({fact.Kind})";
				switch (fact.Kind)
				{
					case HintKind.Creature:
					case HintKind.Boss:
						if (!RandomNpcDropRule.Plan(fact.Npc, Main.expertMode, lucky: true).Any(d => d.Item == item)) failures.Add($"{name}: {HintFacts.NpcName(fact.Npc)} does not drop it");
						break;
					case HintKind.Recipe:
						if (Main.recipe[fact.Recipe].Disabled || plan.Results[fact.Recipe].type != item) failures.Add($"{name}: recipe {fact.Recipe} does not make it");
						if (!fact.Ingredients.SequenceEqual(HintFacts.PlannedIngredients(fact.Recipe))) failures.Add($"{name}: ingredients differ from the plan");
						if (!fact.Tiles.SequenceEqual(HintFacts.PlannedTiles(fact.Recipe).Where(t => t >= 0))) failures.Add($"{name}: stations differ from the plan");
						if (!HintFacts.Text(fact, 1).Contains(CodexText.Get("Hint.Station." + HintFacts.StationGroup(fact.Tiles)))) failures.Add($"{name}: level 1 without the station kind");
						foreach (int tile in fact.Tiles)
						{
							if (!HintFacts.Text(fact, 4).Contains(HintFacts.StationName(tile))) failures.Add($"{name}: level 4 without station {HintFacts.StationName(tile)}");
						}
						break;
					case HintKind.Bag:
						if (!BagPlan.Plan(fact.Bag, lucky: true).Any(d => d.Item == item)) failures.Add($"{name}: {HintFacts.ItemName(fact.Bag)} does not hold it");
						break;
					case HintKind.Pot:
						if (!GuaranteedSources.PotItems(Tiers.Max).Contains(item)) failures.Add($"{name}: not in the pot items");
						break;
					case HintKind.Chest:
						if (!ChestSystem.StationInChests(item)) failures.Add($"{name}: not hidden in chests");
						break;
					case HintKind.Shop:
						bool sold = NPCShopDatabase.AllShops.OfType<NPCShop>().Any(s => s.NpcType == fact.Npc && s.Entries.Any(e => e?.Item?.type == item));
						if (!sold) failures.Add($"{name}: {HintFacts.NpcName(fact.Npc)} does not sell it");
						break;
				}
				// Level 2: the biomes named are places where the host spawns
				if (fact.Kind == HintKind.Creature && BiomeSlots.Active)
				{
					int type = BiomeSlots.TypeOf(fact.Npc);
					foreach (string biome in fact.Biomes)
					{
						bool spawns = BiomeSlots.Current.Slots.Any(s => BiomeSlots.BiomeName(s.Biome) == biome && HintFacts.SpawnsIn(s, type));
						if (!spawns) failures.Add($"{name}: {HintFacts.NpcName(fact.Npc)} never spawns in {biome}");
					}
					if (fact.Biomes.Count > 0 && !HintFacts.Text(fact, 2).Contains(BiomeSlots.BiomeDisplayName(fact.Biomes[0]))) failures.Add($"{name}: level 2 without its biome");
				}
				// Level 4: the name of the host, the bag or the item
				string named = fact.Kind switch
				{
					HintKind.Creature or HintKind.Boss or HintKind.Shop => HintFacts.NpcName(fact.Npc),
					HintKind.Bag => HintFacts.ItemName(fact.Bag),
					HintKind.Recipe => null,
					_ => HintFacts.ItemName(item)
				};
				if (named != null && !HintFacts.Text(fact, 4).Contains(named)) failures.Add($"{name}: level 4 does not name {named}");
			}
			Check.All(failures, "Hints not matching the planners");
		}

		// base x (1 + 1.5 x tier) x 3^(level - 1): 2, 6, 18, 54 gold at tier 0, 14 to 378 gold at tier 4
		private static void HintPriceScales()
		{
			long gold = HintFacts.CopperPerGold;
			Check.Equal(2 * gold, HintFacts.Price(0, 1, 2), "tier 0 level 1");
			Check.Equal(54 * gold, HintFacts.Price(0, 4, 2), "tier 0 level 4");
			Check.Equal(14 * gold, HintFacts.Price(4, 1, 2), "tier 4 level 1");
			Check.Equal(378 * gold, HintFacts.Price(4, 4, 2), "tier 4 level 4");
			Check.Equal(0L, HintFacts.Price(3, 2, 0), "free hints");
			for (int tier = 0; tier < Tiers.Count; tier++)
			{
				for (int level = 1; level <= HintFacts.MaxLevel; level++)
				{
					long price = HintFacts.Price(tier, level, 5);
					if (level > 1) Check.That(price > HintFacts.Price(tier, level - 1, 5), $"tier {tier}: level {level} not dearer");
					if (tier > 0) Check.That(price > HintFacts.Price(tier - 1, level, 5), $"level {level}: tier {tier} not dearer");
				}
			}
			Check.Equal(CodexText.Get("Hint.Coin.Gold", 2), HintFacts.PriceText(2 * gold), "price text");
		}

		private static Item[][] Containers(Player player) => new[] { player.inventory, player.bank.item, player.bank2.item, player.bank3.item, player.bank4.item };

		private static long Coins(Player player) => Containers(player).Sum(items => Utils.CoinsCount(out _, items));

		// Buying takes the coins, raises the level by one, and is refused when the coins are short
		private static void HintPurchaseFlow()
		{
			if (Main.netMode != NetmodeID.SinglePlayer) Check.Skip("single player only");
			if (ExtrasConfig.Instance.HintBasePrice <= 0) Check.Skip("hints are free");
			TargetsOrSkip();
			HintTarget target = HintFacts.Targets().FirstOrDefault();
			if (target == null) Check.Skip("no target offered now");
			Player player = Main.LocalPlayer;
			Item[][] containers = Containers(player);
			Item[][] saved = containers.Select(items => items.Select(i => i.Clone()).ToArray()).ToArray();
			CodexData savedData = CodexSystem.Data;
			try
			{
				CodexSystem.Data = new CodexData { Seed = SeedSystem.WorldSeed, HasSeed = true };
				foreach (Item[] items in containers)
				{
					for (int i = 0; i < items.Length; i++)
					{
						if (!items[i].IsAir && ItemID.Sets.CommonCoin[items[i].type]) items[i].TurnToAir();
					}
				}
				long price = HintFacts.Price(target, 1);
				GiveCoins(player, price - 1);
				Check.Equal(HintPurchase.TooPoor, HintShop.TryBuy(player, target, 1, out _), "purchase without enough coins");
				Check.Equal(0, CodexSystem.HintLevel(target.Item), "level after a refused purchase");
				Check.Equal(price - 1, Coins(player), "coins after a refused purchase");
				GiveCoins(player, price);
				Check.Equal(HintPurchase.Bought, HintShop.TryBuy(player, target, 1, out long paid), "purchase");
				Check.Equal(price, paid, "price paid");
				Check.Equal(0L, Coins(player), "coins left");
				Check.Equal(1, CodexSystem.HintLevel(target.Item), "level after the purchase");
				GiveCoins(player, HintFacts.Price(target, 3) * 2);
				Check.Equal(HintPurchase.NotNext, HintShop.TryBuy(player, target, 3, out _), "a level bought out of order");
				Check.Equal(HintPurchase.NotNext, HintShop.TryBuy(player, target, 1, out _), "the same level bought twice");
				Check.Equal(HintFacts.Price(target, 3) * 2, Coins(player), "coins after refused purchases");
				Check.Equal(1, CodexSystem.HintLevel(target.Item), "level after refused purchases");
			}
			finally
			{
				for (int c = 0; c < containers.Length; c++)
				{
					for (int i = 0; i < containers[c].Length && i < saved[c].Length; i++) containers[c][i] = saved[c][i];
				}
				CodexSystem.Data = savedData;
			}
		}

		// Coins in the coin slots (50 to 53), as few as possible
		private static void GiveCoins(Player player, long copper)
		{
			int[] split = Utils.CoinsSplit(copper);
			int[] coins = { ItemID.CopperCoin, ItemID.SilverCoin, ItemID.GoldCoin, ItemID.PlatinumCoin };
			for (int i = 0; i < 4; i++)
			{
				Item item = new();
				if (split[i] > 0)
				{
					item.SetDefaults(coins[i]);
					item.stack = split[i];
				}
				player.inventory[50 + i] = item;
			}
		}

		// Hint levels survive the codex wire format, the save, and the server's checks
		private static void HintSyncRoundTrip()
		{
			List<HintTarget> targets = TargetsOrSkip();
			string bench = CodexNames.Item(ItemID.WorkBench);
			CodexData data = new() { Seed = 77, HasSeed = true };
			Check.That(data.Apply(CodexEntry.Hint(bench, 2)), "level 2 not applied");
			Check.That(!data.Apply(CodexEntry.Hint(bench, 1)), "a lower level applied");
			Check.That(!data.Apply(CodexEntry.Hint(bench, 5)), "a level above the last applied");
			Check.That(data.Apply(CodexEntry.Hint(bench, 3)), "level 3 not applied");
			Check.Equal(3, data.HintLevel(bench), "level");

			MemoryStream stream = new();
			BinaryWriter writer = new(stream);
			int written = data.AllEntries().Count(e => CodexData.Write(writer, e));
			writer.Flush();
			stream.Position = 0;
			BinaryReader reader = new(stream);
			CodexData copy = new() { Seed = 77, HasSeed = true };
			for (int i = 0; i < written; i++) copy.Apply(CodexData.Read(reader));
			Check.Equal(stream.Length, stream.Position, "bytes read");
			Check.Equal(3, copy.HintLevel(bench), "level after sync");
			Check.Equal(data.Signature(), copy.Signature(), "full sync");

			TagCompound tag = new();
			data.Save(tag);
			Check.Equal(3, CodexData.Load(tag).HintLevel(bench), "level after save and load");
			Check.That(CodexSystem.Merge(new[] { CodexEntry.Hint(bench, 1), CodexEntry.Hint(bench, 1), CodexEntry.Hint(bench, 2) }).Count == 2, "merge");

			// The server takes the next level of a target offered now, nothing else
			HintTarget target = targets[0];
			string name = CodexNames.Item(target.Item);
			CodexData server = new() { Seed = 77, HasSeed = true };
			Check.That(CodexSystem.AcceptHint(CodexEntry.Hint(name, 1), server, target.Tier), "first level refused");
			Check.That(!CodexSystem.AcceptHint(CodexEntry.Hint(name, 2), server, target.Tier), "second level accepted before the first");
			server.Apply(CodexEntry.Hint(name, 1));
			Check.That(CodexSystem.AcceptHint(CodexEntry.Hint(name, 2), server, target.Tier), "second level refused");
			// A target of tier 2 or more is not offered two tiers below
			HintTarget late = targets.FirstOrDefault(t => t.Tier >= 2);
			if (late != null) Check.That(!CodexSystem.AcceptHint(CodexEntry.Hint(CodexNames.Item(late.Item), 1), new CodexData(), late.Tier - 2), "a target not offered yet accepted");
			Check.That(!CodexSystem.AcceptHint(CodexEntry.Hint(CodexNames.Item(ItemID.DirtBlock), 1), server, Tiers.Max), "an item that is no target accepted");
		}

		// Every sentence exists in this language, and the hints of every target are full sentences (no key, no placeholder)
		private static void HintTextsResolved()
		{
			List<HintTarget> targets = TargetsOrSkip();
			List<string> failures = new();
			foreach (string key in HintFacts.AllTextKeys())
			{
				if (!Language.Exists(CodexText.Prefix + key)) failures.Add("missing " + key);
			}
			foreach (string key in new[] { "Hint.Button", "Hint.Intro", "Hint.Nothing", "Hint.TooPoor", "Hint.PanelTitle", "Hint.Buy", "Hint.Repeat", "Hint.Level",
				"Hint.Found", "Hint.Unknown", "Hint.NoHint", "Hint.NotAsked", "Hint.TabIntro", "Hint.About", "Hint.PickTarget", "Hint.Money", "Hint.Free", "Hint.NoStation",
				"Hint.Coin.Copper", "Hint.Coin.Silver", "Hint.Coin.Gold", "Hint.Coin.Platinum", "Hint.Syncing", "Hint.Refused", "Hint.Conditions", "TabHints" })
			{
				if (!Language.Exists(CodexText.Prefix + key)) failures.Add("missing " + key);
			}
			foreach (HintTarget target in targets)
			{
				HintFact fact = HintFacts.For(target.Item);
				for (int level = 1; level <= HintFacts.MaxLevel; level++)
				{
					string text = HintFacts.Text(fact, level);
					if (string.IsNullOrWhiteSpace(text) || text.Contains("Mods.") || text.Contains("Hint.") || text.Contains('{') || text.Contains("Bestiary_"))
					{
						failures.Add($"{HintFacts.ItemName(target.Item)} level {level}: \"{text}\"");
					}
				}
			}
			Check.All(failures, $"Hint texts not resolved ({Language.ActiveCulture.Name})");
		}
	}
}

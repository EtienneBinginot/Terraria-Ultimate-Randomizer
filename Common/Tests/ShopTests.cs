using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Tests
{
	// Shops simulated at every unlocked tier with the pure planner (ShopPlanner.PlanShop): what each vanilla shop sells
	// instead of its items, offline review (Shops.tsv) and coherence checks. Read-only
	public static class ShopTests
	{
		public static void Register()
		{
			TestRunner.Register("ShopDump", ShopDump);
			TestRunner.Register("ShopsNotTooEarly", ShopsNotTooEarly);
			TestRunner.Register("ShopOnlyItemsKept", ShopOnlyItemsKept);
			TestRunner.Register("ShopNoDuplicates", ShopNoDuplicates);
		}

		private sealed class VanillaShop
		{
			public AbstractNPCShop Shop;
			public List<Item> Items = new();
			public List<Condition[]> Conditions = new();
			// Slot order, 0 for what the shop leaves alone (special currency): the planner's input
			public int[] Originals;
		}

		// Every vanilla NPC's shop (NPCShopDatabase.AllShops, keyed and passed to ModifyActiveShop by FullName), every entry
		// not disabled whatever its conditions: in game only the entries whose conditions are met fill slots
		private static List<VanillaShop> VanillaShops()
		{
			List<VanillaShop> shops = new();
			foreach (AbstractNPCShop shop in NPCShopDatabase.AllShops)
			{
				if (shop == null || shop.NpcType <= 0 || shop.NpcType >= NPCID.Count) continue;
				VanillaShop entry = new() { Shop = shop };
				foreach (AbstractNPCShop.Entry e in shop.ActiveEntries)
				{
					if (e?.Item == null || e.Item.IsAir) continue;
					entry.Items.Add(e.Item);
					entry.Conditions.Add(e.Conditions?.ToArray() ?? Array.Empty<Condition>());
				}
				if (entry.Items.Count == 0) continue;
				entry.Originals = entry.Items.Select(i => i.shopSpecialCurrency != -1 ? 0 : i.type).ToArray();
				shops.Add(entry);
			}
			return shops;
		}

		private static ShopSlot[] Plan(VanillaShop shop, int tier, bool noDuplicates)
		{
			EconomyConfig cfg = EconomyConfig.Instance;
			return ShopPlanner.PlanShop(shop.Shop.NpcType, shop.Shop.FullName, shop.Originals, tier, tier >= (int)Tier.Hardmode, cfg.ShopsFollowProgression, noDuplicates);
		}

		// Sold by a vanilla shop and by nothing else in the base game: no drop, bag, recipe, gathered material, ore nor critter
		private static HashSet<int> VanillaShopOnly()
		{
			HashSet<int> other = Obtainability.Collect(withRecipes: true, anyBlock: false, withShops: false);
			return Obtainability.ShopItems.Where(i => ItemPool.IsValid(i) && !other.Contains(i)).ToHashSet();
		}

		// What the shops still sell in this world, simulated at every tier: unchanged items and replacements
		private static HashSet<int> SoldSomewhere(List<VanillaShop> shops)
		{
			HashSet<int> sold = new();
			bool randomized = EconomyConfig.Instance.RandomizeShops;
			foreach (VanillaShop shop in shops)
			{
				for (int tier = 0; tier < Tiers.Count; tier++)
				{
					ShopSlot[] plan = Plan(shop, tier, EconomyConfig.Instance.NoDuplicatesInShop);
					for (int i = 0; i < plan.Length; i++)
					{
						sold.Add(randomized && plan[i].Replaced ? plan[i].Replacement : shop.Items[i].type);
					}
				}
			}
			return sold;
		}

		// How an item stays obtainable once shops are randomized ("" when it doesn't)
		private static string KeptBy(int item, HashSet<int> sold, RecipePlan recipes)
		{
			if (sold.Contains(item)) return "sold";
			if (Findability.Tier(item) != Findability.Never) return "found (tier " + Findability.Tier(item) + ")";
			if (recipes?.Availability != null && item < recipes.Availability.Length && recipes.Availability[item] != Findability.Never) return "crafted (tier " + recipes.Availability[item] + ")";
			return "";
		}

		private static string Name(int item) => Lang.GetItemNameValue(item);

		private static string Internal(int item) => item > 0 && item < ItemID.Count ? ItemID.Search.GetName(item) : DetRandom.ItemName(item);

		// Shops.tsv: one row per vanilla shop entry and simulated unlocked tier. "early": the replacement is above the unlocked
		// tier + 1 while the original is not; "category": another category than the original; "shop-only": nothing else gives
		// the original in vanilla, "kept by" says how it stays obtainable in this world. Always passes
		private static void ShopDump()
		{
			if (!ItemPool.Ready) Check.Skip("items not classified yet");
			EconomyConfig cfg = EconomyConfig.Instance;
			List<VanillaShop> shops = VanillaShops();
			if (shops.Count == 0) Check.Skip("no vanilla shop registered");
			HashSet<int> shopOnly = VanillaShopOnly();
			HashSet<int> sold = SoldSomewhere(shops);
			RecipePlan recipes = RecipeSystem.RecipeCount > 0 ? RecipePlanner.Get() : null;
			StringBuilder tsv = new();
			tsv.AppendLine("npc	npc name	shop	unlocked tier	slot	original	original name	original category	original tier	conditions	" +
				"replacement	replacement name	replacement category	replacement tier	replacement true tier	price	vanilla price	price ratio	early	other category	shop-only	kept by");
			int rows = 0, early = 0, otherCategory = 0, duplicates = 0;
			foreach (VanillaShop shop in shops)
			{
				int npc = shop.Shop.NpcType;
				string conditionsOf(int i) => string.Join(" & ", shop.Conditions[i].Select(c => c.Description?.Value ?? c.ToString()));
				for (int tier = 0; tier < Tiers.Count; tier++)
				{
					ShopSlot[] plan = Plan(shop, tier, cfg.NoDuplicatesInShop);
					HashSet<int> seen = new();
					for (int i = 0; i < plan.Length; i++)
					{
						Item original = shop.Items[i];
						int type = original.type;
						bool replaced = cfg.RandomizeShops && plan[i].Replaced;
						int replacement = replaced ? plan[i].Replacement : type;
						if (replaced && !seen.Add(replacement)) duplicates++;
						ItemInfo info = ItemPool.Info[type];
						ItemInfo rinfo = ItemPool.IsValid(replacement) ? ItemPool.Info[replacement] : info;
						bool isEarly = replaced && info.Tier <= tier && rinfo.Tier > tier + 1;
						bool isOther = replaced && rinfo.Category != info.Category;
						if (isEarly) early++;
						if (isOther) otherCategory++;
						int vanillaPrice = original.GetStoreValue();
						int price = replaced ? plan[i].Price : vanillaPrice;
						bool only = shopOnly.Contains(type);
						tsv.AppendLine(string.Join("	", NPCID.Search.GetName(npc), Lang.GetNPCNameValue(npc), shop.Shop.FullName, tier, i,
							Internal(type), Name(type), info.Category, info.Tier, conditionsOf(i),
							replaced ? Internal(replacement) : "(unchanged)", Name(replacement), rinfo.Category, rinfo.Tier, rinfo.TrueTier,
							price, vanillaPrice, vanillaPrice > 0 ? ((double)price / vanillaPrice).ToString("0.00") : "",
							isEarly ? "early" : "", isOther ? "category" : "", only ? "shop-only" : "", only ? (KeptBy(type, sold, recipes) is { Length: > 0 } k ? k : "LOST") : ""));
						rows++;
					}
				}
			}
			List<int> lost = shopOnly.Where(i => KeptBy(i, sold, recipes).Length == 0).OrderBy(i => i).ToList();
			string dir = Path.Combine(Main.SavePath, "UltimateRandomizer");
			string path = Path.Combine(dir, "Shops.tsv");
			Directory.CreateDirectory(dir);
			File.WriteAllText(path, tsv.ToString());
			log4net.ILog logger = ModContent.GetInstance<UltimateRandomizer>().Logger;
			logger.Info($"ShopDump: {shops.Count} vanilla shops, {shops.Sum(s => s.Items.Count)} entries, {rows} rows written to {path} (shops randomized: {cfg.RandomizeShops}, "
				+ $"follow progression: {cfg.ShopsFollowProgression}, no duplicates: {cfg.NoDuplicatesInShop})");
			logger.Info($"ShopDump: early {early}, other category {otherCategory}, duplicates {duplicates} (all entries at once: in game only those whose conditions are met)");
			logger.Info($"ShopDump: {shopOnly.Count} vanilla shop-only items, {lost.Count} lost in this world: {string.Join(", ", lost.Select(Name))}");
		}

		// No replacement above the unlocked tier + 1 + the shop's jitter, for entries the unlocked tier already reaches (an
		// entry of a later tier is not sold yet)
		private static void ShopsNotTooEarly()
		{
			if (!ItemPool.Ready) Check.Skip("items not classified yet");
			if (GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent) Check.Skip("shuffle or chaos mode: tiers are not followed");
			List<VanillaShop> shops = VanillaShops();
			if (shops.Count == 0) Check.Skip("no vanilla shop registered");
			List<string> failures = new();
			foreach (VanillaShop shop in shops)
			{
				for (int tier = 0; tier < Tiers.Count; tier++)
				{
					ShopSlot[] plan = Plan(shop, tier, EconomyConfig.Instance.NoDuplicatesInShop);
					foreach (ShopSlot slot in plan)
					{
						if (!slot.Replaced || ItemPool.TierOf(slot.Original) > tier) continue;
						int max = tier + 1 + ShopPlanner.TierJitterUp;
						if (ItemPool.TierOf(slot.Replacement) > max)
						{
							failures.Add($"{Lang.GetNPCNameValue(shop.Shop.NpcType)} at tier {tier}: {Name(slot.Original)} -> {Name(slot.Replacement)} (tier {ItemPool.TierOf(slot.Replacement)})");
						}
					}
				}
			}
			Check.All(failures, "Shop replacements too early");
		}

		// Vanilla shop-only items that progression or building needs: wiring, the Clentaminator and its solutions, biome seeds,
		// explosives and early ammo, storage, the Bug Net and the Mining Helmet. Each must still be sold (unchanged or as a
		// replacement) or found or crafted in this world once shops are randomized. Items of the list that something else
		// gives in vanilla are not checked (their other source stays)
		private static readonly int[] KeptShopItems =
		{
			ItemID.Wire, ItemID.Wrench, ItemID.BlueWrench, ItemID.GreenWrench, ItemID.YellowWrench, ItemID.WireCutter, ItemID.Actuator,
			ItemID.Clentaminator, ItemID.GreenSolution, ItemID.BlueSolution, ItemID.PurpleSolution, ItemID.RedSolution, ItemID.DarkBlueSolution,
			ItemID.HallowedSeeds, ItemID.CorruptSeeds, ItemID.CrimsonSeeds, ItemID.MushroomGrassSeeds, ItemID.JungleGrassSeeds, ItemID.GrassSeeds,
			ItemID.Bomb, ItemID.MusketBall, ItemID.FlintlockPistol, ItemID.MiningHelmet, ItemID.BugNet, ItemID.Bottle,
			ItemID.PiggyBank, ItemID.Safe, ItemID.DD2ElderCrystal
		};

		private static void ShopOnlyItemsKept()
		{
			if (!ItemPool.Ready) Check.Skip("items not classified yet");
			if (!EconomyConfig.Instance.RandomizeShops) Check.Skip("shops not randomized");
			List<VanillaShop> shops = VanillaShops();
			if (shops.Count == 0) Check.Skip("no vanilla shop registered");
			HashSet<int> shopOnly = VanillaShopOnly();
			HashSet<int> sold = SoldSomewhere(shops);
			RecipePlan recipes = RecipeSystem.RecipeCount > 0 ? RecipePlanner.Get() : null;
			List<string> failures = new();
			foreach (int item in KeptShopItems.Where(shopOnly.Contains))
			{
				if (KeptBy(item, sold, recipes).Length == 0) failures.Add(Name(item));
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"ShopOnlyItemsKept: checked {string.Join(", ", KeptShopItems.Where(shopOnly.Contains).Select(Name))}; "
				+ $"not shop-only in vanilla: {string.Join(", ", KeptShopItems.Where(i => !shopOnly.Contains(i)).Select(Name))}");
			Check.All(failures, "Shop-only items no longer obtainable");
		}

		// With NoDuplicatesInShop, no shop sells the same replacement twice at any tier
		private static void ShopNoDuplicates()
		{
			if (!ItemPool.Ready) Check.Skip("items not classified yet");
			if (!EconomyConfig.Instance.NoDuplicatesInShop) Check.Skip("duplicates allowed by the settings");
			List<VanillaShop> shops = VanillaShops();
			if (shops.Count == 0) Check.Skip("no vanilla shop registered");
			List<string> failures = new();
			foreach (VanillaShop shop in shops)
			{
				for (int tier = 0; tier < Tiers.Count; tier++)
				{
					HashSet<int> seen = new();
					foreach (ShopSlot slot in Plan(shop, tier, true))
					{
						if (slot.Replaced && !seen.Add(slot.Replacement)) failures.Add($"{Lang.GetNPCNameValue(shop.Shop.NpcType)} at tier {tier}: {Name(slot.Replacement)} twice");
					}
				}
			}
			Check.All(failures, "Duplicates in a shop");
		}
	}
}

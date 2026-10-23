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
			TestRunner.Register("ShopPermutationConserves", ShopPermutationConserves);
		}

		private sealed class VanillaShop
		{
			public AbstractNPCShop Shop;
			public List<Item> Items = new();
			public List<Condition[]> Conditions = new();
			// Index of each item's entry in NPCShop.Entries (-1: not an NPCShop, the Traveling Merchant)
			public List<int> Indices = new();
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
				if (shop is NPCShop npcShop)
				{
					for (int i = 0; i < npcShop.Entries.Count; i++)
					{
						NPCShop.Entry e = npcShop.Entries[i];
						if (e == null || e.Disabled || e.Item == null || e.Item.IsAir) continue;
						entry.Items.Add(e.Item);
						entry.Conditions.Add(e.Conditions?.ToArray() ?? Array.Empty<Condition>());
						entry.Indices.Add(i);
					}
				}
				else
				{
					foreach (AbstractNPCShop.Entry e in shop.ActiveEntries)
					{
						if (e?.Item == null || e.Item.IsAir) continue;
						entry.Items.Add(e.Item);
						entry.Conditions.Add(e.Conditions?.ToArray() ?? Array.Empty<Condition>());
						entry.Indices.Add(-1);
					}
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

		private static bool Permuted => EconomyConfig.Instance.RandomizeShops && EconomyConfig.Instance.ShopStyle == ShopStyle.Permutation;

		// The permutation of this world (the cached one, or built for the seed when the cache is not ready)
		private static ShopPermutation.WorldPlan PermutationPlan()
		{
			if (!SeedSystem.WorldReady) Check.Skip("no world");
			return ShopPermutation.Current ?? ShopPermutation.Build(SeedSystem.WorldSeed);
		}

		// The place of a shop item (-1: the entry is not permuted)
		private static int PlaceOf(ShopPermutation.WorldPlan plan, VanillaShop shop, int i) =>
			shop.Indices[i] >= 0 && plan.ByShop.TryGetValue(shop.Shop.FullName, out Dictionary<int, int> places) && places.TryGetValue(shop.Indices[i], out int p) ? p : -1;

		// What the shops still sell in this world: in the permutation style the entries left alone and what each place sells;
		// otherwise simulated at every tier, unchanged items and replacements
		private static HashSet<int> SoldSomewhere(List<VanillaShop> shops)
		{
			HashSet<int> sold = new();
			if (Permuted)
			{
				ShopPermutation.WorldPlan plan = PermutationPlan();
				sold.UnionWith(plan.Sold);
				foreach (VanillaShop shop in shops)
				{
					for (int i = 0; i < shop.Items.Count; i++)
					{
						if (PlaceOf(plan, shop, i) < 0) sold.Add(shop.Items[i].type);
					}
				}
				return sold;
			}
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
			if (Permuted)
			{
				PermutationDump(shops, shopOnly, sold, recipes);
				return;
			}
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

		// ShopPermutation.tsv: one row per vanilla shop entry of this world's permutation. A place shows its tier, access class
		// and group, the content it received (and where that content comes from), the surprise that replaced it, and the price
		private static void PermutationDump(List<VanillaShop> shops, HashSet<int> shopOnly, HashSet<int> sold, RecipePlan recipes)
		{
			ShopPermutation.WorldPlan plan = PermutationPlan();
			int[] groupOf = new int[plan.Places.Count];
			for (int g = 0; g < plan.Groups.Count; g++)
			{
				foreach (int p in plan.Groups[g]) groupOf[p] = g;
			}
			StringBuilder tsv = new();
			tsv.AppendLine("npc	npc name	shop	entry	conditions	original	original name	original tier	place tier	access	group	" +
				"content	content name	content tier	content from	surprise	sold	sold name	sold category	sold tier	price	vanilla price	shop-only	kept by");
			int rows = 0, home = 0;
			foreach (VanillaShop shop in shops)
			{
				int npc = shop.Shop.NpcType;
				for (int i = 0; i < shop.Items.Count; i++)
				{
					Item original = shop.Items[i];
					int type = original.type;
					int p = PlaceOf(plan, shop, i);
					string conditions = string.Join(" & ", shop.Conditions[i].Select(c => c.Description?.Value ?? c.ToString()));
					bool only = shopOnly.Contains(type);
					string kept = only ? (KeptBy(type, sold, recipes) is { Length: > 0 } k ? k : "LOST") : "";
					if (p < 0)
					{
						tsv.AppendLine(string.Join("	", NPCID.Search.GetName(npc), Lang.GetNPCNameValue(npc), shop.Shop.FullName, shop.Indices[i], conditions,
							Internal(type), Name(type), ItemPool.TierOf(type), "", "", "", "(not permuted)", "", "", "", "", Internal(type), Name(type), "", "",
							original.GetStoreValue(), original.GetStoreValue(), only ? "shop-only" : "", kept));
					}
					else
					{
						DropPermutation.Place place = plan.Places[p];
						int from = plan.ContentOf[p];
						int content = plan.Places[from].Entry.itemId;
						int final = plan.Sold[p];
						if (from == p) home++;
						ItemInfo info = ItemPool.Info[final];
						tsv.AppendLine(string.Join("	", NPCID.Search.GetName(npc), Lang.GetNPCNameValue(npc), shop.Shop.FullName, shop.Indices[i], conditions,
							Internal(type), Name(type), ItemPool.TierOf(type), place.Tier, place.Access, groupOf[p],
							Internal(content), Name(content), ItemPool.TierOf(content), plan.Shops[from] + "#" + plan.Places[from].Index,
							plan.Surprised[p] ? "surprise" : "", Internal(final), Name(final), info.Category, info.Tier,
							ShopPermutation.PriceOf(npc, shop.Shop.FullName, final), original.GetStoreValue(), only ? "shop-only" : "", kept));
					}
					rows++;
				}
			}
			List<int> lost = shopOnly.Where(i => KeptBy(i, sold, recipes).Length == 0).OrderBy(i => i).ToList();
			string dir = Path.Combine(Main.SavePath, "UltimateRandomizer");
			string path = Path.Combine(dir, "ShopPermutation.tsv");
			Directory.CreateDirectory(dir);
			File.WriteAllText(path, tsv.ToString());
			log4net.ILog logger = ModContent.GetInstance<UltimateRandomizer>().Logger;
			logger.Info($"ShopDump: permutation, {plan.Places.Count} places in {plan.Groups.Count} groups, {plan.Surprised.Count(s => s)} surprises "
				+ $"({plan.SurpriseCandidate.Count(c => c)} candidates, chance {EconomyConfig.Instance.ShopSurpriseChance}%), {home} contents left home, {rows} rows written to {path}");
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

		// The permutation of this world's seed, without surprises then with every candidate surprised: each group sells the
		// same multiset of items as in vanilla, no content stays at its place in a group of several, surprises only replace
		// candidates, and every item of a place (the shop-only ones included) is still sold unsurprised somewhere
		private static void ShopPermutationConserves()
		{
			if (!ItemPool.Ready) Check.Skip("items not classified yet");
			if (!SeedSystem.WorldReady) Check.Skip("no world");
			int seed = SeedSystem.WorldSeed;
			ShopPermutation.WorldPlan plain = ShopPermutation.Build(seed, 0f);
			if (plain.Places.Count == 0) Check.Skip("no permutable shop entry");
			List<string> failures = new();
			string Where(ShopPermutation.WorldPlan plan, int p) => $"{plan.Shops[p]}#{plan.Places[p].Index}";
			int[] seen = new int[plain.Places.Count];
			foreach (List<int> group in plain.Groups)
			{
				HashSet<int> members = group.ToHashSet();
				List<int> before = group.Select(p => plain.Places[p].Entry.itemId).OrderBy(i => i).ToList();
				List<int> after = group.Select(p => plain.Sold[p]).OrderBy(i => i).ToList();
				if (!before.SequenceEqual(after)) failures.Add($"group of {Where(plain, group[0])}: items not conserved");
				foreach (int p in group)
				{
					int from = plain.ContentOf[p];
					seen[from]++;
					if (!members.Contains(from)) failures.Add($"{Where(plain, p)}: content from another group ({Where(plain, from)})");
					if (group.Count > 1 && from == p) failures.Add($"{Where(plain, p)}: content left at its place");
				}
			}
			for (int p = 0; p < seen.Length; p++)
			{
				if (seen[p] != 1) failures.Add($"{Where(plain, p)}: content placed {seen[p]} times");
			}
			if (plain.Surprised.Any(s => s)) failures.Add("surprises with a zero chance");

			ShopPermutation.WorldPlan wild = ShopPermutation.Build(seed, 1f);
			HashSet<int> kept = new();
			for (int p = 0; p < wild.Places.Count; p++)
			{
				int content = wild.Places[wild.ContentOf[p]].Entry.itemId;
				if (wild.Surprised[p] && !wild.SurpriseCandidate[p]) failures.Add($"{Where(wild, p)}: surprise on a keeper or material");
				if (!wild.Surprised[p] && wild.Sold[p] != content) failures.Add($"{Where(wild, p)}: sells {Name(wild.Sold[p])} instead of {Name(content)} without a surprise");
				if (!wild.Surprised[p]) kept.Add(wild.Sold[p]);
			}
			HashSet<int> shopOnly = VanillaShopOnly();
			foreach (int item in wild.Places.Select(p => p.Entry.itemId).Distinct().OrderBy(i => i))
			{
				if (!kept.Contains(item)) failures.Add($"{Name(item)}{(shopOnly.Contains(item) ? " (shop-only)" : "")}: no unsurprised copy left");
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"ShopPermutationConserves: {plain.Places.Count} places, {plain.Groups.Count} groups, "
				+ $"{wild.Surprised.Count(s => s)} surprises with every candidate drawn, {shopOnly.Count(i => wild.Places.Any(p => p.Entry.itemId == i))} shop-only items permuted");
			Check.All(failures, "Shop permutation");
		}
	}
}

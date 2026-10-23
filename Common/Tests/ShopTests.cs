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
	// Shops simulated at every unlocked tier with the pure planner (ShopPlanner.PlanShop, AddedSlots) and this world's shop-only
	// items (ShopPermutation): what each vanilla shop sells instead of its items, offline review (Shops.tsv, ShopOnly.tsv) and
	// coherence checks. Read-only
	public static class ShopTests
	{
		public static void Register()
		{
			TestRunner.Register("ShopDump", ShopDump);
			TestRunner.Register("ShopsNotTooEarly", ShopsNotTooEarly);
			TestRunner.Register("ShopOnlyItemsKept", ShopOnlyItemsKept);
			TestRunner.Register("ShopNoDuplicates", ShopNoDuplicates);
			TestRunner.Register("ShopOnlyPlacement", ShopOnlyPlacement);
			TestRunner.Register("ShopOnlySlots", ShopOnlySlots);
		}

		internal sealed class VanillaShop
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
		internal static List<VanillaShop> VanillaShops()
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

		internal static RecipePlan Recipes => RecipeSystem.RecipeCount > 0 ? RecipePlanner.Get() : null;

		// The shop-only plan of this world (the cached one, or built for the seed when the cache is not ready)
		internal static ShopPermutation.WorldPlan ShopOnlyPlan()
		{
			if (!SeedSystem.WorldReady) Check.Skip("no world");
			return ShopPermutation.Current ?? ShopPermutation.Build(SeedSystem.WorldSeed, Recipes);
		}

		// The shop at a simulated progression: one slot per vanilla entry (shop.Items order), then the added slots of this
		// world's shop-only items unlocked at that tier (Original 0, Guaranteed)
		internal static ShopSlot[] Plan(ShopPermutation.WorldPlan permutation, VanillaShop shop, int tier, bool noDuplicates)
		{
			EconomyConfig cfg = EconomyConfig.Instance;
			IReadOnlyList<int> added = ShopPermutation.ItemsOf(permutation, shop.Shop.FullName);
			bool hardMode = tier >= (int)Tier.Hardmode;
			ShopSlot[] slots = ShopPlanner.PlanShop(shop.Shop.NpcType, shop.Shop.FullName, shop.Originals, tier, hardMode, cfg.ShopsFollowProgression, noDuplicates, added);
			return slots.Concat(ShopPlanner.AddedSlots(shop.Shop.NpcType, shop.Shop.FullName, added, tier, hardMode)).ToArray();
		}

		// Sold by a vanilla shop and by nothing else in the base game: no drop, bag, recipe, gathered material, ore nor critter
		internal static HashSet<int> VanillaShopOnly()
		{
			HashSet<int> other = Obtainability.Collect(withRecipes: true, anyBlock: false, withShops: false);
			return Obtainability.ShopItems.Where(i => ItemPool.IsValid(i) && !other.Contains(i)).ToHashSet();
		}

		// Found or crafted by the item's tier + 1 (a later source leaves the item to the shops)
		private static bool Found(int item) => Findability.Tier(item) <= ItemPool.TierOf(item) + 1;

		private static bool Crafted(int item, RecipePlan recipes) =>
			recipes?.Availability != null && item < recipes.Availability.Length && recipes.Availability[item] <= ItemPool.TierOf(item) + 1;

		private static int CraftedTier(int item, RecipePlan recipes) =>
			recipes?.Availability != null && item < recipes.Availability.Length ? recipes.Availability[item] : Findability.Never;

		private static readonly int[] NeverReliableMerchants = { NPCID.SantaClaus, NPCID.Princess, NPCID.SkeletonMerchant, NPCID.TravellingMerchant };

		// Why the merchant of an added slot can't sell it at all (null: it can): its shop must exist, be a vanilla NPC shop and
		// belong to a merchant with a known arrival
		private static string MerchantProblem(ShopPermutation.Merchant merchant)
		{
			if (!NPCShopDatabase.TryGetNPCShop(merchant.Shop, out AbstractNPCShop found) || found is not NPCShop shop) return "shop not found";
			if (shop.NpcType != merchant.Npc) return "shop of another NPC";
			if (NeverReliableMerchants.Contains(shop.NpcType)) return "unreliable merchant " + Lang.GetNPCNameValue(shop.NpcType);
			return null;
		}

		// Sold later than wished: the merchant arrives after the item's tier + 1 (a warning, never a failure: the item is sold)
		private static bool Late(ShopPermutation.Merchant merchant, int item) => merchant.Arrival > ItemPool.TierOf(item) + 1;

		private static string Where(ShopPermutation.Merchant merchant) => $"{Lang.GetNPCNameValue(merchant.Npc)} ({merchant.Shop}, arrival tier {merchant.Arrival})";

		private static string Name(int item) => Lang.GetItemNameValue(item);

		private static string Internal(int item) => item > 0 && item < ItemID.Count ? ItemID.Search.GetName(item) : DetRandom.ItemName(item);

		// How a vanilla shop-only item stays obtainable once shops are randomized ("" when it doesn't): found or crafted in this
		// world (at any tier), left in its shop (never rolled), or sold in an added slot
		internal static string KeptBy(int item, ShopPermutation.WorldPlan plan, RecipePlan recipes)
		{
			if (Findability.Tier(item) != Findability.Never) return "found (tier " + Findability.Tier(item) + ")";
			if (CraftedTier(item, recipes) != Findability.Never) return "crafted (tier " + CraftedTier(item, recipes) + ")";
			if (!ItemPool.IsRandomizable(item)) return "never replaced";
			// A special-currency entry (defender medals) is never rolled
			if (plan.Places.Any(p => p.Original == item && p.Possible && !p.Rolled && !NeverReliableMerchants.Contains(p.Npc))) return "left in its shop";
			if (plan.SellerOf.TryGetValue(item, out int m) && MerchantProblem(plan.Merchants[m]) == null)
			{
				ShopPermutation.Merchant merchant = plan.Merchants[m];
				return $"added slot at {Where(merchant)} from tier {ShopPermutation.SaleTier(merchant, item)}{(Late(merchant, item) ? " (late)" : "")}";
			}
			return "";
		}

		// Shops.tsv: one row per vanilla shop entry and simulated unlocked tier, then one per added slot. "slot kind": a random
		// roll, an added slot (shop-only item), or unchanged; "early": the replacement is above the unlocked tier + 1 while the
		// original is not; "category": another category than the original; "shop-only": nothing else gives the original in
		// vanilla, "kept by" says how it stays obtainable in this world. ShopOnly.tsv: each shop-only item of this world and its
		// added slot. Always passes
		private static void ShopDump()
		{
			if (!ItemPool.Ready) Check.Skip("items not classified yet");
			EconomyConfig cfg = EconomyConfig.Instance;
			List<VanillaShop> shops = VanillaShops();
			if (shops.Count == 0) Check.Skip("no vanilla shop registered");
			ShopPermutation.WorldPlan permutation = ShopOnlyPlan();
			HashSet<int> shopOnly = VanillaShopOnly();
			RecipePlan recipes = Recipes;
			StringBuilder tsv = new();
			tsv.AppendLine("npc	npc name	shop	unlocked tier	slot	entry	original	original name	original category	original tier	conditions	slot kind	" +
				"replacement	replacement name	replacement category	replacement tier	replacement true tier	price	vanilla price	price ratio	early	other category	shop-only	kept by");
			int rows = 0, early = 0, otherCategory = 0, duplicates = 0;
			foreach (VanillaShop shop in shops)
			{
				int npc = shop.Shop.NpcType;
				string conditionsOf(int i) => string.Join(" & ", shop.Conditions[i].Select(c => c.Description?.Value ?? c.ToString()));
				for (int tier = 0; tier < Tiers.Count; tier++)
				{
					ShopSlot[] plan = Plan(permutation, shop, tier, cfg.NoDuplicatesInShop);
					HashSet<int> seen = new();
					for (int i = 0; i < plan.Length; i++)
					{
						if (i >= shop.Items.Count)
						{
							int added = plan[i].Replacement;
							ItemInfo ainfo = ItemPool.Info[added];
							if (!seen.Add(added)) duplicates++;
							tsv.AppendLine(string.Join("	", NPCID.Search.GetName(npc), Lang.GetNPCNameValue(npc), shop.Shop.FullName, tier, i, "", "", "", "", "",
								"from tier " + ItemPool.UnlockTierOf(added), "added slot", Internal(added), Name(added), ainfo.Category, ainfo.Tier, ainfo.TrueTier,
								plan[i].Price, "", "", "", "", "shop-only", KeptBy(added, permutation, recipes)));
							rows++;
							continue;
						}
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
						string kind = !replaced ? "unchanged" : "roll";
						tsv.AppendLine(string.Join("	", NPCID.Search.GetName(npc), Lang.GetNPCNameValue(npc), shop.Shop.FullName, tier, i, shop.Indices[i],
							Internal(type), Name(type), info.Category, info.Tier, conditionsOf(i), kind,
							replaced ? Internal(replacement) : "(unchanged)", Name(replacement), rinfo.Category, rinfo.Tier, rinfo.TrueTier,
							price, vanillaPrice, vanillaPrice > 0 ? ((double)price / vanillaPrice).ToString("0.00") : "",
							isEarly ? "early" : "", isOther ? "category" : "", only ? "shop-only" : "", only ? (KeptBy(type, permutation, recipes) is { Length: > 0 } k ? k : "LOST") : ""));
						rows++;
					}
				}
			}
			StringBuilder onlyTsv = new();
			onlyTsv.AppendLine("item	name	category	tier	unlock tier	seller	seller name	shop	arrival tier	sale tier	late	over capacity	vanilla sellers");
			foreach (int item in permutation.ShopOnly)
			{
				string sellers = string.Join(", ", permutation.Places.Where(p => p.Original == item).Select(p => $"{Lang.GetNPCNameValue(p.Npc)} ({p.Shop}#{p.Index}, tier {p.Tier})"));
				if (!permutation.SellerOf.TryGetValue(item, out int m))
				{
					onlyTsv.AppendLine(string.Join("	", Internal(item), Name(item), ItemPool.Info[item].Category, ItemPool.TierOf(item), ItemPool.UnlockTierOf(item), "", "", "", "", "", "", "", sellers));
					continue;
				}
				ShopPermutation.Merchant merchant = permutation.Merchants[m];
				onlyTsv.AppendLine(string.Join("	", Internal(item), Name(item), ItemPool.Info[item].Category, ItemPool.TierOf(item), ItemPool.UnlockTierOf(item),
					NPCID.Search.GetName(merchant.Npc), Lang.GetNPCNameValue(merchant.Npc), merchant.Shop, merchant.Arrival, ShopPermutation.SaleTier(merchant, item),
					Late(merchant, item) ? "late" : "", permutation.OverCapacity.Contains(item) ? "over" : "", sellers));
			}
			List<int> lost = shopOnly.Where(i => KeptBy(i, permutation, recipes).Length == 0).OrderBy(i => i).ToList();
			string dir = Path.Combine(Main.SavePath, "UltimateRandomizer");
			Directory.CreateDirectory(dir);
			string path = Path.Combine(dir, "Shops.tsv");
			File.WriteAllText(path, tsv.ToString());
			File.WriteAllText(Path.Combine(dir, "ShopOnly.tsv"), onlyTsv.ToString());
			log4net.ILog logger = ModContent.GetInstance<UltimateRandomizer>().Logger;
			logger.Info($"ShopDump: {shops.Count} vanilla shops, {shops.Sum(s => s.Items.Count)} entries, {rows} rows written to {path} (shops randomized: {cfg.RandomizeShops}, "
				+ $"follow progression: {cfg.ShopsFollowProgression}, no duplicates: {cfg.NoDuplicatesInShop})");
			logger.Info($"ShopDump: early {early}, other category {otherCategory}, duplicates {duplicates} (all entries at once: in game only those whose conditions are met)");
			logger.Info($"ShopDump: {permutation.ShopOnly.Count} shop-only items in this world, added slots per merchant: "
				+ string.Join(", ", permutation.Merchants.Where(m => m.Items.Count > 0).Select(m => $"{Lang.GetNPCNameValue(m.Npc)} {m.Items.Count}/{m.Capacity} (arrival {m.Arrival})"))
				+ $"; {permutation.OutOfWindow.Count} late, {permutation.OverCapacity.Count} over capacity");
			logger.Info($"ShopDump: {shopOnly.Count} vanilla shop-only items, {lost.Count} lost in this world: {string.Join(", ", lost.Select(Name))}");
		}

		// No roll above the unlocked tier + 1 + the shop's jitter, for entries the unlocked tier already reaches (an entry of a
		// later tier is not sold yet); no added slot before its item's unlock tier
		private static void ShopsNotTooEarly()
		{
			if (!ItemPool.Ready) Check.Skip("items not classified yet");
			if (GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent) Check.Skip("shuffle or chaos mode: tiers are not followed");
			List<VanillaShop> shops = VanillaShops();
			if (shops.Count == 0) Check.Skip("no vanilla shop registered");
			ShopPermutation.WorldPlan permutation = ShopOnlyPlan();
			List<string> failures = new();
			foreach (VanillaShop shop in shops)
			{
				for (int tier = 0; tier < Tiers.Count; tier++)
				{
					ShopSlot[] plan = Plan(permutation, shop, tier, EconomyConfig.Instance.NoDuplicatesInShop);
					foreach (ShopSlot slot in plan)
					{
						if (slot.Guaranteed)
						{
							if (ItemPool.UnlockTierOf(slot.Replacement) > tier) failures.Add($"{Lang.GetNPCNameValue(shop.Shop.NpcType)} at tier {tier}: added {Name(slot.Replacement)} (unlock tier {ItemPool.UnlockTierOf(slot.Replacement)})");
							continue;
						}
						if (!slot.Replaced || ItemPool.TierOf(slot.Original) > tier) continue;
						int max = tier + 1 + ShopPlanner.TierJitterUp;
						if (ItemPool.TierOf(slot.Replacement) > max)
						{
							failures.Add($"{Lang.GetNPCNameValue(shop.Shop.NpcType)} at tier {tier}: {Name(slot.Original)} -> {Name(slot.Replacement)} (tier {ItemPool.TierOf(slot.Replacement)})");
						}
					}
				}
			}
			Check.All(failures, "Shop items too early");
		}

		// Every vanilla shop-only item (the wiring, Clentaminator, biome seeds, bombs, storage... among them) that a rolled entry
		// sells in this world stays obtainable once shops are randomized: found or crafted in this world at any tier, or sold in
		// an added slot of a reliable merchant. Only an item obtainable nowhere fails; late sales are logged
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
			ShopPermutation.WorldPlan plan = ShopOnlyPlan();
			if (plan.Places.Count == 0) Check.Skip("no vanilla shop registered");
			HashSet<int> shopOnly = VanillaShopOnly();
			RecipePlan recipes = Recipes;
			// Sold in this world by a vanilla entry (its world-wide conditions hold)
			HashSet<int> soldHere = plan.Places.Where(p => p.Possible && !p.Conditions.Contains(Condition.Multiplayer)).Select(p => p.Original).ToHashSet();
			List<string> failures = new();
			int late = 0;
			foreach (int item in shopOnly.Where(soldHere.Contains).OrderBy(i => i))
			{
				string kept = KeptBy(item, plan, recipes);
				if (kept.EndsWith("(late)")) late++;
				if (kept.Length > 0) continue;
				string why = plan.SellerOf.TryGetValue(item, out int m) ? MerchantProblem(plan.Merchants[m]) + " at " + Where(plan.Merchants[m]) : "not sold";
				failures.Add($"{Name(item)}: {why}");
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"ShopOnlyItemsKept: {shopOnly.Count(soldHere.Contains)} vanilla shop-only items checked, {late} sold late "
				+ $"(merchant arriving after the item's tier + 1); "
				+ string.Join(", ", KeptShopItems.Where(shopOnly.Contains).Select(i => $"{Name(i)}: {(KeptBy(i, plan, recipes) is { Length: > 0 } k ? k : "LOST")}")));
			Check.All(failures, "Shop-only items no longer obtainable");
		}

		// With NoDuplicatesInShop, no shop sells the same item twice at any tier (rolls and added slots together)
		private static void ShopNoDuplicates()
		{
			if (!ItemPool.Ready) Check.Skip("items not classified yet");
			if (!EconomyConfig.Instance.NoDuplicatesInShop) Check.Skip("duplicates allowed by the settings");
			List<VanillaShop> shops = VanillaShops();
			if (shops.Count == 0) Check.Skip("no vanilla shop registered");
			ShopPermutation.WorldPlan permutation = ShopOnlyPlan();
			List<string> failures = new();
			foreach (VanillaShop shop in shops)
			{
				for (int tier = 0; tier < Tiers.Count; tier++)
				{
					HashSet<int> seen = new();
					foreach (ShopSlot slot in Plan(permutation, shop, tier, true))
					{
						if (slot.Replaced && !seen.Add(slot.Replacement)) failures.Add($"{Lang.GetNPCNameValue(shop.Shop.NpcType)} at tier {tier}: {Name(slot.Replacement)} twice");
					}
				}
			}
			Check.All(failures, "Duplicates in a shop");
		}

		// This world's shop-only items, computed again from the shop database, Findability and the recipe plan (sources by the
		// item's tier + 1), are exactly the plan's; each has one added slot at a reliable merchant, shown from its unlock tier
		// (not before), no shop gets more than its capacity unless every shop is full, no roll of the shop gives the item again,
		// and the same seed gives the same merchants. Merchants arriving after the item's tier + 1 are logged (late, not a
		// failure). None in Shuffle mode
		private static void ShopOnlyPlacement()
		{
			if (!ItemPool.Ready) Check.Skip("items not classified yet");
			if (!EconomyConfig.Instance.RandomizeShops) Check.Skip("shops not randomized");
			ShopPermutation.WorldPlan plan = ShopOnlyPlan();
			if (plan.Places.Count == 0) Check.Skip("no vanilla shop registered");
			RecipePlan recipes = Recipes;
			List<string> failures = new();
			if (GeneralConfig.Instance.RandomizerMode == RandomizerMode.Shuffle)
			{
				foreach (int item in plan.ShopOnly) failures.Add($"{Name(item)}: placed in Shuffle mode");
				Check.All(failures, "Shop-only items");
				return;
			}

			HashSet<int> expected = new();
			foreach (NPCShop shop in ShopPermutation.Shops())
			{
				foreach (NPCShop.Entry entry in shop.Entries)
				{
					if (entry == null || entry.Disabled || entry.Item == null || entry.Item.IsAir || entry.Item.type >= ItemID.Count) continue;
					int item = entry.Item.type;
					if (entry.Item.shopSpecialCurrency != -1 || !ItemPool.IsRandomizable(item) || Found(item) || Crafted(item, recipes)) continue;
					if (entry.Conditions.Any(c => c != null && ShopPermutation.Classify(c) == ShopPermutation.ConditionKind.World && !c.IsMet())) continue;
					// Same plan solo and hosted
					if (entry.Conditions.Contains(Condition.Multiplayer)) continue;
					expected.Add(item);
				}
			}
			foreach (int item in expected.Where(i => !plan.ShopOnly.Contains(i))) failures.Add($"{Name(item)}: shop-only, not in the plan");
			foreach (int item in plan.ShopOnly.Where(i => !expected.Contains(i))) failures.Add($"{Name(item)}: in the plan, but not shop-only");

			List<string> late = new();
			foreach (int item in plan.ShopOnly)
			{
				if (!plan.SellerOf.TryGetValue(item, out int m))
				{
					failures.Add($"{Name(item)}: sold nowhere");
					continue;
				}
				ShopPermutation.Merchant merchant = plan.Merchants[m];
				if (MerchantProblem(merchant) is string problem) failures.Add($"{Name(item)} at {Where(merchant)}: {problem}");
				if (plan.Merchants.Count(x => x.Items.Contains(item)) != 1) failures.Add($"{Name(item)}: not exactly one added slot");
				if (!ShopPermutation.ItemsOf(plan, merchant.Shop).Contains(item)) failures.Add($"{Name(item)}: missing from the items of {merchant.Shop}");
				int unlock = ItemPool.UnlockTierOf(item);
				if (!ShopPermutation.Unlocked(item, unlock, unlock >= (int)Tier.Hardmode)) failures.Add($"{Name(item)}: not sold at its unlock tier {unlock}");
				if (unlock > 0 && ShopPermutation.Unlocked(item, unlock - 1, unlock - 1 >= (int)Tier.Hardmode)) failures.Add($"{Name(item)}: sold before its unlock tier {unlock}");
				if (Late(merchant, item)) late.Add($"{Name(item)} (tier {ItemPool.TierOf(item)}) at {Where(merchant)}");
			}
			// Capacity: exceeded only once every merchant is full
			bool allFull = plan.Merchants.All(m => m.Items.Count >= m.Capacity);
			foreach (ShopPermutation.Merchant merchant in plan.Merchants)
			{
				if (merchant.Items.Count > merchant.Capacity && !allFull) failures.Add($"{Where(merchant)}: {merchant.Items.Count} added slots for a capacity of {merchant.Capacity}, other shops have room");
			}
			// Late only when every merchant in time was full (or none exists)
			foreach (int item in plan.OutOfWindow)
			{
				int itemTier = ItemPool.TierOf(item);
				if (plan.Merchants.Any(m => m.Arrival <= itemTier + 1 && m.Items.Count < m.Capacity)) failures.Add($"{Name(item)}: sold late while a merchant in time has room");
			}

			// The rolls of the shop never give a shop-only item it sells (duplicates)
			if (EconomyConfig.Instance.NoDuplicatesInShop)
			{
				foreach (VanillaShop shop in VanillaShops())
				{
					IReadOnlyList<int> sold = ShopPermutation.ItemsOf(plan, shop.Shop.FullName);
					if (sold.Count == 0) continue;
					for (int tier = 0; tier < Tiers.Count; tier++)
					{
						foreach (ShopSlot slot in Plan(plan, shop, tier, true))
						{
							if (slot.Replaced && !slot.Guaranteed && sold.Contains(slot.Replacement)) failures.Add($"{shop.Shop.FullName} at tier {tier}: a roll gives the shop-only {Name(slot.Replacement)}");
						}
					}
				}
			}

			ShopPermutation.WorldPlan again = ShopPermutation.Build(SeedSystem.WorldSeed, recipes);
			foreach (int item in plan.ShopOnly)
			{
				string a = plan.SellerOf.TryGetValue(item, out int p) ? plan.Merchants[p].Shop : "-";
				string b = again.SellerOf.TryGetValue(item, out int q) ? again.Merchants[q].Shop : "-";
				if (a != b) failures.Add($"{Name(item)}: {a} then {b} for the same seed");
			}
			log4net.ILog logger = ModContent.GetInstance<UltimateRandomizer>().Logger;
			logger.Info($"ShopOnlyPlacement: {plan.ShopOnly.Count} shop-only items, added slots per merchant (used/capacity): "
				+ string.Join(", ", plan.Merchants.Select(m => $"{Lang.GetNPCNameValue(m.Npc)} {m.Items.Count}/{m.Capacity} (arrival {m.Arrival})"))
				+ $"; {plan.OverCapacity.Count} over capacity");
			if (late.Count > 0) logger.Warn($"ShopOnlyPlacement: {late.Count} shop-only items sold late: " + string.Join(", ", late));
			Check.All(failures, "Shop-only items");
		}

		// Each shop with added slots, filled as the game fills it right now (NPCShop.FillShop: the entries whose conditions are
		// met, no NPC spawned, no ModifyActiveShop), then rolled and given its added slots as ShopNPC.ModifyActiveShop does:
		// every added item unlocked at this progression is in the shop, at its own price, and only rolled slots ever give way
		private static void ShopOnlySlots()
		{
			if (!ItemPool.Ready) Check.Skip("items not classified yet");
			if (!EconomyConfig.Instance.RandomizeShops) Check.Skip("shops not randomized");
			ShopPermutation.WorldPlan plan = ShopOnlyPlan();
			if (plan.ShopOnly.Count == 0) Check.Skip("no shop-only item in this world");
			EconomyConfig cfg = EconomyConfig.Instance;
			List<string> failures = new();
			int shown = 0, displaced = 0;
			foreach (string shopName in plan.ShopItems.Keys.OrderBy(n => n, StringComparer.Ordinal))
			{
				if (!NPCShopDatabase.TryGetNPCShop(shopName, out AbstractNPCShop found) || found is not NPCShop shop)
				{
					failures.Add($"{shopName}: shop not found");
					continue;
				}
				Item[] items = new Item[Chest.maxItems];
				try
				{
					shop.FillShop(items, new NPC(), out _);
				}
				catch (Exception e)
				{
					failures.Add($"{shopName}: FillShop failed ({e.Message})");
					continue;
				}
				int[] originals = items.Select(i => i == null || i.IsAir || i.shopSpecialCurrency != -1 ? 0 : i.type).ToArray();
				IReadOnlyList<int> shopOnly = ShopPermutation.ItemsOf(plan, shopName);
				ShopSlot[] rolls = ShopPlanner.PlanShop(shop.NpcType, shopName, originals, ProgressionSystem.UnlockedTier, Main.hardMode, cfg.ShopsFollowProgression, cfg.NoDuplicatesInShop, shopOnly);
				bool[] rolled = rolls.Select(r => r.Replaced).ToArray();
				Item[] before = (Item[])items.Clone();
				List<ShopSlot> added = ShopPlanner.AddedSlots(shop.NpcType, shopName, shopOnly, ProgressionSystem.UnlockedTier, Main.hardMode);
				int[] where = ShopPermutation.Append(items, ShopPermutation.SlotLimit(shop), rolled, added);
				for (int a = 0; a < added.Count; a++)
				{
					int item = added[a].Replacement;
					int s = where[a];
					if (s < 0)
					{
						failures.Add($"{shopName}: no room for {Name(item)}");
						continue;
					}
					shown++;
					if (items[s]?.type != item || items[s].shopCustomPrice != added[a].Price) failures.Add($"{shopName} slot {s}: {Name(item)} not sold there at its price");
					if (before[s] != null)
					{
						displaced++;
						if (!rolled[s]) failures.Add($"{shopName} slot {s}: {Name(item)} took the place of a slot that is not rolled ({Name(before[s].type)})");
					}
				}
				foreach (int item in shopOnly.Where(i => ShopPermutation.Unlocked(i, ProgressionSystem.UnlockedTier, Main.hardMode)))
				{
					if (!added.Any(x => x.Replacement == item)) failures.Add($"{shopName}: {Name(item)} unlocked but not added");
				}
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"ShopOnlySlots: {shown} added slots shown right now, {displaced} of them in place of a roll (shop full)");
			Check.All(failures, "Shop-only slots");
			if (shown == 0) Check.Skip("no added slot is unlocked right now");
		}
	}
}

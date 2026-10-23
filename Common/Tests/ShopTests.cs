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
	// Shops simulated at every unlocked tier with the pure planner (ShopPlanner.PlanShop) and this world's shop-only items
	// (ShopPermutation): what each vanilla shop sells instead of its items, offline review (Shops.tsv, ShopOnly.tsv) and
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

		// The shop-only item each entry sells instead of a roll (0: a roll)
		private static int[] Guarantees(ShopPermutation.WorldPlan plan, VanillaShop shop) =>
			shop.Indices.Select(i => i >= 0 ? ShopPermutation.GuaranteedAt(plan, shop.Shop.FullName, i) : 0).ToArray();

		internal static ShopSlot[] Plan(ShopPermutation.WorldPlan permutation, VanillaShop shop, int tier, bool noDuplicates)
		{
			EconomyConfig cfg = EconomyConfig.Instance;
			return ShopPlanner.PlanShop(shop.Shop.NpcType, shop.Shop.FullName, shop.Originals, tier, tier >= (int)Tier.Hardmode, cfg.ShopsFollowProgression, noDuplicates,
				Guarantees(permutation, shop), ShopPermutation.ItemsOf(permutation, shop.Shop.FullName));
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

		// Conditions a reliable place never has, whatever ShopPermutation.Classify says (a wrong classification fails here)
		private static readonly Condition[] NeverReliable =
		{
			Condition.Christmas, Condition.Halloween, Condition.BloodMoon, Condition.Eclipse, Condition.EclipseOrBloodMoon, Condition.Thunderstorm, Condition.BirthdayParty,
			Condition.LanternNight, Condition.HappyWindyDay, Condition.InGraveyard, Condition.InSpace, Condition.InUnderworld, Condition.InUnderworldHeight,
			Condition.PreHardmode, Condition.InRain, Condition.InSandstorm, Condition.InOldOneArmy, Condition.IsNpcShimmered, Condition.Multiplayer,
			Condition.DownedClown, Condition.DownedFrostLegion
		};

		private static readonly int[] NeverReliableMerchants = { NPCID.SantaClaus, NPCID.Princess, NPCID.SkeletonMerchant, NPCID.TravellingMerchant };

		// Why a place can't hold a shop-only item, read again from the live shop database (null: it can): an entry that is a
		// roll, of a merchant with a known arrival, without situational condition, its world-wide conditions met, and its tier
		// (arrival, conditions) within one tier of the item's, never before hardmode for a hardmode item nor before the item's
		// tier for a hard gate
		private static string PlaceProblem(ShopPermutation.Place place, int item)
		{
			if (!NPCShopDatabase.TryGetNPCShop(place.Shop, out AbstractNPCShop found) || found is not NPCShop shop || place.Index >= shop.Entries.Count) return "entry not found";
			NPCShop.Entry entry = shop.Entries[place.Index];
			if (entry == null || entry.Disabled || entry.Item == null || entry.Item.type != place.Original) return "entry changed";
			if (entry.Item.shopSpecialCurrency != -1 || !ItemPool.IsRandomizable(entry.Item.type)) return "not a rolled slot";
			if (NeverReliableMerchants.Contains(shop.NpcType)) return "unreliable merchant " + Lang.GetNPCNameValue(shop.NpcType);
			int tier = ShopPermutation.ArrivalTier(shop.NpcType);
			foreach (Condition c in entry.Conditions)
			{
				if (c == null) continue;
				string what = c.Description?.Value ?? c.ToString();
				ShopPermutation.ConditionKind kind = ShopPermutation.Classify(c);
				if (NeverReliable.Contains(c) || kind == ShopPermutation.ConditionKind.Situational) return "situational condition " + what;
				if (kind == ShopPermutation.ConditionKind.World && !c.IsMet()) return "condition false in this world: " + what;
				if (kind == ShopPermutation.ConditionKind.Progression) tier = Math.Max(tier, ShopPermutation.ConditionTier(c));
			}
			tier = Tiers.Clamp(tier);
			int itemTier = ItemPool.TierOf(item);
			if (tier != place.Tier) return $"place tier {place.Tier} planned, {tier} read";
			if (itemTier > tier + 1) return $"sold too early (place tier {tier}, item tier {itemTier})";
			if (tier > itemTier + 1) return $"sold too late (place tier {tier}, item tier {itemTier})";
			if (itemTier >= (int)Tier.Hardmode && tier < (int)Tier.Hardmode) return $"hardmode item sold before hardmode (place tier {tier})";
			if (ItemPool.IsHardGate(item) && tier < itemTier) return $"hard gate sold before its tier (place tier {tier}, item tier {itemTier})";
			return null;
		}

		private static string Where(ShopPermutation.Place place) => $"{Lang.GetNPCNameValue(place.Npc)} ({place.Shop}#{place.Index}, tier {place.Tier}{(place.Moon ? ", moon" : "")})";

		private static string Name(int item) => Lang.GetItemNameValue(item);

		private static string Internal(int item) => item > 0 && item < ItemID.Count ? ItemID.Search.GetName(item) : DetRandom.ItemName(item);

		// How a vanilla shop-only item stays obtainable once shops are randomized ("" when it doesn't): found, crafted, left in
		// its shop (never rolled), or sold as a shop-only item at a reliable place
		internal static string KeptBy(int item, ShopPermutation.WorldPlan plan, RecipePlan recipes)
		{
			if (Found(item)) return "found (tier " + Findability.Tier(item) + ")";
			if (Crafted(item, recipes)) return "crafted (tier " + recipes.Availability[item] + ")";
			if (!ItemPool.IsRandomizable(item)) return "never replaced";
			// A special-currency entry (defender medals) is never rolled
			if (plan.Places.Any(p => p.Original == item && p.Possible && !p.Rolled && !NeverReliableMerchants.Contains(p.Npc))) return "left in its shop";
			if (plan.SellerOf.TryGetValue(item, out int p))
			{
				string problem = PlaceProblem(plan.Places[p], item);
				return problem == null ? "sold by " + Where(plan.Places[p]) : "";
			}
			return "";
		}

		// Shops.tsv: one row per vanilla shop entry and simulated unlocked tier. "slot": a random roll, the shop-only item placed
		// there, or unchanged; "early": the replacement is above the unlocked tier + 1 while the original is not; "category":
		// another category than the original; "shop-only": nothing else gives the original in vanilla, "kept by" says how it
		// stays obtainable in this world. ShopOnly.tsv: each shop-only item of this world and where it is sold. Always passes
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
						string kind = !replaced ? "unchanged" : plan[i].Guaranteed ? "shop-only item" : "roll";
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
			onlyTsv.AppendLine("item	name	category	tier	seller	seller name	shop	entry	place original	place tier	conditions	moon	problem	vanilla sellers");
			foreach (int item in permutation.ShopOnly)
			{
				string sellers = string.Join(", ", permutation.Places.Where(p => p.Original == item).Select(Where));
				if (!permutation.SellerOf.TryGetValue(item, out int p))
				{
					onlyTsv.AppendLine(string.Join("	", Internal(item), Name(item), ItemPool.Info[item].Category, ItemPool.TierOf(item), "", "", "", "", "", "", "", "", "NOT SOLD", sellers));
					continue;
				}
				ShopPermutation.Place place = permutation.Places[p];
				onlyTsv.AppendLine(string.Join("	", Internal(item), Name(item), ItemPool.Info[item].Category, ItemPool.TierOf(item), NPCID.Search.GetName(place.Npc),
					Lang.GetNPCNameValue(place.Npc), place.Shop, place.Index, Internal(place.Original), place.Tier,
					string.Join(" & ", place.Conditions.Select(c => c.Description?.Value ?? c.ToString())), place.Moon ? "moon" : "", PlaceProblem(place, item) ?? "", sellers));
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
			logger.Info($"ShopDump: {permutation.ShopOnly.Count} shop-only items in this world ({permutation.Places.Count(p => p.Reliable)} reliable places, "
				+ $"{permutation.ShopOnly.Count(i => permutation.SellerOf.TryGetValue(i, out int p) && permutation.Places[p].Original == i)} at their own place, "
				+ $"{permutation.ShopOnly.Count(i => permutation.SellerOf.TryGetValue(i, out int p) && permutation.Places[p].Moon)} on a moon phase): "
				+ string.Join(", ", permutation.ShopOnly.Select(Name)));
			logger.Info($"ShopDump: {shopOnly.Count} vanilla shop-only items, {lost.Count} lost in this world: {string.Join(", ", lost.Select(Name))}");
		}

		// No roll above the unlocked tier + 1 + the shop's jitter, for entries the unlocked tier already reaches (an entry of a
		// later tier is not sold yet); a shop-only item never more than one tier above its place
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
						if (!slot.Replaced || slot.Guaranteed || ItemPool.TierOf(slot.Original) > tier) continue;
						int max = tier + 1 + ShopPlanner.TierJitterUp;
						if (ItemPool.TierOf(slot.Replacement) > max)
						{
							failures.Add($"{Lang.GetNPCNameValue(shop.Shop.NpcType)} at tier {tier}: {Name(slot.Original)} -> {Name(slot.Replacement)} (tier {ItemPool.TierOf(slot.Replacement)})");
						}
					}
				}
			}
			foreach ((int item, int p) in permutation.SellerOf)
			{
				ShopPermutation.Place place = permutation.Places[p];
				if (ItemPool.TierOf(item) > place.Tier + 1) failures.Add($"shop-only {Name(item)} (tier {ItemPool.TierOf(item)}) sold by {Where(place)}");
			}
			Check.All(failures, "Shop items too early");
		}

		// Every vanilla shop-only item (the wiring, Clentaminator, biome seeds, bombs, storage... among them) that a rolled entry
		// sells in this world stays obtainable once shops are randomized: found or crafted in this world, or sold at a reliable
		// place (checked again from the shop database: known arrival, no situational condition, one-tier window). A random roll
		// is no guarantee
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
			foreach (int item in shopOnly.Where(soldHere.Contains).OrderBy(i => i))
			{
				if (KeptBy(item, plan, recipes).Length > 0) continue;
				string why = plan.SellerOf.TryGetValue(item, out int p) ? PlaceProblem(plan.Places[p], item) + " at " + Where(plan.Places[p]) : "not sold";
				failures.Add($"{Name(item)}: {why}");
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"ShopOnlyItemsKept: {shopOnly.Count(soldHere.Contains)} vanilla shop-only items checked; "
				+ string.Join(", ", KeptShopItems.Where(shopOnly.Contains).Select(i => $"{Name(i)}: {(KeptBy(i, plan, recipes) is { Length: > 0 } k ? k : "LOST")}")));
			Check.All(failures, "Shop-only items no longer obtainable");
		}

		// With NoDuplicatesInShop, no shop sells the same item twice at any tier (rolls and shop-only items together)
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
		// item's tier + 1), are exactly the plan's, and include every vanilla shop-only item a rolled entry sells here with no
		// such source; each is sold (at least once) at a reliable place within one tier of the item, at most one shop-only item
		// per place, no roll of its shop gives it again, and the same seed gives the same places. None in Shuffle mode
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
			HashSet<int> soldByRoll = new();
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
					soldByRoll.Add(item);
					expected.Add(item);
				}
			}
			foreach (int item in expected.Where(i => !plan.ShopOnly.Contains(i))) failures.Add($"{Name(item)}: shop-only, not in the plan");
			foreach (int item in plan.ShopOnly.Where(i => !expected.Contains(i))) failures.Add($"{Name(item)}: in the plan, but not shop-only");
			foreach (int item in VanillaShopOnly().Where(i => soldByRoll.Contains(i) && !Found(i) && !Crafted(i, recipes) && !plan.ShopOnly.Contains(i)).OrderBy(i => i))
			{
				failures.Add($"{Name(item)}: vanilla shop-only, no source in time, not in the plan");
			}

			int[] copies = new int[ItemPool.Info.Length];
			for (int p = 0; p < plan.Places.Count; p++)
			{
				int item = plan.Guaranteed[p];
				if (item <= 0) continue;
				copies[item]++;
				if (PlaceProblem(plan.Places[p], item) is string problem) failures.Add($"{Name(item)} at {Where(plan.Places[p])}: {problem}");
			}
			foreach (int item in plan.ShopOnly)
			{
				if (copies[item] == 0) failures.Add($"{Name(item)}: sold nowhere");
				if (!plan.SellerOf.TryGetValue(item, out int p) || plan.Guaranteed[p] != item) failures.Add($"{Name(item)}: seller does not sell it");
			}
			foreach (int item in plan.OutOfWindow) failures.Add($"{Name(item)}: no reliable place within one tier");
			foreach (int item in plan.Unreliable) failures.Add($"{Name(item)}: no reliable place left");

			// The rolls of the shop never give a shop-only item it sells (duplicates)
			if (EconomyConfig.Instance.NoDuplicatesInShop)
			{
				foreach (VanillaShop shop in VanillaShops())
				{
					IReadOnlyCollection<int> sold = ShopPermutation.ItemsOf(plan, shop.Shop.FullName);
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
				string a = plan.SellerOf.TryGetValue(item, out int p) ? plan.Places[p].Shop + "#" + plan.Places[p].Index : "-";
				string b = again.SellerOf.TryGetValue(item, out int q) ? again.Places[q].Shop + "#" + again.Places[q].Index : "-";
				if (a != b) failures.Add($"{Name(item)}: {a} then {b} for the same seed");
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"ShopOnlyPlacement: {plan.ShopOnly.Count} shop-only items, {plan.Places.Count(p => p.Reliable)} reliable places of "
				+ $"{plan.Places.Count}: " + string.Join(", ", plan.ShopOnly.Select(i => plan.SellerOf.TryGetValue(i, out int p) ? $"{Name(i)} -> {Lang.GetNPCNameValue(plan.Places[p].Npc)}" : Name(i) + " -> ?")));
			Check.All(failures, "Shop-only items");
		}

		// Each shop selling a shop-only item, filled as the game fills it (NPCShop.FillShop: the entries whose conditions are met
		// right now, no NPC spawned, no ModifyActiveShop): SlotItems finds every placed item whose entry is open now, at a slot
		// holding that entry's vanilla item, and nothing else
		private static void ShopOnlySlots()
		{
			if (!ItemPool.Ready) Check.Skip("items not classified yet");
			if (!EconomyConfig.Instance.RandomizeShops) Check.Skip("shops not randomized");
			ShopPermutation.WorldPlan plan = ShopOnlyPlan();
			if (plan.ShopOnly.Count == 0) Check.Skip("no shop-only item in this world");
			List<string> failures = new();
			int open = 0;
			foreach (string shopName in plan.ShopItems.Keys.OrderBy(n => n, StringComparer.Ordinal))
			{
				if (!NPCShopDatabase.TryGetNPCShop(shopName, out AbstractNPCShop found) || found is not NPCShop shop)
				{
					failures.Add($"{shopName}: shop not found");
					continue;
				}
				Item[] items = new Item[Chest.maxItems];
				bool overflow;
				try
				{
					shop.FillShop(items, new NPC(), out overflow);
				}
				catch (Exception e)
				{
					failures.Add($"{shopName}: FillShop failed ({e.Message})");
					continue;
				}
				int[] slots = ShopPermutation.SlotItems(plan, shopName, items);
				for (int p = 0; p < plan.Places.Count; p++)
				{
					ShopPermutation.Place place = plan.Places[p];
					int item = plan.Guaranteed[p];
					if (item <= 0 || place.Shop != shopName || place.Index >= shop.Entries.Count || !shop.Entries[place.Index].ConditionsMet()) continue;
					open++;
					int s = Array.IndexOf(slots, item);
					if (s < 0)
					{
						// A full shop drops its last entries
						if (!overflow) failures.Add($"{Name(item)} at {Where(place)}: entry open, no slot sells it");
						continue;
					}
					if (items[s]?.type != place.Original) failures.Add($"{Name(item)} at {Where(place)}: slot {s} holds {(items[s] == null ? "nothing" : Name(items[s].type))}");
				}
				for (int s = 0; s < slots.Length; s++)
				{
					if (slots[s] > 0 && (!plan.SellerOf.TryGetValue(slots[s], out int p) || plan.Places[p].Shop != shopName)) failures.Add($"{shopName} slot {s}: {Name(slots[s])} is not placed in this shop");
				}
			}
			Check.All(failures, "Shop-only slots");
			if (open == 0) Check.Skip("no shop-only place is open right now");
		}
	}
}

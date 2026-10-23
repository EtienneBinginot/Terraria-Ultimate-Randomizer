using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	// Randomized shops keep the items only a shop gives in this world. Every shop slot is a random roll (ShopPlanner.PlanShop),
	// except the slots chosen here: each shop-only item (sold by a vanilla shop entry that is rolled, and neither found nor
	// crafted in this world within one tier of the item: Findability and the recipe plan, which count no shop) is moved to one
	// reliable place of any merchant (its own place included) and sold there instead of a roll. A reliable place: a merchant
	// whose arrival is known (not the Princess, the Skeleton Merchant, the Traveling Merchant nor Santa), and only conditions
	// that the world fixes (and that hold in this world), progression, the time of day or a moon phase (both avoided when
	// possible); its tier is the highest of the merchant's arrival and its conditions' tiers, and must be within one tier of
	// the item's (never before hardmode for a hardmode item, never before its tier for a hard gate). Built from the world
	// seed, the synced settings and plans and the shop database built at load: the same on every machine, solo or hosted.
	// Applied on the client that opens a shop (ShopNPC.ModifyActiveShop), shops being client-side; the hints read the same plan
	public static class ShopPermutation
	{
		public enum ConditionKind { World, Progression, Daily, Moon, Situational }

		// True or false for a whole world (its evil, difficulty, special seed): a place needs them all to hold here
		private static readonly HashSet<Condition> WorldWide = new()
		{
			Condition.CrimsonWorld, Condition.CorruptWorld, Condition.InClassicMode, Condition.InExpertMode, Condition.InMasterMode, Condition.InJourneyMode,
			Condition.DrunkWorld, Condition.RemixWorld, Condition.NotTheBeesWorld, Condition.ForTheWorthyWorld, Condition.TenthAnniversaryWorld,
			Condition.DontStarveWorld, Condition.NoTrapsWorld, Condition.ZenithWorld, Condition.NotDrunkWorld, Condition.NotRemixWorld, Condition.NotNotTheBeesWorld,
			Condition.NotForTheWorthy, Condition.NotTenthAnniversaryWorld, Condition.NotDontStarveWorld, Condition.NotNoTrapsWorld, Condition.NotZenithWorld
		};

		// Met every day (or nearly: not during a Blood Moon or an eclipse)
		private static readonly HashSet<Condition> Daily = new()
		{
			Condition.TimeDay, Condition.TimeNight, Condition.NightOrEclipse, Condition.NotBloodMoon, Condition.NotEclipse, Condition.NotEclipseAndNotBloodMoon
		};

		// Met one night in eight (or more): acceptable, but other places come first
		private static readonly HashSet<Condition> MoonPhases = new()
		{
			Condition.MoonPhaseFull, Condition.MoonPhaseWaningGibbous, Condition.MoonPhaseThirdQuarter, Condition.MoonPhaseWaningCrescent, Condition.MoonPhaseNew,
			Condition.MoonPhaseWaxingCrescent, Condition.MoonPhaseFirstQuarter, Condition.MoonPhaseWaxingGibbous, Condition.MoonPhasesQuarter0, Condition.MoonPhasesQuarter1,
			Condition.MoonPhasesQuarter2, Condition.MoonPhasesQuarter3, Condition.MoonPhasesHalf0, Condition.MoonPhasesHalf1, Condition.MoonPhasesEven, Condition.MoonPhasesOdd,
			Condition.MoonPhasesNearNew, Condition.MoonPhasesEvenQuarters, Condition.MoonPhasesOddQuarters, Condition.MoonPhases04, Condition.MoonPhases15,
			Condition.MoonPhases26, Condition.MoonPhases37
		};

		// Progression flags the classifier's shop table does not list
		private static readonly Dictionary<Condition, int> ExtraProgression = new()
		{
			[Condition.BloodMoonOrHardmode] = (int)Tier.Hardmode,
			[Condition.SmashedShadowOrb] = (int)Tier.PostEyeOfCthulhu
		};

		// Merchants whose arrival waits for what no table knows (the Princess: every other NPC), who only visit, or whose whole
		// shop is seasonal (Santa)
		public static readonly HashSet<int> UnreliableMerchants = new() { NPCID.Princess, NPCID.SkeletonMerchant, NPCID.TravellingMerchant, NPCID.SantaClaus };

		// Everything not listed is situational: biomes, events, weather, parties, the bestiary, golf, carried items, nearby
		// NPCs, the seasons, "until" conditions (pre-hardmode, not yet defeated) and any custom condition
		// Situational although the classifier gives them a tier: the Frost Legion waits for Christmas, the Clown for a hardmode
		// Blood Moon and some luck. Multiplayer: a server is no world setting (the same world is played solo and hosted).
		// Pirates (a map or an invasion that comes by itself in hardmode) and Martians (a probe) do follow progression
		private static readonly HashSet<Condition> NotProgression = new() { Condition.DownedFrostLegion, Condition.DownedClown, Condition.Multiplayer };

		public static ConditionKind Classify(Condition condition)
		{
			if (condition == null) return ConditionKind.Daily;
			if (NotProgression.Contains(condition)) return ConditionKind.Situational;
			if (WorldWide.Contains(condition)) return ConditionKind.World;
			if (Daily.Contains(condition)) return ConditionKind.Daily;
			if (MoonPhases.Contains(condition)) return ConditionKind.Moon;
			// An eclipse is in the classifier's table (its items wait for hardmode) but only lasts a day
			if (condition != Condition.Eclipse && ConditionTier(condition) > 0) return ConditionKind.Progression;
			return ConditionKind.Situational;
		}

		public static int ConditionTier(Condition condition) =>
			condition != null && ExtraProgression.TryGetValue(condition, out int tier) ? tier : ItemClassifier.ShopConditionTier(condition);

		// The vanilla NPC shops, in a stable order
		public static IEnumerable<NPCShop> Shops() =>
			NPCShopDatabase.AllShops.OfType<NPCShop>().Where(s => s.NpcType > 0 && s.NpcType < NPCID.Count).OrderBy(s => s.FullName, StringComparer.Ordinal);

		// When the merchant moves in: the seed's arrival order (TownNpcOrderSystem, its vanilla order when not shuffled; the plan
		// is reset when that setting changes, ShopPermutationSystem), else the vanilla arrival (Goblin Tinkerer, Wizard...)
		public static int ArrivalTier(int npcType, int seed)
		{
			int tier = TownNpcOrderSystem.ArrivalTier(npcType, seed);
			return tier >= 0 ? tier : ItemClassifier.VanillaArrivalTier(npcType);
		}

		public static int ArrivalTier(int npcType) => ArrivalTier(npcType, SeedSystem.WorldSeed);

		public sealed class Place
		{
			public string Shop;
			public int Npc;
			// Entry index in NPCShop.Entries (lookup only: the choices never depend on it)
			public int Index;
			// The entry's vanilla item
			public int Original;
			public Condition[] Conditions;
			// The highest of the merchant's arrival and the conditions' tiers
			public int Tier;
			// A random roll of ShopPlanner (randomizable item, no special currency)
			public bool Rolled;
			// The entry can be sold in this world (its world-wide conditions hold)
			public bool Possible;
			public bool Reliable;
			// Sold at a time of day only (day, night, no Blood Moon...), or on a moon phase only
			public bool Timed;
			public bool Moon;
			// Only sold on a server: never a shop-only item's source, so that a world has the same plan solo and hosted
			public bool MultiplayerOnly;
			// Why the place is not reliable ("" when it is)
			public string Why = "";
			// From the shop and the entry's item (and its occurrence among the shop's entries of that item), not the entry
			// index: entries other mods insert don't move the plan
			public int Key;
		}

		public sealed class WorldPlan
		{
			public List<Place> Places;
			// The shop-only items of this world, in placement order
			public List<int> ShopOnly;
			// Per place: the shop-only item it sells instead of a roll (0: a random roll)
			public int[] Guaranteed;
			// Shop-only item -> its place
			public Dictionary<int, int> SellerOf;
			// Shop FullName -> entry index -> place
			public Dictionary<string, Dictionary<int, int>> ByShop;
			// Shop FullName -> the shop-only items it sells (the rolls of that shop avoid them)
			public Dictionary<string, HashSet<int>> ShopItems;
			// Placed out of the one-tier window (no free reliable place in it), or left at an unreliable place (none left)
			public List<int> OutOfWindow = new();
			public List<int> Unreliable = new();
		}

		private static WorldPlan cached;

		public static void Reset() => cached = null;

		// Every entry of the vanilla NPC shops (modded items apart)
		public static List<Place> CollectPlaces(int seed)
		{
			List<Place> places = new();
			foreach (NPCShop shop in Shops())
			{
				int shopKey = DetRandom.StableHash(shop.FullName);
				Dictionary<int, int> occurrences = new();
				int arrival = ArrivalTier(shop.NpcType, seed);
				for (int i = 0; i < shop.Entries.Count; i++)
				{
					NPCShop.Entry entry = shop.Entries[i];
					if (entry == null || entry.Disabled || entry.Item == null || entry.Item.IsAir || entry.Item.type >= ItemID.Count) continue;
					Item item = entry.Item;
					int occurrence = occurrences.TryGetValue(item.type, out int n) ? n : 0;
					occurrences[item.type] = occurrence + 1;
					Condition[] conditions = entry.Conditions.Where(c => c != null).ToArray();
					Place place = new()
					{
						Shop = shop.FullName, Npc = shop.NpcType, Index = i, Original = item.type, Conditions = conditions,
						Rolled = item.shopSpecialCurrency == -1 && ItemPool.IsRandomizable(item.type),
						Key = DetRandom.Hash(seed, RollKey.ShopPermutation, shopKey, DetRandom.StableHash(ItemID.Search.GetName(item.type)), occurrence)
					};
					int tier = arrival;
					bool possible = true;
					List<string> why = new();
					if (UnreliableMerchants.Contains(shop.NpcType)) why.Add("merchant");
					foreach (Condition c in conditions)
					{
						switch (Classify(c))
						{
							case ConditionKind.World:
								if (!c.IsMet()) possible = false;
								break;
							case ConditionKind.Progression:
								tier = Math.Max(tier, ConditionTier(c));
								break;
							case ConditionKind.Daily:
								place.Timed = true;
								break;
							case ConditionKind.Moon:
								place.Moon = true;
								break;
							case ConditionKind.Situational:
								why.Add(c.Description?.Value ?? c.ToString());
								break;
						}
					}
					place.MultiplayerOnly = conditions.Contains(Condition.Multiplayer);
					if (!possible) why.Add("not in this world");
					if (!place.Rolled) why.Add("not rolled");
					place.Tier = Tiers.Clamp(tier);
					place.Possible = possible;
					place.Reliable = why.Count == 0;
					place.Why = string.Join(", ", why);
					places.Add(place);
				}
			}
			return places;
		}

		// Found (drops, bags, gathered, guaranteed sources) or crafted in this world by the item's tier + 1: Findability and the
		// recipe plan, which never count a shop. A later source does not replace the shop
		public static bool HasOtherSource(int item, RecipePlan recipes)
		{
			int crafted = recipes?.Availability != null && item < recipes.Availability.Length ? recipes.Availability[item] : Findability.Never;
			return Math.Min(Findability.Tier(item), crafted) <= ItemPool.TierOf(item) + 1;
		}

		// Never before hardmode for a hardmode item, never before the item's tier for a hard gate (ItemPool.IsHardGate)
		public static bool NotTooEarly(int placeTier, int item)
		{
			int itemTier = ItemPool.TierOf(item);
			if (itemTier >= (int)Tier.Hardmode && placeTier < (int)Tier.Hardmode) return false;
			return !ItemPool.IsHardGate(item) || placeTier >= itemTier;
		}

		// May the place sell the item: within one tier of it, and not too early
		public static bool InWindow(int placeTier, int item) => Math.Abs(placeTier - ItemPool.TierOf(item)) <= 1 && NotTooEarly(placeTier, item);

		// Sold by a rolled entry that can be sold in this world (solo as hosted), with no other source in time. None in
		// Shuffle mode: every shop roll there comes from the guaranteed permutation (ItemPool.Shuffled), which keeps every item
		public static List<int> ShopOnlyItems(List<Place> places, RecipePlan recipes)
		{
			if (GeneralConfig.Instance.RandomizerMode == RandomizerMode.Shuffle) return new List<int>();
			return places.Where(p => p.Rolled && p.Possible && !p.MultiplayerOnly).Select(p => p.Original).Distinct()
				.Where(i => !HasOtherSource(i, recipes))
				.OrderBy(ItemPool.TierOf).ThenBy(i => ItemID.Search.GetName(i), StringComparer.Ordinal).ToList();
		}

		// The whole plan for a seed (the town NPC order of that seed too); recipes: the world's recipe plan (null: crafting
		// counts for nothing). Findability is always the current world's
		public static WorldPlan Build(int seed, RecipePlan recipes)
		{
			List<Place> places = CollectPlaces(seed);
			List<int> shopOnly = ShopOnlyItems(places, recipes);
			int[] guaranteed = new int[places.Count];
			WorldPlan plan = new()
			{
				Places = places, ShopOnly = shopOnly, Guaranteed = guaranteed, SellerOf = new Dictionary<int, int>(),
				ByShop = new Dictionary<string, Dictionary<int, int>>(), ShopItems = new Dictionary<string, HashSet<int>>()
			};
			for (int p = 0; p < places.Count; p++)
			{
				if (!plan.ByShop.TryGetValue(places[p].Shop, out Dictionary<int, int> entries)) plan.ByShop[places[p].Shop] = entries = new Dictionary<int, int>();
				entries[places[p].Index] = p;
			}
			// Always sold first, then at a time of day (night-only slots...), moon phases last; then a rank from the place's and
			// the item's stable keys (ties, never in practice: shop and entry)
			(int, int, string, int) Order(int p, int item) =>
				(places[p].Moon ? 2 : places[p].Timed ? 1 : 0, DetRandom.Hash(seed, RollKey.ShopPermutation, places[p].Key, DetRandom.ItemKey(item)), places[p].Shop, places[p].Index);
			foreach (int item in shopOnly)
			{
				int itemTier = ItemPool.TierOf(item);
				List<int> free = Enumerable.Range(0, places.Count).Where(p => places[p].Reliable && guaranteed[p] == 0).ToList();
				List<int> window = free.Where(p => InWindow(places[p].Tier, item)).ToList();
				List<int> later = window.Count > 0 ? window : free.Where(p => NotTooEarly(places[p].Tier, item)).ToList();
				int chosen;
				if (window.Count > 0)
				{
					chosen = window.OrderBy(p => Order(p, item)).First();
				}
				else if (later.Count > 0)
				{
					// The nearest tier, later rather than earlier, never too early
					chosen = later.OrderBy(p => Math.Abs(places[p].Tier - itemTier)).ThenBy(p => places[p].Tier < itemTier ? 1 : 0).ThenBy(p => Order(p, item)).First();
					plan.OutOfWindow.Add(item);
				}
				else
				{
					// No reliable place left (none late enough): it stays at one of its own places
					chosen = Enumerable.Range(0, places.Count)
						.Where(p => places[p].Original == item && places[p].Rolled && places[p].Possible && !places[p].MultiplayerOnly && guaranteed[p] == 0)
						.DefaultIfEmpty(-1).First();
					if (chosen < 0) continue;
					plan.Unreliable.Add(item);
				}
				guaranteed[chosen] = item;
				plan.SellerOf[item] = chosen;
				if (!plan.ShopItems.TryGetValue(places[chosen].Shop, out HashSet<int> sold)) plan.ShopItems[places[chosen].Shop] = sold = new HashSet<int>();
				sold.Add(item);
			}
			return plan;
		}

		// The plan of the current world (cleared by WorldCaches.Reset); null before the item pool and the world exist
		public static WorldPlan Current
		{
			get
			{
				if (cached != null) return cached;
				if (!ItemPool.Ready || !SeedSystem.WorldReady) return null;
				return cached = Build(SeedSystem.WorldSeed, RecipeSystem.RecipeCount > 0 ? RecipePlanner.Get() : null);
			}
		}

		// Who sells a shop-only item in this world, and from which tier (false: not a shop-only item, or shops not randomized)
		public static bool Seller(int item, out int npc, out int tier)
		{
			npc = 0;
			tier = 0;
			WorldPlan plan = EconomyConfig.Instance.RandomizeShops ? Current : null;
			if (plan == null || !plan.SellerOf.TryGetValue(item, out int p)) return false;
			npc = plan.Places[p].Npc;
			tier = plan.Places[p].Tier;
			return true;
		}

		// The shop-only items a shop sells (empty: none)
		public static IReadOnlyCollection<int> ItemsOf(WorldPlan plan, string shopName) =>
			plan != null && shopName != null && plan.ShopItems.TryGetValue(shopName, out HashSet<int> items) ? items : Array.Empty<int>();

		// The shop-only item of an entry (0: a random roll)
		public static int GuaranteedAt(WorldPlan plan, string shopName, int entryIndex) =>
			plan != null && shopName != null && plan.ByShop.TryGetValue(shopName, out Dictionary<int, int> entries) && entries.TryGetValue(entryIndex, out int p) ? plan.Guaranteed[p] : 0;

		// Runs on the client opening the shop, after NPCShop.FillShop: replays its slot filling (entries in order, disabled
		// ones skipped, unmet ones skipped unless their slot is reserved: an air slot) to know the entry of each slot, and gives
		// the shop-only item of each slot that sells one (0 elsewhere). A slot whose item is no longer its entry's (another mod
		// changed it) gets none
		public static int[] SlotItems(WorldPlan plan, string shopName, Item[] items)
		{
			int[] result = new int[items.Length];
			if (plan == null || shopName == null || !plan.ShopItems.ContainsKey(shopName)) return result;
			if (!NPCShopDatabase.TryGetNPCShop(shopName, out AbstractNPCShop found) || found is not NPCShop shop) return result;
			int limit = shop.FillLastSlot ? items.Length : items.Length - 1;
			int slot = 0;
			for (int e = 0; e < shop.Entries.Count && slot < limit; e++)
			{
				NPCShop.Entry entry = shop.Entries[e];
				if (entry.Disabled) continue;
				bool met = entry.ConditionsMet();
				if (!met && !entry.SlotReserved) continue;
				int s = slot++;
				if (!met) continue;
				Item live = items[s];
				if (live == null || live.IsAir || live.type != entry.Item.type) continue;
				result[s] = GuaranteedAt(plan, shopName, e);
			}
			return result;
		}
	}

	public class ShopPermutationSystem : ModSystem
	{
		// The town NPC order is no world cache setting (SeedSystem.CacheSignature: changing it would wipe every cache), but the
		// places' tiers follow it: the plan and the hints built from it are dropped when it changes
		private static bool? townNpcOrder;

		public override void Load()
		{
			WorldCaches.Reset += ShopPermutation.Reset;
			SeedSystem.OnSeedChanged += FollowTownNpcOrder;
		}

		private static void FollowTownNpcOrder()
		{
			bool now = ExtrasConfig.Instance?.RandomizeTownNpcOrder ?? false;
			if (townNpcOrder.HasValue && townNpcOrder.Value != now)
			{
				ShopPermutation.Reset();
				Codex.HintFacts.Reset();
			}
			townNpcOrder = now;
		}

		public override void OnWorldLoad() => ShopPermutation.Reset();

		public override void Unload()
		{
			WorldCaches.Reset -= ShopPermutation.Reset;
			SeedSystem.OnSeedChanged -= FollowTownNpcOrder;
			townNpcOrder = null;
			ShopPermutation.Reset();
		}
	}
}

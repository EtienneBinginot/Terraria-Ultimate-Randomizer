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
	// Randomized shops keep the items only a shop gives in this world. Every vanilla shop entry is a random roll
	// (ShopPlanner.PlanShop); each shop-only item (sold by a rolled vanilla entry, and neither found nor crafted anywhere in
	// this world, at any tier: Findability and the recipe plan, which count no shop) gets an ADDED slot, after the rolled
	// entries, at one reliable merchant (known arrival: not the Princess, the Skeleton Merchant, the Traveling Merchant nor
	// Santa), shown once progression reaches the item's unlock tier. Merchants are chosen by arrival (this world's town NPC
	// order) and free capacity (Build). Built from the world seed, the synced settings and plans and the shop database built
	// at load: the same on every machine, solo or hosted. Applied on the client that opens a shop (ShopNPC.ModifyActiveShop),
	// shops being client-side; the hints read the same plan
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
			// Entry index in NPCShop.Entries
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
			// A reliable merchant, no situational condition, possible here and rolled
			public bool Reliable;
			// Only sold on a server: never makes an item shop-only, so that a world has the same plan solo and hosted
			public bool MultiplayerOnly;
			// Why the place is not reliable ("" when it is)
			public string Why = "";
		}

		// A shop that can take added slots: a reliable merchant's vanilla shop
		public sealed class Merchant
		{
			public string Shop;
			public int Npc;
			// When the merchant moves in (this world's town NPC order)
			public int Arrival;
			// Free slots left by the vanilla entries open at the same time (EstimateOpen); the added slots fill them
			public int Capacity;
			// The shop-only items added to this shop, in placement order
			public List<int> Items = new();
		}

		public sealed class WorldPlan
		{
			public List<Place> Places;
			public List<Merchant> Merchants;
			// The shop-only items of this world, in placement order
			public List<int> ShopOnly;
			// Shop-only item -> index of the merchant that sells it in an added slot
			public Dictionary<int, int> SellerOf;
			// Shop FullName -> the shop-only items added to it (the rolls of that shop avoid them)
			public Dictionary<string, List<int>> ShopItems;
			// Sold by a merchant arriving later than the item's tier + 1 (every merchant in time was full): late, still sold
			public List<int> OutOfWindow = new();
			// Every merchant was full: added beyond the estimated capacity (a full shop then gives up its last rolls for it)
			public List<int> OverCapacity = new();
			// Sold by a rolled entry and found or crafted only later than the item's tier + 1: no added slot (late is fine,
			// only an item obtainable nowhere needs one), logged for the review
			public List<int> LateSource = new();
		}

		private static WorldPlan cached;

		public static void Reset() => cached = null;

		// Every entry of the vanilla NPC shops (modded items apart)
		public static List<Place> CollectPlaces(int seed)
		{
			List<Place> places = new();
			foreach (NPCShop shop in Shops())
			{
				int arrival = ArrivalTier(shop.NpcType, seed);
				for (int i = 0; i < shop.Entries.Count; i++)
				{
					NPCShop.Entry entry = shop.Entries[i];
					if (entry == null || entry.Disabled || entry.Item == null || entry.Item.IsAir || entry.Item.type >= ItemID.Count) continue;
					Item item = entry.Item;
					Condition[] conditions = entry.Conditions.Where(c => c != null).ToArray();
					Place place = new()
					{
						Shop = shop.FullName, Npc = shop.NpcType, Index = i, Original = item.type, Conditions = conditions,
						Rolled = item.shopSpecialCurrency == -1 && ItemPool.IsRandomizable(item.type)
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

		// Situational entries (biomes, events, pylons, golf scores...) seldom open together: this many count against the capacity
		public const int SituationalReserve = 6;

		// The slots a shop fills (NPCShop.FillShop leaves the last one empty unless FillLastSlot)
		public static int SlotLimit(NPCShop shop) => shop.FillLastSlot ? Chest.maxItems : Chest.maxItems - 1;

		// The most vanilla entries open at once, estimated: for each moon phase, day and night, the entries possible in this
		// world whose moon and time conditions hold (progression ones counted as met), plus up to SituationalReserve situational
		// ones. Moon conditions only read Main.moonPhase (Condition..cctor), set for the check and restored
		public static int EstimateOpen(NPCShop shop)
		{
			List<Condition[]> plain = new();
			int situational = 0;
			foreach (NPCShop.Entry entry in shop.Entries)
			{
				if (entry == null || entry.Disabled) continue;
				Condition[] conditions = entry.Conditions.Where(c => c != null).ToArray();
				if (conditions.Any(c => Classify(c) == ConditionKind.World && !c.IsMet())) continue;
				if (conditions.Any(c => Classify(c) == ConditionKind.Situational)) situational++;
				else plain.Add(conditions);
			}
			int best = 0;
			int phase = Main.moonPhase;
			try
			{
				for (int moon = 0; moon < 8; moon++)
				{
					Main.moonPhase = moon;
					foreach (bool day in new[] { true, false })
					{
						int open = plain.Count(conditions => conditions.All(c => Classify(c) switch
						{
							ConditionKind.Moon => c.IsMet(),
							ConditionKind.Daily => c == Condition.TimeDay ? day : c == Condition.TimeNight || c == Condition.NightOrEclipse ? !day : true,
							_ => true
						}));
						best = Math.Max(best, open);
					}
				}
			}
			finally
			{
				Main.moonPhase = phase;
			}
			return best + Math.Min(situational, SituationalReserve);
		}

		// The first tier the item is found (drops, bags, gathered, guaranteed sources) or crafted in this world: Findability and
		// the recipe plan, which never count a shop (Findability.Never: obtainable nowhere but in a shop)
		public static int SourceTier(int item, RecipePlan recipes)
		{
			int crafted = recipes?.Availability != null && item < recipes.Availability.Length ? recipes.Availability[item] : Findability.Never;
			return Math.Min(Findability.Tier(item), crafted);
		}

		// Found or crafted in this world at any tier: lateness is acceptable, only an item obtainable nowhere needs a shop slot
		public static bool HasOtherSource(int item, RecipePlan recipes) => SourceTier(item, recipes) < Findability.Never;

		// Found or crafted, but only later than the item's tier + 1 (informative: no added slot)
		public static bool OnlyLateSource(int item, RecipePlan recipes)
		{
			int tier = SourceTier(item, recipes);
			return tier < Findability.Never && tier > ItemPool.TierOf(item) + 1;
		}

		// The items sold by a rolled entry that can be sold in this world (solo as hosted), in a stable order. None in Shuffle
		// mode: every shop roll there comes from the guaranteed permutation (ItemPool.Shuffled), which keeps every item
		private static List<int> RolledItems(List<Place> places)
		{
			if (GeneralConfig.Instance.RandomizerMode == RandomizerMode.Shuffle) return new List<int>();
			return places.Where(p => p.Rolled && p.Possible && !p.MultiplayerOnly).Select(p => p.Original).Distinct()
				.OrderBy(ItemPool.TierOf).ThenBy(i => ItemID.Search.GetName(i), StringComparer.Ordinal).ToList();
		}

		// Rolled items with no other source at all in this world
		public static List<int> ShopOnlyItems(List<Place> places, RecipePlan recipes) => RolledItems(places).Where(i => !HasOtherSource(i, recipes)).ToList();

		// From which progression an added slot sells its item: the item's unlock tier (no allowance for a hard gate, never before
		// hardmode for a hardmode item: ItemPool.UnlockTierOf)
		public static bool Unlocked(int item, int unlockedTier, bool hardMode)
		{
			int unlock = ItemPool.UnlockTierOf(item);
			return unlockedTier >= unlock && (unlock < (int)Tier.Hardmode || hardMode);
		}

		// The tier from which a merchant sells an added item: its arrival and the item's unlock tier
		public static int SaleTier(Merchant merchant, int item) => Math.Max(merchant.Arrival, ItemPool.UnlockTierOf(item));

		// The whole plan for a seed (the town NPC order of that seed too); recipes: the world's recipe plan (null: crafting
		// counts for nothing). Findability is always the current world's. Each shop-only item gets an added slot at one
		// reliable merchant: its own vanilla seller when reliable and in time (arrival <= item tier + 1), else among the
		// merchants arriving by its tier (then by its tier + 1) the one with the most free slots, then the earliest, then a
		// stable rank; when all of them are full, the earliest merchant with room (late), and when every shop is full, the
		// earliest merchant (beyond capacity)
		public static WorldPlan Build(int seed, RecipePlan recipes)
		{
			List<Place> places = CollectPlaces(seed);
			List<int> shopOnly = ShopOnlyItems(places, recipes);
			List<Merchant> merchants = new();
			foreach (NPCShop shop in Shops())
			{
				if (UnreliableMerchants.Contains(shop.NpcType)) continue;
				merchants.Add(new Merchant
				{
					Shop = shop.FullName, Npc = shop.NpcType, Arrival = Tiers.Clamp(ArrivalTier(shop.NpcType, seed)),
					Capacity = Math.Max(0, SlotLimit(shop) - EstimateOpen(shop))
				});
			}
			WorldPlan plan = new()
			{
				Places = places, Merchants = merchants, ShopOnly = shopOnly, SellerOf = new Dictionary<int, int>(), ShopItems = new Dictionary<string, List<int>>(),
				LateSource = RolledItems(places).Where(i => OnlyLateSource(i, recipes)).ToList()
			};
			if (merchants.Count == 0) return plan;
			int Rank(int m, int item) => DetRandom.Hash(seed, RollKey.ShopPermutation, DetRandom.StableHash(merchants[m].Shop), DetRandom.ItemKey(item));
			int Free(int m) => merchants[m].Capacity - merchants[m].Items.Count;
			foreach (int item in shopOnly)
			{
				int itemTier = ItemPool.TierOf(item);
				List<int> open = Enumerable.Range(0, merchants.Count).Where(m => Free(m) > 0).ToList();
				HashSet<string> ownShops = places.Where(p => p.Original == item && p.Reliable).Select(p => p.Shop).ToHashSet();
				List<int> own = open.Where(m => ownShops.Contains(merchants[m].Shop) && merchants[m].Arrival <= itemTier + 1).ToList();
				List<int> window = open.Where(m => merchants[m].Arrival <= itemTier).ToList();
				if (window.Count == 0) window = open.Where(m => merchants[m].Arrival <= itemTier + 1).ToList();
				int chosen;
				if (own.Count > 0)
				{
					chosen = own.OrderBy(m => merchants[m].Arrival).ThenBy(m => Rank(m, item)).First();
				}
				else if (window.Count > 0)
				{
					chosen = window.OrderByDescending(Free).ThenBy(m => merchants[m].Arrival).ThenBy(m => Rank(m, item)).First();
				}
				else if (open.Count > 0)
				{
					chosen = open.OrderBy(m => merchants[m].Arrival).ThenByDescending(Free).ThenBy(m => Rank(m, item)).First();
					plan.OutOfWindow.Add(item);
				}
				else
				{
					chosen = Enumerable.Range(0, merchants.Count).OrderBy(m => merchants[m].Arrival).ThenByDescending(Free).ThenBy(m => Rank(m, item)).First();
					plan.OverCapacity.Add(item);
					if (merchants[chosen].Arrival > itemTier + 1) plan.OutOfWindow.Add(item);
				}
				merchants[chosen].Items.Add(item);
				plan.SellerOf[item] = chosen;
				if (!plan.ShopItems.TryGetValue(merchants[chosen].Shop, out List<int> sold)) plan.ShopItems[merchants[chosen].Shop] = sold = new List<int>();
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
				int started = Findability.Version;
				WorldPlan plan = Build(SeedSystem.WorldSeed, RecipeSystem.RecipeCount > 0 ? RecipePlanner.Get() : null);
				// Findability was reset while it was built (the world evil arrived): used once, built again next time
				if (started != Findability.Version) return plan;
				ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Shop-only items: {plan.ShopOnly.Count} obtainable nowhere else (added slots), "
					+ $"{plan.LateSource.Count} other rolled shop items found or crafted only later than their tier + 1 (no slot)");
				if (plan.OutOfWindow.Count + plan.OverCapacity.Count > 0)
				{
					ModContent.GetInstance<UltimateRandomizer>().Logger.Warn($"Shop-only items: {plan.ShopOnly.Count} added slots, {plan.OutOfWindow.Count} at a merchant arriving "
						+ $"later than their tier + 1, {plan.OverCapacity.Count} beyond the estimated capacity");
				}
				return cached = plan;
			}
		}

		// Who sells a shop-only item in this world, and from which tier (false: not a shop-only item, or shops not randomized)
		public static bool Seller(int item, out int npc, out int tier)
		{
			npc = 0;
			tier = 0;
			WorldPlan plan = EconomyConfig.Instance.RandomizeShops ? Current : null;
			if (plan == null || !plan.SellerOf.TryGetValue(item, out int m)) return false;
			npc = plan.Merchants[m].Npc;
			tier = SaleTier(plan.Merchants[m], item);
			return true;
		}

		// The shop-only items added to a shop (empty: none)
		public static IReadOnlyList<int> ItemsOf(WorldPlan plan, string shopName) =>
			plan != null && shopName != null && plan.ShopItems.TryGetValue(shopName, out List<int> items) ? items : Array.Empty<int>();

		// The slots a shop fills, by name (Chest.maxItems - 1 when unknown)
		public static int SlotLimit(string shopName) =>
			shopName != null && NPCShopDatabase.TryGetNPCShop(shopName, out AbstractNPCShop found) && found is NPCShop shop ? SlotLimit(shop) : Chest.maxItems - 1;

		// Puts the added slots into a filled shop: the free slots first (null, below the limit; a reserved air slot stays), then,
		// when the shop is full, the last rolled slots (rolled: per slot, a random roll that may give way). Returns the slot
		// of each added item (-1: no room at all)
		public static int[] Append(Item[] items, int limit, IList<bool> rolled, IList<ShopSlot> added)
		{
			int[] where = new int[added.Count];
			int next = 0;
			int back = Math.Min(limit, items.Length) - 1;
			for (int a = 0; a < added.Count; a++)
			{
				while (next < limit && next < items.Length && items[next] != null) next++;
				int slot = -1;
				if (next < limit && next < items.Length)
				{
					slot = next++;
				}
				else
				{
					while (back >= 0 && !(rolled != null && back < rolled.Count && rolled[back])) back--;
					if (back >= 0) slot = back--;
				}
				where[a] = slot;
				if (slot < 0) continue;
				Item item = new(added[a].Replacement);
				item.stack = 1;
				item.shopCustomPrice = added[a].Price;
				items[slot] = item;
			}
			return where;
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

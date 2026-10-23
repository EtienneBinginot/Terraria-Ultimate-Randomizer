using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	// ShopStyle.Permutation: the vanilla shop entries are shuffled between the merchants, like the enemies' drops
	// (DropPermutation, whose groups and rank-based cycle are reused). Each permutable entry of a vanilla NPC's shop
	// (NPCShopDatabase, entry index in NPCShop.Entries) is a place; its content (the entry's item) moves to another place of
	// the same tier group and access class, and is sold with that place's conditions (moon phase, biome, bosses...). Every
	// shop keeps its slots and every item of a permuted entry is still sold somewhere. A share of the moved contents
	// (ShopSurpriseChance) becomes a random item of the same category, never the keeper copy of an item. Left as they are:
	// special-currency entries (defender medals), the Traveling Merchant (not an NPCShop), pylons, items kept by the pool
	// (DropHelper.KeepOriginal), modded items and seasonal entries (Halloween, Christmas). Computed from the world seed, the
	// synced settings and the shop database built at load (the same on every machine); applied on the client that opens a
	// shop (ModifyActiveShop), shops being client-side
	public static class ShopPermutation
	{
		// Conditions true or false for a whole world (its evil, difficulty, special seed, multiplayer): contents only move
		// between places with the same set of them (the access class), so a Crimson-only entry stays Crimson-only
		private static readonly Dictionary<Condition, string> Restrictive = new()
		{
			[Condition.CrimsonWorld] = "CrimsonWorld", [Condition.CorruptWorld] = "CorruptWorld",
			[Condition.InClassicMode] = "Classic", [Condition.InExpertMode] = "Expert", [Condition.InMasterMode] = "Master", [Condition.InJourneyMode] = "Journey",
			[Condition.Multiplayer] = "Multiplayer",
			[Condition.DrunkWorld] = "Drunk", [Condition.RemixWorld] = "Remix", [Condition.NotTheBeesWorld] = "Bees", [Condition.ForTheWorthyWorld] = "Worthy",
			[Condition.TenthAnniversaryWorld] = "Anniversary", [Condition.DontStarveWorld] = "DontStarve", [Condition.NoTrapsWorld] = "NoTraps", [Condition.ZenithWorld] = "Zenith",
			[Condition.NotDrunkWorld] = "NotDrunk", [Condition.NotRemixWorld] = "NotRemix", [Condition.NotNotTheBeesWorld] = "NotBees", [Condition.NotForTheWorthy] = "NotWorthy",
			[Condition.NotTenthAnniversaryWorld] = "NotAnniversary", [Condition.NotDontStarveWorld] = "NotDontStarve", [Condition.NotNoTrapsWorld] = "NotNoTraps",
			[Condition.NotZenithWorld] = "NotZenith"
		};

		// Seasonal entries keep their vanilla item: a content moved there would only be sold a few days a year
		private static readonly HashSet<Condition> Seasonal = new() { Condition.Christmas, Condition.Halloween };

		public static string AccessOf(IEnumerable<Condition> conditions) =>
			string.Join("+", conditions.Where(c => c != null && Restrictive.ContainsKey(c)).Select(c => Restrictive[c]).Distinct().OrderBy(n => n, StringComparer.Ordinal));

		// A shop entry that is a place (whatever the settings, so that a setting does not move the places of the world)
		public static bool IsPlace(NPCShop.Entry entry)
		{
			if (entry == null || entry.Disabled || entry.Item == null || entry.Item.IsAir) return false;
			Item item = entry.Item;
			if (item.type >= ItemID.Count || item.shopSpecialCurrency != -1 || item.createTile == TileID.TeleportationPylon) return false;
			if (DropHelper.KeepOriginal(item.type)) return false;
			return !entry.Conditions.Any(c => c != null && Seasonal.Contains(c));
		}

		// The vanilla NPC shops, in a stable order
		public static IEnumerable<NPCShop> Shops() =>
			NPCShopDatabase.AllShops.OfType<NPCShop>().Where(s => s.NpcType > 0 && s.NpcType < NPCID.Count).OrderBy(s => s.FullName, StringComparer.Ordinal);

		public static int ShopKey(string fullName) => DetRandom.Hash(RollKey.ShopPermutation, DetRandom.StableHash(fullName ?? ""));

		// When the merchant moves in: this world's arrival order (TownNpcOrderSystem, its vanilla order when not shuffled), else
		// the vanilla arrival (Goblin Tinkerer, Wizard...)
		public static int ArrivalTier(int npcType)
		{
			int tier = TownNpcOrderSystem.ArrivalTier(npcType);
			return tier >= 0 ? tier : ItemClassifier.VanillaArrivalTier(npcType);
		}

		// The tier of a place: the merchant's arrival, its conditions (hardmode, bosses...) and its own item's tier, the highest
		public static int PlaceTier(int npcType, int item, IEnumerable<Condition> conditions)
		{
			int tier = Math.Max(ArrivalTier(npcType), ItemPool.TierOf(item));
			foreach (Condition c in conditions) tier = Math.Max(tier, ItemClassifier.ShopConditionTier(c));
			return Tiers.Clamp(tier);
		}

		public sealed class WorldPlan
		{
			// DropPermutation places: Owner = merchant type, Index = entry index, OwnerKey = ShopKey, Entry.itemId = the item
			public List<DropPermutation.Place> Places;
			public List<string> Shops;
			public List<Condition[]> Conditions;
			// ContentOf[p]: the place whose content goes to place p
			public int[] ContentOf;
			public List<List<int>> Groups;
			public bool[] SurpriseCandidate;
			public bool[] Surprised;
			// What each place sells: its content, or the surprise
			public int[] Sold;
			// Shop FullName -> entry index -> place
			public Dictionary<string, Dictionary<int, int>> ByShop;
		}

		private static WorldPlan cached;

		public static void Reset() => cached = null;

		public static void CollectPlaces(out List<DropPermutation.Place> places, out List<string> shops, out List<Condition[]> conditions)
		{
			places = new List<DropPermutation.Place>();
			shops = new List<string>();
			conditions = new List<Condition[]>();
			foreach (NPCShop shop in Shops())
			{
				int key = ShopKey(shop.FullName);
				for (int i = 0; i < shop.Entries.Count; i++)
				{
					NPCShop.Entry entry = shop.Entries[i];
					if (!IsPlace(entry)) continue;
					Condition[] conds = entry.Conditions.Where(c => c != null).ToArray();
					int item = entry.Item.type;
					places.Add(new DropPermutation.Place
					{
						Owner = shop.NpcType, Index = i, OwnerKey = key, Tier = PlaceTier(shop.NpcType, item, conds), Access = AccessOf(conds),
						Entry = new DropRateInfo(item, 1, 1, 1f, null)
					});
					shops.Add(shop.FullName);
					conditions.Add(conds);
				}
			}
		}

		// Pure (with the item pool): the places whose content may become a surprise. Not a material, ore or bar, and not the
		// keeper copy: every item keeps one unchanged copy, at the most reachable place selling it (lowest tier, fewest
		// conditions, then the rank). Depends on no setting
		public static bool[] SurpriseCandidates(IReadOnlyList<DropPermutation.Place> places, IReadOnlyList<Condition[]> conditions, int[] contentOf, int seed)
		{
			Dictionary<int, int> keeper = new();
			(int, int, int, int, int, int) Order(int p) =>
				(places[p].Tier, conditions[p].Length, DropPermutation.Rank(seed, places[p]), places[p].OwnerKey, places[p].Owner, places[p].Index);
			for (int p = 0; p < places.Count; p++)
			{
				int item = places[contentOf[p]].Entry.itemId;
				if (!keeper.TryGetValue(item, out int kept) || Order(p).CompareTo(Order(kept)) < 0) keeper[item] = p;
			}
			bool[] candidate = new bool[places.Count];
			for (int p = 0; p < places.Count; p++)
			{
				int item = places[contentOf[p]].Entry.itemId;
				candidate[p] = DropPermutation.SurpriseKind(item) && keeper[item] != p;
			}
			return candidate;
		}

		public static double SurpriseRoll(int seed, DropPermutation.Place place) => new Random(DetRandom.Hash(seed, RollKey.ShopSurprise, place.OwnerKey, place.Index)).NextDouble();

		// The surprise of a place: an item of the content's category at the place's tier, everything unlocked (the place's
		// tier and conditions already follow progression), no jackpot. 0 when the roll gives the content back
		public static int Surprise(DropPermutation.Place place, int seed, int content)
		{
			RollRequest request = new()
			{
				Keys = new[] { RollKey.ShopSurprise, seed, place.OwnerKey, place.Index },
				SourceTier = place.Tier,
				OriginalItem = content,
				Policy = CategoryPolicy.SameAsOriginal,
				TierJitterDown = 0,
				TierJitterUp = 0,
				AllowJackpot = false,
				IgnoreGating = true
			};
			int item = ItemPool.Roll(request);
			return item > ItemID.None && item != content ? item : 0;
		}

		// The whole plan for a seed. surpriseChance in [0, 1] (ShopSurpriseChance when null; none in shuffle mode)
		public static WorldPlan Build(int seed, float? surpriseChance = null)
		{
			CollectPlaces(out List<DropPermutation.Place> places, out List<string> shops, out List<Condition[]> conditions);
			int[] contentOf = DropPermutation.Permute(places, seed, out List<List<int>> groups);
			float chance = surpriseChance ?? (GeneralConfig.Instance.RandomizerMode == RandomizerMode.Shuffle ? 0f : EconomyConfig.Instance.ShopSurpriseChance / 100f);
			chance = Math.Clamp(chance, 0f, 1f);
			bool[] candidates = SurpriseCandidates(places, conditions, contentOf, seed);
			bool[] surprised = new bool[places.Count];
			int[] sold = new int[places.Count];
			Dictionary<string, Dictionary<int, int>> byShop = new();
			for (int p = 0; p < places.Count; p++)
			{
				int content = places[contentOf[p]].Entry.itemId;
				sold[p] = content;
				if (chance > 0f && candidates[p] && SurpriseRoll(seed, places[p]) < chance)
				{
					int item = Surprise(places[p], seed, content);
					if (item > 0)
					{
						sold[p] = item;
						surprised[p] = true;
					}
				}
				if (!byShop.TryGetValue(shops[p], out Dictionary<int, int> entries)) byShop[shops[p]] = entries = new Dictionary<int, int>();
				entries[places[p].Index] = p;
			}
			return new WorldPlan
			{
				Places = places, Shops = shops, Conditions = conditions, ContentOf = contentOf, Groups = groups,
				SurpriseCandidate = candidates, Surprised = surprised, Sold = sold, ByShop = byShop
			};
		}

		// The plan of the current world (cleared by WorldCaches.Reset); null before the item pool and the world exist
		public static WorldPlan Current
		{
			get
			{
				if (cached != null) return cached;
				if (!ItemPool.Ready || !SeedSystem.WorldReady) return null;
				return cached = Build(SeedSystem.WorldSeed);
			}
		}

		// The price of an item sold by a shop: as the random style, from the merchant, the shop and the item
		public static int PriceOf(int npcType, string shopName, int item) =>
			PriceRules.Price(item, DetRandom.Get(RollKey.Price, DetRandom.NpcKey(npcType), DetRandom.StableHash(shopName ?? ""), DetRandom.ItemKey(item)));

		// Runs on the client opening the shop, after NPCShop.FillShop: replays its slot filling (entries in order, disabled
		// ones skipped, unmet ones skipped unless their slot is reserved: an air slot) to know the entry of each slot, then
		// sells the planned item in the slots of the places. A slot whose item is no longer its entry's (another mod changed
		// it) is left alone
		public static void Apply(NPC npc, string shopName, Item[] items)
		{
			WorldPlan plan = Current;
			if (plan == null || shopName == null || !plan.ByShop.TryGetValue(shopName, out Dictionary<int, int> places)) return;
			if (!NPCShopDatabase.TryGetNPCShop(shopName, out AbstractNPCShop found) || found is not NPCShop shop) return;
			int limit = shop.FillLastSlot ? items.Length : items.Length - 1;
			int slot = 0;
			for (int e = 0; e < shop.Entries.Count && slot < limit; e++)
			{
				NPCShop.Entry entry = shop.Entries[e];
				if (entry.Disabled) continue;
				bool met = entry.ConditionsMet();
				if (!met && !entry.SlotReserved) continue;
				int s = slot++;
				if (!met || !places.TryGetValue(e, out int p)) continue;
				Item live = items[s];
				if (live == null || live.IsAir || live.type != entry.Item.type || plan.Sold[p] == live.type) continue;
				Item item = new(plan.Sold[p]);
				item.stack = 1;
				item.shopCustomPrice = PriceOf(npc?.type ?? shop.NpcType, shopName, plan.Sold[p]);
				items[s] = item;
			}
		}
	}

	public class ShopPermutationSystem : ModSystem
	{
		public override void Load() => WorldCaches.Reset += ShopPermutation.Reset;

		public override void OnWorldLoad() => ShopPermutation.Reset();

		public override void Unload()
		{
			WorldCaches.Reset -= ShopPermutation.Reset;
			ShopPermutation.Reset();
		}
	}
}

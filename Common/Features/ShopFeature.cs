using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	public class ShopNPC : GlobalNPC
	{
		// Load time: remember what shops sell, so the guaranteed shuffle knows these items are obtainable
		public override void ModifyShop(NPCShop shop)
		{
			foreach (NPCShop.Entry entry in shop.Entries)
			{
				if (entry?.Item == null || entry.Item.IsAir) continue;
				Obtainability.ShopItems.Add(entry.Item.type);
				Obtainability.ShopEntries.Add((shop.NpcType, entry.Item.type, entry.Conditions.ToArray()));
			}
		}

		// Runs on the local client each time a shop is opened. The original item is part of the key, so each replacement keeps
		// the original's unlock conditions (moon phase, progression...); this world's shop-only items of the shop
		// (ShopPermutation) come in added slots after them, once progression unlocks them
		public override void ModifyActiveShop(NPC npc, string shopName, Item[] items)
		{
			EconomyConfig cfg = EconomyConfig.Instance;
			if (!cfg.RandomizeShops || !SeedSystem.WorldReady || !ItemPool.Ready) return;
			// null = unused slot, air = reserved slot whose conditions are not met: both must stay as they are. Items bought
			// with a special currency (defender medals) are left alone
			int[] originals = new int[items.Length];
			for (int i = 0; i < items.Length; i++)
			{
				Item original = items[i];
				if (original == null || original.IsAir || original.shopSpecialCurrency != -1) continue;
				originals[i] = original.type;
			}
			ShopPermutation.WorldPlan permutation = ShopPermutation.Current;
			IReadOnlyList<int> shopOnly = ShopPermutation.ItemsOf(permutation, shopName);
			ShopSlot[] plan = ShopPlanner.PlanShop(npc.type, shopName, originals, ProgressionSystem.UnlockedTier, Main.hardMode, cfg.ShopsFollowProgression, cfg.NoDuplicatesInShop,
				ShopPermutation.ItemsOfNpc(permutation, npc.type));
			bool[] rolled = new bool[items.Length];
			for (int i = 0; i < items.Length; i++)
			{
				if (!plan[i].Replaced) continue;
				Item item = new(plan[i].Replacement);
				item.stack = 1;
				item.shopCustomPrice = plan[i].Price;
				items[i] = item;
				rolled[i] = true;
			}
			List<ShopSlot> added = ShopPlanner.AddedSlots(npc.type, shopName, shopOnly, ProgressionSystem.UnlockedTier, Main.hardMode);
			if (added.Count > 0) ShopPermutation.Append(items, ShopPermutation.SlotLimit(shopName), rolled, added);
		}
	}

	// One shop slot as planned: the original item, what replaces it (0 when left as it is), its price, and whether it is an
	// added slot selling a shop-only item (ShopPermutation, Original 0) rather than a roll
	public readonly record struct ShopSlot(int Original, int Replacement, int Price, bool Guaranteed = false)
	{
		public bool Replaced => Replacement > 0;
	}

	// Pure shop plan: only the world seed, the settings and the given progression (no local player state), so the shop dump
	// and the tests see what a player would see at any progression
	public static class ShopPlanner
	{
		// Rolls tried for one slot before falling back to another item of its category
		public const int Attempts = 12;

		// originalTypes in slot order (0: slot left alone). The duplicate check follows that order, as the shop fills its slots:
		// the result for a slot depends on the items sold before it (only those whose conditions are met in game).
		// avoid: the shop-only items added to the merchant's shops (ShopPermutation.ItemsOfNpc), which no roll gives with
		// noDuplicates (whether or not they are unlocked yet). Each roll avoids, in this order: a duplicate (or an item that
		// another tab of the merchant sells, OwnTab), a merchant beyond the item's share of shops (Spread), an item worth more
		// than its tier affords (PriceRules.Overpriced); when every roll breaks a rule, another item of its category that
		// breaks fewer. Weapons, armor, accessories and tools never go above the unlocked tier (no post-Moon Lord hamaxe
		// before the Moon Lord), unless the vanilla entry itself is of a later tier
		public static ShopSlot[] PlanShop(int npcType, string shopName, IList<int> originalTypes, int unlockedTier, bool hardMode, bool followProgression, bool noDuplicates,
			IEnumerable<int> avoid = null)
		{
			int npcKey = DetRandom.NpcKey(npcType);
			int shopKey = DetRandom.StableHash(shopName ?? "");
			// Shuffle mode: the permutation keeps every item somewhere, only the duplicates are re-rolled (as they always were)
			bool shuffle = GeneralConfig.Instance.RandomizerMode == RandomizerMode.Shuffle;
			ShopSlot[] slots = new ShopSlot[originalTypes.Count];
			HashSet<int> used = new();
			if (noDuplicates && avoid != null) used.UnionWith(avoid);
			int Penalty(int item)
			{
				int penalty = 0;
				if (noDuplicates && (used.Contains(item) || !shuffle && !OwnTab(item, npcType, shopName))) penalty += 4;
				if (shuffle) return penalty;
				if (!Spread(item, npcType)) penalty += 2;
				if (PriceRules.Overpriced(item)) penalty += 1;
				return penalty;
			}
			for (int i = 0; i < originalTypes.Count; i++)
			{
				int original = originalTypes[i];
				slots[i] = new ShopSlot(original, 0, 0);
				if (original <= 0 || !ItemPool.IsRandomizable(original)) continue;

				int originalTier = ItemPool.Info[original].Tier;
				int sourceTier = followProgression ? Math.Max(originalTier, unlockedTier - 1) : originalTier;
				int? maxTier = followProgression && WithinUnlockedTier(ItemPool.Info[original].Category) ? Math.Max(unlockedTier, originalTier) : null;
				int replacement = original;
				int penalty = int.MaxValue;
				for (int attempt = 0; attempt < Attempts; attempt++)
				{
					RollRequest request = new()
					{
						Keys = new[] { RollKey.Shop, npcKey, shopKey, DetRandom.ItemKey(original), attempt },
						SourceTier = Tiers.Clamp(sourceTier),
						OriginalItem = original,
						Policy = CategoryPolicy.SameAsOriginal,
						TierJitterDown = 0,
						TierJitterUp = TierJitterUp,
						AllowJackpot = false,
						GatingTier = unlockedTier,
						GatingHardMode = hardMode,
						MaxTier = maxTier
					};
					int rolled = ItemPool.Roll(request);
					int p = Penalty(rolled);
					if (p < penalty)
					{
						replacement = rolled;
						penalty = p;
					}
					if (penalty == 0) break;
				}
				// The progression limits leave few items of a category (late potions, late furniture): another item of its
				// category, no later than it, from its tier down, that breaks no rule, else fewer rules, else no duplicate
				foreach (int allowed in new[] { 0, 1, 3 })
				{
					if (penalty <= allowed) break;
					int other = Unused(replacement, item => Penalty(item) <= allowed,
						DetRandom.Get(RollKey.Shop, npcKey, shopKey, DetRandom.ItemKey(original), RollKey.Fallback, allowed));
					if (other <= 0) continue;
					replacement = other;
					penalty = Penalty(other);
				}
				used.Add(replacement);
				slots[i] = new ShopSlot(original, replacement, Price(npcKey, shopKey, replacement));
			}
			return slots;
		}

		// The categories a shop never sells above the unlocked tier: what makes a fight easier
		public static bool WithinUnlockedTier(ItemCategory category) => category is ItemCategory.Tool or ItemCategory.Armor or ItemCategory.Accessory
			or ItemCategory.WeaponMelee or ItemCategory.WeaponRanged or ItemCategory.WeaponMagic or ItemCategory.WeaponSummon or ItemCategory.WeaponOther;

		private static int Price(int npcKey, int shopKey, int item) => PriceRules.Price(item, DetRandom.Get(RollKey.Price, npcKey, shopKey, DetRandom.ItemKey(item)));

		// The added slots a shop shows at this progression: its shop-only items (ShopPermutation.ItemsOf) whose unlock tier is
		// reached, in order, each at its own price
		public static List<ShopSlot> AddedSlots(int npcType, string shopName, IEnumerable<int> shopOnly, int unlockedTier, bool hardMode)
		{
			int npcKey = DetRandom.NpcKey(npcType);
			int shopKey = DetRandom.StableHash(shopName ?? "");
			List<ShopSlot> slots = new();
			if (shopOnly == null) return slots;
			foreach (int item in shopOnly)
			{
				if (ShopPermutation.Unlocked(item, unlockedTier, hardMode)) slots.Add(new ShopSlot(0, item, Price(npcKey, shopKey, item), true));
			}
			return slots;
		}

		public const int TierJitterUp = 1;

		// An item of the same category as taken, accepted, whose true tier is no later than taken's (never a hard gate unless
		// taken is one), from taken's tier down, each bucket read from a random start; 0 when there is none
		private static int Unused(int taken, Func<int, bool> accept, Random rng)
		{
			if (!ItemPool.IsValid(taken)) return 0;
			ItemCategory category = ItemPool.Info[taken].Category;
			int ceiling = ItemPool.TrueTierOf(taken);
			int hardCeiling = ItemPool.IsHardGate(taken) ? ceiling : -1;
			for (int t = ItemPool.TierOf(taken); t >= 0; t--)
			{
				int[] bucket = ItemPool.Bucket(category, t);
				if (bucket.Length == 0) continue;
				int start = rng.Next(bucket.Length);
				for (int k = 0; k < bucket.Length; k++)
				{
					int item = bucket[(start + k) % bucket.Length];
					if (ItemPool.Allowed(item, ceiling, hardCeiling) && accept(item)) return item;
				}
			}
			return 0;
		}

		// World-wide spread of the rolls. Each item has its share of merchants (MaxShopsPerItem of the merchants with a shop,
		// modded ones too, ranked by a hash of the seed, the item and the merchant) and, at a merchant with several tabs (the
		// Painter's decor), one tab: a pure function of the seed, the setting and the loaded shops, the same on every machine
		private const int SpreadKey = 0x5B2E;
		private static int[] merchants;
		private static Dictionary<int, string[]> tabs;
		private static readonly Dictionary<int, HashSet<int>> shares = new();
		private static int sharesSeed;
		private static int sharesLimit = -1;

		public static void ResetSpread()
		{
			merchants = null;
			tabs = null;
			shares.Clear();
			sharesLimit = -1;
		}

		private static void EnsureMerchants()
		{
			if (merchants != null) return;
			tabs = NPCShopDatabase.AllShops.Where(s => s != null && s.NpcType > 0).GroupBy(s => s.NpcType)
				.ToDictionary(g => g.Key, g => g.Select(s => s.FullName).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray());
			merchants = tabs.Keys.OrderBy(DetRandom.NpcKey).ThenBy(n => n).ToArray();
		}

		// The merchants that may roll this item (null: every merchant)
		public static HashSet<int> ShareOf(int item)
		{
			int limit = EconomyConfig.Instance.MaxShopsPerItem;
			if (limit <= 0) return null;
			EnsureMerchants();
			if (merchants.Length <= limit) return null;
			int seed = SeedSystem.WorldSeed;
			if (seed != sharesSeed || limit != sharesLimit)
			{
				shares.Clear();
				sharesSeed = seed;
				sharesLimit = limit;
			}
			if (shares.TryGetValue(item, out HashSet<int> share)) return share;
			int key = DetRandom.ItemKey(item);
			share = merchants.OrderBy(n => DetRandom.Hash(seed, SpreadKey, key, DetRandom.NpcKey(n))).ThenBy(n => n).Take(limit).ToHashSet();
			shares[item] = share;
			return share;
		}

		// May this merchant roll this item (its share of merchants; a merchant without a registered shop: yes)
		public static bool Spread(int item, int npcType)
		{
			HashSet<int> share = ShareOf(item);
			return share == null || !tabs.ContainsKey(npcType) || share.Contains(npcType);
		}

		// Is this the merchant's tab that may roll this item (a merchant with one tab: always)
		public static bool OwnTab(int item, int npcType, string shopName)
		{
			EnsureMerchants();
			if (shopName == null || !tabs.TryGetValue(npcType, out string[] names) || names.Length <= 1 || Array.IndexOf(names, shopName) < 0) return true;
			uint pick = (uint)DetRandom.Hash(SeedSystem.WorldSeed, SpreadKey, DetRandom.ItemKey(item), DetRandom.NpcKey(npcType), 1);
			return names[pick % (uint)names.Length] == shopName;
		}
	}
}

using System;
using System.Collections.Generic;
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
				Obtainability.ShopEntries.Add((shop.NpcType, entry.Item.type, System.Linq.Enumerable.ToArray(entry.Conditions)));
			}
		}

		// Runs on the local client each time a shop is opened. The original item is part of the key, so each replacement keeps
		// the original's unlock conditions (moon phase, progression...); the slots chosen for this world's shop-only items
		// (ShopPermutation) sell them instead of a roll
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
			int[] guaranteed = ShopPermutation.SlotItems(permutation, shopName, items);
			ShopSlot[] plan = ShopPlanner.PlanShop(npc.type, shopName, originals, ProgressionSystem.UnlockedTier, Main.hardMode, cfg.ShopsFollowProgression, cfg.NoDuplicatesInShop,
				guaranteed, ShopPermutation.ItemsOf(permutation, shopName));
			for (int i = 0; i < items.Length; i++)
			{
				if (!plan[i].Replaced) continue;
				Item item = new(plan[i].Replacement);
				item.stack = 1;
				item.shopCustomPrice = plan[i].Price;
				items[i] = item;
			}
		}
	}

	// One shop slot as planned: the original item, what replaces it (0 when left as it is), its price, and whether it is a
	// shop-only item placed there (ShopPermutation) rather than a roll
	public readonly record struct ShopSlot(int Original, int Replacement, int Price, bool Guaranteed = false)
	{
		public bool Replaced => Replacement > 0;
	}

	// Pure shop plan: only the world seed, the settings and the given progression (no local player state), so the shop dump
	// and the tests see what a player would see at any progression
	public static class ShopPlanner
	{
		// originalTypes in slot order (0: slot left alone). The duplicate check follows that order, as the shop fills its slots:
		// the result for a slot depends on the items sold before it (only those whose conditions are met in game).
		// guaranteed (per slot, 0: a roll): the shop-only item a slot sells instead of a roll; shopOnly: every shop-only item of
		// the shop, which no roll gives with noDuplicates (whether or not its slot is sold right now)
		public static ShopSlot[] PlanShop(int npcType, string shopName, IList<int> originalTypes, int unlockedTier, bool hardMode, bool followProgression, bool noDuplicates,
			IList<int> guaranteed = null, IEnumerable<int> shopOnly = null)
		{
			int npcKey = DetRandom.NpcKey(npcType);
			int shopKey = DetRandom.StableHash(shopName ?? "");
			ShopSlot[] slots = new ShopSlot[originalTypes.Count];
			HashSet<int> used = new();
			if (noDuplicates && shopOnly != null) used.UnionWith(shopOnly);
			int Price(int item) => PriceRules.Price(item, DetRandom.Get(RollKey.Price, npcKey, shopKey, DetRandom.ItemKey(item)));
			for (int i = 0; i < originalTypes.Count; i++)
			{
				int original = originalTypes[i];
				slots[i] = new ShopSlot(original, 0, 0);
				if (original <= 0 || !ItemPool.IsRandomizable(original)) continue;
				int fixedItem = guaranteed != null && i < guaranteed.Count ? guaranteed[i] : 0;
				if (fixedItem > 0)
				{
					used.Add(fixedItem);
					slots[i] = new ShopSlot(original, fixedItem, Price(fixedItem), true);
					continue;
				}

				int originalTier = ItemPool.Info[original].Tier;
				int sourceTier = followProgression ? Math.Max(originalTier, unlockedTier - 1) : originalTier;
				int replacement = original;
				for (int attempt = 0; attempt < 8; attempt++)
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
						GatingHardMode = hardMode
					};
					replacement = ItemPool.Roll(request);
					if (!noDuplicates || !used.Contains(replacement)) break;
				}
				used.Add(replacement);
				slots[i] = new ShopSlot(original, replacement, Price(replacement));
			}
			return slots;
		}

		public const int TierJitterUp = 1;
	}
}

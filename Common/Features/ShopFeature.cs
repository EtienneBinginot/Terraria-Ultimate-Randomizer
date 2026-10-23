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

		// Runs on the local client each time a shop is opened. The original item is part of the key,
		// so each replacement keeps the original's unlock conditions (moon phase, progression...)
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
			ShopSlot[] plan = ShopPlanner.PlanShop(npc.type, shopName, originals, ProgressionSystem.UnlockedTier, Main.hardMode, cfg.ShopsFollowProgression, cfg.NoDuplicatesInShop);
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

	// One shop slot as planned: the original item, what replaces it (0 when left as it is) and its price
	public readonly record struct ShopSlot(int Original, int Replacement, int Price)
	{
		public bool Replaced => Replacement > 0;
	}

	// Pure shop plan: only the world seed, the settings and the given progression (no local player state), so the shop dump
	// and the tests see what a player would see at any progression
	public static class ShopPlanner
	{
		// originalTypes in slot order (0: slot left alone). The duplicate check follows that order, as the shop fills its slots:
		// the result for a slot depends on the items sold before it (only those whose conditions are met in game)
		public static ShopSlot[] PlanShop(int npcType, string shopName, IList<int> originalTypes, int unlockedTier, bool hardMode, bool followProgression, bool noDuplicates)
		{
			int npcKey = DetRandom.NpcKey(npcType);
			int shopKey = DetRandom.StableHash(shopName ?? "");
			ShopSlot[] slots = new ShopSlot[originalTypes.Count];
			HashSet<int> used = new();
			for (int i = 0; i < originalTypes.Count; i++)
			{
				int original = originalTypes[i];
				slots[i] = new ShopSlot(original, 0, 0);
				if (original <= 0 || !ItemPool.IsRandomizable(original)) continue;

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
				// The progression limits leave few items of a category (late potions): the re-rolls all came back to an item
				// already sold. Another unused item of its category, no later than it, from its tier down
				if (noDuplicates && used.Contains(replacement))
				{
					int other = Unused(replacement, used, DetRandom.Get(RollKey.Shop, npcKey, shopKey, DetRandom.ItemKey(original), RollKey.Fallback));
					if (other > 0) replacement = other;
				}
				used.Add(replacement);
				slots[i] = new ShopSlot(original, replacement, PriceRules.Price(replacement, DetRandom.Get(RollKey.Price, npcKey, shopKey, DetRandom.ItemKey(replacement))));
			}
			return slots;
		}

		public const int TierJitterUp = 1;

		// An item of the same category as taken, not in used, whose true tier is no later than taken's (never a hard gate
		// unless taken is one), from taken's tier down, each bucket read from a random start; 0 when there is none
		private static int Unused(int taken, HashSet<int> used, Random rng)
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
					if (!used.Contains(item) && ItemPool.Allowed(item, ceiling, hardCeiling)) return item;
				}
			}
			return 0;
		}
	}
}

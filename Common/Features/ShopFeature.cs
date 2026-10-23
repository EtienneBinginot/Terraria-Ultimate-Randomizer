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
			int npcKey = DetRandom.NpcKey(npc.type);
			int shopKey = DetRandom.StableHash(shopName ?? "");
			HashSet<int> used = new();
			for (int i = 0; i < items.Length; i++)
			{
				Item original = items[i];
				// null = unused slot, air = reserved slot whose conditions are not met: both must stay as they are
				if (original == null || original.IsAir) continue;
				// Items bought with a special currency (defender medals) are left alone
				if (original.shopSpecialCurrency != -1 || !ItemPool.IsRandomizable(original.type)) continue;

				int originalTier = ItemPool.Info[original.type].Tier;
				int sourceTier = cfg.ShopsFollowProgression ? Math.Max(originalTier, ProgressionSystem.UnlockedTier - 1) : originalTier;
				int replacement = original.type;
				for (int attempt = 0; attempt < 8; attempt++)
				{
					RollRequest request = new()
					{
						Keys = new[] { RollKey.Shop, npcKey, shopKey, DetRandom.ItemKey(original.type), attempt },
						SourceTier = Tiers.Clamp(sourceTier),
						OriginalItem = original.type,
						Policy = CategoryPolicy.SameAsOriginal,
						TierJitterDown = 0,
						TierJitterUp = 1,
						AllowJackpot = false
					};
					replacement = ItemPool.Roll(request);
					if (!cfg.NoDuplicatesInShop || !used.Contains(replacement)) break;
				}
				used.Add(replacement);

				Item item = new(replacement);
				item.stack = 1;
				item.shopCustomPrice = PriceRules.Price(replacement, DetRandom.Get(RollKey.Price, npcKey, shopKey, DetRandom.ItemKey(replacement)));
				items[i] = item;
			}
		}
	}
}

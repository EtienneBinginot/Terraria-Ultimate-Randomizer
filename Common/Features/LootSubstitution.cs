using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	// The vanilla drop rules are kept untouched (conditions, "one of" choices, per-player bags, expert/master...):
	// each item they produce is swapped for its replacement at the moment it spawns.
	public class LootSubstitutionItem : GlobalItem
	{
		public override void OnSpawn(Item item, IEntitySource source)
		{
			if (!SeedSystem.WorldReady || !ItemPool.Ready || item.IsAir) return;
			LootConfig cfg = LootConfig.Instance;
			switch (source)
			{
				case EntitySource_Loot loot when loot.Context != DropHelper.ExtraDropContext && loot.Entity is NPC npc:
					if (!cfg.RandomizeMobDrops || !IsDeathLoot(npc, item.type)) return;
					int owner = BossParts.Owner(npc.type);
					bool isBoss = NpcTiers.IsBoss(owner);
					System.Random dropRng = new(Main.rand.Next());
					// Same anti-farm rules as the extra drops: weak swarms get a share of the jackpot chances, critters and
					// NPCs spawned by another NPC none (their replaced drop stays the planned one)
					float worth = MobLootSystem.IsSpawnedByNpc(npc) ? 0f : RandomNpcDropRule.JackpotWorth(npc);
					int replacement = DropHelper.Replacement(RollKey.MobDrop, DetRandom.NpcKey(owner), item.type,
						NpcTiers.EffectiveTier(owner), isBoss ? NpcTiers.BaseTier(owner) : 0, DropHelper.RollLucky(worth), dropRng, worth > 0f ? dropRng : null, worth);
					int gap = ItemPool.LastJackpotGap;
					DropHelper.ChangeSpawnedItem(item, replacement, StackRules.Scaled(item.type, item.stack, replacement));
					JackpotItem.Mark(item, gap);
					break;

				case EntitySource_ItemOpen open when IsBag(open.ItemType) && BagEnabled(open.ItemType, cfg):
					int bagReplacement = BagReplacement(open.ItemType, item.type, new System.Random(Main.rand.Next()));
					DropHelper.ChangeSpawnedItem(item, bagReplacement, StackRules.Scaled(item.type, item.stack, bagReplacement));
					break;
			}
		}

		// The item replacing one of an opened bag's: the planned one, or a surprise (bagRng). Never a jackpot (no luck, no
		// madness): a boss bag already holds its whole tier, and crates are opened by the dozen
		public static int BagReplacement(int bagType, int itemType, System.Random bagRng)
		{
			int bagTier = LootSnapshot.BagTier(bagType);
			return DropHelper.Replacement(RollKey.BagDrop, DetRandom.ItemKey(bagType), itemType, bagTier, ItemID.Sets.BossBag[bagType] ? bagTier : 0, false, bagRng);
		}

		// The loot source is also used for things that are not drop tables: banners, the Big Mimic's item bursts (it is
		// still alive), Old One's Army medals (dropped by the living crystal), the healing potions every boss leaves
		public static bool IsDeathLoot(NPC npc, int itemType)
		{
			if (npc.active && npc.life > 0) return false;
			int banner = Item.NPCtoBanner(npc.BannerID());
			if (banner > 0 && itemType == Item.BannerToItem(banner)) return false;
			if (npc.boss && itemType is ItemID.LesserHealingPotion or ItemID.HealingPotion or ItemID.GreaterHealingPotion or ItemID.SuperHealingPotion) return false;
			return true;
		}

		// Only real bags: other items also give things "from an opened item" (Minecart Upgrade Kit, starting bag of other mods)
		public static bool IsBag(int itemType) =>
			ItemID.Sets.BossBag[itemType] || ItemID.Sets.IsFishingCrate[itemType] || ItemID.Sets.OpenableBag[itemType];

		public static bool BagEnabled(int bagType, LootConfig cfg) =>
			ItemID.Sets.BossBag[bagType] ? cfg.RandomizeBossBags
			: ItemID.Sets.IsFishingCrate[bagType] ? cfg.RandomizeCrates
			: cfg.RandomizeOtherBags;
	}
}

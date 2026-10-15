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
					if (!cfg.RandomizeMobDrops) return;
					bool isBoss = RandomNpcDropRule.IsBossType(npc.type);
					int replacement = DropHelper.Replacement(RollKey.MobDrop, DetRandom.NpcKey(npc.type), item.type,
						NpcTiers.EffectiveTier(npc.type), isBoss ? NpcTiers.BaseTier(npc.type) : 0, DropHelper.RollLucky());
					DropHelper.ChangeSpawnedItem(item, replacement, StackRules.Scaled(item.type, item.stack, replacement));
					break;

				case EntitySource_ItemOpen open when BagEnabled(open.ItemType, cfg):
					int bagTier = LootSnapshot.BagTier(open.ItemType);
					int bagReplacement = DropHelper.Replacement(RollKey.BagDrop, DetRandom.ItemKey(open.ItemType), item.type,
						bagTier, ItemID.Sets.BossBag[open.ItemType] ? bagTier : 0, DropHelper.RollLucky());
					DropHelper.ChangeSpawnedItem(item, bagReplacement, StackRules.Scaled(item.type, item.stack, bagReplacement));
					break;
			}
		}

		public static bool BagEnabled(int bagType, LootConfig cfg) =>
			ItemID.Sets.BossBag[bagType] ? cfg.RandomizeBossBags
			: ItemID.Sets.IsFishingCrate[bagType] ? cfg.RandomizeCrates
			: cfg.RandomizeOtherBags;
	}
}

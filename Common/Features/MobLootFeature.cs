using System.Collections.Generic;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	public class MobLootNPC : GlobalNPC
	{
		// Runs once at load, before any world exists: we only save the original table and install our rule
		public override void ModifyNPCLoot(NPC npc, NPCLoot npcLoot)
		{
			if (!LootConfig.Instance.RandomizeMobDrops) return;
			List<IItemDropRule> rules = npcLoot.Get(false);
			LootSnapshot.Npcs[npc.type] = LootSnapshot.Capture(rules);
			foreach (IItemDropRule rule in rules)
			{
				if (rule != null) npcLoot.Remove(rule);
			}
			npcLoot.Add(new RandomNpcDropRule(npc.type));
		}
	}

	// Picks the drops when the enemy dies, from the world seed and the current progression
	public class RandomNpcDropRule : IItemDropRule
	{
		private readonly int npcType;

		public List<IItemDropRuleChainAttempt> ChainedRules { get; } = new();

		public RandomNpcDropRule(int npcType)
		{
			this.npcType = npcType;
		}

		public bool CanDrop(DropAttemptInfo info) => true;

		public ItemDropAttemptResult TryDroppingItem(DropAttemptInfo info)
		{
			NPC npc = info.npc;
			if (!SeedSystem.WorldReady || !ItemPool.Ready || npc == null || npc.SpawnedFromStatue)
			{
				return new ItemDropAttemptResult { State = ItemDropAttemptResultState.DidNotRunCode };
			}
			List<DropRateInfo> original = LootSnapshot.ForNpc(npcType);
			bool shuffle = GeneralConfig.Instance.RandomizerMode == RandomizerMode.Shuffle;
			bool bagBoss = false;

			foreach (DropRateInfo entry in original)
			{
				if (ItemID.Sets.BossBag[entry.itemId]) bagBoss = true;
				if (DropHelper.KeepOriginal(entry.itemId))
				{
					DropHelper.TryDropEntry(info, entry, entry.itemId, entry.stackMin, entry.stackMax);
				}
				else if (shuffle)
				{
					// Same drop table, every item swapped with its partner
					int replacement = ItemPool.Shuffled(entry.itemId);
					(int min, int max) = DropHelper.ScaledRange(entry, replacement);
					DropHelper.TryDropEntry(info, entry, replacement, min, max);
				}
			}

			// In expert mode, a boss with a treasure bag puts its loot in the bag (randomized there)
			bool isBoss = npc.boss || NPCID.Sets.ShouldBeCountedAsBoss[npcType];
			if (!shuffle && !(isBoss && bagBoss && info.IsExpertMode))
			{
				RollRandomSlots(info, isBoss);
			}
			return new ItemDropAttemptResult { State = ItemDropAttemptResultState.Success };
		}

		private void RollRandomSlots(DropAttemptInfo info, bool isBoss)
		{
			NPC npc = info.npc;
			if (npc.townNPC || NPCID.Sets.CountsAsCritter[npcType] || (npc.friendly && !isBoss)) return;
			LootConfig cfg = LootConfig.Instance;
			int npcKey = DetRandom.NpcKey(npcType);
			System.Random slotRng = DetRandom.Get(RollKey.MobDropSlots, npcKey);
			int min = isBoss ? cfg.BossDropSlotsMin : cfg.DropSlotsMin;
			int max = isBoss ? cfg.BossDropSlotsMax : cfg.DropSlotsMax;
			int slots = slotRng.Next(min, System.Math.Max(min, max) + 1);
			int sourceTier = NpcTiers.EffectiveTier(npcType);
			bool jackpot = info.rng.NextFloat() * 100 < ProgressionConfig.Instance.RuntimeJackpotChance;

			for (int slot = 0; slot < slots; slot++)
			{
				RollRequest request = new()
				{
					Keys = new[] { RollKey.MobDrop, npcKey, slot },
					SourceTier = sourceTier,
					OriginalItem = -1,
					Policy = CategoryPolicy.Weighted,
					TierJitterDown = 1,
					TierJitterUp = 1,
					AllowJackpot = true,
					IgnoreGating = jackpot
				};
				int item = ItemPool.Roll(request, out int tier);
				// Items above the enemy's level are rarer
				float chance = isBoss ? 1f : cfg.DropChance / 100f / (1 + System.Math.Max(0, tier - sourceTier));
				if (info.rng.NextFloat() >= chance) continue;
				(int stackMin, int stackMax) = StackRules.RollRange(item, DetRandom.Get(RollKey.MobDrop, npcKey, slot, item));
				DropHelper.Drop(info, item, stackMin, stackMax);
			}
		}

		// The bestiary is built before any world exists: only the drops kept from the original table can be shown
		public void ReportDroprates(List<DropRateInfo> drops, DropRateInfoChainFeed ratesInfo)
		{
			DropHelper.ReportKept(LootSnapshot.ForNpc(npcType), drops, ratesInfo);
			Chains.ReportDroprates(ChainedRules, 1f, drops, ratesInfo);
		}
	}
}

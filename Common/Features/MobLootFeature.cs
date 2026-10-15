using System;
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

	// One possible drop of an enemy in the current world
	public struct PlannedDrop
	{
		public int Item;
		public int StackMin;
		public int StackMax;
		public float Chance;
		public List<IItemDropRuleCondition> Conditions;
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
			bool lucky = info.rng.NextFloat() * 100 < ProgressionConfig.Instance.RuntimeJackpotChance;
			foreach (PlannedDrop drop in Plan(npcType, info.IsExpertMode, lucky))
			{
				if (!DropHelper.ConditionsMet(drop.Conditions, info) || info.rng.NextFloat() >= drop.Chance) continue;
				DropHelper.Drop(info, drop.Item, drop.StackMin, drop.StackMax);
			}
			return new ItemDropAttemptResult { State = ItemDropAttemptResultState.Success };
		}

		public static bool IsBoss(int npcType) =>
			NPCID.Sets.ShouldBeCountedAsBoss[npcType] || (ContentSamples.NpcsByNetId.TryGetValue(npcType, out NPC sample) && sample.boss);

		// The full drop table of an enemy in this world (also used by /rdebug npc)
		public static List<PlannedDrop> Plan(int npcType, bool expert, bool lucky = false)
		{
			List<PlannedDrop> drops = new();
			List<DropRateInfo> original = LootSnapshot.ForNpc(npcType);
			LootConfig cfg = LootConfig.Instance;
			bool shuffle = GeneralConfig.Instance.RandomizerMode == RandomizerMode.Shuffle;
			bool isBoss = IsBoss(npcType);
			bool bagBoss = false;
			List<DropRateInfo> replaced = new();

			foreach (DropRateInfo entry in original)
			{
				if (ItemID.Sets.BossBag[entry.itemId]) bagBoss = true;
				if (DropHelper.KeepOriginal(entry.itemId))
				{
					drops.Add(FromEntry(entry, entry.itemId, entry.stackMin, entry.stackMax, entry.dropRate));
				}
				else
				{
					replaced.Add(entry);
				}
			}

			if (shuffle)
			{
				// Same drop table, every item swapped with its partner
				foreach (DropRateInfo entry in replaced)
				{
					int item = ItemPool.Shuffled(entry.itemId);
					(int min, int max) = DropHelper.ScaledRange(entry, item);
					drops.Add(FromEntry(entry, item, min, max, entry.dropRate));
				}
				return drops;
			}

			if (!CanHaveRandomDrops(npcType, isBoss)) return drops;
			int npcKey = DetRandom.NpcKey(npcType);
			int sourceTier = NpcTiers.EffectiveTier(npcType);

			if (cfg.DropTableStyle == DropTableStyle.MirrorOriginal && replaced.Count > 0)
			{
				for (int i = 0; i < replaced.Count; i++)
				{
					DropRateInfo entry = replaced[i];
					RollRequest request = new()
					{
						Keys = new[] { RollKey.MobDrop, npcKey, 1000 + i },
						SourceTier = ItemPool.Info[entry.itemId].Tier,
						OriginalItem = entry.itemId,
						Policy = CategoryPolicy.SameAsOriginal,
						TierJitterDown = 0,
						TierJitterUp = 1,
						AllowJackpot = true,
						IgnoreGating = lucky
					};
					int item = ItemPool.Roll(request);
					(int min, int max) = DropHelper.ScaledRange(entry, item);
					drops.Add(FromEntry(entry, item, min, max, Math.Min(1f, entry.dropRate * cfg.DropRateMultiplier)));
				}
				return drops;
			}

			// In expert mode, a boss with a treasure bag puts its loot in the bag (randomized there)
			if (isBoss && bagBoss && expert) return drops;
			if (!isBoss && replaced.Count == 0 && !cfg.EnemiesWithoutDropsCanDrop) return drops;

			Random slotRng = DetRandom.Get(RollKey.MobDropSlots, npcKey);
			int minSlots = isBoss ? cfg.BossDropSlotsMin : cfg.DropSlotsMin;
			int maxSlots = Math.Max(minSlots, isBoss ? cfg.BossDropSlotsMax : cfg.DropSlotsMax);
			// Skewed towards few drops for normal enemies: most drop little, a few drop a lot
			double skew = isBoss || cfg.DropTableStyle == DropTableStyle.Simple ? 1.0 : 1.8;
			int slots = Math.Min(maxSlots, minSlots + (int)Math.Floor((maxSlots - minSlots + 1) * Math.Pow(slotRng.NextDouble(), skew)));

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
					IgnoreGating = lucky
				};
				int item = ItemPool.Roll(request, out int tier);
				float chance;
				if (cfg.DropTableStyle == DropTableStyle.Simple)
				{
					chance = isBoss ? 1f : cfg.DropChance / 100f / (1 + Math.Max(0, tier - sourceTier));
				}
				else
				{
					// A boss always drops its first slot
					int denominator = isBoss && slot == 0 ? 1 : DropRates.Denominator(item, tier, sourceTier, isBoss, DetRandom.Get(RollKey.MobDrop, npcKey, slot, item, 7));
					chance = Math.Min(1f, cfg.DropRateMultiplier / denominator);
				}
				(int stackMin, int stackMax) = StackRules.RollRange(item, DetRandom.Get(RollKey.MobDrop, npcKey, slot, item));
				drops.Add(new PlannedDrop { Item = item, StackMin = stackMin, StackMax = stackMax, Chance = chance });
			}
			return drops;
		}

		private static bool CanHaveRandomDrops(int npcType, bool isBoss)
		{
			if (!ContentSamples.NpcsByNetId.TryGetValue(npcType, out NPC sample)) return false;
			if (sample.townNPC || NPCID.Sets.ActsLikeTownNPC[npcType] || NPCID.Sets.IsTownPet[npcType]) return false;
			if (NPCID.Sets.CountsAsCritter[npcType]) return LootConfig.Instance.CrittersCanDrop;
			return isBoss || !sample.friendly;
		}

		private static PlannedDrop FromEntry(in DropRateInfo entry, int item, int min, int max, float chance) => new()
		{
			Item = item,
			StackMin = min,
			StackMax = max,
			Chance = chance,
			Conditions = entry.conditions
		};

		// The bestiary is built before any world exists: only the drops kept from the original table can be shown
		public void ReportDroprates(List<DropRateInfo> drops, DropRateInfoChainFeed ratesInfo)
		{
			DropHelper.ReportKept(LootSnapshot.ForNpc(npcType), drops, ratesInfo);
			Chains.ReportDroprates(ChainedRules, 1f, drops, ratesInfo);
		}
	}
}

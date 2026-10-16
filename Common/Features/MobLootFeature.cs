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
	public class MobLootNPC : GlobalNPC
	{
		// Runs once per enemy (and per variant) at load, before any world exists. The original rules are kept:
		// their items are swapped when they spawn (LootSubstitutionItem). We only add a rule for extra random drops.
		public override void ModifyNPCLoot(NPC npc, NPCLoot npcLoot)
		{
			List<IItemDropRule> rules = npcLoot.Get(false).Where(r => r != null).Distinct().ToList();
			LootSnapshot.Npcs[npc.netID] = LootSnapshot.Capture(rules);
			npcLoot.Add(new RandomNpcDropRule(npc.netID));
		}
	}

	// One possible drop of an enemy in the current world (used by the journal, the debug tools and the extra drops)
	public struct PlannedDrop
	{
		public int Item;
		public int StackMin;
		public int StackMax;
		public float Chance;
		public List<IItemDropRuleCondition> Conditions;
		public bool Extra;
		// Placed so that an item is findable (basic stations, vanilla ingredients)
		public bool Guaranteed;
	}

	// Categories of the frequent small drop of every enemy
	public class CommonDropWeights : WeightTable
	{
		public static readonly CommonDropWeights Instance = new();

		public int Material = 30;
		public int Potion = 25;
		public int Ammo = 15;
		public int Food = 10;
		public int Block = 10;
		public int OreBar = 10;
	}

	// Extra random drops, on top of the (replaced) original drops: a random number per enemy type, vanilla-like rates
	public class RandomNpcDropRule : IItemDropRule
	{
		private readonly int netId;

		public List<IItemDropRuleChainAttempt> ChainedRules { get; } = new();

		public RandomNpcDropRule(int netId)
		{
			this.netId = netId;
		}

		public bool CanDrop(DropAttemptInfo info) => true;

		public ItemDropAttemptResult TryDroppingItem(DropAttemptInfo info)
		{
			NPC npc = info.npc;
			if (!SeedSystem.WorldReady || !ItemPool.Ready || npc == null || npc.SpawnedFromStatue || !ExtraDropsAllowedNow(info))
			{
				return new ItemDropAttemptResult { State = ItemDropAttemptResultState.DidNotRunCode };
			}
			IEnumerable<PlannedDrop> drops = ExtraDrops(netId, BossParts.Owner(npc.type), info.IsExpertMode, DropHelper.RollLucky()).Concat(GuaranteedSources.For(npc.type));
			// Stronger enemies (type and individual) drop more often
			float bonus = EnemyIndividualNPC.LootBonus(npc);
			foreach (PlannedDrop drop in drops)
			{
				if (info.rng.NextFloat() >= Math.Min(1f, drop.Chance * bonus)) continue;
				int stack = info.rng.Next(drop.StackMin, Math.Max(drop.StackMin, drop.StackMax) + 1);
				CommonCode.DropItem(npc.Hitbox, new Terraria.DataStructures.EntitySource_Loot(npc, DropHelper.ExtraDropContext), drop.Item, stack, false);
			}
			// A giant always gives one more random item, a bit above its level
			if (EnemiesConfig.Instance.GiantBonusDrop && LootConfig.Instance.RandomizeMobDrops && npc.TryGetGlobalNPC(out EnemyIndividualNPC individual) && individual.Kind == EnemyKind.Giant
				&& GiantBonusAllowed(netId, npc.type))
			{
				int item = GiantDrop(npc, info.rng.Next());
				int stack = info.rng.Next(StackRules.Range(item).min, StackRules.Range(item).max + 1);
				CommonCode.DropItem(npc.Hitbox, new Terraria.DataStructures.EntitySource_Loot(npc, DropHelper.ExtraDropContext), item, Math.Max(1, stack), false);
			}
			return new ItemDropAttemptResult { State = ItemDropAttemptResultState.Success };
		}

		// Same anti-farm protection as the extra drops: a giant boss minion (or a town NPC...) gives nothing more
		public static bool GiantBonusAllowed(int netId, int npcType)
		{
			int owner = BossParts.Owner(npcType);
			return !BossParts.IsMinion(npcType) && !BossParts.IsMinion(owner) && CanHaveExtraDrops(owner, IsBossType(owner), LootSnapshot.ForNpc(netId));
		}

		public static int GiantDrop(NPC npc, int key)
		{
			RollRequest request = new()
			{
				Keys = new[] { RollKey.GiantMini, key },
				SourceTier = NpcTiers.EffectiveTier(BossParts.Owner(npc.type)),
				OriginalItem = -1,
				Policy = CategoryPolicy.Weighted,
				TierJitterUp = 1,
				AllowJackpot = true,
				IgnoreGating = DropHelper.RollLucky()
			};
			return ItemPool.Roll(request);
		}

		// Multi-part bosses drop their loot once: the last Eater of Worlds segment (vanilla makes it a boss as it dies,
		// whether head, body or tail), the last of the Twins
		private bool ExtraDropsAllowedNow(DropAttemptInfo info)
		{
			NPC npc = info.npc;
			if (!IsBossType(BossParts.Owner(npc.type))) return true;
			if (!npc.boss) return false;
			if (npc.type == NPCID.Retinazer || npc.type == NPCID.Spazmatism) return new Conditions.MissingTwin().CanDrop(info);
			return true;
		}

		public static bool IsBossType(int type) =>
			type > 0 && (NPCID.Sets.ShouldBeCountedAsBoss[type] || (ContentSamples.NpcsByNetId.TryGetValue(type, out NPC sample) && sample.boss));

		// Everything an enemy can drop in this world: the replaced original drops, then the extra ones and the guaranteed ones.
		// lucky: ignores the progression lock (what the enemy gives once everything is unlocked)
		public static List<PlannedDrop> Plan(int netId, bool expert, bool lucky = false, bool guaranteed = true)
		{
			List<PlannedDrop> drops = new();
			int type = BossParts.Owner(ContentSamples.NpcsByNetId.TryGetValue(netId, out NPC sample) ? sample.type : netId);
			bool isBoss = IsBossType(type);
			foreach (DropRateInfo entry in LootSnapshot.ForNpc(netId))
			{
				int item = entry.itemId;
				int min = entry.stackMin;
				int max = entry.stackMax;
				if (LootConfig.Instance.RandomizeMobDrops)
				{
					item = DropHelper.Replacement(RollKey.MobDrop, DetRandom.NpcKey(type), entry.itemId, NpcTiers.EffectiveTier(type), isBoss ? NpcTiers.BaseTier(type) : 0, lucky);
					(min, max) = DropHelper.ScaledRange(entry.itemId, entry.stackMin, entry.stackMax, item);
				}
				drops.Add(new PlannedDrop { Item = item, StackMin = min, StackMax = max, Chance = entry.dropRate, Conditions = entry.conditions });
			}
			drops.AddRange(ExtraDrops(netId, type, expert, lucky));
			if (guaranteed) drops.AddRange(GuaranteedSources.For(type));
			return drops;
		}

		public static List<PlannedDrop> ExtraDrops(int netId, int type, bool expert, bool lucky)
		{
			List<PlannedDrop> drops = new();
			LootConfig cfg = LootConfig.Instance;
			if (!cfg.RandomizeMobDrops || cfg.DropTableStyle == DropTableStyle.MirrorOriginal) return drops;
			if (GeneralConfig.Instance.RandomizerMode == RandomizerMode.Shuffle) return drops;
			List<DropRateInfo> original = LootSnapshot.ForNpc(netId);
			bool isBoss = IsBossType(type);
			if (!CanHaveExtraDrops(type, isBoss, original)) return drops;
			// In expert mode, a boss with a treasure bag gets no extra drops: the content of its bag is replaced instead
			if (isBoss && expert && original.Any(d => ItemID.Sets.BossBag[d.itemId])) return drops;

			int npcKey = DetRandom.NpcKey(type);
			int sourceTier = NpcTiers.EffectiveTier(type);
			bool critter = NPCID.Sets.CountsAsCritter[type];
			Random slotRng = DetRandom.Get(RollKey.MobDropSlots, npcKey);
			int minSlots = isBoss ? cfg.BossDropSlotsMin : cfg.DropSlotsMin;
			int maxSlots = Math.Max(minSlots, isBoss ? cfg.BossDropSlotsMax : cfg.DropSlotsMax);
			if (critter) maxSlots = Math.Min(maxSlots, Math.Max(minSlots, 2));
			// Skewed towards few drops for normal enemies: most drop little, a few drop a lot
			double skew = isBoss || cfg.DropTableStyle == DropTableStyle.Simple ? 1.0 : 1.8;
			int slots = Math.Min(maxSlots, minSlots + (int)Math.Floor((maxSlots - minSlots + 1) * Math.Pow(slotRng.NextDouble(), skew)));

			for (int slot = 0; slot < slots; slot++)
			{
				RollRequest request = new()
				{
					Keys = new[] { RollKey.MobDrop, npcKey, 1000 + slot },
					SourceTier = sourceTier,
					OriginalItem = -1,
					Policy = CategoryPolicy.Weighted,
					TierJitterDown = 1,
					TierJitterUp = 1,
					AllowJackpot = true,
					IgnoreGating = lucky,
					MinLimit = isBoss ? NpcTiers.BaseTier(type) : 0
				};
				int item = ItemPool.Roll(request, out int tier);
				float chance;
				if (cfg.DropTableStyle == DropTableStyle.Simple)
				{
					chance = isBoss ? 1f : cfg.DropChance / 100f / (1 + Math.Max(0, tier - sourceTier));
				}
				else
				{
					// A boss always drops its first extra slot
					int denominator = isBoss && slot == 0 ? 1 : DropRates.Denominator(item, tier, sourceTier, isBoss, DetRandom.Get(RollKey.MobDrop, npcKey, slot, item, 7));
					chance = Math.Min(1f, cfg.DropRateMultiplier / denominator);
				}
				(int stackMin, int stackMax) = StackRules.RollRange(item, DetRandom.Get(RollKey.MobDrop, npcKey, slot, item));
				drops.Add(new PlannedDrop { Item = item, StackMin = stackMin, StackMax = stackMax, Chance = chance, Extra = true });
			}

			// A small frequent drop (material, potion, ammo...), so that most kills give something
			if (!isBoss && !critter && cfg.CommonDropChance > 0)
			{
				RollRequest common = new()
				{
					Keys = new[] { RollKey.MobDrop, npcKey, 2000 },
					SourceTier = sourceTier,
					OriginalItem = -1,
					Policy = CategoryPolicy.Weighted,
					Weights = CommonDropWeights.Instance,
					TierJitterDown = 1,
					IgnoreGating = lucky
				};
				int item = ItemPool.Roll(common);
				(int stackMin, int stackMax) = StackRules.RollRange(item, DetRandom.Get(RollKey.MobDrop, npcKey, 2000, item));
				drops.Add(new PlannedDrop { Item = item, StackMin = stackMin, StackMax = stackMax, Chance = cfg.CommonDropChance / 100f, Extra = true });
			}
			return drops;
		}

		public static bool CanHaveExtraDrops(int type, bool isBoss, List<DropRateInfo> original)
		{
			if (!ContentSamples.NpcsByNetId.TryGetValue(type, out NPC sample)) return false;
			if (BossParts.IsMinion(type)) return false;
			if (sample.townNPC || NPCID.Sets.ActsLikeTownNPC[type] || NPCID.Sets.IsTownPet[type] || type == NPCID.TorchGod) return false;
			if (NPCID.Sets.CountsAsCritter[type]) return LootConfig.Instance.CrittersCanDrop;
			if (NPCID.Sets.ProjectileNPC[type] || sample.lifeMax <= 5 || (sample.friendly && !isBoss)) return false;
			if (isBoss) return true;
			// Boss minions and parts drop nothing and give no money: they must not become loot farms
			if (original.Count == 0 && sample.value <= 0) return false;
			return original.Count > 0 || LootConfig.Instance.EnemiesWithoutDropsCanDrop;
		}

		// The bestiary is built before any world exists: the original drops are reported by their own rules
		public void ReportDroprates(List<DropRateInfo> drops, DropRateInfoChainFeed ratesInfo)
		{
			Chains.ReportDroprates(ChainedRules, 1f, drops, ratesInfo);
		}
	}
}

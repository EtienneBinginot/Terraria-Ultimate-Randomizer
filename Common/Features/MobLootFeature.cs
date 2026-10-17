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
		// Kill time only: a jackpot, this many tiers above the progression limit (JackpotItem)
		public int JackpotGap;
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
			IEnumerable<PlannedDrop> drops = ExtraDrops(netId, BossParts.Owner(npc.type), info.IsExpertMode, DropHelper.RollLucky(), wildRng: new Random(info.rng.Next()))
				.Concat(GuaranteedSources.For(npc.type));
			// Stronger enemies (type and individual) drop more often. Guaranteed drops (stations...) leave adaptive power
			// out, so that how fast they are found does not depend on the player's equipment
			float bonus = EnemyIndividualNPC.LootBonus(npc);
			float guaranteedBonus = EnemyIndividualNPC.LootBonus(npc, false);
			foreach (PlannedDrop drop in drops)
			{
				if (info.rng.NextFloat() >= Math.Min(1f, drop.Chance * (drop.Guaranteed ? guaranteedBonus : bonus))) continue;
				int stack = info.rng.Next(drop.StackMin, Math.Max(drop.StackMin, drop.StackMax) + 1);
				DropMarked(npc, drop.Item, stack, drop.JackpotGap);
			}
			foreach ((int item, int stack, int gap) in BonusDrops(npc, netId, info.rng.Next(), DropHelper.RollLucky()))
			{
				DropMarked(npc, item, stack, gap);
			}
			return new ItemDropAttemptResult { State = ItemDropAttemptResultState.Success };
		}

		// Giants: one more useful item a tier above their level, five times as likely to be a jackpot. Minis (small, agile and
		// dangerous): one more item of their level, twice as likely. Never for boss minions and parts (GiantBonusAllowed).
		// Rolled where the loot drops (server), with the runtime rng
		public const float GiantWildMultiplier = 5f;
		public const float MiniWildMultiplier = 2f;

		public static List<(int item, int stack, int gap)> BonusDrops(NPC npc, int netId, int key, bool lucky)
		{
			List<(int, int, int)> drops = new();
			if (!LootConfig.Instance.RandomizeMobDrops || !npc.TryGetGlobalNPC(out EnemyIndividualNPC individual) || !GiantBonusAllowed(netId, npc.type)) return drops;
			EnemiesConfig cfg = EnemiesConfig.Instance;
			int owner = BossParts.Owner(npc.type);
			int item;
			if (individual.Kind == EnemyKind.Giant && cfg.GiantBonusDrop) item = GiantDrop(owner, key, lucky);
			else if (individual.Kind == EnemyKind.Mini && cfg.MiniBonusDrop) item = MiniDrop(owner, key, lucky);
			else return drops;
			int gap = ItemPool.LastJackpotGap;
			// Not the WildRng seed of the roll
			Random rng = new(unchecked(key * 31 + 17));
			(int min, int max) = StackRules.Range(item);
			drops.Add((item, Math.Max(1, rng.Next(min, Math.Max(min, max) + 1)), gap));
			return drops;
		}

		// Drops an item, marked as a jackpot when it is one (the mark is set in JackpotItem.OnSpawn, during the call)
		private static void DropMarked(NPC npc, int item, int stack, int jackpotGap)
		{
			JackpotItem.Next = jackpotGap;
			try
			{
				CommonCode.DropItem(npc.Hitbox, new Terraria.DataStructures.EntitySource_Loot(npc, DropHelper.ExtraDropContext), item, stack, false);
			}
			finally
			{
				JackpotItem.Next = 0;
			}
		}

		// Same anti-farm protection as the extra drops: a giant boss minion (or a town NPC...) gives nothing more
		public static bool GiantBonusAllowed(int netId, int npcType)
		{
			int owner = BossParts.Owner(npcType);
			return !BossParts.IsMinion(npcType) && !BossParts.IsMinion(owner) && CanHaveExtraDrops(owner, IsBossType(owner), LootSnapshot.ForNpc(netId));
		}

		// A giant's bonus item: always useful (ItemPool.IsJackpotWorthy: equipment that does something, a progression station),
		// a tier above the giant's within the progression limit. Other categories are rolled again a few times; only the first
		// roll may go wild, so the jackpot rate stays GiantWildMultiplier times the usual one
		public const int GiantUsefulAttempts = 16;

		public static int GiantDrop(int ownerType, int key, bool lucky, float wildMultiplier = GiantWildMultiplier)
		{
			int item = 0;
			for (int attempt = 0; attempt < GiantUsefulAttempts; attempt++)
			{
				RollRequest request = new()
				{
					Keys = new[] { RollKey.GiantMini, key, attempt },
					SourceTier = Tiers.Clamp(NpcTiers.EffectiveTier(ownerType) + 1),
					OriginalItem = -1,
					Policy = CategoryPolicy.Weighted,
					IgnoreGating = lucky,
					Wild = attempt == 0,
					// Also the rng that checks a lucky roll above the limit (a jackpot, or rolled again within it)
					WildRng = new Random(unchecked(key + attempt * 7919)),
					WildChanceMultiplier = wildMultiplier
				};
				item = ItemPool.Roll(request);
				if (ItemPool.IsJackpotWorthy(item)) return item;
			}
			return item;
		}

		// A mini's bonus item: any category, of its own level
		public static int MiniDrop(int ownerType, int key, bool lucky, float wildMultiplier = MiniWildMultiplier)
		{
			RollRequest request = new()
			{
				Keys = new[] { RollKey.GiantMini, key, 100 },
				SourceTier = NpcTiers.EffectiveTier(ownerType),
				OriginalItem = -1,
				Policy = CategoryPolicy.Weighted,
				IgnoreGating = lucky,
				Wild = true,
				WildRng = new Random(key),
				WildChanceMultiplier = wildMultiplier
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
		// abundance: LootConfig.LootAbundance when null (tests pass an explicit value)
		public static List<PlannedDrop> Plan(int netId, bool expert, bool lucky = false, bool guaranteed = true, float? abundance = null)
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
			drops.AddRange(ExtraDrops(netId, type, expert, lucky, abundance));
			if (guaranteed) drops.AddRange(GuaranteedSources.For(type));
			return drops;
		}

		// wildRng: at kill time only, the madness tail (WildDropChance) may replace an item by one above the limit; the
		// chance stays the one of the planned item, so the setting really is a share of the drops
		public static List<PlannedDrop> ExtraDrops(int netId, int type, bool expert, bool lucky, float? abundance = null, Random wildRng = null)
		{
			List<PlannedDrop> drops = new();
			LootConfig cfg = LootConfig.Instance;
			float abundanceFactor = Math.Max(0f, abundance ?? cfg.LootAbundance);
			// Split between the number of slots and their chances, so that the expected item count follows the factor
			float slotFactor = (float)Math.Sqrt(abundanceFactor);
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
			int slots = isBoss
				? SlotCount(npcKey, true, critter, cfg.BossDropSlotsMin, cfg.BossDropSlotsMax, cfg.DropTableStyle, slotFactor)
				: SlotCount(npcKey, false, critter, cfg.DropSlotsMin, cfg.DropSlotsMax, cfg.DropTableStyle, slotFactor);

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
					chance = isBoss ? 1f : Math.Min(1f, cfg.DropChance / 100f / (1 + Math.Max(0, tier - sourceTier)) * slotFactor);
				}
				else
				{
					// A boss always drops its first extra slot
					int denominator = isBoss && slot == 0 ? 1 : DropRates.Denominator(item, tier, sourceTier, isBoss, DetRandom.Get(RollKey.MobDrop, npcKey, slot, item, 7));
					chance = Math.Min(1f, cfg.DropRateMultiplier * slotFactor / denominator);
				}
				item = Wild(request, item, wildRng);
				int gap = wildRng != null ? ItemPool.LastJackpotGap : 0;
				(int stackMin, int stackMax) = StackRules.RollRange(item, DetRandom.Get(RollKey.MobDrop, npcKey, slot, item));
				drops.Add(new PlannedDrop { Item = item, StackMin = stackMin, StackMax = stackMax, Chance = chance, Extra = true, JackpotGap = gap });
			}

			// Small frequent drops (material, potion, ammo...), so that most kills give something. Abundance scales the
			// chance: every full 100 % is one sure drop, the rest the chance of one more (each a different item).
			// Critters get only a share of it (CritterDropFactor): they come in swarms and die in one hit
			float commonTotal = cfg.CommonDropChance / 100f * abundanceFactor * (critter ? Math.Max(0f, cfg.CritterDropFactor) : 1f);
			for (int c = 0; !isBoss && commonTotal > 0.0001f && c < 20; c++, commonTotal -= 1f)
			{
				RollRequest common = new()
				{
					Keys = new[] { RollKey.MobDrop, npcKey, 2000 + c },
					SourceTier = sourceTier,
					OriginalItem = -1,
					Policy = CategoryPolicy.Weighted,
					Weights = CommonDropWeights.Instance,
					TierJitterDown = 1,
					IgnoreGating = lucky
				};
				int item = Wild(common, ItemPool.Roll(common), wildRng);
				int gap = wildRng != null ? ItemPool.LastJackpotGap : 0;
				(int stackMin, int stackMax) = StackRules.RollRange(item, DetRandom.Get(RollKey.MobDrop, npcKey, 2000 + c, item));
				drops.Add(new PlannedDrop { Item = item, StackMin = stackMin, StackMax = stackMax, Chance = Math.Min(1f, commonTotal), Extra = true, JackpotGap = gap });
			}
			return drops;
		}

		// Number of random extra slots of an enemy type, with explicit settings (the DropTableLength test compares ranges)
		public static int SlotCount(int npcKey, bool isBoss, bool critter, int minSlots, int maxSlots, DropTableStyle style, float slotFactor)
		{
			Random slotRng = DetRandom.Get(RollKey.MobDropSlots, npcKey);
			maxSlots = Math.Max(minSlots, maxSlots);
			// Critters: at most 2 slots before abundance (scaled below like everyone's)
			if (critter) maxSlots = Math.Min(maxSlots, Math.Max(minSlots, 2));
			int slots = Math.Min(maxSlots, minSlots + (int)Math.Floor((maxSlots - minSlots + 1) * Math.Pow(slotRng.NextDouble(), SlotSkew(isBoss, style))));
			// Abundance: rounded up or down from the same per-type roll, so the plan stays fixed for the world
			double scaledSlots = slots * slotFactor;
			return (int)Math.Floor(scaledSlots) + (slotRng.NextDouble() < scaledSlots - Math.Floor(scaledSlots) ? 1 : 0);
		}

		// Skewed towards few drops for normal enemies: most drop little, a few drop a lot
		public static double SlotSkew(bool isBoss, DropTableStyle style) => isBoss || style == DropTableStyle.Simple ? 1.0 : 1.8;

		// Average SlotCount before abundance, over a uniform per-type roll: floor(n * u^skew) >= k when u >= (k/n)^(1/skew)
		public static double ExpectedSlots(int minSlots, int maxSlots, double skew)
		{
			maxSlots = Math.Max(minSlots, maxSlots);
			int n = maxSlots - minSlots + 1;
			double expected = minSlots;
			for (int k = 1; k < n; k++) expected += 1 - Math.Pow(k / (double)n, 1 / skew);
			return expected;
		}

		private static int Wild(RollRequest request, int planned, Random wildRng)
		{
			if (wildRng == null) return planned;
			request.Wild = true;
			request.WildRng = wildRng;
			return ItemPool.Roll(request);
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

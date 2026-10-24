using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	public class MobLootNPC : GlobalNPC
	{
		// Runs once per enemy (and per variant) at load, before any world exists. The original rules keep running: their
		// items are swapped when they spawn (LootSubstitutionItem). The rules of ordinary enemies are wrapped
		// (PermutableRule), silent in the permutation style where RandomNpcDropRule rolls the permuted entries instead
		// (only the vanilla rule types whose report is complete: DropPermutation.Wrappable; the others are left as they
		// are). Per net ID: variants have rules of their own. We add a rule for extra random drops
		public override void ModifyNPCLoot(NPC npc, NPCLoot npcLoot)
		{
			List<IItemDropRule> rules = npcLoot.Get(false).Where(r => r != null).Distinct().ToList();
			bool wrappable = !npc.boss && !npc.townNPC && !BossParts.IsPart(npc.type) && !BossParts.IsMinion(npc.type) && !NpcTiers.IsBoss(npc.type);
			// Bosses of the boss table (BossPermutation): their complete rules are wrapped too, nested ones included
			bool bossWrap = !wrappable && BossPermutation.WrapsLoot(npc.type);
			int nextWrapper = 0;
			List<DropRateInfo> drops = new();
			List<int> ruleOf = new();
			for (int r = 0; r < rules.Count; r++)
			{
				IItemDropRule rule = rules[r];
				List<DropRateInfo> part = LootSnapshot.Capture(new List<IItemDropRule> { rule });
				drops.AddRange(part);
				if (bossWrap)
				{
					IItemDropRule wrapped = part.Count > 0 ? BossPermutation.Wrap(rule, npc.netID, ref nextWrapper) : rule;
					int[] owners = wrapped != rule ? BossPermutation.Track(wrapped, part) : null;
					ruleOf.AddRange(owners ?? Enumerable.Repeat(-1, part.Count));
					if (owners == null) continue;
					while (npcLoot.Get(false).Contains(rule)) npcLoot.Remove(rule);
					npcLoot.Add(wrapped);
					continue;
				}
				bool wrap = wrappable && DropPermutation.Permutable(rule, part, npc.type);
				ruleOf.AddRange(Enumerable.Repeat(wrap ? r : -1, part.Count));
				if (!wrap) continue;
				// Registered once per net ID: never mutate the rule itself, shared with other net IDs
				while (npcLoot.Get(false).Contains(rule)) npcLoot.Remove(rule);
				npcLoot.Add(new PermutableRule(rule, npc.netID, r, part.Select(d => d.itemId)));
			}
			LootSnapshot.Npcs[npc.netID] = drops;
			LootSnapshot.NpcRules[npc.netID] = ruleOf.ToArray();
			npcLoot.Add(new RandomNpcDropRule(npc.netID));
		}
	}

	// NPCs spawned by another NPC (Mother Slime babies, slimes splitting on death...) must not become loot farms: their kills
	// get no random extra drops, no guaranteed drops and no jackpot, like statue spawns. Decided where NPCs are created
	// (server / single player, where the loot rolls too). Kept per slot: globals are recreated by SetDefaults/Transform
	public class SpawnOriginNPC : GlobalNPC
	{
		public override void OnSpawn(NPC npc, IEntitySource source)
		{
			if (Main.netMode == NetmodeID.MultiplayerClient || npc.whoAmI < 0 || npc.whoAmI >= Main.maxNPCs) return;
			// Every new NPC in a slot sets it again: the flag of a previous NPC never sticks
			MobLootSystem.SpawnedByNpc[npc.whoAmI] = IsNpcChild(npc, source);
		}

		// Vanilla spawns children with EntitySource_Parent (GetSpawnSourceForNPCFromNPCAI, GetSpawnSource_NPCHurt). Not
		// flagged: worm segments (the segment killed drops the worm's loot), bosses (the Lunatic Cultist comes from its
		// devotees) and Dungeon Spirits (a rare reward of their own, not a swarm)
		public static bool IsNpcChild(NPC npc, IEntitySource source) =>
			source is EntitySource_Parent { Entity: NPC parent } && parent.whoAmI != npc.whoAmI && npc.aiStyle != NPCAIStyleID.Worm
			&& npc.type != NPCID.DungeonSpirit && !NpcTiers.IsBoss(BossParts.Owner(npc.type));
	}

	public class MobLootSystem : ModSystem
	{
		public static readonly bool[] SpawnedByNpc = new bool[Main.maxNPCs + 1];

		// Median base life of the natural enemies of each tier (JackpotWorth), rebuilt with the other world caches
		private static readonly Dictionary<int, float> medianLife = new();

		public override void Load() => WorldCaches.Reset += ResetWorth;

		public override void OnWorldLoad()
		{
			Array.Clear(SpawnedByNpc);
			ResetWorth();
		}

		public static void ResetWorth() => medianLife.Clear();

		// Only for the NPC really in its slot (tests and tools also roll loot on detached NPCs)
		public static bool IsSpawnedByNpc(NPC npc) =>
			npc != null && npc.whoAmI >= 0 && npc.whoAmI < Main.maxNPCs && Main.npc[npc.whoAmI] == npc && SpawnedByNpc[npc.whoAmI];

		public static float MedianLife(int tier)
		{
			tier = Tiers.Clamp(tier);
			if (medianLife.TryGetValue(tier, out float cached)) return cached;
			float median = 0f;
			// A tier without natural enemies of its own takes the closest one below
			for (int t = tier; t >= 0 && median <= 0f; t--)
			{
				List<int> lives = EnemySwapPool.NaturalEnemies(t).Where(ContentSamples.NpcsByNetId.ContainsKey)
					.Select(n => ContentSamples.NpcsByNetId[n].lifeMax).Where(l => l > 0).OrderBy(l => l).ToList();
				if (lives.Count == 0) continue;
				median = lives.Count % 2 == 1 ? lives[lives.Count / 2] : (lives[lives.Count / 2 - 1] + lives[lives.Count / 2]) / 2f;
			}
			medianLife[tier] = median;
			return median;
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
		// A guaranteed station, summon or tool, dropped one at a time (shared between players at kill time)
		public bool Single;
		// Kill time only: a jackpot, this many tiers above the progression limit (JackpotItem)
		public int JackpotGap;
		// Tiers by which progression still locks this entry of the final plan (DropHelper.LockGap, 0: it drops): a locked
		// entry does not drop (its chance goes to the others: RandomNpcDropRule.Redistribute), except on a lucky kill
		public int LockGap;
		// The original item of a replaced original drop (0: an extra or guaranteed drop). Permutation style: the item itself
		// (it moved from another enemy, it is not replaced), or for a surprise the item of the content it replaced
		public int Original;
		// Permutation style: an original entry RandomNpcDropRule rolls itself (its vanilla rule is silent, PermutableRule)
		public bool Rolled;
		// Permutation style: a moved content replaced by a random item (PermutationSurpriseChance)
		public bool Surprise;

		public bool Locked => LockGap > 0;
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
			if (!SeedSystem.WorldReady || !ItemPool.Ready || npc == null) return new ItemDropAttemptResult { State = ItemDropAttemptResultState.DidNotRunCode };
			// Permutation: the original entries replace the silent vanilla rules, so statue and NPC-spawned enemies drop them
			// as vanilla would (NotFromStatue conditions included); only the extra, guaranteed and bonus drops are withheld
			bool permuted = DropPermutation.Active(netId) || BossPermutation.Active(netId);
			if (permuted) DropRolledOriginals(info, npc);
			if (npc.SpawnedFromStatue || MobLootSystem.IsSpawnedByNpc(npc) || !ExtraDropsAllowedNow(info))
			{
				return new ItemDropAttemptResult { State = permuted ? ItemDropAttemptResultState.Success : ItemDropAttemptResultState.DidNotRunCode };
			}
			// Weak swarm enemies (and critters: never) get a share of the jackpot chances (luck and madness)
			float worth = JackpotWorth(npc);
			IEnumerable<PlannedDrop> drops = KillDrops(netId, BossParts.Owner(npc.type), info.IsExpertMode, DropHelper.RollLucky(worth), new Random(info.rng.Next()), worth)
				.Concat(GuaranteedSources.For(npc.type));
			// Stronger enemies (type and individual) drop more often. Guaranteed drops (stations...) leave adaptive power
			// out, so that how fast they are found does not depend on the player's equipment
			float bonus = EnemyIndividualNPC.LootBonus(npc);
			float guaranteedBonus = EnemyIndividualNPC.LootBonus(npc, false);
			int players = Main.netMode == NetmodeID.Server ? Main.CurrentFrameFlags.ActivePlayersCount : 1;
			// A boss carrying guaranteed items (GuaranteedSources.BossHosts) gives them at every kill, however many players
			// and whatever its size
			if (NpcTiers.IsBoss(BossParts.Owner(npc.type)))
			{
				guaranteedBonus = Math.Max(1f, guaranteedBonus);
				players = 1;
			}
			bool shared = ProgressionConfig.Instance.ScaleGuaranteesWithPlayers;
			foreach (PlannedDrop drop in drops)
			{
				if (info.rng.NextFloat() >= KillChance(drop, drop.Guaranteed ? guaranteedBonus : bonus, players, shared)) continue;
				int stack = info.rng.Next(drop.StackMin, Math.Max(drop.StackMin, drop.StackMax) + 1);
				DropMarked(npc, drop.Item, stack, drop.JackpotGap);
			}
			foreach ((int item, int stack, int gap) in BonusDrops(npc, netId, info.rng.Next(), DropHelper.RollLucky(worth), worth))
			{
				DropMarked(npc, item, stack, gap);
			}
			return new ItemDropAttemptResult { State = ItemDropAttemptResultState.Success };
		}

		// Permutation style: the original entries of the enemy's plan (moved contents with this enemy's conditions, kept items
		// of the same rules), each with its own chance (no loot bonus, no luck, as the vanilla rules). A locked entry does not
		// drop, except on a lucky kill, kept as rarely as a locked extra drop (KillDrops); its chance went to the extra drop
		private void DropRolledOriginals(DropAttemptInfo info, NPC npc)
		{
			bool farm = npc.SpawnedFromStatue || MobLootSystem.IsSpawnedByNpc(npc);
			bool lucky = !farm && DropHelper.RollLucky(JackpotWorth(npc));
			// The owner's floor and table, as KillDrops: an Eater of Worlds body or tail segment drops like the head
			foreach (PlannedDrop drop in OriginalDrops(netId, BossParts.Owner(npc.type)))
			{
				if (!drop.Rolled || drop.Item <= ItemID.None) continue;
				if (drop.Conditions != null && drop.Conditions.Any(c => c != null && !c.CanDrop(info))) continue;
				if (info.rng.NextFloat() >= drop.Chance) continue;
				int gap = 0;
				if (drop.Locked)
				{
					if (!lucky || drop.LockGap > 1 && info.rng.NextDouble() >= ItemPool.LuckyKeepChance(drop.LockGap)) continue;
					gap = ItemPool.IsJackpotWorthy(drop.Item) ? drop.LockGap : 0;
				}
				int min = Math.Max(1, drop.StackMin);
				int stack = info.rng.Next(min, Math.Max(min, drop.StackMax) + 1);
				DropMarked(npc, drop.Item, stack, gap);
			}
		}

		// A random extra drop placing a basic station (a table, a chair, a work bench...) comes this much less often than planned:
		// these are already guaranteed (GuaranteedSources), and a pile of random tables is no reward
		public const float RandomStationChanceFactor = 0.25f;

		// The chance of a planned drop at this kill. Plans, findability and the journal keep the planned chance: only the roll
		// changes. Guaranteed single drops (stations, summons, tools) are shared between the players (ScaleGuaranteesWithPlayers)
		public static float KillChance(PlannedDrop drop, float bonus, int players, bool sharedGuarantees)
		{
			float chance = Math.Min(1f, drop.Chance * bonus);
			if (drop.Guaranteed && drop.Single) chance = GuaranteeChance(chance, players, sharedGuarantees);
			else if (drop.Extra && !drop.Guaranteed && Stations.IsBasicPlacer(drop.Item)) chance *= RandomStationChanceFactor;
			return chance;
		}

		// A guaranteed single drop's chance with this many active players: divided between them when shared
		public static float GuaranteeChance(float chance, int players, bool shared) => shared && players > 1 ? chance / players : chance;

		// Share of the jackpot chances (RuntimeJackpotChance, WildDropChance) a kill gets: its base life against the median of
		// the natural enemies of its tier, in [0.1, 1], so that farming a swarm of weak enemies pays no more than ordinary
		// fights. Critters: 0 (never a jackpot). Bosses and anything sturdier than the median: 1. Runtime rolls only
		public const float MinJackpotWorth = 0.1f;

		public static float JackpotWorth(NPC npc) => npc == null ? 1f : JackpotWorth(npc.netID);

		public static float JackpotWorth(int netId)
		{
			if (!ContentSamples.NpcsByNetId.TryGetValue(netId, out NPC sample)) return 1f;
			if (NPCID.Sets.CountsAsCritter[sample.type]) return 0f;
			int owner = BossParts.Owner(sample.type);
			if (NpcTiers.IsBoss(owner)) return 1f;
			// Parts share their owner's life
			if (owner != sample.type && ContentSamples.NpcsByNetId.TryGetValue(owner, out NPC ownerSample)) sample = ownerSample;
			return WorthOf(sample.lifeMax, MobLootSystem.MedianLife(NpcTiers.EffectiveTier(owner)));
		}

		public static float WorthOf(float life, float medianLife) =>
			medianLife <= 0f || life <= 0f ? 1f : Math.Clamp(life / medianLife, MinJackpotWorth, 1f);

		// Giants: one more useful item a tier above their level, three times as likely to be a jackpot. Minis (small, agile and
		// dangerous): one more item of their level, one and a half times as likely (5 and 2 until 1.3.4: too many jackpots).
		// Never for boss minions and parts (GiantBonusAllowed). Rolled where the loot drops (server), with the runtime rng
		public const float GiantWildMultiplier = 3f;
		public const float MiniWildMultiplier = 1.5f;

		// worth: JackpotWorth, multiplies the madness chance on top of the giant or mini multiplier (0: no madness)
		public static List<(int item, int stack, int gap)> BonusDrops(NPC npc, int netId, int key, bool lucky, float worth = 1f)
		{
			List<(int, int, int)> drops = new();
			if (!LootConfig.Instance.RandomizeMobDrops || !npc.TryGetGlobalNPC(out EnemyIndividualNPC individual) || !GiantBonusAllowed(netId, npc.type)) return drops;
			EnemiesConfig cfg = EnemiesConfig.Instance;
			int owner = BossParts.Owner(npc.type);
			int item;
			if (individual.Kind == EnemyKind.Giant && cfg.GiantBonusDrop) item = GiantDrop(owner, key, lucky, GiantWildMultiplier * worth);
			else if (individual.Kind == EnemyKind.Mini && cfg.MiniBonusDrop) item = MiniDrop(owner, key, lucky, MiniWildMultiplier * worth);
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
			return !BossParts.IsMinion(npcType) && !BossParts.IsMinion(owner) && CanHaveExtraDrops(owner, NpcTiers.IsBoss(owner), LootSnapshot.ForNpc(netId));
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
					// A multiplier of 0 means no madness (RollRequest reads 0 as 1)
					Wild = attempt == 0 && wildMultiplier > 0,
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
				Wild = wildMultiplier > 0,
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
			if (!NpcTiers.IsBoss(BossParts.Owner(npc.type))) return true;
			if (!npc.boss) return false;
			if (npc.type == NPCID.Retinazer || npc.type == NPCID.Spazmatism) return new Conditions.MissingTwin().CanDrop(info);
			return true;
		}

		// The progression lock of an enemy's drops never goes below this tier (a boss always gives the items of its own level)
		public static int MinLimit(int type) => NpcTiers.IsBoss(type) ? NpcTiers.BaseTier(type) : 0;

		// The tier an enemy's drops are rolled at. Kill time: EffectiveTier (ScaleWeakEnemiesWithProgression follows the
		// unlocked tier). Planning (the lucky plan: findability, guarantees, hints, recipe availability): BaseTier, so the
		// world caches never depend on progression
		public static int SourceTier(int type, bool planning) => planning ? NpcTiers.BaseTier(type) : NpcTiers.EffectiveTier(type);

		// Everything an enemy can drop in this world: the replaced original drops, then the extra ones and the guaranteed ones.
		// Every entry comes from the final plan (everything unlocked: the same items from the first day to the Moon Lord).
		// lucky: that final plan as it is (hints, findability). Otherwise what drops now: the entries progression still locks
		// are left out (includeLocked: kept, flagged, for the codex) and their chances go to the others (Redistribute)
		// abundance: LootConfig.LootAbundance when null (tests pass an explicit value)
		// style: the configured one when null (the pacing model compares both)
		public static List<PlannedDrop> Plan(int netId, bool expert, bool lucky = false, bool guaranteed = true, float? abundance = null, bool includeLocked = false, DropTableStyle? style = null)
		{
			int type = BossParts.Owner(ContentSamples.NpcsByNetId.TryGetValue(netId, out NPC sample) ? sample.type : netId);
			List<PlannedDrop> drops = OriginalDrops(netId, type, style, planning: lucky);
			drops.AddRange(ExtraDrops(netId, type, expert, abundance, style: style, planning: lucky));
			if (guaranteed) drops.AddRange(GuaranteedSources.For(type));
			if (lucky)
			{
				for (int i = 0; i < drops.Count; i++)
				{
					PlannedDrop drop = drops[i];
					drop.LockGap = 0;
					drops[i] = drop;
				}
				return drops;
			}
			Redistribute(drops);
			return includeLocked ? drops : drops.Where(d => !d.Locked).ToList();
		}

		// The original drops of an enemy and their final replacements (LootSubstitutionItem swaps them as they spawn). In the
		// permutation style (style: the configured one when null), the entries of the net ID's permuted table instead (bosses:
		// BossPermutation's)
		// planning: rolled at the enemy's base tier (SourceTier)
		public static List<PlannedDrop> OriginalDrops(int netId, int type, DropTableStyle? style = null, bool planning = false)
		{
			List<PlannedDrop> drops = new();
			int minLimit = MinLimit(type);
			DropPermutation.Placed[] table = BossPermutation.InStyle(netId, style) ? BossPermutation.For(netId)
				: DropPermutation.InStyle(netId, style) ? DropPermutation.For(netId) : null;
			if (table != null)
			{
				foreach (DropPermutation.Placed placed in table)
				{
					DropRateInfo entry = placed.Entry;
					// Entries vanilla still rolls (rules left running) are not replaced: they drop as they are
					int lockGap = placed.Rolled && !DropHelper.KeepOriginal(entry.itemId) ? DropHelper.LockGap(entry.itemId, minLimit) : 0;
					drops.Add(new PlannedDrop { Item = entry.itemId, StackMin = entry.stackMin, StackMax = entry.stackMax, Chance = entry.dropRate, Conditions = entry.conditions,
						Original = placed.Surprise ? placed.Replaced : entry.itemId, LockGap = lockGap, Rolled = placed.Rolled, Surprise = placed.Surprise });
				}
				return drops;
			}
			foreach (DropRateInfo entry in LootSnapshot.ForNpc(netId))
			{
				int item = entry.itemId;
				int min = entry.stackMin;
				int max = entry.stackMax;
				int lockGap = 0;
				// The evil boss this world lacks in the permutation style drops its vanilla table (BossPermutation.LeftVanilla)
				if (LootConfig.Instance.RandomizeMobDrops && !BossPermutation.LeftVanilla(netId, style))
				{
					item = DropHelper.Replacement(RollKey.MobDrop, DetRandom.NpcKey(type), entry.itemId, SourceTier(type, planning), minLimit, true);
					(min, max) = DropHelper.ScaledRange(entry.itemId, entry.stackMin, entry.stackMax, item);
					if (!DropHelper.KeepOriginal(entry.itemId)) lockGap = DropHelper.LockGap(item, minLimit);
				}
				drops.Add(new PlannedDrop { Item = item, StackMin = min, StackMax = max, Chance = entry.dropRate, Conditions = entry.conditions, Original = entry.itemId, LockGap = lockGap });
			}
			return drops;
		}

		// The chances of the locked entries (original drops included) go to the unlocked extra entries of the same enemy, in
		// proportion: each is multiplied by (unlocked + locked) / unlocked, capped at 1, so that an enemy drops about as much
		// before its drops unlock. Guaranteed drops neither give nor receive; original drops cannot receive (vanilla rules
		// roll them). Locked entries keep their own chance (the codex, lucky kills). Pure: reads only the entries
		public static void Redistribute(List<PlannedDrop> drops)
		{
			float locked = 0f, receiving = 0f;
			foreach (PlannedDrop drop in drops)
			{
				if (drop.Guaranteed) continue;
				if (drop.Locked) locked += Math.Clamp(drop.Chance, 0f, 1f);
				else if (drop.Extra) receiving += Math.Clamp(drop.Chance, 0f, 1f);
			}
			if (locked <= 0f || receiving <= 0f) return;
			float factor = (receiving + locked) / receiving;
			for (int i = 0; i < drops.Count; i++)
			{
				PlannedDrop drop = drops[i];
				if (drop.Guaranteed || drop.Locked || !drop.Extra) continue;
				drop.Chance = Math.Min(1f, drop.Chance * factor);
				drops[i] = drop;
			}
		}

		// The extra drops one kill rolls: the unlocked entries of the final plan, with the chances of the locked ones
		// (Redistribute) and the madness tail (rng, worth). On a lucky kill, a locked entry may drop too: kept as rarely as a
		// lucky roll that far above the limit (ItemPool.LuckyKeepChance, runtime rng), a jackpot when useful. Server side
		public static List<PlannedDrop> KillDrops(int netId, int type, bool expert, bool lucky, Random rng, float worth)
		{
			List<PlannedDrop> plan = OriginalDrops(netId, type);
			plan.AddRange(ExtraDrops(netId, type, expert, null, rng, worth));
			Redistribute(plan);
			List<PlannedDrop> drops = new();
			foreach (PlannedDrop drop in plan)
			{
				if (!drop.Extra) continue;
				if (!drop.Locked)
				{
					drops.Add(drop);
					continue;
				}
				if (!lucky || rng == null || drop.LockGap > 1 && rng.NextDouble() >= ItemPool.LuckyKeepChance(drop.LockGap)) continue;
				PlannedDrop kept = drop;
				kept.JackpotGap = ItemPool.IsJackpotWorthy(drop.Item) ? drop.LockGap : 0;
				kept.LockGap = 0;
				drops.Add(kept);
			}
			return drops;
		}

		// The final plan of the random extra drops (everything unlocked), each entry flagged with its progression lock.
		// wildRng: at kill time only, the madness tail (WildDropChance) may replace an unlocked item by one above the limit;
		// the chance stays the one of the planned item, so the setting really is a share of the drops. wildMultiplier: scales
		// that chance (JackpotWorth), 0 = no madness
		// style: the configured one when null
		public static List<PlannedDrop> ExtraDrops(int netId, int type, bool expert, float? abundance = null, Random wildRng = null, float wildMultiplier = 1f, DropTableStyle? style = null,
			bool planning = false)
		{
			if (wildMultiplier <= 0f) wildRng = null;
			List<PlannedDrop> drops = new();
			LootConfig cfg = LootConfig.Instance;
			DropTableStyle tableStyle = style ?? cfg.DropTableStyle;
			float abundanceFactor = Math.Max(0f, abundance ?? cfg.LootAbundance);
			// Split between the number of slots and their chances, so that the expected item count follows the factor
			float slotFactor = (float)Math.Sqrt(abundanceFactor);
			if (!cfg.RandomizeMobDrops || tableStyle == DropTableStyle.MirrorOriginal) return drops;
			if (GeneralConfig.Instance.RandomizerMode == RandomizerMode.Shuffle) return drops;
			List<DropRateInfo> original = LootSnapshot.ForNpc(netId);
			bool isBoss = NpcTiers.IsBoss(type);
			if (!CanHaveExtraDrops(type, isBoss, original)) return drops;
			// In expert mode, a boss with a treasure bag gets no extra drops: the content of its bag is replaced instead (the
			// permutation style: the bag rolls them, BagPlan.Plan)
			if (isBoss && expert && original.Any(d => ItemID.Sets.BossBag[d.itemId])) return drops;

			// Permutation: PermutationExtraSlots random slots at vanilla-like rates, not scaled by abundance, with their own
			// weights (no materials: the moved original drops give them), plus the chances of the locked entries
			// (Redistribute); no small frequent drops. Bosses (BossPermutation) too, at boss rates (the first slot always drops),
			// those of the boss owning their bag (ExtrasOwner: both Twins roll Retinazer's, as their bag). Critters have no place
			// in it but get the same slots, each at CritterDropFactor times the rate, and no small frequent drop either. The
			// enemies it leaves out (bosses outside the boss table) keep the vanilla-like extras
			bool permutation = DropPermutation.InStyle(netId, tableStyle) || BossPermutation.InStyle(netId, tableStyle);
			bool critter = NPCID.Sets.CountsAsCritter[type];
			bool critterSlots = critter && tableStyle == DropTableStyle.Permutation;
			bool permutationSlots = permutation || critterSlots;
			float critterFactor = critterSlots ? Math.Max(0f, cfg.CritterDropFactor) : 1f;
			int npcKey = DetRandom.NpcKey(permutation && isBoss ? BossPermutation.ExtrasOwner(type) : type);
			int sourceTier = SourceTier(type, planning);
			int minLimit = MinLimit(type);
			DropTableStyle slotStyle = tableStyle == DropTableStyle.Permutation ? DropTableStyle.VanillaLike : tableStyle;
			int slots = permutationSlots ? Math.Clamp(cfg.PermutationExtraSlots, 0, 3)
				: isBoss
				? SlotCount(npcKey, true, critter, cfg.BossDropSlotsMin, cfg.BossDropSlotsMax, slotStyle, slotFactor)
				: SlotCount(npcKey, false, critter, cfg.DropSlotsMin, cfg.DropSlotsMax, slotStyle, slotFactor);

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
					IgnoreGating = true,
					MinLimit = minLimit,
					Weights = permutationSlots ? cfg.PermutationExtraWeights : null
				};
				int item = ItemPool.Roll(request, out int tier);
				float chance;
				if (slotStyle == DropTableStyle.Simple)
				{
					chance = isBoss ? 1f : Math.Min(1f, cfg.DropChance / 100f / (1 + Math.Max(0, tier - sourceTier)) * slotFactor);
				}
				else
				{
					// A boss always drops its first extra slot
					int denominator = isBoss && slot == 0 ? 1 : DropRates.Denominator(item, tier, sourceTier, isBoss, DetRandom.Get(RollKey.MobDrop, npcKey, slot, item, 7));
					chance = Math.Min(1f, cfg.DropRateMultiplier * (permutationSlots ? 1f : slotFactor) * critterFactor / denominator);
				}
				int lockGap = DropHelper.LockGap(item, minLimit);
				item = Wild(request, item, lockGap, wildRng, wildMultiplier, out int gap);
				(int stackMin, int stackMax) = StackRules.RollRange(item, DetRandom.Get(RollKey.MobDrop, npcKey, slot, item));
				drops.Add(new PlannedDrop { Item = item, StackMin = stackMin, StackMax = stackMax, Chance = chance, Extra = true, JackpotGap = gap, LockGap = lockGap });
			}
			if (permutationSlots) return drops;

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
					IgnoreGating = true
				};
				int planned = ItemPool.Roll(common);
				int lockGap = DropHelper.LockGap(planned, 0);
				int item = Wild(common, planned, lockGap, wildRng, wildMultiplier, out int gap);
				(int stackMin, int stackMax) = StackRules.RollRange(item, DetRandom.Get(RollKey.MobDrop, npcKey, 2000 + c, item));
				drops.Add(new PlannedDrop { Item = item, StackMin = stackMin, StackMax = stackMax, Chance = Math.Min(1f, commonTotal), Extra = true, JackpotGap = gap, LockGap = lockGap });
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

		// The madness tail of an unlocked planned item: the same roll gated, with the runtime rng; only a jackpot replaces the
		// planned item (without a wild roll, the gated item would be a stand-in). Locked items get none
		private static int Wild(RollRequest request, int planned, int lockGap, Random wildRng, float multiplier, out int jackpotGap)
		{
			jackpotGap = 0;
			if (wildRng == null || multiplier <= 0f || lockGap > 0) return planned;
			request.IgnoreGating = false;
			request.Wild = true;
			request.WildRng = wildRng;
			request.WildChanceMultiplier = multiplier;
			int wild = ItemPool.Roll(request);
			if (ItemPool.LastJackpotGap <= 0) return planned;
			jackpotGap = ItemPool.LastJackpotGap;
			return wild;
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

		// The bestiary is built before any world exists: the original drops are reported by their own rules. In a world
		// showing its own drops (WorldDropsReport), the plan's entries this rule rolls (the silenced rules report nothing)
		public void ReportDroprates(List<DropRateInfo> drops, DropRateInfoChainFeed ratesInfo)
		{
			if (WorldDropsReport.Shows(netId)) WorldDropsReport.Report(WorldDropsReport.NpcPlan(netId), drops, ratesInfo);
			Chains.ReportDroprates(ChainedRules, 1f, drops, ratesInfo);
		}
	}
}

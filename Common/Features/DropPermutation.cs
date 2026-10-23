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
	// DropTableStyle.Permutation: the original drop entries of the ordinary enemies are shuffled between them. Each entry of
	// an enemy's flattened drop table (LootSnapshot) is a place; its content (item, stack range, own chance) moves to another
	// place of the same tier and access class, and takes that place's conditions. Every enemy keeps its number of entries,
	// and every item of a permuted vanilla entry still drops somewhere. An eligible enemy without any vanilla entry gets one
	// synthetic place (a random item of its level), permuted like the others. Bosses, their parts and minions, town NPCs,
	// critters and harmless NPCs are left out. Tables are per net ID (variants such as Pinky have rules of their own); a
	// variant whose snapshot is the same as its type's shares the type's table. Computed from the world seed, the synced
	// settings and the snapshot only: the server (kills) and the clients (journal, codex) get the same plan
	public static class DropPermutation
	{
		// Conditions that decide whether an entry can drop at all in a world (difficulty, special seeds, never): contents only
		// move between places with the same set of them, so an expert-only item stays expert-only. Other conditions
		// (hardmode, biome, event...) don't split the groups: they come with the place (Findability raises the tier for them)
		private static readonly HashSet<Type> Restrictive = new()
		{
			typeof(Conditions.IsExpert), typeof(Conditions.IsMasterMode), typeof(Conditions.NotExpert), typeof(Conditions.NotMasterMode),
			typeof(Conditions.IsCrimsonAndNotExpert), typeof(Conditions.IsCorruptionAndNotExpert),
			typeof(Conditions.LegacyHack_IsBossAndExpert), typeof(Conditions.LegacyHack_IsBossAndNotExpert),
			typeof(Conditions.RemixSeed), typeof(Conditions.RemixSeedEasymode), typeof(Conditions.RemixSeedHardmode), typeof(Conditions.NotRemixSeed),
			typeof(Conditions.TenthAnniversaryIsUp), typeof(Conditions.TenthAnniversaryIsNotUp),
			typeof(Conditions.DontStarveIsUp), typeof(Conditions.DontStarveIsNotUp),
			typeof(Conditions.DrunkWorldIsUp), typeof(Conditions.DrunkWorldIsNotUp),
			typeof(Conditions.ForTheWorthyIsUp), typeof(Conditions.ForTheWorthyIsNotUp),
			typeof(Conditions.BeesSeed), typeof(Conditions.NotBeesSeed),
			typeof(Conditions.NoTrapsSeed), typeof(Conditions.NotNoTrapsSeed),
			typeof(Conditions.ZenithSeedIsUp), typeof(Conditions.ZenithSeedIsNotUp),
			typeof(Conditions.MechdusaKill), typeof(Conditions.NeverTrue)
		};

		// The vanilla rule types whose ReportDroprates lists everything they can drop, with its chance and conditions: only
		// these are wrapped (and their entries permuted). Exact types: a subclass (modded) may drop differently. Anything else
		// (modded rules, slime body items, per-player drops, OneFromRulesRule...) keeps running as it is
		private static readonly HashSet<Type> CompleteRules = new()
		{
			typeof(CommonDrop), typeof(CommonDropNotScalingWithLuck), typeof(CommonDropWithRerolls), typeof(ItemDropWithConditionRule),
			typeof(OneFromOptionsDropRule), typeof(OneFromOptionsNotScaledWithLuckDropRule),
			typeof(DropBasedOnExpertMode), typeof(DropBasedOnMasterMode), typeof(LeadingConditionRule)
		};

		private static readonly HashSet<Type> CompleteChains = new()
		{
			typeof(Chains.TryIfSucceeded), typeof(Chains.TryIfFailedRandomRoll), typeof(Chains.TryIfDoesntFillConditions)
		};

		// Groups smaller than this are merged with the next tier of the same access class (like ItemPool's shuffle)
		public const int MinGroupSize = 3;

		// The access class of an entry: its restrictive condition types, sorted ("" for an ordinary entry)
		public static string AccessOf(List<IItemDropRuleCondition> conditions)
		{
			if (conditions == null || conditions.Count == 0) return "";
			return string.Join("+", conditions.Where(c => c != null && Restrictive.Contains(c.GetType())).Select(c => c.GetType().Name).Distinct().OrderBy(n => n, StringComparer.Ordinal));
		}

		// One entry position of an enemy's drop table
		public struct Place
		{
			// The net ID of the table (DropPermutation.UnitOf)
			public int Owner;
			public int Index;
			// DetRandom.NpcKey of the owner: with Index, the stable identity of the place and of its original content
			public int OwnerKey;
			public int Tier;
			public string Access;
			public DropRateInfo Entry;
			// The random entry of an enemy without vanilla drops (Synthetic)
			public bool Synthetic;
		}

		// An entry of a permuted enemy's final table. Entry: what drops (the content, with the place's conditions)
		public struct Placed
		{
			public DropRateInfo Entry;
			// Rolled by RandomNpcDropRule (its vanilla rule is silent: PermutableRule); false: a rule left running, vanilla rolls it
			public bool Rolled;
			// Its content came through the permutation (false: kept, such as coins and progression items)
			public bool Permuted;
			// Where the content comes from (owner net ID, entry index)
			public int FromNetId;
			public int FromIndex;
		}

		public sealed class WorldPlan
		{
			public List<Place> Places;
			// ContentOf[p]: the index of the place whose content goes to place p
			public int[] ContentOf;
			public List<List<int>> Groups;
			// Per table net ID (UnitOf)
			public Dictionary<int, Placed[]> ByUnit;
		}

		private static WorldPlan cached;

		public static void Reset() => cached = null;

		// Pure: the groups of places (indices into places), each of one access class and consecutive tiers, in a stable order
		public static List<List<int>> Groups(IReadOnlyList<Place> places)
		{
			List<List<int>> groups = new();
			List<int> order = Enumerable.Range(0, places.Count)
				.OrderBy(i => places[i].Access ?? "", StringComparer.Ordinal).ThenBy(i => places[i].OwnerKey).ThenBy(i => places[i].Owner).ThenBy(i => places[i].Index).ToList();
			foreach (IGrouping<string, int> access in order.GroupBy(i => places[i].Access ?? ""))
			{
				List<int> members = access.ToList();
				List<List<int>> classGroups = new();
				List<int> current = new();
				for (int t = 0; t < Tiers.Count; t++)
				{
					current.AddRange(members.Where(i => Tiers.Clamp(places[i].Tier) == t));
					if (current.Count >= MinGroupSize)
					{
						classGroups.Add(current);
						current = new List<int>();
					}
				}
				if (current.Count > 0)
				{
					if (classGroups.Count > 0) classGroups[^1].AddRange(current);
					else classGroups.Add(current);
				}
				groups.AddRange(classGroups);
			}
			return groups;
		}

		// The rank of a place (and of its original content) in its group: from the seed and its identity only, so that a place
		// joining or leaving a group (a setting changed which entries are kept) does not move the others
		public static int Rank(int seed, Place place) => DetRandom.Hash(seed, RollKey.DropPermutation, place.OwnerKey, place.Index);

		// Pure: ContentOf for these places and this seed. In each group the places are sorted by rank, and each takes the
		// content of the next one (a random cycle: no content stays at its place). Removing one place only changes the
		// content of the place before it
		public static int[] Permute(IReadOnlyList<Place> places, int seed, out List<List<int>> groups)
		{
			int[] contentOf = Enumerable.Range(0, places.Count).ToArray();
			groups = Groups(places);
			foreach (List<int> group in groups)
			{
				List<int> order = group.OrderBy(i => Rank(seed, places[i])).ThenBy(i => places[i].OwnerKey).ThenBy(i => places[i].Owner).ThenBy(i => places[i].Index).ToList();
				for (int k = 0; k < order.Count; k++) contentOf[order[k]] = order[(k + 1) % order.Count];
			}
			return contentOf;
		}

		// Load-time facts (samples, snapshot): cached until unload
		private static readonly Dictionary<int, bool> eligible = new();
		private static readonly Dictionary<int, int> units = new();

		// Can this net ID's drops be permuted (whatever the style)? Settings play no part, so that a config change does not
		// move the places of the world: ordinary enemies with drops, or without drops but with a coin value (the synthetic
		// place). Not critters, harmless or projectile NPCs, town NPCs, bosses, their parts and minions
		public static bool IsPermutable(int netId)
		{
			if (eligible.TryGetValue(netId, out bool known)) return known;
			bool result = false;
			if (ContentSamples.NpcsByNetId.TryGetValue(netId, out NPC sample))
			{
				int type = sample.type;
				result = type > 0 && !sample.boss && !BossParts.IsPart(type) && !BossParts.IsMinion(type) && !NpcTiers.IsBoss(type)
					&& !sample.townNPC && !NPCID.Sets.ActsLikeTownNPC[type] && !NPCID.Sets.IsTownPet[type] && type != NPCID.TorchGod
					&& !NPCID.Sets.CountsAsCritter[type] && !NPCID.Sets.ProjectileNPC[type] && sample.lifeMax > 5 && !sample.friendly
					&& (LootSnapshot.ForNpc(netId).Count > 0 || sample.value > 0);
			}
			eligible[netId] = result;
			return result;
		}

		// The net ID whose table this net ID uses: its type's when the snapshots are the same (variants sharing the type's
		// rules), else its own
		public static int UnitOf(int netId)
		{
			if (units.TryGetValue(netId, out int unit)) return unit;
			unit = netId;
			if (ContentSamples.NpcsByNetId.TryGetValue(netId, out NPC sample) && sample.type != netId && IsPermutable(sample.type) && SameSnapshot(netId, sample.type))
			{
				unit = sample.type;
			}
			units[netId] = unit;
			return unit;
		}

		private static bool SameSnapshot(int a, int b)
		{
			List<DropRateInfo> x = LootSnapshot.ForNpc(a), y = LootSnapshot.ForNpc(b);
			if (x.Count != y.Count || !LootSnapshot.RulesOf(a).SequenceEqual(LootSnapshot.RulesOf(b))) return false;
			for (int i = 0; i < x.Count; i++)
			{
				if (x[i].itemId != y[i].itemId || x[i].stackMin != y[i].stackMin || x[i].stackMax != y[i].stackMax || x[i].dropRate != y[i].dropRate) return false;
				IEnumerable<string> cx = (x[i].conditions ?? new List<IItemDropRuleCondition>()).Select(c => c?.GetType().FullName ?? "");
				IEnumerable<string> cy = (y[i].conditions ?? new List<IItemDropRuleCondition>()).Select(c => c?.GetType().FullName ?? "");
				if (!cx.SequenceEqual(cy)) return false;
			}
			return true;
		}

		// The permutation applies to this net ID with these settings (style: the configured one when null)
		public static bool InStyle(int netId, DropTableStyle? style = null)
		{
			LootConfig cfg = LootConfig.Instance;
			return cfg.RandomizeMobDrops && (style ?? cfg.DropTableStyle) == DropTableStyle.Permutation && IsPermutable(netId);
		}

		// At kill time: the wrapped vanilla rules of this net ID are silent, RandomNpcDropRule rolls its plan
		public static bool Active(int netId) => SeedSystem.WorldReady && ItemPool.Ready && InStyle(netId);

		public static void Unload()
		{
			cached = null;
			eligible.Clear();
			units.Clear();
		}

		// The wrapped rules a permutation silences: those holding at least one item that is not kept (a rule of kept items only,
		// coins or keys..., keeps running as it is). Shared by RolledEntries and PermutableRule, so both always agree
		public static bool Silences(IEnumerable<int> ruleItems) => ruleItems.Any(i => !DropHelper.KeepOriginal(i));

		// Entries our rule rolls: those of a silenced rule
		public static bool[] RolledEntries(List<DropRateInfo> entries, int[] rules)
		{
			bool[] rolled = new bool[entries.Count];
			HashSet<int> silenced = new();
			foreach (int rule in rules.Where(r => r >= 0).Distinct())
			{
				if (Silences(Enumerable.Range(0, Math.Min(entries.Count, rules.Length)).Where(i => rules[i] == rule).Select(i => entries[i].itemId))) silenced.Add(rule);
			}
			for (int i = 0; i < entries.Count; i++) rolled[i] = i < rules.Length && silenced.Contains(rules[i]);
			return rolled;
		}

		// Every table of the world (UnitOf of the permutable net IDs), in a stable order
		public static List<int> Units()
		{
			SortedSet<int> result = new();
			foreach (int netId in ContentSamples.NpcsByNetId.Keys)
			{
				if (IsPermutable(netId)) result.Add(UnitOf(netId));
			}
			return result.ToList();
		}

		// The synthetic entry of an eligible enemy without any vanilla entry: a random item of its base tier with a vanilla-like
		// chance, created whatever the settings (EnemiesWithoutDropsCanDrop only gates the random extra slot), so the places of
		// the world don't depend on them. Its item follows the item pool (categories, protections)
		public static bool TrySynthetic(int unit, int seed, out DropRateInfo entry)
		{
			entry = default;
			if (LootSnapshot.ForNpc(unit).Count > 0 || !ItemPool.Ready) return false;
			int key = DetRandom.NpcKey(unit);
			int tier = NpcTiers.BaseTier(unit);
			RollRequest request = new()
			{
				Keys = new[] { RollKey.DropPermutation, seed, key },
				SourceTier = tier,
				OriginalItem = -1,
				Policy = CategoryPolicy.Weighted,
				IgnoreGating = true
			};
			int item = ItemPool.Roll(request, out int rolledTier);
			if (item <= ItemID.None) return false;
			int denominator = DropRates.Denominator(item, rolledTier, tier, false, DetRandom.Get(RollKey.DropPermutation, seed, key, 1));
			(int min, int max) = StackRules.RollRange(item, DetRandom.Get(RollKey.DropPermutation, seed, key, 2));
			entry = new DropRateInfo(item, min, max, 1f / Math.Max(1, denominator), null);
			return true;
		}

		// The places of this world: every rolled entry whose item is not kept, of every table, and the synthetic ones
		public static List<Place> CollectPlaces(int seed)
		{
			List<Place> places = new();
			foreach (int unit in Units())
			{
				List<DropRateInfo> entries = LootSnapshot.ForNpc(unit);
				bool[] rolled = RolledEntries(entries, LootSnapshot.RulesOf(unit));
				int key = DetRandom.NpcKey(unit);
				int tier = NpcTiers.BaseTier(unit);
				for (int i = 0; i < entries.Count; i++)
				{
					if (!rolled[i] || DropHelper.KeepOriginal(entries[i].itemId)) continue;
					places.Add(new Place { Owner = unit, Index = i, OwnerKey = key, Tier = tier, Access = AccessOf(entries[i].conditions), Entry = entries[i] });
				}
				if (TrySynthetic(unit, seed, out DropRateInfo synthetic))
				{
					places.Add(new Place { Owner = unit, Index = entries.Count, OwnerKey = key, Tier = tier, Access = "", Entry = synthetic, Synthetic = true });
				}
			}
			return places;
		}

		// The whole plan for a seed. The tier is the enemy's base tier, not the effective one: that one follows progression
		// (ScaleWeakEnemiesWithProgression) and the drops of a world in progress must not move
		public static WorldPlan Build(int seed)
		{
			List<Place> places = CollectPlaces(seed);
			int[] contentOf = Permute(places, seed, out List<List<int>> groups);
			Dictionary<int, Placed[]> byUnit = new();
			HashSet<int> synthetic = places.Where(p => p.Synthetic).Select(p => p.Owner).ToHashSet();
			foreach (int unit in Units())
			{
				List<DropRateInfo> entries = LootSnapshot.ForNpc(unit);
				bool[] rolled = RolledEntries(entries, LootSnapshot.RulesOf(unit));
				// The synthetic slot is filled below, like every place
				Placed[] table = new Placed[entries.Count + (synthetic.Contains(unit) ? 1 : 0)];
				for (int i = 0; i < entries.Count; i++) table[i] = new Placed { Entry = entries[i], Rolled = rolled[i], FromNetId = unit, FromIndex = i };
				byUnit[unit] = table;
			}
			for (int p = 0; p < places.Count; p++)
			{
				Place place = places[p];
				Place source = places[contentOf[p]];
				DropRateInfo content = source.Entry;
				byUnit[place.Owner][place.Index] = new Placed
				{
					Entry = new DropRateInfo(content.itemId, content.stackMin, content.stackMax, content.dropRate, place.Entry.conditions),
					Rolled = true,
					Permuted = true,
					FromNetId = source.Owner,
					FromIndex = source.Index
				};
			}
			return new WorldPlan { Places = places, ContentOf = contentOf, Groups = groups, ByUnit = byUnit };
		}

		private static WorldPlan Empty() => new() { Places = new List<Place>(), ContentOf = Array.Empty<int>(), Groups = new List<List<int>>(), ByUnit = new Dictionary<int, Placed[]>() };

		// The plan of the current world (cleared by WorldCaches.Reset). Before the item pool (and the world) exist the
		// synthetic entries can't be rolled: an empty plan, not kept
		public static WorldPlan Current
		{
			get
			{
				if (cached != null) return cached;
				if (!ItemPool.Ready || !SeedSystem.WorldReady) return Empty();
				return cached = Build(SeedSystem.WorldSeed);
			}
		}

		private static readonly Placed[] None = Array.Empty<Placed>();

		public static Placed[] For(int netId) => Current.ByUnit.TryGetValue(UnitOf(netId), out Placed[] table) ? table : None;

		// Only rules whose report is complete, chains included, are wrapped (CompleteRules)
		public static bool Wrappable(IItemDropRule rule, int depth = 0)
		{
			if (rule == null || depth > 10 || !CompleteRules.Contains(rule.GetType())) return false;
			if (rule is DropBasedOnExpertMode expert && !(Wrappable(expert.ruleForNormalMode, depth + 1) && Wrappable(expert.ruleForExpertMode, depth + 1))) return false;
			if (rule is DropBasedOnMasterMode master && !(Wrappable(master.ruleForDefault, depth + 1) && Wrappable(master.ruleForMasterMode, depth + 1))) return false;
			if (rule.ChainedRules != null)
			{
				foreach (IItemDropRuleChainAttempt chain in rule.ChainedRules)
				{
					if (chain == null || !CompleteChains.Contains(chain.GetType()) || !Wrappable(chain.RuleToChain, depth + 1)) return false;
				}
			}
			return true;
		}
	}

	public class DropPermutationSystem : ModSystem
	{
		public override void Load() => WorldCaches.Reset += DropPermutation.Reset;

		public override void OnWorldLoad() => DropPermutation.Reset();

		public override void Unload()
		{
			WorldCaches.Reset -= DropPermutation.Reset;
			DropPermutation.Unload();
		}
	}

	// Wraps a vanilla rule of an ordinary enemy (registered in its place, never changing it: rule instances are shared
	// between net IDs). In the permutation style the rule is silent and RandomNpcDropRule rolls the net ID's permuted entries;
	// otherwise it resolves the original rule as if it were registered (ItemDropResolver.ResolveRule calls CanDrop, then
	// TryDroppingItem with the resolve action for a nested rule, then the registered rule's chains: none here, the inner
	// rule's own chains are resolved by the action). Runs where the loot rolls (server, single player).
	// Accepted: another mod's ModifyNPCLoot running after ours no longer finds the wrapped vanilla rules (to remove or
	// change them); its own rules are left alone
	public class PermutableRule : IItemDropRule, INestedItemDropRule
	{
		private readonly IItemDropRule inner;
		private readonly int[] items;

		public List<IItemDropRuleChainAttempt> ChainedRules { get; } = new();

		public IItemDropRule Inner => inner;

		public int NetId { get; }

		// Index of the wrapped rule in the net ID's registered rules (LootSnapshot.NpcRules)
		public int RuleIndex { get; }

		public IReadOnlyList<int> Items => items;

		public PermutableRule(IItemDropRule inner, int netId, int ruleIndex, IEnumerable<int> items)
		{
			this.inner = inner;
			NetId = netId;
			RuleIndex = ruleIndex;
			this.items = items.ToArray();
		}

		// Same test as DropPermutation.RolledEntries
		private bool Silenced => DropPermutation.Active(NetId) && DropPermutation.Silences(items);

		public bool CanDrop(DropAttemptInfo info) => true;

		// Vanilla nested rules do nothing when resolved without the action
		public ItemDropAttemptResult TryDroppingItem(DropAttemptInfo info) => new() { State = ItemDropAttemptResultState.DidNotRunCode };

		public ItemDropAttemptResult TryDroppingItem(DropAttemptInfo info, ItemDropRuleResolveAction resolveAction) =>
			Silenced ? new ItemDropAttemptResult { State = ItemDropAttemptResultState.DidNotRunCode } : resolveAction(inner, info);

		// The bestiary is built before any world exists: the original rule reports its drops
		public void ReportDroprates(List<DropRateInfo> drops, DropRateInfoChainFeed ratesInfo)
		{
			if (Silenced) return;
			inner.ReportDroprates(drops, ratesInfo.With(1f));
		}
	}
}

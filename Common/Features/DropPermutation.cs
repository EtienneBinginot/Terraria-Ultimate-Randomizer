using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
	// and every item of a permuted vanilla entry still drops somewhere. Bosses, their parts and minions, town NPCs and the
	// enemies CanHaveExtraDrops excludes are left out. Computed from the world seed, the synced settings and the snapshot
	// only: the server (kills) and the clients (journal, codex) get the same plan
	public static class DropPermutation
	{
		// Conditions that decide whether an entry can drop at all in a world (difficulty, special seeds, never): contents only
		// move between places with the same set of them, so an expert-only item stays expert-only. Other conditions
		// (hardmode, biome, event...) don't split the groups: they come with the place
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
			public int Owner;
			public int Index;
			// DetRandom.NpcKey of the owner: the stable order of the places
			public int OwnerKey;
			public int Tier;
			public string Access;
			public DropRateInfo Entry;
		}

		// An entry of a permuted enemy's final table. Entry: what drops (the content, with the place's conditions)
		public struct Placed
		{
			public DropRateInfo Entry;
			// Rolled by RandomNpcDropRule (its vanilla rule is silent: PermutableRule); false: a per-player rule, vanilla rolls it
			public bool Rolled;
			// Its content came through the permutation (false: kept, such as coins and progression items)
			public bool Permuted;
			// Where the content comes from (owner type, entry index)
			public int FromType;
			public int FromIndex;
		}

		public sealed class WorldPlan
		{
			public List<Place> Places;
			// ContentOf[p]: the index of the place whose content goes to place p
			public int[] ContentOf;
			public List<List<int>> Groups;
			public Dictionary<int, Placed[]> ByType;
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

		// Pure: ContentOf for these places and this seed (Fisher-Yates in each group)
		public static int[] Permute(IReadOnlyList<Place> places, int seed, out List<List<int>> groups)
		{
			int[] contentOf = Enumerable.Range(0, places.Count).ToArray();
			groups = Groups(places);
			foreach (List<int> group in groups)
			{
				Place first = places[group[0]];
				int[] permuted = group.ToArray();
				Random rng = new(DetRandom.Hash(seed, RollKey.DropPermutation, DetRandom.StableHash(first.Access ?? ""), Tiers.Clamp(group.Min(i => places[i].Tier))));
				for (int i = permuted.Length - 1; i > 0; i--)
				{
					int j = rng.Next(i + 1);
					(permuted[i], permuted[j]) = (permuted[j], permuted[i]);
				}
				for (int i = 0; i < group.Count; i++) contentOf[group[i]] = permuted[i];
			}
			return contentOf;
		}

		// Can this enemy type's drops be permuted (whatever the style)? Variants share their type's plan
		public static bool IsPermutable(int type)
		{
			if (type <= 0 || BossParts.IsPart(type) || BossParts.IsMinion(type) || NpcTiers.IsBoss(type)) return false;
			return RandomNpcDropRule.CanHaveExtraDrops(type, false, SourceOf(type).entries);
		}

		// The permutation applies to this type with these settings (style: the configured one when null)
		public static bool InStyle(int type, DropTableStyle? style = null)
		{
			LootConfig cfg = LootConfig.Instance;
			return cfg.RandomizeMobDrops && (style ?? cfg.DropTableStyle) == DropTableStyle.Permutation && IsPermutable(type);
		}

		// At kill time: the vanilla rules of this type are silent, RandomNpcDropRule rolls its plan
		public static bool Active(int type) => SeedSystem.WorldReady && ItemPool.Ready && InStyle(type);

		// The snapshot is captured once at load: so is the source of each type (read at every kill)
		private static readonly Dictionary<int, (List<DropRateInfo>, int[])> sources = new();

		// The snapshot of a type: its own, or the first of its net IDs
		public static (List<DropRateInfo> entries, int[] rules) SourceOf(int type)
		{
			if (sources.TryGetValue(type, out (List<DropRateInfo>, int[]) known)) return known;
			(List<DropRateInfo>, int[]) source = (LootSnapshot.ForNpc(type), LootSnapshot.RulesOf(type));
			if (!LootSnapshot.Npcs.ContainsKey(type))
			{
				foreach (int netId in LootSnapshot.Npcs.Keys.OrderBy(k => k))
				{
					if (ContentSamples.NpcsByNetId.TryGetValue(netId, out NPC sample) && sample.type == type)
					{
						source = (LootSnapshot.ForNpc(netId), LootSnapshot.RulesOf(netId));
						break;
					}
				}
			}
			sources[type] = source;
			return source;
		}

		public static void Unload()
		{
			cached = null;
			sources.Clear();
		}

		// Entries our rule rolls: those of a wrapped rule that holds at least one item that is not kept (a rule of kept items
		// only, coins or keys..., keeps running as it is: PermutableRule.Silenced)
		public static bool[] RolledEntries(List<DropRateInfo> entries, int[] rules)
		{
			bool[] rolled = new bool[entries.Count];
			HashSet<int> permutableRules = new();
			for (int i = 0; i < entries.Count && i < rules.Length; i++)
			{
				if (rules[i] >= 0 && !DropHelper.KeepOriginal(entries[i].itemId)) permutableRules.Add(rules[i]);
			}
			for (int i = 0; i < entries.Count; i++) rolled[i] = i < rules.Length && permutableRules.Contains(rules[i]);
			return rolled;
		}

		// Every permutable type with drops, in a stable order
		public static List<int> PermutableTypes()
		{
			SortedSet<int> types = new();
			foreach (int netId in LootSnapshot.Npcs.Keys)
			{
				if (ContentSamples.NpcsByNetId.TryGetValue(netId, out NPC sample) && IsPermutable(sample.type)) types.Add(sample.type);
			}
			return types.ToList();
		}

		// The places of this world: every rolled entry whose item is not kept, of every permutable type
		public static List<Place> CollectPlaces()
		{
			List<Place> places = new();
			foreach (int type in PermutableTypes())
			{
				(List<DropRateInfo> entries, int[] rules) = SourceOf(type);
				bool[] rolled = RolledEntries(entries, rules);
				int key = DetRandom.NpcKey(type);
				int tier = NpcTiers.BaseTier(type);
				for (int i = 0; i < entries.Count; i++)
				{
					if (!rolled[i] || DropHelper.KeepOriginal(entries[i].itemId)) continue;
					places.Add(new Place { Owner = type, Index = i, OwnerKey = key, Tier = tier, Access = AccessOf(entries[i].conditions), Entry = entries[i] });
				}
			}
			return places;
		}

		// The whole plan for a seed. The tier is the enemy's base tier, not the effective one: that one follows progression
		// (ScaleWeakEnemiesWithProgression) and the drops of a world in progress must not move
		public static WorldPlan Build(int seed)
		{
			List<Place> places = CollectPlaces();
			int[] contentOf = Permute(places, seed, out List<List<int>> groups);
			Dictionary<int, Placed[]> byType = new();
			foreach (int type in PermutableTypes())
			{
				(List<DropRateInfo> entries, int[] rules) = SourceOf(type);
				bool[] rolled = RolledEntries(entries, rules);
				Placed[] table = new Placed[entries.Count];
				for (int i = 0; i < entries.Count; i++) table[i] = new Placed { Entry = entries[i], Rolled = rolled[i], FromType = type, FromIndex = i };
				byType[type] = table;
			}
			for (int p = 0; p < places.Count; p++)
			{
				Place place = places[p];
				Place source = places[contentOf[p]];
				DropRateInfo content = source.Entry;
				byType[place.Owner][place.Index] = new Placed
				{
					Entry = new DropRateInfo(content.itemId, content.stackMin, content.stackMax, content.dropRate, place.Entry.conditions),
					Rolled = true,
					Permuted = true,
					FromType = source.Owner,
					FromIndex = source.Index
				};
			}
			return new WorldPlan { Places = places, ContentOf = contentOf, Groups = groups, ByType = byType };
		}

		// The plan of the current world (cleared by WorldCaches.Reset)
		public static WorldPlan Current => cached ??= Build(SeedSystem.WorldSeed);

		private static readonly Placed[] None = Array.Empty<Placed>();

		public static Placed[] For(int type) => Current.ByType.TryGetValue(type, out Placed[] table) ? table : None;

		// Rules left as they are: per-player drops (DropPerPlayerOnThePlayer reports its items without its condition, and
		// gives each player their own copy), anywhere in the rule's tree
		public static bool ContainsPerPlayer(IItemDropRule rule, int depth = 0)
		{
			if (rule == null || depth > 10) return false;
			if (rule is DropPerPlayerOnThePlayer) return true;
			if (rule.ChainedRules != null)
			{
				foreach (IItemDropRuleChainAttempt chain in rule.ChainedRules)
				{
					if (ContainsPerPlayer(chain?.RuleToChain, depth + 1)) return true;
				}
			}
			// Nested rules (DropBasedOnExpertMode, OneFromRulesRule, SequentialRulesRule...) keep theirs in fields
			for (Type t = rule.GetType(); t != null && t != typeof(object); t = t.BaseType)
			{
				foreach (FieldInfo field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
				{
					object value = field.GetValue(rule);
					if (value is IItemDropRule nested && nested != rule && ContainsPerPlayer(nested, depth + 1)) return true;
					if (value is IEnumerable<IItemDropRule> many && many.Any(r => r != rule && ContainsPerPlayer(r, depth + 1))) return true;
				}
			}
			return false;
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
	// between net IDs). In the permutation style the rule is silent and RandomNpcDropRule rolls the enemy's permuted entries;
	// otherwise it resolves the original rule as if it were registered (ItemDropResolver.ResolveRule calls CanDrop, then
	// TryDroppingItem with the resolve action for a nested rule, then the registered rule's chains: none here, the inner
	// rule's own chains are resolved by the action). Runs where the loot rolls (server, single player)
	public class PermutableRule : IItemDropRule, INestedItemDropRule
	{
		private readonly IItemDropRule inner;
		private readonly int type;
		private readonly int[] items;

		public List<IItemDropRuleChainAttempt> ChainedRules { get; } = new();

		public IItemDropRule Inner => inner;

		public PermutableRule(IItemDropRule inner, int type, IEnumerable<int> items)
		{
			this.inner = inner;
			this.type = type;
			this.items = items.ToArray();
		}

		// Same test as DropPermutation.RolledEntries: a rule of kept items only (coins, keys...) keeps running
		private bool Silenced => DropPermutation.Active(type) && items.Any(i => !DropHelper.KeepOriginal(i));

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

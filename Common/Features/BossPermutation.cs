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
	// DropTableStyle.Permutation for the bosses of the boss table (NpcTiers) and their treasure bags. The rolled entries of the
	// bosses' tables are places, as for the enemies (DropPermutation), but their contents only move between bosses of the
	// same boss group (their base tier; a tier with a single boss joins the one below, or the one above for the lowest) and the
	// same access class, on a random cycle. A boss bag carries the contents of its boss's not-expert places, each with its
	// chance and stack in the bag it came from (BagVersion); the rolled bag entries matching no not-expert drop of their boss
	// (expert accessories such as the Royal Gel) are places of their own (access "Bag"), moved between the bags of the group.
	// The bag item on the boss, coins, kept items and the rules that are not complete (per-player drops, one-from-rules...)
	// stay as they are, as do the entries with a seasonal condition (DropPermutation.KeptVanilla); the other boss-specific
	// conditions follow PlacedConditions. Groups: BossGroups. Computed from the world seed, the synced settings and the snapshots: the server (kills) and the
	// clients (bags are opened on the client, journal, codex) get the same plan
	public static class BossPermutation
	{
		// Index of a bag-only place: this offset + the entry index in the bag's snapshot
		public const int BagIndexOffset = 10000;

		// Boss groups smaller than this join the adjacent tier
		public const int MinGroupBosses = 2;

		public const string BagAccess = "Bag";

		public sealed class WorldPlan
		{
			public List<DropPermutation.Place> Places;
			// ContentOf[p]: the index of the place whose content goes to place p
			public int[] ContentOf;
			// Per place: its content in a bag (the matched entry of its boss's bag, else its own entry without conditions)
			public DropRateInfo[] BagVersion;
			// Groups of places (one boss group and one access class each), and the boss groups (units)
			public List<List<int>> Groups;
			public List<List<int>> BossGroups;
			public Dictionary<int, int> GroupOfUnit;
			// Per boss table (UnitOf) and per bag (BagUnit)
			public Dictionary<int, DropPermutation.Placed[]> ByUnit;
			public Dictionary<int, DropPermutation.Placed[]> ByBag;
			// Per bag: the places of its boss whose contents it carries, in the order of the bag's first entries
			public Dictionary<int, List<int>> Carried;
			// Per bag: its entries travelling with a not-expert drop of its boss (in the bag through Carried)
			public Dictionary<int, bool[]> Matched;
		}

		private static WorldPlan cached;

		public static void Reset() => cached = null;

		// Load-time facts (samples, snapshots): cached until unload
		private static readonly Dictionary<int, bool> sources = new();
		private static readonly Dictionary<int, int> units = new();
		private static Dictionary<int, int> bagOfUnit;
		private static Dictionary<int, int> unitOfBag;

		public static void Unload()
		{
			cached = null;
			sources.Clear();
			units.Clear();
			bagOfUnit = null;
			unitOfBag = null;
		}

		// Its loot is wrapped at load: a boss of the boss table, or one of its parts (Eater of Worlds segments), not a minion
		public static bool WrapsLoot(int type)
		{
			// Vanilla bosses only: a modded boss's rules are left alone
			if (type <= 0 || type >= NPCID.Count || BossParts.IsMinion(type)) return false;
			int owner = BossParts.Owner(type);
			return NpcTiers.HasBossTier(owner) && NpcTiers.IsBoss(owner);
		}

		// A net ID whose drops the boss permutation handles (whatever the style): WrapsLoot, with vanilla drops
		public static bool IsSource(int netId)
		{
			if (sources.TryGetValue(netId, out bool known)) return known;
			bool result = ContentSamples.NpcsByNetId.TryGetValue(netId, out NPC sample) && WrapsLoot(sample.type) && LootSnapshot.ForNpc(netId).Count > 0;
			sources[netId] = result;
			return result;
		}

		// The net ID whose table this one uses: its owner's (Eater of Worlds segments), Retinazer's for Spazmatism (the Twins
		// share their rule), when the snapshots are the same; else its own
		public static int UnitOf(int netId)
		{
			if (units.TryGetValue(netId, out int unit)) return unit;
			unit = netId;
			if (IsSource(netId))
			{
				int type = ContentSamples.NpcsByNetId[netId].type;
				foreach (int candidate in new[] { BossParts.Owner(type), type == NPCID.Spazmatism ? NPCID.Retinazer : type })
				{
					if (candidate != netId && IsSource(candidate) && DropPermutation.SameSnapshot(netId, candidate))
					{
						unit = candidate;
						break;
					}
				}
			}
			units[netId] = unit;
			return unit;
		}

		// Spazmatism's table when its trophy makes it differ from Retinazer's: the entries of the rule the Twins share (those
		// that need the other twin dead) are Retinazer's places, mirrored, so that their contents count once
		public static int MirrorOf(int unit) =>
			ContentSamples.NpcsByNetId.TryGetValue(unit, out NPC sample) && sample.type == NPCID.Spazmatism && UnitOf(unit) == unit
			&& IsSource(NPCID.Retinazer) && UnitOf(NPCID.Retinazer) == NPCID.Retinazer ? NPCID.Retinazer : 0;

		// Entry i of the unit is the same as the mirror's (item, stack, chance, conditions) and needs the other twin dead
		public static bool Mirrors(int unit, int mirror, int i)
		{
			List<DropRateInfo> own = LootSnapshot.ForNpc(unit), other = LootSnapshot.ForNpc(mirror);
			if (i >= own.Count || i >= other.Count) return false;
			DropRateInfo a = own[i], b = other[i];
			if (a.itemId != b.itemId || a.stackMin != b.stackMin || a.stackMax != b.stackMax || a.dropRate != b.dropRate) return false;
			if (a.conditions == null || !a.conditions.Any(c => c is Conditions.MissingTwin)) return false;
			IEnumerable<string> ca = a.conditions.Select(c => c?.GetType().FullName ?? "");
			IEnumerable<string> cb = (b.conditions ?? new List<IItemDropRuleCondition>()).Select(c => c?.GetType().FullName ?? "");
			return ca.SequenceEqual(cb);
		}

		// The boss tier of a table
		public static int TierOf(int unit) =>
			NpcTiers.BaseTier(BossParts.Owner(ContentSamples.NpcsByNetId.TryGetValue(unit, out NPC sample) ? sample.type : unit));

		// The tier a table is grouped by: its boss tier, except Queen Bee and Deerclops, grouped with Skeletron (3) although
		// their tier is the evil bosses' (NpcTiers: 2). Otherwise Skeletron and the Wall of Flesh made a group of two (the
		// Dark Mage and the Flying Dutchman are no NPC.boss), and their loot only swapped with each other
		public static int GroupTierOf(int unit)
		{
			int owner = BossParts.Owner(ContentSamples.NpcsByNetId.TryGetValue(unit, out NPC sample) ? sample.type : unit);
			return owner is NPCID.QueenBee or NPCID.Deerclops ? Math.Max(TierOf(unit), (int)Core.Tier.PostSkeletron) : TierOf(unit);
		}

		// Every boss table of the world, in a stable order
		public static List<int> Units()
		{
			SortedSet<int> result = new();
			foreach (int netId in ContentSamples.NpcsByNetId.Keys)
			{
				if (IsSource(netId)) result.Add(UnitOf(netId));
			}
			return result.ToList();
		}

		private static void EnsureBags()
		{
			if (bagOfUnit != null) return;
			Dictionary<int, int> bags = new(), owners = new();
			foreach (int unit in Units())
			{
				int bag = LootSnapshot.ForNpc(unit).Select(d => d.itemId).FirstOrDefault(i => i > ItemID.None && i < ItemID.Sets.BossBag.Length && ItemID.Sets.BossBag[i] && LootSnapshot.Items.ContainsKey(i));
				if (bag <= ItemID.None) continue;
				bags[unit] = bag;
				// Units are in order: a bag dropped by several tables belongs to the first
				owners.TryAdd(bag, unit);
			}
			unitOfBag = owners;
			bagOfUnit = bags;
		}

		// The treasure bag a boss table drops (0: none)
		public static int BagOf(int unit)
		{
			EnsureBags();
			return bagOfUnit.TryGetValue(unit, out int bag) ? bag : 0;
		}

		// The boss table a bag carries the contents of (0: not a permuted bag)
		public static int BagUnit(int bag)
		{
			EnsureBags();
			return unitOfBag.TryGetValue(bag, out int unit) ? unit : 0;
		}

		// The boss type whose random extra drops a boss type rolls in the permutation style: the one of its bag's table
		// (BagUnit), so that a kill and its bag give the same extras (the Twins: Retinazer's, whichever twin dies last)
		public static int ExtrasOwner(int type)
		{
			if (!IsSource(type)) return type;
			int bag = BagOf(UnitOf(type));
			int unit = bag > ItemID.None ? BagUnit(bag) : 0;
			return unit > 0 && ContentSamples.NpcsByNetId.TryGetValue(unit, out NPC sample) ? BossParts.Owner(sample.type) : type;
		}

		public static IEnumerable<int> Bags()
		{
			EnsureBags();
			return unitOfBag.Keys.OrderBy(b => b);
		}

		// A not-expert place: in expert mode, its content comes in the boss's bag
		public static bool CarriedByBag(string access) =>
			!string.IsNullOrEmpty(access) && access.Split('+').Any(t => t.EndsWith("NotExpert", StringComparison.Ordinal));

		// The access class of a boss entry: DropPermutation's, the Eater of Worlds' legacy "is a boss and (not) expert" read as
		// (not) expert (the boss part stays with the place's own conditions)
		public static string AccessOf(List<IItemDropRuleCondition> conditions)
		{
			string access = DropPermutation.AccessOf(conditions);
			if (access == "") return access;
			return string.Join("+", access.Split('+').Select(t => t switch
			{
				nameof(Conditions.LegacyHack_IsBossAndNotExpert) => nameof(Conditions.NotExpert),
				nameof(Conditions.LegacyHack_IsBossAndExpert) => nameof(Conditions.IsExpert),
				_ => t
			}).Distinct().OrderBy(t => t, StringComparer.Ordinal));
		}

		public static string BagAccessOf(List<IItemDropRuleCondition> conditions)
		{
			string access = AccessOf(conditions);
			return access == "" ? BagAccess : BagAccess + "+" + access;
		}

		public static bool IsBagOnly(DropPermutation.Place place) => place.Index >= BagIndexOffset;

		private static DropRateInfo WithConditions(DropRateInfo entry, List<IItemDropRuleCondition> conditions) =>
			new(entry.itemId, entry.stackMin, entry.stackMax, entry.dropRate, conditions);

		// Pure: the conditions of a boss place holding a moved content (DropPermutation.ConditionClass). The place keeps its own
		// conditions but those about its vanilla item (ItemSelf) and the one-time ones (OneTime): a content arriving at the
		// first-Plantera-kill place drops at every Plantera kill. The content brings its ItemSelf conditions (the Demon Heart
		// stays "not used yet" wherever it goes). A Fight condition stays on the place (what arrives at Terraprisma's place
		// needs a genuinely enraged Empress). unmoved: the content is the place's own, its conditions stay as they are
		public static List<IItemDropRuleCondition> PlacedConditions(List<IItemDropRuleCondition> place, List<IItemDropRuleCondition> content, bool unmoved)
		{
			if (unmoved) return place;
			List<IItemDropRuleCondition> result = new();
			if (place != null) result.AddRange(place.Where(c => DropPermutation.ClassOf(c) is not (DropPermutation.ConditionClass.ItemSelf or DropPermutation.ConditionClass.OneTime)));
			if (content != null) result.AddRange(content.Where(c => DropPermutation.ClassOf(c) == DropPermutation.ConditionClass.ItemSelf));
			return result.Count == 0 && place == null ? null : result;
		}

		// A content freed from a Fight or OneTime condition (Terraprisma, the first Plantera kill's item) drops at most at a
		// vanilla-like boss rate for its item at the place's tier: Golem does not give Terraprisma at every kill. Deterministic
		// (seed, place)
		public static bool FreedRate(DropPermutation.Place source, DropPermutation.Place place, int seed, out float cap)
		{
			cap = 1f;
			List<IItemDropRuleCondition> conditions = source.Entry.conditions;
			if (!DropPermutation.Has(conditions, DropPermutation.ConditionClass.Fight) && !DropPermutation.Has(conditions, DropPermutation.ConditionClass.OneTime)) return false;
			int item = source.Entry.itemId;
			Random rng = new(DetRandom.Hash(seed, RollKey.BossPermutation, place.OwnerKey, place.Index, 1));
			cap = 1f / Math.Max(1, DropRates.Denominator(item, ItemPool.TierOf(item), place.Tier, true, rng));
			return true;
		}

		private sealed class Collected
		{
			public List<DropPermutation.Place> Places = new();
			public List<DropRateInfo> BagVersion = new();
			// Per bag: its entries matched with a not-expert drop of its boss (they travel with it)
			public Dictionary<int, bool[]> Matched = new();
		}

		private static Collected Collect()
		{
			Collected c = new();
			foreach (int unit in Units())
			{
				List<DropRateInfo> entries = LootSnapshot.ForNpc(unit);
				bool[] rolled = DropPermutation.RolledEntries(entries, LootSnapshot.RulesOf(unit));
				int key = DetRandom.NpcKey(unit);
				int tier = TierOf(unit);
				List<int> carried = new();
				int mirror = MirrorOf(unit);
				for (int i = 0; i < entries.Count; i++)
				{
					if (!rolled[i] || DropHelper.KeepOriginal(entries[i].itemId) || mirror > 0 && Mirrors(unit, mirror, i)) continue;
					string access = AccessOf(entries[i].conditions);
					if (CarriedByBag(access)) carried.Add(c.Places.Count);
					c.Places.Add(new DropPermutation.Place { Owner = unit, Index = i, OwnerKey = key, Tier = tier, Access = access, Entry = entries[i] });
					c.BagVersion.Add(WithConditions(entries[i], null));
				}
				int bag = BagOf(unit);
				if (bag <= ItemID.None || BagUnit(bag) != unit) continue;
				List<DropRateInfo> bagEntries = LootSnapshot.ForItem(bag);
				bool[] bagRolled = DropPermutation.RolledEntries(bagEntries, LootSnapshot.RulesOfItem(bag));
				bool[] matched = new bool[bagEntries.Count];
				foreach (int p in carried)
				{
					for (int j = 0; j < bagEntries.Count; j++)
					{
						if (matched[j] || !bagRolled[j] || bagEntries[j].itemId != c.Places[p].Entry.itemId) continue;
						matched[j] = true;
						c.BagVersion[p] = bagEntries[j];
						break;
					}
				}
				c.Matched[bag] = matched;
				for (int j = 0; j < bagEntries.Count; j++)
				{
					if (matched[j] || !bagRolled[j] || DropHelper.KeepOriginal(bagEntries[j].itemId)) continue;
					c.Places.Add(new DropPermutation.Place { Owner = unit, Index = BagIndexOffset + j, OwnerKey = key, Tier = tier, Access = BagAccessOf(bagEntries[j].conditions), Entry = bagEntries[j] });
					c.BagVersion.Add(bagEntries[j]);
				}
			}
			return c;
		}

		// Pure: the boss groups of these tables (unit, tier), lowest tier first. Consecutive tiers with too few tables first
		// merge together (the Lunatic Cultist and the Moon Lord make a group); a group still smaller than MinGroupBosses then
		// joins the group below it, the lowest one the group above. The highest bosses' loot only goes down when nothing
		// else is possible
		public static List<List<int>> BossGroups(IEnumerable<(int unit, int tier)> tables)
		{
			List<List<int>> groups = new();
			bool openSmall = false;
			foreach (IGrouping<int, (int unit, int tier)> bucket in tables.Distinct().OrderBy(t => t.tier).ThenBy(t => t.unit).GroupBy(t => t.tier))
			{
				List<int> members = bucket.Select(t => t.unit).ToList();
				bool small = members.Count < MinGroupBosses;
				// A small tier joins the run of small tiers before it while that run is still too small
				if (small && openSmall && groups[^1].Count < MinGroupBosses) groups[^1].AddRange(members);
				else groups.Add(members);
				openSmall = small;
			}
			for (int g = 0; g < groups.Count && groups.Count > 1; g++)
			{
				if (groups[g].Count >= MinGroupBosses) continue;
				if (g > 0) groups[g - 1].AddRange(groups[g]);
				else groups[1].InsertRange(0, groups[0]);
				groups.RemoveAt(g);
				g--;
			}
			return groups;
		}

		// The rank of a place in its group: from the seed and its identity only (stable, as DropPermutation.Rank)
		public static int Rank(int seed, DropPermutation.Place place) => DetRandom.Hash(seed, RollKey.BossPermutation, place.OwnerKey, place.Index);

		// Pure: ContentOf for these places. Groups: one boss group and one access class each; in each, the places sorted by rank
		// take the content of the next one (a random cycle)
		public static int[] Permute(IReadOnlyList<DropPermutation.Place> places, int seed, Dictionary<int, int> groupOfUnit, out List<List<int>> groups)
		{
			int[] contentOf = Enumerable.Range(0, places.Count).ToArray();
			groups = Enumerable.Range(0, places.Count)
				.GroupBy(i => (groupOfUnit.TryGetValue(places[i].Owner, out int g) ? g : -1, places[i].Access ?? ""))
				.OrderBy(g => g.Key.Item1).ThenBy(g => g.Key.Item2, StringComparer.Ordinal)
				.Select(g => g.ToList()).ToList();
			foreach (List<int> group in groups)
			{
				List<int> order = group.OrderBy(i => Rank(seed, places[i])).ThenBy(i => places[i].OwnerKey).ThenBy(i => places[i].Owner).ThenBy(i => places[i].Index).ToList();
				for (int k = 0; k < order.Count; k++) contentOf[order[k]] = order[(k + 1) % order.Count];
			}
			return contentOf;
		}

		public static WorldPlan Build(int seed)
		{
			Collected c = Collect();
			List<DropPermutation.Place> places = c.Places;
			List<List<int>> bossGroups = BossGroups(places.Select(p => (p.Owner, GroupTierOf(p.Owner))));
			Dictionary<int, int> groupOfUnit = new();
			for (int g = 0; g < bossGroups.Count; g++)
			{
				foreach (int unit in bossGroups[g]) groupOfUnit[unit] = g;
			}
			int[] contentOf = Permute(places, seed, groupOfUnit, out List<List<int>> groups);
			DropRateInfo[] bagVersion = c.BagVersion.ToArray();
			DropPermutation.Placed Moved(int p, List<IItemDropRuleCondition> conditions, bool bag)
			{
				int s = contentOf[p];
				DropRateInfo content = bag ? bagVersion[s] : places[s].Entry;
				if (!bag) conditions = PlacedConditions(places[p].Entry.conditions, places[s].Entry.conditions, s == p);
				float rate = content.dropRate;
				if ((s != p || bag) && FreedRate(places[s], places[p], seed, out float cap)) rate = Math.Min(rate, cap);
				return new DropPermutation.Placed
				{
					Entry = new DropRateInfo(content.itemId, content.stackMin, content.stackMax, rate, conditions), Rolled = true, Permuted = true,
					FromNetId = places[s].Owner, FromIndex = places[s].Index
				};
			}

			Dictionary<int, DropPermutation.Placed[]> byUnit = new();
			foreach (int unit in Units())
			{
				List<DropRateInfo> entries = LootSnapshot.ForNpc(unit);
				bool[] rolled = DropPermutation.RolledEntries(entries, LootSnapshot.RulesOf(unit));
				DropPermutation.Placed[] table = new DropPermutation.Placed[entries.Count];
				for (int i = 0; i < entries.Count; i++) table[i] = new DropPermutation.Placed { Entry = entries[i], Rolled = rolled[i], FromNetId = unit, FromIndex = i };
				byUnit[unit] = table;
			}
			for (int p = 0; p < places.Count; p++)
			{
				DropPermutation.Place place = places[p];
				if (!IsBagOnly(place)) byUnit[place.Owner][place.Index] = Moved(p, null, false);
			}
			// The Twins' shared drops: the same content on both, the last one killed drops it (MissingTwin)
			foreach (int unit in byUnit.Keys.ToList())
			{
				int mirror = MirrorOf(unit);
				if (mirror <= 0 || !byUnit.TryGetValue(mirror, out DropPermutation.Placed[] mirrored)) continue;
				DropPermutation.Placed[] table = byUnit[unit];
				for (int i = 0; i < table.Length; i++)
				{
					if (table[i].Rolled && !DropHelper.KeepOriginal(table[i].Entry.itemId) && Mirrors(unit, mirror, i)) table[i] = mirrored[i];
				}
			}

			Dictionary<int, DropPermutation.Placed[]> byBag = new();
			Dictionary<int, List<int>> carriedOf = new();
			foreach (int bag in Bags())
			{
				int unit = BagUnit(bag);
				List<DropPermutation.Placed> list = new();
				List<int> carried = new();
				// The bag's own conditions (a world's evil, a seed...) come with the matched entries; a content without one
				// has none (its boss's not-expert conditions would never pass in a bag)
				for (int p = 0; p < places.Count; p++)
				{
					if (places[p].Owner != unit || IsBagOnly(places[p]) || !CarriedByBag(places[p].Access)) continue;
					list.Add(Moved(p, bagVersion[contentOf[p]].conditions, true));
					carried.Add(p);
				}
				List<DropRateInfo> bagEntries = LootSnapshot.ForItem(bag);
				bool[] bagRolled = DropPermutation.RolledEntries(bagEntries, LootSnapshot.RulesOfItem(bag));
				bool[] matched = c.Matched.TryGetValue(bag, out bool[] m) ? m : new bool[bagEntries.Count];
				Dictionary<int, int> ownPlaces = new();
				for (int p = 0; p < places.Count; p++)
				{
					if (places[p].Owner == unit && IsBagOnly(places[p])) ownPlaces[places[p].Index - BagIndexOffset] = p;
				}
				for (int j = 0; j < bagEntries.Count; j++)
				{
					if (matched[j]) continue;
					// A bag-only content keeps its own bag conditions (the Demon Heart not used yet...): the place imposes none
					if (ownPlaces.TryGetValue(j, out int q)) list.Add(Moved(q, bagVersion[contentOf[q]].conditions, true));
					else list.Add(new DropPermutation.Placed { Entry = bagEntries[j], Rolled = bagRolled[j], FromNetId = unit, FromIndex = BagIndexOffset + j });
				}
				byBag[bag] = list.ToArray();
				carriedOf[bag] = carried;
			}
			return new WorldPlan
			{
				Places = places, ContentOf = contentOf, BagVersion = bagVersion, Groups = groups, BossGroups = bossGroups, GroupOfUnit = groupOfUnit,
				ByUnit = byUnit, ByBag = byBag, Carried = carriedOf, Matched = c.Matched
			};
		}

		private static WorldPlan Empty() => new()
		{
			Places = new List<DropPermutation.Place>(), ContentOf = Array.Empty<int>(), BagVersion = Array.Empty<DropRateInfo>(), Groups = new List<List<int>>(),
			BossGroups = new List<List<int>>(), GroupOfUnit = new Dictionary<int, int>(), ByUnit = new Dictionary<int, DropPermutation.Placed[]>(),
			ByBag = new Dictionary<int, DropPermutation.Placed[]>(), Carried = new Dictionary<int, List<int>>(), Matched = new Dictionary<int, bool[]>()
		};

		// The plan of the current world (cleared by WorldCaches.Reset)
		public static WorldPlan Current
		{
			get
			{
				if (cached != null) return cached;
				if (!ItemPool.Ready || !SeedSystem.WorldReady) return Empty();
				return cached = Build(SeedSystem.WorldSeed);
			}
		}

		private static readonly DropPermutation.Placed[] None = Array.Empty<DropPermutation.Placed>();

		public static DropPermutation.Placed[] For(int netId) => Current.ByUnit.TryGetValue(UnitOf(netId), out DropPermutation.Placed[] table) ? table : None;

		public static DropPermutation.Placed[] ForBag(int bag) => Current.ByBag.TryGetValue(bag, out DropPermutation.Placed[] table) ? table : None;

		// The boss permutation applies to this net ID with these settings (style: the configured one when null)
		public static bool InStyle(int netId, DropTableStyle? style = null)
		{
			LootConfig cfg = LootConfig.Instance;
			return cfg.RandomizeMobDrops && (style ?? cfg.DropTableStyle) == DropTableStyle.Permutation && IsSource(netId);
		}

		// At kill time: the wrapped vanilla rules of this boss are silent, RandomNpcDropRule rolls its plan
		public static bool Active(int netId) => SeedSystem.WorldReady && ItemPool.Ready && InStyle(netId);

		// The bag's contents follow the permutation (randomized bags, permutation style): its wrapped rules are silent and
		// BossBagRule rolls its plan
		public static bool BagInStyle(int bag, DropTableStyle? style = null)
		{
			LootConfig cfg = LootConfig.Instance;
			return cfg.RandomizeMobDrops && cfg.RandomizeBossBags && (style ?? cfg.DropTableStyle) == DropTableStyle.Permutation
				&& bag > ItemID.None && bag < ItemID.Sets.BossBag.Length && ItemID.Sets.BossBag[bag] && BagUnit(bag) > 0;
		}

		public static bool BagActive(int bag) => SeedSystem.WorldReady && ItemPool.Ready && BagInStyle(bag);

		// Load time: the rule registered in place of a boss's rule. A complete rule without entries kept vanilla
		// (DropPermutation.KeptVanilla) is wrapped whole (PermutableRule); a
		// leading condition rule that is not (the Twins' MissingTwin rule also holds their bag) is copied with its complete
		// chained rules wrapped, the others (the bag, per-player drops) kept as they are. The original instances (shared
		// between net IDs) are never changed. next: the wrapper index, counted per net ID
		public static IItemDropRule Wrap(IItemDropRule rule, int netId, ref int next, int depth = 0)
		{
			if (rule == null || depth > 10) return rule;
			if (DropPermutation.Wrappable(rule, forBosses: true))
			{
				List<DropRateInfo> part = LootSnapshot.Capture(new List<IItemDropRule> { rule });
				if (part.Count == 0) return rule;
				// An entry kept vanilla (seasonal...) keeps its rule running: its chains are looked at below
				if (!part.Any(d => DropPermutation.KeptVanilla(d.conditions))) return new PermutableRule(rule, netId, next++, part.Select(d => d.itemId));
			}
			if (rule.GetType() != typeof(LeadingConditionRule) || rule.ChainedRules == null || rule.ChainedRules.Count == 0
				|| rule.ChainedRules.Any(c => !DropPermutation.IsCompleteChain(c))
				|| DropPermutation.ClassOf(((LeadingConditionRule)rule).condition) == DropPermutation.ConditionClass.Seasonal) return rule;
			LeadingConditionRule copy = new(((LeadingConditionRule)rule).condition);
			bool changed = false;
			foreach (IItemDropRuleChainAttempt chain in rule.ChainedRules)
			{
				// A chain hidden from the report keeps its rule: its items are in no table
				IItemDropRule child = DropPermutation.HidesReport(chain) ? chain.RuleToChain : Wrap(chain.RuleToChain, netId, ref next, depth + 1);
				changed |= child != chain.RuleToChain;
				copy.ChainedRules.Add(chain switch
				{
					Chains.TryIfSucceeded s => new Chains.TryIfSucceeded(child, s.hideLootReport),
					Chains.TryIfFailedRandomRoll f => new Chains.TryIfFailedRandomRoll(child, f.hideLootReport),
					_ => new Chains.TryIfDoesntFillConditions(child, ((Chains.TryIfDoesntFillConditions)chain).hideLootReport)
				});
			}
			return changed ? copy : rule;
		}

		// Load time: for each entry of the original rule's capture, the index of the wrapper of the copy reporting it (-1:
		// none, the entry's rule keeps running). Null when the copy does not report the same entries (it is then not used)
		public static int[] Track(IItemDropRule wrapped, List<DropRateInfo> original)
		{
			List<DropRateInfo> capture;
			Dictionary<int, int> tracked = new();
			PermutableRule.Tracked = tracked;
			try
			{
				capture = LootSnapshot.Capture(new List<IItemDropRule> { wrapped });
			}
			finally
			{
				PermutableRule.Tracked = null;
			}
			if (capture.Count != original.Count) return null;
			int[] owners = new int[capture.Count];
			for (int i = 0; i < capture.Count; i++)
			{
				if (capture[i].itemId != original[i].itemId || capture[i].dropRate != original[i].dropRate) return null;
				owners[i] = tracked.TryGetValue(i, out int r) ? r : -1;
			}
			return owners;
		}

		// All the wrappers in a registered rule (nested in leading condition rules), for the tests
		public static IEnumerable<PermutableRule> Wrappers(IItemDropRule rule, int depth = 0)
		{
			if (rule == null || depth > 10) yield break;
			if (rule is PermutableRule wrapper)
			{
				yield return wrapper;
				yield break;
			}
			if (rule.ChainedRules == null) yield break;
			foreach (IItemDropRuleChainAttempt chain in rule.ChainedRules)
			{
				foreach (PermutableRule nested in Wrappers(chain?.RuleToChain, depth + 1)) yield return nested;
			}
		}
	}

	public class BossPermutationSystem : ModSystem
	{
		public override void Load() => WorldCaches.Reset += BossPermutation.Reset;

		public override void OnWorldLoad() => BossPermutation.Reset();

		public override void Unload()
		{
			WorldCaches.Reset -= BossPermutation.Reset;
			BossPermutation.Unload();
		}
	}

	// Added to every boss bag: when the bag's permutation is active (its wrapped vanilla rules are silent), rolls the bag's plan
	// (BagPlan.Plan: the permuted contents, the kept entries of the silenced rules, the boss's random extra drops), each with
	// its own chance. Bags are opened where the player opens them (Player.OpenBossBag, on that client in multiplayer); the
	// items come with the bag's EntitySource_ItemOpen, as vanilla's (CommonCode.DropItem), and LootSubstitutionItem leaves
	// them alone
	public class BossBagRule : IItemDropRule
	{
		private readonly int bagType;

		public List<IItemDropRuleChainAttempt> ChainedRules { get; } = new();

		public BossBagRule(int bagType)
		{
			this.bagType = bagType;
		}

		public bool CanDrop(DropAttemptInfo info) => true;

		public ItemDropAttemptResult TryDroppingItem(DropAttemptInfo info)
		{
			if (info.player == null || !BossPermutation.BagActive(bagType)) return new ItemDropAttemptResult { State = ItemDropAttemptResultState.DidNotRunCode };
			// As a boss kill (RandomNpcDropRule.DropRolledOriginals, KillDrops): a locked entry only drops on a lucky opening
			// (a boss's worth), kept as rarely as a lucky roll that far above the limit, marked as a jackpot when useful. No
			// madness tail: the extras are the planned ones, like the bag's other contents
			bool lucky = DropHelper.RollLucky(1f);
			foreach (PlannedDrop drop in BagPlan.Plan(bagType, includeLocked: true))
			{
				if (!drop.Rolled && !drop.Extra || drop.Item <= ItemID.None) continue;
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
				JackpotItem.Next = gap;
				try
				{
					CommonCode.DropItem(info, drop.Item, stack, false);
				}
				finally
				{
					JackpotItem.Next = 0;
				}
			}
			return new ItemDropAttemptResult { State = ItemDropAttemptResultState.Success };
		}

		// Recipe browsers and other readers: the vanilla rules report the original contents, or in a world showing its own
		// drops (WorldDropsReport), this rule the bag's plan (the silenced rules report nothing)
		public void ReportDroprates(List<DropRateInfo> drops, DropRateInfoChainFeed ratesInfo)
		{
			if (WorldDropsReport.ShowsBag(bagType)) WorldDropsReport.Report(WorldDropsReport.BagPlanned(bagType), drops, ratesInfo);
		}
	}
}

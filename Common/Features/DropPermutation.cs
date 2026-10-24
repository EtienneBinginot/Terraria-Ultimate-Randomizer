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
	// DropTableStyle.Permutation: the original drop entries of the ordinary enemies, vanilla and modded (their tier: NpcTiers'
	// estimate), are shuffled between them. Each entry of
	// an enemy's flattened drop table (LootSnapshot) is a place; its content (item, stack range, own chance) moves to another
	// place of the same tier and access class (a hard gate never to a place below its true tier: GateBands), and takes that
	// place's conditions. Every enemy keeps its number of entries,
	// and every item of a permuted vanilla entry still drops somewhere. An eligible enemy without any vanilla entry gets one
	// synthetic place (a random item of its level), permuted like the others. Vanilla repeats common items on every
	// variant (each zombie its Shackle, each slime its Gel and Slime Staff): an item keeps at most a few permuted copies
	// (PermutationDuplicateCopies, PermutationMaterialCopies for materials; not ores, bars, blocks nor critters) per access
	// class, its most reachable ones, and every other copy becomes a random item. A share of the kept contents (PermutationSurpriseChance)
	// becomes one too, never a material nor the keeper copy of an item (SurpriseCandidates). Bosses and their parts
	// (BossPermutation has their own), minions, town NPCs, critters and harmless NPCs are left out. Tables are per net ID
	// (variants such as Pinky have rules of their own); a variant whose snapshot is the same as its type's shares the type's table. Computed from the world seed, the synced
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

		// Conditions on the season, the weather, the player or one NPC's own state (a named NPC, special AI values, a pirate
		// map...): an access class of their own, like the restrictive ones (AccessOf). A Halloween content only moves between
		// Halloween places and takes the place's condition: a content moved to an ordinary place, or an ordinary one moved
		// there, would drop at the wrong moment or never
		private static readonly HashSet<Type> Seasonal = new()
		{
			typeof(Conditions.IsChristmas), typeof(Conditions.HalloweenWeapons), typeof(Conditions.HalloweenGoodieBagDrop), typeof(Conditions.XmasPresentDrop),
			typeof(Conditions.WindyEnoughForKiteDrops), typeof(Conditions.PlayerNeedsHealing), typeof(Conditions.NamedNPC),
			typeof(Conditions.IsUsingSpecificAIValues), typeof(Conditions.LivingFlames), typeof(Conditions.PirateMap)
		};

		// The kinds of drop conditions. Restrictive and Seasonal: contents only move within their access class (AccessOf), the
		// conditions staying on the place. For bosses (BossPermutation.PlacedConditions): ItemSelf conditions are about the item itself
		// (the Demon Heart not used yet, no Portal Gun yet) and travel with it; Fight conditions (a genuinely enraged Empress)
		// stay on their place, the content leaving it loses them; OneTime conditions (the first Plantera kill) are dropped on
		// both sides. Enemies keep every other condition on the place
		public enum ConditionClass
		{
			Place,
			Restrictive,
			Seasonal,
			ItemSelf,
			Fight,
			OneTime
		}

		public static ConditionClass ClassOf(IItemDropRuleCondition condition) => condition switch
		{
			null => ConditionClass.Place,
			_ when Restrictive.Contains(condition.GetType()) => ConditionClass.Restrictive,
			_ when Seasonal.Contains(condition.GetType()) => ConditionClass.Seasonal,
			Conditions.NotUsedDemonHeart or Conditions.NoPortalGun => ConditionClass.ItemSelf,
			Conditions.EmpressOfLightIsGenuinelyEnraged => ConditionClass.Fight,
			Conditions.FirstTimeKillingPlantera => ConditionClass.OneTime,
			// Another mod's condition: nothing says what it reads (a modded biome, event or boss beaten), so its entries only
			// travel between places with the same condition (AccessOf: by its full type name)
			_ when IsModdedCondition(condition) => ConditionClass.Restrictive,
			_ => ConditionClass.Place
		};

		public static bool IsModdedCondition(IItemDropRuleCondition condition) => condition != null && condition.GetType().Assembly != typeof(Conditions).Assembly;

		public static bool Has(List<IItemDropRuleCondition> conditions, ConditionClass kind) => conditions != null && conditions.Any(c => c != null && ClassOf(c) == kind);

		// An entry with a seasonal condition: permuted among the places of its own seasonal class only (AccessOf)
		public static bool IsSeasonal(List<IItemDropRuleCondition> conditions) => Has(conditions, ConditionClass.Seasonal);

		// Load time: a rule whose report is complete (Wrappable: only the vanilla rule types, so another mod's own rule classes
		// keep running as they are), of any NPC or bag, vanilla or modded. Seasonal entries are wrapped too: they only move
		// between places of their class. part: the rule's capture
		public static bool Permutable(IItemDropRule rule, List<DropRateInfo> part, int ownerType, bool forBosses = false) =>
			ownerType > 0 && part.Count > 0 && Wrappable(rule, forBosses: forBosses);

		// The vanilla rule types whose ReportDroprates lists everything they can drop, with its chance and conditions: only
		// these are wrapped (and their entries permuted). Exact types: a subclass (modded) may drop differently. Anything else
		// (modded rules, slime body items, per-player drops, OneFromRulesRule...) keeps running as it is
		private static readonly HashSet<Type> CompleteRules = new()
		{
			typeof(CommonDrop), typeof(CommonDropNotScalingWithLuck), typeof(CommonDropWithRerolls), typeof(ItemDropWithConditionRule),
			typeof(OneFromOptionsDropRule), typeof(OneFromOptionsNotScaledWithLuckDropRule),
			typeof(DropBasedOnExpertMode), typeof(DropBasedOnMasterMode), typeof(LeadingConditionRule),
			// Reports nothing and drops nothing: a complete (empty) branch, as Queen Bee's expert one
			typeof(DropNothing)
		};

		// Bosses and their bags (BossPermutation) also wrap these: their report lists every option with its own chance
		private static readonly HashSet<Type> CompleteForBosses = new()
		{
			typeof(FewFromOptionsDropRule), typeof(FewFromOptionsNotScaledWithLuckDropRule), typeof(FromOptionsWithoutRepeatsDropRule)
		};

		private static readonly HashSet<Type> CompleteChains = new()
		{
			typeof(Chains.TryIfSucceeded), typeof(Chains.TryIfFailedRandomRoll), typeof(Chains.TryIfDoesntFillConditions)
		};

		// Groups smaller than this are merged with the next tier of the same access class (like ItemPool's shuffle)
		public const int MinGroupSize = 3;

		// The access class of an entry: its restrictive and seasonal condition types, sorted ("" for an ordinary entry)
		public static string AccessOf(List<IItemDropRuleCondition> conditions)
		{
			if (conditions == null || conditions.Count == 0) return "";
			return string.Join("+", conditions.Where(c => ClassOf(c) is ConditionClass.Restrictive or ConditionClass.Seasonal).Select(c => IsModdedCondition(c) ? c.GetType().FullName : c.GetType().Name).Distinct().OrderBy(n => n, StringComparer.Ordinal));
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
			// PermutationSurpriseChance: the content's item was replaced by a random one (Replaced), with the content's chance
			// and the place's conditions
			public bool Surprise;
			public int Replaced;
		}

		public sealed class WorldPlan
		{
			public List<Place> Places;
			// ContentOf[p]: the index of the place whose content goes to place p
			public int[] ContentOf;
			public List<List<int>> Groups;
			// Per table net ID (UnitOf)
			public Dictionary<int, Placed[]> ByUnit;
			// Per place: its content may become a surprise by the chance roll (SurpriseCandidates), is past its item's copy cap
			// (always a surprise when one can be rolled), and did become one
			public bool[] SurpriseCandidate;
			public bool[] Forced;
			public bool[] Surprised;
			// The world evil of the spawn map the keepers were ranked with (EarlyShare)
			public WorldEvil Evil;
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

		// Pure: ContentOf for these places and this seed. In each group (each of its GateBands, cut at fixed tiers) the places
		// are sorted by rank, and each takes the content of the next one (a random cycle: no content stays at its place, but
		// a hard gate alone in its band). Removing one place only changes the content of the place before it (or, when it
		// leaves a band with a single owner, merges that band into the one below)
		public static int[] Permute(IReadOnlyList<Place> places, int seed, out List<List<int>> groups)
		{
			int[] contentOf = Enumerable.Range(0, places.Count).ToArray();
			groups = Groups(places);
			foreach (List<int> group in groups)
			{
				foreach (List<int> band in GateBands(group, i => PlaceTier(places[i]), i => GateTier(places[i].Entry.itemId), i => places[i].Owner))
				{
					List<int> order = band.OrderBy(i => Rank(seed, places[i])).ThenBy(i => places[i].OwnerKey).ThenBy(i => places[i].Owner).ThenBy(i => places[i].Index).ToList();
					for (int k = 0; k < order.Count; k++) contentOf[order[k]] = order[(k + 1) % order.Count];
				}
			}
			return contentOf;
		}

		// Hard gates (ItemPool.IsHardGate: Luminite, the mechanical souls, Ectoplasm, Beetle Husks...) have no tier allowance:
		// a content holding one never moves to a place of a lower tier. -1: any place
		public static int GateTier(int item) => ItemPool.IsHardGate(item) ? ItemPool.TrueTierOf(item) : -1;

		// The tier from which a place gives its content: its own, raised by its progression conditions (hardmode, Plantera...:
		// Findability.ConditionTier); -1 for a place that never drops (a pre-hardmode only entry of a hardmode enemy)
		public static int PlaceTier(Place place)
		{
			int tier = Findability.ConditionTier(place.Entry.conditions, place.Tier);
			return tier == Findability.Never ? -1 : tier;
		}

		// The tiers that cut a group into gate bands: the true tiers of every hard gate of the item pool, whichever gates the
		// group holds and whatever its lowest place, so that a place joining or leaving a group (a setting changed which
		// entries are kept) never moves the cuts of the others. Read once per item pool (load time: the tier overrides need
		// a reload)
		private static int[] gateCuts = Array.Empty<int>();
		private static ItemInfo[] gateCutsFor;

		public static IReadOnlyList<int> GateCuts()
		{
			if (gateCutsFor == ItemPool.Info) return gateCuts;
			gateCuts = Enumerable.Range(1, Math.Max(0, ItemPool.Info.Length - 1)).Where(ItemPool.IsHardGate).Select(ItemPool.TrueTierOf).Distinct().OrderBy(t => t).ToArray();
			gateCutsFor = ItemPool.Info;
			return gateCuts;
		}

		public static List<List<int>> GateBands(List<int> group, Func<int, int> placeTier, Func<int, int> gateTier, Func<int, int> ownerOf) =>
			GateBands(group, placeTier, gateTier, ownerOf, GateCuts());

		// Pure: the parts of a group its contents cycle in. The cuts (GateCuts) split its places into bands by placeTier (from
		// one cut to the next), and every content cycles in its own place's band: a hard gate only reaches places of its true
		// tier or later. A place that never drops (tier -1) joins the band of the group's lowest dropping place (it gives
		// nothing early). A band of a single owner (the Moon Lord alone from tier 9) would only shuffle that owner's own loot:
		// it joins the band below, its gates too high for it staying where they are, as does a gate whose own place is below
		// its true tier (vanilla put it there). Each band keeps the group's order
		public static List<List<int>> GateBands(List<int> group, Func<int, int> placeTier, Func<int, int> gateTier, Func<int, int> ownerOf, IReadOnlyList<int> fixedCuts)
		{
			if (group.Count < 2) return new List<List<int>> { group };
			// Over the places that drop: one that never does lowers nothing
			int lowest = group.Select(placeTier).Where(t => t >= 0).DefaultIfEmpty(-1).Min();
			List<int> cuts = fixedCuts.Where(c => c >= 0).Distinct().OrderBy(c => c).ToList();
			List<List<int>> stay = new();
			Dictionary<int, List<int>> byCut = new();
			List<int> Band(int cut)
			{
				if (!byCut.TryGetValue(cut, out List<int> band)) byCut[cut] = band = new List<int>();
				return band;
			}
			foreach (int p in group)
			{
				int tier = placeTier(p);
				if (gateTier(p) > tier) stay.Add(new List<int> { p });
				else
				{
					int banded = tier < 0 ? lowest : tier;
					Band(cuts.Where(c => c <= banded).DefaultIfEmpty(-1).Max()).Add(p);
				}
			}
			cuts.Insert(0, -1);
			for (int k = cuts.Count - 1; k > 0; k--)
			{
				if (!byCut.TryGetValue(cuts[k], out List<int> band) || band.Select(ownerOf).Distinct().Count() >= 2) continue;
				byCut.Remove(cuts[k]);
				// The lowest band takes any gate up to the group's lowest dropping tier
				int floor = k == 1 ? lowest : cuts[k - 1];
				foreach (int p in band)
				{
					if (gateTier(p) > floor) stay.Add(new List<int> { p });
					else Band(cuts[k - 1]).Add(p);
				}
			}
			// In the group's order (a merged band had its members appended)
			Dictionary<int, int> position = group.Select((p, i) => (p, i)).ToDictionary(x => x.p, x => x.i);
			return stay.Concat(byCut.Where(b => b.Value.Count > 0).OrderBy(b => b.Key).Select(b => b.Value.OrderBy(p => position[p]).ToList())).ToList();
		}

		// Load-time facts (samples, snapshot): cached until unload
		private static readonly Dictionary<int, bool> eligible = new();
		private static readonly Dictionary<int, int> units = new();

		// Can this net ID's drops be permuted (whatever the style)? Settings play no part, so that a config change does not
		// move the places of the world: ordinary enemies with drops, or without drops but with a coin value (the synthetic
		// place). Vanilla and modded NPCs alike (a modded enemy's vanilla-typed rules are wrapped, its own rule classes keep
		// running; its tier is NpcTiers' estimate). Not critters, harmless or projectile NPCs, town NPCs, bosses, their parts
		// and minions
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
					// The synthetic place (no drops, a coin value) for vanilla enemies only: a modded one without drops is often a
					// worm segment or a summoned minion, a farm of free loot
					&& (LootSnapshot.ForNpc(netId).Count > 0 || sample.value > 0 && type < NPCID.Count);
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

		public static bool SameSnapshot(int a, int b)
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

		// The synthetic entry of an eligible enemy without any vanilla entry: a random item of its place tier (PlaceTierOf) with a vanilla-like
		// chance, created whatever the settings (EnemiesWithoutDropsCanDrop only gates the random extra slot), so the places of
		// the world don't depend on them. Its item follows the item pool (categories, protections)
		public static bool TrySynthetic(int unit, int seed, out DropRateInfo entry)
		{
			entry = default;
			if (LootSnapshot.ForNpc(unit).Count > 0 || !ItemPool.Ready) return false;
			int key = DetRandom.NpcKey(unit);
			int tier = PlaceTierOf(unit);
			RollRequest request = new()
			{
				Keys = new[] { RollKey.DropPermutation, seed, key },
				SourceTier = tier,
				OriginalItem = -1,
				Policy = CategoryPolicy.Weighted,
				IgnoreGating = true
			};
			int item = RandomNpcDropRule.RollBelowGates(request, out int rolledTier);
			if (item <= ItemID.None) return false;
			int denominator = DropRates.Denominator(item, rolledTier, tier, false, DetRandom.Get(RollKey.DropPermutation, seed, key, 1));
			(int min, int max) = StackRules.RollExtraRange(item, tier, DetRandom.Get(RollKey.DropPermutation, seed, key, 2));
			entry = new DropRateInfo(item, min, max, 1f / Math.Max(1, denominator), null);
			return true;
		}

		// The tier of an enemy's places: its base tier (NpcTiers: its stats), but for a vanilla enemy spawning from a known stage
		// (NpcTiers.SpawnTier) the tier it is met from when that is lower: mimics, the Wyvern, Giant Tortoises or Moss Hornets
		// come with hardmode but are tier 6 to 8 by their stats, and their whole table waited for that tier (the Wyvern's Souls
		// of Flight went to a tier-7 Dark Mage). An event enemy: its event's tier, also when it is weaker than its event
		// (Martian Madness after Golem: EventTier). Their random extra slots keep the stats tier (RandomNpcDropRule.SourceTier):
		// a strong enemy still unlocks more loot later
		public static int PlaceTierOf(int unit)
		{
			int tier = NpcTiers.BaseTier(unit);
			int type = ContentSamples.NpcsByNetId.TryGetValue(unit, out NPC sample) ? sample.type : unit;
			int spawn = type > 0 && type < NPCID.Count ? NpcTiers.SpawnTier(type) : -1;
			if (spawn < 0) return tier;
			return GuaranteedSources.IsEventEnemy(type) ? spawn : Math.Min(tier, spawn);
		}

		// The stage a vanilla event enemy comes with (NpcTiers.SpawnTier: the Old One's Army waves, the eclipse, the moons,
		// Martian Madness), -1 for the others
		public static int EventTier(int type)
		{
			if (type <= 0 || type >= NPCID.Count) return -1;
			int spawn = NpcTiers.SpawnTier(type);
			return spawn >= 0 && GuaranteedSources.IsEventEnemy(type) ? spawn : -1;
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
				int tier = PlaceTierOf(unit);
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

		// Blocks, critters and ores: found in the world itself (tiles, critters), every copy may become a surprise
		public static bool FoundInWorld(int item) => ItemPool.IsValid(item) && (ItemPool.Info[item].Category is ItemCategory.Block or ItemCategory.Critter || OreMap.IsOre(item));

		// Materials, ores and bars are never rolled surprises: the permutation keeps them where recipes expect enemies to give
		// them (materials beyond their copy cap are forced ones all the same: SurpriseCandidates)
		public static bool SurpriseKind(int item) => ItemPool.IsValid(item) && ItemPool.Info[item].Category is not (ItemCategory.Material or ItemCategory.OreBar);

		// The copy cap of an item (SurpriseCandidates): materialCopies for a material, duplicateCopies for the rest; 0 (no
		// limit) for ores and bars, which recipes need wherever vanilla put them, and for items FoundInWorld
		public static int CopyCap(int item, int duplicateCopies, int materialCopies)
		{
			if (!ItemPool.IsValid(item) || FoundInWorld(item) || OreMap.IsOre(item)) return 0;
			// The start drops keep every copy: the critical stations' recipes count on them in the first kills (1.7.0 kept four
			// Lens copies on rare enemies, and the Lens was no start material any more)
			if (Array.IndexOf(RecipePlanner.StartDrops, item) >= 0) return 0;
			return ItemPool.Info[item].Category switch
			{
				ItemCategory.OreBar => 0,
				ItemCategory.Material => Math.Max(0, materialCopies),
				_ => Math.Max(0, duplicateCopies)
			};
		}

		// Pure (with the item pool): the places whose content may become a surprise (candidates of the chance roll), and those
		// whose content always does (forced). Never a synthetic content (already random). Every item but those FoundInWorld
		// ranks its permuted copies from the most reachable: the lowest condition tier (Findability.ConditionTier) first, then
		// no condition at all, then (a tier-0 material: Gel, Lens, the critical stations' start materials) the owners the early
		// kills meet most (earlyShare: owner -> share of the early kills, PacingModel.EarlyKillShare; null: not used), then the
		// rank. Copies are counted per access class (the place's: contents never leave theirs): an item of a NormalvsExpert
		// drop has its Classic copies and its Expert ones, each class keeps its own keeper and its own cap of copies, so the
		// item drops in every difficulty. The keeper is never surprised whatever its other sources (shops, bags, recipes and
		// boss drops are randomized too), so an item with a single copy always stays. Vanilla gives every zombie variant its
		// own Shackle and every slime its own Gel: the copies past the item's cap (CopyCap, 0: no cap) are forced, so a common
		// item stops filling the early tables. The kept copies after the keeper are candidates, not materials, ores or bars.
		// Depends on the caps (and the spawn map) only, so a surprise stays when the chance or another system's setting moves
		public static bool[] SurpriseCandidates(IReadOnlyList<Place> places, int[] contentOf, int seed, int duplicateCopies, int materialCopies, out bool[] forced, Func<int, double> earlyShare = null)
		{
			Dictionary<int, double> shares = new();
			double Early(int p)
			{
				int item = places[contentOf[p]].Entry.itemId;
				if (earlyShare == null || !ItemPool.IsValid(item) || ItemPool.Info[item].Category != ItemCategory.Material || ItemPool.Info[item].Tier != 0) return 0;
				int owner = places[p].Owner;
				if (!shares.TryGetValue(owner, out double share)) shares[owner] = share = earlyShare(owner);
				return share;
			}
			(int, int, double, int, int, int, int) Order(int p)
			{
				List<IItemDropRuleCondition> conditions = places[p].Entry.conditions;
				bool free = conditions == null || conditions.Count == 0;
				return (Findability.ConditionTier(conditions, places[p].Tier), free ? 0 : 1, -Early(p), Rank(seed, places[p]), places[p].OwnerKey, places[p].Owner, places[p].Index);
			}
			// Each place's position among the copies of its content's item in its access class (0: the keeper); -1 for the
			// uncounted ones
			int[] position = Enumerable.Repeat(-1, places.Count).ToArray();
			Dictionary<(int, string), List<int>> copies = new();
			for (int p = 0; p < places.Count; p++)
			{
				Place source = places[contentOf[p]];
				int item = source.Entry.itemId;
				if (source.Synthetic || FoundInWorld(item)) continue;
				(int, string) key = (item, places[p].Access ?? "");
				if (!copies.TryGetValue(key, out List<int> list)) copies[key] = list = new List<int>();
				list.Add(p);
			}
			foreach (List<int> list in copies.Values)
			{
				list.Sort((a, b) => Order(a).CompareTo(Order(b)));
				for (int k = 0; k < list.Count; k++) position[list[k]] = k;
			}
			bool[] candidate = new bool[places.Count];
			forced = new bool[places.Count];
			for (int p = 0; p < places.Count; p++)
			{
				Place source = places[contentOf[p]];
				if (source.Synthetic) continue;
				int item = source.Entry.itemId;
				int cap = CopyCap(item, duplicateCopies, materialCopies);
				forced[p] = cap > 0 && position[p] >= cap;
				candidate[p] = !forced[p] && SurpriseKind(item) && position[p] != 0;
			}
			return candidate;
		}

		// The share of the early kills (PacingModel.EarlyKillShare: spawn map and synced settings, never a drop plan) made on
		// this table's enemies (variants count as their type)
		public static double EarlyShare(int owner)
		{
			int type = BiomeSlots.TypeOf(owner);
			return PacingModel.EarlyKillShare(k => k == type);
		}

		// The surprise roll of a place, in [0, 1): its own hash, so the surprises of a world only grow with the chance
		public static double SurpriseRoll(int seed, Place place) => new Random(DetRandom.Hash(seed, RollKey.DropSurprise, place.OwnerKey, place.Index)).NextDouble();

		// The surprise of a place: an item rolled with the extra drop weights (no materials by default) at the place's base
		// tier, everything unlocked (the plan is final: progression locks it like any entry, DropHelper.LockGap), no jackpot.
		// Its chance is the content's, capped by a vanilla-like rate for the new item: a frequent entry (gel, 1/1) does not
		// become a frequent weapon
		// attempt: a forced copy rolls again when it drew its own content (attempt 0 keeps the keys of the first roll)
		public static bool TrySurprise(Place place, int seed, DropRateInfo content, out int item, out int stackMin, out int stackMax, out float dropRate, int attempt = 0)
		{
			RollRequest request = new()
			{
				Keys = attempt == 0 ? new[] { RollKey.DropSurprise, seed, place.OwnerKey, place.Index } : new[] { RollKey.DropSurprise, seed, place.OwnerKey, place.Index, -attempt },
				SourceTier = place.Tier,
				OriginalItem = -1,
				Policy = CategoryPolicy.Weighted,
				Weights = LootConfig.Instance.PermutationExtraWeights,
				AllowJackpot = false,
				IgnoreGating = true
			};
			item = RandomNpcDropRule.RollBelowGates(request, out int rolledTier);
			(stackMin, stackMax) = item > ItemID.None ? StackRules.RollExtraRange(item, place.Tier, DetRandom.Get(RollKey.DropSurprise, seed, place.OwnerKey, place.Index, 1)) : (0, 0);
			dropRate = content.dropRate;
			if (item > ItemID.None)
			{
				int denominator = DropRates.Denominator(item, rolledTier, place.Tier, false, DetRandom.Get(RollKey.DropSurprise, seed, place.OwnerKey, place.Index, 2));
				dropRate = Math.Min(content.dropRate, 1f / Math.Max(1, denominator));
			}
			return item > ItemID.None && item != content.itemId;
		}

		public const int ForcedSurpriseAttempts = 4;

		// The whole plan for a seed. The tier is the enemy's base tier, not the effective one: that one follows progression
		// (ScaleWeakEnemiesWithProgression) and the drops of a world in progress must not move. surpriseChance: share of the
		// candidates that become surprises, in [0, 1] (PermutationSurpriseChance when null; none in shuffle mode).
		// duplicateCopies / materialCopies: the copy caps (PermutationDuplicateCopies / PermutationMaterialCopies when null,
		// 0: no cap; none in shuffle mode, which only moves the vanilla contents)
		public static WorldPlan Build(int seed, float? surpriseChance = null, int? duplicateCopies = null, int? materialCopies = null)
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
			// Surprises: a forced place, or a candidate whose own roll is under the chance, gets a random item instead of its
			// content (a forced place keeps it when no surprise can be rolled)
			bool shuffle = GeneralConfig.Instance.RandomizerMode == RandomizerMode.Shuffle;
			float chance = surpriseChance ?? (shuffle ? 0f : LootConfig.Instance.PermutationSurpriseChance / 100f);
			chance = Math.Clamp(chance, 0f, 1f);
			int copies = duplicateCopies ?? (shuffle ? 0 : LootConfig.Instance.PermutationDuplicateCopies);
			int materials = materialCopies ?? (shuffle ? 0 : LootConfig.Instance.PermutationMaterialCopies);
			bool[] candidates = SurpriseCandidates(places, contentOf, seed, copies, materials, out bool[] forced, EarlyShare);
			bool[] surprised = new bool[places.Count];
			for (int p = 0; p < places.Count; p++)
			{
				Place place = places[p];
				Placed placed = byUnit[place.Owner][place.Index];
				DropRateInfo content = placed.Entry;
				if (!forced[p] && !(candidates[p] && chance > 0f && SurpriseRoll(seed, place) < chance)) continue;
				// A forced copy tries a few rolls: drawing its own item again would leave one copy over the cap
				int item = 0, min = 0, max = 0;
				float rate = 0f;
				bool rolled = false;
				for (int attempt = 0; attempt < (forced[p] ? ForcedSurpriseAttempts : 1) && !rolled; attempt++) rolled = TrySurprise(place, seed, content, out item, out min, out max, out rate, attempt);
				if (!rolled) continue;
				placed.Entry = new DropRateInfo(item, min, max, rate, content.conditions);
				placed.Surprise = true;
				placed.Replaced = content.itemId;
				byUnit[place.Owner][place.Index] = placed;
				surprised[p] = true;
			}
			return new WorldPlan { Places = places, ContentOf = contentOf, Groups = groups, ByUnit = byUnit, SurpriseCandidate = candidates, Forced = forced, Surprised = surprised, Evil = BiomeSlots.CurrentEvil };
		}

		private static WorldPlan Empty() => new()
		{
			Places = new List<Place>(), ContentOf = Array.Empty<int>(), Groups = new List<List<int>>(), ByUnit = new Dictionary<int, Placed[]>(),
			SurpriseCandidate = Array.Empty<bool>(), Forced = Array.Empty<bool>(), Surprised = Array.Empty<bool>()
		};

		// The plan of the current world (cleared by WorldCaches.Reset; made again when the world's evil arrives on a client: the
		// keepers follow the spawn map, EarlyShare). Before the item pool (and the world) exist the
		// synthetic entries can't be rolled: an empty plan, not kept
		public static WorldPlan Current
		{
			get
			{
				if (cached != null && cached.Evil == BiomeSlots.CurrentEvil) return cached;
				if (!ItemPool.Ready || !SeedSystem.WorldReady) return Empty();
				return cached = Build(SeedSystem.WorldSeed);
			}
		}

		private static readonly Placed[] None = Array.Empty<Placed>();

		public static Placed[] For(int netId) => Current.ByUnit.TryGetValue(UnitOf(netId), out Placed[] table) ? table : None;

		// Only rules whose report is complete, chains included, are wrapped (CompleteRules). forBosses: also the few-from-options
		// rules. Never a rule with a chain hidden from the report (its items would be silenced without being in the table: the
		// Nailhead's nails)
		public static bool Wrappable(IItemDropRule rule, int depth = 0, bool forBosses = false)
		{
			if (rule == null || depth > 10 || !(CompleteRules.Contains(rule.GetType()) || forBosses && CompleteForBosses.Contains(rule.GetType()))) return false;
			if (rule is DropBasedOnExpertMode expert && !(Wrappable(expert.ruleForNormalMode, depth + 1, forBosses) && Wrappable(expert.ruleForExpertMode, depth + 1, forBosses))) return false;
			if (rule is DropBasedOnMasterMode master && !(Wrappable(master.ruleForDefault, depth + 1, forBosses) && Wrappable(master.ruleForMasterMode, depth + 1, forBosses))) return false;
			if (rule.ChainedRules != null)
			{
				foreach (IItemDropRuleChainAttempt chain in rule.ChainedRules)
				{
					if (chain == null || !CompleteChains.Contains(chain.GetType()) || !Wrappable(chain.RuleToChain, depth + 1, forBosses)) return false;
					if (HidesReport(chain)) return false;
				}
			}
			return true;
		}

		public static bool IsCompleteChain(IItemDropRuleChainAttempt chain) => chain != null && CompleteChains.Contains(chain.GetType());

		public static bool HidesReport(IItemDropRuleChainAttempt chain) => chain switch
		{
			Chains.TryIfSucceeded s => s.hideLootReport,
			Chains.TryIfFailedRandomRoll f => f.hideLootReport,
			Chains.TryIfDoesntFillConditions c => c.hideLootReport,
			_ => true
		};
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
	// Only the vanilla rule types are wrapped, of vanilla and modded NPCs and boss bags (DropPermutation.Permutable): another
	// mod's own rule classes keep running as they are. Accepted limitation: another mod's ModifyNPCLoot
	// (or ModifyItemLoot) running after ours no longer finds the wrapped vanilla rules (to remove or change them, a "rule is
	// CommonDrop" check fails), and a vanilla-typed rule another mod added before ours to a vanilla NPC is wrapped like the
	// vanilla ones; rules of other types are left alone
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

		// A boss bag's rule (BossPermutation, NetId 0): silent when the bag's permutation is active
		public int BagType { get; }

		public PermutableRule(IItemDropRule inner, int netId, int ruleIndex, IEnumerable<int> items, int bagType = 0)
		{
			this.inner = inner;
			NetId = netId;
			RuleIndex = ruleIndex;
			BagType = bagType;
			this.items = items.ToArray();
		}

		// Same test as DropPermutation.RolledEntries
		internal bool Silenced => (BagType > 0 ? BossPermutation.BagActive(BagType) : DropPermutation.Active(NetId) || BossPermutation.Active(NetId)) && DropPermutation.Silences(items);

		// Load time only (BossPermutation.Track): the wrapper reporting each entry of a capture, by entry index
		public static Dictionary<int, int> Tracked;

		public bool CanDrop(DropAttemptInfo info) => true;

		// Vanilla nested rules do nothing when resolved without the action
		public ItemDropAttemptResult TryDroppingItem(DropAttemptInfo info) => new() { State = ItemDropAttemptResultState.DidNotRunCode };

		public ItemDropAttemptResult TryDroppingItem(DropAttemptInfo info, ItemDropRuleResolveAction resolveAction) =>
			Silenced ? new ItemDropAttemptResult { State = ItemDropAttemptResultState.DidNotRunCode } : resolveAction(inner, info);

		// The bestiary is built before any world exists: the original rule reports its drops. In a world showing its own
		// drops (WorldDropsReport, client option), a silenced rule reports nothing: our rule reports its plan instead
		public void ReportDroprates(List<DropRateInfo> drops, DropRateInfoChainFeed ratesInfo)
		{
			if (Silenced && WorldDropsReport.Showing) return;
			int before = drops.Count;
			inner.ReportDroprates(drops, ratesInfo.With(1f));
			if (Tracked == null) return;
			for (int i = before; i < drops.Count; i++) Tracked[i] = RuleIndex;
		}
	}
}

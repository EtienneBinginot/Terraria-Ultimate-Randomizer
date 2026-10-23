using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using UltimateRandomizer.Common.Codex;
using UltimateRandomizer.Common.Codex.Interface;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;
using UltimateRandomizer.Common.Journal;

namespace UltimateRandomizer.Common.Tests
{
	// The Guide's hints: their facts come from the planners, their prices, buying, syncing and texts
	public static class HintTests
	{
		public static void Register()
		{
			TestRunner.Register("HintFactsTrue", HintFactsTrue);
			TestRunner.Register("HintPriceScales", HintPriceScales);
			TestRunner.Register("HintPurchaseFlow", HintPurchaseFlow);
			TestRunner.Register("HintSyncRoundTrip", HintSyncRoundTrip);
			TestRunner.Register("HintTextsResolved", HintTextsResolved);
			TestRunner.Register("HintSourceFrequent", HintSourceFrequent);
			TestRunner.Register("HintTargetsHaveSource", HintTargetsHaveSource);
			TestRunner.Register("HintBiomeEveryLevel", HintBiomeEveryLevel);
			TestRunner.Register("HintRevealDiscovers", HintRevealDiscovers);
			TestRunner.Register("HintHostsNamed", HintHostsNamed);
			TestRunner.Register("PlannedShareSums", PlannedShareSums);
			TestRunner.Register("HintSortedByProgress", HintSortedByProgress);
			TestRunner.Register("HintFoundUnlocks", HintFoundUnlocks);
			TestRunner.Register("HintClickOpensCodex", HintClickOpensCodex);
			TestRunner.Register("HintTreeComplete", HintTreeComplete);
			TestRunner.Register("HintTreeDeterministic", HintTreeDeterministic);
			TestRunner.Register("HintTabsSplit", HintTabsSplit);
			TestRunner.Register("HintTreeKeepsOtherHints", HintTreeKeepsOtherHints);
			TestRunner.Register("HintConditionsShown", HintConditionsShown);
			TestRunner.Register("HintSummonTiers", HintSummonTiers);
			TestRunner.Register("HintLockedShown", HintLockedShown);
		}

		private static List<HintTarget> RecipeTargets() =>
			TargetsOrSkip().Concat(HintFacts.AllTargets()).Distinct().Where(t => HintFacts.For(t.Item)?.Kind == HintKind.Recipe).ToList();

		// A recipe hint gives every ingredient and station down to a raw source: every crafted part is expanded (within the
		// depth limit), nothing is left without a source, and the parts follow the planned recipe
		private static void HintTreeComplete()
		{
			List<HintTarget> targets = RecipeTargets();
			if (targets.Count == 0) Check.Skip("no recipe hint in this world");
			List<string> failures = new();
			foreach (HintTarget target in targets)
			{
				string name = HintFacts.ItemName(target.Item);
				List<HintTreeNode> tree = HintFacts.SourceTree(target.Item);
				HintFact fact = HintFacts.For(target.Item);
				int parts = fact.Ingredients.Count(x => x.type > 0) + fact.Tiles.Distinct().Select(t => HintFacts.StationItem(t) is int s && s > 0 ? s : -1 - t).Distinct().Count();
				if (parts > 0 && tree.Count == 0) failures.Add($"{name}: empty tree");
				bool truncated = HintFacts.TreeTruncated(target.Item);
				int first = tree.Count(n => n.Depth == 1);
				if (truncated ? first > parts : first != parts) failures.Add($"{name}: {first} first parts, recipe has {parts}");
				for (int i = 0; i < tree.Count; i++)
				{
					HintTreeNode node = tree[i];
					string part = $"{name} > {HintFacts.TreeName(node)}";
					if (node.Leaf == HintTreeLeaf.Unknown) failures.Add($"{part}: no source");
					if (node.Depth < 1 || node.Depth > HintFacts.MaxTreeDepth) failures.Add($"{part}: depth {node.Depth}");
					if (node.Leaf == HintTreeLeaf.Fact && (node.Fact == null || node.Fact.Kind == HintKind.Recipe)) failures.Add($"{part}: fact leaf without a raw source");
					if (node.Leaf == HintTreeLeaf.Mined && node.Ore <= 0) failures.Add($"{part}: no ore to mine");
					if (node.Leaf == HintTreeLeaf.WorldStation && (node.Item > 0 || node.Tile < 0)) failures.Add($"{part}: station without its tile");
					// An ingredient dropped by an enemy is named with that enemy, not left to "found in the world"
					if (node.Leaf == HintTreeLeaf.Findable && GuaranteedSources.HostsOf(node.Item).Any()) failures.Add($"{part}: guaranteed on an enemy but not named");
					// A crafted part is followed by its own parts, one level deeper
					bool expanded = i + 1 < tree.Count && tree[i + 1].Depth == node.Depth + 1;
					if (node.Leaf == HintTreeLeaf.Recipe && node.Depth < HintFacts.MaxTreeDepth && !expanded && !(truncated && i == tree.Count - 1)) failures.Add($"{part}: crafted but not expanded");
					bool needsTool = node.Leaf is HintTreeLeaf.Caught or HintTreeLeaf.Filled;
					if (needsTool && node.Depth < HintFacts.MaxTreeDepth && !expanded && !(truncated && i == tree.Count - 1)) failures.Add($"{part}: its tool is missing");
					if (node.Leaf != HintTreeLeaf.Recipe && !needsTool && expanded) failures.Add($"{part}: expanded without a recipe");
					if (node.Leaf == HintTreeLeaf.Harvested && !WorldSources.HerbTable(node.Herb).Contains(node.Item)) failures.Add($"{part}: not given by {HintFacts.ItemName(node.Herb)}");
				}
			}
			Check.All(failures, "Incomplete recipe hint trees");
		}

		// A tree depends on the planned data only, not on the order the facts were asked in: built with every fact still to
		// find, then again once all of them are known
		private static void HintTreeDeterministic()
		{
			List<HintTarget> targets = RecipeTargets();
			if (targets.Count == 0) Check.Skip("no recipe hint in this world");
			static string Describe(List<HintTreeNode> tree) => string.Join(";", tree.Select(n => $"{n.Item}/{n.Tile}x{n.Stack}@{n.Depth}:{n.Leaf}:{n.Ore}:{n.Fact?.Kind}:{n.Fact?.Npc}:{n.Fact?.Bag}"));
			// In one order from nothing known, then from nothing known again in the other order, then with every fact known
			HintFacts.Reset();
			Dictionary<int, string> forward = targets.ToDictionary(t => t.Item, t => Describe(HintFacts.SourceTree(t.Item)));
			HintFacts.Reset();
			Dictionary<int, string> backward = Enumerable.Reverse(targets).ToDictionary(t => t.Item, t => Describe(HintFacts.SourceTree(t.Item)));
			HintFacts.ForgetTrees();
			List<string> failures = targets.Where(t => backward[t.Item] != forward[t.Item] || Describe(HintFacts.SourceTree(t.Item)) != forward[t.Item])
				.Select(t => HintFacts.ItemName(t.Item)).ToList();
			Check.All(failures, "Recipe hint trees depend on the order of the facts");
		}

		// A hint tree never gives away another hint: a station, summon or pickaxe of the tree whose own hint is not known
		// shows no source, and nothing below it
		private static void HintTreeKeepsOtherHints()
		{
			List<HintTarget> targets = RecipeTargets();
			if (targets.Count == 0) Check.Skip("no recipe hint in this world");
			List<string> failures = new();
			int hiddenSeen = 0;
			foreach (HintTarget target in targets)
			{
				List<HintTreeNode> tree = HintFacts.SourceTree(target.Item);
				foreach (HintTreeNode node in tree)
				{
					bool other = node.Item > 0 && node.Item != target.Item && HintFacts.RawTarget(node.Item) != null;
					bool known = CodexSystem.ShownHintLevel(node.Item) >= HintFacts.MaxLevel;
					if (HintFacts.HiddenInTree(node, target.Item) != (other && !known)) failures.Add($"{HintFacts.ItemName(target.Item)} > {HintFacts.TreeName(node)}: hidden {!(other && !known)}");
					if (other && !known) hiddenSeen++;
				}
				// The rows: a hidden part is followed by no deeper row
				int rowsShown = HintView.TreeRows(target.Item, false, 500, false).Count;
				int expected = 1;
				int below = int.MaxValue;
				foreach (HintTreeNode node in tree)
				{
					if (node.Depth > below) continue;
					below = HintFacts.HiddenInTree(node, target.Item) ? node.Depth : int.MaxValue;
					expected++;
				}
				if (HintFacts.TreeTruncated(target.Item)) expected++;
				if (rowsShown != expected) failures.Add($"{HintFacts.ItemName(target.Item)}: {rowsShown} rows, expected {expected}");
			}
			Check.All(failures, $"Hint trees giving other hints away ({hiddenSeen} parts kept for their own hint)");
		}

		// The Guide's two categories: boss and event summons in Bosses, stations and pickaxes in Crafting, each target once
		private static void HintTabsSplit()
		{
			TargetsOrSkip();
			List<string> failures = new();
			foreach (bool known in new[] { false, true })
			{
				List<HintTarget> all = HintPanelState.TabTargets(known);
				List<HintTarget> crafting = HintPanelState.TabTargets(known, false), bosses = HintPanelState.TabTargets(known, true);
				if (crafting.Count + bosses.Count != all.Count || crafting.Intersect(bosses).Any()) failures.Add($"known {known}: {crafting.Count} + {bosses.Count} of {all.Count}");
				// Judged on the items themselves, not on the kind the filter reads
				failures.AddRange(bosses.Where(t => !Protection.IsBossSummon(t.Item)).Select(t => $"{HintFacts.ItemName(t.Item)} among the bosses"));
				failures.AddRange(crafting.Where(t => Protection.IsBossSummon(t.Item)).Select(t => $"{HintFacts.ItemName(t.Item)} among crafting"));
			}
			Check.All(failures, "Hint categories");
		}

		private static List<HintFact> CreatureFacts() => TargetsOrSkip().Select(t => HintFacts.For(t.Item)).Where(f => f != null && f.Kind == HintKind.Creature).ToList();

		// Steady share of a type in each biome, recomputed from the slots (not from the planner's table): slots open in the
		// band, weight 1 (rare 0.1, conditional 0.5), only the slots neither rare nor conditional count for the type, split by
		// sampling Pick
		private static Dictionary<int, double> SteadyFromSlots(int type, int tier)
		{
			int band = BiomeSlots.BandOf(Tiers.Clamp(tier));
			Dictionary<int, double> totals = new(), mine = new();
			const int Rolls = 200;
			foreach (BiomeSlot slot in BiomeSlots.SpawnMap.Slots)
			{
				if (slot.Critter || slot.AbsentEvil || slot.Band > band) continue;
				double weight = (slot.Rare ? 0.1 : 1) * (slot.Conditional ? 0.5 : 1);
				totals[slot.Biome] = totals.GetValueOrDefault(slot.Biome) + weight;
				if (slot.Rare || slot.Conditional) continue;
				int hits = 0;
				for (int k = 0; k < Rolls; k++)
				{
					if (BiomeSlots.TypeOf(BiomeSlots.Pick(slot, (k + 0.5) / Rolls)) == type) hits++;
				}
				if (hits > 0) mine[slot.Biome] = mine.GetValueOrDefault(slot.Biome) + weight * hits / Rolls;
			}
			return mine.ToDictionary(p => p.Key, p => p.Value / totals[p.Key]);
		}

		// The same in one period (Day: day and any-hour slots...), counting for the type only the slots met from the tier on
		// (BiomeSlots.EffectiveTier), as the guaranteed items' share is stated (GuaranteedSources.BiomeShare)
		private static Dictionary<int, double> SteadyFromSlotsAt(int type, int tier, SlotTime period)
		{
			int band = BiomeSlots.BandOf(Tiers.Clamp(tier));
			Dictionary<int, double> totals = new(), mine = new();
			const int Rolls = 200;
			foreach (BiomeSlot slot in BiomeSlots.SpawnMap.Slots)
			{
				if (slot.Critter || slot.AbsentEvil || slot.Band > band || !BiomeSlots.OpenIn(slot.Time, period)) continue;
				double weight = (slot.Rare ? 0.1 : 1) * (slot.Conditional ? 0.5 : 1);
				totals[slot.Biome] = totals.GetValueOrDefault(slot.Biome) + weight;
				if (slot.Rare || slot.Conditional || BiomeSlots.EffectiveTier(slot) > tier) continue;
				int hits = 0;
				for (int k = 0; k < Rolls; k++)
				{
					if (BiomeSlots.TypeOf(BiomeSlots.Pick(slot, (k + 0.5) / Rolls)) == type) hits++;
				}
				if (hits > 0) mine[slot.Biome] = mine.GetValueOrDefault(slot.Biome) + weight * hits / Rolls;
			}
			return mine.ToDictionary(p => p.Key, p => p.Value / totals[p.Key]);
		}

		// A creature hint leads to an enemy met often (at least 5 % of the steady spawns of its best biome, no rare enemy)
		// that drops the item with the chance the codex shows (1 % or more); a guaranteed item's enemies are judged together,
		// on the share they make of its biome. Fallbacks are counted by HintTargetsHaveSource
		private static void HintSourceFrequent()
		{
			List<HintFact> facts = CreatureFacts().Where(f => !f.Fallback).ToList();
			if (facts.Count == 0) Check.Skip("no creature hint in this world");
			bool shares = BiomeSlots.SpawnsPlanned && !(BiomeSlots.Active && EnemiesConfig.Instance.BiomeVanillaChance > 0);
			List<string> failures = new();
			foreach (HintFact fact in facts)
			{
				string name = $"{HintFacts.ItemName(fact.Item)} on {HintFacts.NpcName(fact.Npc)}";
				int type = BiomeSlots.TypeOf(fact.Npc);
				if (fact.Hosts.Append(fact.Npc).Any(h => ContentSamples.NpcsByNetId[h].rarity > 0)) failures.Add($"{name}: a rare enemy");
				if (fact.Chance < 0.01f - 1e-6f) failures.Add($"{name}: {fact.Chance:P2} chance");
				// Recomputed from the enemy's lucky plan: every planned entry of the item together
				float missing = 1f;
				foreach (PlannedDrop drop in RandomNpcDropRule.Plan(fact.Npc, Main.expertMode, lucky: true))
				{
					if (drop.Item == fact.Item) missing *= 1f - Math.Clamp(drop.Chance, 0f, 1f);
				}
				if (Math.Abs((1f - missing) - fact.Chance) > 1e-4f) failures.Add($"{name}: states {fact.Chance:P2}, planned {1f - missing:P2}");
				if (!shares || !BiomeSlots.HasAnySlot(type)) continue;
				// Several enemies: the share they make together of the biome named first, at the tier and in the period(s) they
				// were placed for
				int biome = GuaranteedSources.BiomeOf(fact.Item);
				if (fact.Hosts.Count > 1 && biome >= 0)
				{
					int placed = GuaranteedSources.PlacedTier(fact.Item);
					SlotTime period = GuaranteedSources.PlacedTime(fact.Item);
					double together = fact.Hosts.Sum(h => SteadyFromSlotsAt(BiomeSlots.TypeOf(h), placed, period).GetValueOrDefault(biome));
					if (fact.Biomes.Count == 0 || fact.Biomes[0] != BiomeSlots.BiomeName(biome)) failures.Add($"{name}: does not name {BiomeSlots.BiomeName(biome)} first");
					if (together < GuaranteedSources.MinShare(biome, placed) - 0.005) failures.Add($"{name}: its enemies make {together:P1} of {BiomeSlots.BiomeName(biome)}");
					if (Math.Abs(together - fact.Share) > 0.02) failures.Add($"{name}: states {fact.Share:P1} of {BiomeSlots.BiomeName(biome)}, slots give {together:P1}");
					continue;
				}
				// Several enemies without a biome (common hosts of the tier): each lives elsewhere, the hint names no place (the
				// roaming sentences)
				if (fact.Hosts.Count > 1)
				{
					if (fact.Biomes.Count > 0) failures.Add($"{name}: names {fact.Biomes[0]} for enemies placed without a biome");
					continue;
				}
				// Judged at the requirement's tier for a guaranteed host, like the guarantees
				GuaranteedSources.Requirement requirement = GuaranteedSources.Requirements.FirstOrDefault(r => r.Item == fact.Item);
				int tier = requirement.Item == fact.Item && GuaranteedSources.HostsOf(fact.Item).Contains(fact.Npc) ? Math.Max(fact.Tier, requirement.Tier) : fact.Tier;
				Dictionary<int, double> steady = SteadyFromSlots(type, tier);
				double best = steady.Values.DefaultIfEmpty(0).Max();
				int bestBiome = steady.Count == 0 ? -1 : steady.OrderByDescending(p => p.Value).ThenBy(p => p.Key).First().Key;
				if (bestBiome < 0 || best < GuaranteedSources.MinShare(bestBiome, tier) - 0.005) failures.Add($"{name}: {best:P1} of the steady spawns of its best biome");
				if (fact.Biomes.Count == 0) failures.Add($"{name}: no biome named");
				else if (steady.Where(p => BiomeSlots.BiomeName(p.Key) == fact.Biomes[0]).Select(p => p.Value).DefaultIfEmpty(0).First() < best - 0.005)
				{
					failures.Add($"{name}: {fact.Biomes[0]} is not its best biome");
				}
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Hint sources: {facts.Count} creature hints, lowest chance {facts.Min(f => f.Chance):P1}, lowest share {facts.Min(f => f.Share):P1}");
			Check.All(failures, "Creature hints on rare sources");
		}

		private static bool AtDefault(object config, string field)
		{
			System.Reflection.FieldInfo info = config.GetType().GetField(field);
			object value = info?.GetCustomAttributes(typeof(System.ComponentModel.DefaultValueAttribute), false).OfType<System.ComponentModel.DefaultValueAttribute>().FirstOrDefault()?.Value;
			return info != null && Equals(Convert.ChangeType(value, info.FieldType.IsEnum ? Enum.GetUnderlyingType(info.FieldType) : info.FieldType), Convert.ChangeType(info.GetValue(config), info.FieldType.IsEnum ? Enum.GetUnderlyingType(info.FieldType) : info.FieldType));
		}

		// Every station or summon target with guaranteed hosts has a creature or recipe hint (or a boss or bag giving it at
		// least once in four, a shop, a world source); with the default settings, never a creature kept only because nothing
		// else than a chest or a pot gives it
		private static void HintTargetsHaveSource()
		{
			List<HintTarget> targets = TargetsOrSkip().Where(t => t.Kind is HintTargetKind.Station or HintTargetKind.Summon && GuaranteedSources.HostsOf(t.Item).Any()).ToList();
			if (targets.Count == 0) Check.Skip("no guaranteed station or summon");
			EnemiesConfig enemies = EnemiesConfig.Instance;
			bool defaults = AtDefault(enemies, "RandomizeEnemySpawns") && AtDefault(enemies, "EnemySwapMode") && AtDefault(enemies, "BiomeVanillaChance")
				&& AtDefault(ProgressionConfig.Instance, "GuaranteedDropMultiplier");
			List<string> failures = new();
			int fallbacks = 0;
			foreach (HintTarget target in targets)
			{
				HintFact fact = HintFacts.For(target.Item);
				string name = HintFacts.ItemName(target.Item);
				bool good = fact != null && (fact.Kind is HintKind.Creature or HintKind.Recipe or HintKind.Shop or HintKind.World
					|| fact.Kind is HintKind.Boss or HintKind.Bag && fact.Chance >= HintFacts.MinBossChance - 1e-6f);
				if (!good) failures.Add($"{name}: {(fact == null ? "no hint" : $"{fact.Kind} ({fact.Chance:P1})")}");
				else if (fact.Fallback)
				{
					fallbacks++;
					if (defaults) failures.Add($"{name}: only a rare source ({HintFacts.NpcName(fact.Npc)}, {fact.Chance:P1}, {fact.Share:P1} of its biome)");
				}
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Hint targets: {targets.Count} guaranteed stations and summons, {fallbacks} on a fallback creature");
			Check.All(failures, "Targets without a frequent hint");
		}

		// Every level of a creature hint names its best biome
		private static void HintBiomeEveryLevel()
		{
			List<HintFact> facts = CreatureFacts().Where(f => f.Biomes.Count > 0).ToList();
			if (facts.Count == 0) Check.Skip("no creature hint with a known biome");
			List<string> failures = new();
			foreach (HintFact fact in facts)
			{
				string biome = BiomeSlots.BiomeDisplayName(fact.Biomes[0]);
				for (int level = 1; level <= HintFacts.MaxLevel; level++)
				{
					if (!HintFacts.Text(fact, level).Contains(biome)) failures.Add($"{HintFacts.ItemName(fact.Item)} level {level} without {biome}");
				}
				if (!HintFacts.Text(fact, 4).Contains(Codex.Interface.CodexStats.Percent(fact.Chance))) failures.Add($"{HintFacts.ItemName(fact.Item)} level 4 without its chance");
			}
			Check.All(failures, "Hint levels without the biome");
		}

		// Buying the last level of a creature or boss hint reveals the enemy: known in the codex with 0 kills, the item among
		// its drops, seen in its best biome. On an empty copy of the codex, restored afterwards
		private static void HintRevealDiscovers()
		{
			if (Main.netMode != NetmodeID.SinglePlayer) Check.Skip("single player only (the server records the reveal)");
			List<HintFact> facts = TargetsOrSkip().Select(t => HintFacts.For(t.Item)).Where(f => f != null && f.Kind is HintKind.Creature or HintKind.Boss).ToList();
			// Several enemies when there are (a guaranteed item), else any
			HintFact fact = facts.FirstOrDefault(f => f.Hosts.Count > 1) ?? facts.FirstOrDefault();
			if (fact == null) Check.Skip("no creature or boss hint in this world");
			// Reveals of the real codex first: they must not land in the copy
			CodexSystem.ProcessReveals(force: true);
			CodexData saved = CodexSystem.Data;
			try
			{
				CodexSystem.Data = new CodexData { Seed = saved.Seed, HasSeed = saved.HasSeed, RecipeStamp = saved.RecipeStamp, HasRecipeStamp = saved.HasRecipeStamp };
				string item = CodexNames.Item(fact.Item);
				// The purchase path: the hint entry, then the reveal once the facts are ready (forced here)
				CodexSystem.Submit(CodexEntry.Hint(item, HintFacts.MaxLevel - 1));
				CodexSystem.ProcessReveals(force: true);
				Check.Equal(0, CodexSystem.KnownNpcs().Count, "enemies known after level 3");
				CodexSystem.Submit(CodexEntry.Hint(item, HintFacts.MaxLevel));
				Check.Equal(1, CodexSystem.PendingRevealCount, "reveals waiting after level 4");
				CodexSystem.ProcessReveals(force: true);
				CodexSystem.RefreshIndex();
				List<int> known = CodexSystem.KnownNpcs();
				// A guaranteed item: every enemy named
				int expected = fact.Kind == HintKind.Creature && fact.Hosts.Count > 0 ? fact.Hosts.Distinct().Count() : 1;
				Check.That(known.Count == expected, $"{known.Count} enemies known after the reveal, expected {expected}");
				foreach (int npc in known)
				{
					string what = HintFacts.NpcName(npc);
					Check.Equal(0, CodexSystem.KillCount(npc), $"kills of the revealed {what}");
					Check.That(CodexSystem.DropsOf(npc).Any(d => d.item == fact.Item && d.count == 0), $"the item is not among the drops of {what}");
					Check.That(CodexSystem.NpcsDropping(fact.Item).Any(d => d.npc == npc), $"the item page does not list {what}");
					if (fact.Biomes.Count > 0)
					{
						Check.That(CodexSystem.BiomesWhereSeen(npc).Contains(fact.Biomes[0]), $"{what} not seen in {fact.Biomes[0]}");
						Check.Equal(1, CodexSystem.SightingCount(npc), $"sightings of the revealed {what}");
					}
				}
				// Saved and synced like any discovery
				TagCompound tag = new();
				CodexSystem.Data.Save(tag);
				Check.Equal(CodexSystem.Data.Signature(), CodexData.Load(tag).Signature(), "the reveal after a save");
				CodexData copy = new();
				foreach ((int count, byte[] bytes) in CodexSystem.SplitEntries(CodexSystem.Data.AllEntries(), CodexSystem.PacketBytes))
				{
					using BinaryReader reader = new(new MemoryStream(bytes));
					for (int i = 0; i < count; i++) copy.Apply(CodexData.Read(reader));
				}
				Check.Equal(CodexSystem.Data.Signature(), copy.Signature(), "the reveal after a network round trip");
			}
			finally
			{
				CodexSystem.Data = saved;
			}
		}

		// A guaranteed station's creature hint names exactly the enemies carrying it, the most met first, in the biome they
		// were chosen in: level 4 lists them with the chance on each, the craft trees shorten the list
		private static void HintHostsNamed()
		{
			List<HintTarget> targets = TargetsOrSkip().Where(t => GuaranteedSources.HostsOf(t.Item).Count() > 1).ToList();
			// Only the hints that lead to those enemies (another source may be reached first: a recipe, another creature)
			List<HintFact> facts = targets.Select(t => HintFacts.For(t.Item)).Where(f => f != null && f.Kind == HintKind.Creature && f.Hosts.Count > 0).ToList();
			if (facts.Count == 0) Check.Skip("no creature hint for an item carried by several enemies");
			List<string> failures = new();
			foreach (HintFact fact in facts)
			{
				string name = HintFacts.ItemName(fact.Item);
				List<int> hosts = GuaranteedSources.HostsOf(fact.Item).ToList();
				if (!fact.Hosts.SequenceEqual(hosts)) failures.Add($"{name}: names {string.Join(", ", fact.Hosts.Select(HintFacts.NpcName))}, carried by {string.Join(", ", hosts.Select(HintFacts.NpcName))}");
				if (fact.Npc != hosts[0]) failures.Add($"{name}: first enemy {HintFacts.NpcName(fact.Npc)}, expected {HintFacts.NpcName(hosts[0])}");
				int biome = GuaranteedSources.BiomeOf(fact.Item);
				if (biome >= 0 && (fact.Biomes.Count == 0 || fact.Biomes[0] != BiomeSlots.BiomeName(biome))) failures.Add($"{name}: biome {fact.Biomes.FirstOrDefault() ?? "none"}, placed in {BiomeSlots.BiomeName(biome)}");
				// Several enemies without a biome: none named (each one lives elsewhere)
				if (biome < 0 && hosts.Count > 1 && fact.Biomes.Count > 0) failures.Add($"{name}: names {fact.Biomes[0]} for enemies placed without a biome");
				string text = HintFacts.Text(fact, HintFacts.MaxLevel);
				foreach (int host in hosts.Take(hosts.Count <= HintFacts.NamedHosts + 1 ? hosts.Count : HintFacts.NamedHosts))
				{
					if (!text.Contains(HintFacts.NpcName(host))) failures.Add($"{name}: level 4 does not name {HintFacts.NpcName(host)}");
				}
				if (!text.Contains(CodexStats.Percent(fact.Chance))) failures.Add($"{name}: level 4 without the chance on each");
				// The same chance on every enemy named
				foreach (int host in hosts)
				{
					float chance = GuaranteedSources.For(host).Where(d => d.Item == fact.Item).Sum(d => d.Chance);
					float first = GuaranteedSources.For(hosts[0]).Where(d => d.Item == fact.Item).Sum(d => d.Chance);
					if (Math.Abs(chance - first) > 1e-5f) failures.Add($"{name}: {HintFacts.NpcName(host)} at {chance:P2}, {HintFacts.NpcName(hosts[0])} at {first:P2}");
				}
				if (!HintFacts.ShortHosts(fact).Contains(HintFacts.NpcName(hosts[0]))) failures.Add($"{name}: the short list misses {HintFacts.NpcName(hosts[0])}");
			}
			Check.All(failures, "Hints naming the enemies of a guaranteed item");
		}

		// The conditions a world-fixed check does not settle, as the hint must show them: every planned entry of the item at
		// the source needs a described condition (not one this world always meets), those of the best entry
		private static List<string> ExpectedConditions(HintFact fact)
		{
			if (fact.Kind is not (HintKind.Creature or HintKind.Boss or HintKind.Bag) || fact.Kind == HintKind.Creature && fact.Hosts.Count > 0) return new List<string>();
			List<PlannedDrop> plan = fact.Kind == HintKind.Bag ? BagPlan.Plan(fact.Bag, lucky: true) : RandomNpcDropRule.Plan(fact.Npc, Main.expertMode, lucky: true);
			float minChance = ProgressionConfig.Instance.MinFindableChance / 100f;
			List<PlannedDrop> entries = plan.Where(d => d.Item == fact.Item && d.Chance >= minChance && Findability.WorldAllows(d.Conditions)).ToList();
			// Allowed in this world, a difficulty, evil or legacy boss condition is always met
			List<string> Described(PlannedDrop drop) => (drop.Conditions ?? new List<Terraria.GameContent.ItemDropRules.IItemDropRuleCondition>())
				.Where(c => c is not (Terraria.GameContent.ItemDropRules.Conditions.IsExpert or Terraria.GameContent.ItemDropRules.Conditions.IsMasterMode
					or Terraria.GameContent.ItemDropRules.Conditions.IsCrimson or Terraria.GameContent.ItemDropRules.Conditions.IsCorruption
					or Terraria.GameContent.ItemDropRules.Conditions.NotExpert or Terraria.GameContent.ItemDropRules.Conditions.NotMasterMode
					or Terraria.GameContent.ItemDropRules.Conditions.IsCrimsonAndNotExpert or Terraria.GameContent.ItemDropRules.Conditions.IsCorruptionAndNotExpert
					or Terraria.GameContent.ItemDropRules.Conditions.LegacyHack_IsBossAndExpert or Terraria.GameContent.ItemDropRules.Conditions.LegacyHack_IsBossAndNotExpert))
				.Select(c => c.GetConditionDescription()).Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()).Distinct().ToList();
			if (entries.Count == 0 || entries.Any(d => Described(d).Count == 0)) return new List<string>();
			return Described(entries.OrderByDescending(d => d.Chance).First());
		}

		// Every source says when it works: the drop conditions at levels 2 and 4 and on the tree lines, the time of a creature
		// met at one time only, the weather or event of one met only then, the times of several enemies met at different
		// times; recipe conditions, hardmode ores (an altar) and critters' times on the tree lines
		private static void HintConditionsShown()
		{
			List<HintTarget> targets = TargetsOrSkip();
			List<string> failures = new();
			int conditions = 0, times = 0, treeLines = 0;
			foreach (HintTarget target in targets)
			{
				HintFact fact = HintFacts.For(target.Item);
				if (fact == null || fact.Kind == HintKind.World) continue;
				string name = $"{HintFacts.ItemName(target.Item)} ({fact.Kind})";
				string level2 = HintFacts.Text(fact, 2), level4 = HintFacts.Text(fact, 4);
				foreach (string condition in ExpectedConditions(fact))
				{
					conditions++;
					if (!level2.Contains(condition)) failures.Add($"{name}: level 2 without \"{condition}\"");
					if (!level4.Contains(condition)) failures.Add($"{name}: level 4 without \"{condition}\"");
				}
				if (fact.Kind == HintKind.Creature && fact.Time != SlotTime.Any && !HintFacts.MixedTimes(fact))
				{
					times++;
					if (!level4.Contains(CodexText.Get("Hint.Time." + fact.Time))) failures.Add($"{name}: level 4 does not say {fact.Time}");
				}
				if (HintFacts.MixedTimes(fact))
				{
					times++;
					foreach (SlotTime time in fact.HostTimes.Distinct())
					{
						if (!level4.Contains(CodexText.Get("Hint.Time." + time))) failures.Add($"{name}: level 4 does not group its enemies {time}");
					}
				}
				if (fact.Conditional)
				{
					conditions++;
					if (!level4.Contains(CodexText.Get("Hint.When.Conditional"))) failures.Add($"{name}: level 4 without its weather or event");
				}
			}
			foreach (HintTarget target in RecipeTargets())
			{
				foreach (HintTreeNode node in HintFacts.SourceTree(target.Item))
				{
					string part = $"{HintFacts.ItemName(target.Item)} > {HintFacts.TreeName(node)}";
					string line = HintFacts.TreeSource(node);
					List<string> expected = new();
					if (node.Leaf == HintTreeLeaf.Fact && node.Fact != null)
					{
						expected.AddRange(ExpectedConditions(node.Fact));
						if (node.Fact.Kind == HintKind.Creature && node.Fact.Time != SlotTime.Any && !HintFacts.MixedTimes(node.Fact)) expected.Add(CodexText.Get("Hint.When.Short." + node.Fact.Time));
						if (node.Fact.Conditional) expected.Add(CodexText.Get("Hint.When.Short.Conditional"));
					}
					if (node.Leaf == HintTreeLeaf.Recipe && node.Fact != null && HintFacts.RecipeConditions(node.Fact).Length > 0) expected.Add(HintFacts.RecipeConditions(node.Fact));
					if (node.Leaf == HintTreeLeaf.Mined && OreMap.IsHardmodePairOre(node.Ore)) expected.Add(CodexText.Get("Hint.When.Short.Altar"));
					if (node.Leaf == HintTreeLeaf.Caught && node.Time != SlotTime.Any) expected.Add(CodexText.Get("Hint.When.Short." + node.Time));
					foreach (string when in expected)
					{
						treeLines++;
						if (!line.Contains(when)) failures.Add($"{part}: \"{line}\" without \"{when}\"");
					}
				}
			}
			// A made-up creature source dropping only on windy days (and the legacy boss check this world meets, which says
			// nothing): the drop-condition path is tested even when no target of this world needs one
			List<string> windy = HintFacts.ConditionTexts(new List<Terraria.GameContent.ItemDropRules.IItemDropRuleCondition>
			{
				new Terraria.GameContent.ItemDropRules.Conditions.WindyEnoughForKiteDrops(),
				Main.expertMode ? new Terraria.GameContent.ItemDropRules.Conditions.LegacyHack_IsBossAndExpert() : new Terraria.GameContent.ItemDropRules.Conditions.LegacyHack_IsBossAndNotExpert()
			});
			if (windy.Count != 1) failures.Add($"windy day and legacy boss conditions: {windy.Count} texts ({string.Join("; ", windy)}), expected the windy day only");
			else
			{
				HintFact synthetic = new()
				{
					Item = ItemID.WorkBench, Kind = HintKind.Creature, Npc = NPCID.BlueSlime, Tier = 0, Chance = 0.05f, Movement = 0, Conditions = windy,
					Biomes = new List<string> { BiomeSlots.BiomeName((int)SpawnBiome.Forest) }
				};
				conditions++;
				if (!HintFacts.Text(synthetic, 2).Contains(windy[0])) failures.Add($"synthetic windy source: level 2 without \"{windy[0]}\"");
				if (!HintFacts.Text(synthetic, 4).Contains(windy[0])) failures.Add($"synthetic windy source: level 4 without \"{windy[0]}\"");
				if (!HintFacts.FactWhen(synthetic).Contains(windy[0])) failures.Add("synthetic windy source: no short condition");
				string tree = HintFacts.TreeSource(new HintTreeNode { Item = ItemID.WorkBench, Stack = 1, Depth = 1, Leaf = HintTreeLeaf.Fact, Fact = synthetic });
				if (!tree.Contains(windy[0])) failures.Add($"synthetic windy source: \"{tree}\" without \"{windy[0]}\"");
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Hint conditions: {conditions} conditions, {times} times in sentences, {treeLines} on tree lines");
			Check.All(failures, "Hints hiding when their source works");
		}

		// A boss summon is offered when its boss is the next step, whatever its rarity: the Suspicious Looking Eye from the
		// start, the Mechanical Eye in hardmode, the Lihzahrd Power Cell after Plantera (it was offered long before Golem)
		private static void HintSummonTiers()
		{
			List<string> failures = new();
			foreach ((int item, Tier expected) in new[] { (ItemID.SuspiciousLookingEye, Tier.PreBoss), (ItemID.MechanicalEye, Tier.Hardmode), (ItemID.LihzahrdPowerCell, Tier.PostPlantera),
				(ItemID.SlimeCrown, Tier.PreBoss), (ItemID.TruffleWorm, Tier.PostPlantera), (ItemID.CelestialSigil, Tier.PostCultist) })
			{
				int tier = Protection.SummonTier(item);
				if (tier != (int)expected) failures.Add($"{HintFacts.ItemName(item)}: tier {tier}, expected {(int)expected}");
				HintTarget target = HintFacts.Ready ? HintFacts.RawTarget(item) : null;
				if (target != null && target.Tier != (int)expected) failures.Add($"{HintFacts.ItemName(item)}: offered from tier {target.Tier}, expected {(int)expected}");
			}
			// Event summons keep their own moment
			if (Protection.SummonTier(ItemID.PirateMap) < (int)Tier.Hardmode) failures.Add($"Pirate Map at tier {Protection.SummonTier(ItemID.PirateMap)}");
			if (Protection.SummonTier(ItemID.GuideVoodooDoll) < (int)Tier.PostEvilBoss) failures.Add($"Guide Voodoo Doll at tier {Protection.SummonTier(ItemID.GuideVoodooDoll)}");
			Check.All(failures, "Summon tiers");
		}

		// Targets more than one tier ahead are listed (greyed, with when they come) but never sold: the shop refuses them before
		// taking coins, the server refuses them too; every target with a source is either on sale, known or listed as locked
		private static void HintLockedShown()
		{
			if (!HintFacts.Ready) Check.Skip("no world");
			List<HintTarget> locked = HintFacts.LockedTargets();
			if (locked.Count == 0) Check.Skip("nothing is more than one tier ahead");
			List<string> failures = new();
			foreach (bool bosses in new[] { false, true })
			{
				HashSet<int> onSale = HintPanelState.TabTargets(false, bosses).Select(t => t.Item).ToHashSet();
				HashSet<int> known = HintPanelState.TabTargets(true, bosses).Select(t => t.Item).ToHashSet();
				HashSet<int> later = HintPanelState.LockedTabTargets(bosses).Select(t => t.Item).ToHashSet();
				foreach (HintTarget target in HintFacts.AllTargets().Where(t => (t.Kind == HintTargetKind.Summon) == bosses))
				{
					string name = HintFacts.ItemName(target.Item);
					bool isLocked = HintFacts.IsLocked(target);
					if (isLocked && onSale.Contains(target.Item)) failures.Add($"{name} (tier {target.Tier}) on sale");
					if (isLocked && !later.Contains(target.Item) && !known.Contains(target.Item)) failures.Add($"{name} (tier {target.Tier}) not shown");
					if (!isLocked && later.Contains(target.Item)) failures.Add($"{name} (tier {target.Tier}) shown as locked");
				}
			}
			int unlocked = ProgressionSystem.UnlockedTier;
			foreach (HintTarget target in locked)
			{
				string name = HintFacts.ItemName(target.Item);
				if (HintShop.Check(target, CodexSystem.HintLevel(target.Item) + 1) == HintPurchase.Bought) failures.Add($"{name}: the shop would sell it");
				CodexEntry entry = CodexEntry.Hint(CodexNames.Item(target.Item), CodexSystem.Data.HintLevel(CodexNames.Item(target.Item)) + 1);
				if (CodexSystem.AcceptHint(entry, CodexSystem.Data, unlocked)) failures.Add($"{name}: the server would accept it");
				if (!Language.Exists(CodexText.Prefix + "Hint.Unlock." + (Tier)Tiers.Clamp(target.Tier - 1))) failures.Add($"{name}: no text for when it comes");
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Hint locked targets: {locked.Count} ({string.Join(", ", locked.Take(6).Select(t => HintFacts.ItemName(t.Item) + " " + HintFacts.UnlockText(t)))})");
			Check.All(failures, "Locked hint targets");
		}

		// The planned shares of every biome sum to 1, on several seeds and both world evils; a rare owner gets less of a
		// biome than a common one (single-slot owners without extra in the same biome)
		private static void PlannedShareSums()
		{
			List<string> failures = new();
			int compared = 0;
			for (int i = 0; i < 6; i++)
			{
				int seed = 2000 + i * 7919;
				foreach (WorldEvil evil in new[] { WorldEvil.Corruption, WorldEvil.Crimson })
				{
					BiomeSlotMap map = BiomeSlots.Build(seed, 8f, evil);
					foreach (int biome in map.Slots.Where(s => !s.Critter && !s.AbsentEvil).Select(s => s.Biome).Distinct())
					{
						Dictionary<int, double> shares = BiomeSlots.SharesIn(map, biome);
						double sum = shares.Values.Sum();
						if (Math.Abs(sum - 1) > 1e-6) failures.Add($"seed {seed} ({evil}): {BiomeSlots.BiomeName(biome)} sums to {sum:0.0000}");
						List<BiomeSlot> alone = map.Slots.Where(s => s.Biome == biome && !s.Critter && !s.AbsentEvil && s.Extra == 0 && s.EvilShared == null && !s.Conditional
							&& map.Slots.Count(o => o.Biome == biome && !o.AbsentEvil && BiomeSlots.Spawners(o).Any(id => BiomeSlots.TypeOf(id) == BiomeSlots.TypeOf(s.Owner))) == 1).ToList();
						BiomeSlot rare = alone.FirstOrDefault(s => s.Rare);
						BiomeSlot common = alone.FirstOrDefault(s => !s.Rare);
						if (rare == null || common == null) continue;
						compared++;
						double r = shares.GetValueOrDefault(BiomeSlots.TypeOf(rare.Owner)), c = shares.GetValueOrDefault(BiomeSlots.TypeOf(common.Owner));
						if (r >= c) failures.Add($"seed {seed}: in {BiomeSlots.BiomeName(biome)}, rare {Lang.GetNPCNameValue(BiomeSlots.TypeOf(rare.Owner))} {r:P1} >= common {c:P1}");
					}
				}
			}
			if (compared == 0) failures.Add("no biome with both a rare and a common owner to compare");
			Check.All(failures, "Planned shares");
		}

		// Targets come by tier, then by the tier of their hinted source; the two tabs split them: to buy (offered now, not
		// fully known) and known (the last level, bought or found)
		private static void HintSortedByProgress()
		{
			List<HintTarget> sorted = HintFacts.ByProgress(TargetsOrSkip());
			List<string> failures = new();
			for (int i = 1; i < sorted.Count; i++)
			{
				HintTarget a = sorted[i - 1], b = sorted[i];
				int fa = HintFacts.For(a.Item)?.Tier ?? Findability.Never, fb = HintFacts.For(b.Item)?.Tier ?? Findability.Never;
				if (a.Tier > b.Tier || a.Tier == b.Tier && fa > fb) failures.Add($"{HintFacts.ItemName(a.Item)} (tier {a.Tier}/{fa}) before {HintFacts.ItemName(b.Item)} (tier {b.Tier}/{fb})");
			}
			Check.That(HintFacts.ByProgress(sorted.AsEnumerable().Reverse()).Select(t => t.Item).SequenceEqual(sorted.Select(t => t.Item)), "the order depends on the input order");
			List<HintTarget> toBuy = HintPanelState.TabTargets(false), known = HintPanelState.TabTargets(true);
			HashSet<int> offered = HintFacts.Targets().Select(t => t.Item).ToHashSet();
			foreach (HintTarget t in toBuy)
			{
				if (!offered.Contains(t.Item)) failures.Add($"{HintFacts.ItemName(t.Item)} to buy but not offered");
				if (CodexSystem.ShownHintLevel(t.Item) >= HintFacts.MaxLevel) failures.Add($"{HintFacts.ItemName(t.Item)} to buy but fully known");
			}
			foreach (HintTarget t in known)
			{
				if (CodexSystem.ShownHintLevel(t.Item) < HintFacts.MaxLevel) failures.Add($"{HintFacts.ItemName(t.Item)} known at level {CodexSystem.ShownHintLevel(t.Item)}");
			}
			if (toBuy.Any(t => known.Contains(t))) failures.Add("a target in both tabs");
			Check.All(failures, "Hints out of order");
		}

		// A target obtained in the world (not only seen in a recipe or a chest) counts as fully known: shown at the last
		// level, never sold, and the server records the last level (and its reveal) for free. On an empty copy of the codex,
		// restored afterwards
		private static void HintFoundUnlocks()
		{
			if (Main.netMode != NetmodeID.SinglePlayer) Check.Skip("single player only (the server records it)");
			TargetsOrSkip();
			HintTarget target = HintFacts.Targets().FirstOrDefault(t => HintFacts.For(t.Item) != null);
			if (target == null) Check.Skip("no target on sale now");
			CodexSystem.ProcessReveals(force: true);
			CodexData saved = CodexSystem.Data;
			try
			{
				CodexSystem.Data = new CodexData { Seed = saved.Seed, HasSeed = saved.HasSeed, RecipeStamp = saved.RecipeStamp, HasRecipeStamp = saved.HasRecipeStamp };
				string name = CodexNames.Item(target.Item);
				Check.Equal(0, CodexSystem.ShownHintLevel(target.Item), "level before it is found");
				CodexSystem.Submit(CodexEntry.Seen(name));
				CodexSystem.ProcessReveals(force: true);
				Check.Equal(0, CodexSystem.Data.HintLevel(name), "level once only seen");
				Check.That(!CodexSystem.IsFound(target.Item), "seen counts as found");
				CodexSystem.Submit(CodexEntry.Obtained(name));
				Check.That(CodexSystem.IsFound(target.Item), "obtained does not count as found");
				Check.Equal(HintFacts.MaxLevel, CodexSystem.ShownHintLevel(target.Item), "level shown once found");
				CodexSystem.ProcessReveals(force: true);
				Check.Equal(HintFacts.MaxLevel, CodexSystem.Data.HintLevel(name), "level recorded once found");
				Check.Equal(0, CodexSystem.PendingFoundCount, "found items waiting");
				HintFact fact = HintFacts.For(target.Item);
				if (fact.Kind is HintKind.Creature or HintKind.Boss) Check.That(CodexSystem.KnownNpcs().Contains(CodexSystem.RevealKey(fact)), "the source of a found target is not revealed");
				if (fact.Kind == HintKind.Recipe) Check.That(CodexSystem.Data.Recipes.Contains(RecipeSystem.RecipeKey(fact.Recipe)), "the recipe of a found target is not shown");
			}
			finally
			{
				CodexSystem.Data = saved;
			}
		}

		// Clicking a hint opens the codex where it leads: the enemy or boss once known, the craft tree of a recipe, the bag,
		// else the target's own page; and those pages build
		private static void HintClickOpensCodex()
		{
			List<HintTarget> targets = TargetsOrSkip();
			List<string> failures = new();
			List<CodexPage> pages = new();
			foreach (HintTarget target in targets)
			{
				int item = target.Item;
				HintFact fact = HintFacts.For(item);
				string name = HintFacts.ItemName(item);
				if (!HintNavigation.Target(item, fact, HintFacts.MaxLevel - 1, new List<int>()).Equals(CodexPage.ItemPage(item))) failures.Add($"{name}: level 3 does not lead to its page");
				List<int> known = fact != null && fact.Kind is HintKind.Creature or HintKind.Boss ? new List<int> { CodexSystem.RevealKey(fact) } : new List<int>();
				CodexPage page = HintNavigation.Target(item, fact, HintFacts.MaxLevel, known);
				CodexPage expected = fact?.Kind switch
				{
					HintKind.Creature or HintKind.Boss => CodexPage.NpcPage(CodexSystem.RevealKey(fact)),
					HintKind.Recipe => CodexPage.TreePage(item),
					HintKind.Bag when fact.Bag > 0 => CodexPage.ItemPage(fact.Bag),
					_ => CodexPage.ItemPage(item)
				};
				if (!page.Equals(expected)) failures.Add($"{name}: {page.Kind} {page.Id}, expected {expected.Kind} {expected.Id}");
				if (fact != null && fact.Kind is HintKind.Creature or HintKind.Boss && !HintNavigation.Target(item, fact, HintFacts.MaxLevel, new List<int>()).Equals(CodexPage.ItemPage(item)))
				{
					failures.Add($"{name}: leads to an enemy not known yet");
				}
				if (!pages.Contains(page) && pages.Count < 8) pages.Add(page);
			}
			CodexUIState state = JournalSystem.State;
			if (state != null && state.Content != null)
			{
				var saved = state.SaveNavigation();
				try
				{
					state.ClearFilters();
					foreach (CodexPage page in pages)
					{
						try
						{
							state.Navigate(page);
							state.BuildNow();
						}
						catch (Exception e)
						{
							failures.Add($"{page.Kind} {page.Id}: {e.GetType().Name} {e.Message}");
						}
					}
				}
				finally
				{
					state.RestoreNavigation(saved);
				}
			}
			Check.All(failures, "Hints leading to the wrong codex page");
		}

		private static List<HintTarget> TargetsOrSkip()
		{
			if (!HintFacts.Ready) Check.Skip("recipes not loaded");
			List<HintTarget> targets = HintFacts.AllTargets();
			if (targets.Count == 0) Check.Skip("no hint target with these settings");
			return targets;
		}

		// Level 1, 2 and 4 of every target say what the planners do: the host really carries it, the recipe really makes it
		private static void HintFactsTrue()
		{
			List<HintTarget> targets = TargetsOrSkip();
			List<string> failures = new();
			RecipePlan plan = RecipePlanner.Get();
			Check.That(targets.Any(t => t.Kind == HintTargetKind.Station), "no station among the targets");
			foreach (HintTarget target in targets)
			{
				int item = target.Item;
				HintFact fact = HintFacts.For(item);
				string name = $"{HintFacts.ItemName(item)} ({fact.Kind})";
				switch (fact.Kind)
				{
					case HintKind.Creature:
					case HintKind.Boss:
						if (!RandomNpcDropRule.Plan(fact.Npc, Main.expertMode, lucky: true).Any(d => d.Item == item)) failures.Add($"{name}: {HintFacts.NpcName(fact.Npc)} does not drop it");
						break;
					case HintKind.Recipe:
						if (Main.recipe[fact.Recipe].Disabled || plan.Results[fact.Recipe].type != item) failures.Add($"{name}: recipe {fact.Recipe} does not make it");
						if (!fact.Ingredients.SequenceEqual(HintFacts.PlannedIngredients(fact.Recipe))) failures.Add($"{name}: ingredients differ from the plan");
						if (!fact.Tiles.SequenceEqual(HintFacts.PlannedTiles(fact.Recipe).Where(t => t >= 0))) failures.Add($"{name}: stations differ from the plan");
						if (!HintFacts.Text(fact, 1).Contains(CodexText.Get("Hint.Station." + HintFacts.StationGroup(fact.Tiles)))) failures.Add($"{name}: level 1 without the station kind");
						foreach (int tile in fact.Tiles)
						{
							if (!HintFacts.Text(fact, 4).Contains(HintFacts.StationName(tile))) failures.Add($"{name}: level 4 without station {HintFacts.StationName(tile)}");
						}
						break;
					case HintKind.Bag:
						if (!BagPlan.Plan(fact.Bag, lucky: true).Any(d => d.Item == item)) failures.Add($"{name}: {HintFacts.ItemName(fact.Bag)} does not hold it");
						break;
					case HintKind.Pot:
						if (!GuaranteedSources.PotItems(Tiers.Max).Contains(item)) failures.Add($"{name}: not in the pot items");
						break;
					case HintKind.Chest:
						if (!ChestSystem.StationInChests(item)) failures.Add($"{name}: not hidden in chests");
						break;
					case HintKind.Shop:
						bool sold = NPCShopDatabase.AllShops.OfType<NPCShop>().Any(s => s.NpcType == fact.Npc && s.Entries.Any(e => e?.Item?.type == item));
						if (!sold) failures.Add($"{name}: {HintFacts.NpcName(fact.Npc)} does not sell it");
						break;
				}
				// Level 2: the biomes named are places where the host spawns
				if (fact.Kind == HintKind.Creature && BiomeSlots.Active)
				{
					int type = BiomeSlots.TypeOf(fact.Npc);
					foreach (string biome in fact.Biomes)
					{
						bool spawns = BiomeSlots.Current.Slots.Any(s => BiomeSlots.BiomeName(s.Biome) == biome && HintFacts.SpawnsIn(s, type));
						if (!spawns) failures.Add($"{name}: {HintFacts.NpcName(fact.Npc)} never spawns in {biome}");
					}
					if (fact.Biomes.Count > 0 && !HintFacts.Text(fact, 2).Contains(BiomeSlots.BiomeDisplayName(fact.Biomes[0]))) failures.Add($"{name}: level 2 without its biome");
				}
				// Level 4: the name of the host, the bag or the item
				string named = fact.Kind switch
				{
					HintKind.Creature or HintKind.Boss or HintKind.Shop => HintFacts.NpcName(fact.Npc),
					HintKind.Bag => HintFacts.ItemName(fact.Bag),
					HintKind.Recipe => null,
					_ => HintFacts.ItemName(item)
				};
				if (named != null && !HintFacts.Text(fact, 4).Contains(named)) failures.Add($"{name}: level 4 does not name {named}");
			}
			Check.All(failures, "Hints not matching the planners");
		}

		// base x (1 + 1.5 x tier) x 3^(level - 1): 2, 6, 18, 54 gold at tier 0, 14 to 378 gold at tier 4
		private static void HintPriceScales()
		{
			long gold = HintFacts.CopperPerGold;
			Check.Equal(2 * gold, HintFacts.Price(0, 1, 2), "tier 0 level 1");
			Check.Equal(54 * gold, HintFacts.Price(0, 4, 2), "tier 0 level 4");
			Check.Equal(14 * gold, HintFacts.Price(4, 1, 2), "tier 4 level 1");
			Check.Equal(378 * gold, HintFacts.Price(4, 4, 2), "tier 4 level 4");
			Check.Equal(0L, HintFacts.Price(3, 2, 0), "free hints");
			for (int tier = 0; tier < Tiers.Count; tier++)
			{
				for (int level = 1; level <= HintFacts.MaxLevel; level++)
				{
					long price = HintFacts.Price(tier, level, 5);
					if (level > 1) Check.That(price > HintFacts.Price(tier, level - 1, 5), $"tier {tier}: level {level} not dearer");
					if (tier > 0) Check.That(price > HintFacts.Price(tier - 1, level, 5), $"level {level}: tier {tier} not dearer");
				}
			}
			Check.Equal(CodexText.Get("Hint.Coin.Gold", 2), HintFacts.PriceText(2 * gold), "price text");
		}

		private static Item[][] Containers(Player player) => new[] { player.inventory, player.bank.item, player.bank2.item, player.bank3.item, player.bank4.item };

		private static long Coins(Player player) => Containers(player).Sum(items => Utils.CoinsCount(out _, items));

		// Buying takes the coins, raises the level by one, and is refused when the coins are short
		private static void HintPurchaseFlow()
		{
			if (Main.netMode != NetmodeID.SinglePlayer) Check.Skip("single player only");
			if (ExtrasConfig.Instance.HintBasePrice <= 0) Check.Skip("hints are free");
			TargetsOrSkip();
			HintTarget target = HintFacts.Targets().FirstOrDefault();
			if (target == null) Check.Skip("no target offered now");
			Player player = Main.LocalPlayer;
			Item[][] containers = Containers(player);
			Item[][] saved = containers.Select(items => items.Select(i => i.Clone()).ToArray()).ToArray();
			CodexData savedData = CodexSystem.Data;
			try
			{
				CodexSystem.Data = new CodexData { Seed = SeedSystem.WorldSeed, HasSeed = true };
				foreach (Item[] items in containers)
				{
					for (int i = 0; i < items.Length; i++)
					{
						if (!items[i].IsAir && ItemID.Sets.CommonCoin[items[i].type]) items[i].TurnToAir();
					}
				}
				long price = HintFacts.Price(target, 1);
				GiveCoins(player, price - 1);
				Check.Equal(HintPurchase.TooPoor, HintShop.TryBuy(player, target, 1, out _), "purchase without enough coins");
				Check.Equal(0, CodexSystem.HintLevel(target.Item), "level after a refused purchase");
				Check.Equal(price - 1, Coins(player), "coins after a refused purchase");
				GiveCoins(player, price);
				Check.Equal(HintPurchase.Bought, HintShop.TryBuy(player, target, 1, out long paid), "purchase");
				Check.Equal(price, paid, "price paid");
				Check.Equal(0L, Coins(player), "coins left");
				Check.Equal(1, CodexSystem.HintLevel(target.Item), "level after the purchase");
				GiveCoins(player, HintFacts.Price(target, 3) * 2);
				Check.Equal(HintPurchase.NotNext, HintShop.TryBuy(player, target, 3, out _), "a level bought out of order");
				Check.Equal(HintPurchase.NotNext, HintShop.TryBuy(player, target, 1, out _), "the same level bought twice");
				Check.Equal(HintFacts.Price(target, 3) * 2, Coins(player), "coins after refused purchases");
				Check.Equal(1, CodexSystem.HintLevel(target.Item), "level after refused purchases");
			}
			finally
			{
				for (int c = 0; c < containers.Length; c++)
				{
					for (int i = 0; i < containers[c].Length && i < saved[c].Length; i++) containers[c][i] = saved[c][i];
				}
				CodexSystem.Data = savedData;
			}
		}

		// Coins in the coin slots (50 to 53), as few as possible
		private static void GiveCoins(Player player, long copper)
		{
			int[] split = Utils.CoinsSplit(copper);
			int[] coins = { ItemID.CopperCoin, ItemID.SilverCoin, ItemID.GoldCoin, ItemID.PlatinumCoin };
			for (int i = 0; i < 4; i++)
			{
				Item item = new();
				if (split[i] > 0)
				{
					item.SetDefaults(coins[i]);
					item.stack = split[i];
				}
				player.inventory[50 + i] = item;
			}
		}

		// Hint levels survive the codex wire format, the save, and the server's checks
		private static void HintSyncRoundTrip()
		{
			List<HintTarget> targets = TargetsOrSkip();
			string bench = CodexNames.Item(ItemID.WorkBench);
			CodexData data = new() { Seed = 77, HasSeed = true };
			Check.That(data.Apply(CodexEntry.Hint(bench, 2)), "level 2 not applied");
			Check.That(!data.Apply(CodexEntry.Hint(bench, 1)), "a lower level applied");
			Check.That(!data.Apply(CodexEntry.Hint(bench, 5)), "a level above the last applied");
			Check.That(data.Apply(CodexEntry.Hint(bench, 3)), "level 3 not applied");
			Check.Equal(3, data.HintLevel(bench), "level");

			MemoryStream stream = new();
			BinaryWriter writer = new(stream);
			int written = data.AllEntries().Count(e => CodexData.Write(writer, e));
			writer.Flush();
			stream.Position = 0;
			BinaryReader reader = new(stream);
			CodexData copy = new() { Seed = 77, HasSeed = true };
			for (int i = 0; i < written; i++) copy.Apply(CodexData.Read(reader));
			Check.Equal(stream.Length, stream.Position, "bytes read");
			Check.Equal(3, copy.HintLevel(bench), "level after sync");
			Check.Equal(data.Signature(), copy.Signature(), "full sync");

			TagCompound tag = new();
			data.Save(tag);
			Check.Equal(3, CodexData.Load(tag).HintLevel(bench), "level after save and load");
			Check.That(CodexSystem.Merge(new[] { CodexEntry.Hint(bench, 1), CodexEntry.Hint(bench, 1), CodexEntry.Hint(bench, 2) }).Count == 2, "merge");

			// The server takes the next level of a target offered now, nothing else
			HintTarget target = targets[0];
			string name = CodexNames.Item(target.Item);
			CodexData server = new() { Seed = 77, HasSeed = true };
			Check.That(CodexSystem.AcceptHint(CodexEntry.Hint(name, 1), server, target.Tier), "first level refused");
			Check.That(!CodexSystem.AcceptHint(CodexEntry.Hint(name, 2), server, target.Tier), "second level accepted before the first");
			server.Apply(CodexEntry.Hint(name, 1));
			Check.That(CodexSystem.AcceptHint(CodexEntry.Hint(name, 2), server, target.Tier), "second level refused");
			// A target of tier 2 or more is not offered two tiers below
			HintTarget late = targets.FirstOrDefault(t => t.Tier >= 2);
			if (late != null) Check.That(!CodexSystem.AcceptHint(CodexEntry.Hint(CodexNames.Item(late.Item), 1), new CodexData(), late.Tier - 2), "a target not offered yet accepted");
			Check.That(!CodexSystem.AcceptHint(CodexEntry.Hint(CodexNames.Item(ItemID.DirtBlock), 1), server, Tiers.Max), "an item that is no target accepted");
		}

		// Every sentence exists in this language, and the hints of every target are full sentences (no key, no placeholder)
		private static void HintTextsResolved()
		{
			List<HintTarget> targets = TargetsOrSkip();
			List<string> failures = new();
			foreach (string key in HintFacts.AllTextKeys())
			{
				if (!Language.Exists(CodexText.Prefix + key)) failures.Add("missing " + key);
			}
			foreach (string key in new[] { "Hint.Button", "Hint.Intro", "Hint.Nothing", "Hint.TooPoor", "Hint.PanelTitle", "Hint.Buy", "Hint.Repeat", "Hint.Level",
				"Hint.Found", "Hint.Unknown", "Hint.NoHint", "Hint.NotAsked", "Hint.TabIntro", "Hint.About", "Hint.PickTarget", "Hint.Money", "Hint.Free", "Hint.NoStation",
				"Hint.Coin.Copper", "Hint.Coin.Silver", "Hint.Coin.Gold", "Hint.Coin.Platinum", "Hint.Syncing", "Hint.Refused", "Hint.Conditions", "TabHints" })
			{
				if (!Language.Exists(CodexText.Prefix + key)) failures.Add("missing " + key);
			}
			foreach (HintTarget target in targets)
			{
				HintFact fact = HintFacts.For(target.Item);
				for (int level = 1; level <= HintFacts.MaxLevel; level++)
				{
					string text = HintFacts.Text(fact, level);
					if (string.IsNullOrWhiteSpace(text) || text.Contains("Mods.") || text.Contains("Hint.") || text.Contains('{') || text.Contains("Bestiary_"))
					{
						failures.Add($"{HintFacts.ItemName(target.Item)} level {level}: \"{text}\"");
					}
				}
			}
			Check.All(failures, $"Hint texts not resolved ({Language.ActiveCulture.Name})");
		}
	}
}

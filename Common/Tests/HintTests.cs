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
			TestRunner.Register("PlannedShareSums", PlannedShareSums);
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

		// A creature hint leads to an enemy met often (at least 5 % of the steady spawns of its best biome, no rare enemy)
		// that drops the item with the chance the codex shows (1 % or more). Fallbacks are counted by HintTargetsHaveSource
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
				if (ContentSamples.NpcsByNetId[fact.Npc].rarity > 0) failures.Add($"{name}: a rare enemy");
				if (fact.Chance < 0.01f - 1e-6f) failures.Add($"{name}: {fact.Chance:P2} chance");
				// Recomputed from the enemy's lucky plan: every planned entry of the item together
				float missing = 1f;
				foreach (PlannedDrop drop in RandomNpcDropRule.Plan(fact.Npc, Main.expertMode, lucky: true))
				{
					if (drop.Item == fact.Item) missing *= 1f - Math.Clamp(drop.Chance, 0f, 1f);
				}
				if (Math.Abs((1f - missing) - fact.Chance) > 1e-4f) failures.Add($"{name}: states {fact.Chance:P2}, planned {1f - missing:P2}");
				if (!shares || !BiomeSlots.HasAnySlot(type)) continue;
				// Judged at the requirement's tier for a guaranteed host, like the guarantees
				GuaranteedSources.Requirement requirement = GuaranteedSources.Requirements.FirstOrDefault(r => r.Item == fact.Item);
				int tier = requirement.Item == fact.Item && GuaranteedSources.HostsOf(fact.Item).Contains(fact.Npc) ? Math.Max(fact.Tier, requirement.Tier) : fact.Tier;
				Dictionary<int, double> steady = SteadyFromSlots(type, tier);
				double best = steady.Values.DefaultIfEmpty(0).Max();
				if (best < GuaranteedSources.MinHostShare - 0.005) failures.Add($"{name}: {best:P1} of the steady spawns of its best biome");
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

		// Every station or summon target with guaranteed hosts has a creature or recipe hint; with the default settings,
		// never a creature kept only because nothing else than a chest or a pot gives it
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
				if (fact == null || fact.Kind is not (HintKind.Creature or HintKind.Recipe)) failures.Add($"{name}: {(fact == null ? "no hint" : fact.Kind.ToString())}");
				else if (fact.Fallback)
				{
					fallbacks++;
					if (defaults) failures.Add($"{name}: only a rare source ({HintFacts.NpcName(fact.Npc)}, {fact.Chance:P1}, {fact.Share:P1} of its biome)");
				}
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Hint targets: {targets.Count} guaranteed stations and summons, {fallbacks} on a fallback creature");
			Check.All(failures, "Targets without a frequent hint");
		}

		// Levels 2 to 4 of a creature hint name its best biome
		private static void HintBiomeEveryLevel()
		{
			List<HintFact> facts = CreatureFacts().Where(f => f.Biomes.Count > 0).ToList();
			if (facts.Count == 0) Check.Skip("no creature hint with a known biome");
			List<string> failures = new();
			foreach (HintFact fact in facts)
			{
				string biome = BiomeSlots.BiomeDisplayName(fact.Biomes[0]);
				for (int level = 2; level <= HintFacts.MaxLevel; level++)
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
			HintFact fact = TargetsOrSkip().Select(t => HintFacts.For(t.Item)).FirstOrDefault(f => f != null && f.Kind is HintKind.Creature or HintKind.Boss);
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
				Check.That(known.Count == 1, $"{known.Count} enemies known after the reveal");
				int npc = known[0];
				Check.Equal(0, CodexSystem.KillCount(npc), "kills of the revealed enemy");
				Check.That(CodexSystem.DropsOf(npc).Any(d => d.item == fact.Item && d.count == 0), "the item is not among its drops");
				Check.That(CodexSystem.NpcsDropping(fact.Item).Any(d => d.npc == npc), "the item page does not list the enemy");
				if (fact.Biomes.Count > 0)
				{
					Check.That(CodexSystem.BiomesWhereSeen(npc).Contains(fact.Biomes[0]), $"not seen in {fact.Biomes[0]}");
					Check.Equal(1, CodexSystem.SightingCount(npc), "sightings of the revealed enemy");
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
				CodexSystem.RefreshIndex();
			}
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

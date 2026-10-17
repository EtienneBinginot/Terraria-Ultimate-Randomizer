using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Codex;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Tests
{
	public static class LootTests
	{
		public static void Register()
		{
			TestRunner.Register("SnapshotsCaptured", SnapshotsCaptured);
			TestRunner.Register("MobPlansValid", MobPlansValid);
			TestRunner.Register("LootAbundanceScales", LootAbundanceScales);
			TestRunner.Register("ProtectedDropsKept", ProtectedDropsKept);
			TestRunner.Register("NoMinionFarms", NoMinionFarms);
			TestRunner.Register("EventItemsKept", EventItemsKept);
			TestRunner.Register("BagPlansValid", BagPlansValid);
			TestRunner.Register("BagsKeepCoins", BagsKeepCoins);
			TestRunner.Register("CrittersDrop", CrittersDrop);
			TestRunner.Register("WildTail", WildTail);
			TestRunner.Register("PlansIgnoreWild", PlansIgnoreWild);
			TestRunner.Register("DropTableLength", DropTableLength);
			TestRunner.Register("JackpotUseful", JackpotUseful);
			TestRunner.Register("JackpotNotInEnemyDrops", JackpotNotInEnemyDrops);
			TestRunner.Register("JackpotRatesLower", JackpotRatesLower);
			TestRunner.Register("JackpotDropsMarked", JackpotDropsMarked);
			TestRunner.Register("GiantBonusUseful", GiantBonusUseful);
			TestRunner.Register("MiniBonusDrop", MiniBonusDrop);
		}

		// Critters get a share (CritterDropFactor) of the small frequent drop, here at abundance 3
		private static void CrittersDrop()
		{
			LootConfig cfg = LootConfig.Instance;
			if (!cfg.RandomizeMobDrops || cfg.DropTableStyle == DropTableStyle.MirrorOriginal || !cfg.CrittersCanDrop) Check.Skip("critters get no random drops");
			if (GeneralConfig.Instance.RandomizerMode == RandomizerMode.Shuffle) Check.Skip("shuffle mode has no extra drops");
			float first = Math.Min(1f, cfg.CommonDropChance / 100f * 3f * cfg.CritterDropFactor);
			if (first <= 0.0001f) Check.Skip("critters get no small frequent drop with these settings");
			List<string> failures = new();
			foreach (int npc in new[] { NPCID.Bunny, NPCID.Mouse, NPCID.Butterfly })
			{
				List<PlannedDrop> plan = RandomNpcDropRule.Plan(npc, Main.expertMode, abundance: 3f);
				if (!plan.Any(d => d.Extra && Math.Abs(d.Chance - first) < 0.001f)) failures.Add($"{NpcName(npc)}: no frequent drop at {first * 100:0} % ({plan.Count(d => d.Extra)} random drops)");
			}
			Check.All(failures, "Critters without their frequent drop");
		}

		// The madness tail, with explicit parameters: about `chance` % above the limit, each tier `decay` times rarer
		private static void WildTail()
		{
			const int rolls = 100000;
			const int limit = 2;
			Random rng = new(1234);
			int[] counts = new int[Tiers.Count];
			int beyond = 0;
			for (int i = 0; i < rolls; i++)
			{
				int tier = ItemPool.WildTier(limit, 8f, 3f, rng);
				if (tier > Tiers.Max) beyond++;
				else counts[tier]++;
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"WildTail: limit {limit}, tiers {string.Join(" / ", counts)} out of {rolls}");
			List<string> failures = new();
			int above = rolls - counts[limit] - beyond;
			double share = above * 100.0 / rolls;
			if (beyond > 0) failures.Add($"{beyond} rolls above tier {Tiers.Max}");
			if (counts.Take(limit).Sum() > 0) failures.Add("rolls below the limit");
			if (share < 7.3 || share > 8.7) failures.Add($"{share:0.00} % above the limit, expected about 8");
			for (int t = limit + 1; t <= limit + 2; t++)
			{
				double ratio = counts[t + 1] == 0 ? double.MaxValue : counts[t] / (double)counts[t + 1];
				if (ratio < 2.4 || ratio > 3.75) failures.Add($"tier {t} is {ratio:0.00} times as frequent as tier {t + 1}, expected about 3");
			}
			// Near the top, the tail stops at tier 9
			for (int i = 0; i < 2000; i++)
			{
				if (ItemPool.WildTier(7, 50f, 1.5f, rng) > Tiers.Max) { failures.Add("a roll from tier 7 went above tier 9"); break; }
			}
			int none = 0;
			for (int i = 0; i < 10000; i++) if (ItemPool.WildTier(limit, 0f, 3f, rng) != limit) none++;
			if (none > 0) failures.Add($"{none} rolls above the limit with a chance of 0 %");
			Check.All(failures, "Wild tier tail");
		}

		// Every runtime roll above the progression limit (madness or luck) is useful: equipment or a progression station,
		// never an ore, a block or a material; its gap is its tier above the limit
		private static void JackpotUseful()
		{
			ProgressionConfig progression = ProgressionConfig.Instance;
			if (!progression.TierGating) Check.Skip("no progression limit");
			if (GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent) Check.Skip("only the coherent mode rolls jackpots");
			int limit = ItemPool.GatingLimit(0);
			if (limit >= Tiers.Max) Check.Skip("the limit is already the last tier");
			List<string> failures = new();
			int jackpots = 0;
			try
			{
				ItemPool.WildChanceOverride = 100f;
				for (int i = 0; i < 3000; i++)
				{
					bool lucky = i % 2 == 1;
					RollRequest request = new()
					{
						Keys = new[] { 4242, i },
						SourceTier = lucky ? Tiers.Max : 0,
						OriginalItem = -1,
						Policy = CategoryPolicy.Weighted,
						TierJitterDown = 1,
						IgnoreGating = lucky,
						Wild = !lucky,
						WildRng = new Random(i)
					};
					int item = ItemPool.Roll(request);
					int gap = ItemPool.LastJackpotGap;
					if (ItemPool.TierOf(item) > limit && gap == 0) failures.Add($"{Lang.GetItemNameValue(item)} above the limit without a jackpot mark");
					if (gap == 0) continue;
					jackpots++;
					if (!ItemPool.IsJackpotWorthy(item)) failures.Add($"{Lang.GetItemNameValue(item)} ({ItemPool.Info[item].Category}) as a jackpot");
					if (gap != ItemPool.TierOf(item) - limit) failures.Add($"{Lang.GetItemNameValue(item)}: gap {gap}, tier {ItemPool.TierOf(item)}, limit {limit}");
					if (failures.Count > 20) break;
				}
				// Plans never roll jackpots
				RollRequest planned = new() { Keys = new[] { 4243, 1 }, SourceTier = Tiers.Max, OriginalItem = -1, Policy = CategoryPolicy.Weighted, IgnoreGating = true };
				ItemPool.Roll(planned);
				if (ItemPool.LastJackpotGap != 0) failures.Add("a roll without the runtime rng is a jackpot");
			}
			finally
			{
				ItemPool.WildChanceOverride = null;
				ItemPool.ClearJackpot();
			}
			Check.That(jackpots > 0, "no jackpot in 3000 rolls above the limit");
			// Never something without effect: music boxes (accessories), pets and light pets, dyes, vanity; real equipment is
			int[] useless = { ItemID.MusicBoxOverworldDay, ItemID.Carrot, ItemID.FairyBell, ItemID.WispinaBottle, ItemID.BlackDye, ItemID.SummerHat };
			foreach (int item in useless.Where(ItemPool.IsValid))
			{
				if (ItemPool.IsJackpotWorthy(item)) failures.Add($"{Lang.GetItemNameValue(item)} counts as a jackpot");
			}
			foreach (int item in new int[] { ItemID.Minishark, ItemID.HermesBoots }.Where(ItemPool.IsValid))
			{
				if (!ItemPool.IsJackpotWorthy(item)) failures.Add($"{Lang.GetItemNameValue(item)} is not worth a jackpot");
			}
			Check.All(failures, "Useless jackpots");
		}

		// A jackpot is not recorded as the enemy's (or the bag's) drop, its mark follows a copy of the item, and no planned
		// drop (journal, codex, hints) is a jackpot
		private static void JackpotNotInEnemyDrops()
		{
			Item item = new(ItemID.Zenith);
			Check.That(CodexSystem.CountsAsDrop(item), "an ordinary drop is not recorded");
			Check.That(item.TryGetGlobalItem(out JackpotItem jackpot), "no jackpot data on items");
			// Set directly: Mark would also show the effects
			jackpot.Gap = 3;
			Check.Equal(3, JackpotItem.GapOf(item), "gap of a marked item");
			Check.That(!CodexSystem.CountsAsDrop(item), "a jackpot is recorded as a drop");
			Check.Equal(3, JackpotItem.GapOf(item.Clone()), "gap of a copy (picked up)");
			List<string> failures = new();
			foreach (int npc in LootSnapshot.Npcs.Keys.OrderBy(k => k).Where((_, i) => i % 10 == 0))
			{
				if (RandomNpcDropRule.Plan(npc, Main.expertMode, lucky: true).Any(d => d.JackpotGap > 0)) failures.Add(Lang.GetNPCNameValue(npc));
			}
			Check.All(failures, "Planned drops marked as jackpots");
		}

		// Kills through the real extra-drop rule with madness at 100 %: the drops above the limit are marked with their gap,
		// useful, and the others are not marked. The spawned items are removed
		private static void JackpotDropsMarked()
		{
			if (Main.netMode != NetmodeID.SinglePlayer) Check.Skip("single player only (drops spawn on the server)");
			LootConfig cfg = LootConfig.Instance;
			if (!cfg.RandomizeMobDrops || cfg.DropTableStyle == DropTableStyle.MirrorOriginal || GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent) Check.Skip("no random extra drops");
			if (!ProgressionConfig.Instance.TierGating) Check.Skip("no progression limit");
			int limit = ItemPool.GatingLimit(0);
			if (limit >= Tiers.Max) Check.Skip("the limit is already the last tier");
			NPC npc = new();
			npc.SetDefaults(NPCID.Zombie);
			npc.Center = Main.LocalPlayer.Center;
			npc.life = 0;
			List<int> slots = new();
			List<string> failures = new();
			int marked = 0;
			try
			{
				ItemPool.WildChanceOverride = 100f;
				CodexSystem.SpawnLog = slots;
				RandomNpcDropRule rule = new(NPCID.Zombie);
				for (int i = 0; i < 40; i++)
				{
					rule.TryDroppingItem(new DropAttemptInfo { npc = npc, player = Main.LocalPlayer, rng = Main.rand, IsExpertMode = Main.expertMode, IsMasterMode = Main.masterMode });
				}
				CodexSystem.SpawnLog = null;
				foreach (int slot in slots.Distinct())
				{
					if (slot < 0 || slot >= Main.maxItems || !Main.item[slot].active) continue;
					Item item = Main.item[slot];
					int gap = JackpotItem.GapOf(item);
					if (gap == 0) continue;
					marked++;
					if (!ItemPool.IsJackpotWorthy(item.type)) failures.Add($"{Lang.GetItemNameValue(item.type)} marked but not useful");
					if (gap != ItemPool.TierOf(item.type) - limit) failures.Add($"{Lang.GetItemNameValue(item.type)}: gap {gap}, tier {ItemPool.TierOf(item.type)}, limit {limit}");
					if (CodexSystem.CountsAsDrop(item)) failures.Add($"{Lang.GetItemNameValue(item.type)} would be recorded as the zombie's drop");
				}
			}
			finally
			{
				CodexSystem.SpawnLog = null;
				ItemPool.WildChanceOverride = null;
				ItemPool.ClearJackpot();
				foreach (int slot in slots.Distinct())
				{
					if (slot < 0 || slot >= Main.maxItems) continue;
					Main.item[slot].TurnToAir();
					Main.item[slot].active = false;
				}
			}
			Check.That(marked > 0, $"no marked jackpot in {slots.Count} drops of 40 kills");
			Check.All(failures, "Jackpot drops");
		}

		// A giant's bonus item is useful (equipment that does something, a progression station) and within the limit unless it
		// is a jackpot, which comes about GiantWildMultiplier times as often as with the usual madness chance (seeded rngs)
		private static void GiantBonusUseful()
		{
			if (!LootConfig.Instance.RandomizeMobDrops || GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent) Check.Skip("no random extra drops");
			List<string> failures = new();
			int limit = ItemPool.GatingLimit(0);
			int useful = 0;
			const int Rolls = 2000;
			try
			{
				for (int i = 0; i < Rolls; i++)
				{
					int item = RandomNpcDropRule.GiantDrop(NPCID.Zombie, 5000 + i, false);
					int gap = ItemPool.LastJackpotGap;
					if (ItemPool.IsJackpotWorthy(item)) useful++;
					if (gap == 0 && ProgressionConfig.Instance.TierGating && ItemPool.TierOf(item) > limit && failures.Count < 10) failures.Add($"{Lang.GetItemNameValue(item)} above the limit without a jackpot");
				}
				if (useful < Rolls * 0.98) failures.Add($"only {useful} useful items out of {Rolls}");
				// The jackpot rate against the usual one, on the same rolls
				if (ProgressionConfig.Instance.TierGating && ProgressionConfig.Instance.WildDropChance > 0 && limit < Tiers.Max)
				{
					int Jackpots(float multiplier)
					{
						int count = 0;
						for (int i = 0; i < 4000; i++)
						{
							RandomNpcDropRule.GiantDrop(NPCID.Zombie, 9000 + i, false, multiplier);
							if (ItemPool.LastJackpotGap > 0) count++;
						}
						return count;
					}
					int normal = Jackpots(1f);
					int giant = Jackpots(RandomNpcDropRule.GiantWildMultiplier);
					ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Giant jackpots: {giant} per 4000 bonus drops, {normal} with the usual madness chance");
					double expected = RandomNpcDropRule.GiantWildMultiplier * normal;
					if (normal >= 20 && (giant < expected * 0.7 || giant > expected * 1.3)) failures.Add($"{giant} giant jackpots, expected about {expected:0} ({normal} x {RandomNpcDropRule.GiantWildMultiplier})");
				}
			}
			finally
			{
				ItemPool.ClearJackpot();
			}
			Check.All(failures, "Giant bonus drops");
		}

		// A mini gives one bonus item, a giant one useful item, an ordinary enemy none; a boss minion never does
		private static void MiniBonusDrop()
		{
			if (!LootConfig.Instance.RandomizeMobDrops) Check.Skip("enemy drops are not randomized");
			EnemiesConfig cfg = EnemiesConfig.Instance;
			List<string> failures = new();
			int Count(int type, EnemyKind kind, out int item)
			{
				NPC npc = new();
				npc.SetDefaults(type);
				item = 0;
				if (!npc.TryGetGlobalNPC(out EnemyIndividualNPC individual)) return -1;
				individual.Kind = kind;
				List<(int item, int stack, int gap)> drops = RandomNpcDropRule.BonusDrops(npc, npc.netID, 77, false);
				if (drops.Count > 0) item = drops[0].item;
				return drops.Count;
			}
			try
			{
				if (cfg.MiniBonusDrop && Count(NPCID.Zombie, EnemyKind.Mini, out _) != 1) failures.Add("a mini zombie gives no bonus item");
				if (cfg.GiantBonusDrop)
				{
					if (Count(NPCID.Zombie, EnemyKind.Giant, out int item) != 1) failures.Add("a giant zombie gives no bonus item");
					// Only the coherent mode rolls by category (Chaos picks anything)
					else if (GeneralConfig.Instance.RandomizerMode == RandomizerMode.Coherent && !ItemPool.IsJackpotWorthy(item)) failures.Add($"a giant zombie gives {Lang.GetItemNameValue(item)}");
				}
				if (Count(NPCID.Zombie, EnemyKind.Normal, out _) != 0) failures.Add("an ordinary zombie gives a bonus item");
				foreach (int minion in new int[] { NPCID.ServantofCthulhu, NPCID.Creeper, NPCID.Bee })
				{
					if (Count(minion, EnemyKind.Mini, out _) > 0 || Count(minion, EnemyKind.Giant, out _) > 0) failures.Add($"{Lang.GetNPCNameValue(minion)} gives a bonus item");
				}
			}
			finally
			{
				ItemPool.ClearJackpot();
			}
			if (!cfg.MiniBonusDrop && !cfg.GiantBonusDrop) Check.Skip("giant and mini bonus drops are off");
			Check.All(failures, "Giant and mini bonus drops");
		}

		// Jackpots rarer than in 1.2.0 (madness 12 %, luck 4 %) and 1.2.1 (9 %, 3 %, decay 3, allowance 3): items up to two
		// tiers ahead already come normally
		private static void JackpotRatesLower()
		{
			float Default(string field) => Convert.ToSingle(typeof(ProgressionConfig).GetField(field).GetCustomAttributes(typeof(System.ComponentModel.DefaultValueAttribute), false)
				.OfType<System.ComponentModel.DefaultValueAttribute>().First().Value);
			Check.Equal(5f, Default("WildDropChance"), "default WildDropChance");
			Check.Equal(2f, Default("RuntimeJackpotChance"), "default RuntimeJackpotChance");
			Check.Equal(4f, Default("WildTierDecay"), "default WildTierDecay");
			Check.Equal(2f, Default("TierAllowance"), "default TierAllowance");
		}

		// Plans, findability and guarantees never use the madness tail: identical at 0 % and 50 %
		private static void PlansIgnoreWild()
		{
			int[] npcs = LootSnapshot.Npcs.Keys.OrderBy(k => k).Where((_, i) => i % 8 == 0).ToArray();
			string Snapshot(float chance)
			{
				ItemPool.WildChanceOverride = chance;
				GuaranteedSources.Reset();
				Findability.Reset();
				IEnumerable<string> plans = npcs.Select(n => string.Join(",", RandomNpcDropRule.Plan(n, Main.expertMode).Select(d => $"{d.Item}:{d.Chance:0.0000}")));
				IEnumerable<string> tiers = Enumerable.Range(1, ItemPool.Info.Length - 1).Select(t => Findability.Tier(t).ToString());
				IEnumerable<string> hosts = GuaranteedSources.Requirements.Select(r => r.Item + ">" + string.Join(",", GuaranteedSources.HostsOf(r.Item).OrderBy(h => h)));
				return string.Join("|", plans) + "#" + string.Join(",", tiers) + "#" + string.Join(";", hosts);
			}
			try
			{
				string calm = Snapshot(0f);
				string wild = Snapshot(50f);
				Check.That(calm == wild, "a plan, the findability or the guaranteed sources change with WildDropChance");
			}
			finally
			{
				ItemPool.WildChanceOverride = null;
				GuaranteedSources.Reset();
				Findability.Reset();
			}
		}

		private static string NpcName(int netId) => ContentSamples.NpcsByNetId.TryGetValue(netId, out NPC sample) ? sample.TypeName : netId.ToString();

		private static void SnapshotsCaptured()
		{
			Check.That(LootSnapshot.Npcs.Count > 300, $"only {LootSnapshot.Npcs.Count} enemy drop tables captured");
			Check.That(LootSnapshot.Items.Count > 30, $"only {LootSnapshot.Items.Count} bag tables captured");
			Check.That(LootSnapshot.ForNpc(NPCID.Zombie).Count > 0, "Zombie has no captured drops");
			Check.That(LootSnapshot.ForItem(ItemID.EyeOfCthulhuBossBag).Count > 0, "Eye of Cthulhu bag has no captured content");
		}

		private static void ValidatePlan(string source, List<PlannedDrop> plan, List<string> failures)
		{
			foreach (PlannedDrop drop in plan)
			{
				if (drop.Item <= ItemID.None || drop.Item >= ItemLoader.ItemCount) failures.Add($"{source}: invalid item {drop.Item}");
				else if (drop.Chance <= 0 || drop.Chance > 1.0001f) failures.Add($"{source}: chance {drop.Chance} for {Lang.GetItemNameValue(drop.Item)}");
				else if (drop.StackMin < 1 || drop.StackMax < drop.StackMin) failures.Add($"{source}: stack {drop.StackMin}-{drop.StackMax}");
				else if (drop.Extra && !ItemPool.IsRandomizable(drop.Item) && drop.Item != ItemID.Gel) failures.Add($"{source}: extra drop {Lang.GetItemNameValue(drop.Item)} is protected/excluded");
			}
		}

		private static void MobPlansValid()
		{
			List<string> failures = new();
			foreach (int netId in LootSnapshot.Npcs.Keys)
			{
				ValidatePlan(NpcName(netId), RandomNpcDropRule.Plan(netId, Main.expertMode), failures);
			}
			Check.All(failures, "Invalid enemy drop tables");
		}

		// Long drop tables: with explicit ranges (2 to 7 against the former 1 to 4, abundance 3), the average slot count of
		// the common enemies follows the skewed roll (about 1.85 times more), and the plans really use that count
		private static void DropTableLength()
		{
			float factor = (float)Math.Sqrt(3);
			List<int> enemies = new();
			for (int type = 1; type < NPCID.Count; type++)
			{
				if (NPCID.Sets.CountsAsCritter[type] || RandomNpcDropRule.IsBossType(type)) continue;
				if (RandomNpcDropRule.CanHaveExtraDrops(type, false, LootSnapshot.ForNpc(type))) enemies.Add(type);
			}
			if (enemies.Count < 50) Check.Skip($"only {enemies.Count} enemies can get extra drops with these settings");
			double before = enemies.Average(t => RandomNpcDropRule.SlotCount(DetRandom.NpcKey(t), false, false, 1, 4, DropTableStyle.VanillaLike, factor));
			double after = enemies.Average(t => RandomNpcDropRule.SlotCount(DetRandom.NpcKey(t), false, false, 2, 7, DropTableStyle.VanillaLike, factor));
			double skew = RandomNpcDropRule.SlotSkew(false, DropTableStyle.VanillaLike);
			double expected = RandomNpcDropRule.ExpectedSlots(2, 7, skew) * factor;
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Drop tables: {before:0.00} slots per enemy with 1-4, {after:0.00} with 2-7 (expected {expected:0.00}), {enemies.Count} enemies");
			List<string> failures = new();
			if (Math.Abs(after - expected) > expected * 0.15) failures.Add($"{after:0.00} slots per enemy with 2 to 7, expected about {expected:0.00}");
			if (after < before * 1.6) failures.Add($"2 to 7 slots give {after / before:0.00} times the slots of 1 to 4, expected about 1.85");

			// The plans use that count: the extra drops are the slots plus the small frequent drops
			LootConfig cfg = LootConfig.Instance;
			if (cfg.RandomizeMobDrops && cfg.DropTableStyle != DropTableStyle.MirrorOriginal && GeneralConfig.Instance.RandomizerMode != RandomizerMode.Shuffle)
			{
				int commons = 0;
				for (float total = cfg.CommonDropChance / 100f * 3f; total > 0.0001f && commons < 20; total -= 1f) commons++;
				foreach (int type in enemies.Take(40))
				{
					int slots = RandomNpcDropRule.SlotCount(DetRandom.NpcKey(type), false, false, cfg.DropSlotsMin, cfg.DropSlotsMax, cfg.DropTableStyle, factor);
					int planned = RandomNpcDropRule.ExtraDrops(type, type, Main.expertMode, false, 3f).Count - commons;
					if (planned != slots) failures.Add($"{NpcName(type)}: {planned} random slots planned, {slots} expected");
				}
			}
			Check.All(failures, "Drop table length");
		}

		// Explicit abundance values (not the config): more random items per kill, chances at most 100 %, guaranteed drops unchanged
		private static void LootAbundanceScales()
		{
			LootConfig cfg = LootConfig.Instance;
			if (!cfg.RandomizeMobDrops || cfg.DropTableStyle == DropTableStyle.MirrorOriginal) Check.Skip("random enemy drops are off");
			if (GeneralConfig.Instance.RandomizerMode == RandomizerMode.Shuffle) Check.Skip("shuffle mode has no extra drops");
			int[] enemies = { NPCID.BlueSlime, NPCID.Zombie, NPCID.DemonEye, NPCID.CaveBat, NPCID.Skeleton, NPCID.Vulture, NPCID.Antlion, NPCID.Harpy, NPCID.GoblinScout, NPCID.Piranha };
			float[] factors = { 1f, 2f, 4f };
			double[] expected = new double[factors.Length];
			List<string> failures = new();
			foreach (int npc in enemies)
			{
				string guaranteedAt1 = null;
				for (int f = 0; f < factors.Length; f++)
				{
					List<PlannedDrop> plan = RandomNpcDropRule.Plan(npc, Main.expertMode, abundance: factors[f]);
					foreach (PlannedDrop drop in plan)
					{
						if (drop.Chance > 1.0001f) failures.Add($"{NpcName(npc)} x{factors[f]}: {Lang.GetItemNameValue(drop.Item)} at {drop.Chance * 100:0} %");
						if (drop.Extra) expected[f] += drop.Chance;
					}
					string guaranteed = string.Join(";", plan.Where(d => d.Guaranteed).Select(d => $"{d.Item}:{d.Chance:0.0000}"));
					if (f == 0) guaranteedAt1 = guaranteed;
					else if (guaranteed != guaranteedAt1) failures.Add($"{NpcName(npc)} x{factors[f]}: guaranteed drops changed");
				}
			}
			if (expected[0] <= 0) Check.Skip("these enemies have no random drops with the current settings");
			double ratio2 = expected[1] / expected[0];
			double ratio4 = expected[2] / expected[1];
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Abundance: {expected[0] / enemies.Length:0.00} / {expected[1] / enemies.Length:0.00} / {expected[2] / enemies.Length:0.00} random items per kill at x1 / x2 / x4");
			if (ratio2 < 1.4 || ratio2 > 2.6) failures.Add($"x2 gives {ratio2:0.00} times the random items of x1, expected about 2");
			if (ratio4 < 1.4 || ratio4 > 2.6) failures.Add($"x4 gives {ratio4:0.00} times the random items of x2, expected about 2");
			Check.All(failures, "Loot abundance");
		}

		private static void ProtectedDropsKept()
		{
			List<string> failures = new();
			foreach ((int netId, List<DropRateInfo> drops) in LootSnapshot.Npcs)
			{
				foreach (DropRateInfo entry in drops)
				{
					if (!DropHelper.KeepOriginal(entry.itemId)) continue;
					int replacement = DropHelper.Replacement(RollKey.MobDrop, DetRandom.NpcKey(netId), entry.itemId, 0, 0, false);
					if (replacement != entry.itemId) failures.Add($"{NpcName(netId)}: {Lang.GetItemNameValue(entry.itemId)} replaced");
				}
			}
			int[] mustKeep = { ItemID.Pwnhammer, ItemID.TempleKey, ItemID.CopperCoin, ItemID.EyeOfCthulhuBossBag };
			foreach (int item in mustKeep)
			{
				if (!DropHelper.KeepOriginal(item)) failures.Add($"{Lang.GetItemNameValue(item)} is not kept");
			}
			Check.All(failures, "Progression drops changed");
		}

		private static void NoMinionFarms()
		{
			if (!LootConfig.Instance.RandomizeMobDrops) Check.Skip("enemy drops are not randomized");
			int[] noExtra = { NPCID.Creeper, NPCID.ServantofCthulhu, NPCID.TheHungry, NPCID.Probe, NPCID.Bee, NPCID.SkeletronHand, NPCID.Guide, NPCID.Merchant, NPCID.TorchGod };
			List<string> failures = new();
			foreach (int type in noExtra)
			{
				// The number of extra drops is random and may be 0: check the rule itself
				if (RandomNpcDropRule.CanHaveExtraDrops(type, RandomNpcDropRule.IsBossType(type), LootSnapshot.ForNpc(type))) failures.Add($"{NpcName(type)} can get extra drops");
			}
			Check.All(failures, "Minions or town NPCs get random drops");
		}

		// Old One's Army medals and mana, and banners must never be replaced
		private static void EventItemsKept()
		{
			List<string> failures = new();
			foreach (int item in new[] { ItemID.DefenderMedal, ItemID.DD2EnergyCrystal })
			{
				if (ItemPool.IsRandomizable(item)) failures.Add($"{Lang.GetItemNameValue(item)} is randomizable");
			}
			Check.All(failures, "Event items can be replaced");
			Check.Equal(NPCID.EaterofWorldsHead, BossParts.Owner(NPCID.EaterofWorldsTail), "owner of the Eater of Worlds tail");
			Check.That(RandomNpcDropRule.IsBossType(BossParts.Owner(NPCID.EaterofWorldsBody)), "Eater of Worlds segments count as the boss");
		}

		private static void BagPlansValid()
		{
			List<string> failures = new();
			foreach (int bag in LootSnapshot.Items.Keys)
			{
				ValidatePlan(Lang.GetItemNameValue(bag), BagPlan.Plan(bag), failures);
			}
			Check.All(failures, "Invalid bag contents");
		}

		private static void BagsKeepCoins()
		{
			List<string> failures = new();
			foreach (int bag in LootSnapshot.Items.Keys.Where(b => ItemID.Sets.BossBag[b]))
			{
				List<DropRateInfo> original = LootSnapshot.ForItem(bag);
				List<PlannedDrop> plan = BagPlan.Plan(bag);
				for (int i = 0; i < original.Count && i < plan.Count; i++)
				{
					if (ItemID.Sets.CommonCoin[original[i].itemId] && plan[i].Item != original[i].itemId) failures.Add($"{Lang.GetItemNameValue(bag)} loses its coins");
				}
			}
			Check.All(failures, "Boss bag coins changed");
		}
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Codex;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Tests
{
	// Ceilings of the rolls: the hardmode wall, true tiers and the isolated re-pick, banned items, chest caps, taming and
	// jackpot chests
	public static class CapTests
	{
		public static void Register()
		{
			TestRunner.Register("HardmodeWall", HardmodeWall);
			TestRunner.Register("RepickOnlyChangesFlagged", RepickOnlyChangesFlagged);
			TestRunner.Register("RaisedTiers", RaisedTiers);
			TestRunner.Register("BannedNeverRolled", BannedNeverRolled);
			TestRunner.Register("ChestCap", ChestCap);
			TestRunner.Register("ChestTameKeepsOpened", ChestTameKeepsOpened);
			TestRunner.Register("JackpotChests", JackpotChests);
		}

		private static string Name(int item) => Lang.GetItemNameValue(item);

		private static void Log(string text) => ModContent.GetInstance<UltimateRandomizer>().Logger.Info(text);

		// Before hardmode the progression limit stops at PostSkeletron whatever the unlocked tier, a source's floor (Wall of
		// Flesh) still wins, and UnlockTier (hints) is its exact inverse
		private static void HardmodeWall()
		{
			if (!ProgressionConfig.Instance.TierGating) Check.Skip("tier gating is off");
			List<string> failures = new();
			for (int unlocked = 0; unlocked <= (int)Tier.PostSkeletron; unlocked++)
			{
				int limit = ItemPool.GatingLimit(unlocked, false, 0);
				if (limit > (int)Tier.PostSkeletron) failures.Add($"limit {limit} at unlocked tier {unlocked} before hardmode");
				int floor = ItemPool.GatingLimit(unlocked, false, (int)Tier.Hardmode);
				if (floor != (int)Tier.Hardmode) failures.Add($"limit {floor} with a hardmode floor at unlocked tier {unlocked}");
			}
			for (int unlocked = 0; unlocked < Tiers.Count; unlocked++)
			{
				int limit = ItemPool.GatingLimit(unlocked, unlocked >= (int)Tier.Hardmode, 0);
				for (int tier = 0; tier < Tiers.Count; tier++)
				{
					bool given = limit >= tier;
					bool unlocks = unlocked >= ItemPool.UnlockTier(tier);
					if (given != unlocks) failures.Add($"tier {tier} at unlocked tier {unlocked}: limit {limit}, UnlockTier {ItemPool.UnlockTier(tier)}");
				}
			}
			Check.All(failures, "Hardmode wall");
		}

		// An item that may be re-picked: banned, or given later than its bucket's tier (a re-pick only happens when the true
		// tier is above the roll's ceiling, which is never below the bucket it picked from)
		private static bool Flagged(int item) => ItemPool.IsBanned(item) || ItemPool.TrueTierOf(item) > ItemPool.TierOf(item);

		// The same rolls with and without the re-pick: every difference is an item the re-pick had to replace, so every other
		// roll (mob plans, recipe results, chest slots, gated rolls) gives exactly what it gave before the re-pick existed
		private static void RepickOnlyChangesFlagged()
		{
			if (GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent) Check.Skip("only in Coherent mode");
			int[] npcs = LootSnapshot.Npcs.Keys.OrderBy(k => k).Where((_, i) => i % 3 == 0).ToArray();
			int[] originals = Enumerable.Range(1, ItemPool.Info.Length - 1).Where(ItemPool.IsRandomizable).Where((_, i) => i % 7 == 0).ToArray();
			List<int> Snapshot()
			{
				GuaranteedSources.Reset();
				Findability.Reset();
				List<int> items = new();
				foreach (int npc in npcs) items.AddRange(RandomNpcDropRule.Plan(npc, Main.expertMode, guaranteed: false).Select(d => d.Item));
				for (int i = 0; i < RecipeSystem.RecipeCount && i < Recipe.numRecipes; i++)
				{
					(int type, int stack)[] ingredients = RecipeSystem.OriginalIngredients(i);
					items.Add(RecipeSystem.RollResult(i, ingredients.Select(x => x.type).ToList(), ingredients.Sum(x => x.stack), lucky: true).type);
				}
				for (int k = 0; k < originals.Length; k++)
				{
					int original = originals[k];
					items.Add(ChestSystem.RollChestSlot(100 + k, 300 + k % 50, k % 40, k % 4, original, tame: false));
					RollRequest gated = new() { Keys = new[] { 5150, k }, SourceTier = ItemPool.TierOf(original), OriginalItem = original, Policy = CategoryPolicy.SameAsOriginal, TierJitterUp = 1 };
					items.Add(ItemPool.Roll(gated));
				}
				return items;
			}
			List<string> failures = new();
			int differences = 0, rolls = 0;
			try
			{
				ItemPool.RepickDisabled = true;
				List<int> raw = Snapshot();
				ItemPool.RepickDisabled = false;
				List<int> repicked = Snapshot();
				rolls = raw.Count;
				if (raw.Count != repicked.Count) failures.Add($"{raw.Count} rolls without the re-pick, {repicked.Count} with it");
				for (int i = 0; i < Math.Min(raw.Count, repicked.Count); i++)
				{
					if (raw[i] == repicked[i]) continue;
					differences++;
					if (!Flagged(raw[i]) && failures.Count < 15) failures.Add($"{Name(raw[i])} (tier {ItemPool.TierOf(raw[i])}, true tier {ItemPool.TrueTierOf(raw[i])}) became {Name(repicked[i])}");
					if (Flagged(repicked[i]) && ItemPool.IsBanned(repicked[i]) && failures.Count < 15) failures.Add($"re-picked into the banned {Name(repicked[i])}");
				}
			}
			finally
			{
				ItemPool.RepickDisabled = false;
				GuaranteedSources.Reset();
				Findability.Reset();
			}
			Log($"Re-pick: {differences} of {rolls} rolls changed");
			Check.All(failures, "Rolls changed by the re-pick");
		}

		// Crafted items that no recipe makes before hardmode wait for it (true tier), nothing is lowered
		private static void RaisedTiers()
		{
			List<string> failures = new();
			int raised = 0;
			for (int type = 1; type < ItemPool.Info.Length; type++)
			{
				ItemInfo info = ItemPool.Info[type];
				if (info.Excluded) continue;
				if (info.TrueTier < info.Tier) failures.Add($"{Name(type)}: true tier {info.TrueTier} below its tier {info.Tier}");
				if (info.TrueTier > info.Tier) raised++;
			}
			foreach (int item in new[] { ItemID.PearlwoodSword, ItemID.PearlwoodWorkBench })
			{
				if (ItemPool.IsValid(item) && !ItemPool.Info[item].Excluded && ItemPool.TrueTierOf(item) < (int)Tier.Hardmode)
				{
					failures.Add($"{Name(item)}: true tier {ItemPool.TrueTierOf(item)}, hardmode expected");
				}
			}
			Log($"Raised tiers: {raised} items");
			Check.All(failures, "True tiers");
		}

		// Magic Storage's creative and internal items and tModLoader's own items are banned, and no roll gives a banned item
		private static void BannedNeverRolled()
		{
			List<string> failures = new();
			if (ModContent.TryFind("ModLoader", "StartBag", out ModItem startBag) && ItemPool.IsValid(startBag.Type) && !ItemPool.IsBanned(startBag.Type))
			{
				failures.Add("tModLoader's start bag is not banned");
			}
			RandomizerMode mode = GeneralConfig.Instance.RandomizerMode;
			for (int i = 0; i < 6000; i++)
			{
				RollRequest request = new()
				{
					Keys = new[] { 6161, i },
					SourceTier = i % Tiers.Count,
					OriginalItem = -1,
					Policy = i % 2 == 0 ? CategoryPolicy.Weighted : CategoryPolicy.Any,
					TierJitterUp = 1,
					IgnoreGating = true
				};
				int item = ItemPool.Roll(request);
				if (ItemPool.IsBanned(item) && failures.Count < 10) failures.Add($"{Name(item)} rolled ({mode})");
			}
			// A banned item found in a bucket is re-picked into an allowed one
			for (int type = 1; type < ItemPool.Info.Length; type++)
			{
				if (!ItemPool.IsBanned(type) || ItemPool.Info[type].Protected) continue;
				int repicked = ItemPool.Repick(type, ItemPool.TierOf(type), Tiers.Max, new[] { 6162, type }, 0);
				if (ItemPool.IsBanned(repicked) && failures.Count < 10) failures.Add($"{Name(type)} re-picked into {Name(repicked)}");
			}
			Check.All(failures, "Banned items");
			if (!ModLoader.HasMod("MagicStorage")) Check.Skip("Magic Storage is not loaded");
			foreach (string name in new[] { "CreativeStorageUnit", "StorageDeactivator", "StorageCoreAdamantite" })
			{
				if (ModContent.TryFind("MagicStorage", name, out ModItem item) && ItemPool.IsValid(item.Type) && !ItemPool.Info[item.Type].Excluded && !ItemPool.IsBanned(item.Type))
				{
					failures.Add($"Magic Storage's {name} is not banned");
				}
			}
			Check.All(failures, "Magic Storage items");
		}

		// Chest slots (world creation and taming) never go above the chest's cap, nor reach hardmode in a pre-hardmode chest
		private static void ChestCap()
		{
			if (GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent) Check.Skip("only in Coherent mode (shuffle and chaos keep no tier)");
			List<string> failures = new();
			for (int chestTier = 0; chestTier < Tiers.Count; chestTier++)
			{
				int cap = ChestSystem.ChestCap(chestTier);
				if (chestTier < (int)Tier.Hardmode && cap >= (int)Tier.Hardmode) failures.Add($"cap {cap} for a chest of tier {chestTier}");
			}
			int[] originals = Enumerable.Range(1, ItemPool.Info.Length - 1).Where(t => ItemPool.IsRandomizable(t) && ItemPool.TierOf(t) <= (int)Tier.Hardmode)
				.Where((_, i) => i % 5 == 0).ToArray();
			foreach (int chestTier in new[] { 0, 1, 3, 6, 7 })
			{
				int cap = ChestSystem.ChestCap(chestTier);
				for (int k = 0; k < originals.Length && failures.Count < 20; k++)
				{
					foreach (bool tame in new[] { false, true })
					{
						int item = ChestSystem.RollChestSlot(200 + k, 400 + chestTier, k % 40, chestTier, originals[k], tame);
						if (ItemPool.TrueTierOf(item) > cap) failures.Add($"{Name(item)} (true tier {ItemPool.TrueTierOf(item)}) in a chest of tier {chestTier}, cap {cap}");
						if (ItemPool.IsBanned(item)) failures.Add($"{Name(item)} is banned");
					}
				}
			}
			Check.All(failures, "Chest caps");
		}

		// Taming never touches an opened chest nor a jackpot chest, and only re-rolls items above the cap or banned
		private static void ChestTameKeepsOpened()
		{
			HashSet<int> opened = new() { CodexEntry.ChestPosition(10, 20), CodexEntry.ChestPosition(30, 40) };
			HashSet<int> jackpots = new() { CodexEntry.ChestPosition(50, 60) };
			Check.That(!ChestSystem.ShouldTame(10, 20, opened, jackpots), "an opened chest is tamed");
			Check.That(!ChestSystem.ShouldTame(30, 40, opened, jackpots), "an opened chest is tamed");
			Check.That(!ChestSystem.ShouldTame(50, 60, opened, jackpots), "a jackpot chest is tamed");
			Check.That(ChestSystem.ShouldTame(10, 40, opened, jackpots), "an unopened chest is not tamed");
			int low = Enumerable.Range(1, ItemPool.Info.Length - 1).FirstOrDefault(t => ItemPool.IsRandomizable(t) && !ItemPool.IsBanned(t) && ItemPool.TrueTierOf(t) == 0);
			int high = Enumerable.Range(1, ItemPool.Info.Length - 1).FirstOrDefault(t => ItemPool.IsRandomizable(t) && ItemPool.TrueTierOf(t) >= (int)Tier.Hardmode);
			if (low > 0) Check.That(!ChestSystem.NeedsTaming(low, ChestSystem.ChestCap(0)), $"{Name(low)} (tier 0) needs taming in a surface chest");
			if (high > 0) Check.That(ChestSystem.NeedsTaming(high, ChestSystem.ChestCap(3)), $"{Name(high)} (hardmode) does not need taming in a shadow chest");
		}

		// The jackpot chest selection is deterministic, picks at least 2 when there are enough chests, never an opened,
		// locked, biome, base or station chest; their content is 2 to 4 useful items above the cap (the jackpot tail), the same
		// every time
		private static void JackpotChests()
		{
			List<string> failures = new();
			List<ChestSystem.ChestSpot> spots = new();
			for (int i = 0; i < 300; i++)
			{
				spots.Add(new ChestSystem.ChestSpot { X = 100 + i * 7, Y = 200 + i % 13, Tier = i % 9, NearBase = i % 11 == 0, Locked = i % 17 == 0, HoldsStation = i % 19 == 0 });
			}
			HashSet<int> opened = spots.Where((_, i) => i % 5 == 0).Select(s => CodexEntry.ChestPosition(s.X, s.Y)).ToHashSet();
			List<int> first = ChestSystem.SelectJackpotChests(spots, opened, 1.5f, new Random(77));
			List<int> again = ChestSystem.SelectJackpotChests(spots, opened, 1.5f, new Random(77));
			if (!first.SequenceEqual(again)) failures.Add("the selection is not deterministic");
			if (first.Count < 2) failures.Add($"{first.Count} jackpot chests, at least 2 expected");
			if (first.Distinct().Count() != first.Count) failures.Add("a chest chosen twice");
			foreach (int position in first)
			{
				ChestSystem.ChestSpot spot = spots.First(s => CodexEntry.ChestPosition(s.X, s.Y) == position);
				if (opened.Contains(position) || spot.NearBase || spot.Locked || spot.HoldsStation || spot.Tier >= (int)Tier.Hardmode)
				{
					failures.Add($"chest at {spot.X},{spot.Y} (tier {spot.Tier}) is not eligible");
				}
			}
			if (ChestSystem.SelectJackpotChests(spots, opened, 0f, new Random(77)).Count != 0) failures.Add("jackpot chests with a chance of 0");
			Check.All(failures, "Jackpot chest selection");
			if (GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent) Check.Skip("content: only in Coherent mode");
			int aboveHardmode = 0, total = 0;
			for (int i = 0; i < 60; i++)
			{
				int chestTier = i % 4;
				int cap = ChestSystem.ChestCap(chestTier);
				int x = 500 + i * 9, y = 700 + i;
				List<(int type, int stack, int gap)> content = ChestSystem.JackpotChestContent(x, y, chestTier);
				if (!content.SequenceEqual(ChestSystem.JackpotChestContent(x, y, chestTier))) failures.Add($"content of {x},{y} not deterministic");
				List<(int type, int stack, int gap)> jackpots = content.Where(e => e.gap > 0).ToList();
				if (jackpots.Count < 2 || jackpots.Count > 4) failures.Add($"{jackpots.Count} jackpot items in a chest of tier {chestTier}");
				foreach ((int type, _, int gap) in jackpots)
				{
					total++;
					if (ItemPool.TrueTierOf(type) >= (int)Tier.Hardmode) aboveHardmode++;
					if (!ItemPool.IsJackpotWorthy(type)) failures.Add($"{Name(type)} in a jackpot chest is not useful");
					if (ItemPool.TrueTierOf(type) <= cap) failures.Add($"{Name(type)} (true tier {ItemPool.TrueTierOf(type)}) not above the cap {cap}");
					if (gap != ItemPool.TrueTierOf(type) - cap) failures.Add($"{Name(type)}: gap {gap}, true tier {ItemPool.TrueTierOf(type)}, cap {cap}");
				}
				if (!content.Any(e => e.type == ItemID.GoldCoin)) failures.Add("no gold in a jackpot chest");
				if (failures.Count > 20) break;
			}
			Log($"Jackpot chests: {total} items in 60 simulated chests, {aboveHardmode} from hardmode");
			Check.All(failures, "Jackpot chest content");
		}
	}
}

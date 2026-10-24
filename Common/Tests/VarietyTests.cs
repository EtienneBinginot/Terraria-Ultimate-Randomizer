using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Tests
{
	// Weight tables, surprise rolls, tier window and ore permutation
	public static class VarietyTests
	{
		public static void Register()
		{
			TestRunner.Register("SurpriseZero", SurpriseZero);
			TestRunner.Register("SurpriseHundred", SurpriseHundred);
			TestRunner.Register("SurpriseGatedAndProtected", SurpriseGatedAndProtected);
			TestRunner.Register("WeightTablesSeparated", WeightTablesSeparated);
			TestRunner.Register("TierWindowVariety", TierWindowVariety);
			TestRunner.Register("SparseBucketsWiden", SparseBucketsWiden);
			TestRunner.Register("OreMappingBijective", OreMappingBijective);
		}

		private static void RequireCoherent()
		{
			if (GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent) Check.Skip("only in Coherent mode");
		}

		// Randomizable items whose category can be surprised (not bags, boss summons or critters)
		private static int[] Originals(int count, int salt)
		{
			int[] all = Enumerable.Range(1, ItemPool.Info.Length - 1)
				.Where(t => ItemPool.IsRandomizable(t) && ItemPool.Info[t].Category is not (ItemCategory.Bag or ItemCategory.BossSummon or ItemCategory.Critter))
				.ToArray();
			Random rng = new(salt);
			return Enumerable.Range(0, count).Select(_ => all[rng.Next(all.Length)]).ToArray();
		}

		private static RollRequest Request(int original, int key, bool ignoreGating) => new()
		{
			Keys = new[] { 5150, key, original },
			SourceTier = ItemPool.TierOf(original),
			OriginalItem = original,
			Policy = CategoryPolicy.SameAsOriginal,
			TierJitterUp = 1,
			IgnoreGating = ignoreGating
		};

		private static void SurpriseZero()
		{
			RequireCoherent();
			List<string> failures = new();
			Random rng = new(1);
			int i = 0;
			foreach (int original in Originals(500, 11))
			{
				int item = ItemPool.RollReplacement(Request(original, i++, true), 0, rng);
				if (ItemPool.Info[item].Category != ItemPool.Info[original].Category) failures.Add($"{Lang.GetItemNameValue(original)} -> {Lang.GetItemNameValue(item)}");
			}
			Check.All(failures, "A 0% surprise changed the category");
		}

		// At 100%, the categories follow the loot weights
		private static void SurpriseHundred()
		{
			RequireCoherent();
			const int Rolls = 3000;
			CategoryWeights weights = LootConfig.Instance.DropCategoryWeights;
			int[] counts = new int[ItemCategories.Count];
			Random rng = new(2);
			int i = 0;
			foreach (int original in Originals(Rolls, 12))
			{
				int item = ItemPool.RollReplacement(Request(original, i++, true), 100, rng);
				counts[(int)ItemPool.Info[item].Category]++;
			}
			double total = Enumerable.Range(0, ItemCategories.Count).Sum(c => (double)weights.Get((ItemCategory)c));
			List<string> failures = new();
			for (int c = 0; c < ItemCategories.Count; c++)
			{
				double expected = weights.Get((ItemCategory)c) / total * 100;
				double actual = counts[c] * 100.0 / Rolls;
				if (Math.Abs(expected - actual) > 5) failures.Add($"{(ItemCategory)c}: {actual:0.#}% instead of {expected:0.#}%");
			}
			Check.All(failures, "Surprise rolls don't follow the loot weights");
		}

		private static void SurpriseGatedAndProtected()
		{
			RequireCoherent();
			ProgressionConfig progression = ProgressionConfig.Instance;
			int limit = ItemPool.GatingLimit(0);
			List<string> failures = new();
			Random rng = new(3);
			int i = 0;
			foreach (int original in Originals(1500, 13))
			{
				int item = ItemPool.RollReplacement(Request(original, i++, false), 100, rng);
				if (!ItemPool.IsRandomizable(item)) failures.Add($"{Lang.GetItemNameValue(item)} is protected or excluded");
				// The original's category may go up to its own tier (it already drops there in vanilla)
				int allowed = ItemPool.Info[item].Category == ItemPool.Info[original].Category ? Math.Max(limit, ItemPool.TierOf(original)) : limit;
				if (ItemPool.TierOf(item) > allowed) failures.Add($"{Lang.GetItemNameValue(item)} tier {ItemPool.TierOf(item)} > {allowed}");
			}
			Check.All(failures, "Surprise rolls ignore progression or protection");
		}

		private static void WeightTablesSeparated()
		{
			WeightTable loot = LootConfig.Instance.DropCategoryWeights;
			WeightTable ingredients = CraftingConfig.Instance.IngredientWeights;
			double lootTotal = Enumerable.Range(0, ItemCategories.Count).Sum(c => loot.Get((ItemCategory)c));
			double lootGear = Enumerable.Range(0, ItemCategories.Count).Where(c => ItemCategories.IsGear((ItemCategory)c)).Sum(c => loot.Get((ItemCategory)c));
			Check.That(lootTotal > 0 && lootGear / lootTotal > 0.5, $"loot weights give only {lootGear / Math.Max(1, lootTotal):P0} of equipment");
			List<string> gearInIngredients = Enumerable.Range(0, ItemCategories.Count)
				.Where(c => ItemCategories.IsGear((ItemCategory)c) && ingredients.Get((ItemCategory)c) > 0)
				.Select(c => ((ItemCategory)c).ToString()).ToList();
			Check.All(gearInIngredients, "Ingredient weights can pick equipment");
		}

		// Early rolls must not keep giving the same few items
		private static void TierWindowVariety()
		{
			RequireCoherent();
			HashSet<int> gear = new();
			for (int i = 0; i < 600; i++)
			{
				RollRequest request = new() { Keys = new[] { 6160, i }, SourceTier = 0, OriginalItem = -1, Policy = CategoryPolicy.Weighted, TierJitterUp = 1 };
				int item = ItemPool.Roll(request);
				if (ItemCategories.IsGear(ItemPool.Info[item].Category)) gear.Add(item);
			}
			Check.That(gear.Count >= 25, $"only {gear.Count} different pieces of equipment in 600 early rolls");
		}

		// A pick in a sparse bucket (ammo or food of the early tiers, an empty summon tier) widens to the tiers below until it has
		// ItemPool.MinCandidates items, never above the wanted tier while the tiers up to it hold any (pure picks of every bucket)
		private static void SparseBucketsWiden()
		{
			if (!ItemPool.Ready) Check.Skip("the item pool is not built");
			List<string> failures = new();
			int widened = 0;
			for (int c = 0; c < ItemCategories.Count; c++)
			{
				ItemCategory category = (ItemCategory)c;
				int available = 0;
				for (int t = 0; t < Tiers.Count; t++)
				{
					available += ItemPool.BucketSize(category, t);
					if (available == 0) continue;
					if (ItemPool.BucketSize(category, t) < ItemPool.MinCandidates) widened++;
					HashSet<int> picked = new();
					for (int k = 0; k < 600; k++)
					{
						int item = ItemPool.PickFromBucket(category, t, new Random(DetRandom.Hash(6180, c, t, k)), t);
						picked.Add(item);
						if (ItemPool.TierOf(item) > t && failures.Count < 20) failures.Add($"{category} tier {t}: {Lang.GetItemNameValue(item)} of tier {ItemPool.TierOf(item)}");
					}
					int expected = Math.Min(ItemPool.MinCandidates, available);
					if (picked.Count < expected && failures.Count < 20) failures.Add($"{category} tier {t}: {picked.Count} different items, {expected} expected ({available} up to this tier)");
				}
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"SparseBucketsWiden: {widened} sparse buckets widened");
			Check.All(failures, "Sparse buckets");
		}

		private static void OreMappingBijective()
		{
			List<int> ores = Enumerable.Range(1, ItemPool.Info.Length - 1).Where(t => ItemPool.IsRandomizable(t) && OreMap.IsOre(t)).ToList();
			Check.That(ores.Count >= 8, $"only {ores.Count} ores");
			string N(int item) => Lang.GetItemNameValue(item);
			List<string> failures = new();
			// Each vanilla pair has exactly one ore once the world chose it (both in drunk worlds for the evil and hardmode ores)
			(int a, int b, int chosen, bool both)[] pairs =
			{
				(ItemID.CopperOre, ItemID.TinOre, WorldGen.SavedOreTiers.Copper, false), (ItemID.IronOre, ItemID.LeadOre, WorldGen.SavedOreTiers.Iron, false),
				(ItemID.SilverOre, ItemID.TungstenOre, WorldGen.SavedOreTiers.Silver, false), (ItemID.GoldOre, ItemID.PlatinumOre, WorldGen.SavedOreTiers.Gold, false),
				(ItemID.DemoniteOre, ItemID.CrimtaneOre, WorldGen.crimson ? TileID.Crimtane : TileID.Demonite, Main.drunkWorld),
				(ItemID.CobaltOre, ItemID.PalladiumOre, WorldGen.SavedOreTiers.Cobalt, Main.drunkWorld), (ItemID.MythrilOre, ItemID.OrichalcumOre, WorldGen.SavedOreTiers.Mythril, Main.drunkWorld),
				(ItemID.AdamantiteOre, ItemID.TitaniumOre, WorldGen.SavedOreTiers.Adamantite, Main.drunkWorld)
			};
			foreach ((int a, int b, int chosen, bool both) in pairs)
			{
				bool known = !both && (chosen == ContentSamples.ItemsByType[a].createTile || chosen == ContentSamples.ItemsByType[b].createTile);
				int count = (OreMap.IsPresent(a) ? 1 : 0) + (OreMap.IsPresent(b) ? 1 : 0);
				if (count != (known ? 1 : 2)) failures.Add($"{N(a)}/{N(b)}: {count} present, expected {(known ? 1 : 2)}");
			}
			// With every pair decided (one ore each): a permutation of the world's ores inside each tier, absent ores unchanged
			Func<int, bool> decided = type => OreMap.PairOf(type) < 0 || OreMap.IsPresent(type) && !OreMap.IsPresent(Partner(type));
			Func<int, bool> firstOfPair = type => OreMap.PairOf(type) < 0 || (decided(type) || decided(Partner(type)) ? OreMap.IsPresent(type) && decided(type) : type < Partner(type));
			Dictionary<int, int> map = OreMap.Build(firstOfPair);
			List<int> present = ores.Where(firstOfPair).ToList();
			HashSet<int> images = new();
			foreach (int ore in ores)
			{
				int image = map.TryGetValue(ore, out int x) ? x : ore;
				if (!present.Contains(ore))
				{
					if (image != ore) failures.Add($"{N(ore)} (not in this world) gives {N(image)}");
					continue;
				}
				if (!images.Add(image)) failures.Add($"{N(image)} given by two ores");
				if (!present.Contains(image)) failures.Add($"{N(ore)} gives {N(image)}, not an ore of this world");
				if (ItemPool.TierOf(image) != ItemPool.TierOf(ore)) failures.Add($"{N(ore)} -> {N(image)} changes tier");
			}
			// Every tier keeps an ore of the world
			foreach (int ore in ores)
			{
				if (!present.Any(o => ItemPool.TierOf(o) == ItemPool.TierOf(ore))) failures.Add($"no ore of this world at the tier of {N(ore)}");
			}
			// One-to-one too when some pairs have both ores present next to pairs with one (drunk worlds, undecided hardmode
			// pairs, modded ores): every present ore is given by exactly one present ore of its tier
			void OneToOne(Func<int, bool> presence, string label)
			{
				Dictionary<int, int> m = OreMap.Build(presence);
				HashSet<int> given = new();
				foreach (int ore in ores.Where(presence))
				{
					int image = m.TryGetValue(ore, out int v) ? v : ore;
					if (!presence(image)) failures.Add($"{label}: {N(ore)} gives {N(image)}, absent");
					else if (!given.Add(image)) failures.Add($"{label}: {N(image)} given by two ores");
					if (ItemPool.TierOf(image) != ItemPool.TierOf(ore)) failures.Add($"{label}: {N(ore)} -> {N(image)} changes tier");
				}
			}
			OneToOne(type => OreMap.PairOf(type) is 0 or 2 || firstOfPair(type), "Copper and Silver pairs both present");
			OneToOne(type => OreMap.PairOf(type) >= 4 || firstOfPair(type), "drunk world");
			// An altar deciding a pair only changes what stood for that pair: undecided Cobalt/Palladium, then Cobalt chosen
			Func<int, bool> before = type => OreMap.PairOf(type) == OreMap.PairOf(ItemID.CobaltOre) || firstOfPair(type);
			Func<int, bool> after = type => OreMap.PairOf(type) == OreMap.PairOf(ItemID.CobaltOre) ? type == ItemID.CobaltOre : firstOfPair(type);
			OneToOne(before, "Cobalt undecided");
			OneToOne(after, "Cobalt chosen");
			Dictionary<int, int> mapBefore = OreMap.Build(before);
			Dictionary<int, int> mapAfter = OreMap.Build(after);
			HashSet<int> pair = new() { ItemID.CobaltOre, ItemID.PalladiumOre };
			// Ores of Cobalt's tier may change with it (a cycle with unequal counts stays in place); other tiers never do
			int cobaltTier = ItemPool.TierOf(ItemID.CobaltOre);
			foreach (int ore in ores.Where(o => !pair.Contains(o) && before(o) && ItemPool.TierOf(o) != cobaltTier))
			{
				int was = mapBefore.TryGetValue(ore, out int w) ? w : ore;
				int now = mapAfter.TryGetValue(ore, out int n) ? n : ore;
				int expected = pair.Contains(was) ? ItemID.CobaltOre : was;
				if (now != expected) failures.Add($"{N(ore)} gave {N(was)}, gives {N(now)} once Cobalt is chosen");
			}
			Check.All(failures, "Ore permutation");
		}

		private static int Partner(int ore) => ore switch
		{
			ItemID.CopperOre => ItemID.TinOre, ItemID.TinOre => ItemID.CopperOre, ItemID.IronOre => ItemID.LeadOre, ItemID.LeadOre => ItemID.IronOre,
			ItemID.SilverOre => ItemID.TungstenOre, ItemID.TungstenOre => ItemID.SilverOre, ItemID.GoldOre => ItemID.PlatinumOre, ItemID.PlatinumOre => ItemID.GoldOre,
			ItemID.DemoniteOre => ItemID.CrimtaneOre, ItemID.CrimtaneOre => ItemID.DemoniteOre, ItemID.CobaltOre => ItemID.PalladiumOre, ItemID.PalladiumOre => ItemID.CobaltOre,
			ItemID.MythrilOre => ItemID.OrichalcumOre, ItemID.OrichalcumOre => ItemID.MythrilOre, ItemID.AdamantiteOre => ItemID.TitaniumOre, ItemID.TitaniumOre => ItemID.AdamantiteOre,
			_ => ore
		};
	}
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;

namespace UltimateRandomizer.Common.Core
{
	public struct ItemInfo
	{
		public ItemCategory Category;
		public int Tier;
		public bool Excluded;
		public bool Protected;
	}

	public enum CategoryPolicy
	{
		// Category picked from the configured weights (mob drops)
		Weighted,
		// Same category as the original item (recipes, shops, bags...)
		SameAsOriginal,
		// Any category (chaos mode)
		Any
	}

	public struct RollRequest
	{
		public int[] Keys;
		public int SourceTier;
		public int OriginalItem;
		public CategoryPolicy Policy;
		public int TierJitterDown;
		public int TierJitterUp;
		public bool AllowJackpot;
		public bool IgnoreGating;
		// The progression lock never goes below this tier (a boss can always drop items of its own level)
		public int MinLimit;
	}

	// Every randomizable item sorted by category and tier. All features pick their items here.
	public static class ItemPool
	{
		public static ItemInfo[] Info { get; private set; } = Array.Empty<ItemInfo>();

		public static int ContentHash { get; private set; }

		public static bool Ready => Info.Length > 0;

		// Buckets[category][tier] = item types, sorted so the order is the same on every machine
		private static int[][][] buckets;
		private static bool[] categoryHasItems;
		private static int[] shuffleMap;

		public static void Build()
		{
			Stopwatch watch = Stopwatch.StartNew();
			int count = ItemLoader.ItemCount;
			DetRandom.BuildItemKeys(count);

			GeneralConfig general = GeneralConfig.Instance;
			HashSet<int> excludedItems = general.ExcludedItems.Where(d => d != null && !d.IsUnloaded).Select(d => d.Type).ToHashSet();
			HashSet<string> excludedMods = general.ExcludedMods.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()).ToHashSet();
			Dictionary<int, Tier> overrides = new();
			foreach (TierOverride o in ProgressionConfig.Instance.TierOverrides)
			{
				if (o?.Item != null && !o.Item.IsUnloaded) overrides[o.Item.Type] = o.Tier;
			}
			HashSet<int> protectedItems = Protection.BuildProtectedSet();

			ItemInfo[] info = new ItemInfo[count];
			List<int>[,] lists = new List<int>[ItemCategories.Count, Tiers.Count];
			for (int c = 0; c < ItemCategories.Count; c++)
			{
				for (int t = 0; t < Tiers.Count; t++) lists[c, t] = new List<int>();
			}
			int hash = count;
			for (int type = 1; type < count; type++)
			{
				hash = DetRandom.Hash(hash, DetRandom.ItemKey(type));
				if (!ContentSamples.ItemsByType.TryGetValue(type, out Item item) || ItemClassifier.IsExcluded(type, item, excludedItems, excludedMods))
				{
					info[type] = new ItemInfo { Excluded = true };
					continue;
				}
				ItemCategory category = ItemClassifier.Classify(type, item);
				int tier = ItemClassifier.ComputeTier(type, item, overrides);
				bool isProtected = protectedItems.Contains(type);
				info[type] = new ItemInfo { Category = category, Tier = tier, Protected = isProtected };
				if (!isProtected) lists[(int)category, tier].Add(type);
			}

			buckets = new int[ItemCategories.Count][][];
			categoryHasItems = new bool[ItemCategories.Count];
			for (int c = 0; c < ItemCategories.Count; c++)
			{
				buckets[c] = new int[Tiers.Count][];
				for (int t = 0; t < Tiers.Count; t++)
				{
					buckets[c][t] = lists[c, t].ToArray();
					if (buckets[c][t].Length > 0) categoryHasItems[c] = true;
				}
			}
			Info = info;
			ContentHash = hash;
			shuffleMap = null;
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Classified {count} items in {watch.ElapsedMilliseconds} ms ({protectedItems.Count} protected).");
		}

		public static void Unload()
		{
			Info = Array.Empty<ItemInfo>();
			buckets = null;
			categoryHasItems = null;
			shuffleMap = null;
		}

		public static void OnSeedChanged() => shuffleMap = null;

		public static bool IsValid(int type) => type > 0 && type < Info.Length;

		// Can this item be replaced by another one?
		public static bool IsRandomizable(int type) => IsValid(type) && !Info[type].Excluded && !Info[type].Protected;

		public static int BucketSize(ItemCategory category, int tier) => buckets == null ? 0 : buckets[(int)category][tier].Length;

		public static int Roll(in RollRequest request) => Roll(request, out _);

		public static int Roll(in RollRequest request, out int rolledTier)
		{
			RandomizerMode mode = GeneralConfig.Instance.RandomizerMode;
			ProgressionConfig progression = ProgressionConfig.Instance;
			bool hasOriginal = IsRandomizable(request.OriginalItem);

			if (mode == RandomizerMode.Shuffle && hasOriginal)
			{
				rolledTier = Info[request.OriginalItem].Tier;
				return Shuffled(request.OriginalItem);
			}

			Random rng = DetRandom.Get(request.Keys);
			if (mode == RandomizerMode.Chaos)
			{
				int picked = PickFromBucket(AnyCategory(rng), rng.Next(Tiers.Count), rng);
				rolledTier = TierOf(picked);
				return picked;
			}

			int target = request.SourceTier + rng.Next(-request.TierJitterDown, request.TierJitterUp + 1);
			if (request.AllowJackpot && rng.NextDouble() * 100 < progression.JackpotTierChance)
			{
				target += rng.Next(2, 4);
			}
			target = Tiers.Clamp(target);
			ItemCategory category = request.Policy switch
			{
				CategoryPolicy.SameAsOriginal when hasOriginal => Info[request.OriginalItem].Category,
				CategoryPolicy.Any => AnyCategory(rng),
				_ => WeightedCategory(rng, LootConfig.Instance.DropCategoryWeights)
			};
			bool gated = !request.IgnoreGating && progression.TierGating;
			int limit = gated ? Tiers.Clamp(Math.Max(ProgressionSystem.UnlockedTier + progression.TierAllowance, request.MinLimit)) : Tiers.Max;
			if (target <= limit)
			{
				int intended = PickFromBucket(category, target, rng, limit);
				rolledTier = TierOf(intended);
				return intended;
			}

			// Not unlocked yet: a stand-in from the highest allowed tier, until progression catches up
			int[] fallbackKeys = new int[request.Keys.Length + 2];
			request.Keys.CopyTo(fallbackKeys, 0);
			fallbackKeys[^2] = RollKey.Fallback;
			fallbackKeys[^1] = limit;
			int fallback = PickFromBucket(category, limit, DetRandom.Get(fallbackKeys), limit);
			rolledTier = TierOf(fallback);
			return fallback;
		}

		public static int TierOf(int type) => IsValid(type) ? Info[type].Tier : 0;

		// Looks for an item in the wanted tier, then the tiers below, then above; then in other categories
		public static int PickFromBucket(ItemCategory category, int tier, Random rng, int maxTier = Tiers.Max)
		{
			if (buckets == null) return ItemID.DirtBlock;
			if (!categoryHasItems[(int)category]) category = categoryHasItems[(int)ItemCategory.Material] ? ItemCategory.Material : ItemCategory.Misc;
			int[][] tiers = buckets[(int)category];
			for (int t = tier; t >= 0; t--)
			{
				if (tiers[t].Length > 0) return tiers[t][rng.Next(tiers[t].Length)];
			}
			// Above the wanted tier, but never above what progression allows
			for (int t = tier + 1; t <= Math.Min(maxTier, Tiers.Max); t++)
			{
				if (tiers[t].Length > 0) return tiers[t][rng.Next(tiers[t].Length)];
			}
			return category is ItemCategory.Material or ItemCategory.Misc ? ItemID.Gel : PickFromBucket(ItemCategory.Material, tier, rng, maxTier);
		}

		public static ItemCategory WeightedCategory(Random rng, CategoryWeights weights)
		{
			int total = 0;
			for (int c = 0; c < ItemCategories.Count; c++)
			{
				if (categoryHasItems[c]) total += weights.Get((ItemCategory)c);
			}
			if (total <= 0) return ItemCategory.Material;
			int roll = rng.Next(total);
			for (int c = 0; c < ItemCategories.Count; c++)
			{
				if (!categoryHasItems[c]) continue;
				roll -= weights.Get((ItemCategory)c);
				if (roll < 0) return (ItemCategory)c;
			}
			return ItemCategory.Material;
		}

		private static ItemCategory AnyCategory(Random rng)
		{
			List<int> available = new();
			for (int c = 0; c < ItemCategories.Count; c++)
			{
				if (categoryHasItems[c] && c != (int)ItemCategory.BossSummon) available.Add(c);
			}
			return available.Count == 0 ? ItemCategory.Material : (ItemCategory)available[rng.Next(available.Count)];
		}

		// Guaranteed shuffle: a permutation inside each (category, tier) group, so every item still exists somewhere
		public static int Shuffled(int type)
		{
			if (!IsRandomizable(type)) return type;
			shuffleMap ??= BuildShuffleMap();
			return shuffleMap[type];
		}

		private static int[] BuildShuffleMap()
		{
			int[] map = new int[Info.Length];
			for (int i = 0; i < map.Length; i++) map[i] = i;
			HashSet<int> obtainable = Obtainability.Collect();
			for (int c = 0; c < ItemCategories.Count; c++)
			{
				// Tiny groups are merged with the next tier, otherwise their items could only map to themselves
				List<List<int>> groups = new();
				List<int> current = new();
				for (int t = 0; t < Tiers.Count; t++)
				{
					// Only items that exist somewhere in the world: so every one of them stays obtainable
					current.AddRange(buckets[c][t].Where(obtainable.Contains));
					if (current.Count >= 3)
					{
						groups.Add(current);
						current = new List<int>();
					}
				}
				if (current.Count > 0)
				{
					if (groups.Count > 0) groups[^1].AddRange(current);
					else groups.Add(current);
				}
				for (int g = 0; g < groups.Count; g++)
				{
					List<int> group = groups[g];
					int[] permuted = group.ToArray();
					Random rng = DetRandom.Get(RollKey.Shuffle, c, g);
					for (int i = permuted.Length - 1; i > 0; i--)
					{
						int j = rng.Next(i + 1);
						(permuted[i], permuted[j]) = (permuted[j], permuted[i]);
					}
					for (int i = 0; i < group.Count; i++) map[group[i]] = permuted[i];
				}
			}
			return map;
		}
	}

	public class ItemPoolSystem : ModSystem
	{
		// Runs after recipes are set up: Item.material and the recipe list are final here
		public override void PostSetupRecipes() => ItemPool.Build();

		public override void Unload()
		{
			ItemPool.Unload();
			DetRandom.Unload();
			NpcTiers.Unload();
			LootSnapshot.Unload();
			Obtainability.Unload();
		}
	}
}

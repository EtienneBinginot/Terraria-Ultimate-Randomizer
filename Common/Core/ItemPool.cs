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
		// The tier its rarity (or KnownTiers, an override) gives, before vanilla recipes lower it (ItemClassifier.LowerByRecipes)
		public int RarityTier;
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
		// Category weights of the Weighted policy (null = loot weights)
		public WeightTable Weights;
		// Madness (WildDropChance): the drop may go above the progression limit. Runtime drops only, with their own
		// rng (WildRng, required): plans, findability and guarantees never set it, so they stay classic
		public bool Wild;
		public Random WildRng;
		// Multiplies WildDropChance for this roll (0: 1), still with the runtime rng only (giant and mini bonus drops, weak
		// enemies: RandomNpcDropRule.JackpotWorth). To disable the madness of a roll, leave Wild false
		public float WildChanceMultiplier;
	}

	// Every randomizable item sorted by category and tier. All features pick their items here.
	public static class ItemPool
	{
		public static ItemInfo[] Info { get; private set; } = Array.Empty<ItemInfo>();

		public static int ContentHash { get; private set; }

		// The tier of every item, excluded ones too (they may be recipe ingredients), after the tiers from recipes (tests)
		public static int[] AllTiers { get; private set; } = Array.Empty<int>();

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
			// Tiers of every item first (excluded ones too: they may be ingredients), then lowered by the vanilla recipes
			int[] rarityTiers = new int[count];
			for (int type = 1; type < count; type++)
			{
				rarityTiers[type] = ContentSamples.ItemsByType.TryGetValue(type, out Item sample) && sample != null ? ItemClassifier.ComputeTier(type, sample, overrides) : Tiers.Max;
			}
			int[] tiers = (int[])rarityTiers.Clone();
			int lowered = ItemClassifier.LowerByRecipes(tiers, overrides, out string loweredExamples);
			for (int type = 1; type < count; type++)
			{
				hash = DetRandom.Hash(hash, DetRandom.ItemKey(type));
				if (!ContentSamples.ItemsByType.TryGetValue(type, out Item item) || ItemClassifier.IsExcluded(type, item, excludedItems, excludedMods))
				{
					info[type] = new ItemInfo { Excluded = true };
					continue;
				}
				ItemCategory category = ItemClassifier.Classify(type, item);
				int tier = tiers[type];
				bool isProtected = protectedItems.Contains(type);
				info[type] = new ItemInfo { Category = category, Tier = tier, RarityTier = rarityTiers[type], Protected = isProtected };
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
			AllTiers = tiers;
			ContentHash = hash;
			shuffleMap = null;
			progressionStations = null;
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Classified {count} items in {watch.ElapsedMilliseconds} ms ({protectedItems.Count} protected), "
				+ $"tiers from recipes: {lowered} lowered ({loweredExamples}).");
		}

		public static void Unload()
		{
			WildChanceOverride = null;
			Info = Array.Empty<ItemInfo>();
			AllTiers = Array.Empty<int>();
			buckets = null;
			categoryHasItems = null;
			shuffleMap = null;
			progressionStations = null;
			OreMap.Reset();
		}

		public static void OnSeedChanged()
		{
			shuffleMap = null;
			OreMap.Reset();
			WorldCaches.ResetAll();
		}

		public static bool IsValid(int type) => type > 0 && type < Info.Length;

		// Can this item be replaced by another one?
		public static bool IsRandomizable(int type) => IsValid(type) && !Info[type].Excluded && !Info[type].Protected;

		public static int BucketSize(ItemCategory category, int tier) => buckets == null ? 0 : buckets[(int)category][tier].Length;

		// The items of a category and tier, in the same order on every machine (do not modify)
		public static int[] Bucket(ItemCategory category, int tier) => buckets == null ? Array.Empty<int>() : buckets[(int)category][Tiers.Clamp(tier)];

		// Tests only (restored in finally): replaces WildDropChance and its progression share (JackpotShare), to check that
		// plans don't depend on it and to force madness
		public static float? WildChanceOverride;

		// The share of both jackpot chances (luck: DropHelper.RollLucky, madness: Roll) at an unlocked tier: JackpotStartShare
		// before the first boss, rising linearly to all of them at Tier.PostPlantera. Runtime rolls only
		public static float JackpotShare(int unlockedTier)
		{
			float start = Math.Clamp(ProgressionConfig.Instance.JackpotStartShare / 100f, 0f, 1f);
			const int full = (int)Tier.PostPlantera;
			return unlockedTier >= full ? 1f : start + (1f - start) * Math.Max(0, unlockedTier) / full;
		}

		// The highest tier a jackpot may reach with this progression limit: MaxJackpotTiers above it, and never a hardmode
		// item before hardmode (may be below the limit: then no jackpot at all)
		public static int JackpotCeiling(int gatingLimit)
		{
			int ceiling = Tiers.Clamp(gatingLimit + Math.Max(1, ProgressionConfig.Instance.MaxJackpotTiers));
			return Main.hardMode ? ceiling : Math.Min(ceiling, (int)Tier.PostSkeletron);
		}

		// Jackpots: a runtime roll (WildRng set: at kill or bag opening, never in plans) whose item is above the progression
		// limit, by madness (WildDropChance) or luck (RuntimeJackpotChance, IgnoreGating), and useful (equipment or a
		// progression station): a roll above the limit on an ore or a block is no jackpot, the normal roll stands. The last Roll says by how many tiers it is
		// above the limit (0: no jackpot). Rolls run on the game thread only
		public static int LastJackpotGap { get; private set; }

		public static void ClearJackpot() => LastJackpotGap = 0;

		private static int[] progressionStations;

		// Placers of progression stations (Hellforge, hardmode anvils and forges, Ancient Manipulator...) that may be rolled
		private static int[] ProgressionStations()
		{
			if (progressionStations != null) return progressionStations;
			List<int> list = new();
			for (int type = 1; type < Info.Length; type++)
			{
				if (IsRandomizable(type) && Stations.PlacedTile(type, out int tile) && !Stations.Info(tile).World && Stations.Info(tile).Kind == StationKind.Progression) list.Add(type);
			}
			return progressionStations = list.ToArray();
		}

		// Worth a jackpot: equipment that does something (weapons, armor with defense, accessories, tools, mounts) or a
		// progression station; never a music box (an accessory), vanity, a pet or light pet, nor a dye
		public static bool IsJackpotWorthy(int type) =>
			IsValid(type) && (ItemCategories.IsGear(Info[type].Category) && UsefulGear(type) || Array.IndexOf(ProgressionStations(), type) >= 0);

		private static bool UsefulGear(int type)
		{
			if (!ContentSamples.ItemsByType.TryGetValue(type, out Item item)) return false;
			if (item.vanity || item.dye > 0 || item.hairDye >= 0 || item.createTile == TileID.MusicBoxes) return false;
			if (item.buffType > 0 && item.buffType < Main.vanityPet.Length && (Main.vanityPet[item.buffType] || Main.lightPet[item.buffType])) return false;
			return Info[type].Category switch
			{
				ItemCategory.Armor => item.defense > 0,
				ItemCategory.MountPet => item.mountType >= 0,
				ItemCategory.WeaponMelee or ItemCategory.WeaponRanged or ItemCategory.WeaponMagic or ItemCategory.WeaponSummon or ItemCategory.WeaponOther => item.damage > 0,
				_ => true
			};
		}

		// The limit progression sets for a roll, whether or not this one ignores it
		public static int GatingLimit(int minLimit)
		{
			ProgressionConfig progression = ProgressionConfig.Instance;
			return progression.TierGating ? Tiers.Clamp(Math.Max(ProgressionSystem.UnlockedTier + progression.TierAllowance, minLimit)) : Tiers.Max;
		}

		// The inverse of GatingLimit: the unlocked tier from which gated rolls may give an item of this tier
		public static int UnlockTier(int tier)
		{
			ProgressionConfig progression = ProgressionConfig.Instance;
			return progression.TierGating ? Math.Max(0, tier - progression.TierAllowance) : 0;
		}

		public static int Roll(in RollRequest request) => Roll(request, out _);

		public static int Roll(in RollRequest request, out int rolledTier)
		{
			LastJackpotGap = 0;
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
				_ => WeightedCategory(rng, request.Weights ?? LootConfig.Instance.DropCategoryWeights)
			};
			int window = LootConfig.Instance.TierWindow;
			int limit = request.IgnoreGating ? Tiers.Max : GatingLimit(request.MinLimit);
			// Some categories have nothing in the early tiers (dyes, summon weapons...): rather than changing the category,
			// a replacement may go up to the original item's own tier, which this source already gives in vanilla
			int categoryLimit = hasOriginal && category == Info[request.OriginalItem].Category ? Math.Max(limit, Info[request.OriginalItem].Tier) : limit;
			// Bags and summons stay at the limit: a wild boss bag would hand out its whole tier (Hallowed Bars before hardmode...)
			if (request.Wild && request.WildRng != null && limit < Tiers.Max && category is not (ItemCategory.Bag or ItemCategory.BossSummon))
			{
				// Only the runtime rng is used here: without a wild roll, the result is the same as without the flag
				float baseChance = WildChanceOverride ?? progression.WildDropChance * JackpotShare(ProgressionSystem.UnlockedTier);
				float wildChance = Math.Min(100f, baseChance * (request.WildChanceMultiplier > 0 ? request.WildChanceMultiplier : 1f));
				int wildTier = WildTier(limit, wildChance, progression.WildTierDecay, request.WildRng, JackpotCeiling(limit));
				if (wildTier > limit)
				{
					// Only something useful is a jackpot: a wild ore or block is no jackpot, the normal roll stands
					int wild = PickFromBucket(category, wildTier, request.WildRng, wildTier);
					if (TierOf(wild) > limit && IsJackpotWorthy(wild))
					{
						LastJackpotGap = TierOf(wild) - limit;
						rolledTier = TierOf(wild);
						return wild;
					}
				}
			}
			if (target <= limit)
			{
				int intended = PickFromBucket(category, target, rng, categoryLimit, window);
				// A lucky runtime roll above the limit it ignores is a jackpot when useful; otherwise it is no jackpot, and the
				// item is the one the limit allows
				if (request.IgnoreGating && request.WildRng != null)
				{
					int gatingLimit = GatingLimit(request.MinLimit);
					if (TierOf(intended) > gatingLimit)
					{
						// Too far above the limit (MaxJackpotTiers, the hardmode wall) counts as not useful: rolled within the limit
						if (IsJackpotWorthy(intended) && TierOf(intended) <= JackpotCeiling(gatingLimit)) LastJackpotGap = TierOf(intended) - gatingLimit;
						else
						{
							RollRequest normal = request;
							normal.IgnoreGating = false;
							normal.Wild = false;
							return Roll(normal, out rolledTier);
						}
					}
				}
				rolledTier = TierOf(intended);
				return intended;
			}

			// Not unlocked yet: a stand-in from the highest allowed tier, until progression catches up
			int[] fallbackKeys = new int[request.Keys.Length + 2];
			request.Keys.CopyTo(fallbackKeys, 0);
			fallbackKeys[^2] = RollKey.Fallback;
			fallbackKeys[^1] = limit;
			int fallback = PickFromBucket(category, limit, DetRandom.Get(fallbackKeys), categoryLimit, window);
			rolledTier = TierOf(fallback);
			return fallback;
		}

		public static int TierOf(int type) => IsValid(type) ? Info[type].Tier : 0;

		// The wild tier tail: with chance (%), limit + k where P(k) is proportional to decay^-k, up to maxTier (the
		// weights are renormalized over the tiers allowed, so each step stays `decay` times rarer). Else the limit.
		public static int WildTier(int limit, float chance, float decay, Random rng, int maxTier = Tiers.Max)
		{
			maxTier = Math.Min(maxTier, Tiers.Max);
			int steps = maxTier - limit;
			if (steps <= 0 || chance <= 0 || rng.NextDouble() * 100 >= chance) return Math.Min(limit, Tiers.Max);
			double ratio = 1.0 / Math.Max(1.01, decay);
			double total = 0, weight = 1;
			for (int k = 1; k <= steps; k++) total += weight *= ratio;
			double roll = rng.NextDouble() * total;
			weight = 1;
			for (int k = 1; k < steps; k++)
			{
				weight *= ratio;
				if (roll < weight) return limit + k;
				roll -= weight;
			}
			return maxTier;
		}

		// Rolls the replacement of an existing item. With surpriseChance (%), the category is not kept: the item is
		// rolled with the loot weights instead. Bags, boss summons and critters always keep their category.
		public static int RollReplacement(RollRequest request, float surpriseChance, Random surpriseRng)
		{
			if (IsSurprise(request.OriginalItem, surpriseChance, surpriseRng))
			{
				int[] keys = new int[request.Keys.Length + 2];
				request.Keys.CopyTo(keys, 0);
				keys[^2] = RollKey.Surprise;
				keys[^1] = surpriseRng.Next();
				request.Keys = keys;
				request.Policy = CategoryPolicy.Weighted;
				request.Weights = null;
			}
			return Roll(request);
		}

		public static bool IsSurprise(int originalItem, float surpriseChance, Random surpriseRng)
		{
			if (surpriseRng == null || surpriseChance <= 0 || GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent) return false;
			if (!IsRandomizable(originalItem)) return false;
			if (Info[originalItem].Category is ItemCategory.Bag or ItemCategory.BossSummon or ItemCategory.Critter) return false;
			return surpriseRng.NextDouble() * 100 < surpriseChance;
		}

		// Looks for an item in the wanted tier (and up to `window` tiers below, for variety), then the tiers below,
		// then above; then in other categories
		public static int PickFromBucket(ItemCategory category, int tier, Random rng, int maxTier = Tiers.Max, int window = 0)
		{
			if (buckets == null) return ItemID.DirtBlock;
			if (!categoryHasItems[(int)category]) category = categoryHasItems[(int)ItemCategory.Material] ? ItemCategory.Material : ItemCategory.Misc;
			int[][] tiers = buckets[(int)category];
			if (window > 0)
			{
				int low = Math.Max(0, tier - window);
				int total = 0;
				for (int t = low; t <= tier; t++) total += tiers[t].Length;
				if (total > 0)
				{
					int index = rng.Next(total);
					for (int t = low; t <= tier; t++)
					{
						if (index < tiers[t].Length) return tiers[t][index];
						index -= tiers[t].Length;
					}
				}
			}
			for (int t = tier; t >= 0; t--)
			{
				if (tiers[t].Length > 0) return tiers[t][rng.Next(tiers[t].Length)];
			}
			// Above the wanted tier, but never above what progression allows
			for (int t = tier + 1; t <= Math.Min(maxTier, Tiers.Max); t++)
			{
				if (tiers[t].Length > 0) return tiers[t][rng.Next(tiers[t].Length)];
			}
			return category is ItemCategory.Material or ItemCategory.Misc ? ItemID.Gel : PickFromBucket(ItemCategory.Material, tier, rng, maxTier, window);
		}

		public static ItemCategory WeightedCategory(Random rng, WeightTable weights)
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

	// Results computed once per world from the seed and settings (guaranteed sources, findability, recipe plans...):
	// cleared whenever the seed or the settings change
	public static class WorldCaches
	{
		public static event Action Reset;

		public static void ResetAll() => Reset?.Invoke();
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

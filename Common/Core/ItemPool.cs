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
		// The tier from which it may be given at random (>= Tier): raised for crafted items that no recipe makes before it
		// (Pearlwood Sword: rarity 0, Pearlwood is hardmode). The buckets and the plans keep Tier; rolls re-pick an item
		// whose TrueTier is above their ceiling (ItemClassifier.RaiseByRecipes, ItemPool.Repick)
		public int TrueTier;
		// The earliest vanilla source (drop, bag, shop, recipe, gathered: ItemClassifier.RaiseBySources), -1 without any;
		// Source describes it, SourceRaised: it raised TrueTier (TierAudit)
		public int SourceTier;
		public string Source;
		public bool SourceRaised;
		// Never given at random (storage mod items, inactive vanilla guides, tModLoader's own items: ItemClassifier.IsBanned): re-picked
		public bool Banned;
		// Its true tier (or ban) comes from the hand-reviewed MaterialTiers table
		public bool FromTable;
		// Its true tier was lowered by a world-generated chest that holds it (ItemClassifier.LowerByWorldChests)
		public bool FromWorldChest;
		// Its true tier was lowered by a reliable vanilla source (a boss drop, a shop...), a hand fix (ItemClassifier.TrueTierFixes)
		// or its recipes settled after them (ItemClassifier.SettleByRecipes): it may be below Tier
		public bool Corrected;
		// A hard progression gate (ItemClassifier.HardGates: luminite, chlorophyte, ectoplasm, mechanical souls... and what is only
		// made from them): no TierAllowance (UnlockTierOf, GatingLimitFor)
		public bool HardGate;
		public bool Excluded;
		public bool Protected;
		// A life or mana upgrade (Protection.Upgrades): replaceable, Misc, but in no bucket (never rolled, never a new
		// ingredient), kept where vanilla drops it; its source is one random recipe of its own (RecipePlanner.SpecialRecipe)
		public bool Special;
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
		// An explicit ceiling (null: none), gated or not: nothing above it, by Tier or TrueTier, jackpots included (the
		// hardmode wall of fishing and shimmer, chest caps)
		public int? MaxTier;
		// The progression the gating limit reads (null: the live one, ProgressionSystem.UnlockedTier and Main.hardMode): plans
		// simulated at another progression (the shop dump and tests)
		public int? GatingTier;
		public bool GatingHardMode;
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
			// Then the true tiers: never below the final tiers, raised for crafted items without any loot source
			int[] trueTiers = (int[])tiers.Clone();
			int raised = ItemClassifier.RaiseByRecipes(trueTiers, overrides, LootSources(), out string raisedExamples);
			// Then by the earliest vanilla source of every item (Portal Gun: only the Moon Lord drops it)
			int[] beforeSources = (int[])trueTiers.Clone();
			int[] sourceTiers = new int[count];
			string[] sourceNames = new string[count];
			int[] reliableTiers = new int[count];
			int bySources = ItemClassifier.RaiseBySources(trueTiers, overrides, sourceTiers, sourceNames, reliableTiers, out string sourceExamples);
			// A reliable source lowers the true tier below the rarity's (Slimy Saddle: the King Slime; Plantera's weapons): never
			// below that source, never a fixed tier (KnownTiers, config overrides). Tier and the buckets never change
			bool[] corrected = new bool[count];
			int byReliable = 0;
			for (int type = 1; type < count; type++)
			{
				if (reliableTiers[type] == int.MaxValue || overrides.ContainsKey(type) || ItemClassifier.HasKnownTier(type)) continue;
				if (reliableTiers[type] >= Math.Max(tiers[type], trueTiers[type])) continue;
				trueTiers[type] = reliableTiers[type];
				corrected[type] = true;
				byReliable++;
			}
			// Then the hand-reviewed materials (MaterialTiers) and the hand fixes (ItemClassifier.TrueTierFixes): their true tier is
			// exactly the table's, whatever the recipes and sources computed (lower too); null bans them. The config overrides still
			// win. Tier and the buckets never change
			bool[] fromTable = new bool[count];
			bool[] fixedTier = new bool[count];
			bool[] tableBanned = new bool[count];
			int tableChanged = 0;
			void SetExactly(int type, int tier)
			{
				int set = Tiers.Clamp(tier);
				if (set != (corrected[type] ? trueTiers[type] : Math.Max(tiers[type], trueTiers[type]))) tableChanged++;
				trueTiers[type] = set;
				fixedTier[type] = true;
			}
			foreach ((int type, (int? tier, _)) in MaterialTiers.Table)
			{
				if (type <= 0 || type >= count || overrides.ContainsKey(type)) continue;
				fromTable[type] = true;
				if (tier == null)
				{
					tableBanned[type] = true;
					fixedTier[type] = true;
					continue;
				}
				SetExactly(type, tier.Value);
			}
			foreach ((int type, Tier tier) in ItemClassifier.TrueTierFixes)
			{
				if (type <= 0 || type >= count || overrides.ContainsKey(type) || fromTable[type]) continue;
				SetExactly(type, (int)tier);
				corrected[type] = true;
			}
			// Then the chests of world generation: what they hold is found from their tier (lower only)
			bool[] fromChest = new bool[count];
			int byChests = ItemClassifier.LowerByWorldChests(trueTiers, tiers, fixedTier, corrected, overrides, fromChest, sourceTiers, sourceNames, out string chestExamples);
			// The effective true tiers (never below Tier, except what the steps above lowered on purpose), with their mining floor;
			// then the crafted items follow their ingredients' final tiers (SettleByRecipes) and the hard gates are found
			int[] floors = new int[count];
			for (int type = 1; type < count; type++)
			{
				if (overrides.ContainsKey(type) || ItemClassifier.HasKnownTier(type)) fixedTier[type] = true;
				if (!fromTable[type] && !fromChest[type] && !corrected[type]) trueTiers[type] = Math.Max(tiers[type], trueTiers[type]);
			}
			for (int type = 1; type < count; type++)
			{
				if (fixedTier[type]) continue;
				floors[type] = ItemClassifier.MiningFloor(type, trueTiers);
				trueTiers[type] = Math.Max(trueTiers[type], floors[type]);
			}
			HashSet<int> lootSources = LootSources();
			int settled = ItemClassifier.SettleByRecipes(trueTiers, fixedTier, floors, lootSources, fromChest, corrected, out string settleExamples);
			bool[] hardGates = ItemClassifier.HardGates(trueTiers, lootSources, fromChest);
			int banned = 0;
			for (int type = 1; type < count; type++)
			{
				hash = DetRandom.Hash(hash, DetRandom.ItemKey(type));
				if (!ContentSamples.ItemsByType.TryGetValue(type, out Item item) || ItemClassifier.IsExcluded(type, item, excludedItems, excludedMods))
				{
					info[type] = new ItemInfo { Excluded = true };
					continue;
				}
				bool isProtected = protectedItems.Contains(type);
				bool special = !isProtected && Protection.IsUpgrade(type);
				ItemCategory category = special ? ItemCategory.Misc : ItemClassifier.Classify(type, item);
				int tier = tiers[type];
				// Banned items stay in their bucket (the buckets never change): rolls re-pick them
				bool isBanned = ItemClassifier.IsBanned(item) || tableBanned[type];
				if (isBanned) banned++;
				// Final (a material of the table, a world chest item or a corrected one keeps its tier even below its bucket's tier)
				int trueTier = trueTiers[type];
				info[type] = new ItemInfo { Category = category, Tier = tier, RarityTier = rarityTiers[type], TrueTier = trueTier, Banned = isBanned, Protected = isProtected,
					SourceTier = sourceTiers[type], Source = sourceNames[type], SourceRaised = !fixedTier[type] && trueTier > Math.Max(tier, beforeSources[type]),
					FromTable = fromTable[type], FromWorldChest = fromChest[type], Corrected = corrected[type] && !fromChest[type], HardGate = hardGates[type],
					Special = special };
				// Upgrades stay out of the buckets: the buckets (and every plan rolled from them) are the same as when they were protected
				if (!isProtected && !special) lists[(int)category, tier].Add(type);
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
			log4net.ILog logger = ModContent.GetInstance<UltimateRandomizer>().Logger;
			logger.Info($"Classified {count} items in {watch.ElapsedMilliseconds} ms ({protectedItems.Count} protected, {banned} banned), "
				+ $"tiers from recipes: {lowered} lowered ({loweredExamples}), {raised} raised ({raisedExamples}); "
				+ $"tiers from sources: {bySources} raised ({sourceExamples}), {byReliable} lowered by a reliable source; material table and fixes: "
				+ $"{fromTable.Count(f => f)} + {ItemClassifier.TrueTierFixes.Count} listed, {tableChanged} true tiers changed, {tableBanned.Count(b => b)} banned; "
				+ $"world chests: {byChests} true tiers lowered ({chestExamples}); settled by recipes: {settled} ({settleExamples}); {hardGates.Count(g => g)} hard gates.");
			LogSuspicious(logger);
		}

		// Items something drops, a bag holds or a shop sells in the base game (captured at load, before recipes are set up)
		private static HashSet<int> LootSources()
		{
			HashSet<int> set = new(Obtainability.ShopItems);
			foreach (List<Terraria.GameContent.ItemDropRules.DropRateInfo> drops in LootSnapshot.Npcs.Values) set.UnionWith(drops.Select(d => d.itemId));
			foreach (List<Terraria.GameContent.ItemDropRules.DropRateInfo> drops in LootSnapshot.Items.Values) set.UnionWith(drops.Select(d => d.itemId));
			set.UnionWith(LootSnapshot.GlobalDrops().Select(d => d.itemId));
			return set;
		}

		// Pooled items that nothing gives (no drop, bag, shop, recipe, nor gathered) and that can't be researched: often
		// internal or creative items of a mod. Logged only, to ban them by name later if they show up in game
		private static void LogSuspicious(log4net.ILog logger)
		{
			HashSet<int> sources = LootSources();
			for (int i = 0; i < Recipe.numRecipes; i++)
			{
				if (Main.recipe[i]?.createItem != null && !Main.recipe[i].Disabled) sources.Add(Main.recipe[i].createItem.type);
			}
			List<string> suspicious = new();
			for (int type = 1; type < Info.Length; type++)
			{
				ItemInfo info = Info[type];
				if (info.Excluded || info.Protected || info.Banned || sources.Contains(type)) continue;
				if (info.Category is ItemCategory.Block or ItemCategory.Critter) continue;
				if (!ContentSamples.ItemsByType.TryGetValue(type, out Item item) || item.ResearchUnlockCount != 0) continue;
				if (item.createTile >= TileID.Dirt && TileID.Sets.Ore[item.createTile]) continue;
				suspicious.Add(DetRandom.ItemName(type));
			}
			if (suspicious.Count > 0) logger.Info($"{suspicious.Count} suspicious items (no source, not researchable): {string.Join(", ", suspicious.Take(40))}");
		}

		public static void Unload()
		{
			WildChanceOverride = null;
			RepickDisabled = false;
			LegacyGating = false;
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

		public static bool IsSpecial(int type) => IsValid(type) && Info[type].Special;

		public static int BucketSize(ItemCategory category, int tier) => buckets == null ? 0 : buckets[(int)category][tier].Length;

		// The items of a category and tier, in the same order on every machine (do not modify)
		public static int[] Bucket(ItemCategory category, int tier) => buckets == null ? Array.Empty<int>() : buckets[(int)category][Tiers.Clamp(tier)];

		// Tests only (restored in finally): replaces WildDropChance, to check that
		// plans don't depend on it and to force madness
		public static float? WildChanceOverride;

		// Chance (0-1) that a lucky item `gap` tiers above the limit stays a jackpot: 1 for one tier, then WildTierDecay times
		// rarer for each more, as the madness tail (WildTier). No cap: a jackpot is the only way to get a hardmode item before
		// the Wall of Flesh (GatingLimit and the runtime caps stop everything else there)
		public static double LuckyKeepChance(int gap) => gap <= 1 ? 1.0 : Math.Pow(Math.Max(1.01, ProgressionConfig.Instance.WildTierDecay), -(gap - 1));

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
			IsValid(type) && !Info[type].Banned && (ItemCategories.IsGear(Info[type].Category) && UsefulGear(type) || Array.IndexOf(ProgressionStations(), type) >= 0);

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
		public static int GatingLimit(int minLimit) => GatingLimit(ProgressionSystem.UnlockedTier, Main.hardMode, minLimit);

		// TierAllowance tiers above the unlocked tier, but no hardmode item before hardmode (only a jackpot goes there), except
		// for a source whose floor is higher: the Wall of Flesh drops its loot before Main.hardMode is set (NPC.NPCLoot calls
		// NPCLoot_DropItems, then DoDeathEvents, which calls WorldGen.StartHardmode), its MinLimit keeps them
		public static int GatingLimit(int unlockedTier, bool hardMode, int minLimit) => GatingLimit(unlockedTier, hardMode, minLimit, Allowance);

		private static int GatingLimit(int unlockedTier, bool hardMode, int minLimit, int allowance)
		{
			ProgressionConfig progression = ProgressionConfig.Instance;
			if (!progression.TierGating) return Tiers.Max;
			int limit = unlockedTier + allowance;
			if (!hardMode && !LegacyGating) limit = Math.Min(limit, PreHardmodeCap);
			return Tiers.Clamp(Math.Max(limit, minLimit));
		}

		// The progression limit for one item: a hard gate (luminite, chlorophyte, mechanical souls...) gets no allowance
		public static int GatingLimitFor(int type, int minLimit) =>
			IsHardGate(type) ? GatingLimit(ProgressionSystem.UnlockedTier, Main.hardMode, minLimit, 0) : GatingLimit(minLimit);

		public static bool IsHardGate(int type) => IsValid(type) && Info[type].HardGate;

		// Tests only (PlanDiff, restored in finally): the planning rules of worlds made before the hardmode wall (allowance
		// 2, no wall in GatingLimit and UnlockTier), to measure how much of a world's plan the wall changes
		public static bool LegacyGating;

		private static int Allowance => LegacyGating ? 2 : ProgressionConfig.Instance.TierAllowance;

		// The highest tier given before hardmode (except jackpots and boss floors)
		public const int PreHardmodeCap = (int)Tier.PostSkeletron;

		// The ceiling of runtime rolls that ignore progression (fishing, Extractinator, herbs, shimmer): the hardmode wall
		public static int HardmodeWall => Main.hardMode ? Tiers.Max : PreHardmodeCap;

		// The inverse of GatingLimit: the unlocked tier from which gated rolls may give an item of this tier (a hardmode item
		// waits for hardmode, whatever the allowance)
		public static int UnlockTier(int tier) => UnlockTier(tier, Allowance);

		private static int UnlockTier(int tier, int allowance)
		{
			ProgressionConfig progression = ProgressionConfig.Instance;
			if (!progression.TierGating) return 0;
			int unlock = Math.Max(0, tier - allowance);
			return tier >= (int)Tier.Hardmode && !LegacyGating ? Math.Max((int)Tier.Hardmode, unlock) : unlock;
		}

		// The unlocked tier from which gated rolls give this item: UnlockTier of its true tier, without the allowance for a hard
		// gate (Chlorophyte Bars wait for the mechanical bosses, Luminite for the Moon Lord)
		public static int UnlockTierOf(int type) => UnlockTier(TrueTierOf(type), IsHardGate(type) ? 0 : Allowance);

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
				// Banned items only (the permutation stays as it is)
				return Checked(Shuffled(request.OriginalItem), rolledTier, Tiers.Max, new[] { RollKey.Shuffle, DetRandom.ItemKey(request.OriginalItem) }, RepickStage.Shuffle);
			}

			Random rng = DetRandom.Get(request.Keys);
			if (mode == RandomizerMode.Chaos)
			{
				ItemCategory anyCategory = AnyCategory(rng);
				int tier = rng.Next(Tiers.Count);
				int picked = Checked(PickFromBucket(anyCategory, tier, rng), tier, Tiers.Max, request.Keys, RepickStage.Chaos);
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
			int limit = request.IgnoreGating ? Tiers.Max
				: request.GatingTier.HasValue ? GatingLimit(request.GatingTier.Value, request.GatingHardMode, request.MinLimit) : GatingLimit(request.MinLimit);
			// Some categories have nothing in the early tiers (dyes, summon weapons...): rather than changing the category,
			// a replacement may go up to the original item's own tier, which this source already gives in vanilla
			int categoryLimit = hasOriginal && category == Info[request.OriginalItem].Category ? Math.Max(limit, Info[request.OriginalItem].Tier) : limit;
			int maxTier = request.MaxTier.HasValue ? Tiers.Clamp(request.MaxTier.Value) : Tiers.Max;
			limit = Math.Min(limit, maxTier);
			categoryLimit = Math.Min(categoryLimit, maxTier);
			// The highest true tier this roll may give (re-picked above it): its limit when gated, else the explicit ceiling only;
			// the same without the allowance for the hard gates
			int ceiling = request.IgnoreGating ? maxTier : categoryLimit;
			int hardCeiling = request.IgnoreGating ? maxTier : Math.Min(maxTier, hasOriginal && category == Info[request.OriginalItem].Category
				? Math.Max(HardLimit(request.MinLimit), Info[request.OriginalItem].Tier) : HardLimit(request.MinLimit));
			// Bags and summons stay at the limit: a wild boss bag would hand out its whole tier (Hallowed Bars before hardmode...)
			if (request.Wild && request.WildRng != null && limit < Tiers.Max && category is not (ItemCategory.Bag or ItemCategory.BossSummon))
			{
				// Only the runtime rng is used here: without a wild roll, the result is the same as without the flag
				float baseChance = WildChanceOverride ?? progression.WildDropChance;
				float wildChance = Math.Min(100f, baseChance * (request.WildChanceMultiplier > 0 ? request.WildChanceMultiplier : 1f));
				int wildCeiling = maxTier;
				int wildTier = WildTier(limit, wildChance, progression.WildTierDecay, request.WildRng, wildCeiling);
				if (wildTier > limit)
				{
					// Only something useful is a jackpot: a wild ore or block is no jackpot, the normal roll stands
					int wild = Checked(PickFromBucket(category, wildTier, request.WildRng, wildTier), wildTier, wildCeiling, request.Keys, RepickStage.Wild);
					if (TrueTierOf(wild) > limit && IsJackpotWorthy(wild))
					{
						LastJackpotGap = TrueTierOf(wild) - Math.Min(GatingLimitFor(wild, request.MinLimit), limit);
						rolledTier = TierOf(wild);
						return wild;
					}
				}
			}
			if (target <= limit)
			{
				int intended = Checked(PickFromBucket(category, target, rng, categoryLimit, window), target, ceiling, request.Keys, RepickStage.Intended, window, hardCeiling);
				// A lucky runtime roll above the limit it ignores is a jackpot when useful; otherwise it is no jackpot, and the
				// item is the one the limit allows
				if (request.IgnoreGating && request.WildRng != null)
				{
					int gatingLimit = Math.Min(GatingLimitFor(intended, request.MinLimit), maxTier);
					if (TrueTierOf(intended) > gatingLimit)
					{
						// The planned item stays; far above the limit it is kept as rarely as the madness tail (runtime rng only),
						// otherwise (or when not useful) rolled within the limit
						int gap = TrueTierOf(intended) - gatingLimit;
						if (IsJackpotWorthy(intended) && (gap <= 1 || request.WildRng.NextDouble() < LuckyKeepChance(gap))) LastJackpotGap = gap;
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
			int fallback = Checked(PickFromBucket(category, limit, DetRandom.Get(fallbackKeys), categoryLimit, window), limit, ceiling, fallbackKeys, RepickStage.Fallback, window, hardCeiling);
			rolledTier = TierOf(fallback);
			return fallback;
		}

		// Tests only (restored in finally): rolls keep what they picked, to check the re-pick changes nothing else
		public static bool RepickDisabled;

		private static class RepickStage
		{
			public const int Intended = 0;
			public const int Fallback = 1;
			public const int Wild = 2;
			public const int Chaos = 3;
			public const int Shuffle = 4;
			public const int Pick = 5;
		}

		// May a roll whose true-tier ceiling is this give the item? hardCeiling: the ceiling of the hard gates (no allowance)
		public static bool Allowed(int type, int ceiling, int hardCeiling = Tiers.Max) =>
			!IsBanned(type) && TrueTierOf(type) <= ceiling && (TrueTierOf(type) <= hardCeiling || !IsHardGate(type));

		// The progression limit of the hard gates: the unlocked tier itself (or the source's floor)
		private static int HardLimit(int minLimit) => GatingLimit(ProgressionSystem.UnlockedTier, Main.hardMode, minLimit, 0);

		public static bool IsBanned(int type) => IsValid(type) && Info[type].Banned;

		private static int Checked(int picked, int tier, int ceiling, int[] keys, int stage, int window = 0, int hardCeiling = Tiers.Max) =>
			RepickDisabled || Allowed(picked, ceiling, hardCeiling) ? picked : Repick(picked, tier, ceiling, keys, stage, window, hardCeiling);

		// Isolated re-pick of a banned item, or of one whose true tier is above the ceiling: the buckets, the keys and the
		// roll's own rng stay as they are, so every other roll gives what it gave before. Same category, from the wanted tier
		// down, with an rng of its own (the roll's keys + RollKey.Repick + stage + attempt); then a scan downward; then any
		// allowed material; Gel last
		public static int Repick(int picked, int tier, int ceiling, int[] keys, int stage, int window = 0, int hardCeiling = Tiers.Max)
		{
			if (buckets == null) return picked;
			ItemCategory category = IsValid(picked) ? Info[picked].Category : ItemCategory.Material;
			int top = Math.Min(Tiers.Clamp(Math.Max(tier, TierOf(picked))), Tiers.Clamp(ceiling));
			int[] repickKeys = new int[keys.Length + 3];
			keys.CopyTo(repickKeys, 0);
			repickKeys[^3] = RollKey.Repick;
			repickKeys[^2] = stage;
			for (int attempt = 0; attempt < 4; attempt++)
			{
				repickKeys[^1] = attempt;
				int candidate = PickFromBucket(category, top, DetRandom.Get(repickKeys), top, window);
				if (Allowed(candidate, ceiling, hardCeiling)) return candidate;
			}
			repickKeys[^1] = 4;
			Random rng = DetRandom.Get(repickKeys);
			int found = ScanAllowed(category, top, ceiling, rng, hardCeiling);
			if (found <= 0 && category != ItemCategory.Material) found = ScanAllowed(ItemCategory.Material, top, ceiling, rng, hardCeiling);
			return found > 0 ? found : ItemID.Gel;
		}

		// The first allowed item from the tier down, each bucket read from a random start
		private static int ScanAllowed(ItemCategory category, int top, int ceiling, Random rng, int hardCeiling)
		{
			for (int t = top; t >= 0; t--)
			{
				int[] bucket = buckets[(int)category][t];
				if (bucket.Length == 0) continue;
				int start = rng.Next(bucket.Length);
				for (int i = 0; i < bucket.Length; i++)
				{
					int item = bucket[(start + i) % bucket.Length];
					if (Allowed(item, ceiling, hardCeiling)) return item;
				}
			}
			return 0;
		}

		// PickFromBucket for features that pick without a roll (starting kit, jackpot chest potions): a banned item, or one
		// above the ceiling, is re-picked in isolation (keys: what the caller would use for a roll)
		public static int PickAllowed(ItemCategory category, int tier, Random rng, int[] keys, int ceiling = Tiers.Max)
		{
			int picked = PickFromBucket(category, tier, rng);
			return RepickDisabled || Allowed(picked, ceiling) ? picked : Repick(picked, tier, ceiling, keys, RepickStage.Pick);
		}

		// The tier the plans work with (rolls aim at it, findability, guaranteed sources, recipe availability)
		public static int TierOf(int type) => IsValid(type) ? Info[type].Tier : 0;

		// The tier from which the item may be given (>= TierOf, except the materials of MaterialTiers: exactly the table's, and
		// what world-generated chests hold: their chest's tier, lower only):
		// progression checks, jackpot gaps, what the codex shows
		public static int TrueTierOf(int type) => IsValid(type) ? Info[type].TrueTier : 0;

		// How often a material comes back as a random recipe ingredient (MaterialTiers; unlisted items: uncommon)
		public static Commonness CommonnessOf(int type) => MaterialTiers.Table.TryGetValue(type, out var entry) ? entry.Commonness : Commonness.Uncommon;

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
			// TODO(shops): Collect() counts every vanilla shop item as obtainable, but randomized shops (ShopFeature.PlanShop) may
			// no longer sell it; the permutation only stays complete if shops use Shuffled too (Roll does in Shuffle mode)
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

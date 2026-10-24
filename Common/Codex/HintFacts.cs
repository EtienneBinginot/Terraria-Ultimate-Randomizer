using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.Map;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Codex.Interface;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;
using DropRuleConditions = Terraria.GameContent.ItemDropRules.Conditions;

namespace UltimateRandomizer.Common.Codex
{
	// Where a hint says the item comes from. The order is the preference between sources of the same tier.
	public enum HintKind : byte { Creature, Recipe, Boss, Bag, Chest, Pot, Shop, World }

	// House: what a house needs besides its table and chair (door, platform, torch); Upgrade: life and mana upgrades;
	// Storage: Magic Storage's network pieces and storage unit upgrades (when that mod is loaded). Never saved nor synced,
	// new kinds still go at the end
	public enum HintTargetKind : byte { Station, Summon, Pickaxe, House, Upgrade, Storage }

	// How a part of a recipe hint's tree is obtained: crafted (its parts follow), from the source the Guide would name for
	// it, gathered or mined in the world, already explained above, or nothing known
	// Findable: found in the world in a way hints don't name (fishing, chest loot, plants, Extractinator...);
	// WorldStation: a station that has no item and stands in the world (altars)
	// Caught: a critter caught with a bug net; Filled: a bucket filled in a pool of its liquid; Harvested: given by a kind of
	// plant (plants are randomized)
	public enum HintTreeLeaf : byte { Recipe, Fact, Gathered, Mined, Repeat, Unknown, Findable, WorldStation, Caught, Filled, Harvested }

	// One ingredient or station of a recipe hint's tree, with its depth (1: a part of the hinted recipe)
	public sealed class HintTreeNode
	{
		// 0 for a station without an item (Tile)
		public int Item;
		public int Stack;
		public int Depth;
		public bool Station;
		public int Tile = -1;
		public HintTreeLeaf Leaf;
		// Fact and Recipe leaves: the source of the item
		public HintFact Fact;
		// Caught: where the critter lives (biome names of BiomeSlots); Harvested: the plant
		public List<string> Biomes = new();
		// Caught: when its critter slots are open (fireflies at night...)
		public SlotTime Time;
		public int Herb;
		// Gathered, mined or caught: another way to get it (an enemy, a boss, a bag...), when there is one
		public HintFact Also;
		// Mined: the ore to mine (another one when mined ores are randomized). Read when shown: an altar deciding a hardmode
		// ore pair changes it without resetting the hint caches
		public int Ore => Leaf == HintTreeLeaf.Mined ? HintFacts.MinedFrom(Item) : 0;
	}

	// An item the Guide can give hints about, and the tier its price follows
	public sealed class HintTarget
	{
		public int Item;
		public int Tier;
		public HintTargetKind Kind;
	}

	// The facts behind the hints of one item, all read from the planners of this world (the same on every machine)
	public sealed class HintFact
	{
		public int Item;
		public HintKind Kind;
		// Tier from which this source gives the item
		public int Tier;
		// Creature, boss or shop keeper (NPC type); critter of a world source (0: none)
		public int Npc;
		// Recipe index (Main.recipe), its planned ingredients and stations
		public int Recipe = -1;
		public (int type, int stack)[] Ingredients = Array.Empty<(int, int)>();
		public int[] Tiles = Array.Empty<int>();
		public int Bag;
		// World source (WorldSources key)
		public string World;
		// Tier band of the "where" sentence (ingredients, boss, bag, pot...): 0 early .. 3 late
		public int Band;
		// Where the host spawns (biome names of BiomeSlots), when known
		public List<string> Biomes = new();
		public SlotTime Time;
		// 0 walks, 1 flies, 2 swims, 3 critter; -1 unknown
		public int Movement = -1;
		// Planned chance that the creature, boss or bag gives the item (1 for recipes and shops)
		public float Chance = 1f;
		// Creature: steady share of the spawns of its best biome that are this enemy (BiomeSlots.SteadyShare); 0: unknown.
		// A guaranteed item's enemies: the share they make together in its biome
		public double Share;
		// A guaranteed item: every enemy carrying it in its biome, the most met first (Npc is the first), each with Chance;
		// empty for any other source
		public List<int> Hosts = new();
		// When each of Hosts is met in its biome (empty: unknown, all at any hour)
		public List<SlotTime> HostTimes = new();
		// What the creature's, boss's or bag's drop needs beyond the kill or the opening: the descriptions of its drop
		// conditions (a windy day, an event...), those this world always meets left out
		public List<string> Conditions = new();
		// A creature met in its best biome only through slots open with a weather, a season or an event
		public bool Conditional;
		// A creature that fails the frequency rules, kept because nothing better than a chest or a pot gives the item
		public bool Fallback;
		// A crafting station: its other way, told at the last level (StationWays). Guardian: the enemy (or boss) carrying it
		// when the hint names its recipe; StationRecipe: its recipe when the hint names another source
		public HintFact Guardian;
		public HintFact StationRecipe;
	}

	// Hint targets, facts, prices and sentences. Pure data from the planners (RecipePlanner, GuaranteedSources,
	// RandomNpcDropRule.Plan, BagPlan, BiomeSlots), the seed and synced settings, so every machine gives the same hints.
	public static class HintFacts
	{
		public const int MaxLevel = 4;

		// Text families ("Codex.Hint." + family + "." + variant) and their variant counts; tools/py/check_loc.py reads this list
		public static readonly string[] HintTexts =
		{
			"Kind.Creature/3", "Kind.Roaming/3", "Kind.Recipe/3", "Kind.Boss/3", "Kind.Bag/3", "Kind.Chest/3", "Kind.Pot/3", "Kind.Shop/3",
			"Where.Creature/3", "Where.Roaming/3", "Where.Recipe/3", "Where.Boss/3", "Where.Bag/3", "Where.Chest/3", "Where.Pot/3", "Where.Shop/3",
			"Shape.Creature/2", "Shape.Roaming/2", "Shape.Recipe/2", "Shape.Boss/2", "Shape.Bag/2", "Shape.Chest/2", "Shape.Pot/2", "Shape.Shop/2",
			"Name.Creature/2", "Name.Roaming/2", "Name.Recipe/2", "Name.Boss/2", "Name.Bag/2", "Name.Chest/2", "Name.Pot/2", "Name.Shop/2",
			"Name.Hosts/2", "Kind.Hosts.Roaming/2", "Where.Hosts.Roaming/2", "Shape.Hosts.Roaming/2", "Name.Hosts.Roaming/2", "Name.HostsTimed/2"
		};

		// Vanilla summons found in the world rather than dropped or crafted (Protection.WorldSummons): (item, text key, critter to
		// show). The critters are caught and the crystal broken as in vanilla, whatever the settings; the Power Cells of the
		// Temple's chests only while they are kept (WorldSourceKept)
		private static readonly (int item, string key, int npc)[] WorldSources =
		{
			(ItemID.TruffleWorm, "TruffleWorm", NPCID.TruffleWorm),
			(ItemID.EmpressButterfly, "EmpressButterfly", NPCID.EmpressButterfly),
			(ItemID.LihzahrdPowerCell, "PowerCell", 0),
			(ItemID.QueenSlimeCrystal, "GelatinCrystal", 0)
		};

		// The Lihzahrd Power Cells of the Temple's chests stay there when protected or when chests are not randomized
		private static bool WorldSourceKept(int item) => item != ItemID.LihzahrdPowerCell || !LootConfig.Instance.RandomizeChests || DropHelper.KeepOriginal(item);

		// Texts of the world sources: "Codex.Hint.World." + key + "." + one of these
		public static readonly string[] WorldTextParts = { "Kind", "Where", "Shape", "Name" };

		public static IEnumerable<string> WorldKeys => WorldSources.Select(w => w.key);

		private static Dictionary<int, HintTarget> rawTargets;
		private static List<HintTarget> candidates;
		private static readonly Dictionary<int, HintFact> facts = new();
		private static readonly Dictionary<int, List<HintTreeNode>> trees = new();
		private static readonly HashSet<int> truncatedTrees = new();
		private static Dictionary<int, List<(int npc, int tier, float chance)>> planDrops;
		private static Dictionary<int, List<(int bag, int tier, float chance)>> bagContents;
		// The first tier at which each natural enemy spawns (EnemySwapPool.NaturalEnemies), bosses at their own
		private static Dictionary<int, int> naturalTiers;
		private static Dictionary<int, int> shopKeepers;
		// The indexes above, built a few enemies at a time (Warm) or all at once when needed first
		private static IEnumerator<bool> indexer;

		// Moves every time the facts are forgotten (the panel rebuilds then)
		public static int CacheVersion { get; private set; }

		static HintFacts()
		{
			WorldCaches.Reset += Reset;
			// Plans read the effective tiers of enemies, which may follow progression
			ProgressionSystem.OnTierChanged += Reset;
			ChestSystem.OnStationsChanged += Reset;
		}

		public static void Reset()
		{
			rawTargets = null;
			candidates = null;
			facts.Clear();
			ForgetTrees();
			planDrops = null;
			bagContents = null;
			naturalTiers = null;
			shopKeepers = null;
			indexer = null;
			CacheVersion++;
		}

		public static bool Ready => SeedSystem.WorldReady && ItemPool.Ready && RecipeSystem.RecipeCount > 0;

		// ---- Targets ----

		// Every item hints may be about, with its tier, before looking for its sources (cheap: the server checks
		// purchases with it)
		private static Dictionary<int, HintTarget> RawTargets()
		{
			if (rawTargets != null) return rawTargets;
			Dictionary<int, HintTarget> byItem = new();
			void Add(int item, int tier, HintTargetKind kind)
			{
				if (!ItemPool.IsValid(item) || ItemPool.Info[item].Excluded) return;
				tier = Tiers.Clamp(tier);
				if (byItem.TryGetValue(item, out HintTarget old) && old.Tier <= tier) return;
				byItem[item] = new HintTarget { Item = item, Tier = tier, Kind = kind };
			}
			foreach ((_, Stations.StationInfo info) in Stations.Basic()) Add(info.Item, info.Tier, HintTargetKind.Station);
			RecipePlan plan = RecipePlanner.Get();
			foreach (int tile in Stations.RequiredTiles())
			{
				Stations.StationInfo info = Stations.Info(tile);
				if (info.Kind != StationKind.Progression || info.World) continue;
				// The item of the station that is obtained first (Mythril or Orichalcum Anvil...), vanilla first
				int best = Stations.Placers(tile).OrderBy(p => p < ItemID.Count ? 0 : 1).ThenBy(p => Availability(plan, p)).ThenBy(p => p).FirstOrDefault();
				if (best > 0) Add(best, info.Tier, HintTargetKind.Station);
			}
			// The themed stations planned with a recipe and a guardian (GuaranteedSources.PlacerRequirements), at their tier
			foreach (GuaranteedSources.Requirement r in GuaranteedSources.PlacerRequirements())
			{
				if (Stations.PlacedTile(r.Item, out int placed) && Stations.Info(placed).Kind == StationKind.Thematic) Add(r.Item, r.Tier, HintTargetKind.Station);
			}
			// Boss and event summons (not the life crystals and mirrors sorted with them), needed before their boss
			for (int type = 1; type < ItemPool.Info.Length; type++)
			{
				if (Protection.IsBossSummon(type)) Add(type, Protection.SummonTier(type), HintTargetKind.Summon);
			}
			// The pickaxes placed on enemies for a mining level: cheap ones only
			foreach (GuaranteedSources.Requirement r in GuaranteedSources.Requirements.Where(r => r.Tool)) Add(r.Item, r.Tier, HintTargetKind.Pickaxe);
			foreach (int item in RecipeSystem.HouseItems) Add(item, 0, HintTargetKind.House);
			// Life and mana upgrades, at the tier their recipe waits for
			foreach (int item in Protection.Upgrades) Add(item, Protection.UpgradeTier(item), HintTargetKind.Upgrade);
			// Magic Storage's pieces, at their tier like any item
			foreach (int item in StorageItems()) Add(item, ItemPool.TierOf(item), HintTargetKind.Storage);
			rawTargets = byItem;
			return byItem;
		}

		public const string MagicStorage = "MagicStorage";

		// Magic Storage's items worth a hint, when that mod is loaded: one item per tile of its storage network (its tiles that
		// are not crafting stations: heart, units, components, connectors, accesses; the item named like the tile, else the
		// lowest style: the plain Storage Unit, not the upgraded ones) and its storage unit upgrades ("Upgrade..." items)
		public static List<int> StorageItems()
		{
			List<int> items = new();
			if (!ModLoader.TryGetMod(MagicStorage, out Mod mod)) return items;
			Dictionary<int, ModItem> byTile = new();
			foreach (ModItem modItem in mod.GetContent<ModItem>())
			{
				if (modItem.Name.StartsWith("Upgrade", StringComparison.Ordinal))
				{
					items.Add(modItem.Type);
					continue;
				}
				if (!Stations.PlacedTile(modItem.Type, out int tile) || TileLoader.GetTile(tile) is not ModTile modTile || modTile.Mod != mod) continue;
				// The combined crafting stations and altars are stations, not storage
				if (modTile.AdjTiles is { Length: > 0 }) continue;
				if (byTile.TryGetValue(tile, out ModItem old) && !BetterStoragePlacer(modItem, old, modTile)) continue;
				byTile[tile] = modItem;
			}
			items.AddRange(byTile.Values.Select(m => m.Type));
			items.Sort();
			return items;
		}

		private static bool BetterStoragePlacer(ModItem item, ModItem old, ModTile tile)
		{
			if ((item.Name == tile.Name) != (old.Name == tile.Name)) return item.Name == tile.Name;
			int style = ContentSamples.ItemsByType[item.Type].placeStyle, oldStyle = ContentSamples.ItemsByType[old.Type].placeStyle;
			return style != oldStyle ? style < oldStyle : item.Type < old.Type;
		}

		// Every target with a source, by tier, whatever the progression
		public static List<HintTarget> AllTargets()
		{
			if (candidates != null) return candidates;
			candidates = RawTargets().Values.Where(t => For(t.Item) != null)
				.OrderBy(t => t.Tier).ThenBy(t => t.Kind).ThenBy(t => DetRandom.ItemName(t.Item), StringComparer.Ordinal).ToList();
			return candidates;
		}

		// Targets in the order progression reaches them: their tier, then the tier of the source the hint names, then when
		// the recipe plan makes them available, then the order of AllTargets (kind, name)
		public static List<HintTarget> ByProgress(IEnumerable<HintTarget> targets)
		{
			RecipePlan plan = RecipePlanner.Get();
			Dictionary<HintTarget, int> order = new();
			List<HintTarget> all = AllTargets();
			for (int i = 0; i < all.Count; i++) order[all[i]] = i;
			return targets.OrderBy(t => t.Tier).ThenBy(t => For(t.Item)?.Tier ?? Findability.Never).ThenBy(t => Availability(plan, t.Item))
				.ThenBy(t => order.TryGetValue(t, out int i) ? i : int.MaxValue).ToList();
		}

		// Targets offered now: up to one tier above the unlocked one
		public static List<HintTarget> Targets(int unlockedTier) => AllTargets().Where(t => t.Tier <= unlockedTier + 1).ToList();

		public static List<HintTarget> Targets() => Targets(ProgressionSystem.UnlockedTier);

		// A target beyond what is offered now (more than one tier above the unlocked one): shown, never sold
		public static bool IsLocked(HintTarget target, int unlockedTier) => target.Tier > unlockedTier + 1;

		public static bool IsLocked(HintTarget target) => IsLocked(target, ProgressionSystem.UnlockedTier);

		// Every target with a source that is not offered yet
		public static List<HintTarget> LockedTargets() => AllTargets().Where(t => IsLocked(t)).ToList();

		// "after Plantera": when a locked target will be offered (the boss of the tier before its own)
		// When the Guide starts to talk about a target: one tier before the target itself
		public static string UnlockText(HintTarget target) => CodexText.Of("Hint.Unlock", (Tier)Tiers.Clamp(target.Tier - 1));

		// When the target itself comes in the game (an Orichalcum Anvil: after the Wall of Flesh)
		public static string ComesText(HintTarget target) => CodexText.Of("Hint.Unlock", (Tier)Tiers.Clamp(target.Tier));

		public static HintTarget Target(int item) => AllTargets().FirstOrDefault(t => t.Item == item);

		// The target and tier of an item without building its facts (server checks)
		public static HintTarget RawTarget(int item) => RawTargets().TryGetValue(item, out HintTarget target) ? target : null;

		public static bool Warmed => candidates != null;

		// A slice of the work behind the hints (enemy plans, bag plans, then the facts of each target), for about
		// budgetMs: run on the client over a few frames after the world loads or the tier changes, so that opening the
		// Guide's panel or the codex never freezes the game
		public static void Warm(double budgetMs)
		{
			if (!Ready || Warmed) return;
			System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
			indexer ??= BuildIndexes();
			while (planDrops == null && watch.Elapsed.TotalMilliseconds < budgetMs)
			{
				if (!indexer.MoveNext()) break;
			}
			if (planDrops == null) return;
			foreach (HintTarget target in RawTargets().Values)
			{
				if (watch.Elapsed.TotalMilliseconds >= budgetMs) return;
				For(target.Item);
			}
			AllTargets();
		}

		private static void EnsureIndexes()
		{
			if (planDrops != null) return;
			indexer ??= BuildIndexes();
			while (planDrops == null && indexer.MoveNext())
			{
			}
		}

		// ---- Prices ----

		public const long CopperPerGold = 10000;

		// base x (1 + 1.5 x tier) x 3^(level - 1), in copper
		public static long Price(int tier, int level, int baseGold)
		{
			if (level < 1 || level > MaxLevel || baseGold <= 0) return 0;
			double price = baseGold * (double)CopperPerGold * (1 + 1.5 * Math.Max(0, tier)) * Math.Pow(3, level - 1);
			return (long)Math.Round(price);
		}

		public static long Price(HintTarget target, int level) => Price(target.Tier, level, ExtrasConfig.Instance.HintBasePrice);

		// ---- Facts ----

		public static HintFact For(int item)
		{
			if (facts.TryGetValue(item, out HintFact fact)) return fact;
			fact = Build(item);
			facts[item] = fact;
			return fact;
		}

		private static int Availability(RecipePlan plan, int item) => item > 0 && item < plan.Availability.Length ? plan.Availability[item] : Findability.Never;

		public static int Band(int tier) => tier <= 0 ? 0 : tier <= 3 ? 1 : tier <= 6 ? 2 : 3;

		private static int Gated(int item) => ItemPool.UnlockTier(ItemPool.TierOf(item));

		// Recipes making the item once unlocked (RecipePlan.Results), with their planned ingredients and stations
		public static IEnumerable<int> RecipesMaking(int item)
		{
			RecipePlan plan = RecipePlanner.Get();
			int count = Math.Min(Math.Min(RecipeSystem.RecipeCount, Recipe.numRecipes), plan.Results.Length);
			for (int i = 0; i < count; i++)
			{
				if (!Main.recipe[i].Disabled && plan.Results[i].type == item) yield return i;
			}
		}

		public static (int type, int stack)[] PlannedIngredients(int recipe) => RecipePlanner.Get().Ingredients[recipe] ?? RecipeSystem.OriginalIngredients(recipe);

		public static int[] PlannedTiles(int recipe) => RecipePlanner.Get().Tiles[recipe] ?? RecipeSystem.OriginalTiles(recipe);

		// Enemies and bosses whose planned drops include each target (lucky: what they give once unlocked), then the
		// bags holding each target: every plan is read once, one enemy or bag per step
		private static IEnumerator<bool> BuildIndexes()
		{
			Dictionary<int, List<(int, int, float)>> drops = new();
			Dictionary<int, List<(int, int, float)>> bags = new();
			float minChance = ProgressionConfig.Instance.MinFindableChance / 100f;
			void Add(Dictionary<int, List<(int, int, float)>> table, int item, int from, int tier, float chance)
			{
				if (!table.TryGetValue(item, out List<(int, int, float)> list)) table[item] = list = new List<(int, int, float)>();
				list.Add((from, tier, chance));
			}
			List<(int npc, int tier)> enemies = new();
			HashSet<int> done = new();
			for (int t = 0; t < Tiers.Count; t++)
			{
				foreach (int npc in EnemySwapPool.NaturalEnemies(t).Where(n => !GuaranteedSources.IsEventEnemy(n)))
				{
					if (done.Add(npc)) enemies.Add((npc, t));
				}
			}
			// Not the evil boss this world lacks (as Findability)
			foreach (int boss in NpcTiers.BossTypes.Where(b => !BossPermutation.AbsentEvil(b, BiomeSlots.CurrentEvil)))
			{
				if (done.Add(boss)) enemies.Add((boss, NpcTiers.BaseTier(boss)));
			}
			foreach ((int npc, int tier) in enemies)
			{
				foreach (PlannedDrop drop in RandomNpcDropRule.Plan(npc, Main.expertMode, lucky: true, guaranteed: false, planning: false))
				{
					if (drop.Chance >= minChance && Findability.WorldAllows(drop.Conditions)) Add(drops, drop.Item, npc, Math.Max(tier, Gated(drop.Item)), drop.Chance);
				}
				yield return true;
			}
			foreach (int bag in LootSnapshot.Items.Keys.Where(b => !Findability.SeasonalBags.Contains(b)).OrderBy(b => b))
			{
				int bagTier = Findability.BagContentTier(bag);
				if (bagTier == Findability.Never) continue;
				HashSet<int> inside = new();
				foreach (PlannedDrop drop in BagPlan.Plan(bag, lucky: true))
				{
					if (drop.Chance >= minChance && Findability.WorldAllows(drop.Conditions) && inside.Add(drop.Item)) Add(bags, drop.Item, bag, Math.Max(bagTier, Gated(drop.Item)), drop.Chance);
				}
				yield return true;
			}
			bagContents = bags;
			naturalTiers = enemies.ToDictionary(e => e.npc, e => e.tier);
			planDrops = drops;
		}

		// Town NPCs selling an item in their original shop (protected items keep their shop when shops are randomized; a shop-only
		// item is sold by the merchant ShopPermutation chose)
		private static int ShopKeeper(int item)
		{
			if (shopKeepers == null)
			{
				shopKeepers = new Dictionary<int, int>();
				foreach (AbstractNPCShop shop in NPCShopDatabase.AllShops.OrderBy(s => s.NpcType))
				{
					if (shop is not NPCShop npcShop) continue;
					foreach (NPCShop.Entry entry in npcShop.Entries)
					{
						if (entry?.Item != null && !entry.Item.IsAir && entry.Item.shopSpecialCurrency == -1) shopKeepers.TryAdd(entry.Item.type, shop.NpcType);
					}
				}
			}
			if (EconomyConfig.Instance.RandomizeShops && ItemPool.IsRandomizable(item)) return ShopPermutation.Seller(item, out int seller, out _) ? seller : 0;
			return shopKeepers.TryGetValue(item, out int npc) ? npc : 0;
		}

		// A creature fact must lead to an enemy met often enough, with a decent chance: never a rare slot, never 0.01 %
		public const double MinHostShare = GuaranteedSources.MinHostShare;
		public const float MinCreatureChance = 0.01f;
		// A boss source must give the item at least once in four fights (a guaranteed creature source is easier otherwise)
		public const float MinBossChance = 0.25f;

		// The preference between sources of the same tier: recipes and creatures, then bosses, bags, shops (and world
		// sources), then chests and pots as a last resort
		public static int KindClass(HintKind kind) => kind switch
		{
			HintKind.Recipe or HintKind.Creature => 0,
			HintKind.Boss => 1,
			HintKind.Bag => 2,
			HintKind.Shop or HintKind.World => 3,
			_ => 4
		};

		// How easily the source gives the item: chance x share of its best biome for creatures, the chance for bosses and
		// bags, certain for recipes, shops and the world
		public static double Ease(HintFact fact) => fact.Kind switch
		{
			HintKind.Creature => fact.Chance * (fact.Share > 0 ? fact.Share : MinHostShare),
			HintKind.Boss or HintKind.Bag => fact.Chance,
			HintKind.Chest or HintKind.Pot => 0,
			_ => 1
		};

		// A fixed key between equal sources (never the NPC id)
		private static string StableKey(HintFact fact) => fact.Kind switch
		{
			HintKind.Creature or HintKind.Boss or HintKind.Shop => CodexNames.Npc(fact.Npc),
			HintKind.Recipe => "r" + RecipeSystem.RecipeKey(fact.Recipe),
			HintKind.Bag => CodexNames.Item(fact.Bag),
			_ => fact.World ?? ""
		};

		// A creature source the hint may lead to, judged at a tier: no rare enemy, a planned chance of 1 % or more, and at
		// least GuaranteedSources.MinShare of the steady spawns (slots neither rare nor conditional) of its best biome. Enemies without
		// any slot (modded), and every enemy when spawns are swapped another way, are judged by their rarity only.
		// A source failing these is returned as a fallback (null: no chance at all)
		private static HintFact CreatureFact(int item, int npc, int tier, int judgeTier, float chance)
		{
			if (chance <= 0) return null;
			int type = BiomeSlots.TypeOf(npc);
			bool ok = chance >= MinCreatureChance - 1e-6f && !(ContentSamples.NpcsByNetId.TryGetValue(npc, out NPC sample) && sample.rarity > 0);
			double share = 0;
			if (BiomeSlots.SpawnsPlanned && BiomeSlots.HasAnySlot(type))
			{
				(int biome, _, _) = BiomeSlots.BestBiome(type, judgeTier);
				share = biome >= 0 ? BiomeSlots.SteadyShare(biome, type, judgeTier) : 0;
				ok &= biome >= 0 && share >= GuaranteedSources.MinShare(biome, judgeTier);
			}
			return new HintFact { Item = item, Kind = HintKind.Creature, Npc = npc, Tier = tier, Band = Band(tier), Chance = chance, Share = share, Fallback = !ok };
		}

		// A guaranteed item: one fact naming every enemy of its biome that carries it (the most met first), the share they
		// make together, the chance on each. Judged like a single creature, on that share in that biome
		private static HintFact GuaranteedFact(int item, List<int> hosts, int requirementTier)
		{
			// Met from that tier on (a Demon Eye in the starting Forest carries tier 0 items)
			int tier = hosts.Min(h => Math.Min(NaturalTier(h), GuaranteedSources.HostTier(h)));
			int npc = hosts[0];
			HintFact fact = CreatureFact(item, npc, tier, Math.Max(tier, requirementTier), CodexStats.LuckyChance(npc, item));
			if (fact == null) return null;
			fact.Hosts = hosts;
			int biome = GuaranteedSources.BiomeOf(item);
			if (biome >= 0 && BiomeSlots.SpawnsPlanned)
			{
				fact.Share = GuaranteedSources.BiomeShare(item);
				bool rare = hosts.Any(h => ContentSamples.NpcsByNetId.TryGetValue(h, out NPC sample) && sample.rarity > 0);
				fact.Fallback = rare || fact.Chance < MinCreatureChance - 1e-6f || fact.Share < GuaranteedSources.MinShare(biome, Math.Max(0, GuaranteedSources.PlacedTier(item)));
			}
			return fact;
		}

		// The tier from which an enemy spawns naturally (its own tier if it never does)
		private static int NaturalTier(int npc) => naturalTiers.TryGetValue(npc, out int tier) ? tier : NpcTiers.BaseTier(npc);

		private static HintFact Build(int item)
		{
			if (!Ready || !ItemPool.IsValid(item)) return null;
			RecipePlan plan = RecipePlanner.Get();
			EnsureIndexes();
			List<HintFact> options = new();
			List<HintFact> rejected = new();
			// Bosses that seldom give it: a fight for little, only better than a chest or a pot
			List<HintFact> weak = new();
			void AddCreature(HintFact fact)
			{
				if (fact != null) (fact.Fallback ? rejected : options).Add(fact);
			}

			// Guaranteed drop of a common enemy (stations, summons, pickaxes): from the tier it spawns at, judged like the
			// guarantees were (at the requirement's tier at least)
			GuaranteedSources.Requirement requirement = GuaranteedSources.Requirements.FirstOrDefault(r => r.Item == item);
			bool required = requirement.Item == item;
			List<int> hosts = GuaranteedSources.HostsOf(item).ToList();
			// A crafting station: its guardian and its recipe are both told (StationWays)
			HintFact guardian = null;
			List<HintFact> recipes = new();
			if (hosts.Count > 0 && GuaranteedSources.TryPlacement(item, out GuaranteedSources.Placement placement) && placement.Boss)
			{
				// Carried by the progression bosses of its tier (no natural enemy there): a boss hint for each
				foreach (int boss in hosts)
				{
					int bossTier = Math.Max(NpcTiers.BaseTier(boss), required ? requirement.Tier : 0);
					HintFact bossFact = new() { Item = item, Kind = HintKind.Boss, Npc = boss, Tier = bossTier, Band = Band(bossTier), Chance = CodexStats.LuckyChance(boss, item) };
					guardian ??= bossFact;
					options.Add(bossFact);
				}
			}
			else if (hosts.Count > 0)
			{
				guardian = GuaranteedFact(item, hosts, required ? requirement.Tier : 0);
				AddCreature(guardian);
			}
			foreach (int recipe in RecipesMaking(item))
			{
				(int type, int stack)[] ingredients = PlannedIngredients(recipe);
				if (ingredients.Any(x => x.type == item)) continue;
				int[] tiles = PlannedTiles(recipe);
				int ingredientTier = ingredients.Select(x => Availability(plan, x.type)).DefaultIfEmpty(0).Max();
				int tier = Math.Max(plan.ResultTiers[recipe], Math.Max(ingredientTier, tiles.Where(t => t >= 0).Select(t => RecipePlanner.StationTier(t, plan.Availability)).DefaultIfEmpty(0).Max()));
				if (tier >= Findability.Never) continue;
				HintFact recipeFact = new() { Item = item, Kind = HintKind.Recipe, Recipe = recipe, Ingredients = ingredients, Tiles = tiles.Where(t => t >= 0).ToArray(), Tier = tier, Band = Band(ingredientTier) };
				recipes.Add(recipeFact);
				options.Add(recipeFact);
			}
			if (planDrops.TryGetValue(item, out List<(int npc, int tier, float chance)> droppers))
			{
				foreach ((int npc, int tier, _) in droppers.DistinctBy(d => d.npc))
				{
					// The chance the codex shows too: every planned entry of the item together
					float chance = CodexStats.LuckyChance(npc, item);
					if (NpcTiers.IsBoss(BossParts.Owner(npc)))
					{
						(chance >= MinBossChance - 1e-6f ? options : weak).Add(new HintFact { Item = item, Kind = HintKind.Boss, Npc = npc, Tier = tier, Band = Band(tier), Chance = chance });
					}
					else AddCreature(CreatureFact(item, npc, tier, tier, chance));
				}
			}
			if (bagContents.TryGetValue(item, out List<(int bag, int tier, float chance)> holders))
			{
				// Like a boss: a bag that seldom gives it (one in eight) is only better than a chest or a pot, never ahead of a
				// guaranteed enemy of a later tier
				foreach ((int bag, int tier, float chance) in holders)
				{
					(chance >= MinBossChance - 1e-6f ? options : weak).Add(new HintFact { Item = item, Kind = HintKind.Bag, Bag = bag, Tier = tier, Band = Band(tier), Chance = chance });
				}
			}
			// Only the stations really hidden in chests of this world (saved, and sent with the world data)
			if (ChestSystem.StationInChests(item))
			{
				options.Add(new HintFact { Item = item, Kind = HintKind.Chest, Tier = 0 });
			}
			if (required && ProgressionConfig.Instance.PotGuaranteeChance > 0 && GuaranteedSources.PotItems(Tiers.Max).Contains(item))
			{
				options.Add(new HintFact { Item = item, Kind = HintKind.Pot, Tier = requirement.Tier, Band = Band(requirement.Tier) });
			}
			int keeper = ShopKeeper(item);
			if (keeper > 0)
			{
				// A shop-only item: from its place's tier (arrival and conditions). Else when the keeper moves in (town NPC arrival
				// order of this world), else the item's own tier
				int arrival = TownNpcOrderSystem.ArrivalTier(keeper);
				int tier = ShopPermutation.Seller(item, out _, out int placeTier) ? Math.Max(placeTier, Gated(item))
					: arrival >= 0 ? Math.Max(arrival, Gated(item)) : ItemPool.TierOf(item);
				options.Add(new HintFact { Item = item, Kind = HintKind.Shop, Npc = keeper, Tier = tier, Band = Band(tier) });
			}
			foreach ((int worldItem, string key, int npc) in WorldSources)
			{
				if (worldItem == item && WorldSourceKept(item)) options.Add(new HintFact { Item = item, Kind = HintKind.World, World = key, Npc = npc, Tier = ItemPool.TierOf(item) });
			}
			// The source reached first; between equal tiers, the preferred kind (chests and pots only when nothing else gives
			// it at that tier), then the easiest, then a fixed key
			HintFact best = Best(options);
			// Nothing but a chest or a pot: the best creature that failed the frequency rules (or a boss that seldom gives
			// it) says more
			if ((best == null || best.Kind is HintKind.Chest or HintKind.Pot) && rejected.Count + weak.Count > 0) best = Best(rejected.Concat(weak).ToList());
			if (best == null) return null;
			if (best.Kind is HintKind.Creature or HintKind.Boss or HintKind.Shop) FillPlaces(best);
			best.Conditions = DropConditions(best);
			if (RecipePlanner.StationPlacer(item, out _))
			{
				// The recipe is the main way, the guardian the backup: the hint tells the one it does not name
				if (guardian != null && !ReferenceEquals(guardian, best) && GuaranteedSources.GuardianOf(item) > 0)
				{
					FillPlaces(guardian);
					best.Guardian = guardian;
				}
				HintFact recipe = Best(recipes);
				if (recipe != null && !ReferenceEquals(recipe, best)) best.StationRecipe = recipe;
			}
			return best;
		}

		// The conditions the source's planned drop of the item needs (none when one planned entry of it needs nothing this
		// world doesn't always meet): the entry with the best chance, as the codex counts them all together
		public static List<string> DropConditions(HintFact fact)
		{
			List<PlannedDrop> plan = fact.Kind switch
			{
				// A guaranteed item's enemies carry it without a condition
				HintKind.Creature when fact.Hosts.Count == 0 => RandomNpcDropRule.Plan(fact.Npc, Main.expertMode, lucky: true, planning: false),
				HintKind.Boss => RandomNpcDropRule.Plan(fact.Npc, Main.expertMode, lucky: true, planning: false),
				HintKind.Bag => BagPlan.Plan(fact.Bag, lucky: true),
				_ => null
			};
			return PlanConditions(plan, fact.Item);
		}

		// The conditions an item's entries of a drop plan need (none when one of them needs nothing this world doesn't always
		// meet): the entry with the best chance. Only the entries that count as a source (MinFindableChance, as the indexes):
		// a 0.01 % entry without a condition must not hide the condition of the real one
		public static List<string> PlanConditions(List<PlannedDrop> plan, int item)
		{
			float minChance = ProgressionConfig.Instance.MinFindableChance / 100f;
			List<PlannedDrop> entries = plan?.Where(d => d.Item == item && d.Chance >= minChance && Findability.WorldAllows(d.Conditions)).ToList();
			if (entries == null || entries.Count == 0 || entries.Any(d => ConditionTexts(d.Conditions).Count == 0)) return new List<string>();
			return ConditionTexts(entries.OrderByDescending(d => d.Chance).First().Conditions);
		}

		// The descriptions of drop conditions (IItemDropRuleCondition.GetConditionDescription: "Drops on Windy Days"...),
		// without those that have none (statue checks, hardmode gates the tier already tells) and those fixed by the world
		// that this world meets (an expert drop in an expert world)
		public static List<string> ConditionTexts(List<IItemDropRuleCondition> conditions)
		{
			List<string> texts = new();
			if (conditions == null) return texts;
			foreach (IItemDropRuleCondition condition in conditions)
			{
				if (condition == null || WorldFixed(condition) && Findability.WorldVerdict(condition) == true) continue;
				string text;
				try
				{
					text = condition.GetConditionDescription();
				}
				catch (Exception)
				{
					// A modded condition reading game state that isn't there
					text = null;
				}
				if (!string.IsNullOrWhiteSpace(text) && !texts.Contains(text.Trim())) texts.Add(text.Trim());
			}
			return texts;
		}

		// Conditions Findability.WorldAllows decides from the world alone (difficulty, world evil, special seeds; the remix
		// seed's easy and hard mode ones keep their text: they also depend on hardmode)
		// The remix easy/hard mode conditions keep their text: the world doesn't fix the mode
		private static bool WorldFixed(IItemDropRuleCondition condition) =>
			condition is not (DropRuleConditions.RemixSeedEasymode or DropRuleConditions.RemixSeedHardmode) && Findability.WorldVerdict(condition) != null;

		private static HintFact Best(List<HintFact> options) => options.OrderBy(o => o.Tier).ThenBy(o => KindClass(o.Kind)).ThenByDescending(Ease)
			.ThenBy(StableKey, StringComparer.Ordinal).ThenBy(o => (int)o.Kind).FirstOrDefault();

		// Where the host spawns, from the planned shares at the fact's tier (the redistributed slots, else the vanilla ones):
		// its best biome first, then up to two more where it is met often (GuaranteedSources.MinShare); the time of the best
		// biome's slots
		private static void FillPlaces(HintFact fact)
		{
			int type = BiomeSlots.TypeOf(fact.Npc);
			// A guaranteed item's enemies: the biome they were chosen in, the time they share there
			int chosen = fact.Kind == HintKind.Creature && fact.Hosts.Count > 0 && BiomeSlots.SpawnsPlanned ? GuaranteedSources.BiomeOf(fact.Item) : -1;
			if (chosen >= 0)
			{
				fact.Biomes = new List<string> { BiomeSlots.BiomeName(chosen) };
				// Day or Night when its biome only holds hosts at that time
				fact.Time = GuaranteedSources.PlacedTime(fact.Item);
				fact.HostTimes = fact.Hosts.Select(h => GuaranteedSources.HostTime(fact.Item, h)).ToList();
				List<BiomeSlot> hostSlots = BiomeSlots.SpawnMap.Slots.Where(s => s.Biome == chosen && SpawnsIn(s, type)).ToList();
				if (hostSlots.Count > 0) fact.Movement = Math.Min(3, hostSlots.GroupBy(s => s.Movement).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key);
				if (fact.Movement < 0 && ContentSamples.NpcsByNetId.TryGetValue(fact.Npc, out NPC hostSample)) fact.Movement = hostSample.noGravity ? 1 : 0;
				return;
			}
			// Several enemies without a biome (common hosts of the tier, spawns swapped another way): each lives elsewhere, so
			// no biome is named (the first one's would be wrong for the others, and the codex would see them all there)
			if (fact.Kind == HintKind.Creature && fact.Hosts.Count > 1)
			{
				fact.Biomes = new List<string>();
				fact.Time = SlotTime.Any;
				if (ContentSamples.NpcsByNetId.TryGetValue(fact.Npc, out NPC firstSample)) fact.Movement = firstSample.noGravity ? 1 : 0;
				return;
			}
			// Swapped another way, an enemy spawns anywhere: no biome is named
			List<(int biome, double share, SlotTime time)> biomes = BiomeSlots.SpawnsPlanned ? BiomeSlots.PlannedBiomes(type, fact.Tier) : new();
			if (biomes.Count > 0)
			{
				fact.Biomes = biomes.Where((b, i) => i == 0 || BiomeSlots.SteadyShare(b.biome, type, fact.Tier) >= GuaranteedSources.MinShare(b.biome, fact.Tier)).Take(3).Select(b => BiomeSlots.BiomeName(b.biome)).ToList();
				fact.Time = biomes[0].time;
				List<BiomeSlot> slots = BiomeSlots.SpawnMap.Slots.Where(s => s.Biome == biomes[0].biome && SpawnsIn(s, type)).ToList();
				// Never met there on an ordinary day: only with its weather, season or event
				fact.Conditional = fact.Kind == HintKind.Creature && BiomeSlots.SteadyShare(biomes[0].biome, type, fact.Tier) <= 0 && slots.Any(s => s.Conditional);
				if (slots.Count > 0) fact.Movement = Math.Min(3, slots.GroupBy(s => s.Movement).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key);
			}
			if (fact.Movement < 0 && ContentSamples.NpcsByNetId.TryGetValue(fact.Npc, out NPC sample))
			{
				fact.Movement = NPCID.Sets.CountsAsCritter[sample.type] ? 3 : sample.noGravity ? 1 : 0;
			}
		}

		// Does this enemy type spawn in the slot (owner, extra, or owner of the absent evil sharing it)? The owner of an
		// absent-evil slot spawns through the present evil's slot instead
		public static bool SpawnsIn(BiomeSlot slot, int type) =>
			BiomeSlots.Spawners(slot).Any(id => BiomeSlots.TypeOf(id) == type && !(slot.AbsentEvil && id == slot.Owner));

		// ---- Recipe trees ----

		public const int MaxTreeDepth = 6;
		public const int MaxTreeNodes = 80;

		// Everything a recipe hint needs, down to raw sources: in a randomized world the ingredients and stations of a recipe
		// come from anywhere, so the hint names the source of each one (crafted parts are expanded in turn). Planned data
		// only (planned ingredients and stations, the facts), so every machine shows the same tree. Empty when the item's
		// hint is not a recipe
		public static List<HintTreeNode> SourceTree(int item)
		{
			if (trees.TryGetValue(item, out List<HintTreeNode> tree)) return tree;
			tree = new List<HintTreeNode>();
			HintFact fact = For(item);
			if (fact != null && fact.Kind == HintKind.Recipe && !TreeParts(fact, 1, tree, new HashSet<int> { item })) truncatedTrees.Add(item);
			trees[item] = tree;
			return tree;
		}

		// Some parts were left out (MaxTreeNodes)
		public static bool TreeTruncated(int item) => truncatedTrees.Contains(item);

		// The trees only (tests: built again from facts already known)
		public static void ForgetTrees()
		{
			trees.Clear();
			truncatedTrees.Clear();
		}

		// The ingredients, then the stations of a recipe fact. An item explained earlier in the tree (or crafted from
		// itself further down) is not explained again
		// Returns false when parts were left out
		private static bool TreeParts(HintFact recipe, int depth, List<HintTreeNode> tree, HashSet<int> explained)
		{
			IEnumerable<(int type, int stack, int tile)> parts = recipe.Ingredients.Where(x => x.type > 0).Select(x => (x.type, x.stack, -1))
				.Concat(recipe.Tiles.Distinct().Select(t => (StationItem(t), 1, t)).DistinctBy(s => s.Item1 > 0 ? s.Item1 : -1 - s.t));
			foreach ((int type, int stack, int tile) in parts)
			{
				if (!TreePart(type, stack, tile, depth, tree, explained)) return false;
			}
			return true;
		}

		// One part, then what it needs: the parts of its recipe, the bug net of a critter, the empty bucket of a filled one
		private static bool TreePart(int type, int stack, int tile, int depth, List<HintTreeNode> tree, HashSet<int> explained)
		{
			if (tree.Count >= MaxTreeNodes) return false;
			HintTreeNode node = new() { Item = type, Stack = stack, Depth = depth, Station = tile >= 0, Tile = tile };
			tree.Add(node);
			bool Needs(int tool) => depth >= MaxTreeDepth || TreePart(tool, 1, -1, depth + 1, tree, explained);
			void Raw(HintTreeLeaf leaf)
			{
				node.Leaf = leaf;
				node.Also = AlsoFrom(type);
			}
			if (type <= 0) node.Leaf = HintTreeLeaf.WorldStation;
			else if (!explained.Add(type)) node.Leaf = HintTreeLeaf.Repeat;
			else if (OreMap.IsOre(type) && OreMap.IsFindable(type)) Raw(HintTreeLeaf.Mined);
			else if (Findability.IsWorldMaterial(type)) Raw(HintTreeLeaf.Gathered);
			else if (CritterOf(type) > 0)
			{
				node.Leaf = HintTreeLeaf.Caught;
				node.Also = AlsoFrom(type);
				node.Biomes = CritterBiomes(CritterOf(type));
				node.Time = CritterTime(CritterOf(type));
				return Needs(ItemID.BugNet);
			}
			else if (type is ItemID.WaterBucket or ItemID.LavaBucket or ItemID.HoneyBucket)
			{
				node.Leaf = HintTreeLeaf.Filled;
				return Needs(ItemID.EmptyBucket);
			}
			else
			{
				node.Fact = For(type);
				if (node.Fact == null)
				{
					node.Herb = HerbGiving(type);
					node.Leaf = node.Herb > 0 ? HintTreeLeaf.Harvested : Findability.Tier(type) < Findability.Never ? HintTreeLeaf.Findable : HintTreeLeaf.Unknown;
				}
				else if (node.Fact.Kind != HintKind.Recipe) node.Leaf = HintTreeLeaf.Fact;
				else
				{
					node.Leaf = HintTreeLeaf.Recipe;
					if (depth < MaxTreeDepth) return TreeParts(node.Fact, depth + 1, tree, explained);
				}
			}
			return true;
		}

		// An enemy, boss, bag or shop also giving a raw material (not a recipe, a chest or a pot: the raw source is easier)
		private static HintFact AlsoFrom(int item)
		{
			HintFact fact = For(item);
			return fact != null && fact.Kind is HintKind.Creature or HintKind.Boss or HintKind.Bag or HintKind.Shop ? fact : null;
		}

		// Where a critter is caught: the biomes of its critter slots (swapped critters live by habitat)
		public static List<string> CritterBiomes(int critter)
		{
			if (!BiomeSlots.SpawnsPlanned) return new List<string>();
			int type = BiomeSlots.TypeOf(critter);
			return BiomeSlots.SpawnMap.Slots.Where(s => s.Critter && SpawnsIn(s, type)).Select(s => s.Biome).Distinct().OrderBy(b => b)
				.Take(3).Select(BiomeSlots.BiomeName).ToList();
		}

		// When a critter is met: the times of its critter slots (fireflies at night...)
		public static SlotTime CritterTime(int critter)
		{
			if (!BiomeSlots.SpawnsPlanned) return SlotTime.Any;
			int type = BiomeSlots.TypeOf(critter);
			List<SlotTime> times = BiomeSlots.SpawnMap.Slots.Where(s => s.Critter && !s.AbsentEvil && SpawnsIn(s, type)).Select(s => s.Time).Distinct().ToList();
			return times.Count == 1 ? times[0] : SlotTime.Any;
		}

		// The plant whose harvest gives this item (randomized plants keep a few fixed results), 0 when none
		public static int HerbGiving(int item)
		{
			if (!LootConfig.Instance.RandomizeHerbs) return 0;
			foreach (int herb in Findability.HerbPlants.Where(ItemPool.IsRandomizable))
			{
				if (Features.WorldSources.HerbTable(herb).Contains(item)) return herb;
			}
			return 0;
		}

		// How a world material is gathered, and where
		private static (string how, SpawnBiome? where) Gathering(int item) => item switch
		{
			ItemID.DirtBlock or ItemID.ClayBlock => ("Dig", SpawnBiome.Forest),
			ItemID.StoneBlock or ItemID.Cobweb => ("Dig", SpawnBiome.Caverns),
			ItemID.MudBlock => ("Dig", SpawnBiome.Jungle),
			ItemID.SandBlock or ItemID.Cactus => (item == ItemID.Cactus ? "Grow" : "Dig", SpawnBiome.Desert),
			ItemID.SnowBlock or ItemID.IceBlock => ("Dig", SpawnBiome.Snow),
			ItemID.SlushBlock => ("Dig", SpawnBiome.UndergroundSnow),
			ItemID.SiltBlock => ("Dig", SpawnBiome.Caverns),
			ItemID.GraniteBlock => ("Dig", SpawnBiome.Granite),
			ItemID.MarbleBlock => ("Dig", SpawnBiome.Marble),
			ItemID.Sandstone or ItemID.HardenedSand or ItemID.DesertFossil => ("Dig", SpawnBiome.UndergroundDesert),
			ItemID.EbonstoneBlock => ("Dig", SpawnBiome.Corruption),
			ItemID.CrimstoneBlock => ("Dig", SpawnBiome.Crimson),
			ItemID.PearlstoneBlock => ("Dig", SpawnBiome.Hallow),
			ItemID.AshBlock => ("Dig", SpawnBiome.Underworld),
			ItemID.Hive => ("Dig", SpawnBiome.UndergroundJungle),
			ItemID.CrystalShard => ("Grow", SpawnBiome.UndergroundHallow),
			ItemID.Wood or ItemID.Acorn => ("Chop", SpawnBiome.Forest),
			ItemID.BorealWood => ("Chop", SpawnBiome.Snow),
			ItemID.PalmWood => ("Chop", SpawnBiome.Ocean),
			ItemID.RichMahogany => ("Chop", SpawnBiome.Jungle),
			ItemID.Ebonwood => ("Chop", SpawnBiome.Corruption),
			ItemID.Shadewood => ("Chop", SpawnBiome.Crimson),
			ItemID.Pearlwood => ("Chop", SpawnBiome.Hallow),
			ItemID.Daybloom or ItemID.Mushroom or ItemID.Pumpkin => ("Grow", SpawnBiome.Forest),
			ItemID.Moonglow or ItemID.Vine => ("Grow", SpawnBiome.Jungle),
			ItemID.JungleSpores => ("Grow", SpawnBiome.UndergroundJungle),
			ItemID.Blinkroot => ("Grow", SpawnBiome.Underground),
			ItemID.Waterleaf => ("Grow", SpawnBiome.Desert),
			ItemID.Shiverthorn => ("Grow", SpawnBiome.Snow),
			ItemID.Deathweed => ("Grow", WorldGen.crimson ? SpawnBiome.Crimson : SpawnBiome.Corruption),
			ItemID.Fireblossom => ("Grow", SpawnBiome.Underworld),
			ItemID.GlowingMushroom => ("Grow", SpawnBiome.Mushroom),
			ItemID.VileMushroom => ("Grow", SpawnBiome.Corruption),
			ItemID.ViciousMushroom => ("Grow", SpawnBiome.Crimson),
			ItemID.Amethyst or ItemID.Topaz or ItemID.Sapphire or ItemID.Emerald or ItemID.Ruby or ItemID.Diamond => ("Gem", null),
			ItemID.Amber => ("Gem", SpawnBiome.UndergroundDesert),
			ItemID.Seashell or ItemID.Coral or ItemID.Starfish => ("Beach", null),
			ItemID.FallenStar => ("Star", null),
			ItemID.Obsidian => ("Obsidian", null),
			_ => ("Other", null)
		};

		public static readonly string[] GatherKinds = { "Dig", "Chop", "Grow", "Gem", "Beach", "Star", "Obsidian", "Other" };

		// The critter this item is (caught with a bug net), 0 when none
		public static int CritterOf(int item) => ContentSamples.ItemsByType.TryGetValue(item, out Item sample) && sample.makeNPC > 0 ? sample.makeNPC : 0;

		// A part that is a hint target itself (a station, a summon, a pickaxe) keeps its own hint: its source shows only once
		// that hint is bought to the end or the item found, so one hint never gives several targets at once
		public static bool HiddenInTree(HintTreeNode node, int root) =>
			node.Item > 0 && node.Item != root && RawTarget(node.Item) != null && CodexSystem.ShownHintLevel(node.Item) < MaxLevel;

		// The name of a part: its item, or the station standing in the world
		public static string TreeName(HintTreeNode node) => node.Item > 0 ? ItemName(node.Item) : StationName(node.Tile);

		// The ore whose mining gives this one (TileDropFeature: OreMap.Get when mined ores are randomized)
		public static int MinedFrom(int ore)
		{
			if (!LootConfig.Instance.RandomizeOreDrops) return ore;
			for (int type = 1; type < ItemPool.Info.Length; type++)
			{
				if (OreMap.IsOre(type) && OreMap.IsPresent(type) && ItemPool.IsRandomizable(type) && OreMap.Get(type) == ore) return type;
			}
			return ore;
		}

		// Where a part of the tree comes from, in a few words, and another way when there is one
		public static string TreeSource(HintTreeNode node)
		{
			string main = WithWhen(MainSource(node), NodeWhen(node));
			return node.Also == null ? main : CodexText.Get("Hint.Tree.Also", main, FactSource(node.Also));
		}

		// "(night)", "(rain or event)", "(near water)", "(after smashing an altar)"... at the end of a tree line
		private static string WithWhen(string text, List<string> when) => when.Count == 0 ? text : text + " " + CodexText.Get("Hint.Tree.When", string.Join(", ", when));

		// When the main source of a part works: the conditions of a recipe, the altar of a hardmode ore, the time of a critter
		// (a fact's own are added by FactSource)
		private static List<string> NodeWhen(HintTreeNode node)
		{
			List<string> when = new();
			switch (node.Leaf)
			{
				case HintTreeLeaf.Recipe when node.Fact != null:
					string recipe = RecipeConditions(node.Fact);
					if (recipe.Length > 0) when.Add(recipe);
					break;
				case HintTreeLeaf.Mined when OreMap.IsHardmodePairOre(node.Ore):
					when.Add(CodexText.Get("Hint.When.Short.Altar"));
					break;
				case HintTreeLeaf.Caught when node.Time != SlotTime.Any:
					when.Add(CodexText.Get("Hint.When.Short." + node.Time));
					break;
			}
			return when;
		}

		// The time, weather or event and drop conditions of a fact, in a few words (empty: always). Several enemies met at
		// different times say it with their names (ShortHosts)
		public static List<string> FactWhen(HintFact fact)
		{
			List<string> when = new();
			if (fact == null) return when;
			if (fact.Time != SlotTime.Any && fact.Kind is HintKind.Creature or HintKind.Boss && !MixedTimes(fact)) when.Add(CodexText.Get("Hint.When.Short." + fact.Time));
			if (fact.Conditional) when.Add(CodexText.Get("Hint.When.Short.Conditional"));
			when.AddRange(fact.Conditions);
			return when;
		}

		private static string MainSource(HintTreeNode node)
		{
			HintFact fact = node.Fact;
			return node.Leaf switch
			{
				HintTreeLeaf.Mined => CodexText.Get("Hint.Tree.Mined", ItemName(node.Ore)),
				HintTreeLeaf.Gathered => GatherText(node.Item),
				HintTreeLeaf.Repeat => CodexText.Get("Hint.Tree.Repeat"),
				HintTreeLeaf.Unknown => CodexText.Get("Hint.Tree.Unknown"),
				HintTreeLeaf.Findable => CodexText.Get("Hint.Tree.Findable"),
				HintTreeLeaf.WorldStation => CodexText.Get("Hint.Tree.WorldStation"),
				HintTreeLeaf.Caught => node.Biomes.Count > 0
					? CodexText.Get("Hint.Tree.Caught", string.Join(", ", node.Biomes.Select(BiomeSlots.BiomeDisplayName)))
					: CodexText.Get("Hint.Tree.CaughtAnywhere"),
				HintTreeLeaf.Harvested => CodexText.Get("Hint.Tree.Harvested", ItemName(node.Herb), GatherWhere(node.Herb)),
				HintTreeLeaf.Filled => CodexText.Get("Hint.Tree.Filled." + node.Item switch { ItemID.LavaBucket => "Lava", ItemID.HoneyBucket => "Honey", _ => "Water" }),
				HintTreeLeaf.Recipe => CodexText.Get("Hint.Tree.Recipe", NamePhrase(fact)),
				_ => FactSource(fact)
			};
		}

		private static string FactSource(HintFact fact) => WithWhen(FactSourceText(fact), FactWhen(fact));

		private static string FactSourceText(HintFact fact)
		{
			return fact.Kind switch
			{
				HintKind.Creature when fact.Hosts.Count > 1 && fact.Biomes.Count > 0 => CodexText.Get("Hint.Tree.Hosts", ShortHosts(fact), BestBiomeName(fact), CodexStats.Percent(fact.Chance)),
				HintKind.Creature when fact.Hosts.Count > 1 => CodexText.Get("Hint.Tree.HostsAnywhere", ShortHosts(fact), CodexStats.Percent(fact.Chance)),
				HintKind.Creature when fact.Biomes.Count > 0 => CodexText.Get("Hint.Tree.Creature", NpcName(fact.Npc), BestBiomeName(fact), CodexStats.Percent(fact.Chance)),
				HintKind.Creature => CodexText.Get("Hint.Tree.CreatureAnywhere", NpcName(fact.Npc), CodexStats.Percent(fact.Chance)),
				HintKind.Boss => CodexText.Get("Hint.Tree.Boss", NpcName(fact.Npc), CodexStats.Percent(fact.Chance)),
				HintKind.Shop => CodexText.Get("Hint.Tree.Shop", NpcName(fact.Npc)),
				HintKind.Bag => CodexText.Get("Hint.Tree.Bag", ItemName(fact.Bag)),
				HintKind.Chest => CodexText.Get("Hint.Tree.Chest"),
				HintKind.Pot => CodexText.Get("Hint.Tree.Pot"),
				_ => CodexText.Get("Hint.Tree.World")
			};
		}

		private static string GatherWhere(int item) => Gathering(item).where is SpawnBiome biome ? BiomeSlots.BiomeDisplayName(biome.ToString()) : "";

		private static string GatherText(int item) => CodexText.Get("Hint.Tree.Gathered." + Gathering(item).how, GatherWhere(item));

		public static readonly string[] TreeTextKeys =
		{
			"Title", "Also", "OwnHint", "Station", "More", "Mined", "Repeat", "Unknown", "Findable", "WorldStation", "Caught", "CaughtAnywhere", "Harvested", "Filled.Water", "Filled.Lava",
			"Filled.Honey",
			"Recipe", "Creature", "CreatureAnywhere", "Hosts", "HostsAnywhere", "Boss", "Shop",
			"Bag", "Chest", "Pot", "World", "When"
		};

		// ---- Texts ----

		public static string LevelFamily(HintFact fact, int level)
		{
			string stem = level switch { 1 => "Kind", 2 => "Where", 3 => "Shape", _ => "Name" };
			// Several enemies without a biome: plural sentences
			if (Roaming(fact)) return stem + (fact.Hosts.Count > 1 ? ".Hosts.Roaming" : ".Roaming");
			// Several enemies: their names and the chance on each
			if (stem == "Name" && fact.Kind == HintKind.Creature && fact.Hosts.Count > 1) return MixedTimes(fact) ? "Name.HostsTimed" : "Name.Hosts";
			return stem + "." + fact.Kind;
		}

		// A creature without a known biome: its own sentences, never "here: anywhere"
		private static bool Roaming(HintFact fact) => fact.Kind == HintKind.Creature && fact.Biomes.Count == 0;

		private static int Variants(string family)
		{
			string entry = HintTexts.FirstOrDefault(t => t.StartsWith(family + "/", StringComparison.Ordinal));
			return entry != null && int.TryParse(entry.Substring(entry.IndexOf('/') + 1), out int n) ? n : 1;
		}

		// The same variant for an item and a level in a world
		public static int Variant(HintFact fact, string family)
		{
			int n = Variants(family);
			int hash = DetRandom.Hash(SeedSystem.WorldSeed, DetRandom.ItemKey(fact.Item), DetRandom.StableHash(family));
			return (int)((uint)hash % (uint)n);
		}

		// The sentence of a level (1 to 4)
		public static string Text(HintFact fact, int level)
		{
			level = Math.Clamp(level, 1, MaxLevel);
			if (fact.Kind == HintKind.World) return CodexText.Get($"Hint.World.{fact.World}.{WorldTextParts[level - 1]}", ItemName(fact.Item));
			string family = LevelFamily(fact, level);
			string key = $"Hint.{family}.{Variant(fact, family)}";
			string text = level switch
			{
				// The best biome from the first level on: without it, the other levels are a long hunt
				1 => CodexText.Get(key, StationPhrase(fact), BestBiomeName(fact)),
				2 => CodexText.Get(key, WherePhrase(fact), BiomeList(fact), TimePhrase(fact), SharePhrase(fact)),
				3 => CodexText.Get(key, PlacePhrase(fact)),
				_ => CodexText.Get(key, NamePhrase(fact), BestBiomeName(fact), CodexStats.Percent(fact.Chance))
			};
			// Bosses and shop keepers have their own sentences: the place comes after, when known (creatures name it in theirs)
			if (level is 2 or 4 && fact.Kind is HintKind.Boss or HintKind.Shop && fact.Biomes.Count > 0) text += " " + CodexText.Get("Hint.Around", PlacePhrase(fact));
			// Where the recipe must be made (near water, in the snow, in a graveyard...)
			string conditions = level is 2 or 4 ? RecipeConditions(fact) : "";
			if (conditions.Length > 0) text += " " + CodexText.Get("Hint.Conditions", conditions);
			// When the source works: the time (level 2 says it already), a weather or event, the drop's conditions
			if (level is 2 or 4) text += WhenSentences(fact, level == 4);
			if (level == MaxLevel) text += StationWays(fact);
			return text;
		}

		// A crafting station's ways, at the last level: its recipe (ingredients and stations), and its guardian when the hint
		// names another source (empty for any other item)
		public static string StationWays(HintFact fact)
		{
			string text = "";
			HintFact recipe = fact.Kind == HintKind.Recipe && RecipePlanner.StationPlacer(fact.Item, out _) ? fact : fact.StationRecipe;
			if (recipe != null) text += " " + CodexText.Get("Hint.StationRecipe", IngredientList(recipe), recipe.Tiles.Length == 0 ? CodexText.Get("Hint.NoStation") : string.Join(", ", recipe.Tiles.Select(StationName)));
			HintFact guardian = fact.Guardian;
			if (guardian != null)
			{
				string where = guardian.Biomes.Count > 0 ? CodexText.Get("Hint.GuardianWhere", BestBiomeName(guardian)) : "";
				text += " " + CodexText.Get("Hint.Guardian", NpcName(guardian.Npc), CodexStats.Percent(guardian.Chance), where);
			}
			return text;
		}

		// "3 Wood, 2 Gel"
		public static string IngredientList(HintFact recipe) => string.Join(", ", recipe.Ingredients.Where(x => x.type > 0).Select(x => $"{x.stack} {ItemName(x.type)}"));

		// " Only at night. It only comes with an event or a weather... Mind the condition: Drops on Windy Days." (empty:
		// always)
		public static string WhenSentences(HintFact fact, bool withTime)
		{
			string text = "";
			// Several enemies met at different times say it with their names
			if (withTime && fact.Time != SlotTime.Any && fact.Kind is HintKind.Creature or HintKind.Boss && !MixedTimes(fact)) text += " " + CodexText.Get("Hint.When.Time", TimePhrase(fact));
			if (fact.Conditional) text += " " + CodexText.Get("Hint.When.Conditional");
			if (fact.Conditions.Count > 0) text += " " + CodexText.Get("Hint.When.Drop", string.Join("; ", fact.Conditions));
			return text;
		}

		public static string RecipeConditions(HintFact fact) => fact.Kind == HintKind.Recipe ? RecipeConditions(fact.Recipe) : "";

		// Where a recipe must be made ("near water, in the snow..."), empty when anywhere
		public static string RecipeConditions(int recipe)
		{
			if (recipe < 0 || recipe >= Recipe.numRecipes) return "";
			return string.Join(", ", Main.recipe[recipe].Conditions.Select(c => c?.Description?.Value).Where(d => !string.IsNullOrWhiteSpace(d)));
		}

		public static string ItemName(int type) => type > 0 && type < ItemLoader.ItemCount ? Lang.GetItemNameValue(type) : "?";

		public static string NpcName(int npc) => ContentSamples.NpcsByNetId.TryGetValue(npc, out NPC sample) ? sample.TypeName : "?";

		// The kind of the main station of a recipe
		public static string StationGroup(int[] tiles)
		{
			if (tiles == null || tiles.Length == 0) return "Hand";
			return tiles[0] switch
			{
				TileID.WorkBenches or TileID.HeavyWorkBench or TileID.TinkerersWorkbench => "Bench",
				TileID.Anvils or TileID.MythrilAnvil => "Anvil",
				TileID.Furnaces or TileID.Hellforge or TileID.AdamantiteForge or TileID.LihzahrdFurnace or TileID.GlassKiln => "Forge",
				TileID.DemonAltar => "Altar",
				TileID.Tables or TileID.Tables2 or TileID.Chairs or TileID.Bookcases or TileID.Sinks => "Furniture",
				TileID.Bottles or TileID.AlchemyTable or TileID.CookingPots or TileID.Kegs => "Brewing",
				TileID.CrystalBall or TileID.ImbuingStation or TileID.LunarCraftingStation => "Magic",
				_ => "Machine"
			};
		}

		public static readonly string[] StationGroups = { "Hand", "Bench", "Anvil", "Forge", "Altar", "Furniture", "Brewing", "Magic", "Machine" };
		public static readonly string[] Movements = { "Ground", "Flying", "Water", "Critter" };

		private static string StationPhrase(HintFact fact) => fact.Kind == HintKind.Recipe ? CodexText.Get("Hint.Station." + StationGroup(fact.Tiles)) : "";

		private static string WherePhrase(HintFact fact)
		{
			if (fact.Kind == HintKind.Creature) return CodexText.Get("Hint.Move." + Movements[Math.Clamp(fact.Movement, 0, 3)]);
			return CodexText.Get("Hint.Band." + Math.Clamp(fact.Band, 0, 3));
		}

		private static string BiomeList(HintFact fact) => string.Join(", ", fact.Biomes.Select(BiomeSlots.BiomeDisplayName));

		// How much of its best biome's spawns the creature makes (empty when unknown)
		private static string SharePhrase(HintFact fact) => fact.Kind == HintKind.Creature && fact.Share > 0 && fact.Biomes.Count > 0
			? CodexText.Get("Hint.Share", CodexStats.Percent((float)fact.Share), BestBiomeName(fact)) : "";

		private static string BestBiomeName(HintFact fact) => fact.Biomes.Count > 0 ? BiomeSlots.BiomeDisplayName(fact.Biomes[0]) : "";

		// The biomes and the time (only used when they are known)
		public static string PlacePhrase(HintFact fact) => fact.Biomes.Count > 0 ? CodexText.Get("Hint.Place.Known", BiomeList(fact), TimePhrase(fact)) : "";

		private static string TimePhrase(HintFact fact) => CodexText.Get("Hint.Time." + fact.Time);

		private static string NamePhrase(HintFact fact) => fact.Kind switch
		{
			HintKind.Creature when fact.Hosts.Count > 1 => HostsPhrase(fact),
			HintKind.Creature or HintKind.Boss or HintKind.Shop => NpcName(fact.Npc),
			HintKind.Bag => ItemName(fact.Bag),
			HintKind.Recipe => fact.Tiles.Length == 0 ? CodexText.Get("Hint.NoStation") : string.Join(", ", fact.Tiles.Select(StationName)),
			_ => ItemName(fact.Item)
		};

		// Names shown before "and N others"
		public const int NamedHosts = 3;

		// "Zombie, Blue Slime, Demon Eye and 3 others" (every name up to four); enemies met at different times by group:
		// "Blue Slime and Pinky (by day); Zombie, Demon Eye, Raven and 2 others (at night)"
		public static string HostsPhrase(HintFact fact) => ByTime(fact, NamesPhrase, "Hint.Time.");

		private static string NamesPhrase(List<string> names)
		{
			if (names.Count <= 1) return names.FirstOrDefault() ?? "";
			if (names.Count <= NamedHosts + 1) return CodexText.Get("Hint.Hosts.List", string.Join(", ", names.Take(names.Count - 1)), names[^1]);
			return CodexText.Get("Hint.Hosts.AndMore", string.Join(", ", names.Take(NamedHosts)), names.Count - NamedHosts);
		}

		// "Zombie, Blue Slime +4", or by time "Blue Slime +1 (day); Zombie +3 (night)" (craft trees: every name on hover,
		// HostNames)
		public static string ShortHosts(HintFact fact)
		{
			if (!MixedTimes(fact))
			{
				List<string> names = fact.Hosts.Select(NpcName).ToList();
				return names.Count <= 2 ? string.Join(", ", names) : CodexText.Get("Hint.Hosts.Plus", string.Join(", ", names.Take(2)), names.Count - 2);
			}
			return ByTime(fact, names => names.Count <= 1 ? names.FirstOrDefault() ?? "" : CodexText.Get("Hint.Hosts.Plus", names[0], names.Count - 1), "Hint.When.Short.");
		}

		public static string HostNames(HintFact fact) => MixedTimes(fact) ? ByTime(fact, names => string.Join(", ", names), "Hint.Time.") : string.Join(", ", fact.Hosts.Select(NpcName));

		// Are the enemies of a fact met at different times?
		public static bool MixedTimes(HintFact fact) => fact.HostTimes.Count == fact.Hosts.Count && fact.HostTimes.Distinct().Count() > 1;

		// The names of a fact's enemies, grouped by the time they are met (in the order of the first of each group) when the
		// times differ
		private static string ByTime(HintFact fact, Func<List<string>, string> phrase, string timeKey)
		{
			if (!MixedTimes(fact)) return phrase(fact.Hosts.Select(NpcName).ToList());
			List<string> groups = new();
			foreach (SlotTime time in fact.HostTimes.Distinct())
			{
				List<string> names = fact.Hosts.Where((h, i) => fact.HostTimes[i] == time).Select(NpcName).ToList();
				groups.Add(CodexText.Get("Hint.Hosts.Group", CodexText.Get(timeKey + time), phrase(names)));
			}
			return string.Join("; ", groups);
		}

		// The item placing a station, else its map name
		public static int StationItem(int tile) => TileLoader.GetItemDropFromTypeAndStyle(tile, Recipe.GetRequiredTileStyle(tile));

		public static string StationName(int tile)
		{
			int item = StationItem(tile);
			return item > 0 ? ItemName(item) : Lang.GetMapObjectName(MapHelper.TileToLookup(tile, Recipe.GetRequiredTileStyle(tile)));
		}

		// Every sentence any target could give (tests: no raw key, in each language)
		public static IEnumerable<string> AllTextKeys()
		{
			foreach (string entry in HintTexts)
			{
				string family = entry.Substring(0, entry.IndexOf('/'));
				for (int v = 0; v < Variants(family); v++) yield return $"Hint.{family}.{v}";
			}
			foreach (string key in WorldKeys)
			{
				foreach (string part in WorldTextParts) yield return $"Hint.World.{key}.{part}";
			}
			foreach (string group in StationGroups) yield return "Hint.Station." + group;
			foreach (string key in TreeTextKeys) yield return "Hint.Tree." + key;
			foreach (string kind in GatherKinds) yield return "Hint.Tree.Gathered." + kind;
			yield return "Hint.Around";
			yield return "Hint.StationRecipe";
			yield return "Hint.Guardian";
			yield return "Hint.GuardianWhere";
			yield return "Hint.Place.Known";
			yield return "Hint.Share";
			yield return "Hint.Hosts.List";
			yield return "Hint.Hosts.AndMore";
			yield return "Hint.Hosts.Plus";
			yield return "Hint.Hosts.Group";
			yield return "Hint.When.Time";
			yield return "Hint.When.Conditional";
			yield return "Hint.When.Drop";
			yield return "Hint.When.Short.Conditional";
			yield return "Hint.When.Short.Altar";
			foreach (Tier tier in Enum.GetValues<Tier>()) yield return "Hint.Unlock." + tier;
			yield return "Hint.LockedTitle";
			yield return "Hint.LockedSay";
			yield return "Hint.LockedWhen";
			foreach (string move in Movements) yield return "Hint.Move." + move;
			for (int b = 0; b < 4; b++) yield return "Hint.Band." + b;
			foreach (SlotTime time in Enum.GetValues<SlotTime>())
			{
				yield return "Hint.Time." + time;
				yield return "Hint.When.Short." + time;
			}
		}

		// Price as coins ("1 platinum 54 gold")
		public static string PriceText(long copper)
		{
			if (copper <= 0) return CodexText.Get("Hint.Free");
			int[] coins = Utils.CoinsSplit(copper);
			List<string> parts = new();
			string[] names = { "Copper", "Silver", "Gold", "Platinum" };
			for (int i = 3; i >= 0; i--)
			{
				if (coins[i] > 0) parts.Add(CodexText.Get("Hint.Coin." + names[i], coins[i]));
			}
			return string.Join(" ", parts);
		}
	}
}

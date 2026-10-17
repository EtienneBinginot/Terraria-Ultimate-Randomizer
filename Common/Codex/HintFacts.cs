using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.Map;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Codex.Interface;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Codex
{
	// Where a hint says the item comes from. The order is the preference between sources of the same tier.
	public enum HintKind : byte { Creature, Recipe, Boss, Bag, Chest, Pot, Shop, World }

	public enum HintTargetKind : byte { Station, Summon, Pickaxe }

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
	}

	// Hint targets, facts, prices and sentences. Pure data from the planners (RecipePlanner, GuaranteedSources,
	// RandomNpcDropRule.Plan, BagPlan, BiomeSlots), the seed and synced settings, so every machine gives the same hints.
	public static class HintFacts
	{
		public const int MaxLevel = 4;

		// Text families ("Codex.Hint." + family + "." + variant) and their variant counts; tools/py/check_loc.py reads this list
		public static readonly string[] HintTexts =
		{
			"Kind.Creature/3", "Kind.Recipe/3", "Kind.Boss/3", "Kind.Bag/3", "Kind.Chest/3", "Kind.Pot/3", "Kind.Shop/3",
			"Where.Creature/3", "Where.Roaming/3", "Where.Recipe/3", "Where.Boss/3", "Where.Bag/3", "Where.Chest/3", "Where.Pot/3", "Where.Shop/3",
			"Shape.Creature/2", "Shape.Recipe/2", "Shape.Boss/2", "Shape.Bag/2", "Shape.Chest/2", "Shape.Pot/2", "Shape.Shop/2",
			"Name.Creature/2", "Name.Recipe/2", "Name.Boss/2", "Name.Bag/2", "Name.Chest/2", "Name.Pot/2", "Name.Shop/2"
		};

		// Vanilla summons found in the world rather than dropped or crafted (Protection.WorldSummons): (item, text key, critter to show)
		private static readonly (int item, string key, int npc)[] WorldSources =
		{
			(ItemID.TruffleWorm, "TruffleWorm", NPCID.TruffleWorm),
			(ItemID.EmpressButterfly, "EmpressButterfly", NPCID.EmpressButterfly),
			(ItemID.LihzahrdPowerCell, "PowerCell", 0),
			(ItemID.QueenSlimeCrystal, "GelatinCrystal", 0)
		};

		// Texts of the world sources: "Codex.Hint.World." + key + "." + one of these
		public static readonly string[] WorldTextParts = { "Kind", "Where", "Shape", "Name" };

		public static IEnumerable<string> WorldKeys => WorldSources.Select(w => w.key);

		private static Dictionary<int, HintTarget> rawTargets;
		private static List<HintTarget> candidates;
		private static readonly Dictionary<int, HintFact> facts = new();
		private static Dictionary<int, List<(int npc, int tier)>> planDrops;
		private static Dictionary<int, List<(int bag, int tier)>> bagContents;
		private static Dictionary<int, List<int>> placers;
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
			planDrops = null;
			bagContents = null;
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
				if (info.Kind != StationKind.Progression || tile == TileID.DemonAltar) continue;
				// The vanilla item of the station that is obtained first (Mythril or Orichalcum Anvil...)
				int best = Placers(tile).Where(p => p < ItemID.Count).OrderBy(p => Availability(plan, p)).ThenBy(p => p).FirstOrDefault();
				if (best > 0) Add(best, info.Tier, HintTargetKind.Station);
			}
			// Boss and event summons (not the life crystals and mirrors sorted with them), needed before their boss
			for (int type = 1; type < ItemPool.Info.Length; type++)
			{
				if (Protection.IsBossSummon(type)) Add(type, Protection.SummonTier(type), HintTargetKind.Summon);
			}
			// The pickaxes placed on enemies for a mining level: cheap ones only
			foreach (GuaranteedSources.Requirement r in GuaranteedSources.Requirements.Where(r => r.Tool)) Add(r.Item, r.Tier, HintTargetKind.Pickaxe);
			rawTargets = byItem;
			return byItem;
		}

		// Every target with a source, by tier, whatever the progression
		public static List<HintTarget> AllTargets()
		{
			if (candidates != null) return candidates;
			candidates = RawTargets().Values.Where(t => For(t.Item) != null)
				.OrderBy(t => t.Tier).ThenBy(t => t.Kind).ThenBy(t => DetRandom.ItemName(t.Item), StringComparer.Ordinal).ToList();
			return candidates;
		}

		// Targets offered now: up to one tier above the unlocked one
		public static List<HintTarget> Targets(int unlockedTier) => AllTargets().Where(t => t.Tier <= unlockedTier + 1).ToList();

		public static List<HintTarget> Targets() => Targets(ProgressionSystem.UnlockedTier);

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

		private static List<int> Placers(int tile)
		{
			if (placers == null)
			{
				placers = new Dictionary<int, List<int>>();
				foreach ((int type, Item sample) in ContentSamples.ItemsByType)
				{
					if (sample.createTile < TileID.Dirt) continue;
					if (!placers.TryGetValue(sample.createTile, out List<int> list)) placers[sample.createTile] = list = new List<int>();
					list.Add(type);
				}
			}
			return placers.TryGetValue(tile, out List<int> found) ? found : new List<int>();
		}

		// Tier from which a station can be placed (tiles without an item, like the Demon Altar, are part of the world)
		private static int TileTier(RecipePlan plan, int tile)
		{
			List<int> list = Placers(tile);
			return list.Count == 0 ? 0 : list.Min(p => Availability(plan, p));
		}

		public static int Band(int tier) => tier <= 0 ? 0 : tier <= 3 ? 1 : tier <= 6 ? 2 : 3;

		private static int Gated(int item)
		{
			ProgressionConfig progression = ProgressionConfig.Instance;
			return progression.TierGating ? Math.Max(0, ItemPool.TierOf(item) - progression.TierAllowance) : 0;
		}

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
			HashSet<int> wanted = WantedItems();
			Dictionary<int, List<(int, int)>> drops = new();
			Dictionary<int, List<(int, int)>> bags = new();
			float minChance = ProgressionConfig.Instance.MinFindableChance / 100f;
			void Add(Dictionary<int, List<(int, int)>> table, int item, int from, int tier)
			{
				if (!table.TryGetValue(item, out List<(int, int)> list)) table[item] = list = new List<(int, int)>();
				list.Add((from, tier));
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
			foreach (int boss in NpcTiers.BossTypes)
			{
				if (done.Add(boss)) enemies.Add((boss, NpcTiers.BaseTier(boss)));
			}
			foreach ((int npc, int tier) in enemies)
			{
				foreach (PlannedDrop drop in RandomNpcDropRule.Plan(npc, Main.expertMode, lucky: true, guaranteed: false))
				{
					if (wanted.Contains(drop.Item) && drop.Chance >= minChance && Findability.WorldAllows(drop.Conditions)) Add(drops, drop.Item, npc, Math.Max(tier, Gated(drop.Item)));
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
					if (wanted.Contains(drop.Item) && drop.Chance >= minChance && Findability.WorldAllows(drop.Conditions) && inside.Add(drop.Item)) Add(bags, drop.Item, bag, Math.Max(bagTier, Gated(drop.Item)));
				}
				yield return true;
			}
			bagContents = bags;
			planDrops = drops;
		}

		// Items that may become targets (before their facts are known)
		private static HashSet<int> WantedItems()
		{
			HashSet<int> set = Stations.Basic().Select(s => s.info.Item).ToHashSet();
			foreach (int tile in Stations.RequiredTiles())
			{
				if (Stations.Info(tile).Kind == StationKind.Progression) set.UnionWith(Placers(tile));
			}
			for (int type = 1; type < ItemPool.Info.Length; type++)
			{
				if (Protection.IsBossSummon(type)) set.Add(type);
			}
			foreach (GuaranteedSources.Requirement r in GuaranteedSources.Requirements.Where(r => r.Tool)) set.Add(r.Item);
			return set;
		}

		// Town NPCs selling an item in their original shop (protected items keep their shop when shops are randomized)
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
			if (EconomyConfig.Instance.RandomizeShops && ItemPool.IsRandomizable(item)) return 0;
			return shopKeepers.TryGetValue(item, out int npc) ? npc : 0;
		}

		private static HintFact Build(int item)
		{
			if (!Ready || !ItemPool.IsValid(item)) return null;
			RecipePlan plan = RecipePlanner.Get();
			List<HintFact> options = new();

			// Guaranteed drop of a common enemy (basic stations, pickaxes)
			foreach (int host in GuaranteedSources.HostsOf(item))
			{
				options.Add(new HintFact { Item = item, Kind = HintKind.Creature, Npc = host, Tier = NpcTiers.BaseTier(host) });
			}
			foreach (int recipe in RecipesMaking(item))
			{
				(int type, int stack)[] ingredients = PlannedIngredients(recipe);
				if (ingredients.Any(x => x.type == item)) continue;
				int[] tiles = PlannedTiles(recipe);
				int ingredientTier = ingredients.Select(x => Availability(plan, x.type)).DefaultIfEmpty(0).Max();
				int tier = Math.Max(plan.ResultTiers[recipe], Math.Max(ingredientTier, tiles.Where(t => t >= 0).Select(t => TileTier(plan, t)).DefaultIfEmpty(0).Max()));
				if (tier >= Findability.Never) continue;
				options.Add(new HintFact { Item = item, Kind = HintKind.Recipe, Recipe = recipe, Ingredients = ingredients, Tiles = tiles.Where(t => t >= 0).ToArray(), Tier = tier, Band = Band(ingredientTier) });
			}
			EnsureIndexes();
			if (planDrops.TryGetValue(item, out List<(int npc, int tier)> droppers))
			{
				foreach ((int npc, int tier) in droppers)
				{
					bool boss = RandomNpcDropRule.IsBossType(BossParts.Owner(npc));
					options.Add(new HintFact { Item = item, Kind = boss ? HintKind.Boss : HintKind.Creature, Npc = npc, Tier = tier, Band = Band(tier) });
				}
			}
			if (bagContents.TryGetValue(item, out List<(int bag, int tier)> holders))
			{
				foreach ((int bag, int tier) in holders) options.Add(new HintFact { Item = item, Kind = HintKind.Bag, Bag = bag, Tier = tier, Band = Band(tier) });
			}
			GuaranteedSources.Requirement requirement = GuaranteedSources.Requirements.FirstOrDefault(r => r.Item == item);
			bool required = requirement.Item == item;
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
				// When the keeper moves in (town NPC arrival order of this world), else the item's own tier
				int arrival = TownNpcOrderSystem.ArrivalTier(keeper);
				int tier = arrival >= 0 ? Math.Max(arrival, Gated(item)) : ItemPool.TierOf(item);
				options.Add(new HintFact { Item = item, Kind = HintKind.Shop, Npc = keeper, Tier = tier, Band = Band(tier) });
			}
			foreach ((int worldItem, string key, int npc) in WorldSources)
			{
				if (worldItem == item) options.Add(new HintFact { Item = item, Kind = HintKind.World, World = key, Npc = npc, Tier = ItemPool.TierOf(item) });
			}
			if (options.Count == 0) return null;
			// The source reached first; between equal tiers, the preferred kind, then a fixed order
			HintFact best = options.OrderBy(o => o.Tier).ThenBy(o => o.Kind).ThenBy(o => o.Npc).ThenBy(o => o.Recipe).ThenBy(o => o.Bag).First();
			if (best.Kind is HintKind.Creature) FillPlaces(best);
			return best;
		}

		// Where the host spawns: its slots in the biome map (its own vanilla slots when spawns are not redistributed;
		// none when spawns are swapped in another way)
		private static void FillPlaces(HintFact fact)
		{
			int type = BiomeSlots.TypeOf(fact.Npc);
			bool active = BiomeSlots.Active;
			if (active || !EnemiesConfig.Instance.RandomizeEnemySpawns)
			{
				List<BiomeSlot> slots = BiomeSlots.Current.Slots.Where(s => active ? SpawnsIn(s, type) : BiomeSlots.TypeOf(s.Original) == type).ToList();
				if (slots.Count > 0)
				{
					fact.Biomes = slots.Select(s => BiomeSlots.BiomeName(s.Biome)).Distinct().OrderBy(DetRandom.StableHash).Take(3).ToList();
					fact.Time = slots.All(s => s.Time == SlotTime.Day) ? SlotTime.Day : slots.All(s => s.Time == SlotTime.Night) ? SlotTime.Night : SlotTime.Any;
					fact.Movement = Math.Min(3, slots.GroupBy(s => s.Movement).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key);
				}
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

		// ---- Texts ----

		public static string LevelFamily(HintFact fact, int level) => level switch
		{
			1 => "Kind." + fact.Kind,
			2 => fact.Kind == HintKind.Creature && fact.Biomes.Count == 0 ? "Where.Roaming" : "Where." + fact.Kind,
			3 => "Shape." + fact.Kind,
			_ => "Name." + fact.Kind
		};

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
				1 => CodexText.Get(key, StationPhrase(fact)),
				2 => CodexText.Get(key, WherePhrase(fact), BiomeList(fact), TimePhrase(fact)),
				3 => CodexText.Get(key),
				_ => CodexText.Get(key, NamePhrase(fact))
			};
			// Where the recipe must be made (near water, in the snow, in a graveyard...)
			string conditions = level is 2 or 4 ? RecipeConditions(fact) : "";
			return conditions.Length > 0 ? text + " " + CodexText.Get("Hint.Conditions", conditions) : text;
		}

		public static string RecipeConditions(HintFact fact)
		{
			if (fact.Kind != HintKind.Recipe || fact.Recipe < 0 || fact.Recipe >= Recipe.numRecipes) return "";
			return string.Join(", ", Main.recipe[fact.Recipe].Conditions.Select(c => c?.Description?.Value).Where(d => !string.IsNullOrWhiteSpace(d)));
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

		private static string TimePhrase(HintFact fact) => CodexText.Get("Hint.Time." + fact.Time);

		private static string NamePhrase(HintFact fact) => fact.Kind switch
		{
			HintKind.Creature or HintKind.Boss or HintKind.Shop => NpcName(fact.Npc),
			HintKind.Bag => ItemName(fact.Bag),
			HintKind.Recipe => fact.Tiles.Length == 0 ? CodexText.Get("Hint.NoStation") : string.Join(", ", fact.Tiles.Select(StationName)),
			_ => ItemName(fact.Item)
		};

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
			foreach (string move in Movements) yield return "Hint.Move." + move;
			for (int b = 0; b < 4; b++) yield return "Hint.Band." + b;
			foreach (SlotTime time in Enum.GetValues<SlotTime>()) yield return "Hint.Time." + time;
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

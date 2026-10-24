using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Tests
{
	// "Giga export" for an offline coherence audit: tab separated files in <SavePath>/UltimateRandomizer/Export/ (README.txt
	// describes the columns). Always passes; read-only (only reads the live plans and caches), each file is written inside its
	// own try/catch so one failure does not lose the others. Logged as "WorldExport: <file> <rows>"
	public static class ExportTests
	{
		private const int Never = Findability.Never;

		public static void Register()
		{
			TestRunner.Register("WorldExport", WorldExport);
		}

		private static string Show(int tier) => tier >= Never ? "never" : tier.ToString();

		private static string Name(int item) => item > 0 ? Lang.GetItemNameValue(item) : "";

		private static string Internal(int item) => item <= 0 ? "" : item < ItemID.Count ? ItemID.Search.GetName(item) : DetRandom.ItemName(item);

		private static string ModOf(int item) => item < ItemID.Count ? "Terraria" : ItemLoader.GetItem(item)?.Mod.Name ?? "?";

		private static string NpcInternal(int netId) => netId > 0 && netId < NPCID.Count ? NPCID.Search.GetName(netId) : (NPCLoader.GetNPC(netId)?.FullName ?? netId.ToString());

		private static string NpcName(int netId) => Lang.GetNPCNameValue(netId);

		private static string Clean(string text) => (text ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

		private static string ItemRef(int item) => item > 0 ? $"{Internal(item)} ({Name(item)})" : "";

		private static string TierOfItem(int item) => ItemPool.IsValid(item) ? ItemPool.Info[item].Tier.ToString() : "";

		private static string Row(params object[] cells) => string.Join("\t", cells.Select(c => Clean(c?.ToString())));

		private static void WorldExport()
		{
			if (!SeedSystem.WorldReady || !ItemPool.Ready) Check.Skip("world or item pool not ready");
			string dir = Path.Combine(Main.SavePath, "UltimateRandomizer", "Export");
			Directory.CreateDirectory(dir);
			log4net.ILog log = ModContent.GetInstance<UltimateRandomizer>().Logger;
			void Write(string file, Func<(string header, List<string> rows)> build)
			{
				try
				{
					(string header, List<string> rows) = build();
					File.WriteAllText(Path.Combine(dir, file), header + "\n" + string.Join("\n", rows) + (rows.Count > 0 ? "\n" : ""));
					log.Info($"WorldExport: {file} {rows.Count}");
				}
				catch (Exception e)
				{
					log.Error($"WorldExport: {file} FAILED: {e}");
				}
			}
			RecipePlan plan = null;
			try
			{
				if (RecipeSystem.RecipeCount > 0) plan = RecipePlanner.Get();
			}
			catch (Exception e)
			{
				log.Error("WorldExport: recipe plan unavailable: " + e);
			}
			if (plan?.Availability == null) plan = null;
			// Shared between the drop, bag and findability files
			Dictionary<int, List<PlannedDrop>> npcPlans = new();
			Dictionary<int, List<PlannedDrop>> bagPlans = new();
			foreach (int netId in ContentSamples.NpcsByNetId.Keys.OrderBy(n => n))
			{
				try
				{
					List<PlannedDrop> drops = RandomNpcDropRule.Plan(netId, expert: false, lucky: true);
					if (drops.Count > 0) npcPlans[netId] = drops;
				}
				catch (Exception e)
				{
					log.Error($"WorldExport: npc plan {netId}: {e.Message}");
				}
			}
			for (int bag = 1; bag < ItemLoader.ItemCount; bag++)
			{
				try
				{
					if (!LootSnapshot.ForItem(bag).Any()) continue;
					List<PlannedDrop> drops = BagPlan.Plan(bag, lucky: true);
					if (drops.Count > 0) bagPlans[bag] = drops;
				}
				catch (Exception e)
				{
					log.Error($"WorldExport: bag plan {bag}: {e.Message}");
				}
			}
			Write("items.tsv", () => Items(plan));
			Write("recipes.tsv", () => Recipes(plan));
			Write("drops.tsv", () => Drops(npcPlans));
			Write("bags.tsv", () => Bags(bagPlans));
			Write("shops.tsv", Shops);
			Write("chests.tsv", Chests);
			Write("worldsources.tsv", WorldSourceRows);
			Write("findability.tsv", () => FindabilityRows(plan, npcPlans, bagPlans));
			try
			{
				File.WriteAllText(Path.Combine(dir, "README.txt"), Readme);
				log.Info("WorldExport: README.txt 1");
			}
			catch (Exception e)
			{
				log.Error("WorldExport: README.txt FAILED: " + e.Message);
			}
		}

		// ---- items.tsv: the ItemTierDump columns plus the mod
		private static (string, List<string>) Items(RecipePlan plan)
		{
			int count = plan != null ? Math.Min(RecipeSystem.RecipeCount, Recipe.numRecipes) : 0;
			(int type, int stack)[][] vanillaIngredients = new (int, int)[count][];
			int[] vanillaResults = new int[count];
			int[][] vanillaTiles = new int[count][];
			int[][] vanillaGroups = new int[count][];
			for (int i = 0; i < count; i++)
			{
				vanillaResults[i] = RecipeSystem.OriginalResult(i);
				vanillaIngredients[i] = RecipeSystem.OriginalIngredients(i);
				vanillaTiles[i] = RecipeSystem.OriginalTiles(i);
				vanillaGroups[i] = RecipeSystem.OriginalGroups(i);
			}
			int[] vanillaAvail = null;
			if (plan != null)
			{
				int[] vanillaResultTiers = Enumerable.Range(0, count).Select(i => RecipePlanner.ResultTier(i, vanillaResults[i])).ToArray();
				vanillaAvail = RecipePlanner.Availability(Findability.BaseTier, vanillaResults, vanillaResultTiers, vanillaIngredients, vanillaTiles, out _, null, vanillaGroups);
			}
			int[] firstRecipe = new int[ItemPool.Info.Length];
			Array.Fill(firstRecipe, -1);
			for (int i = 0; i < count; i++)
			{
				int result = plan.Results[i].type;
				if (result <= 0 || result >= firstRecipe.Length || firstRecipe[result] >= 0) continue;
				if ((plan.Twin != null && plan.Twin[i]) || (plan.LoopOff != null && plan.LoopOff[i])) continue;
				firstRecipe[result] = i;
			}
			string Bracket(int item) => plan != null && item > 0 && item < plan.Availability.Length ? Show(plan.Availability[item]) : "?";
			Dictionary<int, string> oresByPick = new();
			List<string> rows = new();
			for (int type = 1; type < Math.Min(ItemLoader.ItemCount, ItemPool.Info.Length); type++)
			{
				ItemInfo info = ItemPool.Info[type];
				
				ContentSamples.ItemsByType.TryGetValue(type, out Item sample);
				string ingredients = "", key = "", ores = "";
				int recipe = firstRecipe[type];
				if (recipe >= 0)
				{
					ingredients = string.Join("; ", (plan.Ingredients[recipe] ?? vanillaIngredients[recipe]).Select(x => $"{Name(x.type)} x{x.stack} [{Bracket(x.type)}]"));
					if (plan.PlannedKeys != null && plan.PlannedKeys[recipe] > 0) key = Name(plan.PlannedKeys[recipe]);
				}
				int pick = sample?.pick ?? 0;
				if (pick > 0 && !oresByPick.TryGetValue(pick, out ores))
				{
					ores = string.Join(", ", RecipePlanner.OresFrom(pick).Where(ItemPool.IsValid).OrderBy(o => o).Select(Name));
					oresByPick[pick] = ores;
				}
				rows.Add(Row(type, Internal(type), Name(type), info.Category, sample?.rare ?? 0, sample?.value ?? 0, info.RarityTier, info.Tier,
					info.TrueTier, info.SourceTier, info.Source ?? "none", info.FromTable, ItemClassifier.HasKnownTier(type), info.Excluded, info.Banned, info.Protected,
					Show(Findability.Tier(type)), vanillaAvail != null && type < vanillaAvail.Length ? Show(vanillaAvail[type]) : "", Bracket(type),
					ingredients, key, pick, ores, RecipePlanner.PickToMine(type), ModOf(type)));
			}
			return ("id\tinternal name\tname\tcategory\trarity\tvalue\trarity tier\ttier\ttrue tier\tsource tier\tearliest source\tfrom table\tknown tier\texcluded\tbanned\tprotected\t" +
				"findability tier\tvanilla recipe tier\tplanned recipe tier\tplanned recipe ingredients\tkey ingredient\tpick power\tores it mines\tpick to mine\tmod", rows);
		}

		// ---- recipes.tsv
		private static (string, List<string>) Recipes(RecipePlan plan)
		{
			if (plan == null) throw new InvalidOperationException("no recipe plan");
			List<string> rows = new();
			int count = Math.Min(RecipeSystem.RecipeCount, Recipe.numRecipes);
			string Tiles(IEnumerable<int> tiles) => string.Join("; ", (tiles ?? Enumerable.Empty<int>()).Select(t => t >= 0 && t < TileID.Count ? TileID.Search.GetName(t) : t.ToString()));
			string Avail(int type) => type > 0 && type < plan.Availability.Length ? Show(plan.Availability[type]) : "?";
			string Ingr(IEnumerable<(int type, int stack)> list) => string.Join("; ", (list ?? Enumerable.Empty<(int, int)>()).Select(x => $"{Internal(x.type)} x{x.stack} [tier {TierOfItem(x.type)}, avail {Avail(x.type)}, value {RecipeSystem.EffectiveValue(x.type)}]"));
			for (int i = 0; i < count; i++)
			{
				Recipe recipe = Main.recipe[i];
				int vanillaResult = RecipeSystem.OriginalResult(i);
				(int type, int stack) result = plan.Results[i];
				var vanillaIngredients = RecipeSystem.OriginalIngredients(i);
				int[] vanillaTiles = RecipeSystem.OriginalTiles(i);
				var planned = plan.Ingredients[i] ?? vanillaIngredients;
				int[] plannedTiles = plan.Tiles?[i] ?? vanillaTiles;
				int partner = plan.SwapPartner != null ? plan.SwapPartner[i] : -1;
				bool shell = RecipeSystem.IsShell(i);
				bool modded = RecipeSystem.IsModded(i);
				bool twin = plan.Twin != null && plan.Twin[i];
				bool loopOff = plan.LoopOff != null && plan.LoopOff[i];
				int doable = 0;
				foreach ((int type, int stack) x in planned ?? Array.Empty<(int, int)>())
				{
					if (x.type > 0 && x.type < plan.Availability.Length) doable = Math.Max(doable, plan.Availability[x.type]);
				}
				foreach (int tile in plannedTiles ?? Array.Empty<int>())
				{
					int station = GuaranteedSources.StationItem(tile);
					if (station > 0 && station < plan.Availability.Length) doable = Math.Max(doable, plan.Availability[station]);
				}
				int resultTier = ItemPool.IsValid(result.type) ? ItemPool.Info[result.type].Tier : -1;
				rows.Add(Row(i, recipe.Mod?.Name ?? "Terraria", ItemRef(result.type), result.stack, resultTier,
					Ingr(vanillaIngredients), Tiles(vanillaTiles), Ingr(planned), Tiles(plannedTiles),
					plan.PlannedKeys != null && plan.PlannedKeys[i] > 0 ? Internal(plan.PlannedKeys[i]) : "",
					RecipeSystem.IsRandomized(i), plan.IngredientTiers != null && plan.IngredientTiers[i] >= 0, partner >= 0, partner >= 0 ? partner : "",
					shell, shell && Protection.IsUpgrade(result.type), !modded && !shell && !RecipeSystem.Touchable(i), RecipeSystem.HouseRecipe(i),
					twin ? "twin" : loopOff ? "loopoff" : "", plan.Repaired != null && plan.Repaired[i], plan.ValueShort != null && plan.ValueShort[i],
					RecipeSystem.OriginalIngredientValue(i), plan.IngredientValues != null ? plan.IngredientValues[i] : 0,
					twin || loopOff ? "off" : Show(doable),
					plan.IngredientTiers != null ? plan.IngredientTiers[i] : -1, plan.LockKeys != null && plan.LockKeys[i] > 0 ? Internal(plan.LockKeys[i]) : "",
					vanillaResult > 0 ? Internal(vanillaResult) : ""));
			}
			return ("index\tmod\tresult\tstack\tresult item tier\tvanilla ingredients\tvanilla stations\tplanned ingredients\tplanned stations\tkey ingredient\t" +
				"randomized\trebuilt\tswapped\tswap partner\tshell\tupgrade shell\tprotected\thouse\tswitched off\trepaired\tvalue short\t" +
				"vanilla value\tplanned value\tplanned doable tier\tingredient tier T\tlock key\tvanilla result", rows);
		}

		// ---- drops.tsv and bags.tsv
		private const string DropHeader = "owner\towner name\towner tier\tboss\tspawn tier\texpert\tidx\titem\titem tier\tstack min\tstack max\tchance\tconditions\tkind\t" +
			"original item\torigin npc\torigin index\tlock gap\tguaranteed single\tjackpot gap";

		private static string Conditions(PlannedDrop d)
		{
			if (d.Conditions == null) return "";
			return string.Join(" & ", d.Conditions.Where(c => c != null).Select(c =>
			{
				try
				{
					return c.GetConditionDescription() ?? c.GetType().Name;
				}
				catch
				{
					return c.GetType().Name;
				}
			}));
		}

		private static string Kind(PlannedDrop d, bool placed, DropPermutation.Placed p)
		{
			if (d.Guaranteed) return "guaranteed";
			if (d.Extra) return "extra";
			if (d.Surprise) return "surprise";
			if (placed) return p.Permuted ? "permuted" : "kept";
			return d.Original != 0 && d.Original != d.Item ? "replaced" : "kept";
		}

		private static void DropRows(List<string> rows, string owner, string ownerName, string ownerTier, string boss, string spawn, bool expert, List<PlannedDrop> drops, DropPermutation.Placed[] table)
		{
			for (int i = 0; i < drops.Count; i++)
			{
				PlannedDrop d = drops[i];
				bool placed = table != null && i < table.Length && !d.Extra && !d.Guaranteed;
				DropPermutation.Placed p = placed ? table[i] : default;
				rows.Add(Row(owner, ownerName, ownerTier, boss, spawn, expert, i, ItemRef(d.Item), TierOfItem(d.Item), d.StackMin, d.StackMax, d.Chance.ToString("0.#####"),
					Conditions(d), Kind(d, placed, p), d.Original > 0 ? Internal(d.Original) : "", placed && p.Permuted ? NpcInternal(p.FromNetId) : "", placed && p.Permuted ? p.FromIndex : "",
					d.LockGap, d.Guaranteed && d.Single, d.JackpotGap));
			}
		}

		private static (string, List<string>) Drops(Dictionary<int, List<PlannedDrop>> npcPlans)
		{
			List<string> rows = new();
			foreach ((int netId, List<PlannedDrop> normal) in npcPlans)
			{
				// A boss part that never drops (its plan is its owner's, listed under the owner)
				if (RandomNpcDropRule.NeverDrops(netId)) continue;
				DropPermutation.Placed[] table = null;
				try
				{
					table = BossPermutation.InStyle(netId) ? BossPermutation.For(netId) : DropPermutation.InStyle(netId) ? DropPermutation.For(netId) : null;
				}
				catch
				{
				}
				string tier = NpcTiers.BaseTier(netId).ToString();
				string boss = NpcTiers.IsBoss(netId).ToString();
				string spawn = NpcTiers.SpawnTier(netId).ToString();
				DropRows(rows, NpcInternal(netId), NpcName(netId), tier, boss, spawn, false, normal, table);
				DropRows(rows, NpcInternal(netId), NpcName(netId), tier, boss, spawn, true, RandomNpcDropRule.Plan(netId, expert: true, lucky: true), table);
			}
			return (DropHeader, rows);
		}

		private static (string, List<string>) Bags(Dictionary<int, List<PlannedDrop>> bagPlans)
		{
			List<string> rows = new();
			foreach ((int bag, List<PlannedDrop> drops) in bagPlans)
			{
				DropRows(rows, Internal(bag), Name(bag), LootSnapshot.BagTier(bag).ToString(), ItemID.Sets.BossBag[bag].ToString(), "", false, drops, null);
			}
			return (DropHeader, rows);
		}

		// ---- shops.tsv (slot kind: roll, unchanged, or an added slot selling a shop-only item)
		private static (string, List<string>) Shops()
		{
			EconomyConfig cfg = EconomyConfig.Instance;
			var shops = ShopTests.VanillaShops();
			var shopOnly = ShopTests.VanillaShopOnly();
			var permutation = ShopTests.ShopOnlyPlan();
			RecipePlan recipes = ShopTests.Recipes;
			List<string> rows = new();
			foreach (var shop in shops)
			{
				int npc = shop.Shop.NpcType;
				for (int tier = 0; tier < Tiers.Count; tier++)
				{
					ShopSlot[] slots = ShopTests.Plan(permutation, shop, tier, cfg.NoDuplicatesInShop);
					for (int i = 0; i < slots.Length; i++)
					{
						if (i >= shop.Items.Count)
						{
							// An added slot: a shop-only item of this world, sold from its unlock tier
							int added = slots[i].Replacement;
							ItemInfo ainfo = ItemPool.Info[added];
							rows.Add(Row(NpcInternal(npc), NpcName(npc), shop.Shop.FullName, tier, i, "", "", "", "", "from tier " + ShopPermutation.UnlockTier(added), "added slot",
								Internal(added), Name(added), ainfo.Category, ainfo.Tier, ainfo.TrueTier, slots[i].Price, "", true, ShopTests.KeptBy(added, permutation, recipes)));
							continue;
						}
						Item original = shop.Items[i];
						bool replaced = cfg.RandomizeShops && slots[i].Replaced;
						int replacement = replaced ? slots[i].Replacement : original.type;
						ItemInfo? info = ItemPool.IsValid(original.type) ? ItemPool.Info[original.type] : null;
						ItemInfo? rinfo = ItemPool.IsValid(replacement) ? ItemPool.Info[replacement] : info;
						bool only = shopOnly.Contains(original.type);
						rows.Add(Row(NpcInternal(npc), NpcName(npc), shop.Shop.FullName, tier, i, Internal(original.type), Name(original.type), info?.Category, info?.Tier,
							string.Join(" & ", shop.Conditions[i].Select(c => c.Description?.Value ?? c.ToString())),
							!replaced ? "unchanged" : "roll",
							replaced ? Internal(replacement) : "(unchanged)", Name(replacement), rinfo?.Category, rinfo?.Tier, rinfo?.TrueTier,
							replaced ? slots[i].Price : original.GetStoreValue(), original.GetStoreValue(), only, only ? ShopTests.KeptBy(original.type, permutation, recipes) : ""));
					}
				}
			}
			return ("npc\tnpc name\tshop\tunlocked tier\tslot\toriginal\toriginal name\toriginal category\toriginal tier\tconditions\tslot kind\treplacement\treplacement name\t" +
				"replacement category\treplacement tier\treplacement true tier\tprice\tvanilla price\tshop-only\tkept by", rows);
		}

		// ---- chests.tsv: no per-chest-type planner exists (chest slots are rolled per position)
		private static (string, List<string>) Chests()
		{
			List<string> rows = new();
			for (int tier = 0; tier < Tiers.Count; tier++)
			{
				rows.Add(Row("chest tier", tier, "cap: highest item tier a natural chest of that tier gives", ChestSystem.ChestCap(tier)));
			}
			rows.Add(Row("note", "", "per-position chest contents are rolled by ChestSystem.RollChestSlot at world generation; no planner API to export", ""));
			rows.Add(Row("chests randomized", "", "ChestSystem.ChestsRandomized", ChestSystem.ChestsRandomized));
			return ("kind\tvalue\tdescription\tresult", rows);
		}

		// ---- worldsources.tsv: best effort (tree shake, Extractinator and Angler rolls use a runtime Random: not exported)
		private static (string, List<string>) WorldSourceRows()
		{
			List<string> rows = new();
			foreach (int herb in Findability.HerbPlants)
			{
				try
				{
					rows.Add(Row("herb", ItemRef(herb), string.Join("; ", WorldSources.HerbTable(herb).Select(ItemRef)), "fixed replacements (HerbVariety), before progression and kept chance"));
				}
				catch (Exception e)
				{
					rows.Add(Row("herb", ItemRef(herb), "", "error: " + e.Message));
				}
			}
			for (int tier = 0; tier < Tiers.Count; tier++)
			{
				rows.Add(Row("pot guaranteed items", tier, string.Join("; ", GuaranteedSources.PotItems(tier).Select(ItemRef)), "GuaranteedSources.PotItems at this unlocked tier"));
			}
			for (int ore = 1; ore < ItemID.Count; ore++)
			{
				if (!OreMap.IsOre(ore)) continue;
				rows.Add(Row("ore", ItemRef(ore), ItemRef(OreMap.Get(ore)), $"OreMap: pair {OreMap.PairOf(ore)}, present {OreMap.IsPresent(ore)}"));
			}
			rows.Add(Row("note", "", "tree shake, Extractinator, Angler, fishing and pot rolls use runtime random numbers: not exported", ""));
			return ("kind\tsource\tcontent\tnote", rows);
		}

		// ---- findability.tsv
		private static (string, List<string>) FindabilityRows(RecipePlan plan, Dictionary<int, List<PlannedDrop>> npcPlans, Dictionary<int, List<PlannedDrop>> bagPlans)
		{
			Dictionary<int, List<string>> drops = new(), bags = new();
			foreach ((int netId, List<PlannedDrop> list) in npcPlans)
			{
				foreach (PlannedDrop d in list)
				{
					if (d.Item <= 0 || d.Guaranteed) continue;
					if (!drops.TryGetValue(d.Item, out List<string> l)) drops[d.Item] = l = new List<string>();
					l.Add(NpcInternal(netId));
				}
			}
			foreach ((int bag, List<PlannedDrop> list) in bagPlans)
			{
				foreach (PlannedDrop d in list)
				{
					if (d.Item <= 0) continue;
					if (!bags.TryGetValue(d.Item, out List<string> l)) bags[d.Item] = l = new List<string>();
					l.Add(Internal(bag));
				}
			}
			var shops = ShopTests.VanillaShops();
			var permutation = ShopTests.ShopOnlyPlan();
			Dictionary<int, int> shopTier = new();
			bool randomized = EconomyConfig.Instance.RandomizeShops;
			foreach (var shop in shops)
			{
				for (int tier = 0; tier < Tiers.Count; tier++)
				{
					ShopSlot[] slots = ShopTests.Plan(permutation, shop, tier, EconomyConfig.Instance.NoDuplicatesInShop);
					for (int i = 0; i < slots.Length; i++)
					{
						int item = i >= shop.Items.Count ? slots[i].Replacement : randomized && slots[i].Replaced ? slots[i].Replacement : shop.Items[i].type;
						if (item > 0 && !shopTier.ContainsKey(item)) shopTier[item] = tier;
					}
				}
			}
			Dictionary<int, List<int>> recipesOf = new();
			if (plan != null)
			{
				int count = Math.Min(RecipeSystem.RecipeCount, Recipe.numRecipes);
				for (int i = 0; i < count; i++)
				{
					if ((plan.Twin != null && plan.Twin[i]) || (plan.LoopOff != null && plan.LoopOff[i])) continue;
					if (RecipeSystem.IsShell(i) && !RecipePlanner.ShellActive(plan, i)) continue;
					int result = plan.Results[i].type;
					if (result <= 0) continue;
					if (!recipesOf.TryGetValue(result, out List<int> l)) recipesOf[result] = l = new List<int>();
					l.Add(i);
				}
			}
			HashSet<int> herbs = new();
			foreach (int herb in Findability.HerbPlants)
			{
				try
				{
					herbs.UnionWith(WorldSources.HerbTable(herb));
				}
				catch
				{
				}
			}
			string Few(List<string> l) => l == null ? "" : $"{l.Count}: " + string.Join(", ", l.Distinct().Take(10));
			List<string> rows = new();
			for (int type = 1; type < Math.Min(ItemLoader.ItemCount, ItemPool.Info.Length); type++)
			{
				
				string hosts = "";
				try
				{
					hosts = string.Join(", ", GuaranteedSources.HostsOf(type).Take(10).Select(NpcInternal));
				}
				catch
				{
				}
				string recipe = recipesOf.TryGetValue(type, out List<int> rl) ? $"{rl.Count}: " + string.Join(", ", rl.Take(10)) : "";
				rows.Add(Row(type, Internal(type), Name(type), ModOf(type), ItemPool.Info[type].Tier, Show(Findability.Tier(type)), Show(Findability.BaseTier(type)),
					Few(drops.GetValueOrDefault(type)), Few(bags.GetValueOrDefault(type)), shopTier.TryGetValue(type, out int st) ? st.ToString() : "", recipe,
					herbs.Contains(type) ? "herb" : "", OreMap.IsOre(type) ? "ore" : "", hosts, plan != null && type < plan.Availability.Length ? Show(plan.Availability[type]) : ""));
			}
			return ("id\tinternal name\tname\tmod\ttier\tfindability tier\tfindability tier without guarantees\tenemy drop sources (count: first 10)\tbag sources (count: first 10)\t" +
				"first shop tier\tplanned recipes (count: indexes)\therb\tore\tguaranteed hosts\trecipe availability tier", rows);
		}

		private const string Readme =
			"Ultimate Randomizer world export (test WorldExport). Tab separated, one header line, overwritten at each run. Tier 0 = PreBoss ... 9 = PostMoonLord, 'never' = not obtainable.\n" +
			"items.tsv: one row per item (columns of ItemTiers.tsv + mod). tier = planned tier, true tier = tier once sources are considered, findability tier = first tier found in this world (incl. guarantees), planned recipe tier = recipe availability.\n" +
			"recipes.tsv: one row per recipe index. vanilla/planned ingredients 'internal xN [tier of item, availability tier, value]'. flags: randomized (live recipe differs from vanilla), rebuilt (ingredients rebuilt by the planner), swapped + partner (result swap), shell (recipe of an item no recipe makes), upgrade shell, protected (not touchable), house, mining chain, switched off (twin or loopoff), repaired, value short. planned doable tier = max availability of planned ingredients and stations (off = switched off). ingredient tier T = tier the ingredients were rebuilt for (-1 none).\n" +
			"drops.tsv: for each NPC with a plan (lucky final plan, no progression lock), normal and expert. kind: permuted (content moved from origin npc/index), kept, replaced (original item swapped), surprise, extra (random extra drop), guaranteed (GuaranteedSources). lock gap = tiers progression would lock it (0 in the lucky plan). jackpot gap = runtime only (0).\n" +
			"bags.tsv: same columns for every bag/crate plan (BagPlan.Plan, lucky).\n" +
			"shops.tsv: every vanilla shop entry at simulated unlocked tiers 0..9 with its replacement (slot kind: roll, guaranteed shop-only item, or unchanged) and price.\n" +
			"chests.tsv: only the tier caps and a note: no chest planner API exists (contents are rolled per position).\n" +
			"worldsources.tsv: best effort: herb replacement tables, guaranteed pot items per tier, ore map. Tree shake, Extractinator, Angler, fishing use runtime randomness and are not exported.\n" +
			"findability.tsv: per item, the tier, findability tier (with and without guarantees), enemy drop and bag sources (count and first 10), first shop tier, planned recipes, herb/ore flags, guaranteed hosts and recipe availability tier.\n";
	}
}

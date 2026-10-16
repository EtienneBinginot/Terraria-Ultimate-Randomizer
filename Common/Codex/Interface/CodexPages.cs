using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.UI;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;
using UltimateRandomizer.Common.Journal;

namespace UltimateRandomizer.Common.Codex.Interface
{
	// The content of each tab and page. Only discovered things are listed; planned drops not found yet are "?".
	public partial class CodexUIState
	{
		private const float Icon = 44;
		private const float Portrait = 76;
		private const int MaxRecipesShown = 40;
		private const int MaxTreeDepth = 6;
		private const int MaxTreeRows = 400;

		private int categoryFilter = -1;
		private int tierFilter = -1;

		private void Build(CodexPage page)
		{
			if (!Ready)
			{
				Text(Language.GetTextValue("Mods.UltimateRandomizer.Debug.NeedWorld"));
				return;
			}
			switch (page.Kind)
			{
				case CodexPageKind.Enemies: BuildEnemies(); break;
				case CodexPageKind.Items: BuildItems(); break;
				case CodexPageKind.Crafts: BuildCraftPicker(); break;
				case CodexPageKind.Bags: BuildBags(); break;
				case CodexPageKind.Biomes: BuildBiomes(); break;
				case CodexPageKind.Npc: BuildNpc(page.Id); break;
				case CodexPageKind.Item: BuildItem(page.Id); break;
				case CodexPageKind.Tree: BuildTree(page.Id); break;
			}
		}

		// ---- Helpers ----

		private void Add(UIElement element) => list.Add(element);

		private void Text(string text, Color? color = null) => Add(new CodexRow(30).WithLabel(text, 0.9f, color ?? Color.White));

		private void Section(string text) => Add(new CodexSection(text));

		private void Grid(IEnumerable<UIElement> cells, float cellWidth, float cellHeight, float indent = 0)
		{
			foreach (UIElement row in CodexGrid.Rows(cells, cellWidth, cellHeight, ListWidth, indent)) Add(row);
		}

		private void Empty(bool nothingAtAll) => Text(Language.GetTextValue(nothingAtAll ? "Mods.UltimateRandomizer.Codex.Empty" : "Mods.UltimateRandomizer.Codex.NoMatch"), CodexDraw.Muted);

		private static string Tier(int tier) => Language.GetTextValue("Mods.UltimateRandomizer.Codex.Tier", tier);

		private static int NpcTier(int netId) => ContentSamples.NpcsByNetId.TryGetValue(netId, out NPC sample) ? NpcTiers.BaseTier(sample.type) : 0;

		private static string SourceName(CodexSource kind) => Language.GetTextValue("Mods.UltimateRandomizer.Codex.Source." + kind);

		private static string CategoryName(int category) => Language.GetTextValue("Mods.UltimateRandomizer.Codex.Category." + (category < 0 ? "All" : ((ItemCategory)category).ToString()));

		private List<int> SortedItems(string key, Func<IEnumerable<int>> items) => Cached(key, () => items().Distinct().Where(t => t > 0 && t < ItemLoader.ItemCount)
			.OrderBy(CodexDraw.ItemName, StringComparer.CurrentCultureIgnoreCase).ToList());

		// ---- Enemies ----

		private void BuildEnemies()
		{
			List<int> killed = Cached("killed", () => CodexSystem.KilledNpcs().Distinct().OrderBy(CodexDraw.NpcName, StringComparer.CurrentCultureIgnoreCase).ToList());
			List<int> shown = killed.Where(id => Matches(CodexDraw.NpcName(id))).ToList();
			Text(Language.GetTextValue("Mods.UltimateRandomizer.Codex.EnemiesCount", killed.Count), CodexDraw.Gold);
			if (shown.Count == 0)
			{
				Empty(killed.Count == 0);
				return;
			}
			Grid(shown.Select(id => (UIElement)new CodexNpcPortrait(id, Portrait, Portrait, false, CodexSystem.KillCount(id))), Portrait, Portrait);
		}

		private void BuildNpc(int netId)
		{
			string name = CodexDraw.NpcName(netId);
			CodexRow header = new(160);
			header.Add(new CodexNpcPortrait(netId, 280, 150, true, 0, false), 16);
			UIElement info = new();
			info.Width.Set(-header.Used - 10, 1f);
			info.Height.Set(150, 0);
			float top = 4;
			void Line(string text, float scale, Color color)
			{
				CodexLabel label = new(text, scale, color);
				label.Top.Set(top, 0);
				info.Append(label);
				top += 30 * scale + 4;
			}
			Line(name, 1.3f, CodexDraw.Gold);
			Line(Language.GetTextValue("Mods.UltimateRandomizer.Codex.Kills", CodexSystem.KillCount(netId)), 0.9f, Color.White);
			Line(Tier(NpcTier(netId)), 0.9f, Color.White);
			if (ContentSamples.NpcsByNetId.TryGetValue(netId, out NPC sample)) Line(Language.GetTextValue("Mods.UltimateRandomizer.Codex.Life", sample.lifeMax), 0.9f, Color.White);
			header.Add(info);
			Add(header);

			List<string> biomes = CodexSystem.BiomesWhereSeen(netId);
			if (biomes.Count > 0)
			{
				CodexRow seen = new(40);
				seen.Add(new CodexLabel(Language.GetTextValue("Mods.UltimateRandomizer.Codex.SeenIn"), 0.85f, CodexDraw.Muted), 8);
				foreach (string biome in biomes.OrderBy(BiomeSlots.BiomeDisplayName))
				{
					seen.Add(new CodexBiomeIcon(biome), 2);
					seen.Add(new CodexLabel(BiomeSlots.BiomeDisplayName(biome), 0.8f), 12);
				}
				Add(seen);
			}

			List<(int item, int count)> drops = CodexSystem.DropsOf(netId);
			Section(Language.GetTextValue("Mods.UltimateRandomizer.Codex.DropsFound"));
			if (drops.Count == 0) Text(Language.GetTextValue("Mods.UltimateRandomizer.Codex.NoDrops"), CodexDraw.Muted);
			else Grid(drops.Select(d => (UIElement)new CodexItemIcon(d.item, d.count, Icon)), Icon, Icon);

			// Only how many: the planned items stay secret until they drop
			HashSet<int> found = drops.Select(d => d.item).ToHashSet();
			int remaining = Cached("plan" + netId, () => new List<int> { RandomNpcDropRule.Plan(netId, Main.expertMode).Select(p => p.Item).Where(i => i > ItemID.None).Distinct().Count(i => !found.Contains(i)) })[0];
			if (remaining > 0)
			{
				Section(Language.GetTextValue("Mods.UltimateRandomizer.Codex.ToFind", remaining));
				string hover = Language.GetTextValue("Mods.UltimateRandomizer.Codex.UnknownDrop");
				Grid(Enumerable.Range(0, Math.Min(remaining, 60)).Select(_ => (UIElement)new CodexItemIcon(0, 0, Icon) { HoverText = hover }), Icon, Icon);
			}
		}

		// ---- Items ----

		private void BuildItems()
		{
			List<int> seen = SortedItems("seen", CodexSystem.ItemsSeen);
			CodexRow filters = new(40);
			filters.Add(FilterButton(Language.GetTextValue("Mods.UltimateRandomizer.Codex.FilterCategory", CategoryName(categoryFilter)), 300, step =>
				categoryFilter = Cycle(categoryFilter, step, ItemCategories.Count)));
			filters.Add(FilterButton(Language.GetTextValue("Mods.UltimateRandomizer.Codex.FilterTier", tierFilter < 0 ? Language.GetTextValue("Mods.UltimateRandomizer.Codex.Category.All") : tierFilter.ToString()), 180, step =>
				tierFilter = Cycle(tierFilter, step, Tiers.Count)));
			List<int> shown = seen.Where(t => Matches(CodexDraw.ItemName(t)) && PassesFilters(t)).ToList();
			filters.Add(new CodexLabel(Language.GetTextValue("Mods.UltimateRandomizer.Codex.ItemsCount", shown.Count, seen.Count), 0.85f, CodexDraw.Gold));
			Add(filters);
			if (shown.Count == 0)
			{
				Empty(seen.Count == 0);
				return;
			}
			Grid(shown.Select(t => (UIElement)new CodexItemIcon(t, 0, Icon)), Icon, Icon);
		}

		private bool PassesFilters(int type)
		{
			if (categoryFilter < 0 && tierFilter < 0) return true;
			if (!ItemPool.IsValid(type)) return false;
			ItemInfo info = ItemPool.Info[type];
			return (categoryFilter < 0 || (int)info.Category == categoryFilter) && (tierFilter < 0 || info.Tier == tierFilter);
		}

		// -1 (all), 0 .. count-1, then back to all
		private static int Cycle(int value, int step, int count) => (value + 1 + step + count + 1) % (count + 1) - 1;

		// Left click: next value, right click: previous
		private CodexButton FilterButton(string text, float width, Action<int> change)
		{
			CodexButton button = new(text, width, () =>
			{
				change(1);
				MarkDirty(false);
			}, 0.85f);
			button.OnRightClick += (_, _) =>
			{
				change(-1);
				MarkDirty(false);
			};
			return button;
		}

		private void BuildItem(int type)
		{
			if (type <= 0 || type >= ItemLoader.ItemCount)
			{
				Empty(true);
				return;
			}
			ContentSamples.ItemsByType.TryGetValue(type, out Item sample);
			CodexRow header = new(72);
			header.Add(new CodexItemIcon(type, 0, 64, () => { }), 12);
			UIElement info = new();
			info.Width.Set(420, 0);
			info.Height.Set(66, 0);
			CodexLabel name = new(CodexDraw.ItemName(type), 1.2f, sample != null && sample.rare != ItemRarityID.White ? ItemRarity.GetColor(sample.rare) : Color.White);
			info.Append(name);
			string details = ItemPool.IsValid(type) ? $"{CategoryName((int)ItemPool.Info[type].Category)}  -  {Tier(ItemPool.Info[type].Tier)}" : "";
			CodexLabel sub = new(details, 0.85f, CodexDraw.Muted);
			sub.Top.Set(36, 0);
			info.Append(sub);
			header.Add(info, 10);
			List<int> giving = CodexSystem.RecipesGiving(type);
			if (giving.Count > 0) header.Add(new CodexButton(Language.GetTextValue("Mods.UltimateRandomizer.Codex.OpenTree"), 170, () => Navigate(CodexPage.TreePage(type))));
			Add(header);

			bool any = false;
			if (giving.Count > 0)
			{
				any = true;
				Section(Language.GetTextValue("Mods.UltimateRandomizer.Codex.CraftedWith"));
				Recipes(giving);
			}
			List<int> using_ = CodexSystem.RecipesUsing(type);
			if (using_.Count > 0)
			{
				any = true;
				Section(Language.GetTextValue("Mods.UltimateRandomizer.Codex.UsedIn"));
				Recipes(using_);
			}
			List<(int npc, int count)> droppedBy = CodexSystem.NpcsDropping(type);
			if (droppedBy.Count > 0)
			{
				any = true;
				Section(Language.GetTextValue("Mods.UltimateRandomizer.Codex.DroppedBy"));
				Grid(droppedBy.OrderByDescending(d => d.count).Select(d => (UIElement)new CodexNpcPortrait(d.npc, 64, 64, false, d.count)), 64, 64);
			}
			List<(CodexSource kind, int from, int count)> sources = CodexSystem.SourcesOf(type);
			if (sources.Count > 0)
			{
				any = true;
				Section(Language.GetTextValue("Mods.UltimateRandomizer.Codex.OtherSources"));
				foreach ((CodexSource kind, int from, int count) in sources.OrderBy(s => s.kind).ThenByDescending(s => s.count))
				{
					CodexRow row = new(40) { Highlight = true };
					row.Add(new CodexLabel(SourceName(kind), 0.9f), 10);
					if ((kind == CodexSource.Bag || kind == CodexSource.Shimmer) && from > 0)
					{
						row.Add(new CodexItemIcon(from, 0, 36), 6);
						row.Add(new CodexLabel(CodexDraw.ItemName(from), 0.85f), 10);
					}
					row.Add(new CodexLabel("x" + CodexDraw.Count(count), 0.9f, CodexDraw.Gold));
					Add(row);
				}
			}
			if (CodexSystem.HasOpenedBag(type))
			{
				any = true;
				Section(Language.GetTextValue("Mods.UltimateRandomizer.Codex.BagContents"));
				BagContents(type);
			}
			if (!any) Text(Language.GetTextValue("Mods.UltimateRandomizer.Codex.NoSource"), CodexDraw.Muted);
		}

		private void Recipes(List<int> recipes)
		{
			foreach (int recipe in recipes.Take(MaxRecipesShown)) Add(new CodexRecipeRow(recipe));
			if (recipes.Count > MaxRecipesShown) Text(Language.GetTextValue("Mods.UltimateRandomizer.Codex.More", recipes.Count - MaxRecipesShown), CodexDraw.Muted);
		}

		// ---- Crafts tree ----

		private int treeRoot;
		private readonly HashSet<string> expanded = new();

		private void ResetTree(int root)
		{
			if (treeRoot == root && expanded.Count > 0) return;
			treeRoot = root;
			expanded.Clear();
			expanded.Add(root.ToString());
		}

		private void BuildCraftPicker()
		{
			List<int> craftable = SortedItems("craftable", () =>
			{
				List<int> results = new();
				int count = Math.Min(Recipe.numRecipes, RecipeSystem.RecipeCount);
				for (int i = 0; i < count; i++)
				{
					if (!Main.recipe[i].Disabled && CodexSystem.IsRecipeDiscovered(i)) results.Add(Main.recipe[i].createItem.type);
				}
				return results;
			});
			Text(Language.GetTextValue("Mods.UltimateRandomizer.Codex.PickItem"), CodexDraw.Gold);
			List<int> shown = craftable.Where(t => Matches(CodexDraw.ItemName(t))).ToList();
			if (shown.Count == 0)
			{
				Empty(craftable.Count == 0);
				return;
			}
			Grid(shown.Select(t => (UIElement)new CodexItemIcon(t, 0, Icon, () => Navigate(CodexPage.TreePage(t)))), Icon, Icon);
		}

		private void BuildTree(int root)
		{
			if (root <= 0 || root >= ItemLoader.ItemCount)
			{
				Empty(true);
				return;
			}
			if (treeRoot != root) ResetTree(root);
			CodexRow header = new(40);
			header.Add(new CodexLabel(Language.GetTextValue("Mods.UltimateRandomizer.Codex.TreeOf", CodexDraw.ItemName(root)), 1f, CodexDraw.Gold), 16);
			header.Add(new CodexButton(Language.GetTextValue("Mods.UltimateRandomizer.Codex.ItemPage"), 150, () => Navigate(CodexPage.ItemPage(root))));
			Add(header);
			int rows = 0;
			TreeNode(root, 1, root.ToString(), new HashSet<int>(), 1, ref rows);
			if (rows >= MaxTreeRows) Text(Language.GetTextValue("Mods.UltimateRandomizer.Codex.TreeTooBig"), CodexDraw.Muted);
		}

		// One item of the tree; expanded, its discovered recipes and their ingredients below it. An item already
		// above in its branch is a cycle and is not expanded again.
		private void TreeNode(int item, int depth, string path, HashSet<int> ancestors, int amount, ref int rows)
		{
			if (rows++ >= MaxTreeRows) return;
			float indent = (depth - 1) * 26;
			List<int> recipes = CodexSystem.RecipesGiving(item);
			bool cycle = ancestors.Contains(item);
			bool canExpand = recipes.Count > 0 && !cycle && depth <= MaxTreeDepth;
			bool open = canExpand && expanded.Contains(path);
			CodexRow row = new(42, indent) { Highlight = true };
			if (canExpand)
			{
				row.Add(new CodexButton(open ? "-" : "+", 30, () =>
				{
					if (!expanded.Remove(path)) expanded.Add(path);
					MarkDirty(true);
				}), 6);
			}
			else
			{
				row.Space(36);
			}
			row.Add(new CodexItemIcon(item, amount, 38), 8);
			row.Add(new CodexLabel(CodexDraw.ItemName(item), 0.9f), 12);
			if (cycle) row.Add(new CodexLabel(Language.GetTextValue("Mods.UltimateRandomizer.Codex.Cycle"), 0.8f, CodexDraw.Muted));
			else if (recipes.Count == 0) LeafSources(row, item);
			else if (!canExpand) row.Add(new CodexLabel(Language.GetTextValue("Mods.UltimateRandomizer.Codex.TooDeep"), 0.8f, CodexDraw.Muted));
			Add(row);
			if (!open) return;

			ancestors.Add(item);
			foreach (int recipe in recipes)
			{
				if (rows >= MaxTreeRows) break;
				Add(new CodexRecipeRow(recipe, indent + 36, 32));
				rows++;
				foreach (Item ingredient in Main.recipe[recipe].requiredItem)
				{
					if (ingredient == null || ingredient.IsAir) continue;
					TreeNode(ingredient.type, depth + 1, path + "/" + recipe + ":" + ingredient.type, ancestors, ingredient.stack, ref rows);
				}
			}
			ancestors.Remove(item);
		}

		// Where a raw ingredient was found: the enemies dropping the most, then the other kinds of sources
		private static void LeafSources(CodexRow row, int item)
		{
			List<(int npc, int count)> droppedBy = CodexSystem.NpcsDropping(item);
			List<(CodexSource kind, int from, int count)> sources = CodexSystem.SourcesOf(item);
			if (droppedBy.Count == 0 && sources.Count == 0)
			{
				row.Add(new CodexLabel(Language.GetTextValue("Mods.UltimateRandomizer.Codex.NoSource"), 0.8f, CodexDraw.Muted));
				return;
			}
			foreach ((int npc, int count) in droppedBy.OrderByDescending(d => d.count).Take(4)) row.Add(new CodexNpcPortrait(npc, 38, 38, false, count), 4);
			row.Space(6);
			foreach (IGrouping<CodexSource, (CodexSource kind, int from, int count)> group in sources.GroupBy(s => s.kind).OrderBy(g => g.Key))
			{
				row.Add(new CodexLabel($"{SourceName(group.Key)} x{CodexDraw.Count(group.Sum(s => s.count))}", 0.8f, CodexDraw.Muted), 10);
			}
		}

		// ---- Bags ----

		private void BuildBags()
		{
			List<int> bags = SortedItems("bags", CodexSystem.OpenedBags);
			List<int> shown = bags.Where(b => Matches(CodexDraw.ItemName(b))).ToList();
			if (shown.Count == 0)
			{
				Empty(bags.Count == 0);
				return;
			}
			foreach (int bag in shown)
			{
				CodexRow header = new(46);
				header.Add(new CodexItemIcon(bag, 0, 42), 10);
				header.Add(new CodexLabel(CodexDraw.ItemName(bag), 1f, CodexDraw.Gold), 12);
				header.Add(new CodexLabel(Tier(LootSnapshot.BagTier(bag)), 0.8f, CodexDraw.Muted));
				Add(header);
				BagContents(bag);
			}
		}

		private void BagContents(int bag)
		{
			List<(int item, int count)> contents = CodexSystem.BagContents(bag);
			if (contents.Count == 0) Text(Language.GetTextValue("Mods.UltimateRandomizer.Codex.NothingReceived"), CodexDraw.Muted);
			else Grid(contents.Select(c => (UIElement)new CodexItemIcon(c.item, c.count, Icon)), Icon, Icon, 20);
		}

		// ---- Biomes ----

		private void BuildBiomes()
		{
			List<string> biomes = Cached("biomes", () => CodexSystem.Data.Sightings.Keys.OrderBy(BiomeSlots.BiomeDisplayName, StringComparer.CurrentCultureIgnoreCase).ToList());
			bool any = false;
			foreach (string biome in biomes)
			{
				string biomeName = BiomeSlots.BiomeDisplayName(biome);
				List<int> npcs = CodexSystem.NpcsSeenIn(biome).Distinct().OrderBy(CodexDraw.NpcName, StringComparer.CurrentCultureIgnoreCase).ToList();
				// The search matches the biome, or else the enemies seen there
				if (!Matches(biomeName)) npcs = npcs.Where(id => Matches(CodexDraw.NpcName(id))).ToList();
				if (npcs.Count == 0) continue;
				any = true;
				CodexRow header = new(44);
				header.Add(new CodexBiomeIcon(biome, 38), 10);
				header.Add(new CodexLabel(biomeName, 1f, CodexDraw.Gold), 12);
				header.Add(new CodexLabel(Language.GetTextValue("Mods.UltimateRandomizer.Codex.SeenCount", npcs.Count), 0.8f, CodexDraw.Muted));
				Add(header);
				Grid(npcs.Select(id => (UIElement)new CodexNpcPortrait(id, 64, 64, false, CodexSystem.KillCount(id))), 64, 64, 20);
			}
			if (!any) Empty(biomes.Count == 0);
		}
	}

	public static class CodexRowExtensions
	{
		public static CodexRow WithLabel(this CodexRow row, string text, float scale, Color color)
		{
			row.Add(new CodexLabel(text, scale, color));
			return row;
		}
	}
}

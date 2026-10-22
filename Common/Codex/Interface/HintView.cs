using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Codex.Interface
{
	// Where a hint leads in the codex: its source once the last level is known (the enemy's page when the enemy is known,
	// the craft tree of a recipe, the bag's page), else the target's own page
	public static class HintNavigation
	{
		public static CodexPage Target(int item) => Target(item, HintFacts.For(item), CodexSystem.ShownHintLevel(item), CodexSystem.KnownNpcs());

		public static CodexPage Target(int item, HintFact fact, int level, ICollection<int> knownNpcs)
		{
			if (fact != null && level >= HintFacts.MaxLevel)
			{
				switch (fact.Kind)
				{
					case HintKind.Creature:
					case HintKind.Boss:
						int key = CodexSystem.RevealKey(fact);
						if (knownNpcs.Contains(key)) return CodexPage.NpcPage(key);
						break;
					case HintKind.Recipe:
						return CodexPage.TreePage(item);
					case HintKind.Bag when fact.Bag > 0:
						return CodexPage.ItemPage(fact.Bag);
				}
			}
			return CodexPage.ItemPage(item);
		}
	}

	// The rows showing what the bought levels of a hint reveal: the Guide's sentences, then the shapes (level 3) or
	// the real thing (level 4). Shared by the Guide's panel and the codex Hints tab.
	public static class HintView
	{
		// Every level up to the bought one
		public static List<UIElement> Rows(HintFact fact, int level, float width, bool inCodex)
		{
			List<UIElement> rows = new();
			if (fact == null || level <= 0) return rows;
			for (int l = 1; l <= Math.Min(level, HintFacts.MaxLevel); l++)
			{
				foreach (string line in Wrap($"{l}. {HintFacts.Text(fact, l)}", 0.85f, width - 24))
				{
					rows.Add(new CodexRow(24, 8).WithLabel(line, 0.85f, l == level ? Color.White : CodexDraw.Muted));
				}
				if (l == 3 && level == 3) rows.Add(Visual(fact, true, inCodex));
				if (l == 4) rows.Add(Visual(fact, false, inCodex));
				// A recipe is only worth something with where its parts come from: the tree as shapes at level 3, then clear
				if (fact.Kind == HintKind.Recipe && (l == 4 || (l == 3 && level == 3))) rows.AddRange(TreeRows(fact.Item, l == 3, width, inCodex));
			}
			return rows;
		}

		// The host, the recipe or the container, as a shape (level 3) or as it is (level 4)
		public static UIElement Visual(HintFact fact, bool silhouette, bool inCodex)
		{
			CodexRow row = new(silhouette || fact.Kind != HintKind.Recipe ? 92 : 50, 20);
			switch (fact.Kind)
			{
				case HintKind.Creature:
				case HintKind.Boss:
				case HintKind.Shop:
					row.Add(new CodexNpcPortrait(fact.Npc, 120, 86, false, 0, clickable: inCodex && !silhouette, silhouette: silhouette, textureOnly: !inCodex));
					if (!silhouette) row.Add(new CodexLabel(HintFacts.NpcName(fact.Npc), 0.9f, CodexDraw.Gold), 8);
					break;
				case HintKind.World when fact.Npc != 0:
					row.Add(new CodexNpcPortrait(fact.Npc, 120, 86, false, 0, clickable: false, silhouette: silhouette, textureOnly: !inCodex));
					break;
				case HintKind.Recipe:
					RecipeRow(row, fact, silhouette);
					break;
				case HintKind.Bag:
					row.Add(Icon(fact.Bag, 0, 64, silhouette));
					if (!silhouette) row.Add(new CodexLabel(HintFacts.ItemName(fact.Bag), 0.9f, CodexDraw.Gold), 8);
					break;
				case HintKind.Chest:
					// Where it is (a plain wooden chest), then what
					row.Add(new CodexItemIcon(ItemID.Chest, 0, 64, () => { }), 10);
					row.Add(Icon(fact.Item, 0, 64, silhouette));
					break;
				case HintKind.Pot:
					row.Add(new CodexTileIcon(TileID.Pots, 64), 10);
					row.Add(Icon(fact.Item, 0, 64, silhouette));
					break;
				default:
					row.Add(Icon(fact.Item, 0, 64, silhouette));
					break;
			}
			return row;
		}

		// Every ingredient and station of the recipe, down to raw sources, indented by depth
		public static List<UIElement> TreeRows(int item, bool silhouette, float width, bool inCodex)
		{
			List<UIElement> rows = new();
			List<HintTreeNode> tree = HintFacts.SourceTree(item);
			if (tree.Count == 0) return rows;
			rows.Add(new CodexSection(CodexText.Get("Hint.Tree.Title")));
			// Below a part with a hint of its own, nothing is shown until that hint is known
			int hiddenBelow = int.MaxValue;
			foreach (HintTreeNode node in tree)
			{
				if (node.Depth > hiddenBelow) continue;
				hiddenBelow = int.MaxValue;
				bool hidden = HintFacts.HiddenInTree(node, item);
				if (hidden) hiddenBelow = node.Depth;
				CodexRow row = new(40, 8 + (node.Depth - 1) * 22);
				if (node.Item > 0 || silhouette) row.Add(Icon(node.Item, node.Stack, 34, silhouette), 6);
				else row.Add(new CodexTileIcon(node.Tile, 34), 6);
				int ore = node.Ore;
				if (node.Leaf == HintTreeLeaf.Mined && ore != node.Item) row.Add(Icon(ore, 0, 28, silhouette), 6);
				HintFact fact = hidden ? null : node.Fact;
				if (node.Leaf == HintTreeLeaf.Fact && fact != null && fact.Npc != 0 && fact.Kind is HintKind.Creature or HintKind.Boss or HintKind.Shop)
					row.Add(new CodexNpcPortrait(fact.Npc, 36, 36, false, 0, clickable: false, silhouette: silhouette, textureOnly: !inCodex), 6);
				else if (node.Leaf == HintTreeLeaf.Fact && fact != null && fact.Kind == HintKind.Bag) row.Add(Icon(fact.Bag, 0, 28, silhouette), 6);
				string source = hidden ? CodexText.Get("Hint.Tree.OwnHint") : HintFacts.TreeSource(node);
				string text = silhouette ? "?" : HintFacts.TreeName(node) + (node.Station ? " " + CodexText.Get("Hint.Tree.Station") : "") + ": " + source;
				Color color = hidden ? CodexDraw.Gold : node.Leaf == HintTreeLeaf.Repeat ? CodexDraw.Muted : Color.White;
				row.Add(new CodexLabel(Fit(text, 0.75f, width - row.Used - 16), 0.75f, color) { HoverText = silhouette ? null : text });
				rows.Add(row);
			}
			if (HintFacts.TreeTruncated(item)) rows.Add(new CodexRow(26, 8).WithLabel(CodexText.Get("Hint.Tree.More"), 0.75f, CodexDraw.Muted));
			return rows;
		}

		// Shortened with "..." to fit a width (the full text shows on hover)
		private static string Fit(string text, float scale, float width)
		{
			if (CodexDraw.TextWidth(text, scale) <= width) return text;
			while (text.Length > 4 && CodexDraw.TextWidth(text + "...", scale) > width) text = text.Substring(0, text.Length - 1);
			return text + "...";
		}

		private static CodexItemIcon Icon(int type, int count, float size, bool silhouette) =>
			new(type, count, size) { Silhouette = silhouette };

		// Ingredients with their counts, the result ("?" as a shape) and the stations
		private static void RecipeRow(CodexRow row, HintFact fact, bool silhouette)
		{
			float size = 40;
			foreach ((int type, int stack) in fact.Ingredients)
			{
				if (type > 0) row.Add(Icon(type, stack, size, silhouette), 4);
			}
			row.Space(4);
			row.Add(new CodexLabel("»", 1.2f, CodexDraw.Gold), 6);
			row.Add(silhouette ? new CodexItemIcon(0, 0, size) { HoverText = CodexText.Get("Hint.Unknown") } : Icon(fact.Item, RecipeStack(fact), size, false), 6);
			row.Space(10);
			row.Add(new CodexLabel(CodexText.Get("At"), 0.8f, CodexDraw.Muted), 4);
			if (fact.Tiles.Length == 0) row.Add(new CodexLabel(CodexText.Get("Hint.NoStation"), 0.8f), 6);
			foreach (int tile in fact.Tiles)
			{
				int item = HintFacts.StationItem(tile);
				if (item > 0) row.Add(Icon(item, 0, size - 6, silhouette), 4);
				else row.Add(new CodexLabel(silhouette ? "?" : HintFacts.StationName(tile), 0.8f), 6);
			}
		}

		private static int RecipeStack(HintFact fact)
		{
			RecipePlan plan = RecipePlanner.Get();
			return fact.Recipe >= 0 && fact.Recipe < plan.Results.Length ? plan.Results[fact.Recipe].stack : 1;
		}

		// Words wrapped to a width (the codex labels are single lines)
		public static List<string> Wrap(string text, float scale, float width)
		{
			List<string> lines = new();
			foreach (string paragraph in (text ?? "").Split('\n'))
			{
				string line = "";
				foreach (string word in paragraph.Split(' '))
				{
					string next = line.Length == 0 ? word : line + " " + word;
					if (line.Length > 0 && CodexDraw.TextWidth(next, scale) > width)
					{
						lines.Add(line);
						line = word;
					}
					else
					{
						line = next;
					}
				}
				lines.Add(line);
			}
			return lines;
		}
	}

	// The first style of a 2x2 tile (a pot), drawn from the tile sheet: pots have no item
	public class CodexTileIcon : UIElement
	{
		private readonly int tile;

		public CodexTileIcon(int tile, float size)
		{
			this.tile = tile;
			Width.Set(size, 0);
			Height.Set(size, 0);
		}

		protected override void DrawSelf(SpriteBatch spriteBatch)
		{
			CalculatedStyle d = GetDimensions();
			Rectangle r = d.ToRectangle();
			spriteBatch.Draw(TextureAssets.InventoryBack.Value, r, Color.White * 0.85f);
			if (tile < 0 || tile >= TextureAssets.Tile.Length) return;
			Main.instance.LoadTiles(tile);
			Texture2D texture = TextureAssets.Tile[tile].Value;
			// 16 pixel parts with 2 pixel gaps on the sheet
			float scale = d.Width * 0.6f / 32f;
			Vector2 origin = d.Center() - new Vector2(16 * scale);
			for (int x = 0; x < 2; x++)
			{
				for (int y = 0; y < 2; y++)
				{
					spriteBatch.Draw(texture, origin + new Vector2(x, y) * 16 * scale, new Rectangle(x * 18, y * 18, 16, 16), Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
				}
			}
		}
	}
}

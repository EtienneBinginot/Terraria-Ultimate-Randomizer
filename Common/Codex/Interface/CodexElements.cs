using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.Bestiary;
using Terraria.GameContent.UI;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Map;
using Terraria.ModLoader;
using Terraria.UI;

namespace UltimateRandomizer.Common.Codex.Interface
{
	// The tooltip of the hovered element, drawn once after the whole codex so that no later element covers it
	public static class CodexHover
	{
		public static Item Item;
		public static string Text;

		public static void Clear()
		{
			Item = null;
			Text = null;
		}

		public static void Draw()
		{
			if (Item != null)
			{
				// Same as a chat item tag: the vanilla tooltip of the item
				Main.HoverItem = Item.Clone();
				Main.instance.MouseText(Item.Name, Item.rare, 0, -1, -1, -1, -1, 0);
			}
			else if (!string.IsNullOrEmpty(Text))
			{
				Main.instance.MouseText(Text, 0, 0, -1, -1, -1, -1, 0);
			}
			Clear();
		}
	}

	public static class CodexDraw
	{
		public static readonly Color Gold = new(255, 217, 102);
		public static readonly Color Muted = new(170, 170, 185);

		public static void Fill(SpriteBatch spriteBatch, Rectangle rect, Color color) => spriteBatch.Draw(TextureAssets.MagicPixel.Value, rect, color);

		public static void Border(SpriteBatch spriteBatch, Rectangle r, Color color, int thickness = 2)
		{
			Fill(spriteBatch, new Rectangle(r.X, r.Y, r.Width, thickness), color);
			Fill(spriteBatch, new Rectangle(r.X, r.Bottom - thickness, r.Width, thickness), color);
			Fill(spriteBatch, new Rectangle(r.X, r.Y, thickness, r.Height), color);
			Fill(spriteBatch, new Rectangle(r.Right - thickness, r.Y, thickness, r.Height), color);
		}

		public static float TextWidth(string text, float scale) => string.IsNullOrEmpty(text) ? 0 : FontAssets.MouseText.Value.MeasureString(text).X * scale;

		// 12345 -> 12.3k: counts stay readable on a small icon
		public static string Count(int count) => count >= 100000 ? $"{count / 1000}k" : count >= 10000 ? $"{count / 1000f:0.#}k" : count.ToString();

		public static string NpcName(int netId) => ContentSamples.NpcsByNetId.TryGetValue(netId, out NPC sample) ? sample.TypeName : "?";

		public static string ItemName(int type) => type > 0 && type < ItemLoader.ItemCount ? Lang.GetItemNameValue(type) : "?";
	}

	// An item icon: slot tinted by rarity, stack count, vanilla tooltip on hover, click opens the item page.
	// Type 0 is an undiscovered item ("?"), never revealing what it is.
	public class CodexItemIcon : UIElement
	{
		public readonly int Type;
		private readonly int count;
		private readonly Item item;
		public string HoverText;
		public Color Tint = Color.White;

		public CodexItemIcon(int type, int count = 0, float size = 44, Action onClick = null)
		{
			Type = type;
			this.count = count;
			Width.Set(size, 0);
			Height.Set(size, 0);
			if (type > 0 && ContentSamples.ItemsByType.TryGetValue(type, out Item sample)) item = sample;
			if (onClick != null) OnLeftClick += (_, _) => Click(onClick);
			else if (item != null) OnLeftClick += (_, _) => Click(() => CodexUIState.Open(CodexPage.ItemPage(Type)));
		}

		private static void Click(Action action)
		{
			SoundEngine.PlaySound(SoundID.MenuTick);
			action();
		}

		protected override void DrawSelf(SpriteBatch spriteBatch)
		{
			CalculatedStyle d = GetDimensions();
			Rectangle r = d.ToRectangle();
			Color back = Tint;
			if (item != null && item.rare != ItemRarityID.White) back = Color.Lerp(back, ItemRarity.GetColor(item.rare), 0.3f);
			if (item == null) back = new Color(120, 120, 140);
			if (IsMouseHovering) back = Color.Lerp(back, Color.White, 0.5f);
			spriteBatch.Draw(TextureAssets.InventoryBack.Value, r, back * (IsMouseHovering ? 1f : 0.85f));
			if (item != null)
			{
				Main.instance.LoadItem(item.type);
				ItemSlot.DrawItemIcon(item, 31, spriteBatch, d.Center(), 1f, d.Width * 0.62f, Color.White);
				if (count > 1)
				{
					float scale = d.Width < 40 ? 0.65f : 0.75f;
					Utils.DrawBorderString(spriteBatch, CodexDraw.Count(count), new Vector2(d.X + d.Width * 0.12f, d.Y + d.Height - 22 * scale - 2), Color.White, scale, 0, 0, -1);
				}
			}
			else
			{
				string mark = "?";
				float scale = d.Width / 40f;
				Vector2 size = FontAssets.MouseText.Value.MeasureString(mark) * scale;
				Utils.DrawBorderString(spriteBatch, mark, d.Center() - new Vector2(size.X / 2, size.Y / 2 - 4 * scale), CodexDraw.Muted, scale, 0, 0, -1);
			}
			if (IsMouseHovering)
			{
				if (!string.IsNullOrEmpty(HoverText)) CodexHover.Text = HoverText;
				else if (item != null) CodexHover.Item = item;
			}
		}
	}

	// An animated NPC: the bestiary icon of its entry (a clone, so each portrait animates on its own), else its
	// texture frames. The NPC is drawn by a child so that the portrait clips it.
	public class CodexNpcPortrait : UIElement
	{
		public readonly int NetId;
		private readonly int count;
		private readonly bool big;
		private readonly Asset BackgroundAsset;
		public string HoverText;

		private sealed class Asset
		{
			public ReLogic.Content.Asset<Texture2D> Texture;
			public Color Color;
		}

		public CodexNpcPortrait(int netId, float width, float height, bool big = false, int count = 0, bool clickable = true)
		{
			NetId = netId;
			this.count = count;
			this.big = big;
			Width.Set(width, 0);
			Height.Set(height, 0);
			OverflowHidden = true;
			BestiaryEntry entry = Main.BestiaryDB?.FindEntryByNPCID(netId);
			if (big && entry?.Info != null)
			{
				foreach (IBestiaryBackgroundImagePathAndColorProvider provider in entry.Info.OfType<IBestiaryBackgroundImagePathAndColorProvider>())
				{
					ReLogic.Content.Asset<Texture2D> texture = provider.GetBackgroundImage();
					if (texture == null) continue;
					BackgroundAsset = new Asset { Texture = texture, Color = provider.GetBackgroundColor() ?? Color.White };
					break;
				}
			}
			Append(new NpcDrawer(netId, entry, big));
			if (clickable)
			{
				OnLeftClick += (_, _) =>
				{
					SoundEngine.PlaySound(SoundID.MenuTick);
					CodexUIState.Open(CodexPage.NpcPage(NetId));
				};
			}
		}

		protected override void DrawSelf(SpriteBatch spriteBatch)
		{
			Rectangle r = GetDimensions().ToRectangle();
			CodexDraw.Fill(spriteBatch, r, IsMouseHovering ? new Color(60, 74, 140) : new Color(33, 40, 82));
			if (BackgroundAsset != null && BackgroundAsset.Texture.IsLoaded) spriteBatch.Draw(BackgroundAsset.Texture.Value, r, BackgroundAsset.Color);
			if (IsMouseHovering)
			{
				string text = HoverText ?? CodexDraw.NpcName(NetId);
				int kills = CodexSystem.KillCount(NetId);
				if (HoverText == null && kills > 0) text += "\n" + Language.GetTextValue("Mods.UltimateRandomizer.Codex.Kills", kills);
				CodexHover.Text = text;
			}
		}

		protected override void DrawChildren(SpriteBatch spriteBatch)
		{
			base.DrawChildren(spriteBatch);
			Rectangle r = GetDimensions().ToRectangle();
			if (count > 0 && !big) Utils.DrawBorderString(spriteBatch, "x" + CodexDraw.Count(count), new Vector2(r.X + 4, r.Bottom - 20), Color.White, 0.75f, 0, 0, -1);
			CodexDraw.Border(spriteBatch, r, IsMouseHovering ? CodexDraw.Gold : new Color(18, 22, 48));
		}

		private sealed class NpcDrawer : UIElement
		{
			private readonly int netId;
			private readonly BestiaryEntry entry;
			private readonly bool big;
			private IEntryIcon icon;

			public NpcDrawer(int netId, BestiaryEntry entry, bool big)
			{
				this.netId = netId;
				this.entry = entry;
				this.big = big;
				Width.Set(0, 1f);
				Height.Set(0, 1f);
				IgnoresMouseInteraction = true;
				try
				{
					icon = entry?.Icon?.CreateClone();
				}
				catch (Exception)
				{
					icon = null;
				}
			}

			protected override void DrawSelf(SpriteBatch spriteBatch)
			{
				Rectangle box = GetDimensions().ToRectangle();
				if (icon != null)
				{
					try
					{
						BestiaryUICollectionInfo info = new() { OwnerEntry = entry, UnlockState = BestiaryEntryUnlockState.CanShowDropsWithDropRates_4 };
						// Hovered: the icon animates (the bestiary only animates the hovered entry of its grid)
						EntryIconDrawSettings settings = new() { iconbox = box, IsPortrait = big, IsHovered = true };
						icon.Update(info, box, settings);
						icon.Draw(info, spriteBatch, settings);
						return;
					}
					catch (Exception e)
					{
						ModContent.GetInstance<UltimateRandomizer>().Logger.Warn($"Codex: bestiary icon of {netId} failed, drawing its texture instead: {e.Message}");
						icon = null;
					}
				}
				DrawTexture(spriteBatch, box);
			}

			private void DrawTexture(SpriteBatch spriteBatch, Rectangle box)
			{
				if (!ContentSamples.NpcsByNetId.TryGetValue(netId, out NPC sample)) return;
				int type = sample.type;
				if (type <= 0 || type >= TextureAssets.Npc.Length) return;
				Main.instance.LoadNPC(type);
				Texture2D texture = TextureAssets.Npc[type].Value;
				int frames = Math.Max(1, Main.npcFrameCount[type]);
				int frameHeight = texture.Height / frames;
				int frame = (int)(Main.GameUpdateCount / 8 % frames);
				Rectangle source = new(0, frame * frameHeight, texture.Width, frameHeight);
				float scale = Math.Min(1f, Math.Min(box.Width / (float)texture.Width, box.Height / (float)frameHeight) * 0.9f);
				spriteBatch.Draw(texture, box.Center.ToVector2(), source, sample.color == default ? Color.White : sample.color, 0f, source.Size() / 2f, scale, SpriteEffects.None, 0f);
			}
		}
	}

	// A text button with hover and "active" highlights
	public class CodexButton : UITextPanel<string>
	{
		public bool Active;
		public bool Enabled = true;

		public CodexButton(string text, float width, Action onClick, float scale = 0.9f) : base(text, scale)
		{
			Width.Set(width, 0);
			Height.Set(32, 0);
			SetPadding(6);
			OnLeftClick += (_, _) =>
			{
				if (!Enabled) return;
				SoundEngine.PlaySound(SoundID.MenuTick);
				onClick?.Invoke();
			};
		}

		protected override void DrawSelf(SpriteBatch spriteBatch)
		{
			BackgroundColor = !Enabled ? new Color(40, 46, 80) * 0.8f : Active ? new Color(90, 112, 200) : IsMouseHovering ? new Color(76, 96, 178) : new Color(55, 70, 140) * 0.9f;
			BorderColor = Active ? CodexDraw.Gold : IsMouseHovering && Enabled ? Color.White * 0.8f : Color.Black;
			TextColor = Enabled ? Color.White : Color.Gray;
			base.DrawSelf(spriteBatch);
		}
	}

	// One line of text, measured so that rows can place elements after it
	public class CodexLabel : UIElement
	{
		private readonly string text;
		private readonly float scale;
		private readonly Color color;
		public string HoverText;

		public CodexLabel(string text, float scale = 0.9f, Color? color = null)
		{
			this.text = text ?? "";
			this.scale = scale;
			this.color = color ?? Color.White;
			Width.Set(CodexDraw.TextWidth(this.text, scale) + 4, 0);
			Height.Set(28 * scale, 0);
		}

		protected override void DrawSelf(SpriteBatch spriteBatch)
		{
			CalculatedStyle d = GetDimensions();
			Utils.DrawBorderString(spriteBatch, text, new Vector2(d.X + 2, d.Y + 2 * scale), color, scale, 0, 0, -1);
			if (IsMouseHovering && !string.IsNullOrEmpty(HoverText)) CodexHover.Text = HoverText;
		}
	}

	// A section title with a line under it
	public class CodexSection : UIElement
	{
		private readonly string text;

		public CodexSection(string text)
		{
			this.text = text;
			Width.Set(0, 1f);
			Height.Set(34, 0);
		}

		protected override void DrawSelf(SpriteBatch spriteBatch)
		{
			CalculatedStyle d = GetDimensions();
			Utils.DrawBorderString(spriteBatch, text, new Vector2(d.X + 4, d.Y + 6), CodexDraw.Gold, 1f, 0, 0, -1);
			CodexDraw.Fill(spriteBatch, new Rectangle((int)d.X, (int)(d.Y + d.Height - 3), (int)d.Width, 2), CodexDraw.Gold * 0.35f);
		}
	}

	// A horizontal row: elements placed left to right, vertically centred
	public class CodexRow : UIElement
	{
		private float x;
		public bool Highlight;

		public CodexRow(float height, float indent = 0)
		{
			Width.Set(0, 1f);
			Height.Set(height, 0);
			x = indent + 4;
		}

		public float Used => x;

		public T Add<T>(T element, float gap = 6) where T : UIElement
		{
			element.Left.Set(x, 0);
			element.VAlign = 0.5f;
			Append(element);
			x += element.Width.Pixels + gap;
			return element;
		}

		public void Space(float pixels) => x += pixels;

		protected override void DrawSelf(SpriteBatch spriteBatch)
		{
			if (Highlight && IsMouseHovering) CodexDraw.Fill(spriteBatch, GetDimensions().ToRectangle(), Color.White * 0.06f);
		}
	}

	// A recipe as it is now: ingredient icons, the result, then the crafting stations and conditions
	public class CodexRecipeRow : CodexRow
	{
		public readonly int RecipeIndex;

		public CodexRecipeRow(int recipeIndex, float indent = 0, float iconSize = 40) : base(iconSize + 6, indent)
		{
			RecipeIndex = recipeIndex;
			Highlight = true;
			Recipe recipe = Main.recipe[recipeIndex];
			foreach (Item ingredient in recipe.requiredItem)
			{
				if (ingredient == null || ingredient.IsAir) continue;
				Add(new CodexItemIcon(ingredient.type, ingredient.stack, iconSize), 4);
			}
			Space(4);
			Add(new CodexLabel("»", 1.2f, CodexDraw.Gold), 6);
			Add(new CodexItemIcon(recipe.createItem.type, recipe.createItem.stack, iconSize), 6);
			if (recipe.requiredTile.Count > 0 || recipe.Conditions.Count > 0)
			{
				Space(10);
				Add(new CodexLabel(Language.GetTextValue("Mods.UltimateRandomizer.Codex.At"), 0.8f, CodexDraw.Muted), 4);
			}
			foreach (int tile in recipe.requiredTile)
			{
				if (tile < 0) continue;
				int style = Recipe.GetRequiredTileStyle(tile);
				int item = TileLoader.GetItemDropFromTypeAndStyle(tile, style);
				if (item > 0) Add(new CodexItemIcon(item, 0, iconSize - 6) { Tint = new Color(170, 230, 170) }, 4);
				else Add(new CodexLabel(Lang.GetMapObjectName(MapHelper.TileToLookup(tile, style)), 0.8f), 6);
			}
			foreach (Condition condition in recipe.Conditions)
			{
				string description = condition?.Description?.Value;
				if (!string.IsNullOrEmpty(description)) Add(new CodexLabel(description, 0.75f, CodexDraw.Muted), 8);
			}
		}
	}

	// The filter icon of a biome from the bestiary, in a fixed box
	public class CodexBiomeIcon : UIElement
	{
		public CodexBiomeIcon(string biome, float size = 34)
		{
			Width.Set(size, 0);
			Height.Set(size, 0);
			IBestiaryInfoElement element = Features.BiomeSlots.BestiaryElement(biome);
			UIElement image = null;
			try
			{
				image = (element as IFilterInfoProvider)?.GetFilterImage();
			}
			catch (Exception)
			{
				image = null;
			}
			if (image != null)
			{
				image.HAlign = 0.5f;
				image.VAlign = 0.5f;
				image.IgnoresMouseInteraction = true;
				Append(image);
			}
			hover = Features.BiomeSlots.BiomeDisplayName(biome);
		}

		private readonly string hover;

		protected override void DrawSelf(SpriteBatch spriteBatch)
		{
			Rectangle r = GetDimensions().ToRectangle();
			CodexDraw.Fill(spriteBatch, r, new Color(25, 30, 62));
			CodexDraw.Border(spriteBatch, r, new Color(90, 100, 160), 1);
			if (IsMouseHovering) CodexHover.Text = hover;
		}
	}

	// Lays cells of the same size out in rows that fit the width, each row a list item (the list only draws visible rows)
	public static class CodexGrid
	{
		public static List<UIElement> Rows(IEnumerable<UIElement> cells, float cellWidth, float cellHeight, float width, float indent = 0, float gap = 6)
		{
			List<UIElement> rows = new();
			int perRow = Math.Max(1, (int)((width - indent - 8 + gap) / (cellWidth + gap)));
			UIElement row = null;
			int inRow = 0;
			foreach (UIElement cell in cells)
			{
				if (row == null || inRow == perRow)
				{
					row = new UIElement();
					row.Width.Set(0, 1f);
					row.Height.Set(cellHeight, 0);
					rows.Add(row);
					inRow = 0;
				}
				cell.Left.Set(indent + 4 + inRow * (cellWidth + gap), 0);
				cell.Top.Set(0, 0);
				row.Append(cell);
				inRow++;
			}
			return rows;
		}
	}
}

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

	// The slow part of showing a page the first time is not building it but the cold work of its first frames:
	// every bestiary icon clone runs NPC.SetDefaults (all mods' hooks), and every NPC or item texture not drawn yet is
	// read from disk at once (Main.LoadNPC / LoadItem). A tab of a hundred portraits did all of it in one frame. Here at
	// most PerFrame of these happen per frame drawn; the others wait (drawn as "...") and come on the next frames.
	public static class CodexBudget
	{
		public const int PerFrame = 8;
		private static int left = PerFrame;

		// Something waited in the last frame: the codex shows "Loading..."
		public static bool Deferred { get; private set; }
		private static bool deferredNow;

		public static void NewFrame()
		{
			Deferred = deferredNow;
			deferredNow = false;
			left = PerFrame;
		}

		public static bool TrySpend()
		{
			if (left > 0)
			{
				left--;
				return true;
			}
			deferredNow = true;
			return false;
		}

		public static bool NpcTexture(int type)
		{
			if (type <= 0 || type >= TextureAssets.Npc.Length || TextureAssets.Npc[type].IsLoaded) return true;
			if (!TrySpend()) return false;
			Main.instance.LoadNPC(type);
			return true;
		}

		public static bool ItemTexture(int type)
		{
			if (type <= 0 || type >= TextureAssets.Item.Length || TextureAssets.Item[type].IsLoaded) return true;
			if (!TrySpend()) return false;
			Main.instance.LoadItem(type);
			return true;
		}

		public static void DrawWaiting(SpriteBatch spriteBatch, Rectangle r)
		{
			Utils.DrawBorderString(spriteBatch, "...", r.Center.ToVector2(), CodexDraw.Muted, 0.8f, 0.5f, 0.5f, -1);
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

		// Shortened with "..." to fit a width (the full text shows on hover)
		public static string Fit(string text, float scale, float width)
		{
			if (TextWidth(text, scale) <= width) return text;
			while (text.Length > 4 && TextWidth(text + "...", scale) > width) text = text.Substring(0, text.Length - 1);
			return text + "...";
		}

		// A black shape with a faint outline (hints): what it is, without its colours
		public static void Silhouette(SpriteBatch spriteBatch, Texture2D texture, Vector2 center, Rectangle source, float scale, SpriteEffects effects = SpriteEffects.None)
		{
			Vector2 origin = source.Size() / 2f;
			Color outline = Color.White * 0.35f;
			for (int i = 0; i < 4; i++)
			{
				Vector2 offset = new(i == 0 ? -2 : i == 1 ? 2 : 0, i == 2 ? -2 : i == 3 ? 2 : 0);
				spriteBatch.Draw(texture, center + offset, source, outline, 0f, origin, scale, effects, 0f);
			}
			spriteBatch.Draw(texture, center, source, Color.Black, 0f, origin, scale, effects, 0f);
		}
	}

	// An item icon: slot tinted by rarity, stack count, vanilla tooltip on hover, click opens the item page.
	// Type 0 is an undiscovered item ("?"), never revealing what it is.
	public class CodexItemIcon : UIElement
	{
		public readonly int Type;
		private readonly int count;
		private readonly string countText;
		private readonly Item item;
		public string HoverText;
		public Color Tint = Color.White;
		// Drawn as a black shape: no colours, no name, no click to its page (hints)
		public bool Silhouette;

		public CodexItemIcon(int type, int count = 0, float size = 44, Action onClick = null)
		{
			Type = type;
			this.count = count;
			countText = CodexDraw.Count(count);
			Width.Set(size, 0);
			Height.Set(size, 0);
			if (type > 0 && ContentSamples.ItemsByType.TryGetValue(type, out Item sample)) item = sample;
			if (onClick != null) OnLeftClick += (_, _) => Click(onClick);
			else if (item != null) OnLeftClick += (_, _) =>
			{
				if (!Silhouette) Click(() => CodexUIState.Open(CodexPage.ItemPage(Type)));
			};
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
			if (item != null && item.rare != ItemRarityID.White && !Silhouette) back = Color.Lerp(back, ItemRarity.GetColor(item.rare), 0.3f);
			if (item == null) back = new Color(120, 120, 140);
			if (IsMouseHovering) back = Color.Lerp(back, Color.White, 0.5f);
			spriteBatch.Draw(TextureAssets.InventoryBack.Value, r, back * (IsMouseHovering ? 1f : 0.85f));
			if (item != null && !CodexBudget.ItemTexture(item.type))
			{
				CodexBudget.DrawWaiting(spriteBatch, r);
			}
			else if (item != null)
			{
				if (Silhouette) DrawSilhouette(spriteBatch, d);
				else ItemSlot.DrawItemIcon(item, 31, spriteBatch, d.Center(), 1f, d.Width * 0.62f, Color.White);
				if (count > 1)
				{
					float scale = d.Width < 40 ? 0.65f : 0.75f;
					Utils.DrawBorderString(spriteBatch, countText, new Vector2(d.X + d.Width * 0.12f, d.Y + d.Height - 22 * scale - 2), Color.White, scale, 0, 0, -1);
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
				else if (Silhouette) CodexHover.Text = CodexText.Get("Hint.Unknown");
				else if (item != null) CodexHover.Item = item;
			}
		}

		private void DrawSilhouette(SpriteBatch spriteBatch, CalculatedStyle d)
		{
			if (Type <= 0 || Type >= TextureAssets.Item.Length) return;
			Texture2D texture = TextureAssets.Item[Type].Value;
			Rectangle source = Type < Main.itemAnimations.Length && Main.itemAnimations[Type] != null ? Main.itemAnimations[Type].GetFrame(texture, -1) : texture.Frame();
			float limit = d.Width * 0.62f;
			float scale = Math.Min(1f, limit / Math.Max(1, Math.Max(source.Width, source.Height)));
			CodexDraw.Silhouette(spriteBatch, texture, d.Center(), source, scale);
		}
	}

	// An animated NPC: the bestiary icon of its entry (a clone, so each portrait animates on its own, kept by the codex
	// across rebuilds), else its texture frames. The NPC is drawn by a child so that the portrait clips it.
	public class CodexNpcPortrait : UIElement
	{
		public readonly int NetId;
		private readonly int count;
		private readonly string countText;
		private readonly bool big;
		private readonly Asset BackgroundAsset;
		public string HoverText;
		// A black shape: no background, no name, no click to its page (hints)
		public readonly bool Silhouette;

		private sealed class Asset
		{
			public ReLogic.Content.Asset<Texture2D> Texture;
			public Color Color;
		}

		// textureOnly: its texture frames instead of a bestiary clone (portraits outside the codex window)
		public CodexNpcPortrait(int netId, float width, float height, bool big = false, int count = 0, bool clickable = true, bool silhouette = false, bool textureOnly = false)
		{
			NetId = netId;
			Silhouette = silhouette;
			this.count = count;
			countText = "x" + CodexDraw.Count(count);
			this.big = big;
			Width.Set(width, 0);
			Height.Set(height, 0);
			OverflowHidden = true;
			BestiaryEntry entry = Main.BestiaryDB?.FindEntryByNPCID(netId);
			if (big && !silhouette && entry?.Info != null)
			{
				foreach (IBestiaryBackgroundImagePathAndColorProvider provider in entry.Info.OfType<IBestiaryBackgroundImagePathAndColorProvider>())
				{
					ReLogic.Content.Asset<Texture2D> texture = provider.GetBackgroundImage();
					if (texture == null) continue;
					BackgroundAsset = new Asset { Texture = texture, Color = provider.GetBackgroundColor() ?? Color.White };
					break;
				}
			}
			Append(new NpcDrawer(netId, silhouette || textureOnly ? null : entry, big, silhouette));
			if (clickable && !silhouette)
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
			if (IsMouseHovering && Silhouette)
			{
				CodexHover.Text = HoverText ?? CodexText.Get("Hint.Unknown");
			}
			else if (IsMouseHovering)
			{
				string text = HoverText ?? HintFacts.NpcName(NetId);
				int kills = CodexSystem.KillCount(NetId);
				if (HoverText == null && kills > 0) text += "\n" + CodexText.Get("Kills", kills);
				CodexHover.Text = text;
			}
		}

		protected override void DrawChildren(SpriteBatch spriteBatch)
		{
			base.DrawChildren(spriteBatch);
			Rectangle r = GetDimensions().ToRectangle();
			if (count > 0 && !big) Utils.DrawBorderString(spriteBatch, countText, new Vector2(r.X + 4, r.Bottom - 20), Color.White, 0.75f, 0, 0, -1);
			CodexDraw.Border(spriteBatch, r, IsMouseHovering ? CodexDraw.Gold : new Color(18, 22, 48));
		}

		private sealed class NpcDrawer : UIElement
		{
			private readonly int netId;
			private readonly int type;
			private readonly BestiaryEntry entry;
			private readonly bool big;
			private readonly bool silhouette;
			private IEntryIcon icon;
			// The clone is made on the first frames drawn, within the codex budget (CodexBudget)
			private bool iconResolved;

			// entry null: drawn from its texture (no bestiary clone)
			public NpcDrawer(int netId, BestiaryEntry entry, bool big, bool silhouette)
			{
				this.netId = netId;
				this.entry = entry;
				this.big = big;
				this.silhouette = silhouette;
				type = ContentSamples.NpcsByNetId.TryGetValue(netId, out NPC sample) ? sample.type : 0;
				Width.Set(0, 1f);
				Height.Set(0, 1f);
				IgnoresMouseInteraction = true;
			}

			// The icon and the texture are ready to use (the bestiary icon reads the texture when it updates)
			private bool Ready()
			{
				if (!iconResolved)
				{
					CodexUIState codex = CodexUIState.Instance;
					if (codex == null || entry == null) iconResolved = true;
					else
					{
						icon = codex.BestiaryIcon(netId, big, entry, out bool deferred);
						iconResolved = !deferred;
					}
					if (!iconResolved) return false;
				}
				return CodexBudget.NpcTexture(type);
			}

			private BestiaryUICollectionInfo Info => new() { OwnerEntry = entry, UnlockState = BestiaryEntryUnlockState.CanShowDropsWithDropRates_4 };

			// Hovered: the icon animates (the bestiary only animates the hovered entry of its grid)
			private EntryIconDrawSettings Settings(Rectangle box) => new() { iconbox = box, IsPortrait = big, IsHovered = true };

			private void Fail(Exception e)
			{
				ModContent.GetInstance<UltimateRandomizer>().Logger.Warn($"Codex: bestiary icon of {netId} failed, drawing its texture instead: {e.Message}");
				CodexUIState.Instance?.ForgetIcon(netId, big, icon);
				icon = null;
			}

			// Animated once per game tick (not per frame drawn), only while in the visible part of the list
			public override void Update(GameTime gameTime)
			{
				base.Update(gameTime);
				Rectangle box = GetDimensions().ToRectangle();
				UIElement view = CodexUIState.Instance?.Content;
				if (view != null && !view.GetDimensions().ToRectangle().Intersects(box)) return;
				if (!Ready() || icon == null) return;
				try
				{
					icon.Update(Info, box, Settings(box));
				}
				catch (Exception e)
				{
					Fail(e);
				}
			}

			protected override void DrawSelf(SpriteBatch spriteBatch)
			{
				Rectangle box = GetDimensions().ToRectangle();
				if (!Ready())
				{
					CodexBudget.DrawWaiting(spriteBatch, box);
					return;
				}
				if (icon != null)
				{
					try
					{
						icon.Draw(Info, spriteBatch, Settings(box));
						return;
					}
					catch (Exception e)
					{
						Fail(e);
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
				if (silhouette)
				{
					CodexDraw.Silhouette(spriteBatch, texture, box.Center.ToVector2(), source, scale);
					return;
				}
				spriteBatch.Draw(texture, box.Center.ToVector2(), source, sample.color == default ? Color.White : sample.color, 0f, source.Size() / 2f, scale, SpriteEffects.None, 0f);
			}
		}
	}

	// A text button with hover and "active" highlights
	public class CodexButton : UITextPanel<string>
	{
		public bool Active;
		public bool Enabled = true;
		private readonly LocalizedText label;

		public CodexButton(LocalizedText label, float width, Action onClick, float scale = 0.9f) : this(label.Value, width, onClick, scale)
		{
			this.label = label;
		}

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
			if (label != null && Text != label.Value) SetText(label.Value);
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

		public string Text => text;

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

	// A cell of a grid with a small caption under its icon or portrait (drop chance, spawn share), and a note under it when
	// the source only works at some time or with a condition (CodexWhen: "night", "condition"; the full text on hover)
	public class CodexCaptioned : UIElement
	{
		public const float CaptionHeight = 18;
		public readonly UIElement Inner;
		public readonly CodexLabel Caption;
		// Null without a note
		public readonly CodexLabel Note;

		public CodexCaptioned(UIElement inner, string caption, float width, string hover = null, CodexNote note = default)
		{
			Inner = inner;
			Width.Set(width, 0);
			bool noted = note.Short != null && !note.IsEmpty;
			Height.Set(inner.Height.Pixels + CaptionHeight * (noted ? 2 : 1), 0);
			inner.HAlign = 0.5f;
			Append(inner);
			Caption = new CodexLabel(caption, 0.7f, CodexDraw.Muted) { HoverText = hover };
			Caption.HAlign = 0.5f;
			Caption.Top.Set(inner.Height.Pixels, 0);
			Append(Caption);
			if (!noted) return;
			Note = new CodexLabel(note.Short, 0.65f, CodexWhen.NoteColor) { HoverText = note.Full };
			Note.HAlign = 0.5f;
			Note.Top.Set(inner.Height.Pixels + CaptionHeight, 0);
			Append(Note);
		}

		// The height of a grid's cells: one more line when one of them has a note
		public static float CellHeight(float inner, bool anyNote) => inner + CaptionHeight * (anyNote ? 2 : 1);
	}

	// A section title with a line under it
	public class CodexSection : UIElement
	{
		private readonly string text;

		public string Text => text;

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
				Add(new CodexLabel(CodexText.Get("At"), 0.8f, CodexDraw.Muted), 4);
			}
			foreach (int tile in recipe.requiredTile)
			{
				if (tile < 0) continue;
				int item = HintFacts.StationItem(tile);
				if (item > 0) Add(new CodexItemIcon(item, 0, iconSize - 6) { Tint = new Color(170, 230, 170) }, 4);
				else Add(new CodexLabel(HintFacts.StationName(tile), 0.8f), 6);
			}
			// Where it must be made (near water, in the snow, in a graveyard...), the whole sentence on hover
			string where = CodexWhen.Recipe(recipeIndex).Full;
			foreach (Condition condition in recipe.Conditions)
			{
				string description = condition?.Description?.Value;
				if (!string.IsNullOrEmpty(description)) Add(new CodexLabel(description, 0.75f, CodexWhen.NoteColor) { HoverText = where }, 8);
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
			string description = Features.BiomeSlots.BiomeDescription(biome);
			hover = Features.BiomeSlots.BiomeDisplayName(biome) + (description.Length > 0 ? "\n" + description : "");
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

	// One row of a grid; its cells are made when the row first comes into view (CodexList)
	public sealed class CodexGridRow : UIElement, ICodexLazy
	{
		private Action<CodexGridRow> fill;

		public CodexGridRow(float height, Action<CodexGridRow> fill)
		{
			Width.Set(0, 1f);
			Height.Set(height, 0);
			this.fill = fill;
		}

		public void Materialize()
		{
			if (fill == null) return;
			Action<CodexGridRow> make = fill;
			fill = null;
			make(this);
		}
	}

	// Lays cells of the same size out in rows that fit the width, each row a list item. Cells are made from the source
	// only for rows that are shown: a tab of thousands of items builds a few hundred icons.
	public static class CodexGrid
	{
		public static int PerRow(float cellWidth, float width, float indent = 0, float gap = 6) => Math.Max(1, (int)((width - indent - 8 + gap) / (cellWidth + gap)));

		public static List<UIElement> Rows<T>(IReadOnlyList<T> source, Func<T, UIElement> make, float cellWidth, float cellHeight, float width, float indent = 0, float gap = 6)
		{
			int perRow = PerRow(cellWidth, width, indent, gap);
			List<UIElement> rows = new((source.Count + perRow - 1) / perRow);
			for (int start = 0; start < source.Count; start += perRow)
			{
				int from = start;
				rows.Add(new CodexGridRow(cellHeight, row =>
				{
					int end = Math.Min(source.Count, from + perRow);
					for (int i = from; i < end; i++)
					{
						UIElement cell = make(source[i]);
						cell.Left.Set(indent + 4 + (i - from) * (cellWidth + gap), 0);
						cell.Top.Set(0, 0);
						row.Append(cell);
					}
				}));
			}
			return rows;
		}
	}
}

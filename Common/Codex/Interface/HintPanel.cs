using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.UI.Chat;
using System.Linq;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Codex.Interface
{
	public enum HintPurchase { Bought, TooPoor, NotNext, NotSynced, NoTarget, Locked }

	// Buying hint levels: the local player pays with its own coins (inventory, piggy bank, safe...: Player.BuyItem, like
	// a shop purchase, which vanilla also does on the client) and records the level. In multiplayer the level goes to
	// the server with the codex discoveries; the server checks it is the next level of a target offered now and shares
	// it, or refuses it and the coins come back (CodexSystem).
	public static class HintShop
	{
		// Why a level can't be bought now (Bought: it can), before any coin is taken
		public static HintPurchase Check(HintTarget target, int level)
		{
			if (target == null) return HintPurchase.NoTarget;
			// Joining: the levels already bought by others are not known yet
			if (!CodexSystem.CanBuyHints) return HintPurchase.NotSynced;
			// Shown for what comes later, not sold yet (the server refuses it too: CodexSystem.AcceptHint)
			if (HintFacts.IsLocked(target)) return HintPurchase.Locked;
			// A target already found needs no hint (its last level is given for free)
			if (CodexSystem.IsFound(target.Item) || level < 1 || level > HintFacts.MaxLevel || level != CodexSystem.HintLevel(target.Item) + 1) return HintPurchase.NotNext;
			return HintPurchase.Bought;
		}

		public static HintPurchase TryBuy(Player player, HintTarget target, int level, out long price)
		{
			price = target == null ? 0 : HintFacts.Price(target, level);
			HintPurchase check = Check(target, level);
			if (check != HintPurchase.Bought) return check;
			if (price > 0 && !player.BuyItem(price, -1)) return HintPurchase.TooPoor;
			HintFact fact = HintFacts.For(target.Item);
			// The last level shows the recipe: discovered in the codex once the level is accepted
			Action accepted = level == HintFacts.MaxLevel && fact != null && fact.Kind == HintKind.Recipe ? () => CodexSystem.RecordRecipeOnly(fact.Recipe) : null;
			CodexSystem.RecordHint(target.Item, level, price, accepted);
			return HintPurchase.Bought;
		}

		// The Guide says it
		public static void Say(string text)
		{
			Main.npcChatText = text;
			Main.npcChatCornerItem = 0;
		}
	}

	// The Guide's "Hint" button (after Help, Crafting and Happiness) and the hint panel, client side. tML only lets a
	// ModNPC set its own two chat buttons, so the button is drawn after vanilla's, at the place vanilla would put a next one.
	public class HintSystem : ModSystem
	{
		private static UserInterface userInterface;
		private static HintPanelState state;
		private static bool hovered;
		private static int warmDelay;
		private static int warmVersion = -1;

		// Right and bottom edges of the Guide's dialog and its buttons in the last frame drawn (the panel keeps clear of them)
		public static float ChatRight { get; private set; }
		public static float ChatBottom { get; private set; }

		// Made when first opened: the mod's texts are not loaded yet during Load
		public static HintPanelState State
		{
			get
			{
				if (state == null && userInterface != null)
				{
					state = new HintPanelState();
					state.Activate();
				}
				return state;
			}
		}

		public static bool IsOpen => userInterface?.CurrentState != null;

		// Typing in the panel's search: letters are text, not hotkeys
		public static bool IsTyping => IsOpen && state != null && state.IsTyping;

		public override void Load()
		{
			if (Main.dedServ) return;
			userInterface = new UserInterface();
			On_Main.DrawNPCChatButtons += DrawHintButton;
		}

		public override void Unload()
		{
			userInterface = null;
			state = null;
			hovered = false;
		}

		public static void Open()
		{
			if (userInterface == null) return;
			State.Refresh();
			userInterface.SetState(state);
		}

		public static void Close()
		{
			state?.StopTyping();
			userInterface?.SetState(null);
			hovered = false;
		}

		// Leaves the Guide (panel and dialog) for the codex page about a hint target
		public static void OpenInCodex(int item)
		{
			CodexPage page = HintNavigation.Target(item);
			Close();
			Player player = Main.LocalPlayer;
			if (player != null && player.talkNPC >= 0) player.SetTalkNPC(-1);
			Main.npcChatText = "";
			Main.npcChatCornerItem = 0;
			Journal.JournalSystem.OpenAt(page);
		}

		public override void OnWorldUnload()
		{
			Close();
			warmDelay = 0;
		}

		private static bool TalkingToGuide()
		{
			Player player = Main.LocalPlayer;
			return player != null && player.talkNPC >= 0 && player.talkNPC < Main.maxNPCs && Main.npc[player.talkNPC].active && Main.npc[player.talkNPC].type == NPCID.Guide;
		}

		// A second after the world loads or the hint caches are forgotten (tier change...), their work is spread over frames
		public override void PostUpdateEverything()
		{
			if (Main.dedServ || !HintFacts.Ready || HintFacts.Warmed) return;
			if (warmVersion != HintFacts.CacheVersion)
			{
				warmVersion = HintFacts.CacheVersion;
				warmDelay = 0;
			}
			if (++warmDelay > 60) HintFacts.Warm(2.0);
		}

		public override void UpdateUI(GameTime gameTime)
		{
			if (userInterface?.CurrentState == null) return;
			if (!TalkingToGuide() || Main.InGuideCraftMenu || !HintFacts.Ready || (state != null && state.CloseRequested))
			{
				if (state != null) state.CloseRequested = false;
				Close();
				return;
			}
			userInterface.Update(gameTime);
		}

		public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
		{
			int index = layers.FindIndex(layer => layer.Name == "Vanilla: Mouse Text");
			if (index < 0) return;
			layers.Insert(index, new LegacyGameInterfaceLayer("UltimateRandomizer: Hints", () =>
			{
				if (userInterface?.CurrentState != null) userInterface.Draw(Main.spriteBatch, new GameTime());
				return true;
			}, InterfaceScaleType.UI));
		}

		// Vanilla's layout (Main.DrawNPCChatButtons): buttons from x = 180 + (screenWidth - 800) / 2 at y = 130 + 30 x lines,
		// each one 30 pixels after the previous (the first is at most 260 wide); Happiness comes last when shown
		private static void DrawHintButton(On_Main.orig_DrawNPCChatButtons orig, int superColor, Color chatColor, int numLines, string focusText, string focusText3)
		{
			orig(superColor, chatColor, numLines, focusText, focusText3);
			if (!TalkingToGuide() || !HintFacts.Ready || Main.InGuideCraftMenu)
			{
				hovered = false;
				return;
			}
			DynamicSpriteFont font = FontAssets.MouseText.Value;
			Vector2 baseScale = new(0.9f);
			float Width(string text) => ChatManager.GetStringSize(font, text ?? "", baseScale).X;
			float x = 180 + (Main.screenWidth - 800) / 2;
			float y = 130 + numLines * 30;
			x += Math.Min(260f, Width(focusText)) + 30;
			x += Width(Lang.inter[52].Value) + 30;
			if (!string.IsNullOrWhiteSpace(focusText3)) x += Width(focusText3) + 30;
			if (!Main.remixWorld && Main.LocalPlayer.currentShoppingSettings.HappinessReport != "") x += Width(Language.GetTextValue("UI.NPCCheckHappiness")) + 30;

			string label = CodexText.Get("Hint.Button");
			Vector2 size = ChatManager.GetStringSize(font, label, baseScale);
			Vector2 position = new(x, y);
			ChatRight = Math.Max(Main.screenWidth / 2f + TextureAssets.ChatBack.Width() / 2f, x + size.X * 1.2f);
			ChatBottom = y + size.Y * 1.2f;
			// Not through the panel, if it ever covers the button
			bool overPanel = IsOpen && state != null && state.ContainsMouse;
			bool over = !overPanel && Utils.Between(new Vector2(Main.mouseX, Main.mouseY), position, position + size * baseScale) && !PlayerInput.IgnoreMouseInterface;
			if (over != hovered)
			{
				hovered = over;
				SoundEngine.PlaySound(SoundID.MenuTick);
			}
			Vector2 scale = over ? baseScale * 1.2f : baseScale;
			if (over)
			{
				Main.LocalPlayer.mouseInterface = true;
				Main.LocalPlayer.releaseUseItem = false;
			}
			ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, font, label, position + size * 0.5f, chatColor, over ? Color.Brown : Color.Black, 0f, size * 0.5f, scale, -1f, 2f);
			if (over && Main.mouseLeft && Main.mouseLeftRelease)
			{
				Main.mouseLeftRelease = false;
				SoundEngine.PlaySound(SoundID.MenuOpen);
				if (IsOpen) Close();
				else
				{
					Open();
					HintShop.Say(CodexText.Get(HintFacts.Targets().Count > 0 ? "Hint.Intro" : "Hint.Nothing"));
				}
			}
		}
	}

	// The panel next to the Guide's dialog: targets (a shape until found), their bought level, the price of the next
	// level and Buy; below, everything the bought levels of the selected target reveal
	public class HintPanelState : UIState
	{
		private UIPanel panel;
		private UIText money;
		private CodexList targets;
		private CodexList detail;
		private int selected;
		// The tab shown: targets still to buy, or those fully known (bought to the end, or found)
		private bool knownTab;
		private CodexButton toBuyButton, knownButton;
		// The category shown: crafting (stations, pickaxes) or bosses (boss and event summons)
		private bool bossTab;
		private CodexButton craftingButton, bossesButton;
		private UIPanel searchPanel;
		private UISearchBar searchBar;
		private string search = "";
		private bool dirty = true;
		private (long structure, int pending, int tier, int caches, bool synced) built = (-1, -1, -1, -1, false);
		private int timer;
		private float panelWidth = PanelMaxWidth;
		public bool CloseRequested;

		private const float PanelMaxWidth = 520;
		private const float PanelMinWidth = 380;
		private const float Gap = 16;
		// Share of the panel's height for the targets (the bought hints below)
		private const float ListShare = 0.6f;

		public bool ContainsMouse => panel != null && panel.ContainsPoint(Main.MouseScreen);

		public bool IsTyping => searchBar != null && searchBar.IsWritingText;

		public void StopTyping()
		{
			if (IsTyping) searchBar.ToggleTakingText();
		}

		public override void OnInitialize()
		{
			panel = new UIPanel();
			panel.Width.Set(PanelMaxWidth, 0);
			panel.Height.Set(500, 0);
			panel.BackgroundColor = new Color(33, 43, 79) * 0.96f;
			Append(panel);

			UIText title = new(CodexText.Live("Hint.PanelTitle"), 0.9f);
			title.Top.Set(4, 0);
			panel.Append(title);
			money = new UIText("", 0.8f);
			money.Top.Set(30, 0);
			panel.Append(money);
			CodexButton close = new("X", 36, () => CloseRequested = true);
			close.Left.Set(-36, 1f);
			panel.Append(close);

			// The two tabs, then a smaller filter inside the tab
			craftingButton = TabButton("Hint.TabCrafting", () => ShowCategory(false), 0f, 0, -4, 0.5f, 54, 34, 0.9f);
			bossesButton = TabButton("Hint.TabBosses", () => ShowCategory(true), 4, 0.5f, -4, 0.5f, 54, 34, 0.9f);
			toBuyButton = TabButton("Hint.TabToBuy", () => ShowTab(false), -166, 0.5f, 162, 0f, 94, 24, 0.68f);
			knownButton = TabButton("Hint.TabKnown", () => ShowTab(true), 4, 0.5f, 162, 0f, 94, 24, 0.68f);

			// Same search box as the codex: click to type, right click to clear
			searchPanel = new UIPanel();
			searchPanel.Width.Set(0, 1f);
			searchPanel.Height.Set(30, 0);
			searchPanel.Top.Set(124, 0);
			searchPanel.SetPadding(0);
			searchPanel.BackgroundColor = new Color(25, 32, 66);
			panel.Append(searchPanel);
			searchBar = new UISearchBar(CodexText.Live("Search"), 0.8f);
			searchBar.Width.Set(-12, 1f);
			searchBar.Height.Set(0, 1f);
			searchBar.Left.Set(8, 0);
			searchBar.VAlign = 0.5f;
			searchBar.OnContentsChanged += SearchChanged;
			searchPanel.Append(searchBar);
			searchPanel.OnLeftClick += (_, _) =>
			{
				if (!searchBar.IsWritingText) searchBar.ToggleTakingText();
			};
			searchPanel.OnRightClick += (_, _) => searchBar.SetContents("", true);

			targets = MakeList(158, 0f, -162, ListShare);
			detail = MakeList(4, ListShare, -8, 1 - ListShare);
		}

		private CodexButton TabButton(string key, Action onClick, float leftPixels, float leftPercent, float widthPixels, float widthPercent, float top, float height, float scale)
		{
			CodexButton button = new(CodexText.Live(key), 0, onClick, scale);
			button.Width.Set(widthPixels, widthPercent);
			button.Left.Set(leftPixels, leftPercent);
			button.Top.Set(top, 0);
			button.Height.Set(height, 0);
			if (height < 30) button.SetPadding(2);
			panel.Append(button);
			return button;
		}

		private CodexList MakeList(float top, float topPercent, float height, float heightPercent)
		{
			CodexList list = new();
			list.Top.Set(top, topPercent);
			list.Width.Set(-26, 1f);
			list.Height.Set(height, heightPercent);
			list.ListPadding = 4;
			panel.Append(list);
			UIScrollbar bar = new();
			bar.Top.Set(top + 6, topPercent);
			bar.Height.Set(height - 12, heightPercent);
			bar.HAlign = 1f;
			panel.Append(bar);
			list.SetScrollbar(bar);
			return list;
		}

		// Right of the Guide's dialog when there is room, else below it; never over the dialog or its buttons
		private void Place()
		{
			float screenW = Main.screenWidth, screenH = Main.screenHeight;
			float left = HintSystem.ChatRight + Gap;
			float room = screenW - left - Gap;
			float x, y, w, h;
			if (room >= PanelMinWidth)
			{
				w = Math.Min(PanelMaxWidth, room);
				x = left;
				y = 100;
				h = Math.Min(620, screenH - y - Gap);
			}
			else
			{
				w = Math.Min(PanelMaxWidth, screenW - 2 * Gap);
				x = (screenW - w) / 2;
				y = HintSystem.ChatBottom + Gap;
				h = Math.Max(460, Math.Min(620, screenH - y - Gap));
			}
			if (panel.Left.Pixels == x && panel.Top.Pixels == y && panel.Width.Pixels == w && panel.Height.Pixels == h) return;
			panel.Left.Set(x, 0);
			panel.Top.Set(y, 0);
			panel.Width.Set(w, 0);
			panel.Height.Set(h, 0);
			Recalculate();
			if (Math.Abs(w - panelWidth) > 1)
			{
				panelWidth = w;
				dirty = true;
			}
		}

		public void Refresh()
		{
			dirty = true;
			CloseRequested = false;
		}

		private void SearchChanged(string contents)
		{
			string text = (contents ?? "").Trim();
			if (text == search) return;
			search = text;
			targets.ViewPosition = 0;
			dirty = true;
		}

		private bool Matches(HintTarget target) => search.Length == 0 || HintFacts.ItemName(target.Item).Contains(search, StringComparison.OrdinalIgnoreCase);

		public override void LeftMouseDown(UIMouseEvent evt)
		{
			base.LeftMouseDown(evt);
			// A click anywhere else ends the search input
			if (IsTyping && !searchPanel.ContainsPoint(evt.MousePosition)) searchBar.ToggleTakingText();
		}

		private void ShowCategory(bool bosses)
		{
			if (bossTab == bosses) return;
			bossTab = bosses;
			targets.ViewPosition = 0;
			dirty = true;
		}

		private void ShowTab(bool known)
		{
			if (knownTab == known) return;
			knownTab = known;
			targets.ViewPosition = 0;
			dirty = true;
		}

		// Boss and event summons in the bosses tab, stations and pickaxes in the crafting one
		public static List<HintTarget> TabTargets(bool known, bool bosses) => TabTargets(known).Where(t => (t.Kind == HintTargetKind.Summon) == bosses).ToList();

		// What a tab shows below the targets on sale: those that come later (to buy tab only), greyed and not sold, in the
		// order progression reaches them, so that the whole road is visible
		public static List<HintTarget> LockedTabTargets(bool bosses) =>
			HintFacts.ByProgress(HintFacts.LockedTargets().Where(t => (t.Kind == HintTargetKind.Summon) == bosses && CodexSystem.ShownHintLevel(t.Item) < HintFacts.MaxLevel));

		// The targets of a tab, in the order progression reaches them: to buy, those offered now and not fully known; known,
		// every target bought to the end or found
		public static List<HintTarget> TabTargets(bool known) => known
			? HintFacts.ByProgress(HintFacts.AllTargets().Where(t => CodexSystem.ShownHintLevel(t.Item) >= HintFacts.MaxLevel))
			: HintFacts.ByProgress(HintFacts.Targets().Where(t => CodexSystem.ShownHintLevel(t.Item) < HintFacts.MaxLevel));

		private (long, int, int, int, bool) Key() => (CodexSystem.StructureVersion, CodexSystem.PendingHintCount, ProgressionSystem.UnlockedTier, HintFacts.CacheVersion, CodexSystem.CanBuyHints);

		private void Build()
		{
			dirty = false;
			built = Key();
			float scroll = targets.ViewPosition;
			targets.Clear();
			List<UIElement> rows = new();
			toBuyButton.Active = !knownTab;
			knownButton.Active = knownTab;
			craftingButton.Active = !bossTab;
			bossesButton.Active = bossTab;
			List<HintTarget> all = TabTargets(knownTab, bossTab);
			List<HintTarget> list = all.Where(Matches).ToList();
			List<HintTarget> locked = knownTab ? new List<HintTarget>() : LockedTabTargets(bossTab).Where(Matches).ToList();
			if (!CodexSystem.CanBuyHints) rows.Add(new CodexRow(30, 4).WithLabel(CodexText.Get("Hint.Syncing"), 0.8f, CodexDraw.Muted));
			string empty = all.Count > 0 ? CodexText.Get("NoMatch")
				: TabTargets(knownTab, !bossTab).Count > 0 ? CodexText.Get("Hint.NothingHere")
				: CodexText.Get(knownTab ? "Hint.NothingKnown" : "Hint.Nothing");
			if (list.Count == 0) rows.Add(new CodexRow(30, 4).WithLabel(empty, 0.8f, CodexDraw.Muted));
			foreach (HintTarget target in list) rows.Add(TargetRow(target));
			if (locked.Count > 0)
			{
				rows.Add(new CodexSection(CodexText.Get("Hint.LockedTitle")));
				foreach (HintTarget target in locked) rows.Add(LockedRow(target));
			}
			targets.AddRange(rows);
			targets.Recalculate();
			targets.ViewPosition = scroll;
			BuildDetail();
		}

		private UIElement TargetRow(HintTarget target)
		{
			int item = target.Item;
			int level = CodexSystem.ShownHintLevel(item);
			bool found = CodexSystem.HasSeen(item);
			CodexRow row = new(44, 2) { Highlight = true };
			// The row shows its hints below; its icon leads to the codex
			row.OnLeftClick += (_, _) => Select(item, true);
			CodexItemIcon icon = new(item, 0, 40, () => HintSystem.OpenInCodex(item)) { Silhouette = !found, HoverText = CodexText.Get("Hint.OpenCodex", HintFacts.ItemName(item)) };
			row.Add(icon, 6);
			float nameWidth = panelWidth - 330;
			string name = HintFacts.ItemName(item);
			if (CodexDraw.TextWidth(name, 0.8f) > nameWidth)
			{
				while (name.Length > 4 && CodexDraw.TextWidth(name + "...", 0.8f) > nameWidth) name = name.Substring(0, name.Length - 1);
				name += "...";
			}
			row.Add(new CodexLabel(name, 0.8f, found ? new Color(140, 230, 140) : Color.White) { HoverText = found ? CodexText.Get("Hint.Found") : null });
			CodexLabel levelLabel = new(CodexText.Get("Hint.Level", level, HintFacts.MaxLevel), 0.75f, level > 0 ? CodexDraw.Gold : CodexDraw.Muted);
			levelLabel.Left.Set(-232, 1f);
			levelLabel.VAlign = 0.5f;
			row.Append(levelLabel);
			string text = level >= HintFacts.MaxLevel ? CodexText.Get("Hint.Repeat") : CodexText.Get("Hint.Buy", HintFacts.PriceText(HintFacts.Price(target, level + 1)));
			CodexButton buy = new(text, 160, () => Buy(target), 0.72f) { Enabled = CodexSystem.CanBuyHints || level >= HintFacts.MaxLevel };
			buy.Left.Set(-162, 1f);
			buy.VAlign = 0.5f;
			row.Append(buy);
			return row;
		}

		// A target that comes later: its icon and name greyed, when it will be offered, nothing to buy nor to read
		private UIElement LockedRow(HintTarget target)
		{
			// The row says when the item comes; the Guide, when he will talk about it (a tier earlier)
			string when = HintFacts.UnlockText(target);
			string comes = HintFacts.ComesText(target);
			string hover = CodexText.Get("Hint.LockedWhen", comes, when);
			CodexRow row = new(44, 2) { Highlight = true };
			row.OnLeftClick += (_, _) => HintShop.Say(CodexText.Get("Hint.LockedSay", HintFacts.ItemName(target.Item), when));
			row.Add(new CodexItemIcon(target.Item, 0, 40) { Silhouette = !CodexSystem.HasSeen(target.Item), HoverText = hover }, 6);
			float nameWidth = panelWidth - 330;
			string name = HintFacts.ItemName(target.Item);
			if (CodexDraw.TextWidth(name, 0.8f) > nameWidth)
			{
				while (name.Length > 4 && CodexDraw.TextWidth(name + "...", 0.8f) > nameWidth) name = name.Substring(0, name.Length - 1);
				name += "...";
			}
			row.Add(new CodexLabel(name, 0.8f, CodexDraw.Muted));
			CodexLabel unlock = new(comes, 0.72f, CodexDraw.Muted) { HoverText = hover };
			unlock.Left.Set(-232, 1f);
			unlock.VAlign = 0.5f;
			row.Append(unlock);
			return row;
		}

		private void Select(int item, bool speak)
		{
			// The click that opened the codex reaches the row too
			if (!HintSystem.IsOpen) return;
			selected = item;
			int level = CodexSystem.ShownHintLevel(item);
			HintFact fact = HintFacts.For(item);
			if (speak && fact != null && level > 0) HintShop.Say(HintFacts.Text(fact, level));
			BuildDetail();
		}

		private void Buy(HintTarget target)
		{
			HintFact fact = HintFacts.For(target.Item);
			if (fact == null) return;
			int level = CodexSystem.ShownHintLevel(target.Item);
			if (level >= HintFacts.MaxLevel)
			{
				Select(target.Item, true);
				return;
			}
			HintPurchase result = HintShop.TryBuy(Main.LocalPlayer, target, level + 1, out long price);
			switch (result)
			{
				case HintPurchase.Bought:
					SoundEngine.PlaySound(SoundID.Coins);
					HintShop.Say(HintFacts.Text(fact, level + 1));
					selected = target.Item;
					dirty = true;
					break;
				case HintPurchase.TooPoor:
					HintShop.Say(CodexText.Get("Hint.TooPoor", HintFacts.PriceText(price)));
					break;
				case HintPurchase.NotSynced:
					HintShop.Say(CodexText.Get("Hint.Syncing"));
					break;
				case HintPurchase.Locked:
					HintShop.Say(CodexText.Get("Hint.LockedSay", HintFacts.ItemName(target.Item), HintFacts.UnlockText(target)));
					dirty = true;
					break;
				default:
					HintShop.Say(CodexText.Get("Hint.Refused"));
					dirty = true;
					break;
			}
		}

		private void BuildDetail()
		{
			detail.Clear();
			List<UIElement> rows = new();
			HintFact fact = selected > 0 ? HintFacts.For(selected) : null;
			int level = selected > 0 ? CodexSystem.ShownHintLevel(selected) : 0;
			if (fact == null) rows.Add(new CodexRow(30, 4).WithLabel(CodexText.Get("Hint.PickTarget"), 0.8f, CodexDraw.Muted));
			else
			{
				rows.Add(new CodexSection(CodexText.Get("Hint.About", HintFacts.ItemName(selected))));
				int item = selected;
				CodexRow open = new(36, 4);
				open.Add(new CodexButton(CodexText.Get("Hint.SeeInCodex"), 220, () => HintSystem.OpenInCodex(item), 0.75f));
				rows.Add(open);
				if (level == 0) rows.Add(new CodexRow(30, 4).WithLabel(CodexText.Get("Hint.NoHint"), 0.8f, CodexDraw.Muted));
				rows.AddRange(HintView.Rows(fact, level, panelWidth - 40, inCodex: false));
			}
			detail.AddRange(rows);
			detail.Recalculate();
		}

		public override void Update(GameTime gameTime)
		{
			// A click outside the panel (the Guide's dialog, the world) also ends the search input: it holds the keyboard
			if (IsTyping && Main.mouseLeft && Main.mouseLeftRelease && !searchPanel.ContainsPoint(Main.MouseScreen)) StopTyping();
			if (++timer >= 20)
			{
				timer = 0;
				Player player = Main.LocalPlayer;
				long coins = Utils.CoinsCount(out _, player.inventory) + Utils.CoinsCount(out _, player.bank.item) + Utils.CoinsCount(out _, player.bank2.item)
					+ Utils.CoinsCount(out _, player.bank3.item) + Utils.CoinsCount(out _, player.bank4.item);
				money.SetText(CodexText.Get("Hint.Money", coins > 0 ? HintFacts.PriceText(coins) : "0"));
				bool pressed = (Main.mouseLeft || Main.mouseRight) && ContainsMouse;
				if (!pressed && built != Key()) dirty = true;
			}
			if (dirty) Build();
			base.Update(gameTime);
			if (ContainsMouse)
			{
				Main.LocalPlayer.mouseInterface = true;
				PlayerInput.LockVanillaMouseScroll("UltimateRandomizer/Hints");
			}
		}

		public override void Draw(SpriteBatch spriteBatch)
		{
			// Drawn in the same UI scale as the dialog: its edges from this frame place the panel
			Place();
			CodexHover.Clear();
			CodexBudget.NewFrame();
			base.Draw(spriteBatch);
			CodexHover.Draw();
		}
	}
}

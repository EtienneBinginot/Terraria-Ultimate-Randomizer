using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Journal;

namespace UltimateRandomizer.Common.Codex.Interface
{
	public enum CodexPageKind : byte { Enemies, Items, Crafts, Bags, Biomes, Npc, Item, Tree }

	public readonly struct CodexPage : IEquatable<CodexPage>
	{
		public readonly CodexPageKind Kind;
		public readonly int Id;

		public CodexPage(CodexPageKind kind, int id = 0)
		{
			Kind = kind;
			Id = id;
		}

		public static CodexPage NpcPage(int netId) => new(CodexPageKind.Npc, netId);

		public static CodexPage ItemPage(int type) => new(CodexPageKind.Item, type);

		public static CodexPage TreePage(int type) => new(CodexPageKind.Tree, type);

		// The tab a page belongs to
		public CodexPageKind Tab => Kind switch
		{
			CodexPageKind.Npc => CodexPageKind.Enemies,
			CodexPageKind.Item => CodexPageKind.Items,
			CodexPageKind.Tree => CodexPageKind.Crafts,
			_ => Kind
		};

		public bool IsTab => Kind <= CodexPageKind.Biomes;

		public bool Equals(CodexPage other) => Kind == other.Kind && Id == other.Id;

		public override bool Equals(object obj) => obj is CodexPage other && Equals(other);

		public override int GetHashCode() => ((int)Kind << 24) ^ Id;
	}

	// The codex (J): what was discovered in this world, as tabs of portraits and icons, and pages for each enemy and item.
	// Client only; it reads the codex synced by CodexSystem. Pages are built when shown, and rebuilt when the codex
	// changes (CodexSystem.Version) or the panel is resized, keeping the scroll position.
	public partial class CodexUIState : UIState
	{
		public static CodexUIState Instance { get; private set; }

		public sealed class HistoryEntry
		{
			public CodexPage Page;
			public float Scroll;
		}

		private const int MaxHistory = 60;
		private static readonly CodexPageKind[] Tabs = { CodexPageKind.Enemies, CodexPageKind.Items, CodexPageKind.Crafts, CodexPageKind.Bags, CodexPageKind.Biomes };

		private readonly List<HistoryEntry> history = new();
		private int historyIndex = -1;
		private bool dirty;
		private bool keepScroll;
		private long builtVersion = -1;
		private float builtWidth;
		private int checkTimer;
		private bool wasTyping;
		public bool CloseRequested;

		private UIPanel panel;
		private UIList list;
		private UIScrollbar scrollbar;
		private UIPanel searchPanel;
		private UISearchBar searchBar;
		private string search = "";
		private CodexButton backButton;
		private CodexButton forwardButton;
		private readonly CodexButton[] tabButtons = new CodexButton[Tabs.Length];

		public CodexPage Current => historyIndex >= 0 && historyIndex < history.Count ? history[historyIndex].Page : new CodexPage(CodexPageKind.Enemies);

		public bool IsTyping => searchBar != null && searchBar.IsWritingText;

		public static void Open(CodexPage page) => Instance?.Navigate(page);

		public override void OnInitialize()
		{
			Instance = this;
			panel = new UIPanel();
			panel.Width.Set(0, 0.92f);
			panel.MaxWidth.Set(1000, 0);
			panel.Height.Set(0, 0.86f);
			panel.MaxHeight.Set(640, 0);
			panel.HAlign = 0.5f;
			panel.VAlign = 0.5f;
			panel.BackgroundColor = new Color(33, 43, 79) * 0.96f;
			Append(panel);

			UIText title = new(Language.GetText("Mods.UltimateRandomizer.Codex.Title"), 0.75f, true);
			title.Top.Set(2, 0);
			panel.Append(title);

			backButton = new CodexButton("<", 36, Back);
			backButton.Left.Set(-370, 1f);
			panel.Append(backButton);
			forwardButton = new CodexButton(">", 36, Forward);
			forwardButton.Left.Set(-328, 1f);
			panel.Append(forwardButton);

			searchPanel = new UIPanel();
			searchPanel.Width.Set(234, 0);
			searchPanel.Height.Set(32, 0);
			searchPanel.Left.Set(-284, 1f);
			searchPanel.SetPadding(0);
			searchPanel.BackgroundColor = new Color(25, 32, 66);
			panel.Append(searchPanel);
			searchBar = new UISearchBar(Language.GetText("Mods.UltimateRandomizer.Codex.Search"), 0.8f);
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

			CodexButton close = new("X", 40, () => CloseRequested = true);
			close.Left.Set(-40, 1f);
			panel.Append(close);

			for (int i = 0; i < Tabs.Length; i++)
			{
				CodexPageKind tab = Tabs[i];
				CodexButton button = new(Language.GetTextValue($"Mods.UltimateRandomizer.Codex.Tab{tab}"), 0, () => OpenTab(tab));
				button.Width.Set(-6, 0.2f);
				button.Left.Set(0, i * 0.2f);
				button.Top.Set(40, 0);
				button.Height.Set(36, 0);
				panel.Append(button);
				tabButtons[i] = button;
			}

			list = new UIList();
			list.Top.Set(84, 0);
			list.Width.Set(-26, 1f);
			list.Height.Set(-84, 1f);
			list.ListPadding = 6;
			// Rows stay in the order they were added
			list.ManualSortMethod = _ => { };
			panel.Append(list);

			scrollbar = new UIScrollbar();
			scrollbar.Top.Set(90, 0);
			scrollbar.Height.Set(-96, 1f);
			scrollbar.HAlign = 1f;
			panel.Append(scrollbar);
			list.SetScrollbar(scrollbar);
		}

		// Called when the codex opens: the first time on the enemies, then where it was left
		public void Refresh()
		{
			if (history.Count == 0) Navigate(new CodexPage(CodexPageKind.Enemies));
			else MarkDirty(true);
		}

		public void OnClosed()
		{
			if (searchBar != null && searchBar.IsWritingText) searchBar.ToggleTakingText();
			CloseRequested = false;
		}

		private void SearchChanged(string contents)
		{
			string text = (contents ?? "").Trim();
			if (text == search) return;
			search = text;
			// The search filters a tab: from a page, it goes back to the page's tab
			if (!Current.IsTab) Navigate(new CodexPage(Current.Tab));
			else MarkDirty(false);
		}

		private void OpenTab(CodexPageKind tab)
		{
			if (search.Length > 0) searchBar.SetContents("", true);
			Navigate(new CodexPage(tab));
		}

		public void Navigate(CodexPage page)
		{
			if (historyIndex >= 0 && historyIndex < history.Count)
			{
				if (history[historyIndex].Page.Equals(page))
				{
					MarkDirty(true);
					return;
				}
				history[historyIndex].Scroll = list?.ViewPosition ?? 0;
				history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1);
			}
			history.Add(new HistoryEntry { Page = page });
			if (history.Count > MaxHistory) history.RemoveAt(0);
			historyIndex = history.Count - 1;
			if (page.Kind == CodexPageKind.Tree) ResetTree(page.Id);
			MarkDirty(false);
		}

		private void Back() => Move(-1);

		private void Forward() => Move(1);

		private void Move(int step)
		{
			int target = historyIndex + step;
			if (target < 0 || target >= history.Count) return;
			history[historyIndex].Scroll = list?.ViewPosition ?? 0;
			historyIndex = target;
			if (Current.Kind == CodexPageKind.Tree) ResetTree(Current.Id);
			MarkDirty(false);
			restoreScroll = history[historyIndex].Scroll;
		}

		private float? restoreScroll;

		private void MarkDirty(bool keep)
		{
			// A rebuild asked to keep the scroll must not override one that resets it
			keepScroll = dirty ? keepScroll && keep : keep;
			dirty = true;
		}

		private float ListWidth
		{
			get
			{
				float width = list?.GetInnerDimensions().Width ?? 0;
				return width > 100 ? width : 900;
			}
		}

		// Builds the current page now, catching nothing (the tests call it)
		public void BuildNow()
		{
			dirty = false;
			builtVersion = CodexSystem.Version;
			builtWidth = ListWidth;
			list.Clear();
			Build(Current);
			list.Recalculate();
			UpdateChrome();
		}

		private void Rebuild()
		{
			if (list == null) return;
			float scroll = restoreScroll ?? (keepScroll ? list.ViewPosition : 0);
			restoreScroll = null;
			try
			{
				BuildNow();
			}
			catch (Exception e)
			{
				ModContent.GetInstance<UltimateRandomizer>().Logger.Error($"Codex: page {Current.Kind} {Current.Id} failed", e);
				list.Clear();
				list.Add(new JournalRow($"[c/FF6060:{Language.GetTextValue("Mods.UltimateRandomizer.Codex.Error")}]"));
				list.Recalculate();
			}
			list.ViewPosition = scroll;
		}

		private void UpdateChrome()
		{
			CodexPageKind tab = Current.Tab;
			for (int i = 0; i < Tabs.Length; i++) tabButtons[i].Active = Tabs[i] == tab;
			backButton.Enabled = historyIndex > 0;
			forwardButton.Enabled = historyIndex < history.Count - 1;
		}

		// Tests: pages built off screen, then the history put back
		public (List<HistoryEntry> history, int index, string search) SaveNavigation() => (new List<HistoryEntry>(history), historyIndex, search);

		public void RestoreNavigation((List<HistoryEntry> history, int index, string search) saved)
		{
			history.Clear();
			history.AddRange(saved.history);
			historyIndex = saved.index;
			search = saved.search;
			MarkDirty(true);
		}

		public UIList Content => list;

		public override void Update(GameTime gameTime)
		{
			if (++checkTimer >= 20)
			{
				checkTimer = 0;
				if (builtVersion != CodexSystem.Version || Math.Abs(builtWidth - ListWidth) > 1) MarkDirty(true);
			}
			if (dirty) Rebuild();
			base.Update(gameTime);
			if (panel.ContainsPoint(Main.MouseScreen))
			{
				Main.LocalPlayer.mouseInterface = true;
				PlayerInput.LockVanillaMouseScroll("UltimateRandomizer/Codex");
			}
			// Escape closes the codex, unless it just stopped the search input
			bool escape = Main.keyState.IsKeyDown(Keys.Escape) && !Main.oldKeyState.IsKeyDown(Keys.Escape);
			if (escape && !IsTyping && !wasTyping) CloseRequested = true;
			wasTyping = IsTyping;
		}

		public override void LeftMouseDown(UIMouseEvent evt)
		{
			base.LeftMouseDown(evt);
			// A click anywhere else ends the search input
			if (IsTyping && !searchPanel.ContainsPoint(evt.MousePosition)) searchBar.ToggleTakingText();
		}

		public override void Draw(SpriteBatch spriteBatch)
		{
			CodexHover.Clear();
			base.Draw(spriteBatch);
			CodexHover.Draw();
		}

		// ---- Small caches, valid for one codex version ----

		private long cacheVersion = -1;
		private readonly Dictionary<string, object> cache = new();

		private T Cached<T>(string key, Func<T> make) where T : class
		{
			if (cacheVersion != CodexSystem.Version)
			{
				cache.Clear();
				cacheVersion = CodexSystem.Version;
			}
			if (cache.TryGetValue(key, out object value) && value is T typed) return typed;
			T made = make();
			cache[key] = made;
			return made;
		}

		private bool Matches(string name) => search.Length == 0 || (name != null && name.Contains(search, StringComparison.OrdinalIgnoreCase));

		private static bool Ready => SeedSystem.WorldReady && ItemPool.Ready;
	}
}

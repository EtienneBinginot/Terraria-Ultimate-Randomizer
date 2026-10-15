using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI;
using Terraria.UI.Chat;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Journal
{
	// Lists the drops of the monsters already killed and the content of the bags already opened, in this world
	public class JournalUIState : UIState
	{
		private enum Tab { Monsters, Bags }

		private Tab tab = Tab.Monsters;
		private UIPanel panel;
		private UIList list;

		public override void OnInitialize()
		{
			panel = new UIPanel();
			panel.Width.Set(720, 0);
			panel.Height.Set(520, 0);
			panel.HAlign = 0.5f;
			panel.VAlign = 0.5f;
			Append(panel);

			UIText title = new(Language.GetText("Mods.UltimateRandomizer.Journal.Title"), 0.9f, true);
			title.HAlign = 0.5f;
			panel.Append(title);

			AddButton("Mods.UltimateRandomizer.Journal.Monsters", 0, () => SwitchTab(Tab.Monsters));
			AddButton("Mods.UltimateRandomizer.Journal.Bags", 160, () => SwitchTab(Tab.Bags));
			UITextPanel<string> close = new("X");
			close.Width.Set(40, 0);
			close.Left.Set(-40, 1f);
			close.OnLeftClick += (_, _) => JournalSystem.Toggle();
			panel.Append(close);

			list = new UIList();
			list.Top.Set(80, 0);
			list.Width.Set(-26, 1f);
			list.Height.Set(-80, 1f);
			list.ListPadding = 6;
			panel.Append(list);

			UIScrollbar scrollbar = new();
			scrollbar.Top.Set(80, 0);
			scrollbar.Height.Set(-80, 1f);
			scrollbar.HAlign = 1f;
			panel.Append(scrollbar);
			list.SetScrollbar(scrollbar);
		}

		private void AddButton(string key, float left, Action onClick)
		{
			UITextPanel<LocalizedText> button = new(Language.GetText(key));
			button.Top.Set(36, 0);
			button.Left.Set(left, 0);
			button.Width.Set(150, 0);
			button.OnLeftClick += (_, _) => onClick();
			panel.Append(button);
		}

		private void SwitchTab(Tab newTab)
		{
			tab = newTab;
			Refresh();
		}

		public void Refresh()
		{
			if (list == null) return;
			list.Clear();
			if (!SeedSystem.WorldReady || !ItemPool.Ready) return;
			List<JournalRow> rows = tab == Tab.Monsters ? MonsterRows() : BagRows();
			if (rows.Count == 0) rows.Add(new JournalRow(Language.GetTextValue("Mods.UltimateRandomizer.Journal.Empty")));
			foreach (JournalRow row in rows) list.Add(row);
			list.Recalculate();
		}

		private static List<JournalRow> MonsterRows()
		{
			List<JournalRow> rows = new();
			foreach (int type in LootSnapshot.Npcs.Keys.OrderBy(t => Lang.GetNPCNameValue(t)))
			{
				if (!ContentSamples.NpcsByNetId.TryGetValue(type, out NPC sample) || Main.BestiaryTracker.Kills.GetKillCount(sample) <= 0) continue;
				List<PlannedDrop> drops = RandomNpcDropRule.Plan(type, Main.expertMode);
				if (drops.Count == 0) continue;
				rows.Add(new JournalRow(Header(Lang.GetNPCNameValue(type), NpcTiers.BaseTier(type)) + "\n" + DropsText(drops)));
			}
			return rows;
		}

		private static List<JournalRow> BagRows()
		{
			List<JournalRow> rows = new();
			JournalPlayer journal = Main.LocalPlayer.GetModPlayer<JournalPlayer>();
			foreach (int type in LootSnapshot.Items.Keys.OrderBy(t => Lang.GetItemNameValue(t)))
			{
				if (!journal.HasOpened(type)) continue;
				rows.Add(new JournalRow($"[i:{type}] " + Header(Lang.GetItemNameValue(type), LootSnapshot.BagTier(type)) + "\n" + DropsText(RandomBagRule.Plan(type))));
			}
			return rows;
		}

		private static string Header(string name, int tier) => $"[c/FFD966:{name}] ({Language.GetTextValue("Mods.UltimateRandomizer.Journal.Tier", tier)})";

		private static string DropsText(List<PlannedDrop> drops)
		{
			StringBuilder sb = new();
			int onLine = 0;
			foreach (PlannedDrop drop in drops)
			{
				if (drop.Item <= ItemID.None) continue;
				if (onLine == 5)
				{
					sb.Append('\n');
					onLine = 0;
				}
				string stack = drop.StackMax <= 1 ? "" : drop.StackMin == drop.StackMax ? $"x{drop.StackMin}" : $"x{drop.StackMin}-{drop.StackMax}";
				string chance = drop.Chance >= 1f ? "100%" : $"1/{Math.Max(1, Math.Round(1 / drop.Chance))}";
				sb.Append($"[i:{drop.Item}]{stack} {chance}   ");
				onLine++;
			}
			return sb.ToString();
		}

		public override void Update(GameTime gameTime)
		{
			base.Update(gameTime);
			// Clicks on the journal must not use the held item
			if (panel != null && panel.ContainsPoint(Main.MouseScreen)) Main.LocalPlayer.mouseInterface = true;
		}
	}

	// A text line that draws item icons ([i:id] tags) and shows their tooltip on hover
	public class JournalRow : UIElement
	{
		private readonly TextSnippet[] snippets;

		public JournalRow(string text)
		{
			snippets = ChatManager.ParseMessage(text, Color.White).ToArray();
			ChatManager.ConvertNormalSnippets(snippets);
			int lines = text.Count(c => c == '\n') + 1;
			Width.Set(0, 1f);
			Height.Set(lines * 30 + 4, 0);
		}

		protected override void DrawSelf(SpriteBatch spriteBatch)
		{
			CalculatedStyle dims = GetDimensions();
			ChatManager.DrawColorCodedStringWithShadow(spriteBatch, FontAssets.MouseText.Value, snippets, new Vector2(dims.X + 4, dims.Y + 4),
				0f, Vector2.Zero, Vector2.One, out int hovered, dims.Width - 8, 2f);
			if (hovered >= 0 && IsMouseHovering) snippets[hovered].OnHover();
		}
	}
}

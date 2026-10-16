using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;
using UltimateRandomizer.Common.Journal;
using UltimateRandomizer.Common.Tests;

namespace UltimateRandomizer.Common.Debug
{
	// Debug panel (K): overview of the randomizer state, live inspector, testing tools and the automated tests
	public class DebugUIState : UIState
	{
		private enum Tab { Overview, Inspector, Tools, Tests }

		private Tab tab = Tab.Overview;
		private UIPanel panel;
		private UIList list;
		private int refreshTimer;

		private static string T(string key, params object[] args) => DebugTools.Say(key, args);

		private static string OnOff(bool value) => value ? $"[c/7CFC00:{T("On")}]" : $"[c/FF6060:{T("Off")}]";

		public override void OnInitialize()
		{
			panel = new UIPanel();
			panel.Width.Set(780, 0);
			panel.Height.Set(600, 0);
			panel.HAlign = 0.5f;
			panel.VAlign = 0.5f;
			Append(panel);

			UIText title = new(Language.GetText("Mods.UltimateRandomizer.Debug.Title"), 0.85f, true) { HAlign = 0.5f };
			panel.Append(title);

			string[] tabs = { "TabOverview", "TabInspector", "TabTools", "TabTests" };
			for (int i = 0; i < tabs.Length; i++)
			{
				Tab target = (Tab)i;
				UITextPanel<LocalizedText> button = new(Language.GetText("Mods.UltimateRandomizer.Debug." + tabs[i]));
				button.Top.Set(36, 0);
				button.Left.Set(i * 170, 0);
				button.Width.Set(160, 0);
				button.OnLeftClick += (_, _) =>
				{
					tab = target;
					Rebuild();
				};
				panel.Append(button);
			}
			UITextPanel<string> close = new("X");
			close.Width.Set(40, 0);
			close.Left.Set(-40, 1f);
			close.OnLeftClick += (_, _) => DebugSystem.Toggle();
			panel.Append(close);

			list = new UIList();
			list.Top.Set(82, 0);
			list.Width.Set(-26, 1f);
			list.Height.Set(-82, 1f);
			list.ListPadding = 6;
			panel.Append(list);

			UIScrollbar scrollbar = new();
			scrollbar.Top.Set(82, 0);
			scrollbar.Height.Set(-82, 1f);
			scrollbar.HAlign = 1f;
			panel.Append(scrollbar);
			list.SetScrollbar(scrollbar);
		}

		public void Rebuild()
		{
			if (list == null) return;
			float scroll = list.ViewPosition;
			list.Clear();
			if (!SeedSystem.WorldReady || !ItemPool.Ready)
			{
				list.Add(new JournalRow(T("NeedWorld")));
				return;
			}
			switch (tab)
			{
				case Tab.Overview: BuildOverview(); break;
				case Tab.Inspector: BuildInspector(); break;
				case Tab.Tools: BuildTools(); break;
				case Tab.Tests: BuildTests(); break;
			}
			list.Recalculate();
			list.ViewPosition = scroll;
		}

		private void Text(string text) => list.Add(new JournalRow(text));

		private void Buttons(params (string label, Action action)[] buttons) => list.Add(new ButtonRow(buttons, Rebuild));

		private void BuildOverview()
		{
			GeneralConfig general = GeneralConfig.Instance;
			ProgressionConfig progression = ProgressionConfig.Instance;
			int unlocked = ProgressionSystem.UnlockedTier;
			int limit = progression.TierGating ? Tiers.Clamp(unlocked + progression.TierAllowance) : Tiers.Max;
			int randomizable = Enumerable.Range(1, ItemPool.Info.Length - 1).Count(ItemPool.IsRandomizable);
			int protectedCount = ItemPool.Info.Count(i => i.Protected);
			Text($"[c/FFD966:{T("Seed")}] {SeedSystem.WorldSeed}   [c/FFD966:{T("Mode")}] {general.RandomizerMode}   [c/FFD966:{T("Network")}] {(Main.netMode == NetmodeID.SinglePlayer ? T("SinglePlayer") : T("Multiplayer"))}");
			Text($"[c/FFD966:{T("Tier")}] {unlocked} ({(Tier)unlocked})   [c/FFD966:{T("Limit")}] {limit}   [c/FFD966:{T("Gating")}] {OnOff(progression.TierGating)}");
			Text($"[c/FFD966:{T("Pool")}] {randomizable} / {protectedCount} / {ItemPool.Info.Count(i => i.Excluded)}   [c/FFD966:{T("Snapshots")}] {LootSnapshot.Npcs.Count} / {LootSnapshot.Items.Count}");
			Text($"[c/FFD966:{T("Recipes")}] {OnOff(RecipeSystem.Applied)}   [c/FFD966:{T("Shimmer")}] {OnOff(ShimmerSystem.Applied)}   [c/FFD966:{T("Chests")}] {OnOff(ChestSystem.ChestsRandomized)}");
			SwapStats swaps = EnemySwapSystem.Stats;
			Text($"[c/FFD966:{T("Swaps")}] {T("SwapStats", swaps.Flagged, swaps.SwappedEnemies, swaps.SwappedCritters, swaps.NotInPool, swaps.ChanceFailed, swaps.NoTarget)}");
			Text($"[c/FFD966:{T("SetTier")}]");
			Buttons(Enumerable.Range(0, 5).Select(t => ($"{t}", (Action)(() => DebugTools.SetTier(t)))).ToArray());
			Buttons(Enumerable.Range(5, 5).Select(t => ($"{t}", (Action)(() => DebugTools.SetTier(t)))).ToArray());
			Buttons((T("NewSeed"), DebugTools.NewSeed));
			Text($"[c/FFD966:{T("PoolTable")}]");
			for (int c = 0; c < ItemCategories.Count; c++)
			{
				StringBuilder sb = new($"{(ItemCategory)c}: ");
				for (int t = 0; t < Tiers.Count; t++) sb.Append($"{ItemPool.BucketSize((ItemCategory)c, t)} ");
				Text(sb.ToString());
			}
		}

		private void BuildInspector()
		{
			Item item = Main.HoverItem != null && !Main.HoverItem.IsAir ? Main.HoverItem : Main.LocalPlayer.HeldItem;
			Text($"[c/FFD966:{T("HoverItem")}]");
			if (item == null || item.IsAir || !ItemPool.IsValid(item.type))
			{
				Text(T("NoItem"));
			}
			else
			{
				ItemInfo info = ItemPool.Info[item.type];
				(int min, int max) = StackRules.Range(item.type);
				Text($"[i:{item.type}] {Lang.GetItemNameValue(item.type)} (#{item.type}, {DetRandom.ItemName(item.type)})");
				Text(T("ItemInfo", info.Category, info.Tier, (Tier)info.Tier, OnOff(info.Protected), OnOff(info.Excluded), min, max, StackRules.ValueOf(item.type)));
				if (ItemPool.IsRandomizable(item.type)) Text(T("ShufflePartner", $"[i:{ItemPool.Shuffled(item.type)}]"));
				int found = Findability.Tier(item.type);
				int available = RecipeSystem.Applied && item.type < RecipePlanner.Get().Availability.Length ? RecipePlanner.Get().Availability[item.type] : found;
				Text(T("Findable", found == Findability.Never ? "-" : found.ToString(), available == Findability.Never ? "-" : available.ToString()));
				List<int> hosts = GuaranteedSources.HostsOf(item.type).ToList();
				if (hosts.Count > 0) Text(T("GuaranteedOn", string.Join(", ", hosts.Select(h => ContentSamples.NpcsByNetId[h].TypeName))));
			}

			NPC npc = NearestNpc();
			Text($"[c/FFD966:{T("NearestNpc")}]");
			if (npc == null)
			{
				Text(T("NoNpc"));
				return;
			}
			EnemyMultipliers m = EnemyStatCache.Get(BossParts.Owner(npc.type), EnemyStatsNPC.IsBossLike(npc));
			Text($"{npc.TypeName} (#{npc.type}/{npc.netID})   {T("NpcTier", NpcTiers.BaseTier(npc.type), NpcTiers.EffectiveTier(npc.type))}   {npc.life}/{npc.lifeMax} HP, {npc.damage} dmg, {npc.defense} def");
			Text(T("Multipliers", m.Life.ToString("0.00"), m.Damage.ToString("0.00"), m.Defense.ToString("0.00"), m.Scale.ToString("0.00")));
			if (npc.TryGetGlobalNPC(out EnemyIndividualNPC individual) && individual.Rolled)
			{
				Text(T("Individual", T("Kind" + individual.Kind), individual.Life.ToString("0.00"), individual.Damage.ToString("0.00"), individual.Defense.ToString("0.00"), individual.Size.ToString("0.00"), EnemyIndividualNPC.LootBonus(npc).ToString("0.00")));
			}
			string swap = EnemySwapPool.IsSwappable(npc.type) ? T("Swappable") : T("NotSwappable");
			Text(swap);
			Text($"[c/FFD966:{T("Drops")}]");
			List<PlannedDrop> drops = RandomNpcDropRule.Plan(npc.netID, Main.expertMode);
			if (drops.Count == 0) Text(T("NoDrops"));
			foreach (PlannedDrop drop in drops)
			{
				string chance = drop.Chance >= 1f ? "100%" : $"1/{Math.Max(1, Math.Round(1 / drop.Chance))}";
				string stack = drop.StackMin == drop.StackMax ? $"{drop.StackMin}" : $"{drop.StackMin}-{drop.StackMax}";
				string tag = drop.Guaranteed ? T("GuaranteedTag") : drop.Extra ? T("ExtraTag") : drop.Conditions != null && drop.Conditions.Count > 0 ? T("ConditionTag") : "";
				Text($"[i:{drop.Item}] {Lang.GetItemNameValue(drop.Item)} x{stack}  {chance}  {tag}");
			}
		}

		private static NPC NearestNpc()
		{
			NPC best = null;
			float bestDistance = 600f * 600f;
			for (int i = 0; i < Main.maxNPCs; i++)
			{
				NPC npc = Main.npc[i];
				if (!npc.active) continue;
				float distance = Vector2.DistanceSquared(npc.Center, Main.MouseWorld);
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = npc;
				}
			}
			return best;
		}

		private void BuildTools()
		{
			Text($"[c/FFD966:{T("Player")}]   {T("GodMode")}: {OnOff(DebugTools.GodMode)}   {T("SpawnRate")}: {T("SpawnRate" + DebugTools.SpawnRate)}");
			Buttons((T("ToggleGodMode"), DebugTools.ToggleGodMode), (T("CycleSpawnRate"), DebugTools.CycleSpawnRate), (T("GiveKit"), DebugTools.GiveTestKit));
			Text($"[c/FFD966:{T("World")}]   {(Main.dayTime ? T("Day") : T("Night"))}");
			Buttons((T("SpawnEnemies"), () => DebugTools.SpawnEnemies(5)), (T("KillHostiles"), DebugTools.KillHostiles), (T("ToggleDay"), DebugTools.ToggleDay));
			Buttons((T("GiveBags"), DebugTools.GiveBossBags), (T("OpenJournal"), DebugTools.OpenJournal), (T("NewSeed"), DebugTools.NewSeed));
			if (!DebugTools.CanChangeWorld(out string reason)) Text($"[c/FF6060:{reason}]");
			Text(T("ToolsHelp"));
		}

		private void BuildTests()
		{
			Buttons((T("RunTests"), () => TestRunner.RunAll()));
			List<TestResult> results = TestRunner.LastResults;
			if (results.Count == 0)
			{
				Text(T("TestsHelp"));
				return;
			}
			int passed = results.Count(r => r.Outcome == TestOutcome.Passed);
			int failed = results.Count(r => r.Outcome == TestOutcome.Failed);
			Text(T("TestSummary", passed, failed, results.Count - passed - failed));
			foreach (TestResult result in results.OrderBy(r => r.Outcome == TestOutcome.Failed ? 0 : r.Outcome == TestOutcome.Skipped ? 1 : 2))
			{
				string mark = result.Outcome switch
				{
					TestOutcome.Passed => "[c/7CFC00:OK]",
					TestOutcome.Failed => "[c/FF6060:KO]",
					_ => "[c/AAAAAA:--]"
				};
				string detail = string.IsNullOrEmpty(result.Detail) ? "" : "\n    " + Sanitize(result.Detail);
				Text($"{mark} {result.Name} ({result.Milliseconds} ms){detail}");
			}
		}

		// Brackets would be read as chat tags
		private static string Sanitize(string text) => text.Replace("[", "(").Replace("]", ")");

		public override void Update(GameTime gameTime)
		{
			base.Update(gameTime);
			if (panel != null && panel.ContainsPoint(Main.MouseScreen))
			{
				Main.LocalPlayer.mouseInterface = true;
				PlayerInput.LockVanillaMouseScroll("UltimateRandomizer/Debug");
			}
			// The inspector follows the mouse and the overview the world state
			if ((tab == Tab.Inspector || tab == Tab.Overview) && ++refreshTimer >= 20)
			{
				refreshTimer = 0;
				Rebuild();
			}
		}
	}

	// A row of buttons; the panel is rebuilt after each click so the displayed state is up to date
	public class ButtonRow : UIElement
	{
		public ButtonRow((string label, Action action)[] buttons, Action afterClick)
		{
			Width.Set(0, 1f);
			Height.Set(40, 0);
			float left = 0;
			foreach ((string label, Action action) in buttons)
			{
				UITextPanel<string> button = new(label, 0.9f);
				float width = Math.Max(60, label.Length * 9 + 24);
				button.Width.Set(width, 0);
				button.Left.Set(left, 0);
				button.OnLeftClick += (_, _) =>
				{
					action();
					afterClick();
				};
				Append(button);
				left += width + 6;
			}
		}
	}
}

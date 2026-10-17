using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;
using UltimateRandomizer.Common.Codex;
using UltimateRandomizer.Common.Codex.Interface;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;
using UltimateRandomizer.Common.Journal;

namespace UltimateRandomizer.Common.Tests
{
	// The codex interface: every page builds, and nothing undiscovered is shown. Pages are built off screen and the
	// codex navigation (and data) put back afterwards.
	public static class CodexUiTests
	{
		public static void Register()
		{
			TestRunner.Register("CodexUiBuilds", CodexUiBuilds);
			TestRunner.Register("CodexHidesUndiscovered", CodexHidesUndiscovered);
			TestRunner.Register("CodexManyItemsBounded", CodexManyItemsBounded);
			TestRunner.Register("CodexTextsResolved", CodexTextsResolved);
		}

		private static IEnumerable<UIElement> Walk(UIElement element)
		{
			foreach (UIElement child in element.Children)
			{
				yield return child;
				foreach (UIElement inner in Walk(child)) yield return inner;
			}
		}

		// Every row of the page, shown or not, with its cells made (the list only keeps the visible rows as children)
		private static IEnumerable<UIElement> WalkAll(CodexList list)
		{
			foreach (UIElement row in list.Items)
			{
				(row as ICodexLazy)?.Materialize();
				yield return row;
				foreach (UIElement inner in Walk(row)) yield return inner;
			}
		}

		private static CodexUIState StateOrSkip()
		{
			CodexUIState state = JournalSystem.State;
			if (state == null || state.Content == null) Check.Skip("no codex interface (server)");
			return state;
		}

		private static void Build(CodexUIState state, CodexPage page, List<string> failures)
		{
			try
			{
				state.Navigate(page);
				state.BuildNow();
				if (state.Content.Count == 0) failures.Add($"{page.Kind} {page.Id}: empty page");
			}
			catch (Exception e)
			{
				failures.Add($"{page.Kind} {page.Id}: {e.GetType().Name} {e.Message}");
			}
		}

		// Every tab, 20 item pages, a few enemy pages and a craft tree build without an exception
		private static void CodexUiBuilds()
		{
			CodexUIState state = StateOrSkip();
			var saved = state.SaveNavigation();
			List<string> failures = new();
			try
			{
				foreach (CodexPageKind tab in new[] { CodexPageKind.Enemies, CodexPageKind.Items, CodexPageKind.Crafts, CodexPageKind.Bags, CodexPageKind.Biomes, CodexPageKind.Hints })
				{
					Build(state, new CodexPage(tab), failures);
				}
				// Seen items first, then items spread over the whole list (not random: the same ones every run)
				List<int> items = CodexSystem.ItemsSeen().Take(10).ToList();
				for (int i = 0; items.Count < 20; i++) items.Add(1 + (i * 977 + 31) % (ItemLoader.ItemCount - 1));
				foreach (int item in items) Build(state, CodexPage.ItemPage(item), failures);
				List<int> npcs = CodexSystem.KilledNpcs().Take(5).Append(NPCID.BlueSlime).Append(NPCID.EyeofCthulhu).ToList();
				foreach (int npc in npcs) Build(state, CodexPage.NpcPage(npc), failures);
				int craftable = CraftableItem(out _);
				if (craftable > 0) Build(state, CodexPage.TreePage(craftable), failures);

				// Texts built from enum names
				foreach (CodexSource kind in Enum.GetValues<CodexSource>())
				{
					if (!Language.Exists("Mods.UltimateRandomizer.Codex.Source." + kind)) failures.Add("missing text Codex.Source." + kind);
				}
				for (int c = 0; c < ItemCategories.Count; c++)
				{
					if (!Language.Exists("Mods.UltimateRandomizer.Codex.Category." + (ItemCategory)c)) failures.Add("missing text Codex.Category." + (ItemCategory)c);
				}
				foreach (CodexPageKind tab in new[] { CodexPageKind.Enemies, CodexPageKind.Items, CodexPageKind.Crafts, CodexPageKind.Bags, CodexPageKind.Biomes, CodexPageKind.Hints })
				{
					if (!Language.Exists($"Mods.UltimateRandomizer.Codex.Tab{tab}")) failures.Add("missing text Codex.Tab" + tab);
				}
			}
			finally
			{
				state.RestoreNavigation(saved);
			}
			Check.All(failures, "Codex pages failing");
		}

		// An item with at least two recipes (a discovered one first if possible); the second one is returned too
		private static int CraftableItem(out (int first, int second) recipes)
		{
			recipes = (-1, -1);
			Dictionary<int, List<int>> byResult = new();
			int count = Math.Min(Recipe.numRecipes, RecipeSystem.RecipeCount);
			for (int i = 0; i < count; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (recipe.Disabled || recipe.createItem.IsAir) continue;
				if (!byResult.TryGetValue(recipe.createItem.type, out List<int> list)) byResult[recipe.createItem.type] = list = new List<int>();
				list.Add(i);
			}
			foreach ((int item, List<int> list) in byResult)
			{
				if (list.Count < 2) continue;
				recipes = (list[0], list[1]);
				return item;
			}
			return byResult.Count > 0 ? byResult.Keys.First() : 0;
		}

		// With only one of an item's recipes discovered, its page shows that recipe and not the other one, the enemy
		// tab shows no enemy (nothing killed) and the items tab only the seen item
		private static void CodexHidesUndiscovered()
		{
			CodexUIState state = StateOrSkip();
			int item = CraftableItem(out (int first, int second) recipes);
			if (recipes.second < 0) Check.Skip("no item with two recipes");
			CodexData savedData = CodexSystem.Data;
			var saved = state.SaveNavigation();
			List<string> failures = new();
			try
			{
				CodexData fresh = new() { Seed = SeedSystem.WorldSeed, HasSeed = true };
				fresh.Apply(CodexEntry.Recipe(RecipeSystem.RecipeKey(recipes.first)));
				fresh.Apply(CodexEntry.Seen(CodexNames.Item(item)));
				CodexSystem.Data = fresh;

				state.ClearFilters();
				state.Navigate(CodexPage.ItemPage(item));
				state.BuildNow();
				List<int> shown = WalkAll(state.Content).OfType<CodexRecipeRow>().Select(r => r.RecipeIndex).Distinct().ToList();
				if (!shown.Contains(recipes.first)) failures.Add($"discovered recipe {recipes.first} not shown");
				foreach (int recipe in shown.Where(r => !CodexSystem.IsRecipeDiscovered(r))) failures.Add($"undiscovered recipe {recipe} shown");

				state.Navigate(new CodexPage(CodexPageKind.Enemies));
				state.BuildNow();
				int portraits = WalkAll(state.Content).OfType<CodexNpcPortrait>().Count();
				if (portraits > 0) failures.Add($"{portraits} enemies shown with no kill");

				state.Navigate(new CodexPage(CodexPageKind.Items));
				state.BuildNow();
				foreach (CodexItemIcon icon in WalkAll(state.Content).OfType<CodexItemIcon>().Where(i => i.Type != item)) failures.Add($"unseen item {icon.Type} shown");

				state.Navigate(new CodexPage(CodexPageKind.Bags));
				state.BuildNow();
				if (WalkAll(state.Content).OfType<CodexItemIcon>().Any()) failures.Add("a bag shown with none opened");
			}
			finally
			{
				CodexSystem.Data = savedData;
				state.RestoreNavigation(saved);
			}
			Check.All(failures, "Undiscovered things shown");
		}

		// A codex of 3000 seen items and 300 killed enemies: the items and enemies tabs only make the elements of the
		// visible rows (the lag of 1.0.1 came from thousands of icons recalculated on every frame), at the top and
		// scrolled to the bottom, and the last item is reachable
		private static void CodexManyItemsBounded()
		{
			CodexUIState state = StateOrSkip();
			if (ItemLoader.ItemCount < 3100) Check.Skip("fewer than 3100 item types");
			const int MaxElements = 600;
			CodexData savedData = CodexSystem.Data;
			var saved = state.SaveNavigation();
			List<string> failures = new();
			try
			{
				CodexData fresh = new() { Seed = SeedSystem.WorldSeed, HasSeed = true };
				for (int type = 1; type <= 3000; type++) fresh.Apply(CodexEntry.Seen(CodexNames.Item(type)));
				int killed = 0;
				foreach (int netId in ContentSamples.NpcsByNetId.Keys.Where(id => id != 0).OrderBy(id => id))
				{
					if (killed >= 300) break;
					fresh.Apply(CodexEntry.Kill(CodexNames.Npc(netId)));
					killed++;
				}
				CodexSystem.Data = fresh;
				state.ClearFilters();

				foreach (CodexPageKind tab in new[] { CodexPageKind.Items, CodexPageKind.Enemies })
				{
					state.Navigate(new CodexPage(tab));
					state.BuildNow();
					CodexList list = state.Content;
					int top = Walk(list).Count();
					if (top == 0 || top > MaxElements) failures.Add($"{tab}: {top} elements at the top (rows: {list.Count})");
					list.ViewPosition = float.MaxValue;
					list.Sync();
					List<UIElement> bottom = Walk(list).ToList();
					if (bottom.Count == 0 || bottom.Count > MaxElements) failures.Add($"{tab}: {bottom.Count} elements at the bottom");
					if (tab == CodexPageKind.Items)
					{
						int last = CodexSystem.ItemsSeen().OrderBy(CodexDraw.ItemName, StringComparer.CurrentCultureIgnoreCase).Last();
						if (!bottom.OfType<CodexItemIcon>().Any(i => i.Type == last)) failures.Add($"Items: the last item {last} is not shown at the bottom");
						if (list.Count < 3000 / 40) failures.Add($"Items: only {list.Count} rows for 3000 items");
					}
				}
			}
			finally
			{
				CodexSystem.Data = savedData;
				state.RestoreNavigation(saved);
			}
			Check.All(failures, "Codex pages too heavy");
		}

		// No text of the codex (window, tabs, every tab's page, an item and an enemy page) is a raw localization key
		private static void CodexTextsResolved()
		{
			CodexUIState state = StateOrSkip();
			var saved = state.SaveNavigation();
			List<string> failures = new();
			void Look(string where, IEnumerable<UIElement> elements)
			{
				foreach (UIElement element in elements)
				{
					string text = element switch
					{
						CodexLabel label => label.Text,
						CodexSection section => section.Text,
						CodexButton button => button.Text,
						Terraria.GameContent.UI.Elements.UIText uiText => uiText.Text,
						_ => null
					};
					if (text != null && (text.Contains("Mods.") || text.Contains("Bestiary_"))) failures.Add($"{where}: {text}");
				}
			}
			try
			{
				Look("window", Walk(state));
				List<CodexPage> pages = new[] { CodexPageKind.Enemies, CodexPageKind.Items, CodexPageKind.Crafts, CodexPageKind.Bags, CodexPageKind.Biomes, CodexPageKind.Hints }.Select(t => new CodexPage(t)).ToList();
				foreach (int item in CodexSystem.ItemsSeen().Take(5).Append(ItemID.WoodenSword)) pages.Add(CodexPage.ItemPage(item));
				foreach (int npc in CodexSystem.KilledNpcs().Take(3).Append(NPCID.BlueSlime)) pages.Add(CodexPage.NpcPage(npc));
				foreach (CodexPage page in pages)
				{
					state.Navigate(page);
					state.BuildNow();
					Look($"{page.Kind} {page.Id}", WalkAll(state.Content));
				}
				foreach (CodexSource kind in Enum.GetValues<CodexSource>())
				{
					if (!Language.Exists(CodexText.Prefix + "Source." + kind)) failures.Add("missing text Codex.Source." + kind);
				}
			}
			finally
			{
				state.RestoreNavigation(saved);
			}
			Check.All(failures, "Raw keys shown");
		}
	}
}

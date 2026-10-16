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
		}

		private static IEnumerable<UIElement> Walk(UIElement element)
		{
			foreach (UIElement child in element.Children)
			{
				yield return child;
				foreach (UIElement inner in Walk(child)) yield return inner;
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
				foreach (CodexPageKind tab in new[] { CodexPageKind.Enemies, CodexPageKind.Items, CodexPageKind.Crafts, CodexPageKind.Bags, CodexPageKind.Biomes })
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
				foreach (CodexPageKind tab in new[] { CodexPageKind.Enemies, CodexPageKind.Items, CodexPageKind.Crafts, CodexPageKind.Bags, CodexPageKind.Biomes })
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

				state.Navigate(CodexPage.ItemPage(item));
				state.BuildNow();
				List<int> shown = Walk(state.Content).OfType<CodexRecipeRow>().Select(r => r.RecipeIndex).Distinct().ToList();
				if (!shown.Contains(recipes.first)) failures.Add($"discovered recipe {recipes.first} not shown");
				foreach (int recipe in shown.Where(r => !CodexSystem.IsRecipeDiscovered(r))) failures.Add($"undiscovered recipe {recipe} shown");

				state.Navigate(new CodexPage(CodexPageKind.Enemies));
				state.BuildNow();
				int portraits = Walk(state.Content).OfType<CodexNpcPortrait>().Count();
				if (portraits > 0) failures.Add($"{portraits} enemies shown with no kill");

				state.Navigate(new CodexPage(CodexPageKind.Items));
				state.BuildNow();
				foreach (CodexItemIcon icon in Walk(state.Content).OfType<CodexItemIcon>().Where(i => i.Type != item)) failures.Add($"unseen item {icon.Type} shown");

				state.Navigate(new CodexPage(CodexPageKind.Bags));
				state.BuildNow();
				if (Walk(state.Content).OfType<CodexItemIcon>().Any()) failures.Add("a bag shown with none opened");
			}
			finally
			{
				CodexSystem.Data = savedData;
				state.RestoreNavigation(saved);
			}
			Check.All(failures, "Undiscovered things shown");
		}
	}
}

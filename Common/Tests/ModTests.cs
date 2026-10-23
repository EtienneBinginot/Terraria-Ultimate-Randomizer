using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Tests
{
	// Other mods' content: their recipes randomized and tiered, their items in the pool, their NPCs' drops permuted, their
	// stations tiered. Magic Storage is the reference mod (the tests that need another mod's content skip without it)
	public static class ModTests
	{
		public static void Register()
		{
			TestRunner.Register("ModdedRecipesRandomized", ModdedRecipesRandomized);
			TestRunner.Register("ModdedItemsInPool", ModdedItemsInPool);
			TestRunner.Register("ModdedDropsPermuted", ModdedDropsPermuted);
			TestRunner.Register("ModdedStationsTiered", ModdedStationsTiered);
		}

		private static string Name(int item) => Lang.GetItemNameValue(item);

		private static void Log(string text) => ModContent.GetInstance<UltimateRandomizer>().Logger.Info(text);

		private static int Count => Math.Min(RecipeSystem.RecipeCount, Recipe.numRecipes);

		private static int ModItemType(string mod, string name) => ModContent.TryFind(mod, name, out ModItem item) ? item.Type : 0;

		// Magic Storage's storage upgrades in their order, each with the vanilla material its recipe is gated by (0: none)
		private static readonly (string name, int anchor)[] Upgrades =
		{
			("UpgradeDemonite", ItemID.DemoniteBar), ("UpgradeHellstone", ItemID.HellstoneBar), ("UpgradeHallowed", ItemID.HallowedBar),
			("UpgradeBlueChlorophyte", ItemID.BeetleHusk), ("UpgradeLuminite", ItemID.LunarBar), ("UpgradeTerra", 0)
		};

		// When an ingredient of a recipe is had with a plan (a group by its earliest item)
		private static int IngredientAvail(RecipePlan plan, int i, int type)
		{
			foreach (int id in RecipePlanner.GroupsOf(plan, i))
			{
				if (RecipeGroup.recipeGroups.TryGetValue(id, out RecipeGroup group) && group.ContainsItem(type))
				{
					return group.ValidItems.Where(v => v > 0 && v < plan.Availability.Length).Select(v => plan.Availability[v]).DefaultIfEmpty(Findability.Never).Min();
				}
			}
			return type > 0 && type < plan.Availability.Length ? plan.Availability[type] : Findability.Never;
		}

		// The tier of a recipe as set up, from its ingredients' true tiers (a group by its earliest item)
		private static int OriginalTier(int i)
		{
			int tier = 0;
			foreach ((int type, _) in RecipeSystem.OriginalIngredients(i))
			{
				IEnumerable<int> choices = ItemClassifier.IngredientChoices(RecipeSystem.OriginalGroups(i), type).Where(ItemPool.IsValid);
				tier = Math.Max(tier, choices.Select(ItemPool.TrueTierOf).DefaultIfEmpty(0).Min());
			}
			return tier;
		}

		// Magic Storage's upgrades take the tiers of their bars (its recipes set its items' tiers), in order; with its recipes
		// randomized, each rebuilt one is live as planned, has a key, and is not doable much before its original tier
		private static void ModdedRecipesRandomized()
		{
			if (!ModLoader.HasMod("MagicStorage")) Check.Skip("Magic Storage is not loaded");
			List<string> failures = new();
			List<string> shown = new();
			int previous = -1;
			string previousName = null;
			foreach ((string name, int anchor) in Upgrades)
			{
				int type = ModItemType("MagicStorage", name);
				if (!ItemPool.IsValid(type))
				{
					failures.Add($"{name} not found");
					continue;
				}
				int tier = ItemPool.TrueTierOf(type);
				shown.Add($"{name} {ItemPool.TierOf(type)}/{tier}");
				if (anchor > 0 && tier < ItemPool.TrueTierOf(anchor)) failures.Add($"{name}: tier {tier}, below {Name(anchor)} ({ItemPool.TrueTierOf(anchor)})");
				if (tier < previous) failures.Add($"{name}: tier {tier}, below {previousName} ({previous})");
				previous = tier;
				previousName = name;
			}
			Log("ModdedRecipesRandomized: Magic Storage upgrades (tier/true tier): " + string.Join(", ", shown));
			Check.All(failures, "Magic Storage upgrade tiers");

			CraftingConfig cfg = CraftingConfig.Instance;
			if (!RecipeSystem.Applied || !cfg.RandomizeRecipes) Check.Skip("recipes are not randomized");
			if (!cfg.RandomizeModdedRecipes) Check.Skip("other mods' recipes are left alone");
			if (cfg.IngredientMode != IngredientMode.Randomized) Check.Skip("ingredients are vanilla");
			bool coherent = GeneralConfig.Instance.RandomizerMode == RandomizerMode.Coherent;
			RecipePlan plan = RecipePlanner.Get();
			int total = 0, rebuildable = 0, rebuilt = 0;
			List<string> examples = new();
			for (int i = 0; i < Count; i++)
			{
				if (Main.recipe[i].Mod?.Name != "MagicStorage" || RecipeSystem.VanillaDisabled(i) || RecipeSystem.IsShell(i)) continue;
				total++;
				string where = $"{Name(RecipeSystem.OriginalResult(i))} (recipe {i})";
				if (RecipeSystem.LeftAlone(i)) failures.Add($"{where}: left alone");
				if (RecipePlanner.Rebuildable(i)) rebuildable++;
				if (plan.Ingredients[i] == null || plan.Repaired[i]) continue;
				rebuilt++;
				if (!plan.Twin[i] && !Main.recipe[i].requiredItem.Select(x => (x.type, x.stack)).SequenceEqual(plan.Ingredients[i])) failures.Add($"{where}: live ingredients are not the planned ones");
				int t = Math.Max(0, plan.IngredientTiers[i]);
				int highest = plan.Ingredients[i].Select(x => IngredientAvail(plan, i, x.type)).DefaultIfEmpty(0).Max();
				int original = OriginalTier(i);
				if (t > 0 && plan.PlannedKeys[i] == 0 && !plan.KeyIngredient[i]) failures.Add($"{where}: rebuilt for tier {t} without a key");
				// The key is found at the recipe's tier T (vanilla doable tier of its original ingredients); a tier of allowance
				if (coherent && highest < Math.Min(original, t) - 1) failures.Add($"{where}: original tier {original}, rebuilt for {t}, its ingredients all had at {highest}");
				if (examples.Count < 8) examples.Add($"{Name(plan.Results[i].type)} T{t} (was {original}, key {(plan.PlannedKeys[i] > 0 ? Name(plan.PlannedKeys[i]) : "-")})");
			}
			Log($"ModdedRecipesRandomized: {total} Magic Storage recipes, {rebuildable} rebuildable, {rebuilt} rebuilt; e.g. {string.Join(", ", examples)}");
			if (total == 0) Check.Skip("Magic Storage has no recipe");
			if (rebuildable > 0 && rebuilt == 0) failures.Add("no Magic Storage recipe was rebuilt");
			Check.All(failures, "Magic Storage recipes");
		}

		// The items of utility mods (tModLoader, Recipe Browser) are banned; Magic Storage's are in the pool (not banned, in
		// their bucket), except its creative unit
		private static void ModdedItemsInPool()
		{
			List<string> failures = new();
			for (int type = ItemID.Count; type < ItemPool.Info.Length; type++)
			{
				if (!ContentSamples.ItemsByType.TryGetValue(type, out Item item) || item.ModItem?.Mod?.Name is not ("ModLoader" or "RecipeBrowser")) continue;
				if (!ItemPool.Info[type].Excluded && !ItemPool.IsBanned(type)) failures.Add($"{item.ModItem.Mod.Name}'s {item.ModItem.Name} is not banned");
			}
			Check.All(failures, "Utility mods' items");
			if (!ModLoader.HasMod("MagicStorage")) Check.Skip("Magic Storage is not loaded");
			if (GeneralConfig.Instance.ExcludedMods.Any(m => m?.Trim() == "MagicStorage")) Check.Skip("Magic Storage is excluded");
			foreach (string name in new[] { "StorageHeart", "StorageUnit", "StorageComponent", "CraftingAccess", "UpgradeHallowed", "PortableAccessPreHM" })
			{
				int type = ModItemType("MagicStorage", name);
				if (!ItemPool.IsValid(type))
				{
					failures.Add($"{name} not found");
					continue;
				}
				ItemInfo info = ItemPool.Info[type];
				if (info.Excluded || info.Banned) failures.Add($"{name} is {(info.Excluded ? "excluded" : "banned")}");
				else if (!info.Protected && !info.Special && !ItemPool.Bucket(info.Category, info.Tier).Contains(type)) failures.Add($"{name} is in no bucket");
			}
			int creative = ModItemType("MagicStorage", "CreativeStorageUnit");
			if (ItemPool.IsValid(creative) && !ItemPool.Info[creative].Excluded && !ItemPool.IsBanned(creative)) failures.Add("the Creative Storage Unit is not banned");
			Check.All(failures, "Magic Storage items");
		}

		// Every rolled entry of another mod's ordinary enemy or boss (its vanilla-typed rules: they are wrapped) is a place of
		// the world's permutation, in a group or boss group holding vanilla places too
		private static void ModdedDropsPermuted()
		{
			DropPermutation.WorldPlan enemies = DropPermutation.Current;
			BossPermutation.WorldPlan bosses = BossPermutation.Current;
			List<string> failures = new();
			List<string> shown = new();
			int checkedUnits = 0;
			bool Vanilla(int netId) => ContentSamples.NpcsByNetId.TryGetValue(netId, out NPC sample) && sample.type < NPCID.Count;
			void CheckUnit(int unit, List<DropPermutation.Place> places, List<List<int>> groups, string kind)
			{
				List<DropRateInfo> entries = LootSnapshot.ForNpc(unit);
				bool[] rolled = DropPermutation.RolledEntries(entries, LootSnapshot.RulesOf(unit));
				List<int> expected = Enumerable.Range(0, entries.Count).Where(i => rolled[i] && !DropHelper.KeepOriginal(entries[i].itemId)).ToList();
				if (expected.Count == 0) return;
				checkedUnits++;
				string name = Lang.GetNPCNameValue(unit);
				if (shown.Count < 10) shown.Add($"{name} ({kind}, tier {NpcTiers.BaseTier(unit)}, {expected.Count} entries)");
				foreach (int index in expected)
				{
					int p = places.FindIndex(x => x.Owner == unit && x.Index == index);
					if (p < 0)
					{
						failures.Add($"{name}: entry {index} ({Name(entries[index].itemId)}) is no place");
						continue;
					}
					List<int> group = groups.FirstOrDefault(g => g.Contains(p));
					if (group == null || !group.Any(q => Vanilla(places[q].Owner))) failures.Add($"{name}: entry {index} is only permuted with modded places");
				}
			}
			foreach (int unit in DropPermutation.Units().Where(u => !Vanilla(u))) CheckUnit(unit, enemies.Places, enemies.Groups, "enemy");
			foreach (int unit in BossPermutation.Units().Where(u => !Vanilla(u)))
			{
				CheckUnit(unit, bosses.Places, bosses.Groups, "boss");
				if (bosses.GroupOfUnit.TryGetValue(unit, out int g) && !bosses.BossGroups[g].Any(Vanilla)) failures.Add($"{Lang.GetNPCNameValue(unit)}: in a boss group without vanilla bosses");
			}
			Log($"ModdedDropsPermuted: {checkedUnits} modded tables checked; e.g. {string.Join(", ", shown)}");
			if (checkedUnits == 0) Check.Skip("no modded enemy or boss with permutable drops");
			Check.All(failures, "Modded drops");
		}

		// Every modded station some recipe needs is known (themed, or progression when many recipes need it) at its item's true
		// tier; a progression one is protected, a themed one guaranteed when stations are not protected; a recipe given a
		// modded station it did not have gets a progression one of its tier or below
		private static void ModdedStationsTiered()
		{
			List<int> tiles = Stations.RequiredTiles().Where(Stations.IsModded).OrderBy(t => t).ToList();
			if (tiles.Count == 0) Check.Skip("no modded crafting station");
			ProgressionConfig cfg = ProgressionConfig.Instance;
			HashSet<int> configProtected = cfg.ProtectedItems.Where(d => d != null && !d.IsUnloaded).Select(d => d.Type).ToHashSet();
			List<string> failures = new();
			List<string> shown = new();
			foreach (int tile in tiles)
			{
				Stations.StationInfo info = Stations.Info(tile);
				string name = Stations.Name(tile);
				shown.Add($"{name} ({info.Kind}, tier {info.Tier})");
				if (!info.Modded || info.Kind is not (StationKind.Thematic or StationKind.Progression)) failures.Add($"{name}: {info.Kind}, not a known modded station");
				List<int> placers = Stations.Placers(tile).Where(p => ItemPool.IsValid(p) && !ItemPool.Info[p].Excluded).ToList();
				if (placers.Count == 0) continue;
				int expected = placers.Min(ItemPool.TrueTierOf);
				if (info.Tier != expected) failures.Add($"{name}: tier {info.Tier}, its item says {expected}");
				bool shouldProtect = info.Kind == StationKind.Progression || cfg.StationProtection != StationProtection.None;
				foreach (int placer in placers)
				{
					bool isProtected = ItemPool.Info[placer].Protected;
					if (shouldProtect && !isProtected) failures.Add($"{name}: {Name(placer)} is not protected");
					if (!shouldProtect && isProtected && !configProtected.Contains(placer) && !Protection.IsBossSummon(placer)) failures.Add($"{name}: {Name(placer)} is protected");
				}
				if (!shouldProtect && placers.Any(p => ItemPool.IsRandomizable(p) && !ItemPool.IsBanned(p)))
				{
					int item = GuaranteedSources.StationItem(tile);
					if (item <= 0 || !GuaranteedSources.Requirements.Any(r => r.Item == item && r.Station)) failures.Add($"{name}: unprotected but not guaranteed");
				}
			}
			Log("ModdedStationsTiered: " + string.Join(", ", shown));
			if (RecipeSystem.Applied)
			{
				RecipePlan plan = RecipePlanner.Get();
				for (int i = 0; i < Count; i++)
				{
					if (plan.Tiles[i] == null) continue;
					foreach (int tile in plan.Tiles[i].Where(t => Stations.IsModded(t) && !RecipeSystem.OriginalTiles(i).Contains(t)))
					{
						Stations.StationInfo info = Stations.Info(tile);
						if (info.Kind != StationKind.Progression || info.Tier > RecipePlanner.RecipeTier(i))
						{
							failures.Add($"{Name(RecipeSystem.OriginalResult(i))} (tier {RecipePlanner.RecipeTier(i)}) got {Stations.Name(tile)} ({info.Kind}, tier {info.Tier})");
						}
					}
				}
			}
			Check.All(failures, "Modded stations");
		}
	}
}

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
			TestRunner.Register("ModdedProgressionGuaranteed", ModdedProgressionGuaranteed);
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
			List<string> ownCondition = new();
			for (int i = 0; i < Count; i++)
			{
				if (Main.recipe[i].Mod?.Name != "MagicStorage" || RecipeSystem.VanillaDisabled(i) || RecipeSystem.IsShell(i)) continue;
				total++;
				string where = $"{Name(RecipeSystem.OriginalResult(i))} (recipe {i})";
				// A recipe with a condition of its mod stays as it is on purpose (Cooked Marshmallow near a campfire): logged
				if (RecipeSystem.HasModdedCondition(i))
				{
					if (!RecipeSystem.LeftAlone(i)) failures.Add($"{where}: randomized with a condition of its mod");
					ownCondition.Add(where);
					continue;
				}
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
			Log($"ModdedRecipesRandomized: {total} Magic Storage recipes, {rebuildable} rebuildable, {rebuilt} rebuilt; e.g. {string.Join(", ", examples)}; " +
				$"left alone for a condition of their mod: {(ownCondition.Count == 0 ? "none" : string.Join(", ", ownCondition))}");
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
			// No modded enemy or boss with permutable drops here: the pure planner with explicit places instead
			if (checkedUnits == 0) SyntheticModdedPlaces(failures);
			Check.All(failures, "Modded drops");
		}

		// A drop condition of another assembly than tModLoader's, as another mod's would be
		private sealed class ForeignDropCondition : IItemDropRuleCondition, IProvideItemConditionDescription
		{
			public bool CanDrop(DropAttemptInfo info) => true;
			public bool CanShowItemDropInUI() => true;
			public string GetConditionDescription() => null;
		}

		// A modded owner's ordinary entry is grouped with the vanilla entries of its tier; an entry under another mod's
		// condition is Restrictive and only grouped with entries under the same condition (its access class)
		private static void SyntheticModdedPlaces(List<string> failures)
		{
			List<IItemDropRuleCondition> foreign = new() { new ForeignDropCondition() };
			if (DropPermutation.ClassOf(foreign[0]) != DropPermutation.ConditionClass.Restrictive) failures.Add($"a modded condition is {DropPermutation.ClassOf(foreign[0])}, not Restrictive");
			string access = DropPermutation.AccessOf(foreign);
			if (!access.Contains(typeof(ForeignDropCondition).FullName)) failures.Add($"a modded condition's access class is \"{access}\", not its full type name");
			List<DropPermutation.Place> places = new();
			void Add(int owner, int index, string placeAccess) => places.Add(new DropPermutation.Place { Owner = owner, Index = index, OwnerKey = owner * 31 + index, Tier = 0, Access = placeAccess });
			foreach (int owner in new[] { NPCID.BlueSlime, NPCID.Zombie, NPCID.DemonEye, NPCID.Skeleton }) Add(owner, 0, "");
			int modded = NPCLoader.NPCCount + 5;
			Add(modded, 0, "");
			Add(modded, 1, access);
			Add(NPCID.Zombie, 1, access);
			Add(NPCID.DemonEye, 1, access);
			DropPermutation.Permute(places, 12345, out List<List<int>> groups);
			List<int> plain = groups.FirstOrDefault(g => g.Contains(4));
			if (plain == null || !plain.Any(q => places[q].Owner < NPCID.Count && places[q].Access == "")) failures.Add("synthetic: the modded entry is not grouped with vanilla entries");
			foreach (List<int> group in groups)
			{
				if (group.Select(q => places[q].Access).Distinct().Count() > 1) failures.Add("synthetic: a group mixes a modded condition with other entries");
			}
			Log($"ModdedDropsPermuted: no modded table, synthetic places checked ({groups.Count} groups, access \"{access}\")");
		}

		// Other mods' boss summons and stations are treated like the vanilla ones: a summon whose tier is estimated
		// (Protection.ModdedSummonTierKnown) is unprotected unless AutoProtectBossSummons, and when randomizable it is a
		// guarantee at its summon tier with hosts; one whose tier is unknown stays protected. A modded progression or themed
		// station is protected only by StationProtection, else a guarantee (a station quest with randomized recipes)
		private static void ModdedProgressionGuaranteed()
		{
			if (!ItemPool.Ready || !SeedSystem.WorldReady) Check.Skip("no world");
			ProgressionConfig cfg = ProgressionConfig.Instance;
			List<int> summons = Enumerable.Range(ItemID.Count, Math.Max(0, ItemPool.Info.Length - ItemID.Count)).Where(Protection.IsBossSummon).ToList();
			List<int> stationTiles = Stations.RequiredTiles().Where(t => Stations.IsModded(t) && !Stations.Info(t).World && Stations.Info(t).Kind is StationKind.Progression or StationKind.Thematic).ToList();
			if (summons.Count == 0 && stationTiles.Count == 0) Check.Skip("no modded boss summon nor crafting station");
			HashSet<int> listed = cfg.ProtectedItems.Where(d => d != null && !d.IsUnloaded).Select(d => d.Type).ToHashSet();
			List<string> failures = new();
			List<string> shown = new();
			foreach (int item in summons)
			{
				ItemInfo info = ItemPool.Info[item];
				if (info.Excluded || listed.Contains(item)) continue;
				bool known = Protection.ModdedSummonTierKnown(item);
				if (cfg.AutoProtectBossSummons || !known)
				{
					if (!info.Protected) failures.Add($"{Name(item)}: not protected ({(known ? "AutoProtectBossSummons" : "tier unknown")})");
					continue;
				}
				if (info.Protected) failures.Add($"{Name(item)}: protected although its tier is estimated");
				if (!ItemPool.IsRandomizable(item)) continue;
				GuaranteedSources.Requirement r = GuaranteedSources.Requirements.FirstOrDefault(x => x.Item == item);
				if (r.Item != item) failures.Add($"{Name(item)}: not guaranteed");
				else if (!GuaranteedSources.HostsOf(item).Any()) failures.Add($"{Name(item)}: guaranteed at tier {r.Tier} without a host");
				else if (shown.Count < 8) shown.Add($"{Name(item)} (tier {r.Tier}, {GuaranteedSources.HostsOf(item).Count()} hosts)");
			}
			foreach (int tile in stationTiles)
			{
				List<int> placers = Stations.Placers(tile).Where(p => ItemPool.IsValid(p) && !ItemPool.Info[p].Excluded && !listed.Contains(p)).ToList();
				if (placers.Count == 0 || cfg.StationProtection != StationProtection.None) continue;
				if (placers.Any(p => ItemPool.Info[p].Protected)) failures.Add($"{Stations.Name(tile)}: placer protected");
				int item = GuaranteedSources.StationItem(tile);
				if (item <= 0) continue;
				if (!GuaranteedSources.Requirements.Any(x => x.Item == item)) failures.Add($"{Stations.Name(tile)}: {Name(item)} not guaranteed");
				else if (shown.Count < 12) shown.Add($"{Stations.Name(tile)} ({Stations.Info(tile).Kind}, tier {Stations.Info(tile).Tier})");
			}
			Log($"ModdedProgressionGuaranteed: {summons.Count} modded summons, {stationTiles.Count} modded stations; e.g. {string.Join(", ", shown)}");
			Check.All(failures, "Modded summons and stations");
		}

		// Every modded station some recipe needs is known (themed, or progression when many recipes need it) at its item's true
		// tier; both are guaranteed (station quests) when stations are not protected (StationProtection), like the vanilla
		// ones; a recipe given a modded station it did not have gets a progression one of its tier or below, or a themed one
		// (only an ordinary recipe: never a station's nor a house's)
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
				bool shouldProtect = cfg.StationProtection != StationProtection.None;
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
						if (info.Kind == StationKind.Thematic)
						{
							if (RecipePlanner.StationRecipe(i) || RecipeSystem.HouseRecipe(i)) failures.Add($"{Name(RecipeSystem.OriginalResult(i))} (a station or a house) got the themed {Stations.Name(tile)}");
						}
						else if (info.Kind != StationKind.Progression || info.Tier > RecipePlanner.RecipeTier(i))
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

using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Tests
{
	public static class CoreTests
	{
		public static void Register()
		{
			TestRunner.Register("PoolBuilt", PoolBuilt);
			TestRunner.Register("Classification", Classification);
			TestRunner.Register("Determinism", Determinism);
			TestRunner.Register("GatingRespected", GatingRespected);
			TestRunner.Register("NoProtectedRolled", NoProtectedRolled);
			TestRunner.Register("SameCategory", SameCategory);
			TestRunner.Register("ShuffleBijection", ShuffleBijection);
			TestRunner.Register("StackRules", StackRulesValid);
			TestRunner.Register("PriceFloor", PriceFloor);
			TestRunner.Register("DropRates", DropRatesValid);
			TestRunner.Register("TierFromRecipes", TierFromRecipes);
		}

		private static IEnumerable<int> Randomizable() => Enumerable.Range(1, ItemPool.Info.Length - 1).Where(ItemPool.IsRandomizable);

		private static int[] Sample(int count, int salt)
		{
			int[] all = Randomizable().ToArray();
			Random rng = new(salt);
			return Enumerable.Range(0, count).Select(_ => all[rng.Next(all.Length)]).ToArray();
		}

		// Crafted items are found no later than their vanilla recipe allows: the Tinkerer's combinations made of pre-hardmode
		// parts are below hardmode; an item is never lowered below its best original vanilla recipe (the highest tier of its
		// ingredients and stations), and only lowered when such a recipe allows it; KnownTiers and overrides keep theirs
		private static void TierFromRecipes()
		{
			HashSet<int> overridden = ProgressionConfig.Instance.TierOverrides.Where(o => o?.Item != null && !o.Item.IsUnloaded).Select(o => o.Item.Type).ToHashSet();
			int[] tiers = ItemPool.AllTiers;
			List<string> failures = new();
			foreach (int item in new[] { ItemID.YellowHorseshoeBalloon, ItemID.BundleofBalloons, ItemID.FrostsparkBoots, ItemID.LightningBoots })
			{
				if (overridden.Contains(item) || !ItemPool.IsValid(item)) continue;
				if (ItemPool.TierOf(item) >= (int)Tier.Hardmode) failures.Add($"{Lang.GetItemNameValue(item)} at tier {ItemPool.TierOf(item)} (rarity {ItemPool.Info[item].RarityTier}), crafted before hardmode");
			}
			// Gear made from late materials stays late (a wrong ingredient tier would pull it to the start)
			foreach ((int item, Tier floor) in new[] { (ItemID.NecroHelmet, Tier.PostSkeletron), (ItemID.SpookyHelmet, Tier.PostPlantera), (ItemID.SpiderMask, Tier.Hardmode),
				(ItemID.MartianConduitWall, Tier.PostGolem), (ItemID.AncientBattleArmorHat, Tier.Hardmode) })
			{
				if (overridden.Contains(item) || !ItemPool.IsValid(item)) continue;
				// Only a lowering counts: an item whose rarity already gives a lower tier keeps it
				int expected = System.Math.Min((int)floor, ItemPool.Info[item].RarityTier);
				if (ItemPool.TierOf(item) < expected) failures.Add($"{Lang.GetItemNameValue(item)} at tier {ItemPool.TierOf(item)}, at least {expected} expected");
			}
			// The original recipes (the live ones are randomized), placers of each tile for the stations
			Dictionary<int, List<int>> placers = new();
			foreach ((int type, Item sample) in ContentSamples.ItemsByType)
			{
				if (sample == null || sample.createTile < TileID.Dirt || type <= 0 || type >= tiers.Length) continue;
				if (!placers.TryGetValue(sample.createTile, out List<int> list)) placers[sample.createTile] = list = new List<int>();
				list.Add(type);
			}
			Dictionary<int, int> best = new();
			int count = Math.Min(Recipe.numRecipes, Features.RecipeSystem.RecipeCount);
			for (int i = 0; i < count; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (recipe == null || Features.RecipeSystem.VanillaDisabled(i) || recipe.Mod != null && recipe.Mod.Name != "ModLoader") continue;
				int result = Features.RecipeSystem.OriginalResult(i);
				// The same recipes as the lowering: the live recipe's conditions stay, its ingredients are randomized (the originals)
				if (!ItemClassifier.SetsTier(recipe, Features.RecipeSystem.OriginalIngredients(i).Select(x => x.type).ToList())) continue;
				if (result <= 0 || result >= tiers.Length) continue;
				int tier = 0;
				foreach ((int type, _) in Features.RecipeSystem.OriginalIngredients(i))
				{
					if (type <= 0 || type >= tiers.Length) continue;
					tier = Math.Max(tier, ItemClassifier.IngredientChoices(recipe, type).Where(t => t > 0 && t < tiers.Length).Select(t => tiers[t]).DefaultIfEmpty(Tiers.Max).Min());
				}
				foreach (int tile in Features.RecipeSystem.OriginalTiles(i).Where(t => t >= 0)) tier = Math.Max(tier, ItemClassifier.StationTier(tile, placers, t => tiers[t]));
				best[result] = Math.Min(best.GetValueOrDefault(result, int.MaxValue), tier);
			}
			int lowered = 0;
			for (int item = 1; item < ItemPool.Info.Length && item < tiers.Length; item++)
			{
				if (!ItemPool.IsValid(item)) continue;
				ItemInfo info = ItemPool.Info[item];
				if (info.Tier > info.RarityTier) failures.Add($"{Lang.GetItemNameValue(item)} raised from {info.RarityTier} to {info.Tier}");
				if (info.Tier == info.RarityTier || failures.Count > 20) continue;
				lowered++;
				string name = Lang.GetItemNameValue(item);
				if (overridden.Contains(item) || ItemClassifier.HasKnownTier(item)) failures.Add($"{name}: a fixed tier lowered to {info.Tier}");
				else if (!best.TryGetValue(item, out int recipeTier)) failures.Add($"{name}: lowered to {info.Tier} without a vanilla recipe");
				else if (info.Tier < recipeTier) failures.Add($"{name}: tier {info.Tier} below its best recipe's {recipeTier}");
				else if (info.Tier > recipeTier) failures.Add($"{name}: tier {info.Tier}, its best recipe allows {recipeTier}");
			}
			Terraria.ModLoader.ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Tiers from recipes: {lowered} items lowered");
			if (count == 0) Check.Skip("no recipe stored");
			Check.All(failures, "Tiers from recipes");
		}

		private static void PoolBuilt()
		{
			int count = Randomizable().Count();
			Check.That(count >= 1000, $"only {count} randomizable items");
			ItemCategory[] required =
			{
				ItemCategory.Material, ItemCategory.WeaponMelee, ItemCategory.WeaponRanged, ItemCategory.WeaponMagic, ItemCategory.WeaponSummon,
				ItemCategory.Armor, ItemCategory.Accessory, ItemCategory.Potion, ItemCategory.Ammo, ItemCategory.Block, ItemCategory.Tool, ItemCategory.OreBar
			};
			List<string> failures = new();
			foreach (ItemCategory category in required)
			{
				int size = Enumerable.Range(0, Tiers.Count).Sum(t => ItemPool.BucketSize(category, t));
				if (size == 0) failures.Add($"{category} is empty");
			}
			Check.All(failures, "Empty categories");
		}

		private static void Classification()
		{
			// (item, category, tier or -1 to ignore)
			(int item, ItemCategory category, int tier)[] expected =
			{
				(ItemID.Zenith, ItemCategory.WeaponMelee, 9),
				(ItemID.CopperShortsword, ItemCategory.WeaponMelee, 0),
				(ItemID.WoodenBow, ItemCategory.WeaponRanged, 0),
				(ItemID.WandofSparking, ItemCategory.WeaponMagic, -1),
				(ItemID.SlimeStaff, ItemCategory.WeaponSummon, -1),
				(ItemID.DirtBlock, ItemCategory.Block, 0),
				(ItemID.Gel, ItemCategory.Material, 0),
				(ItemID.LesserHealingPotion, ItemCategory.Potion, 0),
				(ItemID.WoodenArrow, ItemCategory.Ammo, 0),
				(ItemID.CopperPickaxe, ItemCategory.Tool, 0),
				(ItemID.Shackle, ItemCategory.Accessory, -1),
				(ItemID.CopperHelmet, ItemCategory.Armor, 0),
				(ItemID.EyeOfCthulhuBossBag, ItemCategory.Bag, -1),
				(ItemID.CopperOre, ItemCategory.OreBar, 0),
				(ItemID.CobaltOre, ItemCategory.OreBar, 4),
				(ItemID.HallowedBar, ItemCategory.OreBar, 5),
				(ItemID.Bunny, ItemCategory.Critter, -1),
				(ItemID.SuspiciousLookingEye, ItemCategory.BossSummon, -1)
			};
			List<string> failures = new();
			foreach ((int item, ItemCategory category, int tier) in expected)
			{
				ItemInfo info = ItemPool.Info[item];
				string name = Lang.GetItemNameValue(item);
				if (info.Excluded) failures.Add($"{name} excluded");
				else if (info.Category != category) failures.Add($"{name} is {info.Category}, expected {category}");
				else if (tier >= 0 && info.Tier != tier) failures.Add($"{name} is tier {info.Tier}, expected {tier}");
			}
			if (!ItemPool.Info[ItemID.CopperCoin].Excluded) failures.Add("coins must be excluded");
			if (!ItemPool.Info[ItemID.Heart].Excluded) failures.Add("hearts must be excluded");
			if (ProgressionConfig.Instance.AutoProtectBossSummons && !ItemPool.Info[ItemID.SuspiciousLookingEye].Protected) failures.Add("boss summons must be protected");
			if (ProgressionConfig.Instance.ProtectProgressionMaterials && !ItemPool.Info[ItemID.ShadowScale].Protected) failures.Add("Shadow Scale must be protected");
			Check.All(failures, "Misclassified items");
		}

		private static void Determinism()
		{
			Random rng = new(1234);
			int differentSeeds = 0;
			for (int i = 0; i < 300; i++)
			{
				int[] keys = { rng.Next(), rng.Next(), rng.Next(100) };
				RollRequest request = new() { Keys = keys, SourceTier = rng.Next(Tiers.Count), OriginalItem = -1, Policy = CategoryPolicy.Weighted, TierJitterUp = 1, TierJitterDown = 1, AllowJackpot = true };
				Check.Equal(ItemPool.Roll(request), ItemPool.Roll(request), "same roll twice");
				if (DetRandom.Hash(1, keys) != DetRandom.Hash(2, keys)) differentSeeds++;
			}
			Check.That(differentSeeds >= 295, $"seeds barely change the result ({differentSeeds}/300)");
			Check.Equal(DetRandom.StableHash("Terraria/Zenith"), DetRandom.StableHash("Terraria/Zenith"), "stable hash");
		}

		private static void GatingRespected()
		{
			ProgressionConfig cfg = ProgressionConfig.Instance;
			if (!cfg.TierGating) Check.Skip("tier gating is off");
			if (GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent) Check.Skip("only in Coherent mode");
			int limit = ItemPool.GatingLimit(0);
			List<string> failures = new();
			for (int i = 0; i < 2000; i++)
			{
				RollRequest request = new() { Keys = new[] { 777, i }, SourceTier = 9, OriginalItem = -1, Policy = CategoryPolicy.Weighted, TierJitterUp = 1, AllowJackpot = true };
				int item = ItemPool.Roll(request, out int tier);
				if (ItemPool.TrueTierOf(item) > limit) failures.Add($"{Lang.GetItemNameValue(item)} true tier {ItemPool.TrueTierOf(item)} > {limit}");
				if (tier != ItemPool.TierOf(item)) failures.Add($"reported tier {tier} != real {ItemPool.TierOf(item)}");
			}
			Check.All(failures, "Gating bypassed");
		}

		private static void NoProtectedRolled()
		{
			List<string> failures = new();
			CategoryPolicy[] policies = { CategoryPolicy.Weighted, CategoryPolicy.Any };
			for (int i = 0; i < 3000; i++)
			{
				RollRequest request = new() { Keys = new[] { 4242, i }, SourceTier = i % Tiers.Count, OriginalItem = -1, Policy = policies[i % 2], TierJitterUp = 1, IgnoreGating = true };
				int item = ItemPool.Roll(request);
				if (!ItemPool.IsRandomizable(item) && item != ItemID.Gel) failures.Add(Lang.GetItemNameValue(item));
			}
			Check.All(failures, "Protected or excluded items were given");
		}

		private static void SameCategory()
		{
			if (GeneralConfig.Instance.RandomizerMode == RandomizerMode.Chaos) Check.Skip("Chaos mode ignores categories");
			List<string> failures = new();
			foreach (int original in Sample(1500, 99))
			{
				RollRequest request = new() { Keys = new[] { 31337, original }, SourceTier = ItemPool.TierOf(original), OriginalItem = original, Policy = CategoryPolicy.SameAsOriginal, TierJitterUp = 1 };
				int item = ItemPool.Roll(request);
				if (ItemPool.Info[item].Category != ItemPool.Info[original].Category)
				{
					failures.Add($"{Lang.GetItemNameValue(original)} ({ItemPool.Info[original].Category}) -> {Lang.GetItemNameValue(item)} ({ItemPool.Info[item].Category})");
				}
			}
			Check.All(failures, "Category changed");
		}

		private static void ShuffleBijection()
		{
			HashSet<int> obtainable = Obtainability.Collect();
			List<int> domain = Randomizable().Where(obtainable.Contains).ToList();
			HashSet<int> image = new();
			List<string> failures = new();
			foreach (int item in domain)
			{
				int partner = ItemPool.Shuffled(item);
				if (!image.Add(partner)) failures.Add($"{Lang.GetItemNameValue(partner)} given twice");
				if (ItemPool.Info[partner].Category != ItemPool.Info[item].Category) failures.Add($"{Lang.GetItemNameValue(item)} changes category");
				if (!obtainable.Contains(partner)) failures.Add($"{Lang.GetItemNameValue(partner)} is not obtainable");
			}
			Check.All(failures, "Shuffle is not a permutation");
			Check.That(image.SetEquals(domain), "some obtainable items are never given");
		}

		private static void StackRulesValid()
		{
			List<string> failures = new();
			foreach (int item in Randomizable())
			{
				(int min, int max) = StackRules.Range(item);
				if (min < 1 || max < min || max > StackRules.MaxStackOf(item)) failures.Add($"{Lang.GetItemNameValue(item)}: {min}-{max} (max stack {StackRules.MaxStackOf(item)})");
				int scaled = StackRules.Scaled(ItemID.PlatinumCoin, 1, item);
				if (scaled < 1 || scaled > StackRules.Cap(item)) failures.Add($"{Lang.GetItemNameValue(item)} scaled to {scaled}");
			}
			Check.All(failures, "Invalid stacks");
		}

		private static void PriceFloor()
		{
			EconomyConfig cfg = EconomyConfig.Instance;
			List<string> failures = new();
			Random rng = new(5);
			foreach (int item in Randomizable())
			{
				int price = PriceRules.Price(item, rng);
				int value = StackRules.ValueOf(item);
				if (price < value) failures.Add($"{Lang.GetItemNameValue(item)}: price {price} < value {value}");
				if (price < cfg.MinimumPrice) failures.Add($"{Lang.GetItemNameValue(item)}: price {price} below the minimum");
			}
			Check.All(failures, "Shop prices below the item value (money exploit)");
		}

		private static void DropRatesValid()
		{
			List<string> failures = new();
			Random rng = new(8);
			foreach (int item in Sample(500, 7))
			{
				int denominator = DropRates.Denominator(item, ItemPool.TierOf(item), rng.Next(Tiers.Count), rng.Next(2) == 0, rng);
				if (denominator < 1) failures.Add($"{Lang.GetItemNameValue(item)}: 1/{denominator}");
			}
			Check.All(failures, "Invalid drop rates");
		}
	}
}

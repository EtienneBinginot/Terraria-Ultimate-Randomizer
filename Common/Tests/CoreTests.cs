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
		}

		private static IEnumerable<int> Randomizable() => Enumerable.Range(1, ItemPool.Info.Length - 1).Where(ItemPool.IsRandomizable);

		private static int[] Sample(int count, int salt)
		{
			int[] all = Randomizable().ToArray();
			Random rng = new(salt);
			return Enumerable.Range(0, count).Select(_ => all[rng.Next(all.Length)]).ToArray();
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
			int limit = Tiers.Clamp(ProgressionSystem.UnlockedTier + cfg.TierAllowance);
			List<string> failures = new();
			for (int i = 0; i < 2000; i++)
			{
				RollRequest request = new() { Keys = new[] { 777, i }, SourceTier = 9, OriginalItem = -1, Policy = CategoryPolicy.Weighted, TierJitterUp = 1, AllowJackpot = true };
				int item = ItemPool.Roll(request, out int tier);
				if (ItemPool.TierOf(item) > limit) failures.Add($"{Lang.GetItemNameValue(item)} tier {ItemPool.TierOf(item)} > {limit}");
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

using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Tests
{
	public static class WorldTests
	{
		public static void Register()
		{
			TestRunner.Register("RecipesSafe", RecipesSafe);
			TestRunner.Register("MiningProtected", MiningProtected);
			TestRunner.Register("DecraftSafe", DecraftSafe);
			TestRunner.Register("ShimmerCoherent", ShimmerCoherent);
			TestRunner.Register("ShopSimulation", ShopSimulation);
			TestRunner.Register("EnemySwapPool", EnemySwapPoolValid);
			TestRunner.Register("EnemySwapDaylight", EnemySwapDaylight);
			TestRunner.Register("TownOrderPermutation", TownOrderPermutation);
			TestRunner.Register("EnemyStatRanges", EnemyStatRanges);
		}

		private static void RecipesSafe()
		{
			if (!CraftingConfig.Instance.RandomizeRecipes || !RecipeSystem.Applied) Check.Skip("recipes are not randomized");
			CraftingConfig cfg = CraftingConfig.Instance;
			if (!cfg.LimitRecipeValue) Check.Skip("the value of results is not limited");
			List<string> failures = new();
			for (int i = 0; i < RecipeSystem.RecipeCount && i < Recipe.numRecipes; i++)
			{
				Recipe recipe = Main.recipe[i];
				Item result = recipe.createItem;
				if (recipe.requiredItem.Any(ingredient => ingredient.type == result.type) && result.type != RecipeSystem.OriginalResult(i))
				{
					failures.Add($"{Lang.GetItemNameValue(result.type)} is crafted from itself");
				}
				if (result.type == RecipeSystem.OriginalResult(i)) continue;
				long ingredients = recipe.requiredItem.Sum(ingredient => (long)Math.Max(StackRules.ValueOf(ingredient.type), 1) * ingredient.stack);
				long original = (long)StackRules.ValueOf(RecipeSystem.OriginalResult(i)) * RecipeSystem.OriginalStack(i);
				long max = (long)(Math.Max(ingredients, original) * cfg.RecipeValueGuard) + Item.silver;
				long value = (long)StackRules.ValueOf(result.type) * result.stack;
				if (value <= max) continue;
				// The fallback gives the cheapest item of the original's category and tier when none is cheap enough
				List<int> types = recipe.requiredItem.Select(ingredient => ingredient.type).ToList();
				int count = recipe.requiredItem.Sum(ingredient => ingredient.stack);
				List<(int type, int stack, long value)> candidates = RecipeSystem.FallbackCandidates(i, types, count, ItemPool.TierOf(result.type));
				bool cheapest = candidates.Count > 0 && candidates.All(c => c.value > max) && value <= candidates.Min(c => c.value);
				if (!cheapest) failures.Add($"{Lang.GetItemNameValue(result.type)} x{result.stack} worth {value} > {max}");
			}
			Check.All(failures, "Unsafe recipes");
		}

		private static void MiningProtected()
		{
			if (!RecipeSystem.Applied) Check.Skip("recipes are not randomized");
			if (!ProgressionConfig.Instance.ProtectPickaxeProgression) Check.Skip("mining protection is off");
			List<string> failures = new();
			for (int i = 0; i < RecipeSystem.RecipeCount && i < Recipe.numRecipes; i++)
			{
				int original = RecipeSystem.OriginalResult(i);
				if (!ItemPool.IsValid(original)) continue;
				bool mining = ItemPool.Info[original].Category == ItemCategory.OreBar || (ContentSamples.ItemsByType.TryGetValue(original, out Item sample) && sample.pick > 0);
				if (mining && Main.recipe[i].createItem.type != original) failures.Add($"{Lang.GetItemNameValue(original)} recipe changed");
			}
			Check.All(failures, "Mining progression recipes changed");
		}

		private static void DecraftSafe()
		{
			List<string> failures = new();
			int[][] decraft = ItemID.Sets.CraftingRecipeIndices;
			for (int type = 1; type < decraft.Length; type++)
			{
				if (decraft[type] == null) continue;
				foreach (int index in decraft[type])
				{
					if (index < 0 || index >= Recipe.numRecipes) failures.Add($"{Lang.GetItemNameValue(type)}: bad recipe index {index}");
					else if (Main.recipe[index].createItem.type != type) failures.Add($"{Lang.GetItemNameValue(type)} decrafts with a recipe that now gives {Lang.GetItemNameValue(Main.recipe[index].createItem.type)}");
				}
			}
			Check.All(failures, "Shimmer decrafting can duplicate items");
		}

		private static void ShimmerCoherent()
		{
			if (!CraftingConfig.Instance.RandomizeShimmer || !ShimmerSystem.Applied) Check.Skip("shimmer is not randomized");
			List<string> failures = new();
			int[] set = ItemID.Sets.ShimmerTransformToItem;
			for (int type = 1; type < set.Length; type++)
			{
				int original = ShimmerSystem.OriginalTransform(type);
				int current = set[type];
				if (original <= 0)
				{
					if (current != original) failures.Add($"{Lang.GetItemNameValue(type)} gained a transformation");
					continue;
				}
				if (current == original || !ItemPool.IsValid(current)) continue;
				if (ItemPool.Info[current].Category != ItemPool.Info[original].Category) failures.Add($"{Lang.GetItemNameValue(type)} -> {Lang.GetItemNameValue(current)} changes category");
				long max = Math.Max(StackRules.ValueOf(original), StackRules.ValueOf(type)) * 3L + Item.silver;
				if (StackRules.ValueOf(current) > max) failures.Add($"{Lang.GetItemNameValue(type)} -> {Lang.GetItemNameValue(current)} too valuable");
			}
			Check.All(failures, "Shimmer transformations");
		}

		private static void ShopSimulation()
		{
			if (!EconomyConfig.Instance.RandomizeShops) Check.Skip("shops are not randomized");
			NPC merchant = ContentSamples.NpcsByNetId[NPCID.Merchant];
			int[] stock = { ItemID.Torch, ItemID.LesserHealingPotion, ItemID.WoodenArrow, ItemID.CopperPickaxe, ItemID.CopperAxe, ItemID.Shuriken, ItemID.Bottle, ItemID.PiggyBank };
			Item[] items = new Item[40];
			for (int i = 0; i < stock.Length; i++) items[i] = new Item(stock[i]);
			Item medalItem = new(ItemID.DD2ElderCrystal) { shopSpecialCurrency = 0 };
			items[stock.Length] = medalItem;
			items[stock.Length + 1] = new Item();
			ModContent.GetInstance<ShopNPC>().ModifyActiveShop(merchant, "Terraria/Merchant/Shop", items);
			List<string> failures = new();
			HashSet<int> seen = new();
			for (int i = 0; i < stock.Length; i++)
			{
				Item item = items[i];
				if (item == null || item.IsAir) { failures.Add($"slot {i} emptied"); continue; }
				if (ItemPool.IsRandomizable(stock[i]) && !seen.Add(item.type) && EconomyConfig.Instance.NoDuplicatesInShop) failures.Add($"{Lang.GetItemNameValue(item.type)} sold twice");
				int price = item.shopCustomPrice ?? item.value;
				if (price < StackRules.ValueOf(item.type)) failures.Add($"{Lang.GetItemNameValue(item.type)} sold for {price} < value {StackRules.ValueOf(item.type)}");
			}
			if (items[stock.Length].type != ItemID.DD2ElderCrystal) failures.Add("special currency item replaced");
			if (!items[stock.Length + 1].IsAir) failures.Add("reserved slot filled");
			if (items[stock.Length + 2] != null) failures.Add("unused slot filled");
			Check.All(failures, "Shop");
		}

		private static void EnemySwapPoolValid()
		{
			int[] never = { NPCID.Creeper, NPCID.SkeletronHand, NPCID.ServantofCthulhu, NPCID.TheHungry, NPCID.DungeonGuardian, NPCID.MartianProbe, NPCID.ManEater, NPCID.SolarSolenian, NPCID.Splinterling };
			List<string> failures = new();
			foreach (int type in never)
			{
				if (EnemySwapPool.IsSwappable(type)) failures.Add($"{ContentSamples.NpcsByNetId[type].TypeName} can be swapped");
			}
			foreach (int type in new[] { NPCID.Zombie, NPCID.BlueSlime, NPCID.DemonEye })
			{
				if (!EnemySwapPool.IsSwappable(type)) failures.Add($"{ContentSamples.NpcsByNetId[type].TypeName} can't be swapped");
			}
			Check.All(failures, "Enemy swap pool");
		}

		private static void EnemySwapDaylight()
		{
			bool wasDay = Main.dayTime;
			EnemySwapMode mode = EnemiesConfig.Instance.EnemySwapMode;
			List<string> failures = new();
			// The pool must not depend on the time it was built at: rebuild it at night
			try
			{
				Main.dayTime = false;
				EnemySwapPool.Reset();
				foreach (int type in EnemySwapPool.DaylightEnemies())
				{
					if (ContentSamples.NpcsByNetId.TryGetValue(type, out NPC npc) && EnemySwapPool.FleesDaylight(npc)) failures.Add($"{npc.TypeName} in the day pool");
				}
				Check.That(EnemySwapPool.DaylightEnemies().Any(), "the day pool is empty");
			}
			finally
			{
				Main.dayTime = wasDay;
			}
			try
			{
				Main.dayTime = true;
				NPC slime = new();
				slime.SetDefaults(NPCID.BlueSlime);
				slime.position = new Microsoft.Xna.Framework.Vector2(Main.maxTilesX * 8, (float)(Main.worldSurface * 16 - 400));
				for (int i = 0; i < 40; i++)
				{
					int target = EnemySwapPool.Replacement(slime);
					if (!ContentSamples.NpcsByNetId.TryGetValue(target, out NPC sample)) continue;
					if (EnemySwapPool.FleesDaylight(sample)) failures.Add($"{sample.TypeName} by day");
					if (mode == EnemySwapMode.PerType) break;
				}
			}
			finally
			{
				Main.dayTime = wasDay;
			}
			Check.All(failures, "Daylight-fleeing enemies spawned by day");
		}

		private static void TownOrderPermutation()
		{
			int[] owners = TownNpcOrderSystem.ConditionOwners;
			int[] arrivals = TownNpcOrderSystem.Arrivals();
			Check.That(owners.OrderBy(t => t).SequenceEqual(arrivals.OrderBy(t => t)), "arrivals are not a permutation of the town NPCs");
		}

		private static void EnemyStatRanges()
		{
			EnemiesConfig cfg = EnemiesConfig.Instance;
			List<string> failures = new();
			foreach (int type in new[] { NPCID.EyeofCthulhu, NPCID.KingSlime, NPCID.SkeletronHead, NPCID.WallofFlesh, NPCID.Plantera, NPCID.MoonLordCore })
			{
				EnemyMultipliers m = EnemyStatCache.Get(type, true);
				if (cfg.RandomizeBossHealth && (m.Life < Math.Min(cfg.BossHealthMin, cfg.BossHealthMax) - 0.001f || m.Life > Math.Max(cfg.BossHealthMin, cfg.BossHealthMax) + 0.001f)) failures.Add($"boss {type} life x{m.Life}");
				if (cfg.RandomizeBossDamage && (m.Damage < Math.Min(cfg.BossDamageMin, cfg.BossDamageMax) - 0.001f || m.Damage > Math.Max(cfg.BossDamageMin, cfg.BossDamageMax) + 0.001f)) failures.Add($"boss {type} damage x{m.Damage}");
			}
			Check.All(failures, "Multipliers outside the configured range");
		}
	}
}

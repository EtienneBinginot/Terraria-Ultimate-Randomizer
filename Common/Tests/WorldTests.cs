using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.DataStructures;
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
				// A result too valuable for its recipe is swapped with another one, or the recipe keeps its vanilla result
				if (value > max) failures.Add($"{Lang.GetItemNameValue(result.type)} x{result.stack} worth {value} > {max}");
			}
			Check.All(failures, "Unsafe recipes");
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
				// A surprise (ShimmerSurpriseChance) changes category on purpose: recomputed with the live progression; when that
				// moved since the set was applied, the surprise stream alone tells (its fallback may have kept the category)
				float chance = CraftingConfig.Instance.ShimmerSurpriseChance;
				int replayed = ShimmerSystem.PickTransformation(type, original, chance, ProgressionSystem.UnlockedTier, Main.hardMode, out bool fromSurprise);
				bool surprise = replayed == current ? fromSurprise : ShimmerSystem.IsSurprise(type, original, chance);
				if (!surprise && ItemPool.Info[current].Category != ItemPool.Info[original].Category) failures.Add($"{Lang.GetItemNameValue(type)} -> {Lang.GetItemNameValue(current)} changes category");
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
				// Only the slots this mod priced (a protected item keeps its vanilla price)
				int price = item.shopCustomPrice ?? item.value;
				if (ItemPool.IsRandomizable(stock[i]) && price < PriceRules.Floor(item.type)) failures.Add($"{Lang.GetItemNameValue(item.type)} sold for {price} < floor {PriceRules.Floor(item.type)}");
			}
			if (items[stock.Length].type != ItemID.DD2ElderCrystal) failures.Add("special currency item replaced");
			if (!items[stock.Length + 1].IsAir) failures.Add("reserved slot filled");
			// The unused slots take exactly the shop-only items planned for this shop (ShopPermutation), in order, and nothing else
			const string shopName = "Terraria/Merchant/Shop";
			List<ShopSlot> added = ShopPlanner.AddedSlots(NPCID.Merchant, shopName, ShopPermutation.ItemsOf(ShopPermutation.Current, shopName),
				ProgressionSystem.UnlockedTier, Main.hardMode);
			for (int i = stock.Length + 2; i < items.Length; i++)
			{
				int a = i - (stock.Length + 2);
				if (a < added.Count)
				{
					if (items[i] == null || items[i].type != added[a].Replacement)
						failures.Add($"slot {i}: {(items[i] == null ? "empty" : Lang.GetItemNameValue(items[i].type))}, added {Lang.GetItemNameValue(added[a].Replacement)} expected");
				}
				else if (items[i] != null) failures.Add($"unused slot {i} filled with {Lang.GetItemNameValue(items[i].type)}");
			}
			// A bought item forgets its shop price (no buy-then-stack resale at the shop price)
			Item bought = new(ItemID.Torch) { shopCustomPrice = 12345 };
			ModContent.GetInstance<BoughtItem>().OnCreated(bought, new BuyItemCreationContext(new Item(), merchant));
			if (bought.shopCustomPrice != null) failures.Add($"a bought item keeps its shop price {bought.shopCustomPrice}");
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
					if (ContentSamples.NpcsByNetId.TryGetValue(type, out NPC npc) && EnemySwapPool.DaylightIntolerant(type)) failures.Add($"{npc.TypeName} in the day pool");
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
					if (EnemySwapPool.DaylightIntolerant(target)) failures.Add($"{sample.TypeName} by day");
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
			// Every enemy type stays under the total damage cap
			float cap = Math.Max(1, cfg.MaxDamageMultiplier);
			for (int type = 1; type < NPCLoader.NPCCount; type++)
			{
				float damage = EnemyStatCache.Get(type, false).Damage;
				if (damage > cap + 0.001f) failures.Add($"{type} damage x{damage} above the cap x{cap}");
			}
			Check.All(failures, "Multipliers outside the configured range");
		}
	}
}

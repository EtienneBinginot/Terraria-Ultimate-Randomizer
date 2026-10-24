using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using UltimateRandomizer.Common.Configs;

namespace UltimateRandomizer.Common.Core
{
	// Coherent stack sizes: a sword comes alone, ammo comes by dozens, never 9999 of everything
	public static class StackRules
	{
		public static int MaxStackOf(int type) =>
			ContentSamples.ItemsByType.TryGetValue(type, out Item item) ? Math.Clamp(item.maxStack, 1, Item.CommonMaxStack) : 1;

		public static int ValueOf(int type) =>
			ContentSamples.ItemsByType.TryGetValue(type, out Item item) ? Math.Max(item.value, 0) : 0;

		public static int Cap(int type)
		{
			EconomyConfig cfg = EconomyConfig.Instance;
			ItemCategory category = ItemPool.IsValid(type) ? ItemPool.Info[type].Category : ItemCategory.Misc;
			int cap = (int)Math.Round(cfg.StackCaps.Get(category) * cfg.StackMultiplier);
			return Math.Clamp(cap, 1, MaxStackOf(type));
		}

		public static (int min, int max) Range(int type)
		{
			int cap = Cap(type);
			ItemCategory category = ItemPool.IsValid(type) ? ItemPool.Info[type].Category : ItemCategory.Misc;
			int min = category is ItemCategory.Ammo or ItemCategory.Block or ItemCategory.OreBar ? Math.Max(1, cap / 4) : 1;
			return (min, cap);
		}

		// A stack range for a random drop slot, stable for the world
		public static (int min, int max) RollRange(int type, Random rng) => Roll(Range(type), rng);

		private static (int min, int max) Roll((int min, int max) range, Random rng)
		{
			int low = rng.Next(range.min, range.max + 1);
			int high = rng.Next(low, range.max + 1);
			return (low, high);
		}

		// Openers and summons used up at once (the Temple Key, the Lihzahrd Power Cell, the dungeon and biome keys, the boss and
		// event summons, the Celestial Sigil): one at a time from a random source, a pile of them is no reward
		private static readonly HashSet<int> SingleUseItems = new()
		{
			ItemID.TempleKey, ItemID.LihzahrdPowerCell, ItemID.ShadowKey, ItemID.GoldenKey, ItemID.JungleKey, ItemID.CorruptionKey, ItemID.CrimsonKey,
			ItemID.HallowedKey, ItemID.FrozenKey, ItemID.DungeonDesertKey, ItemID.LightKey, ItemID.NightKey, ItemID.CelestialSigil
		};

		public static bool IsSingleUse(int type) => SingleUseItems.Contains(type) || Protection.IsBossSummon(type);

		// A throwing weapon used up as it is thrown (shurikens, throwing knives, grenades...)
		public static bool IsThrown(int type) =>
			ItemPool.IsValid(type) && ItemPool.Info[type].Category is ItemCategory.WeaponMelee or ItemCategory.WeaponRanged or ItemCategory.WeaponMagic or ItemCategory.WeaponSummon
				or ItemCategory.WeaponOther && ContentSamples.ItemsByType.TryGetValue(type, out Item item) && item.consumable && item.maxStack > 1;

		// The caps of a random extra drop or surprise (extra slots, small frequent drops, permutation surprises, synthetic
		// entries, giant bonus drops; bag extras are the boss's): the drop chances stay, the stacks are small. Potions and
		// food a few at a time, ammo and throwing weapons by dozens, fewer the further the item is above its source
		public const int ExtraConsumableCap = 3;
		public const int ExtraAmmoCap = 50;
		public const int ExtraThrownCap = 30;
		public const int ExtraAmmoFloor = 10;

		// Pure (with the item pool and the stack caps): the stack range an extra drop of this item may have on a source of
		// this tier, before the roll. Single use items: 1. Materials, blocks, ores and the rest: Range
		public static (int min, int max) ExtraRange(int type, int sourceTier)
		{
			if (IsSingleUse(type)) return (1, 1);
			ItemCategory category = ItemPool.IsValid(type) ? ItemPool.Info[type].Category : ItemCategory.Misc;
			int cap = Cap(type);
			if (category is ItemCategory.Potion or ItemCategory.Food) return (1, Math.Clamp(Math.Min(cap, ExtraConsumableCap), 1, MaxStackOf(type)));
			bool thrown = IsThrown(type);
			if (category != ItemCategory.Ammo && !thrown) return Range(type);
			// 50 ammo of the source's tier, 25 a tier above, 16, 12, then 10; the stack caps still apply to ammo (not to throwing
			// weapons: the weapon cap is 1)
			int gap = Math.Max(0, ItemPool.TrueTierOf(type) - sourceTier);
			int max = Math.Max((thrown ? ExtraThrownCap : ExtraAmmoCap) / (1 + gap), ExtraAmmoFloor);
			if (!thrown) max = Math.Min(max, cap);
			max = Math.Clamp(max, 1, MaxStackOf(type));
			return (Math.Max(1, max / 5), max);
		}

		// ExtraRange, rolled: a stack range stable for the world
		public static (int min, int max) RollExtraRange(int type, int sourceTier, Random rng) => Roll(ExtraRange(type, sourceTier), rng);

		// Stack for an item replacing another: keeps roughly the same total value when enabled. A single use item: one
		public static int Scaled(int originalType, int originalStack, int newType)
		{
			if (IsSingleUse(newType)) return 1;
			int cap = Cap(newType);
			if (!EconomyConfig.Instance.ValueBasedStacks) return Math.Clamp(originalStack, 1, cap);
			long originalValue = Math.Max(ValueOf(originalType), 1) * (long)Math.Max(originalStack, 1);
			long newValue = Math.Max(ValueOf(newType), 1);
			return (int)Math.Clamp(Math.Round((double)originalValue / newValue), 1, cap);
		}
	}
}

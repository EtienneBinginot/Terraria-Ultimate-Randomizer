using System;
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
		public static (int min, int max) RollRange(int type, Random rng)
		{
			(int min, int max) = Range(type);
			int low = rng.Next(min, max + 1);
			int high = rng.Next(low, max + 1);
			return (low, high);
		}

		// Stack for an item replacing another: keeps roughly the same total value when enabled
		public static int Scaled(int originalType, int originalStack, int newType)
		{
			int cap = Cap(newType);
			if (!EconomyConfig.Instance.ValueBasedStacks) return Math.Clamp(originalStack, 1, cap);
			long originalValue = Math.Max(ValueOf(originalType), 1) * (long)Math.Max(originalStack, 1);
			long newValue = Math.Max(ValueOf(newType), 1);
			return (int)Math.Clamp(Math.Round((double)originalValue / newValue), 1, cap);
		}
	}
}

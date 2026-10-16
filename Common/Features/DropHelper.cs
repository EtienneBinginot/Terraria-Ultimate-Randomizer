using System;
using Terraria;
using Terraria.ID;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	public static class DropHelper
	{
		// Context of the items dropped by our own extra-drop rule, so they are not replaced a second time
		public const string ExtraDropContext = "UltimateRandomizer_ExtraDrop";

		// Items kept as they are: progression items, excluded items (coins, hearts...) and bags (their content is replaced instead)
		public static bool KeepOriginal(int itemType) =>
			!ItemPool.IsRandomizable(itemType) || ItemPool.Info[itemType].Category == ItemCategory.Bag;

		// The item replacing an original drop of a source (enemy or bag), the same every time in a world
		public static int Replacement(int rollKind, int sourceKey, int originalItem, int sourceTier, int minLimit, bool lucky)
		{
			if (KeepOriginal(originalItem)) return originalItem;
			RollRequest request = new()
			{
				Keys = new[] { rollKind, sourceKey, DetRandom.ItemKey(originalItem) },
				SourceTier = Tiers.Clamp(Math.Max(sourceTier, ItemPool.Info[originalItem].Tier)),
				OriginalItem = originalItem,
				Policy = CategoryPolicy.SameAsOriginal,
				TierJitterDown = 0,
				TierJitterUp = 1,
				AllowJackpot = true,
				IgnoreGating = lucky,
				MinLimit = minLimit
			};
			return ItemPool.Roll(request);
		}

		public static bool RollLucky() => Main.rand.NextFloat() * 100 < ProgressionConfig.Instance.RuntimeJackpotChance;

		// Stack range of a replacement item, from the original range (keeps roughly the same value)
		public static (int min, int max) ScaledRange(int originalItem, int stackMin, int stackMax, int newType)
		{
			int min = StackRules.Scaled(originalItem, stackMin, newType);
			int max = StackRules.Scaled(originalItem, stackMax, newType);
			return (min, Math.Max(min, max));
		}

		// Turns an item that was just spawned into another one, keeping its position and slot.
		// Done inside OnSpawn so the new type is what gets synced; other mods' OnSpawn running after ours see the
		// replaced item but their per-item data from before the change (known limitation, vanilla content is fine).
		public static void ChangeSpawnedItem(Item item, int newType, int stack)
		{
			if (newType <= ItemID.None || newType == item.type) return;
			item.ChangeItemType(newType);
			item.stack = Math.Max(1, stack);
			item.Prefix(-1);
		}
	}
}

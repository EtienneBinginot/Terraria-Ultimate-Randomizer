using System;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	// Shimmer transmutations: each existing transformation gives another item of the same category and tier
	public class ShimmerSystem : ModSystem
	{
		private static int[] original;
		private static bool applied;

		public static int OriginalTransform(int type) => original != null && type < original.Length ? original[type] : -1;

		public static bool Applied => applied;

		public override void PostSetupRecipes()
		{
			original = (int[])ItemID.Sets.ShimmerTransformToItem.Clone();
			SeedSystem.OnSeedChanged += Refresh;
			ProgressionSystem.OnTierChanged += Refresh;
		}

		public override void Unload()
		{
			Restore();
			original = null;
		}

		private static void Refresh()
		{
			if (SeedSystem.WorldReady && CraftingConfig.Instance.RandomizeShimmer && ItemPool.Ready) Apply();
			else Restore();
		}

		private static void Apply()
		{
			if (original == null) return;
			int[] set = ItemID.Sets.ShimmerTransformToItem;
			for (int type = 1; type < original.Length && type < set.Length; type++)
			{
				int target = original[type];
				// Only existing transformations are changed: items that decraft keep decrafting
				if (target <= 0 || !ItemPool.IsRandomizable(type) || !ItemPool.IsRandomizable(target))
				{
					set[type] = target;
					continue;
				}
				set[type] = PickTransformation(type, target);
			}
			applied = true;
		}

		// Same category and tier as the original result, and never worth much more (no shimmer money machine)
		private static int PickTransformation(int type, int target)
		{
			long maxValue = Math.Max(StackRules.ValueOf(target), StackRules.ValueOf(type)) * 3L + Terraria.Item.silver;
			for (int attempt = 0; attempt < 6; attempt++)
			{
				RollRequest request = new()
				{
					Keys = new[] { RollKey.Shimmer, DetRandom.ItemKey(type), attempt },
					SourceTier = ItemPool.Info[target].Tier,
					OriginalItem = target,
					Policy = CategoryPolicy.SameAsOriginal,
					TierJitterDown = 0,
					TierJitterUp = 0,
					AllowJackpot = false,
					// The player already holds an item of that tier: no progression to protect
					IgnoreGating = true
				};
				int replacement = ItemPool.Roll(request);
				if (replacement != type && StackRules.ValueOf(replacement) <= maxValue) return replacement;
			}
			return target;
		}

		private static void Restore()
		{
			if (!applied || original == null) return;
			Array.Copy(original, ItemID.Sets.ShimmerTransformToItem, Math.Min(original.Length, ItemID.Sets.ShimmerTransformToItem.Length));
			applied = false;
		}
	}
}

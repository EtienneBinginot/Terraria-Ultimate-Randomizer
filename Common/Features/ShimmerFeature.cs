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
				RollRequest request = new()
				{
					Keys = new[] { RollKey.Shimmer, DetRandom.ItemKey(type) },
					SourceTier = ItemPool.Info[target].Tier,
					OriginalItem = target,
					Policy = CategoryPolicy.SameAsOriginal,
					TierJitterDown = 0,
					TierJitterUp = 0,
					AllowJackpot = false
				};
				int replacement = ItemPool.Roll(request);
				set[type] = replacement == type ? target : replacement;
			}
			applied = true;
		}

		private static void Restore()
		{
			if (!applied || original == null) return;
			Array.Copy(original, ItemID.Sets.ShimmerTransformToItem, Math.Min(original.Length, ItemID.Sets.ShimmerTransformToItem.Length));
			applied = false;
		}
	}
}

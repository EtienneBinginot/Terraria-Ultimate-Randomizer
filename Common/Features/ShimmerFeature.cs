using System;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	// Shimmer transmutations: each existing transformation gives another item of the same category, of its tier or one above
	// (within the progression limit), or for a share of them (ShimmerSurpriseChance) of any category. Applied on every machine
	// from the seed, the synced settings and the synced progression (boss flags, hardmode), so all agree
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
				set[type] = PickTransformation(type, target, CraftingConfig.Instance.ShimmerSurpriseChance, ProgressionSystem.UnlockedTier, Main.hardMode);
			}
			applied = true;
		}

		public const int TierJitterUp = 1;

		// Is this transmutation a category surprise (its own stream: the same for every attempt and every progression)?
		public static bool IsSurprise(int type, int target, float surpriseChance) =>
			ItemPool.IsSurprise(target, surpriseChance, DetRandom.Get(RollKey.Shimmer, DetRandom.ItemKey(type), RollKey.Surprise));

		// The value cap of a transmutation: never worth much more than the item or its vanilla result (no shimmer money machine)
		public static long MaxValue(int type, int target) => Math.Max(StackRules.ValueOf(target), StackRules.ValueOf(type)) * 3L + Item.silver;

		// Pure (seed, settings, the given progression): the original result's tier or one above, gated like any roll (the
		// progression limit and its allowance, never below the original result's own tier: the player already holds an item
		// of that level), never above the hardmode wall before hardmode, never worth more than MaxValue. A surprise
		// (surpriseChance) draws its category with the loot weights; when none of its attempts fits, the same category is tried.
		// A stackable result stays stackable (a stack of ammo or blocks shimmered into one sword each)
		public static int PickTransformation(int type, int target, float surpriseChance, int unlockedTier, bool hardMode) =>
			PickTransformation(type, target, surpriseChance, unlockedTier, hardMode, out _);

		// fromSurprise: the result came from the surprise draw (pass 0), not from the same-category fallback
		public static int PickTransformation(int type, int target, float surpriseChance, int unlockedTier, bool hardMode, out bool fromSurprise)
		{
			fromSurprise = false;
			long maxValue = MaxValue(type, target);
			bool stackable = StackRules.MaxStackOf(target) > 1;
			bool surprise = IsSurprise(type, target, surpriseChance);
			int targetTier = ItemPool.Info[target].Tier;
			for (int pass = surprise ? 0 : 1; pass < 2; pass++)
			{
				for (int attempt = 0; attempt < 6; attempt++)
				{
					RollRequest request = new()
					{
						Keys = pass == 0 ? new[] { RollKey.Shimmer, DetRandom.ItemKey(type), attempt, RollKey.Surprise } : new[] { RollKey.Shimmer, DetRandom.ItemKey(type), attempt },
						SourceTier = targetTier,
						OriginalItem = target,
						Policy = pass == 0 ? CategoryPolicy.Weighted : CategoryPolicy.SameAsOriginal,
						TierJitterDown = 0,
						TierJitterUp = TierJitterUp,
						AllowJackpot = false,
						MinLimit = targetTier,
						GatingTier = unlockedTier,
						GatingHardMode = hardMode,
						// Never above the jitter (a surprise category empty at that tier would look higher), no hardmode item before
						// hardmode (Refresh applies them again when the tier changes)
						MaxTier = Math.Min(hardMode ? Tiers.Max : ItemPool.PreHardmodeCap, targetTier + TierJitterUp)
					};
					int replacement = ItemPool.Roll(request);
					// A surprise never gives a bag (a cheap item shimmered into a boss bag would be a loot machine)
					if (pass == 0 && ItemPool.IsValid(replacement) && ItemPool.Info[replacement].Category == ItemCategory.Bag) continue;
					if (stackable && replacement > ItemID.None && StackRules.MaxStackOf(replacement) <= 1) continue;
					if (replacement != type && replacement > ItemID.None && StackRules.ValueOf(replacement) <= maxValue)
					{
						fromSurprise = pass == 0;
						return replacement;
					}
				}
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

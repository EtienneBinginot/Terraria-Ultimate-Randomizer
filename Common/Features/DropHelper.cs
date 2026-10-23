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

		// Items kept as they are: progression items, excluded items (coins, hearts...), bags (their content is replaced instead)
		// and the life and mana upgrades (a crate's Life Crystal stays one)
		public static bool KeepOriginal(int itemType) =>
			!ItemPool.IsRandomizable(itemType) || ItemPool.Info[itemType].Category == ItemCategory.Bag || ItemPool.IsSpecial(itemType);

		// The item replacing an original drop of a source (enemy or bag): the same every time in a world, unless a
		// surprise happens (surpriseRng null = never, for the journal and the tests). wildRng: runtime drops only (madness)
		// wildMultiplier: scales the madness chance (RandomNpcDropRule.JackpotWorth)
		public static int Replacement(int rollKind, int sourceKey, int originalItem, int sourceTier, int minLimit, bool lucky, Random surpriseRng = null, Random wildRng = null, float wildMultiplier = 1f)
		{
			// ItemPool.LastJackpotGap then says whether this replacement is a jackpot
			ItemPool.ClearJackpot();
			if (KeepOriginal(originalItem)) return originalItem;
			RollRequest request = ReplacementRequest(rollKind, sourceKey, originalItem, sourceTier, minLimit);
			request.IgnoreGating = lucky;
			request.Wild = wildRng != null && wildMultiplier > 0f;
			request.WildRng = wildRng;
			request.WildChanceMultiplier = wildMultiplier;
			return ItemPool.RollReplacement(request, LootConfig.Instance.SurpriseChance, surpriseRng);
		}

		private static RollRequest ReplacementRequest(int rollKind, int sourceKey, int originalItem, int sourceTier, int minLimit) => new()
		{
			Keys = new[] { rollKind, sourceKey, DetRandom.ItemKey(originalItem) },
			SourceTier = Tiers.Clamp(Math.Max(sourceTier, ItemPool.Info[originalItem].Tier)),
			OriginalItem = originalItem,
			Policy = CategoryPolicy.SameAsOriginal,
			TierJitterDown = 0,
			TierJitterUp = 1,
			AllowJackpot = true,
			MinLimit = minLimit
		};

		// Tiers by which progression still locks an item of a final plan (0: unlocked). Enemy drops and bag contents are not
		// replaced by a stand-in while locked: they simply do not drop. Only Coherent mode gates (Shuffle and Chaos never did)
		public static int LockGap(int item, int minLimit)
		{
			if (GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent || !ItemPool.IsValid(item)) return 0;
			return Math.Max(0, ItemPool.TrueTierOf(item) - ItemPool.GatingLimit(minLimit));
		}

		// What an original drop (enemy) or bag item becomes as it spawns: the final replacement (everything unlocked, the one
		// plans and the codex list). While progression locks it: nothing (0), unless the kill is lucky and keeps it as rarely
		// as a lucky roll that far above the limit (ItemPool.LuckyKeepChance), a jackpot when useful. Unlocked: maybe a
		// surprise (a gated roll: just something else) or the madness tail (wildMultiplier > 0).
		// rng: the runtime rng (null: no surprise, luck nor madness). jackpotGap: the jackpot mark of the result
		public static int SpawnReplacement(int rollKind, int sourceKey, int originalItem, int sourceTier, int minLimit, bool lucky, Random rng, float wildMultiplier, out int jackpotGap)
		{
			jackpotGap = 0;
			ItemPool.ClearJackpot();
			if (KeepOriginal(originalItem)) return originalItem;
			RollRequest request = ReplacementRequest(rollKind, sourceKey, originalItem, sourceTier, minLimit);
			request.IgnoreGating = true;
			int planned = ItemPool.Roll(request);
			int locked = LockGap(planned, minLimit);
			if (locked > 0)
			{
				if (!lucky || rng == null || locked > 1 && rng.NextDouble() >= ItemPool.LuckyKeepChance(locked)) return 0;
				jackpotGap = ItemPool.IsJackpotWorthy(planned) ? locked : 0;
				return planned;
			}
			if (rng == null) return planned;
			if (ItemPool.IsSurprise(originalItem, LootConfig.Instance.SurpriseChance, rng))
			{
				RollRequest surprise = request;
				surprise.Keys = new[] { rollKind, sourceKey, DetRandom.ItemKey(originalItem), RollKey.Surprise, rng.Next() };
				surprise.Policy = CategoryPolicy.Weighted;
				surprise.IgnoreGating = false;
				return ItemPool.Roll(surprise);
			}
			if (wildMultiplier > 0f)
			{
				RollRequest wild = request;
				wild.IgnoreGating = false;
				wild.Wild = true;
				wild.WildRng = rng;
				wild.WildChanceMultiplier = wildMultiplier;
				int item = ItemPool.Roll(wild);
				// Without a wild roll, the gated item is a stand-in: the planned one stays
				if (ItemPool.LastJackpotGap > 0)
				{
					jackpotGap = ItemPool.LastJackpotGap;
					return item;
				}
			}
			return planned;
		}

		// worth: RandomNpcDropRule.JackpotWorth of the kill (0: never lucky). The same chance at every tier
		public static bool RollLucky(float worth = 1f) => worth > 0f && Main.rand.NextFloat() * 100 < LuckyChance(worth);

		// The lucky chance (%) of a kill now (the pacing model uses it too)
		public static float LuckyChance(float worth = 1f) =>
			ProgressionConfig.Instance.RuntimeJackpotChance * worth;

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

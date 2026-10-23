using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	// Each fish gives another item of the same category and tier (always the same one in a world)
	public class FishingPlayer : ModPlayer
	{
		public override void CatchFish(FishingAttempt attempt, ref int itemDrop, ref int npcSpawn, ref AdvancedPopupRequest sonar, ref Vector2 sonarPosition)
		{
			LootConfig cfg = LootConfig.Instance;
			if (!cfg.RandomizeFishing || !SeedSystem.WorldReady || !ItemPool.Ready) return;
			if (itemDrop <= ItemID.None || itemDrop == attempt.questFish || !ItemPool.IsRandomizable(itemDrop)) return;
			// Crates stay crates: their content is randomized when opened
			if (cfg.CratesStayCrates && ItemID.Sets.IsFishingCrate[itemDrop]) return;
			RollRequest request = new()
			{
				Keys = new[] { RollKey.Fishing, DetRandom.ItemKey(itemDrop) },
				SourceTier = ItemPool.Info[itemDrop].Tier,
				OriginalItem = itemDrop,
				Policy = CategoryPolicy.SameAsOriginal,
				TierJitterDown = 0,
				TierJitterUp = 1,
				AllowJackpot = true,
				// No hardmode catch before hardmode: categoryLimit lets a replacement reach the fish's own tier
				MaxTier = ItemPool.HardmodeWall
			};
			itemDrop = ItemPool.RollReplacement(request, cfg.SurpriseChance, new System.Random(Main.rand.Next()));
		}
	}
}

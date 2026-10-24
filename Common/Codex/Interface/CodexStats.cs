using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Codex.Interface
{
	// Numbers shown by the codex: planned drop chances (from the world's drop plan) and spawn shares (from the sightings)
	public static class CodexStats
	{
		// Planned chances by enemy (codex key), valid until something new is listed: the plan only changes with the
		// seed, the settings or the tier, which all move CodexSystem.StructureVersion
		private static readonly Dictionary<int, Dictionary<int, float>> chances = new();
		private static readonly Dictionary<int, Dictionary<int, int>> locks = new();
		private static long chancesVersion = -1;
		private static bool chancesExpert;
		private static int chancesTier = -1;
		private static bool chancesHardmode;

		private static void CheckVersion()
		{
			if (chancesVersion == CodexSystem.StructureVersion && chancesExpert == Main.expertMode && chancesTier == ProgressionSystem.UnlockedTier && chancesHardmode == Main.hardMode) return;
			chances.Clear();
			locks.Clear();
			chancesVersion = CodexSystem.StructureVersion;
			chancesExpert = Main.expertMode;
			chancesTier = ProgressionSystem.UnlockedTier;
			chancesHardmode = Main.hardMode;
		}

		// Chance per kill of each item the enemy can drop now (the chances of its locked drops included: Redistribute). An
		// item planned several times (original drop and extra slot...) gets the chance of dropping at least once. The loot
		// bonus of strong enemies can raise it.
		public static Dictionary<int, float> PlannedChances(int netId)
		{
			CheckVersion();
			if (chances.TryGetValue(netId, out Dictionary<int, float> cached)) return cached;
			List<PlannedDrop> plan = RandomNpcDropRule.Plan(netId, Main.expertMode, includeLocked: true);
			Dictionary<int, float> result = new();
			Dictionary<int, int> locked = new();
			List<PlannedDrop> unlocked = plan.FindAll(d => !d.Locked);
			foreach (PlannedDrop drop in plan)
			{
				if (drop.Item <= ItemID.None) continue;
				if (!drop.Locked)
				{
					if (!result.ContainsKey(drop.Item)) result[drop.Item] = CombinedChance(unlocked, drop.Item);
					continue;
				}
				int unlock = ItemPool.UnlockTierOf(drop.Item);
				locked[drop.Item] = locked.TryGetValue(drop.Item, out int known) ? Math.Min(known, unlock) : unlock;
			}
			// An item also planned unlocked (another entry) drops now
			foreach (int item in result.Keys) locked.Remove(item);
			if (chances.Count > 500)
			{
				chances.Clear();
				locks.Clear();
			}
			chances[netId] = result;
			locks[netId] = locked;
			return result;
		}

		// The items of the enemy's final plan that progression still locks, with the tier that unlocks each
		// (ItemPool.UnlockTier of its true tier): the codex greys them with "Unlocked: defeat X"
		public static Dictionary<int, int> LockedDrops(int netId)
		{
			PlannedChances(netId);
			return locks[netId];
		}

		// "Unlocked: defeat the Eye of Cthulhu"...
		public static string UnlockText(int tier) => CodexText.Get("LockedBy." + (Tier)Tiers.Clamp(tier));

		// The chance of dropping at least once, from every planned entry of the item
		public static float CombinedChance(IEnumerable<PlannedDrop> plan, int item)
		{
			float missing = 1f;
			foreach (PlannedDrop drop in plan)
			{
				if (drop.Item == item) missing *= 1f - Math.Clamp(drop.Chance, 0f, 1f);
			}
			return 1f - missing;
		}

		private static readonly Dictionary<int, List<PlannedDrop>> luckyPlans = new();
		private static long luckyVersion = -1;
		private static bool luckyExpert;

		// The same chance once the enemy's drops are all unlocked (lucky plan): what the hints state, on every machine.
		// It equals what the codex shows once the progression reaches it
		public static float LuckyChance(int netId, int item) => CombinedChance(LuckyPlan(netId), item);

		// The enemy's drop plan once its drops are all unlocked (cached until the hint facts are forgotten)
		public static List<PlannedDrop> LuckyPlan(int netId)
		{
			if (luckyVersion != Codex.HintFacts.CacheVersion || luckyExpert != Main.expertMode)
			{
				luckyPlans.Clear();
				luckyVersion = Codex.HintFacts.CacheVersion;
				luckyExpert = Main.expertMode;
			}
			if (!luckyPlans.TryGetValue(netId, out List<PlannedDrop> plan))
			{
				if (luckyPlans.Count > 500) luckyPlans.Clear();
				luckyPlans[netId] = plan = RandomNpcDropRule.Plan(netId, Main.expertMode, lucky: true, planning: false);
			}
			return plan;
		}

		// null: not in the plan (a madness drop, a drop of other settings, an event...)
		public static float? DropChance(int netId, int item) => PlannedChances(netId).TryGetValue(item, out float chance) ? chance : null;

		// A locked drop says so instead of a chance
		public static string DropChanceText(int netId, int item) =>
			DropChance(netId, item) == null && LockedDrops(netId).ContainsKey(item) ? CodexText.Get("Locked") : Percent(DropChance(netId, item));

		// The hover of a drop's chance: what unlocks it when locked
		public static string DropChanceHover(int netId, int item, string chanceHover) =>
			DropChance(netId, item) == null && LockedDrops(netId).TryGetValue(item, out int tier) ? UnlockText(tier) : chanceHover;

		// "12.5%" ("12,5 %" in French), "?" when unknown
		public static string Percent(float? value)
		{
			if (value == null) return CodexText.Get("ChanceUnknown");
			float percent = value.Value * 100f;
			string number = percent >= 99.995f ? "100" : percent >= 10f ? percent.ToString("0.#") : percent >= 0.01f ? percent.ToString("0.##") : "<0.01";
			return CodexText.Get("Percent", number);
		}
	}
}

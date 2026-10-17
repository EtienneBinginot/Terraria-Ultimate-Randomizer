using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Codex.Interface
{
	// Numbers shown by the codex: planned drop chances (from the world's drop plan) and spawn shares (from the sightings)
	public static class CodexStats
	{
		// Planned chances by enemy (codex key), valid until something new is listed: the plan only changes with the
		// seed, the settings or the tier, which all move CodexSystem.StructureVersion
		private static readonly Dictionary<int, Dictionary<int, float>> chances = new();
		private static long chancesVersion = -1;
		private static bool chancesExpert;

		// Chance per kill of each item the enemy can drop now. An item planned several times (original drop and extra
		// slot...) gets the chance of dropping at least once. The loot bonus of strong enemies can raise it.
		public static Dictionary<int, float> PlannedChances(int netId)
		{
			if (chancesVersion != CodexSystem.StructureVersion || chancesExpert != Main.expertMode)
			{
				chances.Clear();
				chancesVersion = CodexSystem.StructureVersion;
				chancesExpert = Main.expertMode;
			}
			if (chances.TryGetValue(netId, out Dictionary<int, float> cached)) return cached;
			Dictionary<int, float> miss = new();
			foreach (PlannedDrop drop in RandomNpcDropRule.Plan(netId, Main.expertMode))
			{
				if (drop.Item <= ItemID.None) continue;
				float chance = Math.Clamp(drop.Chance, 0f, 1f);
				miss[drop.Item] = miss.GetValueOrDefault(drop.Item, 1f) * (1f - chance);
			}
			Dictionary<int, float> result = new(miss.Count);
			foreach ((int item, float missing) in miss) result[item] = 1f - missing;
			if (chances.Count > 500) chances.Clear();
			chances[netId] = result;
			return result;
		}

		// null: not in the plan (a madness drop, a drop of other settings, an event...)
		public static float? DropChance(int netId, int item) => PlannedChances(netId).TryGetValue(item, out float chance) ? chance : null;

		public static string DropChanceText(int netId, int item) => Percent(DropChance(netId, item));

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

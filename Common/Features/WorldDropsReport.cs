using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.GameContent.Bestiary;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	// Permutation style, client option ShowWorldDrops: the bestiary and Recipe Browser show the drops of this world (the
	// permuted entries, the extra and guaranteed drops) instead of vanilla's. While Showing, the silenced wrappers
	// (PermutableRule) report nothing and RandomNpcDropRule / BossBagRule report their world plan; otherwise every rule
	// reports vanilla. Both readers keep snapshots taken at load (BestiaryDatabase.Merge, Recipe Browser's LootCache), so
	// they are rebuilt on the client once the world plan is ready, after each seed or config change, and on leaving the
	// world (vanilla again). Bag contents are read live by Recipe Browser (GetRulesForItemID + ReportDroprates). Client only:
	// the server has no bestiary, and its reports stay vanilla
	public static class WorldDropsReport
	{
		public static bool Showing { get; private set; }

		// The report of this net ID's rules follows the world plan
		public static bool Shows(int netId) => Showing && (DropPermutation.Active(netId) || BossPermutation.Active(netId));

		public static bool ShowsBag(int bagType) => Showing && BossPermutation.BagActive(bagType);

		// The world's drops should be shown now (client, in a world, permutation style)
		public static bool Wanted() => !Main.dedServ && ClientConfig.Instance.ShowWorldDrops && SeedSystem.WorldReady && ItemPool.Ready
			&& LootConfig.Instance.RandomizeMobDrops && LootConfig.Instance.DropTableStyle == DropTableStyle.Permutation;

		// What a plan entry looks like in a report: the entries our rules roll (the others are reported by their own rules)
		public static IEnumerable<PlannedDrop> Reported(IEnumerable<PlannedDrop> plan) =>
			plan.Where(d => d.Item > ItemID.None && (d.Rolled || d.Extra || d.Guaranteed));

		// The final plan (everything unlocked: a locked entry still drops on a lucky kill), stable through progression so
		// that the snapshots don't need rebuilding at each boss
		public static IEnumerable<PlannedDrop> NpcPlan(int netId)
		{
			if (plans != null && plans.TryGetValue(netId, out List<PlannedDrop> plan)) return plan;
			plan = Reported(RandomNpcDropRule.Plan(netId, Main.expertMode, lucky: true)).ToList();
			if (plans != null) plans[netId] = plan;
			return plan;
		}

		// During a rebuild: the plans already computed (the bestiary and Recipe Browser read every net ID)
		private static Dictionary<int, List<PlannedDrop>> plans;

		public static IEnumerable<PlannedDrop> BagPlanned(int bagType) => Reported(BagPlan.Plan(bagType, lucky: true));

		public static void Report(IEnumerable<PlannedDrop> plan, List<DropRateInfo> drops, DropRateInfoChainFeed ratesInfo)
		{
			foreach (PlannedDrop drop in plan)
			{
				List<IItemDropRuleCondition> conditions = null;
				if (ratesInfo.conditions != null && ratesInfo.conditions.Count > 0) conditions = new List<IItemDropRuleCondition>(ratesInfo.conditions);
				if (drop.Conditions != null && drop.Conditions.Count > 0) (conditions ??= new List<IItemDropRuleCondition>()).AddRange(drop.Conditions.Where(c => c != null));
				int min = Math.Max(1, drop.StackMin);
				drops.Add(new DropRateInfo(drop.Item, min, Math.Max(min, drop.StackMax), Math.Clamp(drop.Chance, 0f, 1f) * ratesInfo.parentDroprateChance, conditions));
			}
		}

		// The net IDs whose bestiary drops are rebuilt: those with wrapped rules (whatever the style, so a restore covers
		// every entry a rebuild changed). Other entries (modded NPCs...) keep what Merge gave them at load
		private static List<int> rebuilt;

		private static IEnumerable<int> RebuiltIds() => rebuilt ??= ContentSamples.NpcsByNetId.Keys
			.Where(n => DropPermutation.IsPermutable(n) || BossPermutation.IsSource(n)).OrderBy(n => n).ToList();

		private static bool bestiaryFailed;
		private static bool browserFailed;

		// show: the world's drops (Wanted) or vanilla. Main thread, client
		public static void Rebuild(bool show)
		{
			Showing = show;
			if (Main.dedServ) return;
			plans = new Dictionary<int, List<PlannedDrop>>();
			try
			{
				RebuildReaders();
			}
			finally
			{
				plans = null;
			}
		}

		private static void RebuildReaders()
		{
			if (!bestiaryFailed)
			{
				try
				{
					// As BestiaryDatabase.ExtractDropsForNPC (private, called by Merge for every net ID at load), for our NPCs only:
					// Merge only appends, the old elements are removed first
					foreach (int netId in RebuiltIds())
					{
						BestiaryEntry entry = Main.BestiaryDB.FindEntryByNPCID(netId);
						if (entry == null) continue;
						entry.Info.RemoveAll(i => i != null && i.GetType() == typeof(ItemDropBestiaryInfoElement));
						foreach (DropRateInfo info in Capture(netId)) entry.Info.Add(new ItemDropBestiaryInfoElement(info));
					}
				}
				catch (Exception e)
				{
					bestiaryFailed = true;
					ModContent.GetInstance<UltimateRandomizer>().Logger.Warn($"Could not rebuild the bestiary drops: {e}");
				}
			}
			RefreshRecipeBrowser();
		}

		private static List<DropRateInfo> Capture(int netId)
		{
			List<DropRateInfo> drops = new();
			DropRateInfoChainFeed feed = new(1f);
			foreach (IItemDropRule rule in Main.ItemDropsDB.GetRulesForNPCID(netId, false)) rule.ReportDroprates(drops, feed);
			return drops;
		}

		// Recipe Browser (optional) fills RecipeBrowser.LootCache.instance.lootInfos (item -> net IDs dropping it) once at
		// load (LootCacheManager.Setup: a new LootCache, then GetRulesForNPCID + ReportDroprates for every net ID, no disk
		// cache). Same loop here, but the new cache is filled before it is published: its craft path search reads it from
		// another thread
		private static void RefreshRecipeBrowser()
		{
			if (browserFailed || !ModLoader.TryGetMod("RecipeBrowser", out Mod browser)) return;
			try
			{
				Type cacheType = browser.Code?.GetType("RecipeBrowser.LootCache");
				FieldInfo instance = cacheType?.GetField("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
				FieldInfo infos = cacheType?.GetField("lootInfos", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
				if (instance == null || infos == null || infos.FieldType != typeof(Dictionary<int, List<int>>)) throw new MissingMemberException("RecipeBrowser.LootCache.instance / lootInfos");
				object cache = Activator.CreateInstance(cacheType);
				Dictionary<int, List<int>> map = (Dictionary<int, List<int>>)infos.GetValue(cache);
				for (int netId = -65; netId < NPCLoader.NPCCount; netId++)
				{
					foreach (DropRateInfo info in Capture(netId))
					{
						if (!map.TryGetValue(info.itemId, out List<int> npcs)) map.Add(info.itemId, npcs = new List<int>());
						if (!npcs.Contains(netId)) npcs.Add(netId);
					}
				}
				instance.SetValue(null, cache);
			}
			catch (Exception e)
			{
				browserFailed = true;
				ModContent.GetInstance<UltimateRandomizer>().Logger.Warn($"Could not refresh Recipe Browser's drop cache: {e}");
			}
		}

		public static void Unload()
		{
			Showing = false;
			rebuilt = null;
			bestiaryFailed = false;
			browserFailed = false;
		}
	}

	// Client: rebuilds the snapshots once the world plan is ready and after anything it depends on changed (seed, server
	// settings, game mode, ShowWorldDrops), restores vanilla when the world is left
	public class WorldDropsReportSystem : ModSystem
	{
		private static bool dirty;

		public static void MarkDirty() => dirty = true;

		private static bool expert;

		public override void Load()
		{
			SeedSystem.OnSeedChanged += MarkDirty;
			WorldCaches.Reset += MarkDirty;
		}

		public override void Unload()
		{
			SeedSystem.OnSeedChanged -= MarkDirty;
			WorldCaches.Reset -= MarkDirty;
			WorldDropsReport.Unload();
		}

		public override void PostUpdateEverything()
		{
			if (Main.dedServ || Main.gameMenu) return;
			bool wanted = WorldDropsReport.Wanted();
			// The extra drops depend on the game mode (journey mode can change it in a world)
			if (expert != Main.expertMode)
			{
				expert = Main.expertMode;
				dirty = true;
			}
			if (!dirty && wanted == WorldDropsReport.Showing) return;
			dirty = false;
			if (!wanted && !WorldDropsReport.Showing) return;
			WorldDropsReport.Rebuild(wanted);
		}

		public override void OnWorldUnload()
		{
			if (WorldDropsReport.Showing) WorldDropsReport.Rebuild(false);
			dirty = false;
		}
	}
}

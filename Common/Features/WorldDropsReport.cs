using System;
using System.Collections.Generic;
using System.Diagnostics;
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
		private static HashSet<int> rebuilt;

		private static HashSet<int> RebuiltIds() => rebuilt ??= ContentSamples.NpcsByNetId.Keys
			.Where(n => DropPermutation.IsPermutable(n) || BossPermutation.IsSource(n)).ToHashSet();

		private static bool browserFailed;

		// A rebuild in progress: the net IDs are read a few hundred per frame (each reads the world plan of its NPC), the
		// bestiary entries are replaced as they come, Recipe Browser's new cache is published at the end
		private class Job
		{
			public int Next = MinNetId;
			public Dictionary<int, List<PlannedDrop>> Plans = new();
			public object BrowserCache;
			public Dictionary<int, List<int>> BrowserMap;
			public FieldInfo BrowserInstance;
			public int Failures;
			public string FirstFailure;
			public double Milliseconds;
			public int Frames;
		}

		private static Job job;

		public static bool Pending => job != null;

		// Recipe Browser's loop starts there (the negative net IDs are vanilla variants)
		private const int MinNetId = -65;

		// show: the world's drops (Wanted) or vanilla. Main thread, client. Done at once (tests, leaving the world)
		public static void Rebuild(bool show)
		{
			Start(show);
			Step(int.MaxValue);
		}

		// Starts a rebuild (replacing one in progress), finished by Step
		public static void Start(bool show)
		{
			Showing = show;
			job = null;
			plans = null;
			if (Main.dedServ) return;
			job = new Job();
			plans = job.Plans;
			PrepareRecipeBrowser(job);
		}

		// Finishes a rebuild in progress now
		public static void Complete()
		{
			if (job != null) Step(int.MaxValue);
		}

		// Reads up to budget net IDs; true once the rebuild is finished
		public static bool Step(int budget)
		{
			if (job == null) return true;
			Stopwatch watch = Stopwatch.StartNew();
			Job current = job;
			HashSet<int> bestiary = RebuiltIds();
			int done = 0;
			while (current.Next < NPCLoader.NPCCount && done < budget)
			{
				int netId = current.Next++;
				bool inBestiary = bestiary.Contains(netId);
				if (!inBestiary && current.BrowserMap == null) continue;
				done++;
				// Per entry: a failing entry keeps its old elements and the loop goes on
				try
				{
					List<DropRateInfo> drops = Capture(netId, current);
					if (inBestiary) ReplaceBestiaryDrops(netId, drops);
					if (current.BrowserMap != null)
					{
						foreach (DropRateInfo info in drops)
						{
							if (!current.BrowserMap.TryGetValue(info.itemId, out List<int> npcs)) current.BrowserMap.Add(info.itemId, npcs = new List<int>());
							if (!npcs.Contains(netId)) npcs.Add(netId);
						}
					}
				}
				catch (Exception e)
				{
					Failed(current, $"net ID {netId}: {e}");
				}
			}
			current.Milliseconds += watch.Elapsed.TotalMilliseconds;
			current.Frames++;
			if (current.Next < NPCLoader.NPCCount) return false;
			if (current.BrowserMap != null)
			{
				try
				{
					current.BrowserInstance.SetValue(null, current.BrowserCache);
				}
				catch (Exception e)
				{
					browserFailed = true;
					Failed(current, $"Recipe Browser's cache: {e}");
				}
			}
			job = null;
			plans = null;
			UltimateRandomizer mod = ModContent.GetInstance<UltimateRandomizer>();
			mod.Logger.Info($"World drops display rebuilt in {current.Milliseconds:0} ms over {current.Frames} frame(s) ({(Showing ? "world" : "vanilla")} drops)");
			if (current.Failures > 0) mod.Logger.Warn($"World drops display: {current.Failures} drop rule(s) or entries could not be read, first: {current.FirstFailure}");
			return true;
		}

		private static void Failed(Job current, string what)
		{
			if (current.Failures++ == 0) current.FirstFailure = what;
		}

		// As BestiaryDatabase.ExtractDropsForNPC (private, called by Merge for every net ID at load), for our NPCs only:
		// Merge only appends, the old elements are removed first (the new ones are built before)
		private static void ReplaceBestiaryDrops(int netId, List<DropRateInfo> drops)
		{
			BestiaryEntry entry = Main.BestiaryDB.FindEntryByNPCID(netId);
			if (entry == null) return;
			List<IBestiaryInfoElement> elements = drops.Select(info => (IBestiaryInfoElement)new ItemDropBestiaryInfoElement(info)).ToList();
			entry.Info.RemoveAll(i => i != null && i.GetType() == typeof(ItemDropBestiaryInfoElement));
			entry.Info.AddRange(elements);
		}

		// Per rule, as LootSnapshot.Capture: a throwing rule (another mod's) loses its own drops only
		private static List<DropRateInfo> Capture(int netId, Job current)
		{
			List<DropRateInfo> drops = new();
			foreach (IItemDropRule rule in Main.ItemDropsDB.GetRulesForNPCID(netId, false))
			{
				if (rule == null) continue;
				try
				{
					rule.ReportDroprates(drops, new DropRateInfoChainFeed(1f));
				}
				catch (Exception e)
				{
					Failed(current, $"net ID {netId}, {rule.GetType().FullName}: {e.Message}");
				}
			}
			return drops;
		}

		// Recipe Browser (optional) fills RecipeBrowser.LootCache.instance.lootInfos (item -> net IDs dropping it) once at
		// load (LootCacheManager.Setup: a new LootCache, then GetRulesForNPCID + ReportDroprates for every net ID, no disk
		// cache). Same loop here, but the new cache is filled before it is published: its craft path search reads it from
		// another thread
		private static void PrepareRecipeBrowser(Job current)
		{
			if (browserFailed || !ModLoader.TryGetMod("RecipeBrowser", out Mod browser)) return;
			try
			{
				Type cacheType = browser.Code?.GetType("RecipeBrowser.LootCache");
				FieldInfo instance = cacheType?.GetField("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
				FieldInfo infos = cacheType?.GetField("lootInfos", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
				if (instance == null || infos == null || infos.FieldType != typeof(Dictionary<int, List<int>>)) throw new MissingMemberException("RecipeBrowser.LootCache.instance / lootInfos");
				object cache = Activator.CreateInstance(cacheType);
				current.BrowserMap = (Dictionary<int, List<int>>)infos.GetValue(cache) ?? throw new MissingMemberException("RecipeBrowser.LootCache.lootInfos is null");
				current.BrowserCache = cache;
				current.BrowserInstance = instance;
			}
			catch (Exception e)
			{
				browserFailed = true;
				current.BrowserMap = null;
				ModContent.GetInstance<UltimateRandomizer>().Logger.Warn($"Could not refresh Recipe Browser's drop cache: {e}");
			}
		}

		// The plans the display and the kills read, computed while the world loads rather than on the first frame (single
		// player and server: PostWorldLoad; clients: when the seed arrives)
		public static void Warm()
		{
			if (!SeedSystem.WorldReady || !ItemPool.Ready || !LootConfig.Instance.RandomizeMobDrops) return;
			try
			{
				if (LootConfig.Instance.DropTableStyle == DropTableStyle.Permutation)
				{
					_ = DropPermutation.Current;
					_ = BossPermutation.Current;
				}
				// The guaranteed sources follow the world's spawn map: a client only has it with the tiles
				if (Main.netMode != NetmodeID.MultiplayerClient) GuaranteedSources.For(NPCID.BlueSlime);
			}
			catch (Exception e)
			{
				ModContent.GetInstance<UltimateRandomizer>().Logger.Warn($"Could not prepare the world drop plans: {e}");
			}
		}

		public static void Unload()
		{
			Showing = false;
			job = null;
			plans = null;
			rebuilt = null;
			browserFailed = false;
		}
	}

	// Client: rebuilds the snapshots once the world plan is ready and after anything it depends on changed (seed, server
	// settings, game mode, ShowWorldDrops), restores vanilla when the world is left. Everywhere: warms the plans when
	// the world is ready
	public class WorldDropsReportSystem : ModSystem
	{
		private static bool dirty;

		public static void MarkDirty() => dirty = true;

		private static bool expert;

		// Net IDs read per frame while rebuilding (the whole loop is about 700 of them)
		private const int PerFrame = 200;

		public override void Load()
		{
			SeedSystem.OnSeedChanged += MarkDirty;
			SeedSystem.OnSeedChanged += WorldDropsReport.Warm;
			WorldCaches.Reset += MarkDirty;
		}

		public override void Unload()
		{
			SeedSystem.OnSeedChanged -= MarkDirty;
			SeedSystem.OnSeedChanged -= WorldDropsReport.Warm;
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
			if (dirty || wanted != WorldDropsReport.Showing)
			{
				dirty = false;
				if (wanted || WorldDropsReport.Showing || WorldDropsReport.Pending) WorldDropsReport.Start(wanted);
			}
			if (WorldDropsReport.Pending) WorldDropsReport.Step(PerFrame);
		}

		public override void OnWorldUnload()
		{
			if (WorldDropsReport.Showing || WorldDropsReport.Pending) WorldDropsReport.Rebuild(false);
			dirty = false;
		}
	}
}

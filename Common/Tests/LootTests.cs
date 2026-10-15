using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Tests
{
	public static class LootTests
	{
		public static void Register()
		{
			TestRunner.Register("SnapshotsCaptured", SnapshotsCaptured);
			TestRunner.Register("MobPlansValid", MobPlansValid);
			TestRunner.Register("ProtectedDropsKept", ProtectedDropsKept);
			TestRunner.Register("NoMinionFarms", NoMinionFarms);
			TestRunner.Register("BagPlansValid", BagPlansValid);
			TestRunner.Register("BagsKeepCoins", BagsKeepCoins);
		}

		private static string NpcName(int netId) => ContentSamples.NpcsByNetId.TryGetValue(netId, out NPC sample) ? sample.TypeName : netId.ToString();

		private static void SnapshotsCaptured()
		{
			Check.That(LootSnapshot.Npcs.Count > 300, $"only {LootSnapshot.Npcs.Count} enemy drop tables captured");
			Check.That(LootSnapshot.Items.Count > 30, $"only {LootSnapshot.Items.Count} bag tables captured");
			Check.That(LootSnapshot.ForNpc(NPCID.Zombie).Count > 0, "Zombie has no captured drops");
			Check.That(LootSnapshot.ForItem(ItemID.EyeOfCthulhuBossBag).Count > 0, "Eye of Cthulhu bag has no captured content");
		}

		private static void ValidatePlan(string source, List<PlannedDrop> plan, List<string> failures)
		{
			foreach (PlannedDrop drop in plan)
			{
				if (drop.Item <= ItemID.None || drop.Item >= ItemLoader.ItemCount) failures.Add($"{source}: invalid item {drop.Item}");
				else if (drop.Chance <= 0 || drop.Chance > 1.0001f) failures.Add($"{source}: chance {drop.Chance} for {Lang.GetItemNameValue(drop.Item)}");
				else if (drop.StackMin < 1 || drop.StackMax < drop.StackMin) failures.Add($"{source}: stack {drop.StackMin}-{drop.StackMax}");
				else if (drop.Extra && !ItemPool.IsRandomizable(drop.Item) && drop.Item != ItemID.Gel) failures.Add($"{source}: extra drop {Lang.GetItemNameValue(drop.Item)} is protected/excluded");
			}
		}

		private static void MobPlansValid()
		{
			List<string> failures = new();
			foreach (int netId in LootSnapshot.Npcs.Keys)
			{
				ValidatePlan(NpcName(netId), RandomNpcDropRule.Plan(netId, Main.expertMode), failures);
			}
			Check.All(failures, "Invalid enemy drop tables");
		}

		private static void ProtectedDropsKept()
		{
			List<string> failures = new();
			foreach ((int netId, List<DropRateInfo> drops) in LootSnapshot.Npcs)
			{
				foreach (DropRateInfo entry in drops)
				{
					if (!DropHelper.KeepOriginal(entry.itemId)) continue;
					int replacement = DropHelper.Replacement(RollKey.MobDrop, DetRandom.NpcKey(netId), entry.itemId, 0, 0, false);
					if (replacement != entry.itemId) failures.Add($"{NpcName(netId)}: {Lang.GetItemNameValue(entry.itemId)} replaced");
				}
			}
			int[] mustKeep = { ItemID.GuideVoodooDoll, ItemID.Pwnhammer, ItemID.TempleKey, ItemID.CopperCoin, ItemID.EyeOfCthulhuBossBag };
			foreach (int item in mustKeep)
			{
				if (!DropHelper.KeepOriginal(item)) failures.Add($"{Lang.GetItemNameValue(item)} is not kept");
			}
			Check.All(failures, "Progression drops changed");
		}

		private static void NoMinionFarms()
		{
			if (!LootConfig.Instance.RandomizeMobDrops) Check.Skip("enemy drops are not randomized");
			int[] noExtra = { NPCID.Creeper, NPCID.ServantofCthulhu, NPCID.TheHungry, NPCID.Probe, NPCID.Bee, NPCID.SkeletronHand, NPCID.Guide, NPCID.Merchant, NPCID.TorchGod };
			List<string> failures = new();
			foreach (int type in noExtra)
			{
				int count = RandomNpcDropRule.ExtraDrops(type, type, false, false).Count;
				if (count > 0) failures.Add($"{NpcName(type)} gets {count} extra drops");
			}
			Check.All(failures, "Minions or town NPCs get random drops");
		}

		private static void BagPlansValid()
		{
			List<string> failures = new();
			foreach (int bag in LootSnapshot.Items.Keys)
			{
				ValidatePlan(Lang.GetItemNameValue(bag), BagPlan.Plan(bag), failures);
			}
			Check.All(failures, "Invalid bag contents");
		}

		private static void BagsKeepCoins()
		{
			List<string> failures = new();
			foreach (int bag in LootSnapshot.Items.Keys.Where(b => ItemID.Sets.BossBag[b]))
			{
				List<DropRateInfo> original = LootSnapshot.ForItem(bag);
				List<PlannedDrop> plan = BagPlan.Plan(bag);
				for (int i = 0; i < original.Count && i < plan.Count; i++)
				{
					if (ItemID.Sets.CommonCoin[original[i].itemId] && plan[i].Item != original[i].itemId) failures.Add($"{Lang.GetItemNameValue(bag)} loses its coins");
				}
			}
			Check.All(failures, "Boss bag coins changed");
		}
	}
}

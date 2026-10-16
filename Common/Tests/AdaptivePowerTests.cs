using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Tests
{
	// Adaptive power: player power from equipment, the multiplier formula, and its application at spawn (boss parts,
	// Transform). The test runner sets PowerOverride to 0 around every test; these tests force their own power.
	public static class AdaptivePowerTests
	{
		public static void Register()
		{
			TestRunner.Register("AdaptivePowerMeasured", AdaptivePowerMeasured);
			TestRunner.Register("AdaptiveFormula", AdaptiveFormula);
			TestRunner.Register("AdaptiveAppliedAtSpawn", AdaptiveAppliedAtSpawn);
			TestRunner.Register("AdaptiveBossParts", AdaptiveBossParts);
			TestRunner.Register("AdaptiveNotCompounded", AdaptiveNotCompounded);
		}

		// Items of a category, highest tiers first, one per tier
		private static List<int> ItemsOf(ItemCategory category, Func<Item, bool> filter)
		{
			return Enumerable.Range(1, ItemPool.Info.Length - 1)
				.Where(t => ItemPool.Info[t].Category == category && !ItemPool.Info[t].Excluded && ContentSamples.ItemsByType.TryGetValue(t, out Item sample) && filter(sample))
				.GroupBy(ItemPool.TierOf).OrderByDescending(g => g.Key).Select(g => g.First()).ToList();
		}

		private static void AdaptivePowerMeasured()
		{
			List<int> weapons = ItemsOf(ItemCategory.WeaponMelee, item => item.damage > 0 && !item.accessory);
			List<int> armors = ItemsOf(ItemCategory.Armor, _ => true);
			List<int> accessories = ItemsOf(ItemCategory.Accessory, item => item.accessory);
			List<int> tools = ItemsOf(ItemCategory.Tool, item => item.damage > 0);
			if (weapons.Count < 2 || armors.Count < 3 || accessories.Count < 4) Check.Skip("not enough tiers of weapons, armor or accessories");
			Player player = new();
			Check.That(Math.Abs(AdaptivePower.PowerOf(player)) < 0.0001f, $"power {AdaptivePower.PowerOf(player):0.00} with nothing");

			// The best weapon counts, not the first one; tools are not weapons
			player.inventory[0] = new Item(weapons[^1]);
			player.inventory[10] = new Item(weapons[0]);
			if (tools.Count > 0) player.inventory[1] = new Item(tools[0]);
			float weapon = ItemPool.TierOf(weapons[0]);
			float power = AdaptivePower.PowerOf(player);
			Check.That(Math.Abs(power - 0.5f * weapon) < 0.0001f, $"weapon tier {weapon}: power {power:0.00}, expected {0.5f * weapon:0.00}");

			// Worn armor: average of the 3 pieces (empty pieces count 0)
			for (int i = 0; i < 3; i++) player.armor[i] = new Item(armors[i]);
			float armor = armors.Take(3).Sum(ItemPool.TierOf) / 3f;
			// Accessories: the 3 best of 4, plus a strong vanity one that must not count
			for (int i = 0; i < 4; i++) player.armor[3 + i] = new Item(accessories[accessories.Count - 1 - i]);
			player.armor[13] = new Item(accessories[0]);
			float accessory = Enumerable.Range(0, 4).Select(i => ItemPool.TierOf(accessories[accessories.Count - 1 - i])).OrderByDescending(t => t).Take(3).Sum() / 3f;
			float expected = 0.5f * weapon + 0.25f * armor + 0.25f * accessory;
			power = AdaptivePower.PowerOf(player);
			Check.That(Math.Abs(power - expected) < 0.0001f, $"power {power:0.00}, expected {expected:0.00} (weapon {weapon}, armor {armor:0.00}, accessories {accessory:0.00})");
		}

		private static void AdaptiveFormula()
		{
			List<string> failures = new();
			foreach ((float life, float damage, float cap) in new[] { (1.8f, 1.5f, 10f), (2f, 1.6f, 10f), (3f, 1.1f, 4f), (0.5f, 1f, 10f), (1.8f, 1.5f, 0.5f) })
			{
				(float life, float damage) previous = (1, 1);
				for (float gap = -2; gap <= 12; gap += 0.25f)
				{
					(float l, float d) = AdaptivePower.Multipliers(gap, life, damage, cap);
					if (l < 1 || d < 1) failures.Add($"x{l:0.00}/x{d:0.00} below 1 at gap {gap} (growth {life}/{damage})");
					if (l > Math.Max(1, cap) + 0.0001f || d > Math.Max(1, cap) + 0.0001f) failures.Add($"x{l:0.00}/x{d:0.00} above the cap {cap} at gap {gap}");
					if (l < previous.life - 0.0001f || d < previous.damage - 0.0001f) failures.Add($"decreasing at gap {gap} (growth {life}/{damage})");
					if (gap <= 0 && (l != 1 || d != 1)) failures.Add($"x{l:0.00}/x{d:0.00} without any gap");
					previous = (l, d);
				}
			}
			(float oneLife, float oneDamage) = AdaptivePower.Multipliers(1, 1.8f, 1.5f, 10);
			if (Math.Abs(oneLife - 1.8f) > 0.001f || Math.Abs(oneDamage - 1.5f) > 0.001f) failures.Add($"one tier: x{oneLife:0.00}/x{oneDamage:0.00}, expected x1.80/x1.50");
			float previousGap = 0;
			for (float power = 0; power <= 10; power += 0.5f)
			{
				float gap = AdaptivePower.Gap(power, 2, 1);
				if (gap < 0) failures.Add($"negative gap {gap} at power {power}");
				if (gap < previousGap) failures.Add($"gap decreasing at power {power}");
				previousGap = gap;
			}
			if (AdaptivePower.Gap(3, 2, 1) != 0 || Math.Abs(AdaptivePower.Gap(5.5f, 2, 1) - 2.5f) > 0.0001f) failures.Add("gap not counted above unlocked tier + grace");
			Check.All(failures, "Adaptive multipliers wrong");
		}

		// A power that is `gap` tiers above what the world allows
		private static float PowerWithGap(float gap) => ProgressionSystem.UnlockedTier + ProgressionConfig.Instance.AdaptiveGrace + gap;

		private static void RequireEnabled()
		{
			if (!ProgressionConfig.Instance.AdaptivePower) Check.Skip("adaptive power is off");
			if (Main.netMode != NetmodeID.SinglePlayer) Check.Skip("single player only");
		}

		private static NPC Spawn(int type)
		{
			Player player = Main.LocalPlayer;
			int index = NPC.NewNPC(new EntitySource_SpawnNPC(), (int)player.Center.X + 1200, (int)player.Center.Y - 200, type);
			Check.That(index >= 0 && index < Main.maxNPCs, $"could not spawn {Lang.GetNPCNameValue(type)}");
			EnemySwapSystem.Pending[index] = false;
			return Main.npc[index];
		}

		private static void Despawn(NPC npc)
		{
			EnemySwapSystem.Pending[npc.whoAmI] = false;
			npc.active = false;
		}

		private static int BaseLife(int type)
		{
			NPC sample = new();
			sample.SetDefaults(type);
			return sample.lifeMax;
		}

		private static void AdaptiveAppliedAtSpawn()
		{
			RequireEnabled();
			List<string> failures = new();
			// The forced gap gives exactly growth^gap (capped), computed here without the formula under test
			ProgressionConfig cfg = ProgressionConfig.Instance;
			float exactLife = (float)Math.Max(1, Math.Min(Math.Max(1, cfg.AdaptiveCap), Math.Pow(Math.Max(1, cfg.LifeGrowthPerTier), 2)));
			float exactDamage = (float)Math.Max(1, Math.Min(Math.Max(1, cfg.AdaptiveCap), Math.Pow(Math.Max(1, cfg.DamageGrowthPerTier), 2)));
			(float formulaLife, float formulaDamage) = AdaptivePower.Multipliers(2, false);
			if (Math.Abs(formulaLife - exactLife) > 0.001f || Math.Abs(formulaDamage - exactDamage) > 0.001f)
				failures.Add($"gap 2: x{formulaLife:0.000}/x{formulaDamage:0.000}, expected x{exactLife:0.000}/x{exactDamage:0.000}");
			// Same seed with and without power: the variation is the same, multiplied by exactly the adaptive part
			NPC probe = Spawnless(NPCID.Zombie);
			EnemyIndividualNPC probeStats = probe.GetGlobalNPC<EnemyIndividualNPC>();
			AdaptivePower.PowerOverride = 0;
			probeStats.Roll(probe, new Random(21));
			(float plainLife, float plainDamage, EnemyKind plainKind) = (probeStats.Life, probeStats.Damage, probeStats.Kind);
			AdaptivePower.PowerOverride = PowerWithGap(2);
			probeStats.Roll(probe, new Random(21));
			if (probeStats.Kind != plainKind || Math.Abs(probeStats.Life - plainLife * exactLife) > 0.001f * probeStats.Life || Math.Abs(probeStats.Damage - plainDamage * exactDamage) > 0.001f * probeStats.Damage)
				failures.Add($"same seed: x{probeStats.Life:0.000}/x{probeStats.Damage:0.000}, expected x{plainLife * exactLife:0.000}/x{plainDamage * exactDamage:0.000}");
			foreach (float gap in new[] { 0f, 2f })
			{
				AdaptivePower.PowerOverride = PowerWithGap(gap);
				(float life, float damage) expected = AdaptivePower.Multipliers(gap, false);
				NPC npc = Spawn(NPCID.Zombie);
				try
				{
					EnemyIndividualNPC individual = npc.GetGlobalNPC<EnemyIndividualNPC>();
					if (!individual.Rolled) failures.Add($"gap {gap}: no roll at spawn");
					if (Math.Abs(individual.AdaptiveLife - expected.life) > 0.001f || Math.Abs(individual.AdaptiveDamage - expected.damage) > 0.001f)
						failures.Add($"gap {gap}: adaptive x{individual.AdaptiveLife:0.00}/x{individual.AdaptiveDamage:0.00}, expected x{expected.life:0.00}/x{expected.damage:0.00}");
					int lifeMax = Math.Max(1, (int)(BaseLife(NPCID.Zombie) * individual.Life));
					if (npc.lifeMax != lifeMax) failures.Add($"gap {gap}: {npc.lifeMax} max health, expected {lifeMax}");
				}
				finally
				{
					Despawn(npc);
				}
			}
			// Weaker than the world: enemies never get weaker
			AdaptivePower.PowerOverride = 0;
			(float weakLife, float weakDamage) = AdaptivePower.ForNpc(Spawnless(NPCID.Zombie));
			if (weakLife != 1 || weakDamage != 1) failures.Add($"no power: x{weakLife:0.00}/x{weakDamage:0.00}");
			Check.All(failures, "Adaptive power not applied at spawn");
		}

		private static NPC Spawnless(int type)
		{
			NPC npc = new();
			npc.SetDefaults(type);
			return npc;
		}

		// Boss parts get their owner's multipliers (boss growth), and nothing else (no variation of their own)
		private static void AdaptiveBossParts()
		{
			if (!ProgressionConfig.Instance.AdaptivePower) Check.Skip("adaptive power is off");
			(int part, int owner)[] pairs = { (NPCID.SkeletronHand, NPCID.SkeletronHead), (NPCID.PrimeCannon, NPCID.SkeletronPrime), (NPCID.GolemFistLeft, NPCID.Golem), (NPCID.TheDestroyerBody, NPCID.TheDestroyer) };
			List<string> failures = new();
			int tested = 0;
			AdaptivePower.PowerOverride = PowerWithGap(2);
			foreach ((int partType, int ownerType) in pairs)
			{
				// A real fight going on would give the live owner's multipliers
				if (NPC.AnyNPCs(ownerType)) continue;
				tested++;
				NPC owner = Spawnless(ownerType);
				NPC part = Spawnless(partType);
				(float life, float damage) expected = AdaptivePower.ForNpc(owner);
				(float life, float damage) boss = AdaptivePower.Multipliers(2, true);
				if (Math.Abs(expected.life - boss.life) > 0.001f || Math.Abs(expected.damage - boss.damage) > 0.001f)
					failures.Add($"{owner.TypeName}: x{expected.life:0.00}/x{expected.damage:0.00}, expected the boss growth x{boss.life:0.00}/x{boss.damage:0.00}");
				int baseLife = part.lifeMax;
				int baseDamage = part.damage;
				EnemyIndividualNPC individual = part.GetGlobalNPC<EnemyIndividualNPC>();
				individual.Roll(part, new Random(3));
				individual.Apply(part);
				if (Math.Abs(individual.Life - expected.life) > 0.001f || Math.Abs(individual.Damage - expected.damage) > 0.001f)
					failures.Add($"{part.TypeName}: x{individual.Life:0.00}/x{individual.Damage:0.00}, owner x{expected.life:0.00}/x{expected.damage:0.00}");
				if (part.lifeMax != Math.Max(1, (int)(baseLife * expected.life))) failures.Add($"{part.TypeName}: {part.lifeMax} max health from {baseLife}");
				if (baseDamage > 0 && part.damage != (int)(baseDamage * expected.damage)) failures.Add($"{part.TypeName}: {part.damage} damage from {baseDamage}");
			}
			if (tested == 0) Check.Skip("the bosses are alive");
			// RaiseBosses touches every living boss and minion: only without a fight going on
			bool fight = Main.npc.Any(n => n.active && (AdaptivePower.IsBoss(n) || AdaptivePower.IsFollower(n.type)));
			if (Main.netMode == NetmodeID.SinglePlayer && !fight) LiveOwner(failures);
			Check.All(failures, "Boss parts not scaled like their owner");
		}

		// A part spawned during the fight copies its living boss, even if the power changed meanwhile; re-measuring a living
		// boss only raises it (and its parts), keeping the health ratio
		private static void LiveOwner(List<string> failures)
		{
			if (AdaptivePower.Multipliers(3, true).life < AdaptivePower.Multipliers(2, true).life + 0.01f) return;
			AdaptivePower.PowerOverride = PowerWithGap(2);
			NPC head = Spawn(NPCID.SkeletronHead);
			NPC hand = null;
			try
			{
				EnemyIndividualNPC headStats = head.GetGlobalNPC<EnemyIndividualNPC>();
				AdaptivePower.PowerOverride = PowerWithGap(1);
				hand = Spawn(NPCID.SkeletronHand);
				EnemyIndividualNPC handStats = hand.GetGlobalNPC<EnemyIndividualNPC>();
				if (handStats.AdaptiveLife != headStats.AdaptiveLife || handStats.AdaptiveDamage != headStats.AdaptiveDamage)
					failures.Add($"live hand x{handStats.AdaptiveLife:0.00}/x{handStats.AdaptiveDamage:0.00}, its head x{headStats.AdaptiveLife:0.00}/x{headStats.AdaptiveDamage:0.00}");

				head.life = head.lifeMax / 2;
				byte id = headStats.RollId;
				AdaptivePower.PowerOverride = PowerWithGap(3);
				AdaptivePower.RaiseBosses();
				(float life, float damage) raised = AdaptivePower.Multipliers(3, true);
				if (Math.Abs(headStats.AdaptiveLife - raised.life) > 0.001f || Math.Abs(headStats.AdaptiveDamage - raised.damage) > 0.001f)
					failures.Add($"raised head x{headStats.AdaptiveLife:0.00}/x{headStats.AdaptiveDamage:0.00}, expected x{raised.life:0.00}/x{raised.damage:0.00}");
				if (head.lifeMax != Math.Max(1, (int)(BaseLife(NPCID.SkeletronHead) * headStats.Life))) failures.Add($"raised head: {head.lifeMax} max health for x{headStats.Life:0.00}");
				float ratio = head.life / (float)head.lifeMax;
				if (Math.Abs(ratio - 0.5f) > 0.02f) failures.Add($"raised head: health ratio {ratio:0.00} instead of 0.50");
				if (headStats.RollId == id) failures.Add("raised head: same roll id, clients would not apply it");
				if (handStats.AdaptiveLife != headStats.AdaptiveLife) failures.Add($"the hand did not follow the raise: x{handStats.AdaptiveLife:0.00}");

				// Never lower
				AdaptivePower.PowerOverride = PowerWithGap(0);
				AdaptivePower.RaiseBosses();
				if (Math.Abs(headStats.AdaptiveLife - raised.life) > 0.001f) failures.Add($"lowered head x{headStats.AdaptiveLife:0.00}");
			}
			finally
			{
				Despawn(head);
				if (hand != null) Despawn(hand);
			}
		}

		// A Transform recreates the globals: the stored roll (adaptive power included) comes back once, not measured again
		// nor multiplied twice
		private static void AdaptiveNotCompounded()
		{
			RequireEnabled();
			if (AdaptivePower.Multipliers(2, false).life < 1.01f) Check.Skip("no adaptive health with these growth and cap settings");
			AdaptivePower.PowerOverride = PowerWithGap(2);
			NPC npc = Spawn(NPCID.Zombie);
			try
			{
				EnemyIndividualNPC before = npc.GetGlobalNPC<EnemyIndividualNPC>();
				(float life, float damage, float adaptive) = (before.Life, before.Damage, before.AdaptiveLife);
				Check.That(adaptive > 1.01f, "no adaptive multiplier at spawn: nothing to test");
				EnemyIndividualNPC.SnapshotRatio(npc);
				npc.Transform(NPCID.BaldZombie);
				Check.That(!npc.GetGlobalNPC<EnemyIndividualNPC>().Rolled, "the change kept the globals: nothing to test");
				// The power changed meanwhile: the enemy keeps what it got at spawn
				AdaptivePower.PowerOverride = PowerWithGap(4);
				EnemyIndividualNPC.RollMissing(npc);
				EnemyIndividualNPC after = npc.GetGlobalNPC<EnemyIndividualNPC>();
				Check.That(after.Rolled, "no individual stats after the change");
				Check.That(after.AdaptiveLife == adaptive && after.Life == life && after.Damage == damage,
					$"health x{after.Life:0.00} (adaptive x{after.AdaptiveLife:0.00}), was x{life:0.00} (adaptive x{adaptive:0.00})");
				int expected = Math.Max(1, (int)(BaseLife(NPCID.BaldZombie) * life));
				Check.That(npc.lifeMax == expected, $"{npc.lifeMax} max health after the change, expected {expected}");
			}
			finally
			{
				Despawn(npc);
			}
		}
	}
}

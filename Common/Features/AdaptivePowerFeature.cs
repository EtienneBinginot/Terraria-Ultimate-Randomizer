using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	// Adaptive power: the randomizer may give strong items early, but enemies and bosses then scale with the player's
	// equipment. Power = tier of the best weapon owned (50%), of the worn armor (25%) and of the 3 best accessories (25%).
	// Every tier above the world's progression (+ a margin) multiplies enemy health and damage.
	// Measured where enemies spawn (server or single player): clients sync their inventory, Void Vault, loadouts and armor
	// slots to the server (Main.TrySyncingMyPlayer, message 5), so Main.player holds their equipment there. The result is
	// folded into the individual roll of EnemyIndividualNPC, which is stored per slot and sent to the clients with the NPC.
	public static class AdaptivePower
	{
		// Tests force a power instead of measuring the players
		public static float? PowerOverride;

		public static bool Enabled => ProgressionConfig.Instance.AdaptivePower && SeedSystem.WorldReady && ItemPool.Ready;

		public static float PowerOf(Player player)
		{
			if (player == null || !ItemPool.Ready) return 0;
			// Everything carried counts (cursor slot 58, Void Vault): a weapon put away before a boss fight still counts
			float weapon = 0;
			for (int i = 0; i < 59 && i < player.inventory.Length; i++) weapon = Math.Max(weapon, WeaponTier(player.inventory[i]));
			if (player.IsVoidVaultEnabled && player.bank4?.item != null)
			{
				foreach (Item item in player.bank4.item) weapon = Math.Max(weapon, WeaponTier(item));
			}
			// The best of the worn equipment and of every loadout (switching loadouts after the spawn changes nothing)
			(float armor, float accessory) = Equipment(player.armor);
			if (player.Loadouts != null)
			{
				foreach (EquipmentLoadout loadout in player.Loadouts)
				{
					(float a, float b) = Equipment(loadout?.Armor);
					armor = Math.Max(armor, a);
					accessory = Math.Max(accessory, b);
				}
			}
			return 0.5f * weapon + 0.25f * armor + 0.25f * accessory;
		}

		// Average tier of the 3 armor pieces (empty pieces count 0) and of the 3 best functional accessories (slots 3-9;
		// 10 and up are vanity)
		private static (float armor, float accessory) Equipment(Item[] slots)
		{
			if (slots == null) return (0, 0);
			float armor = 0;
			for (int i = 0; i < 3 && i < slots.Length; i++)
			{
				Item item = slots[i];
				if (item != null && !item.IsAir) armor += ItemPool.TierOf(item.type);
			}
			List<int> accessories = new();
			for (int i = 3; i < 10 && i < slots.Length; i++)
			{
				Item item = slots[i];
				if (item != null && !item.IsAir && item.accessory) accessories.Add(ItemPool.TierOf(item.type));
			}
			return (armor / 3, accessories.OrderByDescending(t => t).Take(3).Sum() / 3f);
		}

		private static float WeaponTier(Item item)
		{
			if (item == null || item.IsAir || item.damage <= 0 || item.accessory || !ItemPool.IsValid(item.type)) return 0;
			return ItemPool.Info[item.type].Category is ItemCategory.WeaponMelee or ItemCategory.WeaponRanged or ItemCategory.WeaponMagic
				or ItemCategory.WeaponSummon or ItemCategory.WeaponOther ? ItemPool.TierOf(item.type) : 0;
		}

		// Tiers of power above what the world's progression allows (never negative: enemies only get stronger)
		public static float Gap(float power) => Gap(power, ProgressionSystem.UnlockedTier, ProgressionConfig.Instance.AdaptiveGrace);

		public static float Gap(float power, int unlockedTier, int grace) => Math.Max(0, power - (unlockedTier + grace));

		public static (float life, float damage) Multipliers(float gap, bool boss)
		{
			ProgressionConfig cfg = ProgressionConfig.Instance;
			return boss
				? Multipliers(gap, cfg.BossLifeGrowthPerTier, cfg.BossDamageGrowthPerTier, cfg.AdaptiveCap, cfg.AdaptiveDamageCap)
				: Multipliers(gap, cfg.LifeGrowthPerTier, cfg.DamageGrowthPerTier, cfg.AdaptiveCap, cfg.AdaptiveDamageCap);
		}

		public static (float life, float damage) Multipliers(float gap, float lifeGrowth, float damageGrowth, float lifeCap, float damageCap)
		{
			lifeCap = Math.Max(1, lifeCap);
			damageCap = Math.Max(1, damageCap);
			gap = Math.Max(0, gap);
			float life = (float)Math.Min(lifeCap, Math.Pow(Math.Max(1, lifeGrowth), gap));
			float damage = (float)Math.Min(damageCap, Math.Pow(Math.Max(1, damageGrowth), gap));
			return (Math.Max(1, life), Math.Max(1, damage));
		}

		// The power an enemy answers to: the nearest living player's (bosses: the strongest player's)
		public static float PowerNear(NPC npc, bool boss)
		{
			if (PowerOverride.HasValue) return PowerOverride.Value;
			if (Main.netMode == NetmodeID.SinglePlayer) return PowerOf(Main.LocalPlayer);
			float best = 0;
			float bestDistance = float.MaxValue;
			float nearestPower = 0;
			for (int i = 0; i < Main.maxPlayers; i++)
			{
				Player player = Main.player[i];
				if (player == null || !player.active || player.DeadOrGhost) continue;
				float power = PowerOf(player);
				best = Math.Max(best, power);
				float distance = Vector2.DistanceSquared(player.Center, npc.Center);
				if (distance < bestDistance)
				{
					bestDistance = distance;
					nearestPower = power;
				}
			}
			return boss ? best : nearestPower;
		}

		// Same exclusions as the individual stats, but boss parts are included
		public static bool Applies(NPC npc) => Enabled && EnemyIndividualNPC.Rollable(npc);

		public static bool CountsAsBoss(int npcType) =>
			ContentSamples.NpcsByNetId.TryGetValue(npcType, out NPC sample) && (sample.boss || NPCID.Sets.ShouldBeCountedAsBoss[npcType]);

		public static bool IsBoss(NPC npc) => npc.boss || NPCID.Sets.ShouldBeCountedAsBoss[npc.type];

		// Boss parts and minions answer to their boss
		public static bool IsFollower(int npcType) => BossParts.IsPart(npcType) || BossParts.IsMinion(npcType);

		// The multipliers of an enemy now. Boss parts and minions take their living boss's (already rolled) ones, else are
		// measured like their boss would be
		public static (float life, float damage) ForNpc(NPC npc)
		{
			if (!Applies(npc)) return (1, 1);
			if (IsFollower(npc.type))
			{
				NPC owner = FindLeader(npc);
				if (owner != null && owner.TryGetGlobalNPC(out EnemyIndividualNPC rolled) && rolled.Rolled) return (rolled.AdaptiveLife, rolled.AdaptiveDamage);
				bool ownerBoss = CountsAsBoss(BossParts.Leader(npc.type));
				return Multipliers(Gap(PowerNear(npc, ownerBoss)), ownerBoss);
			}
			bool boss = IsBoss(npc);
			return Multipliers(Gap(PowerNear(npc, boss)), boss);
		}

		// The living boss of a part or minion: its realLife NPC (worms), else the nearest NPC of the boss's type
		public static NPC FindLeader(NPC follower)
		{
			int ownerType = BossParts.Leader(follower.type);
			if (ownerType == follower.type) return null;
			if (follower.realLife >= 0 && follower.realLife < Main.maxNPCs && Main.npc[follower.realLife].active && Main.npc[follower.realLife].type == ownerType)
				return Main.npc[follower.realLife];
			NPC best = null;
			float bestDistance = float.MaxValue;
			for (int i = 0; i < Main.maxNPCs; i++)
			{
				NPC other = Main.npc[i];
				if (!other.active || other.type != ownerType || other == follower) continue;
				float distance = Vector2.DistanceSquared(other.Center, follower.Center);
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = other;
				}
			}
			return best;
		}

		// Living bosses (then their parts and minions, which copy them) are measured again and only ever get stronger:
		// summoning with weak equipment and switching afterwards does not dodge adaptive power. Server or single player
		public static void RaiseBosses()
		{
			for (int pass = 0; pass < 2; pass++)
			{
				for (int i = 0; i < Main.maxNPCs; i++)
				{
					NPC npc = Main.npc[i];
					if (!npc.active || !npc.TryGetGlobalNPC(out EnemyIndividualNPC individual) || !individual.Rolled || !Applies(npc)) continue;
					bool follower = IsFollower(npc.type);
					if (pass == 0 ? follower || !IsBoss(npc) : !follower) continue;
					(float life, float damage) = ForNpc(npc);
					individual.RaiseAdaptive(npc, life, damage);
				}
			}
		}
	}

	public class AdaptivePowerSystem : ModSystem
	{
		public const int RemeasureTicks = 120;

		public override void PostUpdateNPCs()
		{
			if (Main.netMode == NetmodeID.MultiplayerClient || Main.GameUpdateCount % RemeasureTicks != 0 || !AdaptivePower.Enabled) return;
			AdaptivePower.RaiseBosses();
		}
	}

	// Tells the local player when their power makes new enemies stronger (or when the world caught up). Client side.
	public class AdaptivePowerPlayer : ModPlayer
	{
		// A change is announced once it held this long (no message for a quick item swap)
		private const int HoldTicks = 180;

		private float lastStep = -1;
		private float pendingStep = -1;
		private uint pendingSince;

		public override void OnEnterWorld()
		{
			lastStep = -1;
			pendingStep = -1;
		}

		public override void PostUpdate()
		{
			if (Player.whoAmI != Main.myPlayer || Main.dedServ || !AdaptivePower.Enabled || !ClientConfig.Instance.AdaptiveMessages) return;
			if (Main.GameUpdateCount % 60 != 0) return;
			// Half tiers: a message per meaningful change, not per small accessory change
			float step = (float)Math.Floor(AdaptivePower.Gap(AdaptivePower.PowerOf(Player)) * 2) / 2;
			bool first = lastStep < 0;
			if (step == lastStep)
			{
				pendingStep = -1;
				return;
			}
			if (!first)
			{
				if (step != pendingStep)
				{
					pendingStep = step;
					pendingSince = Main.GameUpdateCount;
					return;
				}
				if (Main.GameUpdateCount - pendingSince < HoldTicks) return;
			}
			float previous = lastStep;
			lastStep = step;
			pendingStep = -1;
			// Entering the world: only a warning if enemies are already stronger
			if (first && step == 0) return;
			(float life, float damage) = AdaptivePower.Multipliers(step, false);
			string key = step == 0 ? "PowerCaughtUp" : first || step > previous ? "PowerUp" : "PowerDown";
			Color color = key == "PowerUp" ? new Color(255, 120, 80) : new Color(140, 220, 140);
			Main.NewText(Language.GetTextValue("Mods.UltimateRandomizer.Chat." + key, life.ToString("0.0"), damage.ToString("0.0")), color);
		}
	}
}

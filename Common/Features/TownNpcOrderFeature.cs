using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	// Shuffles which town NPC moves in when each arrival condition is met: the Wizard may come with 50 silver, the Merchant after Skeletron...
	// Vanilla only checks the condition of an NPC that is missing, so the conditions are re-implemented here (simplified).
	public class TownNpcOrderSystem : ModSystem
	{
		private static readonly (int type, Func<bool> condition)[] MoveInNpcs =
		{
			(NPCID.Merchant, () => AnyPlayer(p => CoinValue(p) >= Item.silver * 50)),
			// Vanilla needs the Merchant; here any first arrival counts, otherwise the Merchant could need himself
			(NPCID.Nurse, () => TownNpcCount() >= 2 && AnyPlayer(p => p.statLifeMax > 100)),
			(NPCID.ArmsDealer, () => AnyPlayer(p => HasItem(p, i => i.useAmmo == AmmoID.Bullet || i.ammo == AmmoID.Bullet))),
			(NPCID.Dryad, () => NPC.downedBoss1 || NPC.downedBoss2 || NPC.downedBoss3 || NPC.downedQueenBee || NPC.downedSlimeKing || NPC.downedDeerclops || Main.hardMode),
			(NPCID.Demolitionist, () => AnyPlayer(p => HasItem(p, i => Explosives.Contains(i.type)))),
			(NPCID.Clothier, () => NPC.downedBoss3),
			(NPCID.DyeTrader, () => AnyPlayer(p => HasItem(p, i => i.dye > 0))),
			(NPCID.Painter, () => TownNpcCount() >= 8),
			(NPCID.PartyGirl, () => TownNpcCount() >= 14),
			(NPCID.BestiaryGirl, () => Main.GetBestiaryProgressReport().CompletionPercent >= 0.1f),
			(NPCID.WitchDoctor, () => NPC.downedQueenBee),
			(NPCID.Steampunker, () => NPC.downedMechBossAny),
			(NPCID.Pirate, () => NPC.downedPirates),
			(NPCID.Cyborg, () => NPC.downedPlantBoss)
		};

		private static readonly HashSet<int> Explosives = new()
		{
			ItemID.Bomb, ItemID.StickyBomb, ItemID.BouncyBomb, ItemID.Dynamite, ItemID.StickyDynamite, ItemID.BouncyDynamite,
			ItemID.Grenade, ItemID.StickyGrenade, ItemID.BouncyGrenade, ItemID.Beenade, ItemID.ScarabBomb
		};

		public override void Load() => On_Main.UpdateTime_SpawnTownNPCs += AfterVanillaChecks;

		private static int[] arrivals;
		private static int arrivalsSeed;

		private static void AfterVanillaChecks(On_Main.orig_UpdateTime_SpawnTownNPCs orig)
		{
			orig();
			// Vanilla only re-evaluates arrivals every couple of minutes, and resets this counter when it does
			if (Main.checkForSpawns != 0) return;
			if (Main.netMode == NetmodeID.MultiplayerClient || !SeedSystem.WorldReady || !ExtrasConfig.Instance.RandomizeTownNpcOrder) return;
			try
			{
				ApplyShuffledConditions();
			}
			catch (Exception e)
			{
				ModContent.GetInstance<UltimateRandomizer>().Logger.Warn("Town NPC order failed: " + e.Message);
			}
		}

		// Town NPC types in condition order, and who arrives with each condition (for the debug tools and tests)
		public static int[] ConditionOwners => MoveInNpcs.Select(p => p.type).ToArray();

		// Tier from which each condition of MoveInNpcs is usually met (money and items early, then the bosses)
		private static readonly int[] ConditionTiers = { 0, 0, 0, 1, 0, 3, 0, 0, 0, 0, 2, 5, 4, 6 };

		// Tier at which a town NPC moves in, with this world's arrival order (-1: not one of the shuffled NPCs)
		public static int ArrivalTier(int npcType)
		{
			int index;
			if (ExtrasConfig.Instance.RandomizeTownNpcOrder)
			{
				EnsureArrivals();
				index = Array.IndexOf(arrivals, npcType);
			}
			else
			{
				index = Array.FindIndex(MoveInNpcs, p => p.type == npcType);
			}
			return index >= 0 && index < ConditionTiers.Length ? ConditionTiers[index] : -1;
		}

		public static int[] Arrivals()
		{
			EnsureArrivals();
			return (int[])arrivals.Clone();
		}

		private static void EnsureArrivals()
		{
			if (arrivals == null || arrivalsSeed != SeedSystem.WorldSeed)
			{
				arrivals = MoveInNpcs.Select(p => p.type).ToArray();
				Random rng = DetRandom.Get(RollKey.TownNpc, arrivals.Length);
				for (int i = arrivals.Length - 1; i > 0; i--)
				{
					int j = rng.Next(i + 1);
					(arrivals[i], arrivals[j]) = (arrivals[j], arrivals[i]);
				}
				arrivalsSeed = SeedSystem.WorldSeed;
			}
		}

		private static void ApplyShuffledConditions()
		{
			// Who arrives with which condition: a permutation fixed for the world
			EnsureArrivals();
			int firstReady = 0;
			for (int i = 0; i < MoveInNpcs.Length; i++)
			{
				int arriving = arrivals[i];
				bool ready = MoveInNpcs[i].condition() && !NPC.AnyNPCs(arriving);
				Main.townNPCCanSpawn[arriving] = ready;
				if (ready && firstReady == 0) firstReady = arriving;
			}
			// Keep vanilla's choice when it is not one of ours (Guide, Truffle, Princess, rescued NPCs, a homeless NPC...)
			int prioritized = WorldGen.prioritizedTownNPCType;
			bool ours = MoveInNpcs.Any(p => p.type == prioritized);
			if (prioritized == 0 || (ours && !Main.townNPCCanSpawn[prioritized] && !IsHomeless(prioritized)))
			{
				WorldGen.prioritizedTownNPCType = firstReady;
			}
		}

		private static bool IsHomeless(int type)
		{
			for (int i = 0; i < Main.maxNPCs; i++)
			{
				if (Main.npc[i].active && Main.npc[i].type == type && Main.npc[i].homeless) return true;
			}
			return false;
		}

		private static bool AnyPlayer(Func<Player, bool> predicate)
		{
			for (int i = 0; i < Main.maxPlayers; i++)
			{
				Player player = Main.player[i];
				if (player != null && player.active && predicate(player)) return true;
			}
			return false;
		}

		private static bool HasItem(Player player, Func<Item, bool> predicate)
		{
			foreach (Item item in player.inventory)
			{
				if (item != null && !item.IsAir && predicate(item)) return true;
			}
			return false;
		}

		private static long CoinValue(Player player)
		{
			long total = 0;
			foreach (Item item in player.inventory)
			{
				if (item == null || item.IsAir) continue;
				total += item.type switch
				{
					ItemID.CopperCoin => item.stack,
					ItemID.SilverCoin => item.stack * 100L,
					ItemID.GoldCoin => item.stack * 10000L,
					ItemID.PlatinumCoin => item.stack * 1000000L,
					_ => 0
				};
			}
			return total;
		}

		private static int TownNpcCount()
		{
			int count = 0;
			for (int i = 0; i < Main.maxNPCs; i++)
			{
				NPC npc = Main.npc[i];
				if (npc.active && npc.townNPC && !NPCID.Sets.IsTownPet[npc.type]) count++;
			}
			return count;
		}
	}
}

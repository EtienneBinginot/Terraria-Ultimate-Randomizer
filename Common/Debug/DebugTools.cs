using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;
using UltimateRandomizer.Common.Journal;

namespace UltimateRandomizer.Common.Debug
{
	public enum DebugSpawnRate { Normal, None, High }

	// State and actions of the debug panel. World-changing actions are single player only.
	public static class DebugTools
	{
		public static bool GodMode;
		public static DebugSpawnRate SpawnRate = DebugSpawnRate.Normal;

		public static bool Allowed => ClientConfig.Instance.DebugTools;

		public static bool CanChangeWorld(out string reason)
		{
			reason = null;
			if (Main.netMode == NetmodeID.SinglePlayer && SeedSystem.WorldReady) return true;
			reason = Language.GetTextValue("Mods.UltimateRandomizer.Debug.SinglePlayerOnly");
			return false;
		}

		public static string Say(string key, params object[] args) => Language.GetTextValue("Mods.UltimateRandomizer.Debug." + key, args);

		private static void Notify(string message) => Main.NewText("[Ultimate Randomizer] " + message, Color.Gold);

		private static bool Guard()
		{
			if (CanChangeWorld(out string reason)) return true;
			Notify(reason);
			return false;
		}

		// Sets the boss flags so the unlocked tier becomes exactly `tier`
		public static void SetTier(int tier)
		{
			if (!Guard()) return;
			NPC.downedBoss1 = tier >= 1;
			NPC.downedBoss2 = tier >= 2;
			NPC.downedBoss3 = tier >= 3;
			Main.hardMode = tier >= 4;
			NPC.downedMechBoss1 = NPC.downedMechBoss2 = NPC.downedMechBoss3 = NPC.downedMechBossAny = tier >= 5;
			NPC.downedPlantBoss = tier >= 6;
			NPC.downedGolemBoss = tier >= 7;
			NPC.downedAncientCultist = tier >= 8;
			NPC.downedMoonlord = tier >= 9;
			ProgressionSystem.Refresh();
			Notify(Say("TierSet", (Tier)ProgressionSystem.UnlockedTier));
		}

		public static void NewSeed()
		{
			if (!Guard()) return;
			SeedSystem.ChangeSeed(Random.Shared.Next());
			Notify(Say("SeedSet", SeedSystem.WorldSeed));
		}

		public static void ToggleGodMode() => GodMode = !GodMode;

		public static void CycleSpawnRate() => SpawnRate = (DebugSpawnRate)(((int)SpawnRate + 1) % 3);

		public static void ToggleDay()
		{
			if (!Guard()) return;
			Main.dayTime = !Main.dayTime;
			Main.time = 0;
		}

		// Natural enemies of the current tier around the player; they spawn like natural ones, so they can be swapped
		public static void SpawnEnemies(int count)
		{
			if (!Guard()) return;
			Player player = Main.LocalPlayer;
			List<int> candidates = EnemySwapPool.NaturalEnemies(ProgressionSystem.UnlockedTier);
			if (candidates.Count == 0) return;
			for (int i = 0; i < count; i++)
			{
				int type = candidates[Main.rand.Next(candidates.Count)];
				int x = (int)player.Center.X + Main.rand.Next(250, 500) * (Main.rand.NextBool() ? 1 : -1);
				int y = (int)player.Center.Y - 100;
				NPC.NewNPC(new EntitySource_SpawnNPC(), x, y, type);
			}
		}

		// Kills hostile enemies on screen: a quick way to see their drops
		public static void KillHostiles()
		{
			if (!Guard()) return;
			Rectangle screen = new((int)Main.screenPosition.X, (int)Main.screenPosition.Y, Main.screenWidth, Main.screenHeight);
			for (int i = 0; i < Main.maxNPCs; i++)
			{
				NPC npc = Main.npc[i];
				if (npc.active && !npc.friendly && !npc.townNPC && npc.lifeMax > 5 && screen.Intersects(npc.Hitbox)) npc.StrikeInstantKill();
			}
		}

		public static void GiveTestKit()
		{
			if (!Guard()) return;
			Player player = Main.LocalPlayer;
			IEntitySource source = player.GetSource_Misc("UltimateRandomizer_Debug");
			(int item, int stack)[] kit =
			{
				(ItemID.GoldenBugNet, 1), (ItemID.CellPhone, 1), (ItemID.PlatinumCoin, 10), (ItemID.GreaterHealingPotion, 30),
				(ItemID.IronPickaxe, 1), (ItemID.Torch, 99), (ItemID.WoodPlatform, 99)
			};
			foreach ((int item, int stack) in kit) player.QuickSpawnItem(source, item, stack);
		}

		public static void GiveBossBags()
		{
			if (!Guard()) return;
			Player player = Main.LocalPlayer;
			IEntitySource source = player.GetSource_Misc("UltimateRandomizer_Debug");
			foreach (int bag in LootSnapshot.Items.Keys.Where(b => ItemID.Sets.BossBag[b]).OrderBy(b => b)) player.QuickSpawnItem(source, bag, 1);
		}

		public static void OpenJournal() => JournalSystem.Toggle();
	}

	public class DebugPlayer : ModPlayer
	{
		public override void ProcessTriggers(TriggersSet triggersSet)
		{
			if (DebugSystem.ToggleKeybind != null && DebugSystem.ToggleKeybind.JustPressed && DebugTools.Allowed) DebugSystem.Toggle();
		}

		public override void PostUpdate()
		{
			if (!DebugTools.GodMode || Player.whoAmI != Main.myPlayer || Main.netMode != NetmodeID.SinglePlayer) return;
			Player.statLife = Player.statLifeMax2;
			Player.statMana = Player.statManaMax2;
			Player.immune = true;
			Player.immuneTime = 2;
			Player.breath = Player.breathMax;
		}

		public override void OnEnterWorld()
		{
			DebugTools.GodMode = false;
			DebugTools.SpawnRate = DebugSpawnRate.Normal;
		}
	}

	public class DebugSpawnRateNPC : GlobalNPC
	{
		public override void EditSpawnRate(Player player, ref int spawnRate, ref int maxSpawns)
		{
			if (Main.netMode != NetmodeID.SinglePlayer) return;
			switch (DebugTools.SpawnRate)
			{
				case DebugSpawnRate.None:
					maxSpawns = 0;
					break;
				case DebugSpawnRate.High:
					spawnRate = Math.Max(1, spawnRate / 10);
					maxSpawns *= 5;
					break;
			}
		}
	}
}

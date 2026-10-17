using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Codex
{
	public class CodexItem : GlobalItem
	{
		// Tells CodexSystem's NewItem detour which item this call created (read once every OnSpawn hook has run)
		public override void OnSpawn(Item item, IEntitySource source) => CodexSystem.lastSpawned = item;

		// Runs on the client picking the item up
		public override bool OnPickup(Item item, Player player)
		{
			if (player.whoAmI == Main.myPlayer && !Main.dedServ) CodexSystem.RecordObtained(item.type);
			return true;
		}

		// A bag counts as opened even if everything it gave was filtered (coins)
		public override void RightClick(Item item, Player player)
		{
			if (player.whoAmI == Main.myPlayer && LootSnapshot.Items.ContainsKey(item.type)) CodexSystem.RecordBagOpened(item.type);
		}
	}

	public class CodexNPC : GlobalNPC
	{
		// Server and single player only (vanilla runs NPCLoot there)
		public override void OnKill(NPC npc)
		{
			if (SeedSystem.WorldReady) CodexSystem.RecordKill(npc);
		}

		public override void OnSpawn(NPC npc, IEntitySource source)
		{
			if (Main.netMode == NetmodeID.MultiplayerClient) return;
			CodexSystem.MarkSpawn(npc, source is EntitySource_SpawnNPC && !npc.SpawnedFromStatue);
		}
	}

	// The local player's discoveries: chests opened, recipes shown, fish caught, inventory when entering
	public class CodexPlayer : ModPlayer
	{
		private int lastChest = -1;

		public override void OnEnterWorld()
		{
			lastChest = -1;
			CodexSystem.ResetClientScan();
			CodexSystem.RequestFull();
			foreach (Item item in Player.inventory)
			{
				if (item != null && !item.IsAir) CodexSystem.RecordObtained(item.type);
			}
		}

		public override void PostUpdate()
		{
			if (Player.whoAmI != Main.myPlayer || Main.dedServ || !SeedSystem.WorldReady) return;
			// The chest content arrives before the server opens it for this player
			if (Player.chest != lastChest)
			{
				lastChest = Player.chest;
				if (Player.chest >= 0 && Player.chest < Main.maxChests) CodexSystem.RecordChest(Main.chest[Player.chest]);
			}
			if (Main.GameUpdateCount % 15 == 0) CodexSystem.ScanGuide();
			// Crafted, taken from a chest, caught: whatever reaches the inventory without a pickup
			if (Main.GameUpdateCount % 60 == 30) ScanInventory();
			if (Main.playerInventory && Main.GameUpdateCount % 30 == 0 && RecipeSystem.RecipeCount > 0) CodexSystem.ScanAvailableRecipes();
		}

		private void ScanInventory()
		{
			for (int i = 0; i < Main.InventorySlotsTotal && i < Player.inventory.Length; i++)
			{
				Item item = Player.inventory[i];
				if (item != null && !item.IsAir && !CodexSystem.HasObtained(item.type)) CodexSystem.RecordObtained(item.type);
			}
			if (!Main.mouseItem.IsAir && !CodexSystem.HasObtained(Main.mouseItem.type)) CodexSystem.RecordObtained(Main.mouseItem.type);
		}

		// Server: the next player in this slot gets the full codex again
		public override void PlayerDisconnect() => CodexSystem.OnPlayerLeft(Player.whoAmI);

		// On the client that fishes, with the final catch
		public override void ModifyCaughtFish(Item fish)
		{
			if (Player.whoAmI == Main.myPlayer && SeedSystem.WorldReady && !fish.IsAir) CodexSystem.RecordSource(CodexSource.Fishing, 0, fish.type, fish.stack);
		}
	}
}

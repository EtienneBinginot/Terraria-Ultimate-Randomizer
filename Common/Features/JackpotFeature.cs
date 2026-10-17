using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace UltimateRandomizer.Common.Features
{
	// A jackpot is a runtime drop above the progression limit (ItemPool.LastJackpotGap): the server (or single player, or
	// the client opening a bag) marks the item with its gap in tiers, the mark travels with the item (NetSend: world item
	// syncs, but also inventories and chests), and every client shows it: a glow and sparkles while it lies on the ground,
	// a burst, a popup, a sound and, from 2 tiers above, a chat line, all stronger the higher it is. The burst is shown
	// once, for a world item, just after the drop (a later sync, a join, an inventory or a thrown item says it is not
	// fresh), from PostUpdate where the synced position is known. The codex does not count it as the enemy's drop.
	public class JackpotItem : GlobalItem
	{
		public override bool InstancePerEntity => true;

		// Tiers above the limit when it dropped (0: no jackpot)
		public byte Gap;
		// When it was marked (GameUpdateCount of the machine that marked or received it): fresh for FreshTicks
		public uint MarkedAt;
		// Not synced: the burst is still to show (world items only, from PostUpdate)
		public bool Pending;

		public const uint FreshTicks = 120;

		// The gap of the item the next CommonCode.DropItem call spawns (our extra-drop rule sets it around the call)
		public static int Next;

		public static int GapOf(Item item) => item != null && !item.IsAir && item.TryGetGlobalItem(out JackpotItem jackpot) ? jackpot.Gap : 0;

		public override void OnSpawn(Item item, IEntitySource source)
		{
			if (Next > 0) Mark(item, Next);
		}

		// After the item got its final type (a type change recreates this global)
		public static void Mark(Item item, int gap)
		{
			if (gap <= 0 || item == null || item.IsAir || !item.TryGetGlobalItem(out JackpotItem jackpot)) return;
			jackpot.Gap = (byte)Math.Min(gap, 9);
			jackpot.MarkedAt = Math.Max(1u, Main.GameUpdateCount);
			jackpot.Pending = !Main.dedServ;
		}

		private bool Fresh => Gap > 0 && MarkedAt != 0 && Main.GameUpdateCount - MarkedAt < FreshTicks;

		public override void NetSend(Item item, BinaryWriter writer)
		{
			writer.Write(Gap);
			writer.Write(Fresh);
		}

		public override void NetReceive(Item item, BinaryReader reader)
		{
			Gap = reader.ReadByte();
			bool fresh = reader.ReadBoolean();
			MarkedAt = fresh ? Math.Max(1u, Main.GameUpdateCount) : 0;
			// The position is not set yet here (message 21 reads the mod data first): shown from PostUpdate
			Pending = fresh && Gap > 0 && !Main.dedServ;
		}

		// World items only
		public override void PostUpdate(Item item)
		{
			if (Gap == 0 || Main.dedServ) return;
			if (Pending)
			{
				Pending = false;
				JackpotEffects.AnnounceOnce(item, Gap);
			}
			JackpotEffects.Glow(item, Gap);
		}

		public override bool OnPickup(Item item, Player player)
		{
			Pending = false;
			MarkedAt = 0;
			return true;
		}

		// A jackpot keeps its own mark: never merged with an ordinary stack, nor with another gap
		public override bool CanStack(Item destination, Item source) => GapOf(destination) == GapOf(source);

		public override bool CanStackInWorld(Item destination, Item source) => GapOf(destination) == GapOf(source);

		public override void PostDrawInWorld(Item item, SpriteBatch spriteBatch, Color lightColor, Color alphaColor, float rotation, float scale, int whoAmI)
		{
			if (Gap >= 2) JackpotEffects.Beam(item, spriteBatch, Gap);
		}

		public override void ModifyTooltips(Item item, List<TooltipLine> tooltips)
		{
			if (Gap > 0) tooltips.Add(new TooltipLine(Mod, "Jackpot", Language.GetTextValue("Mods.UltimateRandomizer.Jackpot.Tooltip", Gap)) { OverrideColor = JackpotEffects.ColorOf(Gap) });
		}
	}

	public static class JackpotEffects
	{
		private static readonly Color Gold = new(255, 215, 80);
		private static readonly Color Orange = new(255, 150, 40);

		public static Color ColorOf(int gap) => gap >= 3 ? Main.DiscoColor : gap == 2 ? Orange : Gold;

		// Items already announced, by world slot (a client gets an item synced several times)
		private static readonly bool[] announced = new bool[Main.maxItems + 1];

		// Once per world slot: the client opening a bag announces its own item, then the server's echo of it is ignored
		public static void AnnounceOnce(Item item, int gap)
		{
			int slot = Array.IndexOf(Main.item, item);
			if (slot < 0 || slot >= Main.maxItems || announced[slot]) return;
			announced[slot] = true;
			Announce(item, gap);
		}

		// Inactive slots may be reused by a new item
		public static void ForgetInactive()
		{
			for (int i = 0; i < Main.maxItems; i++)
			{
				if (announced[i] && !Main.item[i].active) announced[i] = false;
			}
		}

		public static void Clear() => Array.Clear(announced);

		private static void Announce(Item item, int gap)
		{
			Color color = ColorOf(gap);
			string popup = Language.GetTextValue(gap >= 3 ? "Mods.UltimateRandomizer.Jackpot.PopupBig" : "Mods.UltimateRandomizer.Jackpot.Popup");
			CombatText.NewText(item.Hitbox, color, popup, gap >= 2, false);
			int dusts = 12 * gap;
			for (int i = 0; i < dusts; i++)
			{
				int type = gap >= 3 ? DustID.RainbowMk2 : i % 2 == 0 ? DustID.GoldCoin : DustID.TreasureSparkle;
				Dust dust = Dust.NewDustDirect(item.position, item.width, item.height, type, 0f, 0f, 0, gap >= 3 ? Main.hslToRgb(i / (float)dusts, 1f, 0.6f) : default, 1.2f + 0.3f * gap);
				dust.velocity = Main.rand.NextVector2Circular(2f + gap, 2f + gap);
				dust.noGravity = true;
			}
			SoundStyle sound = gap >= 3 ? SoundID.AchievementComplete : gap == 2 ? SoundID.Item4 : SoundID.CoinPickup;
			SoundEngine.PlaySound(sound.WithVolumeScale(gap >= 3 ? 1.4f : 1f), item.Center);
			if (gap < 2) return;
			string line = Language.GetTextValue("Mods.UltimateRandomizer.Jackpot.Chat", Lang.GetItemNameValue(item.type), gap);
			Main.NewText(gap >= 3 ? line.ToUpperInvariant() : line, color);
		}

		// While it lies on the ground: light, and sparkles more often the higher it is
		public static void Glow(Item item, int gap)
		{
			Color color = ColorOf(gap);
			float strength = 0.5f + 0.35f * gap;
			Lighting.AddLight(item.Center, color.ToVector3() * strength);
			if (Main.rand.Next(Math.Max(2, 12 - 4 * gap)) != 0) return;
			int type = gap >= 3 ? DustID.RainbowMk2 : DustID.TreasureSparkle;
			Dust dust = Dust.NewDustDirect(item.position, item.width, item.height, type, 0f, -1f, 0, gap >= 3 ? Main.DiscoColor : default, 0.9f + 0.2f * gap);
			dust.noGravity = true;
		}

		// A column of light above the item (2 tiers and more), wider and brighter from 3
		public static void Beam(Item item, SpriteBatch spriteBatch, int gap)
		{
			Vector2 bottom = item.Bottom - Main.screenPosition;
			int width = gap >= 3 ? 10 : 4;
			int height = gap >= 3 ? 420 : 200;
			float pulse = 0.75f + 0.25f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 4f);
			Color color = ColorOf(gap) * ((gap >= 3 ? 0.45f : 0.3f) * pulse);
			color.A = 0;
			spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle((int)bottom.X - width / 2, (int)bottom.Y - height, width, height), color);
			spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle((int)bottom.X - width, (int)bottom.Y - height / 3, width * 2, height / 3), color * 0.6f);
		}
	}

	public class JackpotSystem : ModSystem
	{
		public override void PostUpdateItems()
		{
			if (!Main.dedServ && Main.GameUpdateCount % 30 == 0) JackpotEffects.ForgetInactive();
		}

		public override void ClearWorld() => JackpotEffects.Clear();
	}
}

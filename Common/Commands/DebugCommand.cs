using System;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Commands
{
	// /rdebug item <name or id>   category, tier and stack of an item
	// /rdebug npc <name or id>    tier of an enemy
	// /rdebug tier                tier unlocked in this world
	// /rdebug pool                writes the number of items per category and tier to the log
	public class DebugCommand : ModCommand
	{
		public override CommandType Type => CommandType.Chat;

		public override string Command => "rdebug";

		public override string Usage => "/rdebug item <name|id> | npc <name|id> | tier | pool";

		public override string Description => Language.GetTextValue("Mods.UltimateRandomizer.Commands.Debug.Description");

		public override void Action(CommandCaller caller, string input, string[] args)
		{
			if (args.Length == 0)
			{
				caller.Reply(Usage, Color.Yellow);
				return;
			}
			string rest = string.Join(" ", args, 1, args.Length - 1);
			switch (args[0].ToLowerInvariant())
			{
				case "item":
					ShowItem(caller, rest);
					break;
				case "npc":
					ShowNpc(caller, rest);
					break;
				case "tier":
					caller.Reply(Language.GetTextValue("Mods.UltimateRandomizer.Commands.Debug.Tier", (Tier)ProgressionSystem.UnlockedTier, ProgressionSystem.UnlockedTier), Color.Yellow);
					break;
				case "pool":
					LogPool();
					caller.Reply(Language.GetTextValue("Mods.UltimateRandomizer.Commands.Debug.PoolLogged"), Color.Yellow);
					break;
				default:
					caller.Reply(Usage, Color.Yellow);
					break;
			}
		}

		private static void ShowItem(CommandCaller caller, string name)
		{
			int type = FindItem(name);
			if (!ItemPool.IsValid(type))
			{
				caller.Reply(Language.GetTextValue("Mods.UltimateRandomizer.Commands.Debug.NotFound", name), Color.OrangeRed);
				return;
			}
			ItemInfo info = ItemPool.Info[type];
			(int min, int max) = StackRules.Range(type);
			caller.Reply(Language.GetTextValue("Mods.UltimateRandomizer.Commands.Debug.Item",
				Lang.GetItemNameValue(type), info.Category, (Tier)info.Tier, info.Tier, info.Protected, info.Excluded, min, max), Color.Yellow);
		}

		private static void ShowNpc(CommandCaller caller, string name)
		{
			int type = FindNpc(name);
			if (type == 0)
			{
				caller.Reply(Language.GetTextValue("Mods.UltimateRandomizer.Commands.Debug.NotFound", name), Color.OrangeRed);
				return;
			}
			caller.Reply(Language.GetTextValue("Mods.UltimateRandomizer.Commands.Debug.Npc",
				Lang.GetNPCNameValue(type), NpcTiers.BaseTier(type), NpcTiers.EffectiveTier(type)), Color.Yellow);
		}

		private static int FindItem(string name)
		{
			name = name.Trim();
			if (int.TryParse(name, out int id)) return id;
			if (ItemID.Search.TryGetId(name.Replace(" ", ""), out id)) return id;
			for (int type = 1; type < ItemLoader.ItemCount; type++)
			{
				if (Lang.GetItemNameValue(type).Equals(name, StringComparison.OrdinalIgnoreCase)) return type;
			}
			return 0;
		}

		private static int FindNpc(string name)
		{
			name = name.Trim();
			if (int.TryParse(name, out int id)) return id;
			if (NPCID.Search.TryGetId(name.Replace(" ", ""), out id)) return id;
			for (int type = 1; type < NPCLoader.NPCCount; type++)
			{
				if (Lang.GetNPCNameValue(type).Equals(name, StringComparison.OrdinalIgnoreCase)) return type;
			}
			return 0;
		}

		private static void LogPool()
		{
			StringBuilder sb = new("Item pool (category: count per tier 0..9)\n");
			for (int c = 0; c < ItemCategories.Count; c++)
			{
				sb.Append($"  {(ItemCategory)c,-13}:");
				for (int t = 0; t < Tiers.Count; t++) sb.Append($" {ItemPool.BucketSize((ItemCategory)c, t),4}");
				sb.Append('\n');
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info(sb.ToString());
		}
	}
}

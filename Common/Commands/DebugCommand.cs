using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Codex;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Commands
{
	// /rdebug item <name or id>   category, tier and stack of an item
	// /rdebug npc <name or id>    tier and drop table of an enemy in this world
	// /rdebug tier                tier unlocked in this world
	// /rdebug pool                writes the number of items per category and tier to the log
	// /rdebug swaps               what the enemy and critter swaps did since the world was loaded
	// /rdebug recipes             share of the vanilla recipes changed, and why the others keep their result (also logged)
	// /rdebug biomes              the enemies of each biome (by-biome redistribution): a summary, the full list in the log
	// /rdebug codex               what the codex of this world holds (kills, drops, sources, recipes, sightings)
	public class DebugCommand : ModCommand
	{
		public override CommandType Type => CommandType.Chat;

		public override string Command => "rdebug";

		public override string Usage => "/rdebug item <name|id> | npc <name|id> | tier | pool | swaps | recipes | biomes | codex";

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
				case "recipes":
					if (!RecipeSystem.Applied)
					{
						caller.Reply(Language.GetTextValue("Mods.UltimateRandomizer.Commands.Debug.RecipesOff"), Color.Yellow);
						break;
					}
					string summary = RecipeStats.Compute().Summary();
					ModContent.GetInstance<UltimateRandomizer>().Logger.Info(summary);
					caller.Reply(summary, Color.Yellow);
					break;
				case "swaps":
					SwapStats s = EnemySwapSystem.Stats;
					caller.Reply(Language.GetTextValue("Mods.UltimateRandomizer.Debug.SwapStats", s.Flagged, s.SwappedEnemies, s.SwappedCritters, s.NotInPool, s.ChanceFailed, s.NoTarget, s.Inactive), Color.Yellow);
					break;
				case "biomes":
					ShowBiomes(caller);
					break;
				case "codex":
					ShowCodex(caller);
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
			if (!SeedSystem.WorldReady || !ItemPool.Ready) return;
			List<PlannedDrop> drops = RandomNpcDropRule.Plan(type, Main.expertMode);
			if (drops.Count == 0)
			{
				caller.Reply(Language.GetTextValue("Mods.UltimateRandomizer.Commands.Debug.NoDrops"), Color.Yellow);
			}
			foreach (PlannedDrop drop in drops)
			{
				string stack = drop.StackMin == drop.StackMax ? drop.StackMin.ToString() : $"{drop.StackMin}-{drop.StackMax}";
				string chance = drop.Chance >= 1f ? "100%" : $"{drop.Chance * 100:0.##}% (1/{Math.Round(1 / drop.Chance)})";
				string conditions = drop.Conditions == null || drop.Conditions.Count == 0 ? "" : " *";
				caller.Reply($"  {Lang.GetItemNameValue(drop.Item)} x{stack} : {chance}{conditions}", Color.White);
			}
		}

		private static void ShowCodex(CommandCaller caller)
		{
			CodexData d = CodexSystem.Data;
			int sightings = d.Sightings.Values.Sum(s => s.Count);
			caller.Reply(Language.GetTextValue("Mods.UltimateRandomizer.Commands.Debug.Codex", d.Version, d.Kills.Values.Sum(), d.Kills.Count,
				d.Drops.Values.Sum(t => t.Values.Sum()), d.Drops.Count, d.Recipes.Count, d.ItemsSeen.Count, sightings, d.Sightings.Count,
				d.OpenedChests.Count, d.Sources[(int)CodexSource.Bag].Count), Color.Yellow);
			caller.Reply(Language.GetTextValue("Mods.UltimateRandomizer.Commands.Debug.CodexSources", d.Summary()), Color.Yellow);
		}

		private static void ShowBiomes(CommandCaller caller)
		{
			if (!SeedSystem.WorldReady)
			{
				caller.Reply(Language.GetTextValue("Mods.UltimateRandomizer.Debug.NeedWorld"), Color.OrangeRed);
				return;
			}
			if (!BiomeSlots.Active) caller.Reply(Language.GetTextValue("Mods.UltimateRandomizer.Commands.Debug.BiomesOff"), Color.Yellow);
			BiomeSlotMap map = BiomeSlots.Current;
			string Name(int id) => Lang.GetNPCNameValue(id);
			StringBuilder sb = new($"{BiomeSlots.Summary(map)} (seed {SeedSystem.WorldSeed}, {map.ExtraTarget} extras wanted)\n");
			foreach (IGrouping<int, BiomeSlot> biome in map.Slots.GroupBy(s => s.Biome).OrderBy(g => g.Key))
			{
				int enemies = biome.Count(s => !s.Critter);
				int changed = biome.Count(s => s.Owner != s.Original);
				int extras = biome.Count(s => s.Extra != 0);
				caller.Reply(Language.GetTextValue("Mods.UltimateRandomizer.Commands.Debug.Biome", BiomeSlots.BiomeName(biome.Key), biome.Count(), enemies, changed, extras), Color.Yellow);
				sb.Append($"  {BiomeSlots.BiomeName(biome.Key)} ({biome.Count()} slots):\n");
				foreach (BiomeSlot slot in biome)
				{
					string band = slot.Band switch { 0 => "", 1 => " [hardmode]", _ => " [post-Plantera]" };
					string tags = $" tier {slot.Tier}" + (slot.Merged ? " merged" : "") + (slot.Rare ? " rare" : "") + (slot.Conditional ? " conditional" : "") + (slot.Critter ? " critter" : "")
						+ (slot.Time != SlotTime.Any ? " " + slot.Time.ToString().ToLowerInvariant() : "") + (slot.DaySurface ? " day-surface" : "");
					string shared = slot.Extra != 0 ? $" + {Name(slot.Extra)} (shared, {BiomeSlots.ExtraShare:P0})" : "";
					if (slot.EvilShared != null) shared += $" + {string.Join(", ", slot.EvilShared.Select(Name))} (absent evil, {BiomeSlots.EvilShare:P0})";
					if (slot.AbsentEvil) tags += " absent-evil";
					sb.Append($"    {Name(slot.Original)} -> {Name(slot.Owner)}{shared}{band}{tags}\n");
				}
			}
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info(sb.ToString());
			caller.Reply(Language.GetTextValue("Mods.UltimateRandomizer.Commands.Debug.BiomesLogged"), Color.Yellow);
		}

		private static int FindItem(string name)
		{
			name = name.Trim();
			if (int.TryParse(name, out int id)) return id > 0 && id < ItemLoader.ItemCount ? id : 0;
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
			if (int.TryParse(name, out int id)) return id > 0 && id < NPCLoader.NPCCount ? id : 0;
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

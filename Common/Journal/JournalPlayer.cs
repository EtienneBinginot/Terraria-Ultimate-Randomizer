using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Journal
{
	// Remembers which bags this character opened in each world (monsters come from the bestiary kill counts)
	public class JournalPlayer : ModPlayer
	{
		private Dictionary<string, HashSet<string>> openedBags = new();

		public static string WorldKey => Main.ActiveWorldFileData == null ? "" : Main.ActiveWorldFileData.UniqueId + "/" + SeedSystem.WorldSeed;

		public bool HasOpened(int bagType) => openedBags.TryGetValue(WorldKey, out HashSet<string> bags) && bags.Contains(DetRandom.ItemName(bagType));

		public void RecordOpened(int bagType)
		{
			if (!openedBags.TryGetValue(WorldKey, out HashSet<string> bags))
			{
				bags = new HashSet<string>();
				openedBags[WorldKey] = bags;
			}
			bags.Add(DetRandom.ItemName(bagType));
		}

		public override void ProcessTriggers(TriggersSet triggersSet)
		{
			if (JournalSystem.ToggleKeybind != null && JournalSystem.ToggleKeybind.JustPressed) JournalSystem.Toggle();
		}

		public override void SaveData(TagCompound tag)
		{
			tag["worlds"] = openedBags.Keys.ToList();
			tag["bags"] = openedBags.Values.Select(set => string.Join("|", set)).ToList();
		}

		public override void LoadData(TagCompound tag)
		{
			openedBags = new Dictionary<string, HashSet<string>>();
			IList<string> worlds = tag.GetList<string>("worlds");
			IList<string> bags = tag.GetList<string>("bags");
			for (int i = 0; i < worlds.Count && i < bags.Count; i++)
			{
				openedBags[worlds[i]] = bags[i].Split('|', System.StringSplitOptions.RemoveEmptyEntries).ToHashSet();
			}
		}
	}

	public class JournalBagItem : GlobalItem
	{
		public override void RightClick(Item item, Player player)
		{
			if (player.whoAmI == Main.myPlayer && LootSnapshot.Items.ContainsKey(item.type))
			{
				player.GetModPlayer<JournalPlayer>().RecordOpened(item.type);
			}
		}
	}
}

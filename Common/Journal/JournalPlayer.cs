using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using UltimateRandomizer.Common.Codex;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Journal
{
	// Codex hotkey. Bags opened are now in the world codex: the per-character lists of older versions are moved there
	// when the character enters the matching world (the lists of other worlds are kept until then).
	public class JournalPlayer : ModPlayer
	{
		private Dictionary<string, HashSet<string>> legacyBags = new();

		public static string WorldKey => Main.ActiveWorldFileData == null ? "" : Main.ActiveWorldFileData.UniqueId + "/" + SeedSystem.WorldSeed;

		public override void ProcessTriggers(TriggersSet triggersSet)
		{
			// J typed in the codex search is text, not the hotkey
			if (JournalSystem.IsTyping || Codex.Interface.HintSystem.IsTyping) return;
			if (JournalSystem.ToggleKeybind != null && JournalSystem.ToggleKeybind.JustPressed) JournalSystem.Toggle();
		}

		public override void OnEnterWorld()
		{
			if (!SeedSystem.WorldReady || !legacyBags.TryGetValue(WorldKey, out HashSet<string> bags)) return;
			foreach (string bag in bags)
			{
				int type = CodexNames.ItemType(bag);
				if (type > 0) CodexSystem.RecordBagOpened(type);
			}
		}

		// The old list is dropped once the codex has every bag of it (in multiplayer, once the server confirmed them)
		public override void PostUpdate()
		{
			if (Player.whoAmI != Main.myPlayer || legacyBags.Count == 0 || Main.GameUpdateCount % 60 != 0 || !SeedSystem.WorldReady) return;
			string key = WorldKey;
			if (legacyBags.TryGetValue(key, out HashSet<string> bags) && bags.All(bag => CodexNames.ItemType(bag) <= 0 || CodexSystem.HasOpenedBag(CodexNames.ItemType(bag))))
			{
				legacyBags.Remove(key);
			}
		}

		public override void SaveData(TagCompound tag)
		{
			if (legacyBags.Count == 0) return;
			tag["worlds"] = legacyBags.Keys.ToList();
			tag["bags"] = legacyBags.Values.Select(set => string.Join("|", set)).ToList();
		}

		public override void LoadData(TagCompound tag)
		{
			legacyBags = new Dictionary<string, HashSet<string>>();
			IList<string> worlds = tag.GetList<string>("worlds");
			IList<string> bags = tag.GetList<string>("bags");
			for (int i = 0; i < worlds.Count && i < bags.Count; i++)
			{
				legacyBags[worlds[i]] = bags[i].Split('|', System.StringSplitOptions.RemoveEmptyEntries).ToHashSet();
			}
		}
	}
}

using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using UltimateRandomizer.Common.Configs;

namespace UltimateRandomizer.Common.Features
{
	// A Magic Storage starter kit, once per character and per world (characters made before this feature included), when
	// Magic Storage is loaded. Weak reference: its items are found by name, nothing is needed when it is absent. Runs in
	// OnEnterWorld, on the client that owns the player; QuickSpawnItem syncs the items like any spawned item.
	public class StorageKitPlayer : ModPlayer
	{
		private HashSet<string> kitWorlds = new();

		// Each piece of the kit: its internal names (the first one found is given) and how many
		public static readonly (string[] names, int stack)[] Kit =
		{
			(new[] { "StorageHeart" }, 1),
			(new[] { "StorageUnit" }, 4),
			(new[] { "CraftingAccess" }, 1),
			(new[] { "PortableAccessPreHM", "PortableAccess" }, 1),
			(new[] { "PortableCraftingAccessPreHM", "PortableCraftingAccess" }, 1)
		};

		// The kit's items in a loaded storage mod; missing: the pieces none of whose names exist (renamed in a new version)
		public static List<(int type, int stack)> Resolve(string modName, List<string> missing)
		{
			List<(int, int)> items = new();
			foreach ((string[] names, int stack) in Kit)
			{
				ModItem found = null;
				foreach (string name in names)
				{
					if (ModContent.TryFind(modName, name, out found)) break;
				}
				if (found != null) items.Add((found.Type, stack));
				else missing?.Add(names[0]);
			}
			return items;
		}

		// True the first time a world is claimed in this set (then remembered): the kit is given once per world
		public static bool Claim(HashSet<string> worlds, string worldId) => !string.IsNullOrEmpty(worldId) && worlds.Add(worldId);

		// The items to give on entering a world (claiming it), empty when this world already had its kit or none of the kit
		// exists. Static and with an explicit set and world id, for the tests
		public static List<(int type, int stack)> Grant(HashSet<string> worlds, string worldId, string modName, List<string> missing)
		{
			if (string.IsNullOrEmpty(worldId) || worlds.Contains(worldId)) return new List<(int, int)>();
			List<(int type, int stack)> kit = Resolve(modName, missing);
			return kit.Count > 0 && Claim(worlds, worldId) ? kit : new List<(int, int)>();
		}

		public override void OnEnterWorld()
		{
			if (!ExtrasConfig.Instance.MagicStorageKit || Main.ActiveWorldFileData == null || !ModLoader.HasMod("MagicStorage")) return;
			List<string> missing = new();
			List<(int type, int stack)> kit = Grant(kitWorlds, Main.ActiveWorldFileData.UniqueId.ToString(), "MagicStorage", missing);
			if (missing.Count > 0) Mod.Logger.Warn($"Magic Storage kit: items not found: {string.Join(", ", missing)}");
			if (kit.Count == 0) return;
			foreach ((int type, int stack) in kit) Player.QuickSpawnItem(Player.GetSource_Misc("UltimateRandomizer_StorageKit"), type, stack);
			Main.NewText(Language.GetTextValue("Mods.UltimateRandomizer.Chat.StorageKit"), Color.LightGreen);
		}

		public override void SaveData(TagCompound tag) => tag["storageKitWorlds"] = kitWorlds.ToList();

		public override void LoadData(TagCompound tag) => kitWorlds = tag.GetList<string>("storageKitWorlds").ToHashSet();
	}
}

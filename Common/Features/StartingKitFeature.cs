using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	// A random starting kit, given once per character and per world (the world must exist to know its seed)
	public class StartingKitPlayer : ModPlayer
	{
		private HashSet<string> kitWorlds = new();

		private static readonly ItemCategory[] WeaponCategories =
		{
			ItemCategory.WeaponMelee, ItemCategory.WeaponRanged, ItemCategory.WeaponMagic, ItemCategory.WeaponSummon
		};

		public override void OnEnterWorld()
		{
			ExtrasConfig cfg = ExtrasConfig.Instance;
			if (!cfg.StartingKit || !ItemPool.Ready || Main.ActiveWorldFileData == null) return;
			string worldId = Main.ActiveWorldFileData.UniqueId.ToString();
			if (!kitWorlds.Add(worldId)) return;

			int playerKey = DetRandom.StableHash(Player.name);
			System.Random rng = DetRandom.Get(RollKey.StartingKit, playerKey);
			List<int> kit = new();
			// A banned item is re-picked with its own rng (keyed by its place in the kit): the rest of the kit stays the same
			int Pick(ItemCategory category) => ItemPool.PickAllowed(category, 0, rng, new[] { RollKey.StartingKit, playerKey, kit.Count });
			for (int i = 0; i < cfg.KitWeapons; i++) kit.Add(Pick(WeaponCategories[rng.Next(WeaponCategories.Length)]));
			for (int i = 0; i < cfg.KitAccessories; i++) kit.Add(Pick(ItemCategory.Accessory));
			for (int i = 0; i < cfg.KitTools; i++) kit.Add(Pick(ItemCategory.Tool));
			for (int i = 0; i < cfg.KitPotions; i++) kit.Add(Pick(ItemCategory.Potion));

			foreach (int type in kit)
			{
				(int min, int max) = StackRules.Range(type);
				Player.QuickSpawnItem(Player.GetSource_Misc("UltimateRandomizer_StartingKit"), type, rng.Next(min, max + 1));
			}
			Main.NewText(Language.GetTextValue("Mods.UltimateRandomizer.Chat.StartingKit"), Color.LightGreen);
		}

		public override void SaveData(TagCompound tag) => tag["kitWorlds"] = kitWorlds.ToList();

		public override void LoadData(TagCompound tag) => kitWorlds = tag.GetList<string>("kitWorlds").ToHashSet();
	}
}

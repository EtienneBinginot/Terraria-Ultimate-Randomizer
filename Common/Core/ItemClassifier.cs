using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.Default;
using UltimateRandomizer.Common.Configs;

namespace UltimateRandomizer.Common.Core
{
	// Sorts every item into a category and a tier, from its properties
	public static class ItemClassifier
	{
		// Items whose rarity gives a misleading tier
		private static readonly Dictionary<int, Tier> KnownTiers = new()
		{
			[ItemID.Meteorite] = Tier.PostEvilBoss,
			[ItemID.MeteoriteBar] = Tier.PostEvilBoss,
			[ItemID.Hellstone] = Tier.PostSkeletron,
			[ItemID.HellstoneBar] = Tier.PostSkeletron,
			[ItemID.Obsidian] = Tier.PostEvilBoss,
			[ItemID.CobaltOre] = Tier.Hardmode,
			[ItemID.PalladiumOre] = Tier.Hardmode,
			[ItemID.MythrilOre] = Tier.Hardmode,
			[ItemID.OrichalcumOre] = Tier.Hardmode,
			[ItemID.AdamantiteOre] = Tier.Hardmode,
			[ItemID.TitaniumOre] = Tier.Hardmode,
			[ItemID.CobaltBar] = Tier.Hardmode,
			[ItemID.PalladiumBar] = Tier.Hardmode,
			[ItemID.MythrilBar] = Tier.Hardmode,
			[ItemID.OrichalcumBar] = Tier.Hardmode,
			[ItemID.AdamantiteBar] = Tier.Hardmode,
			[ItemID.TitaniumBar] = Tier.Hardmode,
			[ItemID.SoulofLight] = Tier.Hardmode,
			[ItemID.SoulofNight] = Tier.Hardmode,
			[ItemID.SoulofFlight] = Tier.Hardmode,
			[ItemID.CrystalShard] = Tier.Hardmode,
			[ItemID.PixieDust] = Tier.Hardmode,
			[ItemID.UnicornHorn] = Tier.Hardmode,
			[ItemID.CursedFlame] = Tier.Hardmode,
			[ItemID.Ichor] = Tier.Hardmode,
			[ItemID.HallowedBar] = Tier.PostMechBoss,
			[ItemID.SoulofMight] = Tier.PostMechBoss,
			[ItemID.SoulofSight] = Tier.PostMechBoss,
			[ItemID.SoulofFright] = Tier.PostMechBoss,
			[ItemID.ChlorophyteOre] = Tier.PostMechBoss,
			[ItemID.ChlorophyteBar] = Tier.PostMechBoss,
			[ItemID.Ectoplasm] = Tier.PostPlantera,
			[ItemID.SpectreBar] = Tier.PostPlantera,
			[ItemID.ShroomiteBar] = Tier.PostPlantera,
			[ItemID.TurtleShell] = Tier.PostPlantera,
			[ItemID.BeetleHusk] = Tier.PostGolem,
			[ItemID.LihzahrdBrick] = Tier.PostPlantera,
			[ItemID.FragmentSolar] = Tier.PostCultist,
			[ItemID.FragmentVortex] = Tier.PostCultist,
			[ItemID.FragmentNebula] = Tier.PostCultist,
			[ItemID.FragmentStardust] = Tier.PostCultist,
			[ItemID.LunarOre] = Tier.PostMoonLord,
			[ItemID.LunarBar] = Tier.PostMoonLord
		};

		public static bool IsExcluded(int type, Item item, HashSet<int> excludedItems, HashSet<string> excludedMods)
		{
			if (item == null || item.IsAir || item.type != type) return true;
			if (ItemID.Sets.Deprecated[type] || ItemID.Sets.ItemsThatShouldNotBeInInventory[type]) return true;
			if (ItemID.Sets.IsAPickup[type] || ItemID.Sets.CommonCoin[type]) return true;
			if (item.questItem) return true;
			if (item.ModItem is UnloadedItem) return true;
			if (excludedItems.Contains(type)) return true;
			if (item.ModItem != null && excludedMods.Contains(item.ModItem.Mod.Name)) return true;
			return false;
		}

		public static ItemCategory Classify(int type, Item item)
		{
			if (ItemID.Sets.SortingPriorityBossSpawns[type] >= 0) return ItemCategory.BossSummon;
			if (ItemID.Sets.BossBag[type] || ItemID.Sets.IsFishingCrate[type] || ItemID.Sets.OpenableBag[type]) return ItemCategory.Bag;
			if (item.makeNPC > 0) return ItemCategory.Critter;
			if (item.dye > 0 || item.hairDye >= 0) return ItemCategory.Dye;
			if (item.vanity) return ItemCategory.Vanity;
			if (item.accessory) return ItemCategory.Accessory;
			if (item.headSlot >= 0 || item.bodySlot >= 0 || item.legSlot >= 0) return ItemCategory.Armor;
			if (item.pick > 0 || item.axe > 0 || item.hammer > 0 || item.fishingPole > 0 || (item.shoot > ProjectileID.None && item.shoot < Main.projHook.Length && Main.projHook[item.shoot]))
			{
				return ItemCategory.Tool;
			}
			if (item.ammo != AmmoID.None && item.damage > 0 && item.notAmmo == false) return ItemCategory.Ammo;
			if (item.damage > 0 && item.useStyle != ItemUseStyleID.None)
			{
				if (item.CountsAsClass(DamageClass.Summon) || item.CountsAsClass(DamageClass.SummonMeleeSpeed)) return ItemCategory.WeaponSummon;
				if (item.CountsAsClass(DamageClass.Melee)) return ItemCategory.WeaponMelee;
				if (item.CountsAsClass(DamageClass.Ranged)) return ItemCategory.WeaponRanged;
				if (item.CountsAsClass(DamageClass.Magic)) return ItemCategory.WeaponMagic;
				return ItemCategory.WeaponOther;
			}
			if (item.mountType >= 0) return ItemCategory.MountPet;
			if (item.buffType > 0 && item.buffType < Main.vanityPet.Length && (Main.vanityPet[item.buffType] || Main.lightPet[item.buffType])) return ItemCategory.MountPet;
			if (ItemID.Sets.IsFood[type]) return ItemCategory.Food;
			if (item.consumable && (item.healLife > 0 || item.healMana > 0 || item.buffType > 0)) return ItemCategory.Potion;
			if (item.createTile >= TileID.Dirt)
			{
				if (TileID.Sets.Ore[item.createTile] || item.createTile == TileID.MetalBars) return ItemCategory.OreBar;
				if (item.createTile == TileID.ExposedGems) return ItemCategory.Material;
				return Main.tileFrameImportant[item.createTile] ? ItemCategory.Furniture : ItemCategory.Block;
			}
			if (item.createWall > WallID.None) return ItemCategory.Block;
			if (item.material) return ItemCategory.Material;
			return ItemCategory.Misc;
		}

		public static int ComputeTier(int type, Item item, Dictionary<int, Tier> overrides)
		{
			if (overrides.TryGetValue(type, out Tier overridden)) return (int)overridden;
			if (KnownTiers.TryGetValue(type, out Tier known)) return (int)known;
			int rare = item.rare;
			int tier;
			if (rare == ItemRarityID.Expert || rare == ItemRarityID.Master || rare == ItemRarityID.Quest)
			{
				// Special rarities say nothing about progression: use the price instead
				int gold = item.value / Item.gold;
				tier = gold < 1 ? 1 : gold < 3 ? 3 : gold < 6 ? 5 : gold < 10 ? 6 : 7;
			}
			else
			{
				tier = rare switch
				{
					<= 0 => 0,
					1 => 1,
					2 => 2,
					3 => 3,
					4 => 4,
					5 or 6 => 5,
					7 => 6,
					8 => 7,
					9 => 8,
					_ => 9
				};
				// Cheap-looking but valuable items are a bit further in the game
				if (rare <= 1 && item.value >= Item.gold) tier++;
			}
			return Tiers.Clamp(tier);
		}
	}
}

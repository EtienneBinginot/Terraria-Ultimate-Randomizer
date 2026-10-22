using System.Collections.Generic;
using System.Linq;
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
			[ItemID.LunarBar] = Tier.PostMoonLord,
			// Late drops and blocks with a low rarity: without them, what is crafted from them would take their early tier
			// (LowerByRecipes: Spooky armor from Spooky Wood at a work bench)
			[ItemID.SpiderFang] = Tier.Hardmode,
			[ItemID.DarkShard] = Tier.Hardmode,
			[ItemID.LightShard] = Tier.Hardmode,
			[ItemID.AncientCloth] = Tier.Hardmode,
			[ItemID.Pearlwood] = Tier.Hardmode,
			[ItemID.PearlstoneBlock] = Tier.Hardmode,
			[ItemID.PearlsandBlock] = Tier.Hardmode,
			[ItemID.SpookyWood] = Tier.PostPlantera,
			// Only from the Dungeon after Skeletron, and the Steampunker in hardmode (Necro armor, cog machines)
			[ItemID.Bone] = Tier.PostSkeletron,
			[ItemID.Cog] = Tier.Hardmode,
			[ItemID.AncientBattleArmorMaterial] = Tier.Hardmode,
			[ItemID.FrostCore] = Tier.Hardmode,
			[ItemID.MartianConduitPlating] = Tier.PostGolem
		};

		public static bool HasKnownTier(int type) => KnownTiers.ContainsKey(type);

		public static bool IsExcluded(int type, Item item, HashSet<int> excludedItems, HashSet<string> excludedMods)
		{
			if (item == null || item.IsAir || item.type != type) return true;
			if (ItemID.Sets.Deprecated[type] || ItemID.Sets.ItemsThatShouldNotBeInInventory[type]) return true;
			if (ItemID.Sets.IsAPickup[type] || ItemID.Sets.CommonCoin[type]) return true;
			// Old One's Army currency and sentry fuel
			if (type is ItemID.DefenderMedal or ItemID.DD2EnergyCrystal) return true;
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

		// Items made by vanilla recipes are found no later than their best recipe allows: the highest tier among its
		// ingredients (a recipe group: its earliest item) and stations (a progression station at its tier, a basic one at 0,
		// any other at the earliest item placing it), repeated until nothing changes (ingredients may be crafted). Rarity
		// misleads for crafted items: a Yellow Horseshoe Balloon (rarity 4, hardmode) is made from pre-hardmode parts at the
		// Tinkerer's Workshop. Only lowers; KnownTiers and the config overrides keep theirs; modded recipes are left out (their
		// mod chose the rarities). Run when recipes are set up (ItemPool.Build in PostSetupRecipes), before RecipeSystem
		// randomizes them on world load: the live recipes are the vanilla ones then, the same on every machine. Returns how
		// many items it lowered, and a few of them for the log
		public static int LowerByRecipes(int[] tiers, Dictionary<int, Tier> overrides, out string examples)
		{
			List<(int result, List<int[]> parts, List<int> tiles)> recipes = new();
			for (int i = 0; i < Recipe.numRecipes; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (!SetsTier(recipe)) continue;
				int result = recipe.createItem.type;
				if (result >= tiers.Length || overrides.ContainsKey(result) || KnownTiers.ContainsKey(result)) continue;
				List<int[]> parts = new();
				foreach (Item ingredient in recipe.requiredItem)
				{
					if (ingredient == null || ingredient.type <= 0 || ingredient.type >= tiers.Length) continue;
					parts.Add(IngredientChoices(recipe, ingredient.type).Where(t => t > 0 && t < tiers.Length).ToArray());
				}
				recipes.Add((result, parts, recipe.requiredTile.Where(t => t >= 0).Distinct().ToList()));
			}
			Dictionary<int, List<int>> placers = new();
			foreach ((int type, Item sample) in ContentSamples.ItemsByType)
			{
				if (sample == null || sample.createTile < TileID.Dirt || type <= 0 || type >= tiers.Length) continue;
				if (!placers.TryGetValue(sample.createTile, out List<int> list)) placers[sample.createTile] = list = new List<int>();
				list.Add(type);
			}
			int[] before = (int[])tiers.Clone();
			bool changed = true;
			for (int pass = 0; pass < 32 && changed; pass++)
			{
				changed = false;
				foreach ((int result, List<int[]> parts, List<int> tiles) in recipes)
				{
					int tier = 0;
					foreach (int[] part in parts) tier = System.Math.Max(tier, part.Length == 0 ? Tiers.Max : part.Min(t => tiers[t]));
					foreach (int tile in tiles) tier = System.Math.Max(tier, StationTier(tile, placers, t => tiers[t]));
					if (tier >= tiers[result]) continue;
					tiers[result] = tier;
					changed = true;
				}
			}
			int count = Enumerable.Range(1, tiers.Length - 1).Count(t => tiers[t] < before[t]);
			examples = string.Join(", ", new[] { ItemID.YellowHorseshoeBalloon, ItemID.BundleofBalloons, ItemID.FrostsparkBoots }.Where(t => t < tiers.Length)
				.Select(t => $"{Lang.GetItemNameValue(t)} {before[t]} > {tiers[t]}"));
			return count;
		}

		// World kinds a recipe may need: such a recipe is no way to make the item in every world
		private static readonly Condition[] WorldConditions =
		{
			Condition.CrimsonWorld, Condition.CorruptWorld, Condition.DrunkWorld, Condition.RemixWorld, Condition.ZenithWorld, Condition.NotDrunkWorld,
			Condition.NotRemixWorld, Condition.NotZenithWorld
		};

		// A vanilla recipe that can lower its result's tier: none tied to a kind of world, and not tModLoader's reverse recipes
		// (wall or platform back to its block: the block would take the wall's rarity)
		public static bool SetsTier(Recipe recipe)
		{
			if (!IsVanillaRecipe(recipe) || recipe.Conditions.Any(c => WorldConditions.Contains(c))) return false;
			if (recipe.requiredItem.Count == 1 && ContentSamples.ItemsByType.TryGetValue(recipe.requiredItem[0].type, out Item part))
			{
				if (part.createWall > 0 || part.createTile >= TileID.Dirt && TileID.Sets.Platforms[part.createTile]) return false;
			}
			return true;
		}

		// A recipe of vanilla Terraria (tModLoader's own count as vanilla, as in RecipeSystem), enabled, with a result
		public static bool IsVanillaRecipe(Recipe recipe) => recipe != null && !recipe.Disabled && (recipe.Mod == null || recipe.Mod.Name == "ModLoader")
			&& recipe.createItem != null && recipe.createItem.type > 0;

		// The items an ingredient of a recipe accepts: every item of its recipe group, else itself
		public static IEnumerable<int> IngredientChoices(Recipe recipe, int ingredient)
		{
			RecipeGroup group = recipe.acceptedGroups.Select(g => RecipeGroup.recipeGroups.TryGetValue(g, out RecipeGroup found) ? found : null)
				.FirstOrDefault(g => g != null && g.ContainsItem(ingredient));
			return group != null ? group.ValidItems.Append(ingredient).Distinct() : new[] { ingredient };
		}

		// The tier from which a station is had, for the tiers from recipes: a progression station at its tier, a basic one at
		// 0, any other at the earliest item placing it (never, without one)
		public static int StationTier(int tile, Dictionary<int, List<int>> placers, System.Func<int, int> tierOf)
		{
			Stations.StationInfo info = Stations.Info(tile);
			if (info.Kind == StationKind.Progression) return info.Tier;
			if (info.Kind == StationKind.Basic) return 0;
			return placers.TryGetValue(tile, out List<int> list) && list.Count > 0 ? list.Min(tierOf) : Tiers.Max;
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

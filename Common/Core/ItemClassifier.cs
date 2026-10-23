using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.GameContent.ItemDropRules;
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

		// Vanilla items no player can get: the guides already read (Critter Companionship, Environmental Preservation and
		// both, "(inactive)"), which ItemID.Sets.Deprecated does not flag
		private static readonly HashSet<int> BannedVanilla = new()
		{
			ItemID.DontHurtCrittersBookInactive, ItemID.DontHurtNatureBookInactive, ItemID.DontHurtComboBookInactive
		};

		// Items of storage mods other than Magic Storage (its own are all banned by mod name): storage units, hearts, cores,
		// upgrades, access points and remotes, matched by their ModItem name
		private static readonly string[] StorageItemNames =
		{
			"StorageUnit", "StorageHeart", "StorageCore", "StorageUpgrade", "StorageAccess", "StorageConnector", "StorageComponent",
			"CraftingAccess", "RemoteAccess", "PortableRemote", "PortableAccess"
		};

		// Never given at random (they stay replaceable when something gives them): tModLoader's own items (start bag...),
		// Recipe Browser's, every item of a storage mod (Magic Storage and the like: a kit gives them, a random unit or
		// upgrade breaks storage progression), and unobtainable vanilla items
		public static bool IsBanned(Item item)
		{
			if (item == null) return false;
			if (BannedVanilla.Contains(item.type)) return true;
			string mod = item.ModItem?.Mod?.Name;
			if (mod == null) return false;
			if (mod is "ModLoader" or "RecipeBrowser") return true;
			// MagicStorage, MagicStorageExtra...
			if (mod.Contains("Storage")) return true;
			string name = item.ModItem.Name ?? "";
			return StorageItemNames.Any(name.Contains);
		}

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
				if (!SetsTier(recipe, recipe.requiredItem.Select(i => i.type).ToList())) continue;
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
			Dictionary<int, List<int>> placers = Stations.PlacersByTile();
			int[] before = (int[])tiers.Clone();
			bool changed = true;
			for (int pass = 0; pass < 32 && changed; pass++)
			{
				changed = false;
				foreach ((int result, List<int[]> parts, List<int> tiles) in recipes)
				{
					int tier = 0;
					foreach (int[] part in parts) tier = System.Math.Max(tier, part.Length == 0 ? Tiers.Max : part.Min(t => tiers[t]));
					foreach (int tile in tiles) tier = System.Math.Max(tier, StationTier(tile, placers, t => t < tiers.Length ? tiers[t] : Tiers.Max));
					if (tier >= tiers[result]) continue;
					tiers[result] = tier;
					changed = true;
				}
			}
			int count = Enumerable.Range(1, tiers.Length - 1).Count(t => tiers[t] < before[t]);
			examples = string.Join(", ", new int[] { ItemID.YellowHorseshoeBalloon, ItemID.BundleofBalloons, ItemID.FrostsparkBoots }.Where(t => t < tiers.Length)
				.Select(t => $"{Lang.GetItemNameValue(t)} {before[t]} > {tiers[t]}"));
			return count;
		}

		// Crafted items that no vanilla recipe makes before a tier above theirs (Pearlwood Sword: rarity 0, but Pearlwood is a
		// hardmode block): their true tier is raised to the earliest of their recipes (the highest tier among its ingredients,
		// a group by its earliest item, and stations), repeated until nothing changes. Only items nothing drops, holds in a bag
		// or sells (lootSources), without a modded recipe, and not set by KnownTiers or the config; never lowers. A thematic
		// station (Solidifier, Living Loom...) raises nothing (RaisingStationTier), and furniture that world generation places
		// (dungeon and Lihzahrd furniture, Water Candle, books) keeps its tier. trueTiers holds the final tiers (after
		// LowerByRecipes) and is raised in place. Returns how many items it raised, and examples
		public static int RaiseByRecipes(int[] trueTiers, Dictionary<int, Tier> overrides, HashSet<int> lootSources, out string examples)
		{
			HashSet<int> moddedResults = new();
			List<(int result, List<int[]> parts, List<int> tiles)> recipes = new();
			for (int i = 0; i < Recipe.numRecipes; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (recipe == null || recipe.Disabled || recipe.createItem == null || recipe.createItem.type <= 0) continue;
				int result = recipe.createItem.type;
				if (!IsVanillaRecipe(recipe))
				{
					moddedResults.Add(result);
					continue;
				}
				if (!SetsTier(recipe, recipe.requiredItem.Select(i => i.type).ToList())) continue;
				if (result >= trueTiers.Length || overrides.ContainsKey(result) || KnownTiers.ContainsKey(result) || lootSources.Contains(result)) continue;
				if (PlacedByWorldGen(result) || WorldSourced.Contains(result)) continue;
				List<int[]> parts = new();
				foreach (Item ingredient in recipe.requiredItem)
				{
					if (ingredient == null || ingredient.type <= 0 || ingredient.type >= trueTiers.Length) continue;
					parts.Add(IngredientChoices(recipe, ingredient.type).Where(t => t > 0 && t < trueTiers.Length).ToArray());
				}
				recipes.Add((result, parts, recipe.requiredTile.Where(t => t >= 0).Distinct().ToList()));
			}
			recipes.RemoveAll(r => moddedResults.Contains(r.result));
			Dictionary<int, List<int>> placers = Stations.PlacersByTile();
			int[] before = (int[])trueTiers.Clone();
			bool changed = true;
			for (int pass = 0; pass < 32 && changed; pass++)
			{
				changed = false;
				// The earliest recipe of each result, with the tiers of this pass
				Dictionary<int, int> earliest = new();
				foreach ((int result, List<int[]> parts, List<int> tiles) in recipes)
				{
					int tier = 0;
					foreach (int[] part in parts) tier = System.Math.Max(tier, part.Length == 0 ? Tiers.Max : part.Min(t => trueTiers[t]));
					foreach (int tile in tiles) tier = System.Math.Max(tier, RaisingStationTier(tile, placers, t => t < trueTiers.Length ? trueTiers[t] : Tiers.Max));
					earliest[result] = earliest.TryGetValue(result, out int other) ? System.Math.Min(other, tier) : tier;
				}
				foreach ((int result, int tier) in earliest)
				{
					if (tier <= trueTiers[result]) continue;
					trueTiers[result] = tier;
					changed = true;
				}
			}
			int count = Enumerable.Range(1, trueTiers.Length - 1).Count(t => trueTiers[t] > before[t]);
			examples = string.Join(", ", new int[] { ItemID.PearlwoodSword, ItemID.PearlwoodWorkBench }.Concat(Enumerable.Range(1, trueTiers.Length - 1).Where(t => trueTiers[t] > before[t]).Take(6))
				.Distinct().Where(t => t < trueTiers.Length).Select(t => $"{Lang.GetItemNameValue(t)} {before[t]} > {trueTiers[t]}"));
			return count;
		}

		// Kinds of vanilla sources (RaiseBySources, TierAudit)
		private const int SourceGathered = 0, SourceDrop = 1, SourceBag = 2, SourceShop = 3, SourceRecipe = 4;

		// The true tier of every item from its vanilla sources, the earliest of:
		// - NPC drops (LootSnapshot.Npcs): a boss at its tier (NpcTiers.BaseTier: the boss table), any other enemy at 0: its
		//   BaseTier is a guess from its stats (Doctor Bones, Tim or the Dungeon Guardian would look mid or late game), and
		//   EffectiveTier follows the world's progression and the config, unknown at load; then raised by the drop's known
		//   conditions (hardmode, mechanical bosses, Plantera, the moon events...); a drop that needs a special seed is none;
		// - bags and crates (LootSnapshot.Items): a boss bag at its boss's tier, any other at its own true tier, + conditions;
		// - shops (Obtainability.ShopEntries): from when the NPC moves in (Wizard: hardmode, Cyborg: Plantera...), + the
		//   entry's conditions; an entry of a special seed is none;
		// - vanilla recipes, as in RaiseByRecipes (thematic stations raise nothing);
		// - gathered in the world (ores, gems, herbs, Fallen Star, blocks, WorldSourced: Life Crystal, clouds, seeds of
		//   plants, Extractinator outputs) or placed by world generation (dungeon furniture): their own tier, so these are
		//   never raised.
		// The true tier is raised to that source tier, never lowered; items without any source, set by KnownTiers or the
		// config, or with a modded recipe (their mod chose the rarity) keep theirs. Repeated until nothing changes (a bag or
		// an ingredient raised raises what comes from it). sourceTiers / sources (filled, per item): the earliest source and
		// its description, -1 / null without one. Load time (ItemPool.Build), the same on every machine: the snapshots, shops
		// and recipes are the vanilla ones, read in a fixed order. Returns how many items it raised, and examples
		public static int RaiseBySources(int[] trueTiers, Dictionary<int, Tier> overrides, int[] sourceTiers, string[] sources, out string examples)
		{
			int count = trueTiers.Length;
			int[] before = (int[])trueTiers.Clone();
			bool Valid(int t) => t > 0 && t < count;
			List<(int item, int kind, int reference, int tier)> fixedSources = new();

			// Gathered or placed by world generation: their own tier
			HashSet<int> gathered = Features.Findability.GatheredMaterials.Concat(WorldSourced).Where(Valid).ToHashSet();
			for (int type = 1; type < count; type++)
			{
				if (!ContentSamples.ItemsByType.TryGetValue(type, out Item sample) || sample == null) continue;
				if (sample.createTile >= TileID.Dirt && sample.createTile < TileID.Sets.Ore.Length && TileID.Sets.Ore[sample.createTile] || PlacedByWorldGen(type)) gathered.Add(type);
			}
			foreach (int item in gathered.OrderBy(t => t)) fixedSources.Add((item, SourceGathered, 0, before[item]));

			// Drops, and the tier of each boss bag (its earliest boss)
			Dictionary<int, int> bossBags = new();
			foreach (int netId in LootSnapshot.Npcs.Keys.OrderBy(k => k))
			{
				int npcTier = NpcSourceTier(netId);
				foreach (DropRateInfo drop in LootSnapshot.Npcs[netId])
				{
					int condition = DropConditionTier(drop.conditions);
					if (condition < 0 || !Valid(drop.itemId)) continue;
					int tier = System.Math.Max(npcTier, condition);
					fixedSources.Add((drop.itemId, SourceDrop, netId, tier));
					if (ItemID.Sets.BossBag[drop.itemId] && npcTier > 0) bossBags[drop.itemId] = bossBags.TryGetValue(drop.itemId, out int other) ? System.Math.Min(other, tier) : tier;
				}
			}

			// Bags: a boss bag at its boss's tier, any other at its own (current) true tier
			List<(int item, int bag, int condition)> bagSources = new();
			foreach (int bag in LootSnapshot.Items.Keys.OrderBy(k => k))
			{
				foreach (DropRateInfo drop in LootSnapshot.Items[bag])
				{
					int condition = DropConditionTier(drop.conditions);
					if (condition < 0 || !Valid(drop.itemId)) continue;
					if (bossBags.TryGetValue(bag, out int bossTier)) fixedSources.Add((drop.itemId, SourceBag, bag, System.Math.Max(bossTier, condition)));
					else if (Valid(bag)) bagSources.Add((drop.itemId, bag, condition));
				}
			}

			// Shops
			Dictionary<Condition, int> shopConditions = ShopConditionTiers();
			foreach ((int npc, int item, Condition[] conditions) in Obtainability.ShopEntries)
			{
				if (!Valid(item) || conditions.Any(c => SeedConditions.Contains(c))) continue;
				int tier = TownNpcTiers.TryGetValue(npc, out Tier arrival) ? (int)arrival : 0;
				foreach (Condition c in conditions) tier = System.Math.Max(tier, shopConditions.TryGetValue(c, out int t) ? t : 0);
				fixedSources.Add((item, SourceShop, npc, tier));
			}

			// Vanilla recipes (every one of them is a source); a modded recipe makes its result keep its tier
			HashSet<int> keep = new(overrides.Keys.Concat(KnownTiers.Keys));
			List<(int result, List<int[]> parts, List<int> tiles)> recipes = new();
			for (int i = 0; i < Recipe.numRecipes; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (recipe == null || recipe.Disabled || recipe.createItem == null || recipe.createItem.type <= 0) continue;
				int result = recipe.createItem.type;
				if (!IsVanillaRecipe(recipe))
				{
					keep.Add(result);
					continue;
				}
				if (!Valid(result) || !SetsTier(recipe, recipe.requiredItem.Select(i => i.type).ToList())) continue;
				List<int[]> parts = new();
				foreach (Item ingredient in recipe.requiredItem)
				{
					if (ingredient == null || !Valid(ingredient.type)) continue;
					parts.Add(IngredientChoices(recipe, ingredient.type).Where(Valid).ToArray());
				}
				recipes.Add((result, parts, recipe.requiredTile.Where(t => t >= 0).Distinct().ToList()));
			}
			Dictionary<int, List<int>> placers = Stations.PlacersByTile();

			int[] best = new int[count];
			int[] bestKind = new int[count];
			int[] bestReference = new int[count];
			// The first source of the lowest tier stays (fixed order: gathered, drops, bags, shops, recipes)
			void Offer(int item, int tier, int kind, int reference)
			{
				if (tier >= best[item]) return;
				best[item] = tier;
				bestKind[item] = kind;
				bestReference[item] = reference;
			}
			bool changed = true;
			for (int pass = 0; pass < 32 && changed; pass++)
			{
				changed = false;
				System.Array.Fill(best, int.MaxValue);
				foreach ((int item, int kind, int reference, int tier) in fixedSources) Offer(item, tier, kind, reference);
				foreach ((int item, int bag, int condition) in bagSources) Offer(item, System.Math.Max(trueTiers[bag], condition), SourceBag, bag);
				foreach ((int result, List<int[]> parts, List<int> tiles) in recipes)
				{
					int tier = 0;
					foreach (int[] part in parts) tier = System.Math.Max(tier, part.Length == 0 ? Tiers.Max : part.Min(t => trueTiers[t]));
					foreach (int tile in tiles) tier = System.Math.Max(tier, RaisingStationTier(tile, placers, t => t < count ? trueTiers[t] : Tiers.Max));
					Offer(result, tier, SourceRecipe, 0);
				}
				for (int type = 1; type < count; type++)
				{
					if (best[type] == int.MaxValue || keep.Contains(type) || best[type] <= trueTiers[type]) continue;
					trueTiers[type] = Tiers.Clamp(best[type]);
					changed = true;
				}
			}
			for (int type = 0; type < count; type++)
			{
				bool found = type > 0 && best[type] != int.MaxValue;
				sourceTiers[type] = found ? Tiers.Clamp(best[type]) : -1;
				sources[type] = found ? DescribeSource(bestKind[type], bestReference[type]) : null;
			}
			int raised = Enumerable.Range(1, count - 1).Count(t => trueTiers[t] > before[t]);
			examples = string.Join(", ", new int[] { ItemID.PortalGun, ItemID.IchorArrow, ItemID.EndlessMusketPouch, ItemID.RocketIII }
				.Concat(Enumerable.Range(1, count - 1).Where(t => trueTiers[t] > before[t]).Take(6)).Distinct().Where(Valid)
				.Select(t => $"{Lang.GetItemNameValue(t)} {before[t]} > {trueTiers[t]} ({sources[t] ?? "no source"})"));
			return raised;
		}

		private static string DescribeSource(int kind, int reference) => kind switch
		{
			SourceGathered => "gathered",
			SourceDrop => "drop: " + Lang.GetNPCNameValue(reference),
			SourceBag => "bag: " + Lang.GetItemNameValue(reference),
			SourceShop => "shop: " + Lang.GetNPCNameValue(reference),
			_ => "recipe"
		};

		// The tier of an NPC as a drop source: a boss (its parts too) at its tier, any other enemy at 0 (see RaiseBySources)
		private static int NpcSourceTier(int netId)
		{
			int type = BossParts.Owner(ContentSamples.NpcsByNetId.TryGetValue(netId, out NPC sample) ? sample.type : netId);
			// Flagged as a boss, but only a guard of the dungeon before Skeletron is beaten (its stats say Moon Lord)
			if (type == NPCID.DungeonGuardian) return (int)Tier.PostSkeletron;
			return NpcTiers.HasBossTier(type) || NpcTiers.IsBoss(type) ? NpcTiers.BaseTier(type) : 0;
		}

		// The tier a drop's conditions need (vanilla conditions known to wait for a stage of the game, others ignored), -1 when
		// it never happens in a normal world (special seeds)
		public static int DropConditionTier(List<IItemDropRuleCondition> conditions)
		{
			int tier = 0;
			if (conditions == null) return 0;
			foreach (IItemDropRuleCondition condition in conditions)
			{
				int needed = condition switch
				{
					Conditions.NeverTrue or Conditions.RemixSeed or Conditions.RemixSeedEasymode or Conditions.RemixSeedHardmode or Conditions.DrunkWorldIsUp
						or Conditions.ForTheWorthyIsUp or Conditions.BeesSeed or Conditions.NoTrapsSeed or Conditions.ZenithSeedIsUp or Conditions.DontStarveIsUp
						or Conditions.TenthAnniversaryIsUp => -1,
					// Cascade: hardmode, or the Underworld once Skeletron is beaten
					Conditions.YoyoCascade => (int)Tier.PostSkeletron,
					Conditions.IsHardmode or Conditions.SoulOfNight or Conditions.SoulOfLight or Conditions.LivingFlames or Conditions.PirateMap
						or Conditions.HallowKeyCondition or Conditions.JungleKeyCondition or Conditions.CorruptKeyCondition or Conditions.CrimsonKeyCondition
						or Conditions.FrozenKeyCondition or Conditions.DesertKeyCondition or Conditions.YoyosAmarok or Conditions.YoyosHelFire => (int)Tier.Hardmode,
					Conditions.BeatAnyMechBoss or Conditions.DownedAllMechBosses or Conditions.YoyosYelets => (int)Tier.PostMechBoss,
					// The moon events: their medallions are made of Ectoplasm
					Conditions.DownedPlantera or Conditions.FirstTimeKillingPlantera or Conditions.YoyosKraken or Conditions.IsPumpkinMoon
						or Conditions.PumpkinMoonDropGatingChance or Conditions.PumpkinMoonDropGateForTrophies or Conditions.FrostMoonDropGatingChance
						or Conditions.FrostMoonDropGateForTrophies => (int)Tier.PostPlantera,
					_ => 0
				};
				if (needed < 0) return -1;
				tier = System.Math.Max(tier, needed);
			}
			return tier;
		}

		// When a town NPC can move in (the others: from the start)
		private static readonly Dictionary<int, Tier> TownNpcTiers = new()
		{
			[NPCID.GoblinTinkerer] = Tier.PostEyeOfCthulhu,
			[NPCID.Dryad] = Tier.PostEyeOfCthulhu,
			[NPCID.WitchDoctor] = Tier.PostEvilBoss,
			[NPCID.DD2Bartender] = Tier.PostEvilBoss,
			[NPCID.Mechanic] = Tier.PostSkeletron,
			[NPCID.Clothier] = Tier.PostSkeletron,
			[NPCID.Wizard] = Tier.Hardmode,
			[NPCID.Truffle] = Tier.Hardmode,
			[NPCID.Pirate] = Tier.Hardmode,
			[NPCID.SantaClaus] = Tier.Hardmode,
			[NPCID.Steampunker] = Tier.PostMechBoss,
			[NPCID.Cyborg] = Tier.PostPlantera
		};

		// Shop entries of a special seed: no source in a normal world
		private static readonly Condition[] SeedConditions =
		{
			Condition.DrunkWorld, Condition.RemixWorld, Condition.NotTheBeesWorld, Condition.ForTheWorthyWorld, Condition.TenthAnniversaryWorld,
			Condition.DontStarveWorld, Condition.NoTrapsWorld, Condition.ZenithWorld
		};

		// The tier each known shop condition waits for (the others: none)
		private static Dictionary<Condition, int> ShopConditionTiers()
		{
			Dictionary<Condition, int> map = new();
			void Set(int tier, params Condition[] conditions)
			{
				foreach (Condition c in conditions) map[c] = tier;
			}
			Set((int)Tier.PostEyeOfCthulhu, Condition.DownedKingSlime, Condition.DownedEyeOfCthulhu, Condition.DownedEarlygameBoss, Condition.DownedGoblinArmy);
			Set((int)Tier.PostEvilBoss, Condition.DownedEowOrBoc, Condition.DownedEaterOfWorlds, Condition.DownedBrainOfCthulhu, Condition.DownedOldOnesArmyAny,
				Condition.DownedOldOnesArmyT1);
			Set((int)Tier.PostSkeletron, Condition.DownedQueenBee, Condition.DownedSkeletron, Condition.DownedDeerclops);
			Set((int)Tier.Hardmode, Condition.Hardmode, Condition.Eclipse, Condition.DownedClown, Condition.DownedPirates, Condition.DownedFrostLegion);
			Set((int)Tier.PostMechBoss, Condition.DownedQueenSlime, Condition.DownedMechBossAny, Condition.DownedTwins, Condition.DownedDestroyer,
				Condition.DownedSkeletronPrime, Condition.DownedMechBossAll, Condition.DownedOldOnesArmyT2);
			Set((int)Tier.PostPlantera, Condition.DownedPlantera, Condition.DownedMourningWood, Condition.DownedPumpking, Condition.DownedEverscream,
				Condition.DownedSantaNK1, Condition.DownedIceQueen);
			Set((int)Tier.PostGolem, Condition.DownedEmpressOfLight, Condition.DownedDukeFishron, Condition.DownedGolem, Condition.DownedMartians,
				Condition.DownedOldOnesArmyT3);
			Set((int)Tier.PostCultist, Condition.DownedCultist, Condition.DownedSolarPillar, Condition.DownedVortexPillar, Condition.DownedNebulaPillar,
				Condition.DownedStardustPillar);
			Set((int)Tier.PostMoonLord, Condition.DownedMoonLord);
			return map;
		}

		// A recipe station, for the tiers raised by recipes: a thematic station (Glass Kiln, Solidifier, Living Loom...) only
		// reshapes its materials and raises nothing (0), the others as StationTier
		public static int RaisingStationTier(int tile, Dictionary<int, List<int>> placers, System.Func<int, int> tierOf) =>
			Stations.Info(tile).Kind == StationKind.Thematic ? 0 : StationTier(tile, placers, tierOf);

		// Other items the world gives from the start, besides GatheredMaterials, ores and PlacedByWorldGen (checked in
		// WorldGen.KillTile_GetItemDrops, Check3x2, the island generators and Player.ExtractinatorUse): never raised by a
		// shop, bag or drop that also sells or holds them. Not here, as they have no world source: Corrupt / Crimson Seeds
		// (Eye of Cthulhu, Dryad), Hallowed Seeds, Gold Dust (hardmode Merchant only), Beeswax (Queen Bee only)
		private static readonly int[] WorldSourced =
		{
			// Placed by world generation: Life Crystal hearts, floating islands (Rain Cloud: cloud lakes; Snow Cloud: snow
			// islands), surface Sunflowers, the Enchanted Sword shrine
			ItemID.LifeCrystal, ItemID.Cloud, ItemID.RainCloud, ItemID.SnowCloudBlock, ItemID.Sunflower, ItemID.EnchantedSword,
			// Dropped by cutting plants: grass, jungle, mushroom and ash grass seeds, blooming herbs' seeds
			ItemID.GrassSeeds, ItemID.JungleGrassSeeds, ItemID.MushroomGrassSeeds, ItemID.AshGrassSeeds, ItemID.DaybloomSeeds, ItemID.MoonglowSeeds,
			ItemID.BlinkrootSeeds, ItemID.DeathweedSeeds, ItemID.WaterleafSeeds, ItemID.FireblossomSeeds, ItemID.ShiverthornSeeds,
			// Extractinator outputs (ores and gems apart, already gathered): Sturdy Fossil (Desert Fossil), Amber Mosquito,
			// Apprentice Bait, and the moss of each moss kind
			ItemID.FossilOre, ItemID.AmberMosquito, ItemID.ApprenticeBait, ItemID.GreenMoss, ItemID.BrownMoss, ItemID.RedMoss, ItemID.BlueMoss,
			ItemID.PurpleMoss, ItemID.LavaMoss, ItemID.KryptonMoss, ItemID.XenonMoss, ItemID.ArgonMoss, ItemID.VioletMoss
		};

		// The chests world generation fills, and the tier from which each is opened (read in WorldGen.AddBuriedChest, the
		// AddGenPasses lambdas, GetNextJungleChestItem, Pyramid, IslandHouse, GrowLivingTreePassageRoom, MahoganyTreeBiome
		// and MakeDungeon): the main item of each kind of chest
		private static readonly (string kind, int tier, int[] items)[] WorldChests =
		{
			// Surface wooden chests and living trees
			("surface chest", 0, new int[] { ItemID.Spear, ItemID.Blowpipe, ItemID.WoodenBoomerang, ItemID.Aglet, ItemID.ClimbingClaws, ItemID.Umbrella,
				ItemID.CordageGuide, ItemID.WandofSparking, ItemID.Radar, ItemID.PortableStool, ItemID.CreativeWings, ItemID.LivingWoodWand, ItemID.LeafWand,
				ItemID.BabyBirdStaff, ItemID.SunflowerMinecart, ItemID.LadybugMinecart }),
			// Water chests (oceans, flooded caves)
			("water chest", 0, new int[] { ItemID.Trident, ItemID.BreathingReed, ItemID.Flipper, ItemID.WaterWalkingBoots, ItemID.FloatingTube, ItemID.SharkBait, ItemID.SandcastleBucket }),
			// Underground gold chests (and the Lava Charm of the deep caverns)
			("gold chest", 1, new int[] { ItemID.BandofRegeneration, ItemID.MagicMirror, ItemID.CloudinaBottle, ItemID.HermesBoots, ItemID.Mace, ItemID.ShoeSpikes,
				ItemID.FlareGun, ItemID.Extractinator, ItemID.LavaCharm, ItemID.MagicDagger, ItemID.EncumberingStone }),
			("ice chest", 1, new int[] { ItemID.IceBoomerang, ItemID.IceBlade, ItemID.IceSkates, ItemID.SnowballCannon, ItemID.BlizzardinaBottle, ItemID.FlurryBoots,
				ItemID.Fish, ItemID.IceMirror, ItemID.IceMachine }),
			("ivy chest", 1, new int[] { ItemID.FeralClaws, ItemID.AnkletoftheWind, ItemID.StaffofRegrowth, ItemID.Seaweed, ItemID.Boomstick, ItemID.FiberglassFishingPole,
				ItemID.FlowerBoots, ItemID.BeeMinecart, ItemID.LivingMahoganyWand, ItemID.LivingMahoganyLeafWand }),
			("sandstone chest", 1, new int[] { ItemID.DesertMinecart }),
			("mushroom chest", 1, new int[] { ItemID.ShroomMinecart, ItemID.MushroomHat, ItemID.MushroomVest, ItemID.MushroomPants }),
			("pyramid chest", 1, new int[] { ItemID.PharaohsMask, ItemID.PharaohsRobe, ItemID.SandstorminaBottle, ItemID.FlyingCarpet }),
			("sky island chest", 1, new int[] { ItemID.Starfury, ItemID.LuckyHorseshoe, ItemID.ShinyRedBalloon, ItemID.CelestialMagnet, ItemID.SkyMill, ItemID.HighPitch }),
			// Shadow Key (dungeon, after Skeletron)
			("shadow chest", 3, new int[] { ItemID.DarkLance, ItemID.Sunfury, ItemID.FlowerofFire, ItemID.Flamelash, ItemID.HellwingBow, ItemID.TreasureMagnet,
				ItemID.HellMinecart, ItemID.OrnateShadowKey }),
			// Golden Keys (dungeon, after Skeletron)
			("locked dungeon chest", 3, new int[] { ItemID.Muramasa, ItemID.CobaltShield, ItemID.AquaScepter, ItemID.BlueMoon, ItemID.Handgun, ItemID.MagicMissile,
				ItemID.Valor, ItemID.ShadowKey, ItemID.BoneWelder }),
			// Biome keys: the chests open after Plantera
			("biome dungeon chest", 6, new int[] { ItemID.PiranhaGun, ItemID.ScourgeoftheCorruptor, ItemID.VampireKnives, ItemID.RainbowGun, ItemID.StaffoftheFrostHydra,
				ItemID.StormTigerStaff })
		};

		// World-generated chests are a source too: they lower the true tier of what they hold (Trident: rarity 2, a water chest
		// item), never Tier nor the buckets, like MaterialTiers. Config overrides and the reviewed materials keep theirs.
		// lowered[t]: lowered here (its true tier may then be below Tier). Returns how many, and examples
		public static int LowerByWorldChests(int[] trueTiers, int[] tiers, bool[] fromTable, Dictionary<int, Tier> overrides, bool[] lowered, int[] sourceTiers, string[] sources, out string examples)
		{
			List<string> shown = new();
			int count = 0;
			foreach ((string kind, int tier, int[] items) in WorldChests)
			{
				foreach (int type in items)
				{
					if (type <= 0 || type >= trueTiers.Length || fromTable[type] || overrides.ContainsKey(type) || lowered[type]) continue;
					if (sourceTiers[type] < 0 || tier < sourceTiers[type])
					{
						sourceTiers[type] = tier;
						sources[type] = kind;
					}
					int now = System.Math.Max(tiers[type], trueTiers[type]);
					if (tier >= now) continue;
					trueTiers[type] = tier;
					lowered[type] = true;
					count++;
					if (shown.Count < 8 || type == ItemID.Trident) shown.Add($"{Lang.GetItemNameValue(type)} {now} > {tier} ({kind})");
				}
			}
			examples = string.Join(", ", shown);
			return count;
		}

		// Name parts of dungeon and Lihzahrd items that are no furniture placed by world generation
		private static readonly string[] NotWorldFurniture = { "Brick", "Banner", "MusicBox", "Crate", "Desert", "Fake_", "Altar", "Furnace", "PowerCell", "Key" };

		// Furniture that world generation places (dungeon and Lihzahrd furniture, Water Candle, dungeon shelves, books): found
		// with its structure, never raised by its recipe (vanilla items, by internal name)
		public static bool PlacedByWorldGen(int type)
		{
			if (type <= 0 || type >= ItemID.Count) return false;
			if (type is ItemID.WaterCandle or ItemID.Book or ItemID.DungeonShelf) return true;
			string name = ItemID.Search.GetName(type);
			if (!name.Contains("Dungeon") && !name.StartsWith("Lihzahrd")) return false;
			if (!ContentSamples.ItemsByType.TryGetValue(type, out Item sample) || sample.createTile < TileID.Dirt) return false;
			return !NotWorldFurniture.Any(name.Contains);
		}

		// World kinds a recipe may need: such a recipe is no way to make the item in every world
		private static readonly Condition[] WorldConditions =
		{
			Condition.CrimsonWorld, Condition.CorruptWorld, Condition.DrunkWorld, Condition.RemixWorld, Condition.ZenithWorld, Condition.NotDrunkWorld,
			Condition.NotRemixWorld, Condition.NotZenithWorld
		};

		// A vanilla recipe that can lower its result's tier: none tied to a kind of world, and not tModLoader's reverse recipes
		// (wall or platform back to its block: the block would take the wall's rarity)
		// ingredients: the recipe's original ones (the live ones are randomized in a world: the tests pass
		// RecipeSystem.OriginalIngredients)
		public static bool SetsTier(Recipe recipe, IReadOnlyList<int> ingredients)
		{
			if (!IsVanillaRecipe(recipe) || recipe.Conditions.Any(c => WorldConditions.Contains(c))) return false;
			if (ingredients.Count == 1 && ContentSamples.ItemsByType.TryGetValue(ingredients[0], out Item part))
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

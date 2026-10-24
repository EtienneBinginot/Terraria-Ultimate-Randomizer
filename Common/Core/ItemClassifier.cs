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
			// Pick power 65 (Nightmare / Deathbringer Pickaxe, after the evil boss): nothing waits for Skeletron (user decision)
			[ItemID.Hellstone] = Tier.PostEvilBoss,
			[ItemID.HellstoneBar] = Tier.PostEvilBoss,
			// Pick power 55: a Gold or Platinum Pickaxe mines it (water and lava anywhere)
			[ItemID.Obsidian] = Tier.PreBoss,
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
			// Giant Tortoise, any hardmode jungle (the material table agrees)
			[ItemID.TurtleShell] = Tier.Hardmode,
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
			ItemID.DontHurtCrittersBookInactive, ItemID.DontHurtNatureBookInactive, ItemID.DontHurtComboBookInactive,
			// The Lunatic Cultist has a bag sprite but never drops it
			ItemID.CultistBossBag,
			// The Shellphone's other modes (switched to by using it) and its crafting display: the Shellphone itself is enough
			ItemID.ShellphoneDummy, ItemID.ShellphoneSpawn, ItemID.ShellphoneOcean, ItemID.ShellphoneHell,
			// Part of the Jungle Temple, never obtainable in vanilla
			ItemID.LihzahrdAltar
		};

		// Never given at random (they stay replaceable when something gives them): the items of pure utility mods (tModLoader's
		// own start bag..., Recipe Browser's), other mods' creative items (Magic Storage's Creative Storage Unit holds every
		// item) and unobtainable vanilla items. Storage mods' items are allowed like any other (user decision: a random Storage
		// Heart, unit or upgrade is fine; the starter kit still gives Magic Storage's). Internal items: IsInternal
		public static bool IsBanned(Item item)
		{
			if (item == null) return false;
			if (BannedVanilla.Contains(item.type)) return true;
			string mod = item.ModItem?.Mod?.Name;
			if (mod == null) return false;
			if (mod is "ModLoader" or "RecipeBrowser") return true;
			return (item.ModItem.Name ?? "").StartsWith("Creative", System.StringComparison.Ordinal);
		}

		// Another mod's internal item (displays, placeholders, data holders: Magic Storage's storage cores, its shimmer, error
		// and config displays): nothing makes, drops, holds or sells it (sourced: the items with a source), and it can't be
		// researched or its mod adds it by hand (Autoload(false): instances made at load). Banned like IsBanned. Not a critter,
		// block or ore (caught or found in the world)
		public static bool IsInternal(Item item, ISet<int> sourced) =>
			item?.ModItem != null && !sourced.Contains(item.type) && (item.ResearchUnlockCount == 0 || !AutoloadAttribute.GetValue(item.ModItem.GetType()).Value) && item.makeNPC <= 0 && item.createWall <= WallID.None
			&& !(item.createTile >= TileID.Dirt && (TileID.Sets.Ore[item.createTile] || !Main.tileFrameImportant[item.createTile]));

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
			// Mirrors, phones and conches are sorted with the summons but summon nothing (Protection.SortedAsSummon)
			if (ItemID.Sets.SortingPriorityBossSpawns[type] >= 0 && !Protection.SortedAsSummon(type)) return ItemCategory.BossSummon;
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
		// Tinkerer's Workshop. Only lowers; KnownTiers and the config overrides keep theirs; a modded recipe counts only for a modded item
		// (IsTierRecipe). Run when recipes are set up (ItemPool.Build in PostSetupRecipes), before RecipeSystem
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
		// or sells (lootSources), without a modded recipe of a vanilla item (IsTierRecipe), and not set by KnownTiers or the config; never lowers. A thematic
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
				if (recipe == null || Features.RecipeSystem.VanillaDisabled(i) || recipe.createItem == null || recipe.createItem.type <= 0) continue;
				int result = recipe.createItem.type;
				if (!IsTierRecipe(recipe))
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
		private const int SourceGathered = 0, SourceDrop = 1, SourceBag = 2, SourceShop = 3, SourceRecipe = 4, SourceGlobal = 5;

		// The true tier of every item from its vanilla sources, the earliest of:
		// - NPC drops (LootSnapshot.Npcs): a boss (its parts and minions too) at its tier (NpcTiers.BaseTier: the boss table), an
		//   enemy that only spawns later at its spawn tier (NpcTiers.SpawnTier: hardmode, the Temple, events...), any other
		//   at 0: its BaseTier is a guess from its stats (Doctor Bones, Tim or the Dungeon Guardian would look mid or late game),
		//   and EffectiveTier follows the world's progression and the config, unknown at load; then raised by the drop's known
		//   conditions (hardmode, mechanical bosses, Plantera, the moon events...); a drop that needs a special seed is none;
		// - global drops (any enemy: Pirate Map, biome keys, souls...) at their conditions' tier;
		// - bags and crates (LootSnapshot.Items): a boss bag at its boss's tier, any other at its own true tier (a lock box no
		//   earlier than its key, a hardmode or dungeon crate no earlier than where it is caught), + conditions;
		// - shops (Obtainability.ShopEntries): from when the NPC moves in (Wizard: hardmode, Cyborg: Plantera...), + the
		//   entry's conditions; an entry of a special seed is none;
		// - vanilla recipes, as in RaiseByRecipes (thematic stations raise nothing);
		// - gathered in the world (ores, gems, herbs, Fallen Star, blocks, WorldSourced: Life Crystal, clouds, seeds of
		//   plants, Extractinator outputs) or placed by world generation (dungeon furniture): their own tier, so these are
		//   never raised.
		// The true tier is raised to that source tier, never lowered; items without any source, set by KnownTiers or the
		// config, or a vanilla item with a modded recipe (IsTierRecipe) keep theirs. Repeated until nothing changes (a bag or
		// an ingredient raised raises what comes from it). sourceTiers / sources (filled, per item): the earliest source and
		// its description, -1 / null without one. reliableTiers (filled, per item): the earliest reliable source (ReliableDrop: a
		// boss drop or boss bag item, a fair enemy drop; a shop entry whose conditions are all known), int.MaxValue without one:
		// it may lower the true tier below the rarity's (ItemPool.Build). Load time (ItemPool.Build), the same on every machine:
		// the snapshots, shops and recipes are the vanilla ones, read in a fixed order. Returns how many items it raised, and
		// examples
		public static int RaiseBySources(int[] trueTiers, Dictionary<int, Tier> overrides, int[] sourceTiers, string[] sources, int[] reliableTiers, out string examples)
		{
			int count = trueTiers.Length;
			int[] before = (int[])trueTiers.Clone();
			bool Valid(int t) => t > 0 && t < count;
			List<(int item, int kind, int reference, int tier)> fixedSources = new();
			System.Array.Fill(reliableTiers, int.MaxValue);
			void Reliable(int item, int tier) => reliableTiers[item] = System.Math.Min(reliableTiers[item], tier);

			// Gathered or placed by world generation: their own tier (the Jungle Temple's furniture: once the temple opens)
			HashSet<int> gathered = Features.Findability.GatheredMaterials.Concat(WorldSourced).Where(Valid).ToHashSet();
			for (int type = 1; type < count; type++)
			{
				if (!ContentSamples.ItemsByType.TryGetValue(type, out Item sample) || sample == null) continue;
				if (sample.createTile >= TileID.Dirt && sample.createTile < TileID.Sets.Ore.Length && TileID.Sets.Ore[sample.createTile] || PlacedByWorldGen(type)) gathered.Add(type);
			}
			foreach (int item in gathered.OrderBy(t => t)) fixedSources.Add((item, SourceGathered, 0, TempleFurniture(item) ? System.Math.Max(before[item], (int)Tier.PostPlantera) : before[item]));

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
					if (ReliableDrop(netId, drop.dropRate, drop.itemId)) Reliable(drop.itemId, tier);
					if (ItemID.Sets.BossBag[drop.itemId] && npcTier > 0) bossBags[drop.itemId] = bossBags.TryGetValue(drop.itemId, out int other) ? System.Math.Min(other, tier) : tier;
				}
			}
			// Global drops (any enemy): at their conditions' tier; rare and anywhere, so no reliable source
			foreach (DropRateInfo drop in LootSnapshot.GlobalDrops())
			{
				int condition = DropConditionTier(drop.conditions);
				if (condition >= 0 && Valid(drop.itemId)) fixedSources.Add((drop.itemId, SourceGlobal, 0, condition));
			}

			// Bags: a boss bag at its boss's tier, any other at its own (current) true tier
			List<(int item, int bag, int condition)> bagSources = new();
			foreach (int bag in LootSnapshot.Items.Keys.OrderBy(k => k))
			{
				foreach (DropRateInfo drop in LootSnapshot.Items[bag])
				{
					int condition = DropConditionTier(drop.conditions);
					if (condition < 0 || !Valid(drop.itemId)) continue;
					if (bossBags.TryGetValue(bag, out int bossTier))
					{
						fixedSources.Add((drop.itemId, SourceBag, bag, System.Math.Max(bossTier, condition)));
						if (drop.dropRate >= ReliableBossChance) Reliable(drop.itemId, System.Math.Max(bossTier, condition));
					}
					else if (Valid(bag)) bagSources.Add((drop.itemId, bag, condition));
				}
			}

			// Shops; an entry is reliable when the NPC's arrival and every condition are known
			Dictionary<Condition, int> shopConditions = ShopConditionTiers();
			foreach ((int npc, int item, Condition[] conditions) in Obtainability.ShopEntries)
			{
				if (!Valid(item) || conditions.Any(c => SeedConditions.Contains(c))) continue;
				int tier = TownNpcTiers.TryGetValue(npc, out Tier arrival) ? (int)arrival : 0;
				foreach (Condition c in conditions) tier = System.Math.Max(tier, shopConditions.TryGetValue(c, out int t) ? t : 0);
				fixedSources.Add((item, SourceShop, npc, tier));
				if (!UnknownArrival.Contains(npc) && conditions.All(shopConditions.ContainsKey)) Reliable(item, tier);
			}

			// Vanilla recipes and modded ones of modded items (IsTierRecipe: every one of them is a source); another modded recipe
			// makes its (vanilla) result keep its tier
			HashSet<int> keep = new(overrides.Keys.Concat(KnownTiers.Keys));
			List<(int result, List<int[]> parts, List<int> tiles)> recipes = new();
			for (int i = 0; i < Recipe.numRecipes; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (recipe == null || Features.RecipeSystem.VanillaDisabled(i) || recipe.createItem == null || recipe.createItem.type <= 0) continue;
				int result = recipe.createItem.type;
				if (!IsTierRecipe(recipe))
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
				foreach ((int item, int bag, int condition) in bagSources) Offer(item, System.Math.Max(System.Math.Max(trueTiers[bag], condition), BagAccessTier(bag, trueTiers)), SourceBag, bag);
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
			SourceGlobal => "drop: any enemy",
			_ => "recipe"
		};

		// The tier of an NPC as a drop source: a boss (its parts and minions too: the Creeper's Tissue Samples are the Brain's) at
		// its tier, an enemy that only spawns later at its spawn tier (NpcTiers.SpawnTier), another mod's enemy at its estimated
		// tier (NpcTiers.ModdedTier: nothing else says when it spawns), any other enemy at 0 (see RaiseBySources)
		public static int NpcSourceTier(int netId)
		{
			int type = BossParts.Leader(ContentSamples.NpcsByNetId.TryGetValue(netId, out NPC sample) ? sample.type : netId);
			// Flagged as a boss, but only a guard of the dungeon before Skeletron is beaten (its stats say Moon Lord)
			if (type == NPCID.DungeonGuardian) return (int)Tier.PostSkeletron;
			int spawn = NpcTiers.SpawnTier(type);
			if (spawn >= 0) return spawn;
			return IsBossSource(type) || type >= NPCID.Count ? NpcTiers.BaseTier(type) : 0;
		}

		private static bool IsBossSource(int type) => type == NPCID.DungeonGuardian || NpcTiers.HasBossTier(type) || NpcTiers.IsBoss(type);

		// Smallest chances of a reliable drop (RaiseBySources): a boss's (its bag's) at 1 in 50 (Binoculars: the Eye, 1 in 40),
		// an early enemy's at 1 in 100 (Jellyfish Necklace), an enemy of a later stage's at 1 in 500 (its spawn is the gate:
		// Plantera's dungeon enemies drop their weapons at 1 in 150 to 450)
		private const float ReliableBossChance = 0.0199f, ReliableEnemyChance = 0.0099f, ReliableLateEnemyChance = 0.0019f;

		// A drop that may lower its item's true tier to its source's: a boss's (parts, minions) or an enemy's of known spawn tier
		// with a fair chance, never a rare enemy (Nymph, Tim...) nor an unlisted enemy whose stats look like hardmode (its
		// spawn tier is unknown: it stays a source, not a reliable one), nor another mod's enemy (its tier is a guess; a modded
		// boss's estimate is trusted, for its mod's items only: a vanilla item dropped by a modded boss keeps its vanilla tier)
		private static bool ReliableDrop(int netId, float chance, int item)
		{
			if (!ContentSamples.NpcsByNetId.TryGetValue(netId, out NPC sample)) return false;
			int type = BossParts.Leader(sample.type);
			if ((sample.type >= NPCID.Count || type >= NPCID.Count) && item < ItemID.Count) return false;
			if (IsBossSource(type)) return chance >= ReliableBossChance;
			if (type >= NPCID.Count) return false;
			if (sample.rarity > 0 || NPCID.Sets.CountsAsCritter[sample.type] || sample.friendly) return false;
			int spawn = NpcTiers.SpawnTier(type);
			if (spawn >= (int)Tier.Hardmode) return chance >= ReliableLateEnemyChance;
			if (spawn < 0 && NpcTiers.BaseTier(type) >= (int)Tier.Hardmode) return false;
			return chance >= ReliableEnemyChance;
		}

		// A bag that can only be opened, or caught, from a stage on: the Golden Lock Box needs a Golden Key, the Obsidian Lock
		// Box a Shadow Key (post-Skeletron dungeon keys), hardmode crates wait for hardmode, the Dungeon Crate for Skeletron
		// (1.4.4: no dungeon fishing loot before him)
		private static int BagAccessTier(int bag, int[] trueTiers)
		{
			if (bag == ItemID.LockBox) return trueTiers[ItemID.GoldenKey];
			if (bag == ItemID.ObsidianLockbox) return trueTiers[ItemID.ShadowKey];
			if (ItemID.Sets.IsFishingCrateHardmode[bag] || bag == ItemID.HallowedFishingCrate) return (int)Tier.Hardmode;
			if (bag == ItemID.DungeonFishingCrate) return (int)Tier.PostSkeletron;
			return 0;
		}

		// Placed by world generation inside the Jungle Temple, which opens after Plantera (its Temple Key)
		private static bool TempleFurniture(int type) => PlacedByWorldGen(type) && ItemID.Search.GetName(type).StartsWith("Lihzahrd");

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
						or Conditions.FrozenKeyCondition or Conditions.DesertKeyCondition or Conditions.YoyosAmarok or Conditions.YoyosHelFire
						// The mechanical summons' global drop: hardmode, before their bosses
						or Conditions.MechanicalBossesDummyCondition => (int)Tier.Hardmode,
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

		// Town NPCs whose arrival waits for something the table does not know (the Princess: every other NPC), or who only visit:
		// their shop entries are sources, never reliable ones
		private static readonly HashSet<int> UnknownArrival = new() { NPCID.Princess, NPCID.SkeletonMerchant, NPCID.TravellingMerchant };

		// Shop entries of a special seed: no source in a normal world
		private static readonly Condition[] SeedConditions =
		{
			Condition.DrunkWorld, Condition.RemixWorld, Condition.NotTheBeesWorld, Condition.ForTheWorthyWorld, Condition.TenthAnniversaryWorld,
			Condition.DontStarveWorld, Condition.NoTrapsWorld, Condition.ZenithWorld
		};

		private static Dictionary<Condition, int> shopConditionTiers;

		// The tier a shop condition waits for (ShopPermutation); 0 for the others (moon phase, biome, time...)
		public static int ShopConditionTier(Condition condition)
		{
			shopConditionTiers ??= ShopConditionTiers();
			return condition != null && shopConditionTiers.TryGetValue(condition, out int tier) ? tier : 0;
		}

		// When a town NPC moves in, in vanilla (0: from the start, or unknown)
		public static int VanillaArrivalTier(int npcType) => TownNpcTiers.TryGetValue(npcType, out Tier tier) ? (int)tier : 0;

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
			Set((int)Tier.PostEvilBoss, Condition.DownedQueenBee, Condition.DownedDeerclops);
			Set((int)Tier.PostSkeletron, Condition.DownedSkeletron);
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
				ItemID.FlowerBoots, ItemID.BeeMinecart, ItemID.LivingMahoganyWand, ItemID.LivingMahoganyLeafWand, ItemID.HoneyDispenser }),
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
		// item), like MaterialTiers (the bucket follows: ItemPool.Build fills it from the final true tier). Config overrides and
		// the reviewed materials keep theirs. exact[t]: trueTiers[t] is final even below tiers[t] (lowered by a reliable
		// source). lowered[t]: lowered here (its true tier may then be below its classification tier). Returns how many, and examples
		public static int LowerByWorldChests(int[] trueTiers, int[] tiers, bool[] fromTable, bool[] exact, Dictionary<int, Tier> overrides, bool[] lowered, int[] sourceTiers, string[] sources, out string examples)
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
					int now = exact[type] ? trueTiers[type] : System.Math.Max(tiers[type], trueTiers[type]);
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

		// Hand corrections of true tiers from the tier audits (terraria.wiki.gg), for items the material table does not list:
		// set exactly like the table's (lower too), before the recipes settle the crafted items again; the bucket follows (the
		// final true tier). Config overrides still win
		public static readonly Dictionary<int, Tier> TrueTierFixes = new()
		{
			// Dropped by the Lunatic Cultist (Stations says 8 too); rarity 9
			[ItemID.LunarCraftingStation] = Tier.PostCultist,
			// The Zoologist sells it at a 100% Bestiary, which needs the Moon Lord (the shop condition is not read)
			[ItemID.TeleportationPylonVictory] = Tier.PostMoonLord,
			// Mechanical Eye, Worm and Skull at a hardmode anvil
			[ItemID.MechdusaSummon] = Tier.Hardmode,
			// Living Trees and a shimmered Loom (1.4.4), not only the Steampunker
			[ItemID.LivingLoom] = Tier.PreBoss,
			// Underground hearts from the start; the Life Fruit grows after a mechanical boss
			[ItemID.LifeCrystal] = Tier.PreBoss,
			[ItemID.LifeFruit] = Tier.PostMechBoss,
			// The Jungle Temple, opened by Plantera's Temple Key: its enemies, chests and furnace
			[ItemID.LihzahrdPowerCell] = Tier.PostPlantera,
			[ItemID.LizardEgg] = Tier.PostPlantera,
			[ItemID.LihzahrdFurnace] = Tier.PostPlantera,
			[ItemID.SolarTablet] = Tier.PostPlantera,
			// Prismatic Lacewing: the Hallow at night after Plantera; Truffle Worm: hardmode glowing mushrooms; Gelatin Crystal:
			// the underground Hallow in hardmode; Abigail's Flower: near graves whatever the stage
			[ItemID.EmpressButterfly] = Tier.PostPlantera,
			[ItemID.TruffleWorm] = Tier.Hardmode,
			[ItemID.QueenSlimeCrystal] = Tier.Hardmode,
			[ItemID.AbigailsFlower] = Tier.PreBoss,
			// Hardmode global drops: the Pirate Map (usable before, so it would start a pre-hardmode invasion) and the biome keys
			// (only their chests wait for Plantera)
			[ItemID.PirateMap] = Tier.Hardmode,
			[ItemID.JungleKey] = Tier.Hardmode,
			[ItemID.CorruptionKey] = Tier.Hardmode,
			[ItemID.CrimsonKey] = Tier.Hardmode,
			[ItemID.HallowedKey] = Tier.Hardmode,
			[ItemID.FrozenKey] = Tier.Hardmode,
			[ItemID.DungeonDesertKey] = Tier.Hardmode,
			// The Pirate Invasion's rare drops (rarity 5 and 6), any time in hardmode
			[ItemID.CoinGun] = Tier.Hardmode,
			[ItemID.LuckyCoin] = Tier.Hardmode,
			[ItemID.DiscountCard] = Tier.Hardmode,
			[ItemID.GoldRing] = Tier.Hardmode,
			[ItemID.PirateStaff] = Tier.Hardmode,
			// Dungeon only (the Golden Lock Box needs a Golden Key; 1.4.4: no dungeon fishing loot before Skeletron)
			[ItemID.LockBox] = Tier.PostSkeletron,
			[ItemID.DungeonFishingCrate] = Tier.PostSkeletron,
			[ItemID.AlchemyTable] = Tier.PostSkeletron,
			// Made with Pearlstone (hardmode)
			[ItemID.HallowedTorch] = Tier.Hardmode,
			[ItemID.HallowedCampfire] = Tier.Hardmode,
			// Fishing, Angler rewards and the Traveling Merchant, from the start (no source is read for them)
			[ItemID.ReaverShark] = Tier.PreBoss,
			[ItemID.Sextant] = Tier.PreBoss,
			[ItemID.WeatherRadio] = Tier.PreBoss,
			[ItemID.FishermansGuide] = Tier.PreBoss,
			[ItemID.HighTestFishingLine] = Tier.PreBoss,
			[ItemID.AnglerEarring] = Tier.PreBoss,
			[ItemID.TackleBox] = Tier.PreBoss,
			[ItemID.FrogLeg] = Tier.PreBoss,
			[ItemID.BalloonPufferfish] = Tier.PreBoss,
			[ItemID.LifeformAnalyzer] = Tier.PreBoss,
			[ItemID.Stopwatch] = Tier.PreBoss,
			[ItemID.DPSMeter] = Tier.PreBoss,
			// The Skeleton Merchant, underground from the start (moon phase shop conditions are not read)
			[ItemID.MagicLantern] = Tier.PreBoss,
			// Hornets of the underground Jungle (1 %): a reliable source says 0, but the jungle is a step after the Eye (audit 1.6.4)
			[ItemID.Bezoar] = Tier.PostEyeOfCthulhu,
			// Pedguin's set: 1 in 150 from Corrupt and Vicious Penguins (pre-hardmode snow), too rare to count as reliable; its
			// rarity (9) would say post-Cultist
			[ItemID.PedguinHat] = Tier.PreBoss,
			[ItemID.PedguinShirt] = Tier.PreBoss,
			[ItemID.PedguinPants] = Tier.PreBoss,
			// Its recipe (Cell Phone and both conches at a Tinkerer's Workshop) makes the banned ShellphoneDummy, so no source is
			// read: the Demon Conch's tier (lava fishing), not its rarity's 7
			[ItemID.Shellphone] = Tier.Hardmode
		};

		// Pick power gates that hold whatever the stage, their ore being in the world from the start: Hellstone (65),
		// Chlorophyte (200), the Temple's Lihzahrd Bricks (210). Hardmode ores wait for hardmode itself, Meteorite and the evil
		// ores for their event or boss, not for a pickaxe
		public static readonly (int pick, int ore)[] PickGates = { (65, ItemID.Hellstone), (200, ItemID.ChlorophyteOre), (210, ItemID.LihzahrdBrick) };

		// The lowest true tier mining allows: a pickaxe or drill no earlier than the gated ore its power reaches (Nightmare
		// Pickaxe, Molten Pickaxe: Hellstone; Pickaxe Axe, Spectre Pickaxe: Chlorophyte), a block no earlier than a pickaxe that
		// mines it (Ebonstone, Crimstone, Hellforge: 65). 0 without any
		public static int MiningFloor(int type, int[] trueTiers)
		{
			if (!ContentSamples.ItemsByType.TryGetValue(type, out Item sample) || sample == null) return 0;
			int power = System.Math.Max(sample.pick, Features.RecipePlanner.PickToMine(type));
			int floor = 0;
			foreach ((int pick, int ore) in PickGates)
			{
				if (power >= pick && ore < trueTiers.Length) floor = System.Math.Max(floor, trueTiers[ore]);
			}
			return floor;
		}

		// The recipes that set tiers (SetsTier, as LowerByRecipes), and the vanilla results some modded recipe makes
		private static List<(int result, List<int[]> parts, List<int> tiles)> TierRecipes(int count, out HashSet<int> moddedResults)
		{
			moddedResults = new HashSet<int>();
			List<(int result, List<int[]> parts, List<int> tiles)> recipes = new();
			for (int i = 0; i < Recipe.numRecipes; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (recipe == null || Features.RecipeSystem.VanillaDisabled(i) || recipe.createItem == null || recipe.createItem.type <= 0) continue;
				int result = recipe.createItem.type;
				if (!IsTierRecipe(recipe))
				{
					moddedResults.Add(result);
					continue;
				}
				if (result >= count || !SetsTier(recipe, recipe.requiredItem.Select(i => i.type).ToList())) continue;
				List<int[]> parts = new();
				foreach (Item ingredient in recipe.requiredItem)
				{
					if (ingredient == null || ingredient.type <= 0 || ingredient.type >= count) continue;
					parts.Add(IngredientChoices(recipe, ingredient.type).Where(t => t > 0 && t < count).ToArray());
				}
				recipes.Add((result, parts, recipe.requiredTile.Where(t => t >= 0).Distinct().ToList()));
			}
			return recipes;
		}

		// Items whose true tier the recipes may raise in SettleByRecipes: no other source (as RaiseByRecipes: no drop, bag nor
		// shop, nothing the world places or gives, no world chest), no modded recipe of a vanilla item
		private static bool RaisableByRecipes(int type, HashSet<int> lootSources, bool[] fromChest, HashSet<int> moddedResults) =>
			!lootSources.Contains(type) && !fromChest[type] && !moddedResults.Contains(type) && !PlacedByWorldGen(type) && !WorldSourced.Contains(type)
			&& !Features.Findability.GatheredMaterials.Contains(type);

		// Once the hand-set tiers (material table, TrueTierFixes, KnownTiers), the reliable sources and the world chests are in:
		// the true tiers of crafted items again, from the final true tiers of their ingredients (Bone Wings follow the table's
		// Bone Feather, a Gold Crown its tier-0 gems, the Molten set Hellstone, Lava Waders the chest-lowered Water Walking
		// Boots). Each item: lowered to its earliest vanilla recipe (as LowerByRecipes, on true tiers), else raised to it when it
		// has no other source (as RaiseByRecipes); never below its mining floor (MiningFloor); fixed tiers stay (fixedTier).
		// trueTiers holds the effective true tiers and changes in place; lowered[t]: lowered here. Returns how many changed
		public static int SettleByRecipes(int[] trueTiers, bool[] fixedTier, int[] floors, HashSet<int> lootSources, bool[] fromChest, bool[] lowered, out string examples)
		{
			int count = trueTiers.Length;
			List<(int result, List<int[]> parts, List<int> tiles)> recipes = TierRecipes(count, out HashSet<int> moddedResults);
			Dictionary<int, List<int>> placers = Stations.PlacersByTile();
			int[] before = (int[])trueTiers.Clone();
			int TierOf(int t) => t < count ? trueTiers[t] : Tiers.Max;
			bool changed = true;
			for (int pass = 0; pass < 32 && changed; pass++)
			{
				changed = false;
				// The earliest recipe of each result with this pass's tiers: by any station (to lower), by the stations that raise
				Dictionary<int, (int low, int high)> earliest = new();
				foreach ((int result, List<int[]> parts, List<int> tiles) in recipes)
				{
					if (fixedTier[result] || moddedResults.Contains(result)) continue;
					int ingredients = 0;
					foreach (int[] part in parts) ingredients = System.Math.Max(ingredients, part.Length == 0 ? Tiers.Max : part.Min(TierOf));
					int low = ingredients, high = ingredients;
					foreach (int tile in tiles)
					{
						low = System.Math.Max(low, StationTier(tile, placers, TierOf));
						high = System.Math.Max(high, RaisingStationTier(tile, placers, TierOf));
					}
					earliest[result] = earliest.TryGetValue(result, out (int low, int high) other) ? (System.Math.Min(other.low, low), System.Math.Min(other.high, high)) : (low, high);
				}
				foreach ((int result, (int low, int high)) in earliest)
				{
					int down = System.Math.Max(low, floors[result]);
					if (down < trueTiers[result])
					{
						trueTiers[result] = down;
						lowered[result] = true;
						changed = true;
						continue;
					}
					int up = System.Math.Max(high, floors[result]);
					if (up > trueTiers[result] && RaisableByRecipes(result, lootSources, fromChest, moddedResults))
					{
						trueTiers[result] = Tiers.Clamp(up);
						changed = true;
					}
				}
			}
			int settled = Enumerable.Range(1, count - 1).Count(t => trueTiers[t] != before[t]);
			examples = string.Join(", ", new int[] { ItemID.BoneWings, ItemID.FrozenWings, ItemID.GoldCrown, ItemID.MoltenPickaxe, ItemID.LavaWaders, ItemID.TerraBlade, ItemID.CrimsonHelmet }
				.Where(t => t < count).Select(t => $"{Lang.GetItemNameValue(t)} {before[t]} > {trueTiers[t]}"));
			return settled;
		}

		// Hard progression gates (user decision): vanilla makes them absolute (a 200 pickaxe, Plantera, Golem, the Moon Lord, the
		// three mechanical bosses' souls), so gated rolls, findability and recipe locks give them no TierAllowance (ItemPool)
		public static readonly int[] HardGateMaterials =
		{
			ItemID.LunarOre, ItemID.LunarBar, ItemID.ChlorophyteOre, ItemID.ChlorophyteBar, ItemID.Ectoplasm, ItemID.SpectreBar, ItemID.ShroomiteBar,
			ItemID.BeetleHusk, ItemID.HallowedBar, ItemID.SoulofMight, ItemID.SoulofSight, ItemID.SoulofFright
		};

		// The hard gates: those materials, and every item only made from them (each vanilla recipe needs one at least as late
		// as the item, and no drop, bag, shop or world source gives it: Chlorophyte and lunar gear, Hallowed armor, Drax)
		public static bool[] HardGates(int[] trueTiers, HashSet<int> lootSources, bool[] fromChest)
		{
			int count = trueTiers.Length;
			bool[] gate = new bool[count];
			foreach (int item in HardGateMaterials)
			{
				if (item < count) gate[item] = true;
			}
			List<(int result, List<int[]> parts, List<int> tiles)> recipes = TierRecipes(count, out HashSet<int> moddedResults);
			Dictionary<int, List<List<int[]>>> byResult = new();
			foreach ((int result, List<int[]> parts, _) in recipes)
			{
				if (!byResult.TryGetValue(result, out List<List<int[]>> list)) byResult[result] = list = new List<List<int[]>>();
				list.Add(parts);
			}
			bool changed = true;
			for (int pass = 0; pass < 32 && changed; pass++)
			{
				changed = false;
				foreach ((int result, List<List<int[]>> made) in byResult)
				{
					if (gate[result] || !RaisableByRecipes(result, lootSources, fromChest, moddedResults)) continue;
					bool Gated(List<int[]> parts) => parts.Any(part => part.Length > 0 && part.All(t => gate[t] && trueTiers[t] >= trueTiers[result]));
					if (!made.All(Gated)) continue;
					gate[result] = true;
					changed = true;
				}
			}
			return gate;
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
		public static bool SetsTier(Recipe recipe, IReadOnlyList<int> ingredients) => SetsTier(recipe, ingredients, recipe?.createItem?.type ?? 0);

		// The same with the recipe's original result (the live one is randomized in a world: the tests pass RecipeSystem.OriginalResult)
		public static bool SetsTier(Recipe recipe, IReadOnlyList<int> ingredients, int result)
		{
			if (!IsTierRecipe(recipe, result) || recipe.Conditions.Any(c => WorldConditions.Contains(c))) return false;
			if (ingredients.Count == 1 && ContentSamples.ItemsByType.TryGetValue(ingredients[0], out Item part))
			{
				if (part.createWall > 0 || part.createTile >= TileID.Dirt && TileID.Sets.Platforms[part.createTile]) return false;
			}
			return true;
		}

		// A recipe of vanilla Terraria (tModLoader's own count as vanilla, as in RecipeSystem), enabled, with a result. Enabled as
		// set up, not live: twin copies are switched off while the plan is applied
		public static bool IsVanillaRecipe(Recipe recipe) => recipe != null && !Features.RecipeSystem.VanillaDisabled(recipe.RecipeIndex) && (recipe.Mod == null || recipe.Mod.Name == "ModLoader")
			&& recipe.createItem != null && recipe.createItem.type > 0;

		// A recipe whose ingredients and stations set its result's tier: a vanilla one, or another mod's making a modded item (a
		// mod's items follow their ingredients: Magic Storage's upgrades end up at their bars' tiers). A modded recipe of a
		// vanilla item is a shortcut the tiers ignore (that item keeps what vanilla gives it). Read at load, whatever
		// RandomizeModdedRecipes says: the tiers never depend on a world's settings. Best effort: a modded recipe condition
		// (a modded boss beaten...) is not read
		public static bool IsTierRecipe(Recipe recipe) => IsTierRecipe(recipe, recipe?.createItem?.type ?? 0);

		public static bool IsTierRecipe(Recipe recipe, int result) => recipe != null && !Features.RecipeSystem.VanillaDisabled(recipe.RecipeIndex) && result > 0
			&& (recipe.Mod == null || recipe.Mod.Name == "ModLoader" || result >= ItemID.Count);

		// The items an ingredient of a recipe accepts: every item of its recipe group, else itself
		public static IEnumerable<int> IngredientChoices(Recipe recipe, int ingredient) => IngredientChoices(recipe.acceptedGroups, ingredient);

		// The same with a recipe's groups (the original ones once the live recipes are randomized: RecipeSystem.OriginalGroups)
		public static IEnumerable<int> IngredientChoices(IEnumerable<int> groups, int ingredient)
		{
			RecipeGroup group = groups.Select(g => RecipeGroup.recipeGroups.TryGetValue(g, out RecipeGroup found) ? found : null)
				.FirstOrDefault(g => g != null && g.ContainsItem(ingredient));
			return group != null ? group.ValidItems.Append(ingredient).Distinct() : new[] { ingredient };
		}

		// The tier from which a station is had, for the tiers from recipes: a progression station at its tier, a basic one at
		// 0, any other (and any modded one: its tier is not known yet) at the earliest item placing it (never, without one;
		// another mod's tile that no item places is part of its world, as the Demon Altar)
		public static int StationTier(int tile, Dictionary<int, List<int>> placers, System.Func<int, int> tierOf)
		{
			Stations.StationInfo info = Stations.Info(tile);
			if (info.Kind == StationKind.Progression && !info.Modded) return info.Tier;
			if (info.Kind == StationKind.Basic || info.World && info.Modded) return 0;
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

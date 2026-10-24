using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Tests
{
	// Measurements for the recipe redesign (key ingredients of each result's tier). Always passes: the numbers are in
	// client.log, every line starting with "TierCoherence:". Read-only: the live plans and caches are only read
	public static class CoherenceTests
	{
		public static void Register()
		{
			TestRunner.Register("TierCoherence", TierCoherence);
			TestRunner.Register("TierAudit", TierAudit);
			TestRunner.Register("ItemTierDump", ItemTierDump);
			TestRunner.Register("WorldChestTrueTiers", WorldChestTrueTiers);
			TestRunner.Register("TierFloorsHold", TierFloorsHold);
			TestRunner.Register("TiersConsistent", TiersConsistent);
		}

		// The bucket of every item is its final true tier (the plans roll an item at the tier it is really found), and the
		// classification errors of the 1.6.4 audit stay fixed: items without a vanilla recipe or made of tier-0 types at a late
		// station follow their real source (Venom Bullets, lunar bricks), pets, mounts, trophies and masks their boss, and the
		// mirrors, phones and conches are no boss summons. Config overrides are left out
		private static void TiersConsistent()
		{
			if (!ItemPool.Ready) Check.Skip("items not classified yet");
			HashSet<int> overridden = ProgressionConfig.Instance.TierOverrides.Where(o => o?.Item != null && !o.Item.IsUnloaded).Select(o => o.Item.Type).ToHashSet();
			bool Usable(int item) => ItemPool.IsValid(item) && !ItemPool.Info[item].Excluded && !overridden.Contains(item);
			List<string> failures = new();
			for (int type = 1; type < ItemPool.Info.Length; type++)
			{
				ItemInfo info = ItemPool.Info[type];
				if (!info.Excluded && info.Tier != info.TrueTier && failures.Count < 20) failures.Add($"{Name(type)}: tier {info.Tier}, true tier {info.TrueTier}");
			}
			(int item, Tier tier)[] exactly =
			{
				(ItemID.SuperHealingPotion, Tier.PostCultist), (ItemID.RocketIII, Tier.PostPlantera), (ItemID.RocketIV, Tier.PostPlantera),
				(ItemID.Bezoar, Tier.PostEyeOfCthulhu), (ItemID.EmpressFlightBooster, Tier.PostGolem), (ItemID.StyngerBolt, Tier.PostGolem),
				(ItemID.Nail, Tier.PostPlantera), (ItemID.PortalGun, Tier.PostMoonLord), (ItemID.GravityGlobe, Tier.PostMoonLord),
				(ItemID.StaticHook, Tier.PostMechBoss), (ItemID.PogoStick, Tier.PreBoss), (ItemID.TruffleWorm, Tier.Hardmode),
				(ItemID.LihzahrdPowerCell, Tier.PostPlantera), (ItemID.LihzahrdFurnace, Tier.PostPlantera), (ItemID.Autohammer, Tier.PostPlantera),
				(ItemID.VialofVenom, Tier.PostPlantera), (ItemID.ShadowKey, Tier.PostSkeletron), (ItemID.Hellforge, Tier.PostEvilBoss),
				(ItemID.Wire, Tier.PostSkeletron), (ItemID.Actuator, Tier.PostSkeletron), (ItemID.DrillContainmentUnit, Tier.PostMoonLord),
				(ItemID.TerraBlade, Tier.PostPlantera), (ItemID.ChippysCouch, Tier.PostSkeletron), (ItemID.PedguinHat, Tier.PreBoss),
				(ItemID.PedguinShirt, Tier.PreBoss), (ItemID.PedguinPants, Tier.PreBoss), (ItemID.DemonConch, Tier.PostEvilBoss),
				// Pets, mounts, trophies, relics and masks: their boss's tier
				(ItemID.EyeOfCthulhuPetItem, Tier.PostEyeOfCthulhu), (ItemID.KingSlimePetItem, Tier.PostEyeOfCthulhu), (ItemID.SlimySaddle, Tier.PostEyeOfCthulhu),
				(ItemID.HoneyedGoggles, Tier.PostEvilBoss), (ItemID.EyeofCthulhuTrophy, Tier.PostEyeOfCthulhu), (ItemID.KingSlimeMasterTrophy, Tier.PostEyeOfCthulhu),
				(ItemID.MoonLordMasterTrophy, Tier.PostMoonLord), (ItemID.BossMaskMoonlord, Tier.PostMoonLord)
			};
			foreach ((int item, Tier tier) in exactly)
			{
				if (Usable(item) && ItemPool.TierOf(item) != (int)tier) failures.Add($"{Name(item)}: tier {ItemPool.TierOf(item)}, {(int)tier} expected ({ItemPool.Info[item].Source ?? "no source"})");
			}
			// No vanilla recipe before a late tier (late ingredients, or tier-0 types at a late station): not tier 0
			(int item, Tier tier)[] atLeast =
			{
				(ItemID.NanoBullet, Tier.PostPlantera), (ItemID.VenomBullet, Tier.PostPlantera), (ItemID.MiniNukeI, Tier.PostPlantera), (ItemID.CursedFlare, Tier.Hardmode),
				(ItemID.PartyBullet, Tier.Hardmode), (ItemID.ExplodingBullet, Tier.Hardmode), (ItemID.GoldenBullet, Tier.Hardmode), (ItemID.LightKey, Tier.Hardmode),
				(ItemID.NightKey, Tier.Hardmode), (ItemID.HallowedTorch, Tier.Hardmode), (ItemID.CursedCampfire, Tier.Hardmode), (ItemID.IchorCampfire, Tier.Hardmode),
				(ItemID.CobaltBrick, Tier.Hardmode), (ItemID.ChlorophyteBrick, Tier.PostMechBoss), (ItemID.ShroomitePlating, Tier.PostPlantera),
				(ItemID.SolarBrick, Tier.PostCultist), (ItemID.StardustBrick, Tier.PostCultist), (ItemID.SolarPlatform, Tier.PostCultist),
				(ItemID.MartianPlatform, Tier.PostGolem), (ItemID.MartianCostumeShirt, Tier.PostGolem), (ItemID.MartianUniformTorso, Tier.PostGolem),
				(ItemID.PearlwoodSword, Tier.Hardmode)
			};
			foreach ((int item, Tier tier) in atLeast)
			{
				if (Usable(item) && ItemPool.TierOf(item) < (int)tier) failures.Add($"{Name(item)}: tier {ItemPool.TierOf(item)}, at least {(int)tier} expected ({ItemPool.Info[item].Source ?? "no source"})");
			}
			// The Shellphone: the latest of the Cell Phone and both conches it is made of
			int shellphone = Math.Max(ItemPool.TierOf(ItemID.CellPhone), Math.Max(ItemPool.TierOf(ItemID.DemonConch), ItemPool.TierOf(ItemID.MagicConch)));
			if (Usable(ItemID.Shellphone) && ItemPool.TierOf(ItemID.Shellphone) != shellphone) failures.Add($"{Name(ItemID.Shellphone)}: tier {ItemPool.TierOf(ItemID.Shellphone)}, {shellphone} expected");
			foreach (int item in new[] { ItemID.MagicMirror, ItemID.IceMirror, ItemID.CellPhone, ItemID.MagicConch, ItemID.DemonConch, ItemID.Shellphone })
			{
				if (ItemPool.IsValid(item) && !ItemPool.Info[item].Excluded && ItemPool.Info[item].Category == ItemCategory.BossSummon) failures.Add($"{Name(item)} is a boss summon");
			}
			Check.All(failures, "Tier classification");
		}

		// The tier floors of the audits hold: hard gates get no allowance and are never found before their tier; the Creeper's
		// drops are its Brain's (2); temple and hardmode drops wait for their enemies (Lihzahrd Power Cell 6, Pirate Map 4 from
		// the global rules); the Golden Lock Box counts with its key (Muramasa 3); a pickaxe is no earlier than the gated ore it
		// mines; a reliable boss drop lowers its item (Slimy Saddle: the King Slime). Config overrides are left out
		private static void TierFloorsHold()
		{
			if (!ItemPool.Ready) Check.Skip("items not classified yet");
			HashSet<int> overridden = ProgressionConfig.Instance.TierOverrides.Where(o => o?.Item != null && !o.Item.IsUnloaded).Select(o => o.Item.Type).ToHashSet();
			bool Usable(int item) => ItemPool.IsValid(item) && !ItemPool.Info[item].Excluded && !overridden.Contains(item);
			List<string> failures = new();
			void AtLeast(int item, int tier)
			{
				if (Usable(item) && ItemPool.TrueTierOf(item) < tier) failures.Add($"{Name(item)}: true tier {ItemPool.TrueTierOf(item)}, at least {tier} expected");
			}
			void Exactly(int item, int tier)
			{
				if (Usable(item) && ItemPool.TrueTierOf(item) != tier) failures.Add($"{Name(item)}: true tier {ItemPool.TrueTierOf(item)}, {tier} expected");
			}
			bool gating = ProgressionConfig.Instance.TierGating;
			bool world = SeedSystem.WorldReady;
			// Hard gates: flagged, no allowance, never found (guarantees included) before their true tier
			foreach (int item in ItemClassifier.HardGateMaterials.Where(Usable))
			{
				if (!ItemPool.IsHardGate(item)) failures.Add($"{Name(item)} is no hard gate");
				if (gating && ItemPool.UnlockTierOf(item) != ItemPool.TrueTierOf(item)) failures.Add($"{Name(item)}: unlocked at {ItemPool.UnlockTierOf(item)}, true tier {ItemPool.TrueTierOf(item)}");
				int found = world ? Findability.Tier(item) : Never;
				if (gating && found < ItemPool.TrueTierOf(item)) failures.Add($"{Name(item)}: found at {found}, true tier {ItemPool.TrueTierOf(item)}");
			}
			if (Usable(ItemID.ChlorophytePickaxe) && !ItemPool.IsHardGate(ItemID.ChlorophytePickaxe)) failures.Add("Chlorophyte Pickaxe (only made of Chlorophyte Bars) is no hard gate");
			// The Creeper drops as its Brain; Queen Bee and Deerclops sit with the evil bosses
			if (ItemClassifier.NpcSourceTier(NPCID.Creeper) != (int)Tier.PostEvilBoss) failures.Add($"Creeper: source tier {ItemClassifier.NpcSourceTier(NPCID.Creeper)}, 2 expected");
			Exactly(ItemID.TissueSample, (int)Tier.PostEvilBoss);
			AtLeast(ItemID.CrimsonHelmet, (int)Tier.PostEvilBoss);
			AtLeast(ItemID.DeathbringerPickaxe, (int)Tier.PostEvilBoss);
			foreach (int boss in new[] { NPCID.QueenBee, NPCID.Deerclops })
			{
				if (NpcTiers.BaseTier(boss) != (int)Tier.PostEvilBoss) failures.Add($"{Lang.GetNPCNameValue(boss)}: boss tier {NpcTiers.BaseTier(boss)}, 2 expected");
			}
			// Late enemies and the global rules
			if (ItemClassifier.NpcSourceTier(NPCID.Lihzahrd) != (int)Tier.PostPlantera) failures.Add($"Lihzahrd: source tier {ItemClassifier.NpcSourceTier(NPCID.Lihzahrd)}, 6 expected");
			if (ItemClassifier.NpcSourceTier(NPCID.WyvernHead) != (int)Tier.Hardmode) failures.Add($"Wyvern: source tier {ItemClassifier.NpcSourceTier(NPCID.WyvernHead)}, 4 expected");
			AtLeast(ItemID.LihzahrdPowerCell, (int)Tier.PostPlantera);
			AtLeast(ItemID.LizardEgg, (int)Tier.PostPlantera);
			Exactly(ItemID.PirateMap, (int)Tier.Hardmode);
			if (!LootSnapshot.GlobalDrops().Any(d => d.itemId == ItemID.PirateMap)) failures.Add("the global drop rules hold no Pirate Map");
			else if (Usable(ItemID.PirateMap) && ItemPool.Info[ItemID.PirateMap].SourceTier != (int)Tier.Hardmode)
			{
				failures.Add($"Pirate Map: source tier {ItemPool.Info[ItemID.PirateMap].SourceTier} ({ItemPool.Info[ItemID.PirateMap].Source ?? "none"}), 4 expected");
			}
			// Lock boxes wait for their key
			foreach (int item in new[] { ItemID.Muramasa, ItemID.CobaltShield, ItemID.ShadowKey, ItemID.TreasureMagnet }) Exactly(item, (int)Tier.PostSkeletron);
			if (world && Usable(ItemID.Muramasa) && BagPlan.Plan(ItemID.LockBox, lucky: true).Any(d => d.Item == ItemID.Muramasa) && Findability.Tier(ItemID.Muramasa) >= Never)
			{
				failures.Add("Muramasa stays in the Golden Lock Box but is never found");
			}
			// Mining: pickaxes and pick-gated blocks no earlier than the gated ore
			for (int type = 1; type < Math.Min(ItemID.Count, ItemPool.Info.Length); type++)
			{
				if (!Usable(type) || ItemClassifier.HasKnownTier(type) || ItemPool.Info[type].FromTable || ItemClassifier.TrueTierFixes.ContainsKey(type)) continue;
				int power = Math.Max(RecipePlanner.PickPower(type), RecipePlanner.PickToMine(type));
				foreach ((int pick, int ore) in ItemClassifier.PickGates)
				{
					if (power >= pick && ItemPool.TrueTierOf(type) < ItemPool.TrueTierOf(ore) && failures.Count < 30)
					{
						failures.Add($"{Name(type)} (pick {power}): true tier {ItemPool.TrueTierOf(type)}, below {Name(ore)}'s {ItemPool.TrueTierOf(ore)}");
					}
				}
			}
			foreach (int item in new[] { ItemID.EbonstoneBlock, ItemID.CrimstoneBlock, ItemID.Hellforge, ItemID.NightmarePickaxe, ItemID.MoltenPickaxe }) AtLeast(item, (int)Tier.PostEvilBoss);
			Exactly(ItemID.Hellstone, (int)Tier.PostEvilBoss);
			Exactly(ItemID.Obsidian, (int)Tier.PreBoss);
			AtLeast(ItemID.PickaxeAxe, (int)Tier.PostMechBoss);
			// Reliable sources lower: boss drops, the Plantera era, the material table followed by crafted items
			foreach ((int item, int most) in new[] { (ItemID.SlimySaddle, 1), (ItemID.HoneyedGoggles, 2), (ItemID.Binoculars, 1), (ItemID.TerraBlade, 6), (ItemID.FrozenWings, 4) })
			{
				if (Usable(item) && ItemPool.TrueTierOf(item) > most) failures.Add($"{Name(item)}: true tier {ItemPool.TrueTierOf(item)}, at most {most} expected ({ItemPool.Info[item].Source ?? "no source"})");
			}
			AtLeast(ItemID.BoneWings, (int)Tier.PostPlantera);
			Check.All(failures, "Tier floors");
		}

		// World-generated chests lower the true tier of what they hold to the tier the chest is opened (Trident: a water chest,
		// tier 0), and their bucket follows (Tier is the final true tier); a config override keeps its own
		private static void WorldChestTrueTiers()
		{
			if (!ItemPool.Ready) Check.Skip("items not classified yet");
			HashSet<int> overridden = ProgressionConfig.Instance.TierOverrides.Where(o => o?.Item != null && !o.Item.IsUnloaded).Select(o => o.Item.Type).ToHashSet();
			List<string> failures = new();
			foreach ((int item, int tier) in new[] { (ItemID.Trident, 0), (ItemID.Spear, 0), (ItemID.HermesBoots, 1), (ItemID.Starfury, 1), (ItemID.Muramasa, 3), (ItemID.Flamelash, 3), (ItemID.PiranhaGun, 6) })
			{
				if (overridden.Contains(item) || !ItemPool.IsValid(item) || ItemPool.Info[item].Excluded) continue;
				if (ItemPool.TrueTierOf(item) > tier) failures.Add($"{Name(item)}: true tier {ItemPool.TrueTierOf(item)}, its chest opens at {tier}");
				if (ItemPool.Info[item].Tier != ItemPool.TrueTierOf(item)) failures.Add($"{Name(item)}: bucket tier {ItemPool.Info[item].Tier}, true tier {ItemPool.TrueTierOf(item)}");
			}
			Check.All(failures, "World chest items above their chest's tier");
		}

		private const int Never = Findability.Never;

		private static string Name(int item) => Lang.GetItemNameValue(item);

		private static void Log(string text) => ModContent.GetInstance<UltimateRandomizer>().Logger.Info("TierCoherence: " + text);

		private static string Show(int tier) => tier >= Never ? "never" : tier.ToString();

		// Up to n items spread over the list (its order kept), for variety in the log
		private static List<T> Spread<T>(List<T> items, int n) =>
			items.Count <= n ? items : Enumerable.Range(0, n).Select(k => items[(int)((long)k * items.Count / n)]).ToList();

		private static string Names(IEnumerable<int> items) => string.Join(", ", items.Select(Name));

		private static string ByCategory(IEnumerable<int> items) =>
			string.Join(", ", items.GroupBy(t => ItemPool.Info[t].Category).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}"));

		private static string StationName(int tile)
		{
			List<int> placers = Stations.Placers(tile);
			return placers.Count > 0 ? Name(placers.Min()) : Stations.Name(tile);
		}

		private static void TierCoherence()
		{
			if (!ItemPool.Ready || RecipeSystem.RecipeCount == 0) Check.Skip("no world loaded");
			CraftingConfig crafting = CraftingConfig.Instance;
			ProgressionConfig progression = ProgressionConfig.Instance;
			Log($"begin: mode {GeneralConfig.Instance.RandomizerMode}, gating {progression.TierGating}, allowance {progression.TierAllowance}, " +
				$"recipes {crafting.RandomizeRecipes}, ingredients {crafting.IngredientMode}, stations {crafting.RandomizeStations}, unlocked tier {ProgressionSystem.UnlockedTier}, " +
				$"hardmode {Main.hardMode}. Findability and availability tiers are unlock tiers (ItemPool.UnlockTier of an item's tier at best)");
			try
			{
				RecipePlan plan = RecipePlanner.Get();
				List<int>[] candidates = Candidates();
				ProgressionStations(plan.Availability);
				Recipes(plan, candidates);
				Raised();
				JackpotTail();
				VanillaMaterials();
			}
			finally
			{
				// JackpotChestContent rolls: no jackpot mark may stay behind
				ItemPool.ClearJackpot();
			}
			Log("end");
		}

		// Tier audit: the doubtful vanilla items (not excluded, not banned) with their earliest vanilla source, in
		// <SavePath>/UltimateRandomizer/TierAudit.tsv; counts and examples in client.log ("TierAudit:" lines). Reasons:
		// raised (a source raised its true tier), earlier (a source is 2 tiers or more before its true tier), nosource (no
		// drop, bag, shop, recipe nor gathering, not in KnownTiers: its rarity alone, for materials, blocks, ammo, potions,
		// food and misc), chest (a world-generated chest lowered its true tier). Always passes; only writes that file
		private static readonly ItemCategory[] NoSourceCategories =
		{
			ItemCategory.Material, ItemCategory.OreBar, ItemCategory.Block, ItemCategory.Ammo, ItemCategory.Potion, ItemCategory.Misc, ItemCategory.Food
		};

		private static void TierAudit()
		{
			if (!ItemPool.Ready) Check.Skip("items not classified yet");
			void Audit(string text) => ModContent.GetInstance<UltimateRandomizer>().Logger.Info("TierAudit: " + text);
			StringBuilder tsv = new();
			tsv.AppendLine("id\tinternal name\tname\tcategory\trarity tier\ttier\ttrue tier\tsource tier\tearliest source\treason");
			Dictionary<string, List<int>> byReason = new() { ["raised"] = new(), ["earlier"] = new(), ["nosource"] = new(), ["chest"] = new() };
			int audited = 0, reviewed = 0;
			for (int type = 1; type < Math.Min(ItemID.Count, ItemPool.Info.Length); type++)
			{
				ItemInfo info = ItemPool.Info[type];
				if (info.Excluded || info.Banned) continue;
				// Hand-reviewed (MaterialTiers): not doubtful
				if (info.FromTable)
				{
					reviewed++;
					continue;
				}
				audited++;
				List<string> reasons = new();
				if (info.SourceRaised) reasons.Add("raised");
				if (info.FromWorldChest) reasons.Add("chest");
				if (info.SourceTier >= 0 && info.SourceTier <= info.TrueTier - 2) reasons.Add("earlier");
				if (info.SourceTier < 0 && !ItemClassifier.HasKnownTier(type) && Array.IndexOf(NoSourceCategories, info.Category) >= 0) reasons.Add("nosource");
				if (reasons.Count == 0) continue;
				foreach (string reason in reasons) byReason[reason].Add(type);
				tsv.AppendLine(string.Join("\t", type, ItemID.Search.GetName(type), Name(type), info.Category, info.RarityTier, info.Tier, info.TrueTier,
					info.SourceTier, info.Source ?? "none", string.Join(",", reasons)));
			}
			string dir = Path.Combine(Main.SavePath, "UltimateRandomizer");
			string path = Path.Combine(dir, "TierAudit.tsv");
			Directory.CreateDirectory(dir);
			File.WriteAllText(path, tsv.ToString());
			Audit($"{audited} vanilla items audited, written to {path}; {reviewed} materials of the reviewed table (MaterialTiers: " +
				$"{MaterialTiers.Table.Count} listed, {MaterialTiers.Table.Count(e => e.Value.Tier == null)} banned) take its tier and are left out");
			foreach ((string reason, List<int> items) in byReason)
			{
				Audit($"{reason}: {items.Count} ({ByCategory(items)})");
				List<int> examples = Spread(items, 30);
				for (int k = 0; k < examples.Count; k += 10)
				{
					Audit($"{reason} examples: " + string.Join("; ", examples.Skip(k).Take(10).Select(t =>
						$"{Name(t)} {ItemPool.Info[t].Tier}>{ItemPool.TrueTierOf(t)} (source {ItemPool.Info[t].SourceTier}: {ItemPool.Info[t].Source ?? "none"})")));
				}
			}
			TierMaterials(dir, Audit);
			// The final enemy plan (every drop table, lucky: nothing gated) as rolled without the re-pick: the banned items it
			// would hold are the only entries the bans change
			if (!SeedSystem.WorldReady)
			{
				Audit("final enemy plan: no world loaded");
				return;
			}
			int total = 0;
			List<int> banned = new();
			try
			{
				ItemPool.RepickDisabled = true;
				foreach (int netId in LootSnapshot.Npcs.Keys.OrderBy(k => k))
				{
					foreach (PlannedDrop drop in RandomNpcDropRule.Plan(netId, Main.expertMode, lucky: true, guaranteed: false))
					{
						total++;
						if (ItemPool.IsBanned(drop.Item)) banned.Add(drop.Item);
					}
				}
			}
			finally
			{
				ItemPool.RepickDisabled = false;
			}
			Audit($"final enemy plan: {banned.Count} of {total} planned drops are banned items before the re-pick" +
				(banned.Count > 0 ? ": " + string.Join(", ", banned.GroupBy(t => t).OrderByDescending(g => g.Count()).Take(15).Select(g => $"{Name(g.Key)} x{g.Count()}")) : ""));
		}

		// ItemTiers.tsv (next to TierAudit.tsv): one row per vanilla item (excluded and banned ones flagged) with every tier the
		// pool knows, the vanilla and the planned recipe tiers, the planned ingredients (their tier in brackets) and the mining
		// data. Always passes; only reads the live caches and plans, and writes that file
		private static void ItemTierDump()
		{
			if (!ItemPool.Ready) Check.Skip("items not classified yet");
			if (RecipeSystem.RecipeCount == 0) Check.Skip("recipes not captured yet");
			RecipePlan plan = RecipePlanner.Get();
			if (plan?.Availability == null) Check.Skip("no recipe plan");
			int count = Math.Min(RecipeSystem.RecipeCount, Recipe.numRecipes);
			// Vanilla recipes with their original ingredients and stations: the first tier each result is doable
			int[] vanillaResults = new int[count];
			(int type, int stack)[][] vanillaIngredients = new (int, int)[count][];
			int[][] vanillaTiles = new int[count][];
			int[][] vanillaGroups = new int[count][];
			for (int i = 0; i < count; i++)
			{
				vanillaResults[i] = RecipeSystem.OriginalResult(i);
				vanillaIngredients[i] = RecipeSystem.OriginalIngredients(i);
				vanillaTiles[i] = RecipeSystem.OriginalTiles(i);
				vanillaGroups[i] = RecipeSystem.OriginalGroups(i);
			}
			int[] vanillaResultTiers = Enumerable.Range(0, count).Select(i => RecipePlanner.ResultTier(i, vanillaResults[i])).ToArray();
			int[] vanillaAvail = RecipePlanner.Availability(Findability.BaseTier, vanillaResults, vanillaResultTiers, vanillaIngredients, vanillaTiles, out _, null, vanillaGroups);
			// First live planned recipe of each item
			int[] firstRecipe = new int[ItemPool.Info.Length];
			Array.Fill(firstRecipe, -1);
			for (int i = 0; i < count; i++)
			{
				int result = plan.Results[i].type;
				if (result <= 0 || result >= firstRecipe.Length || firstRecipe[result] >= 0) continue;
				if ((plan.Twin != null && plan.Twin[i]) || (plan.LoopOff != null && plan.LoopOff[i])) continue;
				firstRecipe[result] = i;
			}
			string Bracket(int item) => item > 0 && item < plan.Availability.Length ? Show(plan.Availability[item]) : "?";
			Dictionary<int, string> oresByPick = new();
			StringBuilder tsv = new();
			tsv.AppendLine("id	internal name	name	category	rarity	value	rarity tier	tier	true tier	source tier	earliest source	from table	known tier	excluded	banned	protected	" +
				"findability tier	vanilla recipe tier	planned recipe tier	planned recipe ingredients	key ingredient	pick power	ores it mines	pick to mine");
			int written = 0;
			for (int type = 1; type < Math.Min(ItemID.Count, ItemPool.Info.Length); type++)
			{
				ItemInfo info = ItemPool.Info[type];
				ContentSamples.ItemsByType.TryGetValue(type, out Item sample);
				string ingredients = "", key = "", ores = "";
				int recipe = firstRecipe[type];
				if (recipe >= 0)
				{
					ingredients = string.Join("; ", (plan.Ingredients[recipe] ?? vanillaIngredients[recipe]).Select(x => $"{Name(x.type)} x{x.stack} [{Bracket(x.type)}]"));
					if (plan.PlannedKeys != null && plan.PlannedKeys[recipe] > 0) key = Name(plan.PlannedKeys[recipe]);
				}
				int pick = sample?.pick ?? 0;
				if (pick > 0)
				{
					if (!oresByPick.TryGetValue(pick, out ores))
					{
						ores = string.Join(", ", RecipePlanner.OresFrom(pick).Where(ItemPool.IsValid).OrderBy(o => o).Select(Name));
						oresByPick[pick] = ores;
					}
				}
				tsv.AppendLine(string.Join("	", type, ItemID.Search.GetName(type), Name(type), info.Category, sample?.rare ?? 0, sample?.value ?? 0, info.RarityTier, info.Tier,
					info.TrueTier, info.SourceTier, info.Source ?? "none", info.FromTable, ItemClassifier.HasKnownTier(type), info.Excluded, info.Banned, info.Protected,
					Show(Findability.Tier(type)), type < vanillaAvail.Length ? Show(vanillaAvail[type]) : "", Bracket(type),
					ingredients, key, pick, ores, RecipePlanner.PickToMine(type)));
				written++;
			}
			string dir = Path.Combine(Main.SavePath, "UltimateRandomizer");
			string path = Path.Combine(dir, "ItemTiers.tsv");
			Directory.CreateDirectory(dir);
			File.WriteAllText(path, tsv.ToString());
			ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"ItemTierDump: {written} items written to {path}");
		}

		// TierMaterials.tsv (next to TierAudit.tsv): every vanilla item a replaced ingredient may become in this world
		// (RecipePlanner.IngredientCandidates, categories of weight 0 left out as Candidates does; needs a world), and every
		// other vanilla Material or OreBar not excluded nor banned, crafted or not, with its tiers and earliest source
		private static void TierMaterials(string dir, Action<string> audit)
		{
			bool world = SeedSystem.WorldReady && RecipeSystem.RecipeCount > 0;
			HashSet<int> candidates = new();
			if (world)
			{
				List<int>[,] buckets = RecipePlanner.IngredientCandidates();
				WeightTable weights = CraftingConfig.Instance.IngredientWeights;
				for (int c = 0; c < ItemCategories.Count; c++)
				{
					if (weights.Get((ItemCategory)c) <= 0) continue;
					for (int t = 0; t < Tiers.Count; t++) candidates.UnionWith(buckets[c, t]);
				}
			}
			// Results of the original vanilla recipes (and of the randomized ones of other mods) (live ones when the recipes are not stored yet)
			HashSet<int> crafted = new();
			if (RecipeSystem.RecipeCount > 0)
			{
				for (int i = 0; i < RecipeSystem.RecipeCount; i++)
				{
					if (!RecipeSystem.LeftAlone(i) && !RecipeSystem.IsShell(i)) crafted.Add(RecipeSystem.OriginalResult(i));
				}
			}
			else
			{
				for (int i = 0; i < Recipe.numRecipes; i++)
				{
					if (Main.recipe[i]?.createItem != null && ItemClassifier.IsVanillaRecipe(Main.recipe[i])) crafted.Add(Main.recipe[i].createItem.type);
				}
			}
			List<int> rows = new();
			for (int type = 1; type < Math.Min(ItemID.Count, ItemPool.Info.Length); type++)
			{
				ItemInfo info = ItemPool.Info[type];
				bool material = !info.Excluded && !info.Banned && info.Category is ItemCategory.Material or ItemCategory.OreBar;
				if (material || candidates.Contains(type)) rows.Add(type);
			}
			rows = rows.OrderBy(t => ItemPool.Info[t].TrueTier).ThenBy(t => ItemPool.Info[t].Category).ThenBy(Name, StringComparer.CurrentCulture).ThenBy(t => t).ToList();
			StringBuilder tsv = new();
			tsv.AppendLine("id	internal name	name	category	rarity tier	tier	true tier	unlock tier	findability tier	source tier	earliest source	crafted	candidate");
			foreach (int type in rows)
			{
				ItemInfo info = ItemPool.Info[type];
				int found = world ? Findability.Tier(type) : Never;
				tsv.AppendLine(string.Join("	", type, ItemID.Search.GetName(type), Name(type), info.Category, info.RarityTier, info.Tier, info.TrueTier,
					ItemPool.UnlockTierOf(type), found >= Never ? -1 : found, info.SourceTier, info.Source ?? "none", crafted.Contains(type) ? "yes" : "no",
					candidates.Contains(type) ? "yes" : "no"));
			}
			string path = Path.Combine(dir, "TierMaterials.tsv");
			File.WriteAllText(path, tsv.ToString());
			audit($"{rows.Count} materials and ingredient candidates ({candidates.Count} candidates{(world ? "" : ", no world loaded")}), written to {path}");
		}

		// 1. What a replaced ingredient may become (RecipePlanner.IngredientCandidates: found, never crafted, randomizable, no
		// guaranteed station, no mining ore, no bag or summon; categories of weight 0 left out as PickIngredient does), by
		// findability tier. Natural: its own tier (or the unlock tier of it); later: a lower-tier item only a later random
		// source gives; earlier: found before its tier (gathered, mined, herbs)
		private const int MinTierMaterials = 10;

		private static List<int>[] Candidates()
		{
			List<int>[,] buckets = RecipePlanner.IngredientCandidates();
			WeightTable weights = CraftingConfig.Instance.IngredientWeights;
			List<int>[] byTier = new List<int>[Tiers.Count];
			for (int t = 0; t < Tiers.Count; t++)
			{
				List<int> list = new();
				int all = 0;
				for (int c = 0; c < ItemCategories.Count; c++)
				{
					all += buckets[c, t].Count;
					if (weights.Get((ItemCategory)c) > 0) list.AddRange(buckets[c, t]);
				}
				list = list.OrderBy(x => ItemPool.Info[x].Category).ThenBy(x => x).ToList();
				byTier[t] = list;
				int natural = list.Count(x => ItemPool.TierOf(x) == t || ItemPool.UnlockTier(ItemPool.TierOf(x)) == t);
				int later = list.Count(x => ItemPool.TierOf(x) < t);
				int earlier = list.Count(x => ItemPool.UnlockTier(ItemPool.TierOf(x)) > t);
				// True materials (Material, OreBar) carry the recipes of a tier: fewer than 10 is too thin
				int materials = list.Count(x => ItemPool.Info[x].Category is ItemCategory.Material or ItemCategory.OreBar);
				int common = list.Count(x => ItemPool.CommonnessOf(x) == Commonness.Common);
				Log($"candidates findable at tier {t}: {list.Count} (all categories {all}), {materials} materials ({common} common); natural {natural}, " +
					$"later than their tier {later}, earlier {earlier} [{ByCategory(list)}]: {Names(Spread(list, 25))}");
				if (materials < MinTierMaterials) Log($"WARNING tier {t}: only {materials} material candidates (fewer than {MinTierMaterials})");
			}
			return byTier;
		}

		// 2. Progression stations by catalog tier, with the tier this plan makes them available from; and the other stations
		// vanilla recipes need (the tier RaiseByRecipes gives them: their earliest placing item)
		private static void ProgressionStations(int[] avail)
		{
			List<int> tiles = Stations.RequiredTiles().OrderBy(t => Stations.Info(t).Tier).ThenBy(t => t).ToList();
			for (int t = 0; t < Tiers.Count; t++)
			{
				List<int> at = tiles.Where(tile => Stations.Info(tile).Kind == StationKind.Progression && Stations.Info(tile).Tier == t).ToList();
				if (at.Count == 0) continue;
				Log($"progression stations tier {t}: " + string.Join(", ", at.Select(tile => $"{StationName(tile)}{(Stations.Info(tile).World ? " (world)" : "")} " +
					$"(available {Show(RecipePlanner.StationTier(tile, avail))})")));
			}
			Dictionary<int, List<int>> placers = Stations.PlacersByTile();
			int TrueTier(int item) => ItemPool.IsValid(item) && !ItemPool.Info[item].Excluded ? ItemPool.TrueTierOf(item) : Tiers.Max;
			List<int> others = tiles.Where(tile => Stations.Info(tile).Kind is StationKind.Thematic or StationKind.Unknown).ToList();
			Log("other stations (kind, tier from their placers, available): " + string.Join(", ", others.Select(tile =>
				$"{StationName(tile)} ({Stations.Info(tile).Kind}, {ItemClassifier.StationTier(tile, placers, TrueTier)}, {Show(RecipePlanner.StationTier(tile, avail))})")));
		}

		// 3. Randomized vanilla recipes by the unlock tier U of their planned result: locked at U (an ingredient or station only
		// had from U on), or needing a key ingredient; and how the needing ones would load the candidates of tier U (or U..U+1)
		private static void Recipes(RecipePlan plan, List<int>[] candidates)
		{
			int[] avail = plan.Availability;
			int count = Math.Min(Math.Min(RecipeSystem.RecipeCount, Recipe.numRecipes), plan.Results.Length);
			Dictionary<int, int> stationTiers = new();
			int StationAvail(int tile)
			{
				if (!stationTiers.TryGetValue(tile, out int a)) stationTiers[tile] = a = RecipePlanner.StationTier(tile, avail);
				return a;
			}
			int ItemAvail(Recipe recipe, int type) =>
				ItemClassifier.IngredientChoices(recipe, type).Select(x => x > 0 && x < avail.Length ? avail[x] : Never).DefaultIfEmpty(Never).Min();
			int[] total = new int[Tiers.Count], locked = new int[Tiers.Count], byStation = new int[Tiers.Count], unavailable = new int[Tiers.Count];
			int[] needing = new int[Tiers.Count], needingNoSlot = new int[Tiers.Count], noSlot = new int[Tiers.Count];
			// Uses of each item as an ingredient in the planned recipes today
			Dictionary<int, int> uses = new();
			for (int i = 0; i < count; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (recipe == null || recipe.Disabled || !RecipeSystem.Touchable(i)) continue;
				int result = plan.Results[i].type;
				if (!ItemPool.IsValid(result)) continue;
				int u = Tiers.Clamp(ItemPool.UnlockTierOf(result));
				(int type, int stack)[] ingredients = plan.Ingredients[i] ?? RecipeSystem.OriginalIngredients(i);
				int[] tiles = plan.Tiles[i] ?? RecipeSystem.OriginalTiles(i);
				foreach ((int type, _) in ingredients) uses[type] = uses.GetValueOrDefault(type) + 1;
				int ingredientTier = ingredients.Select(x => ItemAvail(recipe, x.type)).DefaultIfEmpty(0).Max();
				int stationTier = tiles.Select(StationAvail).DefaultIfEmpty(0).Max();
				// The redesign would change the original recipe: a slot the planner may replace
				bool slot = RecipeSystem.IngredientsTouchable(i) && RecipeSystem.OriginalIngredients(i).Any(x => RecipePlanner.IngredientReplaceable(x.type));
				total[u]++;
				if (!slot) noSlot[u]++;
				if (Math.Max(ingredientTier, stationTier) >= Never) unavailable[u]++;
				if (Math.Max(ingredientTier, stationTier) >= u)
				{
					locked[u]++;
					if (ingredientTier < u) byStation[u]++;
				}
				else
				{
					needing[u]++;
					if (!slot) needingNoSlot[u]++;
				}
			}
			Log($"recipes: {total.Sum()} randomizable vanilla recipes (Touchable), {needing.Sum()} still doable before U, {noSlot.Sum()} without a replaceable slot " +
				$"(all kept, recipe groups, bags or summons); plan: {plan.KeyIngredient.Count(k => k)} key ingredients, {plan.SwapPartner.Count(r => r >= 0)} results swapped");
			// Spread of the ingredients (every planned recipe, kept originals too): the busiest material of each findability tier
			for (int t = 0; t < Tiers.Count; t++)
			{
				List<int> used = candidates[t].Where(m => uses.GetValueOrDefault(m) > 0).OrderByDescending(m => uses[m]).ThenBy(m => m).ToList();
				if (used.Count == 0)
				{
					Log($"usage tier {t}: no candidate used");
					continue;
				}
				Log($"usage tier {t}: {used.Count} of {candidates[t].Count} candidates used, {used.Sum(m => uses[m])} uses; top: " +
					string.Join(", ", used.Take(5).Select(m => $"{Name(m)} x{uses[m]} ({ItemPool.CommonnessOf(m)})")));
			}
			for (int u = 0; u < Tiers.Count; u++)
			{
				if (total[u] == 0) continue;
				List<int> exact = candidates[u];
				List<int> two = u + 1 < Tiers.Count ? exact.Concat(candidates[u + 1]).ToList() : exact;
				Log($"recipes U={u}: {total[u]}, locked {locked[u]} (by a station only {byStation[u]}, never available {unavailable[u]}), need a key {needing[u]} " +
					$"({needingNoSlot[u]} without a replaceable slot), no replaceable slot {noSlot[u]}; {Load(needing[u], exact, uses, $"tier {u}")}; " +
					$"{Load(needing[u], two, uses, u + 1 < Tiers.Count ? $"tiers {u}-{u + 1}" : $"tier {u}")}");
			}
		}

		private static string Load(int recipes, List<int> materials, Dictionary<int, int> uses, string label)
		{
			if (materials.Count == 0) return $"{label}: no candidate";
			int even = (recipes + materials.Count - 1) / materials.Count;
			int busiest = materials.Max(m => uses.GetValueOrDefault(m));
			return $"{label}: {materials.Count} candidates, {(double)recipes / materials.Count:0.0} uses each, max {even} (busiest with today's uses {busiest + even})";
		}

		private struct RecipeEval
		{
			public int Tier;
			public List<(int item, int tier)> Parts;
			public List<(int tile, int tier)> Tiles;
		}

		// 4. Items whose true tier RaiseByRecipes or RaiseBySources raised (TierAudit details the sources): by (tier > true tier), by category, what set it (the ingredient or
		// station at the maximum of the earliest vanilla recipe, recomputed the same way from the final true tiers) and examples
		private static void Raised()
		{
			int[] all = ItemPool.AllTiers;
			int TierFor(int t) => ItemPool.IsValid(t) && !ItemPool.Info[t].Excluded ? ItemPool.TrueTierOf(t) : t > 0 && t < all.Length ? all[t] : Tiers.Max;
			// Against the classification tier (the bucket is the final true tier)
			List<int> raised = Enumerable.Range(1, ItemPool.Info.Length - 1).Where(t => !ItemPool.Info[t].Excluded && t < all.Length && ItemPool.TrueTierOf(t) > all[t]).ToList();
			Log($"raised: {raised.Count} items; tier > true tier: " + string.Join(", ", raised.GroupBy(t => (all[t], ItemPool.TrueTierOf(t)))
				.OrderBy(g => g.Key.Item1).ThenBy(g => g.Key.Item2).Select(g => $"{g.Key.Item1}>{g.Key.Item2} {g.Count()}")));
			Log("raised by category: " + ByCategory(raised));
			HashSet<int> set = raised.ToHashSet();
			Dictionary<int, List<int>> placers = Stations.PlacersByTile();
			int stored = RecipeSystem.RecipeCount;
			RecipeEval Evaluate(int i)
			{
				Recipe recipe = Main.recipe[i];
				IEnumerable<int> ingredients = i < stored ? RecipeSystem.OriginalIngredients(i).Select(x => x.type) : recipe.requiredItem.Select(x => x.type);
				RecipeEval eval = new() { Parts = new(), Tiles = new() };
				foreach (int type in ingredients)
				{
					if (type <= 0 || type >= all.Length) continue;
					int[] choices = (i < stored ? ItemClassifier.IngredientChoices(RecipeSystem.OriginalGroups(i), type) : ItemClassifier.IngredientChoices(recipe, type)).Where(t => t > 0 && t < all.Length).ToArray();
					int best = choices.Length == 0 ? type : choices.OrderBy(TierFor).ThenBy(t => t).First();
					eval.Parts.Add((best, choices.Length == 0 ? Tiers.Max : TierFor(best)));
				}
				IEnumerable<int> tiles = i < stored ? RecipeSystem.OriginalTiles(i) : recipe.requiredTile;
				foreach (int tile in tiles.Where(t => t >= 0).Distinct()) eval.Tiles.Add((tile, ItemClassifier.RaisingStationTier(tile, placers, TierFor)));
				eval.Tier = eval.Parts.Select(p => p.tier).Concat(eval.Tiles.Select(t => t.tier)).DefaultIfEmpty(0).Max();
				return eval;
			}
			Dictionary<int, RecipeEval> earliest = new();
			for (int i = 0; i < Recipe.numRecipes; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (recipe == null || recipe.createItem == null) continue;
				int result = i < stored ? RecipeSystem.OriginalResult(i) : recipe.createItem.type;
				if (!set.Contains(result)) continue;
				List<int> ingredients = i < stored ? RecipeSystem.OriginalIngredients(i).Select(x => x.type).ToList() : recipe.requiredItem.Select(x => x.type).ToList();
				if (!ItemClassifier.SetsTier(recipe, ingredients)) continue;
				RecipeEval eval = Evaluate(i);
				if (!earliest.TryGetValue(result, out RecipeEval other) || eval.Tier < other.Tier) earliest[result] = eval;
			}
			// Vanilla loot sources (what RaiseByRecipes skips): drops, bags, shops
			HashSet<int> loot = new(Obtainability.ShopItems);
			foreach (var drops in LootSnapshot.Npcs.Values) loot.UnionWith(drops.Select(d => d.itemId));
			foreach (var drops in LootSnapshot.Items.Values) loot.UnionWith(drops.Select(d => d.itemId));
			Dictionary<int, int> culpritItems = new(), culpritTiles = new();
			int mismatch = 0, byGathered = 0, byStation = 0, byOneTier = 0;
			List<string> mismatches = new();
			int byLoot = 0;
			foreach (int item in raised)
			{
				if (ItemPool.TrueTierOf(item) - ItemPool.TierOf(item) == 1) byOneTier++;
				// Dropped, in a bag or sold: raised by its sources, not only by its recipes
				if (loot.Contains(item))
				{
					byLoot++;
					continue;
				}
				if (!earliest.TryGetValue(item, out RecipeEval eval))
				{
					mismatch++;
					if (mismatches.Count < 8) mismatches.Add($"{Name(item)} (no recipe found)");
					continue;
				}
				if (eval.Tier != ItemPool.TrueTierOf(item))
				{
					mismatch++;
					if (mismatches.Count < 8) mismatches.Add($"{Name(item)} (true tier {ItemPool.TrueTierOf(item)}, recomputed {eval.Tier})");
				}
				List<int> items = eval.Parts.Where(p => p.tier == eval.Tier).Select(p => p.item).Distinct().ToList();
				List<int> tiles = eval.Tiles.Where(t => t.tier == eval.Tier).Select(t => t.tile).Distinct().ToList();
				foreach (int c in items) culpritItems[c] = culpritItems.GetValueOrDefault(c) + 1;
				foreach (int c in tiles) culpritTiles[c] = culpritTiles.GetValueOrDefault(c) + 1;
				if (items.Count == 0 && tiles.Count > 0) byStation++;
				if (items.Count > 0 && items.All(c => Findability.IsWorldMaterial(c) || OreMap.IsOre(c))) byGathered++;
			}
			Log($"raised: {byOneTier} by one tier only; {byLoot} with a loot source (not recomputed here); {byStation} set by a station alone; {byGathered} set only by gathered or mined materials; " +
				$"{mismatch} not recomputed exactly{(mismatches.Count > 0 ? ": " + string.Join(", ", mismatches) : "")}");
			string Culprit(int c) => $"{Name(c)} x{culpritItems[c]} (tier {TierFor(c)}, rarity tier {(ItemPool.IsValid(c) ? ItemPool.Info[c].RarityTier : -1)}, " +
				$"found {Show(Findability.Tier(c))}{(loot.Contains(c) ? ", dropped/sold" : "")}{(Findability.IsWorldMaterial(c) ? ", gathered" : "")}" +
				$"{(OreMap.IsOre(c) ? ", ore" : "")}{(Obtainability.RecipeResults.Contains(c) ? ", crafted" : "")}{(ItemClassifier.HasKnownTier(c) ? ", KnownTiers" : "")})";
			List<int> topItems = culpritItems.OrderByDescending(p => p.Value).ThenBy(p => p.Key).Select(p => p.Key).Take(40).ToList();
			for (int k = 0; k < topItems.Count; k += 10) Log("raised by ingredient: " + string.Join("; ", topItems.Skip(k).Take(10).Select(Culprit)));
			Log("raised by station: " + string.Join("; ", culpritTiles.OrderByDescending(p => p.Value).ThenBy(p => p.Key)
				.Select(p => $"{StationName(p.Key)} x{p.Value} ({Stations.Info(p.Key).Kind}, tier {ItemClassifier.RaisingStationTier(p.Key, placers, TierFor)})")));
			string Example(int item)
			{
				string head = $"{Name(item)} [{ItemPool.Info[item].Category}] {ItemPool.TierOf(item)}>{ItemPool.TrueTierOf(item)}";
				if (!earliest.TryGetValue(item, out RecipeEval eval)) return head + ": no recipe";
				return head + ": " + string.Join(" + ", eval.Parts.Select(p => $"{Name(p.item)}({p.tier})")) +
					(eval.Tiles.Count > 0 ? " @ " + string.Join(" + ", eval.Tiles.Select(t => $"{StationName(t.tile)}({t.tier})")) : "");
			}
			List<int> examples = Spread(raised, 40);
			for (int k = 0; k < examples.Count; k += 4) Log("raised example: " + string.Join(" | ", examples.Skip(k).Take(4).Select(Example)));
		}

		// 5. Jackpot chests, by the chest tiers that may hold one (pre-hardmode and unlocked): the tier WildTier rolls for each
		// slot (cap + k, k WildTierDecay times rarer each step), the true tier of what the slot really holds, and the theory
		private static void JackpotTail()
		{
			if (GeneralConfig.Instance.RandomizerMode != RandomizerMode.Coherent)
			{
				Log("jackpot chests: only measured in Coherent mode");
				return;
			}
			float decay = ProgressionConfig.Instance.WildTierDecay;
			Log($"jackpot chests: decay {decay}, tier window {LootConfig.Instance.TierWindow}, {LootConfig.Instance.JackpotChestChance}% of the eligible chests " +
				"(pre-hardmode and unlocked: surface and underground chests; generated shadow chests are locked)");
			(string kind, int tier)[] kinds = { ("surface wooden", 0), ("underground wooden, gold and themed", 1), ("shadow (opened)", 3) };
			foreach ((string kind, int chestTier) in kinds)
			{
				int cap = ChestSystem.ChestCap(chestTier);
				int steps = Tiers.Max - cap;
				if (steps <= 0) continue;
				int[] rolled = new int[steps + 1], held = new int[steps + 1];
				int chests = 200, slots = 0, items = 0, hardmode = 0, below = 0;
				for (int k = 0; k < chests; k++)
				{
					int x = 300 + k * 11, y = 150 + chestTier * 37 + k % 7;
					// The same draws as JackpotChestContent: the number of slots, then each slot's tier
					int count = DetRandom.Get(RollKey.JackpotChest, x, y).Next(2, 5);
					int[] tiers = Enumerable.Range(0, count).Select(index => ItemPool.WildTier(cap, 100f, decay, DetRandom.Get(RollKey.JackpotChest, x, y, index))).ToArray();
					List<int> content = ChestSystem.JackpotChestContent(x, y, chestTier).Where(e => e.gap > 0).Select(e => e.type).ToList();
					slots += count;
					items += content.Count;
					foreach (int t in tiers) rolled[Math.Clamp(t - cap, 0, steps)]++;
					foreach (int item in content)
					{
						held[Math.Clamp(ItemPool.TrueTierOf(item) - cap, 0, steps)]++;
						if (ItemPool.TrueTierOf(item) >= (int)Tier.Hardmode) hardmode++;
					}
					// Slots and items line up only when no slot came back empty
					if (content.Count == count)
					{
						for (int j = 0; j < count; j++)
						{
							if (ItemPool.TrueTierOf(content[j]) < tiers[j]) below++;
						}
					}
				}
				string Histogram(int[] h, int n) => string.Join(", ", Enumerable.Range(1, steps).Where(g => h[g] > 0 || g <= 3).Select(g => $"+{g} {h[g]} ({100.0 * h[g] / Math.Max(1, n):0.#}%)"));
				double ratio = 1.0 / Math.Max(1.01, decay), sum = Enumerable.Range(1, steps).Sum(g => Math.Pow(ratio, g));
				string theory = string.Join(", ", Enumerable.Range(1, Math.Min(steps, 4)).Select(g => $"+{g} {100 * Math.Pow(ratio, g) / sum:0.#}%"));
				Log($"jackpot {kind} (chest tier {chestTier}, cap {cap}): {chests} chests, {items} items for {slots} slots ({slots - items} empty), " +
					$"hardmode {hardmode} ({100.0 * hardmode / Math.Max(1, items):0.#}%); rolled tier: {Histogram(rolled, slots)}; held true tier: {Histogram(held, items)}; " +
					$"theory: {theory}; items below their rolled tier {below}");
			}
		}

		// 6. Vanilla's own tiers: materials and ores or bars (randomizable, no vanilla recipe makes them) by ItemPool.TierOf
		private static void VanillaMaterials()
		{
			HashSet<int> crafted = new();
			for (int i = 0; i < Math.Min(RecipeSystem.RecipeCount, Recipe.numRecipes); i++)
			{
				if (!RecipeSystem.IsModded(i) && ItemClassifier.IsVanillaRecipe(Main.recipe[i])) crafted.Add(RecipeSystem.OriginalResult(i));
			}
			List<int> materials = Enumerable.Range(1, ItemPool.Info.Length - 1)
				.Where(t => ItemPool.IsRandomizable(t) && ItemPool.Info[t].Category is ItemCategory.Material or ItemCategory.OreBar && !crafted.Contains(t)).ToList();
			for (int t = 0; t < Tiers.Count; t++)
			{
				List<int> list = materials.Where(m => ItemPool.TierOf(m) == t).ToList();
				Log($"vanilla materials tier {t}: {list.Count} [{ByCategory(list)}]: {Names(Spread(list, 25))}");
			}
			Log("vanilla materials by unlock tier (same units as the candidates): " + string.Join(", ", Enumerable.Range(0, Tiers.Count)
				.Select(u => $"{u}: {materials.Count(m => ItemPool.UnlockTier(ItemPool.TierOf(m)) == u)}")));
		}
	}
}

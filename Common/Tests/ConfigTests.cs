using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using UltimateRandomizer.Common.Configs;

namespace UltimateRandomizer.Common.Tests
{
	public static class ConfigTests
	{
		public static void Register()
		{
			TestRunner.Register("ConfigTexts", ConfigTexts);
			TestRunner.Register("OtherTexts", OtherTexts);
			TestRunner.Register("ConfigRanges", ConfigRanges);
		}

		private static readonly Type[] ConfigTypes =
		{
			typeof(GeneralConfig), typeof(ProgressionConfig), typeof(LootConfig), typeof(EconomyConfig), typeof(CraftingConfig),
			typeof(EnemiesConfig), typeof(ExtrasConfig), typeof(ClientConfig), typeof(CategoryWeights), typeof(IngredientWeights), typeof(StackCaps), typeof(TierOverride)
		};

		private const string Prefix = "Mods.UltimateRandomizer.Configs.";

		private static void ConfigTexts()
		{
			List<string> missing = new();
			foreach (Type type in ConfigTypes)
			{
				foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
				{
					if (!Language.Exists($"{Prefix}{type.Name}.{field.Name}.Label")) missing.Add($"{type.Name}.{field.Name}");
					if (field.FieldType.IsEnum)
					{
						foreach (string value in Enum.GetNames(field.FieldType))
						{
							if (!Language.Exists($"{Prefix}{field.FieldType.Name}.{value}.Label")) missing.Add($"{field.FieldType.Name}.{value}");
						}
					}
					// Header only has a value once tModLoader has localized it: read the identifier given in the code
					HeaderAttribute header = field.GetCustomAttribute<HeaderAttribute>();
					string identifier = header != null && header.IsIdentifier
						? typeof(HeaderAttribute).GetField("identifier", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(header) as string
						: null;
					if (header != null && header.IsIdentifier && identifier == null) missing.Add($"{type.Name}.{field.Name} header (unreadable)");
					if (identifier != null && !Language.Exists($"{Prefix}{type.Name}.Headers.{identifier}")) missing.Add($"{type.Name}.Headers.{identifier}");
				}
				if (typeof(ModConfig).IsAssignableFrom(type) && !Language.Exists($"{Prefix}{type.Name}.DisplayName")) missing.Add($"{type.Name}.DisplayName");
			}
			Check.All(missing.Distinct().ToList(), "Missing setting texts");
		}

		private static void OtherTexts()
		{
			string[] keys =
			{
				"Chat.OldRandomizerEnabled", "Chat.StartingKit", "Chat.ConfigHostOnly", "Commands.Seed.Show", "Commands.Seed.Changed", "Commands.Seed.NotAllowed",
				"Commands.Chests.Warning", "Commands.Chests.Done", "Journal.Title", "Journal.Monsters", "Journal.Bags", "Journal.Empty", "Keybinds.Journal.DisplayName",
				"Keybinds.Debug.DisplayName", "Debug.Title", "Debug.NeedWorld", "Debug.TabOverview", "Debug.TabInspector", "Debug.TabTools", "Debug.TabTests"
			};
			List<string> missing = keys.Where(k => !Language.Exists("Mods.UltimateRandomizer." + k)).ToList();
			foreach (string id in TestRunner.TestIds)
			{
				if (!Language.Exists("Mods.UltimateRandomizer.Tests." + id)) missing.Add("Tests." + id);
			}
			Check.All(missing, "Missing texts");
		}

		private static void ConfigRanges()
		{
			List<string> failures = new();
			EconomyConfig economy = EconomyConfig.Instance;
			if (economy.PriceMultiplierMin < 1f || economy.PriceMultiplierMax < 1f) failures.Add("price multiplier below 1 (money exploit)");
			LootConfig loot = LootConfig.Instance;
			if (loot.DropSlotsMin > loot.DropSlotsMax) failures.Add("drop slots: minimum above maximum (the maximum is raised to the minimum)");
			if (loot.BossDropSlotsMin > loot.BossDropSlotsMax) failures.Add("boss drop slots: minimum above maximum");
			EnemiesConfig enemies = EnemiesConfig.Instance;
			(string name, float min, float max)[] ranges =
			{
				("enemy health", enemies.EnemyHealthMin, enemies.EnemyHealthMax), ("enemy damage", enemies.EnemyDamageMin, enemies.EnemyDamageMax),
				("enemy defense", enemies.EnemyDefenseMin, enemies.EnemyDefenseMax), ("enemy size", enemies.EnemyScaleMin, enemies.EnemyScaleMax),
				("boss health", enemies.BossHealthMin, enemies.BossHealthMax), ("boss damage", enemies.BossDamageMin, enemies.BossDamageMax)
			};
			foreach ((string name, float min, float max) in ranges)
			{
				if (min > max) failures.Add($"{name}: minimum above maximum");
				else if (min > 1 || max < 1) failures.Add($"{name}: x{min}-x{max} does not include normal (x1), every value is pushed to one side");
			}
			if (enemies.GiantSizeMin > enemies.GiantSizeMax) failures.Add("giant size: minimum above maximum");
			if (enemies.MiniSizeMin > enemies.MiniSizeMax) failures.Add("mini size: minimum above maximum");
			if (enemies.GiantChance + enemies.MiniChance > 100) failures.Add("giant and mini chances add up to more than 100%");
			Check.All(failures, "Settings");
		}
	}
}

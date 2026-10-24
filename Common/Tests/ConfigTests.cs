using System;
using System.Collections.Generic;
using System.ComponentModel;
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
		// Saving a setting the world caches don't use (enemy stats) keeps findability and the recipe plan; a loot setting or a
		// swap pool setting rebuilds them. Checked on clones: the live config is never touched
		private static void CacheSignatureScope()
		{
			T Clone<T>(T config) where T : ModConfig => (T)ConfigManager.GeneratePopulatedClone(config);
			GeneralConfig general = GeneralConfig.Instance;
			LootConfig loot = LootConfig.Instance;
			CraftingConfig crafting = CraftingConfig.Instance;
			ProgressionConfig progression = ProgressionConfig.Instance;
			EconomyConfig economy = EconomyConfig.Instance;
			string current = Core.SeedSystem.CacheSignature(general, loot, crafting, progression, economy, EnemiesConfig.Instance);
			Check.Equal(Core.SeedSystem.CacheSignature(), current, "signature of the live configs");
			EnemiesConfig stats = Clone(EnemiesConfig.Instance);
			stats.GiantChance += 1;
			stats.RandomizeEnemyStats = !stats.RandomizeEnemyStats;
			Check.That(Core.SeedSystem.CacheSignature(general, loot, crafting, progression, economy, stats) == current, "an enemy stat setting rebuilds the world caches");
			EnemiesConfig pool = Clone(EnemiesConfig.Instance);
			pool.SwapEventEnemies = !pool.SwapEventEnemies;
			Check.That(Core.SeedSystem.CacheSignature(general, loot, crafting, progression, economy, pool) != current, "a swap pool setting does not rebuild the world caches");
			LootConfig otherLoot = Clone(loot);
			otherLoot.RandomizeMobDrops = !otherLoot.RandomizeMobDrops;
			Check.That(Core.SeedSystem.CacheSignature(general, otherLoot, crafting, progression, economy, EnemiesConfig.Instance) != current, "a loot setting does not rebuild the world caches");
			ProgressionConfig adaptive = Clone(progression);
			adaptive.AdaptivePower = !adaptive.AdaptivePower;
			adaptive.AdaptiveGrace += 1;
			adaptive.LifeGrowthPerTier += 0.5f;
			adaptive.BossDamageGrowthPerTier += 0.5f;
			adaptive.AdaptiveCap += 1;
			Check.That(Core.SeedSystem.CacheSignature(general, loot, crafting, adaptive, economy, EnemiesConfig.Instance) == current, "an adaptive power setting rebuilds the world caches");
			ProgressionConfig allowance = Clone(progression);
			allowance.TierAllowance += allowance.TierAllowance < 9 ? 1 : -1;
			Check.That(Core.SeedSystem.CacheSignature(general, loot, crafting, allowance, economy, EnemiesConfig.Instance) != current, "the tier allowance does not rebuild the world caches");
		}

		// Every spawn setting the plans read (the spawn map, the swap pool's natural enemies) and the progression scaling move
		// the world caches' signature; the swap chances don't (planned at their defaults). Checked on clones
		private static void CacheSignatureCovers()
		{
			T Clone<T>(T config) where T : ModConfig => (T)ConfigManager.GeneratePopulatedClone(config);
			GeneralConfig general = GeneralConfig.Instance;
			LootConfig loot = LootConfig.Instance;
			CraftingConfig crafting = CraftingConfig.Instance;
			ProgressionConfig progression = ProgressionConfig.Instance;
			EconomyConfig economy = EconomyConfig.Instance;
			string current = Core.SeedSystem.CacheSignature(general, loot, crafting, progression, economy, EnemiesConfig.Instance);
			List<string> failures = new();
			void Expect(string setting, Action<EnemiesConfig> change)
			{
				EnemiesConfig copy = Clone(EnemiesConfig.Instance);
				change(copy);
				if (Core.SeedSystem.CacheSignature(general, loot, crafting, progression, economy, copy) == current) failures.Add(setting);
			}
			Expect(nameof(EnemiesConfig.RandomizeEnemySpawns), e => e.RandomizeEnemySpawns = !e.RandomizeEnemySpawns);
			Expect(nameof(EnemiesConfig.EnemySwapMode), e => e.EnemySwapMode = e.EnemySwapMode == EnemySwapMode.ByBiome ? EnemySwapMode.PerType : EnemySwapMode.ByBiome);
			Expect(nameof(EnemiesConfig.BiomeExtraEnemies), e => e.BiomeExtraEnemies += 5);
			Expect(nameof(EnemiesConfig.SwapEventEnemies), e => e.SwapEventEnemies = !e.SwapEventEnemies);
			ProgressionConfig scaled = Clone(progression);
			scaled.ScaleWeakEnemiesWithProgression = !scaled.ScaleWeakEnemiesWithProgression;
			if (Core.SeedSystem.CacheSignature(general, loot, crafting, scaled, economy, EnemiesConfig.Instance) == current) failures.Add(nameof(ProgressionConfig.ScaleWeakEnemiesWithProgression));
			foreach ((string setting, Action<EnemiesConfig> change) in new (string, Action<EnemiesConfig>)[]
			{
				(nameof(EnemiesConfig.EnemySwapChance), e => e.EnemySwapChance += 5),
				(nameof(EnemiesConfig.BiomeVanillaChance), e => e.BiomeVanillaChance += 5)
			})
			{
				EnemiesConfig copy = Clone(EnemiesConfig.Instance);
				change(copy);
				if (Core.SeedSystem.CacheSignature(general, loot, crafting, progression, economy, copy) != current) failures.Add($"{setting} (planned at its default, must not rebuild)");
			}
			Check.All(failures, "Settings the plans read that do not rebuild the world caches");
		}

		public static void Register()
		{
			TestRunner.Register("ConfigTexts", ConfigTexts);
			TestRunner.Register("OtherTexts", OtherTexts);
			TestRunner.Register("ConfigRanges", ConfigRanges);
			TestRunner.Register("CacheSignatureScope", CacheSignatureScope);
			TestRunner.Register("CacheSignatureCovers", CacheSignatureCovers);
			TestRunner.Register("ConfigResetDefaults", ConfigResetDefaults);
			TestRunner.Register("DebugToolsDefaultOff", DebugToolsDefaultOff);
		}

		// Players get no debug panel nor debug commands unless they ask for them (they reveal the whole randomization)
		private static void DebugToolsDefaultOff()
		{
			DefaultValueAttribute attribute = typeof(ClientConfig).GetField(nameof(ClientConfig.DebugTools))?.GetCustomAttribute<DefaultValueAttribute>();
			Check.That(attribute != null && attribute.Value is false, $"DebugTools defaults to {attribute?.Value ?? "nothing"}");
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
				"Commands.Chests.Warning", "Commands.Chests.Done", "Codex.Title", "Codex.Empty", "Journal.Description", "Keybinds.Journal.DisplayName",
				"Keybinds.Debug.DisplayName", "Debug.Title", "Debug.NeedWorld", "Debug.TabOverview", "Debug.TabInspector", "Debug.TabTools", "Debug.TabTests"
			};
			List<string> missing = keys.Where(k => !Language.Exists("Mods.UltimateRandomizer." + k)).ToList();
			foreach (string id in TestRunner.TestIds)
			{
				if (!Language.Exists("Mods.UltimateRandomizer.Tests." + id)) missing.Add("Tests." + id);
			}
			Check.All(missing, "Missing texts");
		}

		// The reset button gives every setting (nested weight tables too) its [DefaultValue], keeps the settings needing a
		// reload when asked to, covers every config of the mod, and is never saved itself. Checked on copies.
		private static void ConfigResetDefaults()
		{
			List<string> failures = new();
			void Compare(string where, object target)
			{
				foreach (FieldInfo field in target.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
				{
					object value = field.GetValue(target);
					DefaultValueAttribute attribute = field.GetCustomAttribute<DefaultValueAttribute>();
					if (attribute != null && (field.FieldType.IsPrimitive || field.FieldType.IsEnum))
					{
						object expected = field.FieldType.IsEnum ? Enum.ToObject(field.FieldType, attribute.Value) : Convert.ChangeType(attribute.Value, field.FieldType);
						if (!Equals(expected, value)) failures.Add($"{where}.{field.Name}: {value}, default {expected}");
					}
					else if (value is WeightTable or StackCaps) Compare(where + "." + field.Name, value);
				}
			}
			foreach (ModConfig config in ConfigReset.All) Compare(config.Name, ConfigReset.Defaults(config, false));

			string Json(object value) => Newtonsoft.Json.JsonConvert.SerializeObject(value, ConfigManager.serializerSettings);
			GeneralConfig general = GeneralConfig.Instance;
			GeneralConfig kept = (GeneralConfig)ConfigReset.Defaults(general, true);
			if (Json(kept.ExcludedItems) != Json(general.ExcludedItems)) failures.Add("excluded items (reload needed) not kept");
			if (ReferenceEquals(kept.ExcludedItems, general.ExcludedItems)) failures.Add("the copy shares its list with the live config");
			if (Json(general).Contains(nameof(GeneralConfig.ResetAll))) failures.Add("the reset button is saved with the config");

			HashSet<Type> listed = ConfigReset.All.Select(c => c.GetType()).ToHashSet();
			foreach (Type type in typeof(ConfigReset).Assembly.GetTypes().Where(t => typeof(ModConfig).IsAssignableFrom(t) && !t.IsAbstract))
			{
				if (!listed.Contains(type)) failures.Add($"{type.Name} is not reset");
			}
			Check.All(failures, "Reset");
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

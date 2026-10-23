using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using Terraria.ModLoader.IO;
using UltimateRandomizer.Common.Configs;

namespace UltimateRandomizer.Common.Core
{
	// Per-world seed: saved in the world, sent to joining players. Every randomization is derived from it.
	public class SeedSystem : ModSystem
	{
		public static int WorldSeed { get; private set; }

		// False on the main menu and while a world loads: features must not change anything then
		public static bool WorldReady { get; private set; }

		// Fired when anything the randomization depends on changes (seed, config, world entered or left)
		public static event Action OnSeedChanged;

		public static int ChooseNewSeed()
		{
			GeneralConfig cfg = GeneralConfig.Instance;
			string seedText = Main.ActiveWorldFileData?.SeedText;
			return cfg.SeedSource switch
			{
				SeedSource.Fixed => cfg.FixedSeed,
				SeedSource.FromWorldSeed when !string.IsNullOrEmpty(seedText) => DetRandom.StableHash(seedText),
				_ => Random.Shared.Next()
			};
		}

		private static void Activate(int seed)
		{
			WorldSeed = seed;
			WorldReady = true;
			ProgressionSystem.Refresh(notify: false);
			Changed();
		}

		private static void Changed()
		{
			configSignature = ConfigSignature();
			cacheSignature = CacheSignature();
			ItemPool.OnSeedChanged();
			OnSeedChanged?.Invoke();
		}

		private static string configSignature;
		private static string cacheSignature;

		private static string Json(ModConfig config) => JsonConvert.SerializeObject(config, ConfigManager.serializerSettings);

		// Every server setting: tML calls OnChanged on each save (and config sync) even when nothing changed
		private static string ConfigSignature() =>
			string.Join("|", new ModConfig[] { GeneralConfig.Instance, LootConfig.Instance, CraftingConfig.Instance, ProgressionConfig.Instance, EconomyConfig.Instance,
				EnemiesConfig.Instance, ExtrasConfig.Instance }.Select(Json));

		public static string CacheSignature() => CacheSignature(GeneralConfig.Instance, LootConfig.Instance, CraftingConfig.Instance, ProgressionConfig.Instance,
			EconomyConfig.Instance, EnemiesConfig.Instance);

		// What the world caches (findability, guaranteed sources, recipe plan, ore map, shuffle, shop-only places) are built
		// from: enemy stats and the extras don't count, except the settings of the swap pool the guaranteed hosts are taken from
		// and the town NPC order (the merchants' arrival tiers of ShopPermutation)
		public static string CacheSignature(GeneralConfig general, LootConfig loot, CraftingConfig crafting, ProgressionConfig progression, EconomyConfig economy, EnemiesConfig enemies) =>
			string.Join("|", Json(general), Json(loot), Json(crafting), CacheJson(progression), Json(economy), enemies.SwapModdedEnemies, enemies.SwapEventEnemies, enemies.SwapCritters,
				ExtrasConfig.Instance.RandomizeTownNpcOrder);

		// Adaptive power only changes enemies at spawn: its settings don't rebuild the world caches
		private static string CacheJson(ProgressionConfig progression)
		{
			JObject json = JObject.FromObject(progression, JsonSerializer.Create(ConfigManager.serializerSettings));
			foreach (string field in ProgressionConfig.AdaptiveFields) json.Remove(field);
			return json.ToString(Formatting.None);
		}

		// Changes the seed of the current world (command) and sends it to every player
		public static void ChangeSeed(int seed)
		{
			Activate(seed);
			if (Main.netMode == NetmodeID.Server) NetMessage.SendData(MessageID.WorldData);
		}

		// Something the world caches depend on changed (ore choices of the world): recompute everything
		public static void NotifyConfigChanged()
		{
			if (WorldReady && ItemPool.Ready) Changed();
		}

		// A config was saved: features refresh only when a value changed, and the world caches (findability, recipe
		// plan...) are rebuilt only when one of the settings they depend on changed
		public static void NotifyConfigSaved()
		{
			if (!WorldReady || !ItemPool.Ready) return;
			string all = ConfigSignature();
			if (all == configSignature) return;
			configSignature = all;
			string caches = CacheSignature();
			if (caches != cacheSignature)
			{
				cacheSignature = caches;
				ItemPool.OnSeedChanged();
			}
			OnSeedChanged?.Invoke();
		}

		// Chests are randomized during world generation, before the world data exists
		public override void PreWorldGen()
		{
			WorldReady = false;
			WorldSeed = ChooseNewSeed();
			ItemPool.OnSeedChanged();
		}

		public override void OnWorldLoad()
		{
			// Multiplayer clients get the seed from the server (NetReceive) and nothing else
			if (Main.netMode == NetmodeID.MultiplayerClient) return;
			// Default for worlds without a saved seed; LoadWorldData overrides it
			WorldSeed = ChooseNewSeed();
		}

		public override void LoadWorldData(TagCompound tag)
		{
			if (tag.ContainsKey("seed")) WorldSeed = tag.GetInt("seed");
		}

		// Single player and server: the world is fully loaded, everything is computed once here
		public override void PostWorldLoad() => Activate(WorldSeed);

		public override void SaveWorldData(TagCompound tag)
		{
			tag["seed"] = WorldSeed;
		}

		public override void NetSend(BinaryWriter writer)
		{
			writer.Write(WorldSeed);
			writer.Write(ItemPool.ContentHash);
		}

		public override void NetReceive(BinaryReader reader)
		{
			int seed = reader.ReadInt32();
			int contentHash = reader.ReadInt32();
			if (Main.netMode != NetmodeID.MultiplayerClient) return;
			if (contentHash != ItemPool.ContentHash)
			{
				Mod.Logger.Warn("The server has a different item list than this client: randomization may not match.");
			}
			if (!WorldReady || seed != WorldSeed) Activate(seed);
		}

		public override void OnWorldUnload()
		{
			WorldReady = false;
			OnSeedChanged?.Invoke();
		}

		public override void Unload() => OnSeedChanged = null;
	}
}

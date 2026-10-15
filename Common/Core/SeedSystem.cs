using System;
using System.IO;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using UltimateRandomizer.Common.Configs;

namespace UltimateRandomizer.Common.Core
{
	// Per-world seed: saved in the world, sent to joining players. Every randomization is derived from it.
	public class SeedSystem : ModSystem
	{
		public static int WorldSeed { get; private set; }

		// False on the main menu and while the mod loads: features must not change anything then
		public static bool WorldReady { get; private set; }

		public static event Action OnSeedChanged;

		public static int ChooseNewSeed()
		{
			GeneralConfig cfg = GeneralConfig.Instance;
			return cfg.SeedSource switch
			{
				SeedSource.Fixed => cfg.FixedSeed,
				SeedSource.FromWorldSeed => DetRandom.StableHash(Main.ActiveWorldFileData?.SeedText ?? ""),
				_ => Random.Shared.Next()
			};
		}

		public static void SetSeed(int seed)
		{
			WorldSeed = seed;
			ProgressionSystem.Refresh();
			ItemPool.OnSeedChanged();
			OnSeedChanged?.Invoke();
		}

		// Changes the seed of the current world (command) and sends it to every player
		public static void ChangeSeed(int seed)
		{
			SetSeed(seed);
			if (Main.netMode == NetmodeID.Server)
			{
				NetMessage.SendData(MessageID.WorldData);
			}
		}

		// Chests are randomized during world generation, before the world data exists
		public override void PreWorldGen() => SetSeed(ChooseNewSeed());

		public override void OnWorldLoad()
		{
			// New world: pick a seed. LoadWorldData overrides it right after if the world already has one
			WorldReady = true;
			SetSeed(ChooseNewSeed());
		}

		public override void LoadWorldData(TagCompound tag)
		{
			if (tag.ContainsKey("seed"))
			{
				SetSeed(tag.GetInt("seed"));
			}
		}

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
			if (contentHash != ItemPool.ContentHash)
			{
				Mod.Logger.Warn("The server has a different item list than this client: randomization may not match.");
			}
			if (!WorldReady || seed != WorldSeed)
			{
				WorldReady = true;
				SetSeed(seed);
			}
		}

		public override void OnWorldUnload()
		{
			WorldReady = false;
			OnSeedChanged?.Invoke();
		}

		public override void Unload() => OnSeedChanged = null;
	}
}

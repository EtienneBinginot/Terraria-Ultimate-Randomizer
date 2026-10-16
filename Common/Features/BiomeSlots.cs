using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.GameContent.Bestiary;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;
using Tags = Terraria.GameContent.Bestiary.BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions;

namespace UltimateRandomizer.Common.Features
{
	// Vanilla spawn biomes, read from the bestiary spawn tags. Modded biomes get keys from ModdedBiomeStart.
	public enum SpawnBiome
	{
		Forest, Snow, Desert, Jungle, Ocean, Corruption, Crimson, Hallow, Mushroom, Graveyard, Meteor, Sky,
		Underground, Caverns, UndergroundSnow, UndergroundDesert, UndergroundJungle, UndergroundCorruption, UndergroundCrimson,
		UndergroundHallow, UndergroundMushroom, Underworld, Dungeon, Temple, Granite, Marble, SpiderNest
	}

	public enum SlotTime { Any, Day, Night }

	// The zones of a spawn (player zones and the spider wall), to find its biome
	[Flags]
	public enum SpawnZone
	{
		None = 0, Dungeon = 1 << 0, Temple = 1 << 1, SpiderNest = 1 << 2, Graveyard = 1 << 3, Meteor = 1 << 4, Granite = 1 << 5, Marble = 1 << 6,
		Underworld = 1 << 7, Sky = 1 << 8, DirtLayer = 1 << 9, RockLayer = 1 << 10, Glowshroom = 1 << 11, Jungle = 1 << 12,
		UndergroundDesert = 1 << 13, Desert = 1 << 14, Snow = 1 << 15, Hallow = 1 << 16, Corrupt = 1 << 17, Crimson = 1 << 18, Beach = 1 << 19
	}

	// A place where vanilla spawns an enemy: (biome, original enemy). Its owner is the enemy that spawns there instead.
	public class BiomeSlot
	{
		public int Biome;
		public int Original;
		public int Owner;
		public SlotTime Time;
		public int Movement;
		// Tier of the original (NpcTiers), raised to hardmode in hardmode-only biomes and to post-Plantera in the Temple:
		// owners never come from a higher tier, so progression stays classic
		public int Tier;
		public bool Rare;
		// Open only with a weather, a season or an event (rain, sandstorm, Halloween...)
		public bool Conditional;
		public bool Critter;
		// Can be met on the surface by day: never holds an enemy that flees or dies in daylight
		public bool DaySurface;
		// One of the slots that carry the critical stations (Forest slimes and zombies): never shared with an extra
		public bool Critical;
		// An enemy without a natural biome (event enemy) sharing this slot: each spawn picks it half of the time (0: none)
		public int Extra;
		// Its class had too few owners and was merged with a neighbouring tier: it may get an owner one tier above or any
		// tier below (never across the hardmode or Plantera line)
		public bool Merged;

		// 0 pre-hardmode, 1 hardmode, 2 post-Plantera (when the slot opens)
		public int Band => BiomeSlots.BandOf(Tier);

		public int ClassKey => Movement * 100 + (Rare ? 50 : 0) + (Conditional ? 20 : 0) + Tier;
	}

	public class BiomeSlotMap
	{
		public readonly List<BiomeSlot> Slots = new();
		public readonly Dictionary<(int biome, int original), BiomeSlot> ByKey = new();
		public readonly Dictionary<int, List<BiomeSlot>> ByOriginal = new();
		public int Classes;
		public int ClassesKeptVanilla;
		public int ExtraTarget;
		// For each type (slime colours are Blue Slimes): the tier from which it spawns somewhere, as an owner or an extra
		public readonly Dictionary<int, int> SpawnTier = new();

		public int SlotCount(int owner) => Slots.Count(s => s.Owner == owner);
	}

	// Enemies redistributed by biome: every natural enemy (vanilla and modded) keeps its number of spawn slots, but in
	// other biomes. Built from the bestiary, the world seed and the synced settings only, so every machine gets the same
	// map (the swaps themselves run on the server; the map is also read by the guaranteed sources and the pacing model).
	public static class BiomeSlots
	{
		public const int ModdedBiomeStart = 100;

		// Share of the spawns of a shared slot that are its extra
		public const double ExtraShare = 0.5;

		// A class with fewer distinct owners is merged with the class of the tier below
		public const int MinClassOwners = 3;

		// Slime colours vanilla spawns directly (SetDefaults right after NewNPC); the other negative IDs are size variants
		private static readonly HashSet<int> SpawnedNetIds = new() { NPCID.GreenSlime, NPCID.Pinky, NPCID.BlackSlime, NPCID.PurpleSlime, NPCID.RedSlime, NPCID.YellowSlime, NPCID.JungleSlime };

		// Only exist by transformation: they are redistributed through the enemy they come from (Nymph: Lost Girl)
		private static readonly HashSet<int> TransformedOnly = new() { NPCID.Nymph };

		// Enemies vanilla only spawns in hardmode: their slots never open before it, whatever their stats say
		private static readonly HashSet<int> HardmodeOnly = new()
		{
			NPCID.DesertGhoul, NPCID.DesertGhoulCorruption, NPCID.DesertGhoulCrimson, NPCID.DesertGhoulHallow, NPCID.DesertLamiaLight, NPCID.DesertLamiaDark,
			NPCID.DesertScorpionWalk, NPCID.DesertScorpionWall, NPCID.DesertBeast, NPCID.DesertDjinn, NPCID.ToxicSludge, NPCID.Pixie, NPCID.Unicorn,
			NPCID.Gastropod, NPCID.IlluminantBat, NPCID.IlluminantSlime, NPCID.ChaosElemental, NPCID.EnchantedSword, NPCID.CursedHammer, NPCID.CrimsonAxe,
			NPCID.Corruptor, NPCID.Slimer, NPCID.Clinger, NPCID.CorruptSlime, NPCID.Herpling, NPCID.Crimslime, NPCID.IchorSticker, NPCID.FloatyGross,
			NPCID.Wolf, NPCID.IceTortoise, NPCID.IcyMerman, NPCID.ArmoredViking, NPCID.IceElemental, NPCID.IceGolem, NPCID.GiantBat, NPCID.ArmoredSkeleton,
			NPCID.SkeletonArcher, NPCID.Mummy, NPCID.DarkMummy, NPCID.LightMummy, NPCID.BloodMummy, NPCID.Werewolf, NPCID.Wraith, NPCID.PossessedArmor,
			NPCID.WanderingEye, NPCID.Derpling, NPCID.GiantTortoise, NPCID.AnglerFish, NPCID.Arapaima, NPCID.MossHornet, NPCID.Moth, NPCID.JungleCreeper,
			NPCID.JungleCreeperWall, NPCID.GiantFlyingFox, NPCID.Lavabat, NPCID.RedDevil, NPCID.FungoFish, NPCID.GiantFungiBulb, NPCID.RockGolem,
			NPCID.BlackRecluse, NPCID.BlackRecluseWall, NPCID.RuneWizard, NPCID.RainbowSlime, NPCID.SandElemental, NPCID.SandShark,
			NPCID.PigronCorruption, NPCID.PigronCrimson, NPCID.PigronHallow, NPCID.BigMimicCorruption, NPCID.BigMimicCrimson, NPCID.BigMimicHallow, NPCID.BigMimicJungle
		};

		// Only spawn during a sandstorm (the other sandstorm-tagged enemies are regular desert enemies)
		private static readonly HashSet<int> SandstormOnly = new()
		{
			NPCID.Tumbleweed, NPCID.SandElemental, NPCID.SandShark, NPCID.SandsharkCorrupt, NPCID.SandsharkCrimson, NPCID.SandsharkHallow
		};

		// Rare enemies that vanilla spawns through special rules rather than NPC.rarity
		private static readonly HashSet<int> ExplicitRare = new()
		{
			NPCID.Mimic, NPCID.IceMimic, NPCID.BigMimicCorruption, NPCID.BigMimicCrimson, NPCID.BigMimicHallow, NPCID.BigMimicJungle,
			NPCID.DungeonSpirit, NPCID.Pinky, NPCID.GoldenSlime, NPCID.LostGirl, NPCID.Tim, NPCID.RuneWizard, NPCID.DoctorBones, NPCID.BoneLee
		};

		// Event enemies that break outside their event, or are spawned by another enemy
		private static readonly HashSet<int> ExtraDenied = new()
		{
			NPCID.VampireBat, NPCID.BloodNautilus, NPCID.ChatteringTeethBomb, NPCID.ShadowFlameApparition, NPCID.PirateShip, NPCID.PirateShipCannon,
			NPCID.ChaosBall, NPCID.BloodEelHead, NPCID.BloodEelBody, NPCID.BloodEelTail
		};

		// The Forest slots of the vanilla hosts of the critical stations (GuaranteedSources): day slimes, night zombies and eyes
		public static readonly int[] CriticalDayOriginals = { NPCID.BlueSlime, NPCID.GreenSlime, NPCID.PurpleSlime };
		public static readonly int[] CriticalNightOriginals = { NPCID.Zombie, NPCID.DemonEye };

		private class Natural
		{
			public int Id;
			public List<int> Biomes;
			public SlotTime Time;
			public int Movement;
			public int Tier;
			public bool Rare;
			public bool Conditional;
			public bool Critter;
			public bool Intolerant;
		}

		private const int ShuffleAttempts = 8;

		// Hardmode-only enemies whose stats alone said pre-hardmode (logged with the map)
		private static readonly List<int> raisedToHardmode = new();

		private static List<Natural> catalog;
		private static List<Natural> extras;
		private static Dictionary<IBestiaryInfoElement, SpawnBiome> biomeTags;
		private static HashSet<IBestiaryInfoElement> conditionalTags;
		private static HashSet<IBestiaryInfoElement> eventTags;
		private static HashSet<IBestiaryInfoElement> extraEventTags;
		private static HashSet<IBestiaryInfoElement> hardmodeTags;
		private static List<ModBiome> moddedBiomes;
		// Modded biomes where at least one natural enemy spawns: the others don't hide the vanilla biome under them
		private static HashSet<int> usedModdedBiomes = new();
		private static BiomeSlotMap current;

		// Is the by-biome redistribution in use in this world?
		public static bool Active => EnemiesConfig.Instance.RandomizeEnemySpawns && EnemiesConfig.Instance.EnemySwapMode == EnemySwapMode.ByBiome;

		// The map of this world (seed and settings)
		public static BiomeSlotMap Current
		{
			get
			{
				if (current != null) return current;
				current = Build(SeedSystem.WorldSeed, EnemiesConfig.Instance.BiomeExtraEnemies);
				ModContent.GetInstance<UltimateRandomizer>().Logger.Info($"Enemies by biome: {current.Slots.Count} slots, {current.Classes} classes, {current.ClassesKeptVanilla} kept vanilla, "
					+ $"{current.Slots.Count(s => s.Extra != 0)} shared with extras; raised to hardmode: {string.Join(", ", raisedToHardmode.Select(Lang.GetNPCNameValue))}");
				return current;
			}
		}

		public static int BandOf(int tier) => tier >= (int)Tier.PostPlantera ? 2 : tier >= (int)Tier.Hardmode ? 1 : 0;

		public static void Reset()
		{
			current = null;
			catalog = null;
			extras = null;
			tiers = null;
		}

		public static void Unload()
		{
			Reset();
			biomeTags = null;
			conditionalTags = null;
			eventTags = null;
			extraEventTags = null;
			hardmodeTags = null;
			moddedBiomes = null;
		}

		public static bool IsSurface(int biome) => biome >= ModdedBiomeStart || (SpawnBiome)biome is SpawnBiome.Forest or SpawnBiome.Snow or SpawnBiome.Desert
			or SpawnBiome.Jungle or SpawnBiome.Ocean or SpawnBiome.Corruption or SpawnBiome.Crimson or SpawnBiome.Hallow or SpawnBiome.Mushroom
			or SpawnBiome.Graveyard or SpawnBiome.Meteor or SpawnBiome.Sky;

		public static string BiomeName(int biome)
		{
			if (biome < ModdedBiomeStart) return ((SpawnBiome)biome).ToString();
			EnsureTags();
			int index = biome - ModdedBiomeStart;
			return index < moddedBiomes.Count ? moddedBiomes[index].FullName : "Modded" + index;
		}

		// The bestiary element of a biome named by BiomeName (its filter icon and display name), or null
		public static IBestiaryInfoElement BestiaryElement(string biomeName)
		{
			if (string.IsNullOrEmpty(biomeName)) return null;
			EnsureTags();
			if (!biomeName.Contains('/') && Enum.TryParse(biomeName, out SpawnBiome biome))
			{
				// The first tag of a biome is its main one (Surface for the Forest, Snow before the ice variants...)
				foreach ((IBestiaryInfoElement element, SpawnBiome value) in biomeTags)
				{
					if (value == biome) return element;
				}
				return null;
			}
			return moddedBiomes.FirstOrDefault(b => b.FullName == biomeName)?.ModBiomeBestiaryInfoElement;
		}

		public static string BiomeDisplayName(string biomeName)
		{
			if (string.IsNullOrEmpty(biomeName)) return "";
			EnsureTags();
			ModBiome modded = biomeName.Contains('/') ? moddedBiomes.FirstOrDefault(b => b.FullName == biomeName) : null;
			// A biome its mod did not name would show its raw key
			if (modded != null) return Terraria.Localization.Language.Exists(modded.DisplayName.Key) ? modded.DisplayName.Value : modded.Name;
			if (BestiaryElement(biomeName) is IFilterInfoProvider filter && Terraria.Localization.Language.Exists(filter.GetDisplayNameKey()))
			{
				return Terraria.Localization.Language.GetTextValue(filter.GetDisplayNameKey());
			}
			return Codex.Interface.CodexText.Readable(biomeName);
		}

		// The type of an owner (slime colours are Blue Slimes)
		public static int TypeOf(int id) => id < 0 && ContentSamples.NpcsByNetId.TryGetValue(id, out NPC sample) ? sample.type : id;

		public static BiomeSlotMap Build(int seed, float extraPercent)
		{
			BiomeSlotMap map = new();
			List<Natural> naturals = Catalog();
			Dictionary<int, Natural> byId = naturals.ToDictionary(n => n.Id);
			foreach (Natural n in naturals)
			{
				foreach (int biome in n.Biomes)
				{
					map.Slots.Add(new BiomeSlot
					{
						Biome = biome, Original = n.Id, Owner = n.Id, Time = n.Time, Movement = n.Movement, Tier = n.Tier, Rare = n.Rare, Conditional = n.Conditional, Critter = n.Critter,
						// A slot inherits its original's intolerance: vanilla never meets a Gnome in the sun, so its slot is no day slot
						DaySurface = IsSurface(biome) && n.Time != SlotTime.Night && !n.Intolerant,
						Critical = biome == (int)SpawnBiome.Forest && (CriticalDayOriginals.Contains(n.Id) || CriticalNightOriginals.Contains(n.Id))
					});
				}
			}
			map.Slots.Sort((a, b) => a.Biome != b.Biome ? a.Biome.CompareTo(b.Biome)
				: DetRandom.NpcKey(a.Original) != DetRandom.NpcKey(b.Original) ? DetRandom.NpcKey(a.Original).CompareTo(DetRandom.NpcKey(b.Original)) : a.Original.CompareTo(b.Original));

			bool Intolerant(int id) => byId.TryGetValue(id, out Natural n) ? n.Intolerant : EnemySwapPool.DaylightIntolerant(id);
			// Never an owner of a higher tier than the slot, except one tier above in merged classes, never across the
			// hardmode or Plantera line
			bool Accepts(BiomeSlot slot, int owner)
			{
				int tier = byId[owner].Tier;
				// Critical slots never get a stronger owner: the first work bench must drop from weak enemies
				int above = slot.Merged && !slot.Critical ? 1 : 0;
				return (!slot.DaySurface || !Intolerant(owner)) && tier <= slot.Tier + above && BandOf(tier) <= slot.Band;
			}

			foreach ((int groupKey, List<BiomeSlot> slots) in Groups(map))
			{
				map.Classes++;
				// Too small to permute, even merged: stays vanilla
				if (slots.Select(s => s.Original).Distinct().Count() < MinClassOwners)
				{
					map.ClassesKeptVanilla++;
					continue;
				}
				// Repairs only swap two owners (nobody is dropped). A shuffle the greedy repair can't fix is tried again with
				// another key; the vanilla assignment is always valid, so a class that never works stays vanilla.
				int[] owners = null;
				for (int attempt = 0; attempt < ShuffleAttempts && owners == null; attempt++)
				{
					owners = Shuffle(slots, new Random(DetRandom.Hash(seed, RollKey.BiomeSlots, groupKey, attempt)), Accepts);
				}
				if (owners == null)
				{
					map.ClassesKeptVanilla++;
					continue;
				}
				for (int i = 0; i < slots.Count; i++) slots[i].Owner = owners[i];
			}

			int enemySlots = map.Slots.Count(s => !s.Critter);
			map.ExtraTarget = (int)Math.Round(enemySlots * Math.Clamp(extraPercent, 0f, 100f) / 100f);
			if (map.ExtraTarget > 0) PlaceExtras(map, seed);

			foreach (BiomeSlot slot in map.Slots)
			{
				foreach (int id in slot.Extra != 0 ? new[] { slot.Owner, slot.Extra } : new[] { slot.Owner })
				{
					// Never earlier than its own tier, nor than the slot opens
					int type = TypeOf(id);
					int tier = Math.Max(Math.Max(NpcTiers.BaseTier(type), TierOf(id)), slot.Tier);
					map.SpawnTier[type] = Math.Min(map.SpawnTier.GetValueOrDefault(type, int.MaxValue), tier);
				}
				map.ByKey[(slot.Biome, slot.Original)] = slot;
				if (!map.ByOriginal.TryGetValue(slot.Original, out List<BiomeSlot> list)) map.ByOriginal[slot.Original] = list = new List<BiomeSlot>();
				list.Add(slot);
			}
			return map;
		}

		// A shuffle of the class owners, repaired by swapping pairs; null if the repair fails
		private static int[] Shuffle(List<BiomeSlot> slots, Random rng, Func<BiomeSlot, int, bool> accepts)
		{
			int[] owners = slots.Select(s => s.Original).ToArray();
			for (int i = owners.Length - 1; i > 0; i--)
			{
				int j = rng.Next(i + 1);
				(owners[i], owners[j]) = (owners[j], owners[i]);
			}
			for (int i = 0; i < owners.Length; i++)
			{
				if (accepts(slots[i], owners[i])) continue;
				List<int> partners = new();
				for (int j = 0; j < owners.Length; j++)
				{
					if (j != i && accepts(slots[i], owners[j]) && accepts(slots[j], owners[i])) partners.Add(j);
				}
				if (partners.Count == 0) return null;
				int k = partners[rng.Next(partners.Count)];
				(owners[i], owners[k]) = (owners[k], owners[i]);
			}
			return owners;
		}

		private static int OwnerCount(List<BiomeSlot> slots) => slots.Select(s => s.Original).Distinct().Count();

		// Permutation groups: same movement, rarity and condition, same tier. A class with fewer than MinClassOwners owners
		// joins the group of the tier below; the lowest one joins the group above.
		private static List<(int key, List<BiomeSlot> slots)> Groups(BiomeSlotMap map)
		{
			List<(int key, List<BiomeSlot> slots)> groups = new();
			foreach (IGrouping<(int Movement, bool Rare, bool Conditional), BiomeSlot> family in map.Slots.GroupBy(s => (s.Movement, s.Rare, s.Conditional))
				.OrderBy(f => f.Key.Movement).ThenBy(f => f.Key.Rare).ThenBy(f => f.Key.Conditional))
			{
				List<List<BiomeSlot>> merged = new();
				foreach (IGrouping<int, BiomeSlot> tier in family.GroupBy(s => s.Tier).OrderBy(t => t.Key))
				{
					List<BiomeSlot> slots = tier.ToList();
					if (merged.Count > 0 && OwnerCount(slots) < MinClassOwners)
					{
						merged[^1].AddRange(slots);
						foreach (BiomeSlot slot in merged[^1]) slot.Merged = true;
						continue;
					}
					merged.Add(slots);
				}
				if (merged.Count >= 2 && OwnerCount(merged[0]) < MinClassOwners)
				{
					merged[1].InsertRange(0, merged[0]);
					foreach (BiomeSlot slot in merged[1]) slot.Merged = true;
					merged.RemoveAt(0);
				}
				foreach (List<BiomeSlot> slots in merged) groups.Add((slots[0].ClassKey, slots));
			}
			return groups;
		}

		// Extras (enemies without any natural biome) share a few slots with their owners: nobody loses a slot. Same tier
		// first, then any higher tier slot of the same movement and rarity.
		private static void PlaceExtras(BiomeSlotMap map, int seed)
		{
			Random rng = new(DetRandom.Hash(seed, RollKey.BiomeExtras));
			List<Natural> rotation = Extras().ToList();
			for (int i = rotation.Count - 1; i > 0; i--)
			{
				int j = rng.Next(i + 1);
				(rotation[i], rotation[j]) = (rotation[j], rotation[i]);
			}
			int placed = 0;
			for (int pass = 0; pass < 2 && placed < map.ExtraTarget; pass++)
			{
				List<Natural> left = new(rotation);
				while (placed < map.ExtraTarget && left.Count > 0)
				{
					for (int k = 0; k < left.Count && placed < map.ExtraTarget;)
					{
						Natural extra = left[k];
						List<BiomeSlot> options = map.Slots.Where(s => Fits(s, extra, pass == 0)).ToList();
						if (options.Count == 0)
						{
							left.RemoveAt(k);
							continue;
						}
						options[rng.Next(options.Count)].Extra = extra.Id;
						placed++;
						k++;
					}
				}
			}
		}

		// An extra fits a free slot of its movement and rarity, of its tier (or above when sameTier is false), never a
		// critter, critical or conditional slot (it should come outside events), never a day surface slot if it flees daylight
		private static bool Fits(BiomeSlot slot, Natural extra, bool sameTier) =>
			!slot.Critter && slot.Extra == 0 && !slot.Critical && !slot.Conditional && slot.Movement == extra.Movement && slot.Rare == extra.Rare
			&& (sameTier ? slot.Tier == extra.Tier : slot.Tier >= extra.Tier) && (!slot.DaySurface || !extra.Intolerant);

		// Slots that could still take an extra (the tests: below the target, every place an extra fits must be taken)
		public static int FreeExtraPlaces(BiomeSlotMap map) => map.Slots.Count(s => Extras().Any(e => Fits(s, e, false)));

		private static Dictionary<int, int> tiers;

		// Tier of an extra candidate or a natural enemy (raised for hardmode-only ones), -1 if neither
		public static int TierOf(int id)
		{
			tiers ??= Catalog().Concat(Extras()).GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First().Tier);
			return tiers.TryGetValue(id, out int tier) ? tier : -1;
		}

		// Who spawns in a slot: its owner, or half of the time its extra
		public static int Pick(BiomeSlot slot, double roll) => slot.Extra != 0 && roll < ExtraShare ? slot.Extra : slot.Owner;

		// Is this slot open now? Hardmode slots only once hardmode starts, post-Plantera ones once Plantera is dead
		public static bool SlotActive(BiomeSlot slot) => slot.Band switch
		{
			0 => true,
			1 => Main.hardMode,
			_ => NPC.downedPlantBoss
		};

		// The slot a spawn fills: the biome of the player it spawned for, else any slot of the original (always the same one)
		public static BiomeSlot SlotFor(BiomeSlotMap map, NPC npc, int original)
		{
			if (!map.ByOriginal.ContainsKey(original)) return null;
			int closest = npc.FindClosestPlayer();
			List<int> biomes = closest >= 0 && closest < Main.maxPlayers && Main.player[closest].active
				? BiomesOf(Main.player[closest], npc) : new List<int> { (int)SpawnBiome.Forest };
			return SlotIn(map, biomes, original);
		}

		public static BiomeSlot SlotIn(BiomeSlotMap map, int biome, int original) => SlotIn(map, new List<int> { biome }, original);

		// The first matching biome (priority order) where the original has a slot, else any of its slots (always the same one)
		public static BiomeSlot SlotIn(BiomeSlotMap map, List<int> biomes, int original)
		{
			foreach (int biome in biomes)
			{
				if (map.ByKey.TryGetValue((biome, original), out BiomeSlot slot)) return slot;
			}
			if (!map.ByOriginal.TryGetValue(original, out List<BiomeSlot> slots)) return null;
			int first = biomes.Count > 0 ? biomes[0] : (int)SpawnBiome.Forest;
			return slots[(int)((uint)DetRandom.Hash(first, original) % (uint)slots.Count)];
		}

		// The identity used by the map for a live NPC: its slime colour when that colour has slots, else its type
		public static int OriginalOf(BiomeSlotMap map, NPC npc) => map.ByOriginal.ContainsKey(npc.netID) ? npc.netID : npc.type;

		// Owners spawning in the Forest slots of the vanilla critical hosts, as types (guaranteed drops are per type)
		public static List<int> CriticalHosts(bool day)
		{
			int[] originals = day ? CriticalDayOriginals : CriticalNightOriginals;
			return Current.Slots.Where(s => s.Critical && originals.Contains(s.Original)).Select(s => TypeOf(s.Owner)).Distinct().OrderBy(t => t).ToList();
		}

		// Modded biome keys that have slots (for the tests)
		public static IEnumerable<int> UsedModdedBiomes()
		{
			Catalog();
			return usedModdedBiomes.OrderBy(b => b);
		}

		// Biomes of a spawn, from the zones of the player it spawned for (synced to the server) and the NPC's tile
		public static List<int> BiomesOf(Player player, NPC npc)
		{
			SpawnZone zones = SpawnZone.None;
			void Set(bool on, SpawnZone zone)
			{
				if (on) zones |= zone;
			}
			Set(player.ZoneDungeon, SpawnZone.Dungeon);
			Set(player.ZoneLihzhardTemple, SpawnZone.Temple);
			Set(InSpiderNest(npc), SpawnZone.SpiderNest);
			Set(player.ZoneGraveyard, SpawnZone.Graveyard);
			Set(player.ZoneMeteor, SpawnZone.Meteor);
			Set(player.ZoneGranite, SpawnZone.Granite);
			Set(player.ZoneMarble, SpawnZone.Marble);
			Set(player.ZoneUnderworldHeight, SpawnZone.Underworld);
			Set(player.ZoneSkyHeight, SpawnZone.Sky);
			Set(player.ZoneDirtLayerHeight, SpawnZone.DirtLayer);
			Set(player.ZoneRockLayerHeight, SpawnZone.RockLayer);
			Set(player.ZoneGlowshroom, SpawnZone.Glowshroom);
			Set(player.ZoneJungle, SpawnZone.Jungle);
			Set(player.ZoneUndergroundDesert, SpawnZone.UndergroundDesert);
			Set(player.ZoneDesert, SpawnZone.Desert);
			Set(player.ZoneSnow, SpawnZone.Snow);
			Set(player.ZoneHallow, SpawnZone.Hallow);
			Set(player.ZoneCorrupt, SpawnZone.Corrupt);
			Set(player.ZoneCrimson, SpawnZone.Crimson);
			Set(player.ZoneBeach, SpawnZone.Beach);
			EnsureTags();
			List<int> modded = new();
			for (int i = 0; i < moddedBiomes.Count; i++)
			{
				if (usedModdedBiomes.Contains(ModdedBiomeStart + i) && player.InModBiome(moddedBiomes[i])) modded.Add(ModdedBiomeStart + i);
			}
			return BiomesFor(zones, modded);
		}

		// Every biome matching a set of zones, in priority order: structures, modded and mini-biomes, then the layer, then the
		// surface biomes (also a fallback underground: their slots are the closest match). A spawn fills the first one where
		// its original has a slot, so a Dark Mummy in a corrupt desert still finds its Desert slot.
		public static List<int> BiomesFor(SpawnZone zones, IEnumerable<int> moddedBiomeKeys)
		{
			List<int> list = new();
			void Add(SpawnZone zone, SpawnBiome biome)
			{
				if ((zones & zone) != 0) list.Add((int)biome);
			}
			Add(SpawnZone.Dungeon, SpawnBiome.Dungeon);
			Add(SpawnZone.Temple, SpawnBiome.Temple);
			list.AddRange(moddedBiomeKeys);
			Add(SpawnZone.SpiderNest, SpawnBiome.SpiderNest);
			Add(SpawnZone.Graveyard, SpawnBiome.Graveyard);
			Add(SpawnZone.Meteor, SpawnBiome.Meteor);
			Add(SpawnZone.Granite, SpawnBiome.Granite);
			Add(SpawnZone.Marble, SpawnBiome.Marble);
			Add(SpawnZone.Underworld, SpawnBiome.Underworld);
			Add(SpawnZone.Sky, SpawnBiome.Sky);
			bool underground = (zones & (SpawnZone.DirtLayer | SpawnZone.RockLayer)) != 0;
			if (underground)
			{
				Add(SpawnZone.Glowshroom, SpawnBiome.UndergroundMushroom);
				Add(SpawnZone.Jungle, SpawnBiome.UndergroundJungle);
				Add(SpawnZone.UndergroundDesert, SpawnBiome.UndergroundDesert);
				Add(SpawnZone.Snow, SpawnBiome.UndergroundSnow);
				Add(SpawnZone.Hallow, SpawnBiome.UndergroundHallow);
				Add(SpawnZone.Corrupt, SpawnBiome.UndergroundCorruption);
				Add(SpawnZone.Crimson, SpawnBiome.UndergroundCrimson);
				Add(SpawnZone.RockLayer, SpawnBiome.Caverns);
				Add(SpawnZone.DirtLayer, SpawnBiome.Underground);
			}
			Add(SpawnZone.Glowshroom, SpawnBiome.Mushroom);
			Add(SpawnZone.Beach, SpawnBiome.Ocean);
			Add(SpawnZone.Desert, SpawnBiome.Desert);
			Add(SpawnZone.Corrupt, SpawnBiome.Corruption);
			Add(SpawnZone.Crimson, SpawnBiome.Crimson);
			Add(SpawnZone.Hallow, SpawnBiome.Hallow);
			Add(SpawnZone.Jungle, SpawnBiome.Jungle);
			Add(SpawnZone.Snow, SpawnBiome.Snow);
			if (!underground && (zones & SpawnZone.Underworld) == 0) list.Add((int)SpawnBiome.Forest);
			return list;
		}

		// Spider nests have no zone flag: vanilla reads the wall behind the spawn tile
		private static bool InSpiderNest(NPC npc)
		{
			int x = (int)(npc.Center.X / 16);
			int y = (int)(npc.Center.Y / 16);
			for (int dy = 0; dy <= 2; dy++)
			{
				if (WorldGen.InWorld(x, y + dy) && Main.tile[x, y + dy].WallType == WallID.SpiderUnsafe) return true;
			}
			return false;
		}

		// Every enemy with a natural spawn biome (deterministic order)
		private static List<Natural> Catalog()
		{
			if (catalog != null) return catalog;
			EnsureTags();
			EnemiesConfig cfg = EnemiesConfig.Instance;
			List<Natural> naturals = new();
			List<Natural> extraList = new();
			raisedToHardmode.Clear();
			List<int> ids = ContentSamples.NpcsByNetId.Keys.Where(id => id > 0 || SpawnedNetIds.Contains(id)).OrderBy(id => id).ToList();
			foreach (int id in ids)
			{
				if (TransformedOnly.Contains(id) || !ContentSamples.NpcsByNetId.TryGetValue(id, out NPC sample)) continue;
				List<IBestiaryInfoElement> info = Main.BestiaryDB?.FindEntryByNPCID(id)?.Info;
				SortedSet<int> biomes = new();
				bool day = false, night = false, conditional = false, eventOnly = false, extraEvent = false, hardmode = false, temple = false;
				if (info != null)
				{
					foreach (IBestiaryInfoElement element in info)
					{
						if (element == null) continue;
						if (biomeTags.TryGetValue(element, out SpawnBiome biome)) biomes.Add((int)biome);
						if (element == Tags.Times.DayTime) day = true;
						if (element == Tags.Times.NightTime) night = true;
						if (conditionalTags.Contains(element)) conditional = true;
						// Antlions and Dune Splicers also come in sandstorms but are regular desert enemies
						if (element == Tags.Events.Sandstorm && SandstormOnly.Contains(sample.type)) conditional = true;
						if (eventTags.Contains(element)) eventOnly = true;
						if (extraEventTags.Contains(element)) extraEvent = true;
						if (hardmodeTags.Contains(element)) hardmode = true;
						if (element == Tags.Biomes.TheTemple) temple = true;
					}
				}
				if (sample.ModNPC?.SpawnModBiomes != null)
				{
					foreach (int modBiome in sample.ModNPC.SpawnModBiomes)
					{
						int index = moddedBiomes.FindIndex(b => b.Type == modBiome);
						if (index >= 0) biomes.Add(ModdedBiomeStart + index);
					}
				}
				// Zombies and demon eyes only have a time tag: they are surface enemies
				if (biomes.Count == 0 && !eventOnly && (day || night || conditional)) biomes.Add((int)SpawnBiome.Forest);
				int type = sample.type;
				int tier = SlotTier(id, hardmode || HardmodeOnly.Contains(type), temple);
				if (HardmodeOnly.Contains(type) && NpcTiers.BaseTier(id) < (int)Tier.Hardmode) raisedToHardmode.Add(id);
				if (biomes.Count == 0)
				{
					if (!extraEvent || id < 0 || ExtraDenied.Contains(type)) continue;
					int? extraMovement = EnemySwapPool.ExtraMovement(type, sample, cfg);
					if (extraMovement == null) continue;
					extraList.Add(new Natural
					{
						Id = id, Biomes = new List<int>(), Movement = extraMovement.Value, Tier = tier, Rare = sample.rarity > 0, Intolerant = EnemySwapPool.DaylightIntolerant(id)
					});
					continue;
				}
				int? movement = EnemySwapPool.SlotMovement(type, sample, cfg);
				if (movement == null) continue;
				naturals.Add(new Natural
				{
					Id = id, Biomes = biomes.ToList(), Movement = movement.Value, Tier = tier,
					Time = day && !night ? SlotTime.Day : night && !day ? SlotTime.Night : SlotTime.Any,
					Rare = sample.rarity > 0 || ExplicitRare.Contains(id),
					// Weather, seasonal and event slots are open only then: they swap with each other
					Conditional = conditional || eventOnly,
					Critter = NPCID.Sets.CountsAsCritter[type],
					Intolerant = EnemySwapPool.DaylightIntolerant(id)
				});
			}
			usedModdedBiomes = naturals.SelectMany(n => n.Biomes).Where(b => b >= ModdedBiomeStart).ToHashSet();
			catalog = naturals;
			extras = extraList;
			return catalog;
		}

		// Enemies without a natural biome that may take slots: event enemies that work outside their event
		private static List<Natural> Extras()
		{
			Catalog();
			return extras;
		}

		// Extra candidates, for the tests and the debug command
		public static IEnumerable<int> ExtraCandidates() => Extras().Select(e => e.Id);

		// Natural enemies and their vanilla slot counts, for the tests
		public static Dictionary<int, int> VanillaSlotCounts() => Catalog().ToDictionary(n => n.Id, n => n.Biomes.Count);

		private static int SlotTier(int id, bool hardmodeBiome, bool temple)
		{
			int tier = NpcTiers.BaseTier(id);
			if (hardmodeBiome) tier = Math.Max(tier, (int)Tier.Hardmode);
			if (temple) tier = Math.Max(tier, (int)Tier.PostPlantera);
			return tier;
		}

		private static void EnsureTags()
		{
			if (biomeTags != null) return;
			biomeTags = new Dictionary<IBestiaryInfoElement, SpawnBiome>
			{
				[Tags.Biomes.Surface] = SpawnBiome.Forest,
				[Tags.Biomes.Snow] = SpawnBiome.Snow,
				[Tags.Biomes.Desert] = SpawnBiome.Desert,
				[Tags.Biomes.Oasis] = SpawnBiome.Desert,
				[Tags.Biomes.CorruptDesert] = SpawnBiome.Desert,
				[Tags.Biomes.CrimsonDesert] = SpawnBiome.Desert,
				[Tags.Biomes.HallowDesert] = SpawnBiome.Desert,
				[Tags.Biomes.Jungle] = SpawnBiome.Jungle,
				[Tags.Biomes.Ocean] = SpawnBiome.Ocean,
				[Tags.Biomes.TheCorruption] = SpawnBiome.Corruption,
				[Tags.Biomes.TheCrimson] = SpawnBiome.Crimson,
				[Tags.Biomes.TheHallow] = SpawnBiome.Hallow,
				[Tags.Biomes.SurfaceMushroom] = SpawnBiome.Mushroom,
				[Tags.Biomes.Graveyard] = SpawnBiome.Graveyard,
				[Tags.Biomes.Meteor] = SpawnBiome.Meteor,
				[Tags.Biomes.Sky] = SpawnBiome.Sky,
				[Tags.Biomes.Underground] = SpawnBiome.Underground,
				[Tags.Biomes.Caverns] = SpawnBiome.Caverns,
				[Tags.Biomes.UndergroundSnow] = SpawnBiome.UndergroundSnow,
				[Tags.Biomes.CorruptIce] = SpawnBiome.UndergroundSnow,
				[Tags.Biomes.CrimsonIce] = SpawnBiome.UndergroundSnow,
				[Tags.Biomes.HallowIce] = SpawnBiome.UndergroundSnow,
				[Tags.Biomes.UndergroundDesert] = SpawnBiome.UndergroundDesert,
				[Tags.Biomes.CorruptUndergroundDesert] = SpawnBiome.UndergroundDesert,
				[Tags.Biomes.CrimsonUndergroundDesert] = SpawnBiome.UndergroundDesert,
				[Tags.Biomes.HallowUndergroundDesert] = SpawnBiome.UndergroundDesert,
				[Tags.Biomes.UndergroundJungle] = SpawnBiome.UndergroundJungle,
				[Tags.Biomes.UndergroundCorruption] = SpawnBiome.UndergroundCorruption,
				[Tags.Biomes.UndergroundCrimson] = SpawnBiome.UndergroundCrimson,
				[Tags.Biomes.UndergroundHallow] = SpawnBiome.UndergroundHallow,
				[Tags.Biomes.UndergroundMushroom] = SpawnBiome.UndergroundMushroom,
				[Tags.Biomes.TheUnderworld] = SpawnBiome.Underworld,
				[Tags.Biomes.TheDungeon] = SpawnBiome.Dungeon,
				[Tags.Biomes.TheTemple] = SpawnBiome.Temple,
				[Tags.Biomes.Granite] = SpawnBiome.Granite,
				[Tags.Biomes.Marble] = SpawnBiome.Marble,
				[Tags.Biomes.SpiderNest] = SpawnBiome.SpiderNest
			};
			// Open only with a weather or a season: such slots count as rare
			conditionalTags = new HashSet<IBestiaryInfoElement>
			{
				Tags.Events.Rain, Tags.Events.Blizzard, Tags.Events.WindyDay, Tags.Events.Halloween, Tags.Events.Christmas, Tags.Events.Party,
				Tags.Events.SlimeRain
			};
			// Only during an event or an invasion: no natural biome
			eventTags = new HashSet<IBestiaryInfoElement>
			{
				Tags.Events.BloodMoon, Tags.Events.Eclipse, Tags.Invasions.Goblins, Tags.Invasions.Pirates, Tags.Invasions.Martian, Tags.Invasions.OldOnesArmy,
				Tags.Invasions.PumpkinMoon, Tags.Invasions.FrostMoon, Tags.Invasions.FrostLegion,
				Tags.Biomes.SolarPillar, Tags.Biomes.VortexPillar, Tags.Biomes.NebulaPillar, Tags.Biomes.StardustPillar
			};
			// Events whose enemies still work elsewhere (the moons, Martians, the Old One's Army and the pillars are scripted)
			extraEventTags = new HashSet<IBestiaryInfoElement>
			{
				Tags.Events.BloodMoon, Tags.Events.Eclipse, Tags.Invasions.Goblins, Tags.Invasions.Pirates, Tags.Invasions.FrostLegion,
				Tags.Events.Rain, Tags.Events.WindyDay, Tags.Events.Halloween, Tags.Events.Christmas
			};
			// Biomes that only exist in hardmode
			hardmodeTags = new HashSet<IBestiaryInfoElement>
			{
				Tags.Biomes.TheHallow, Tags.Biomes.UndergroundHallow, Tags.Biomes.HallowDesert, Tags.Biomes.HallowUndergroundDesert, Tags.Biomes.HallowIce,
				Tags.Biomes.CorruptIce, Tags.Biomes.CrimsonIce, Tags.Biomes.CorruptDesert, Tags.Biomes.CrimsonDesert,
				Tags.Biomes.CorruptUndergroundDesert, Tags.Biomes.CrimsonUndergroundDesert
			};
			moddedBiomes = ModContent.GetContent<ModBiome>().OrderBy(b => b.FullName, StringComparer.Ordinal).ToList();
		}
	}
}

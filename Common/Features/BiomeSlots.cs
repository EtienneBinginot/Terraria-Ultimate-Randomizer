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
		// owners are never more than one tier above it (none in critical slots) nor across the hardmode or Plantera line,
		// so progression stays classic
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
		// Its tier pair had too few owners and was merged with a neighbouring pair (owners still at most one tier above,
		// never across the hardmode or Plantera line)
		public bool Merged;
		// A slot of the evil biome this world doesn't have (Corruption in a Crimson world): its owner spawns through a slot
		// of the present evil instead (EvilShared there)
		public bool AbsentEvil;
		// Owners of absent-evil slots sharing this present-evil slot: each spawn picks one of them EvilShare of the time (null: none)
		public List<int> EvilShared;

		// 0 pre-hardmode, 1 hardmode, 2 post-Plantera (when the slot opens)
		public int Band => BiomeSlots.BandOf(Tier);

		// Tiers go by pairs (0-1, 2-3, 4-5, 6-7, 8-9): bigger classes, and no pair straddles the hardmode or Plantera line
		public int TierGroup => Tier / 2;

		public int ClassKey => Movement * 100 + (Rare ? 50 : 0) + (Conditional ? 20 : 0) + TierGroup;
	}

	// What the planned shares say about a type in one band of a map, beyond its share of each biome
	public struct SpawnInfo
	{
		// Spawns in at least one slot that is not rare (any biome)
		public bool CommonSlot;
	}

	// The planned shares of one band of a map, computed in one pass over its slots
	internal class ShareTable
	{
		// Biome, then type, then share of the biome's enemy spawns
		public readonly Dictionary<int, Dictionary<int, double>> ByBiome = new();
		// The same, from the slots that are neither rare nor conditional only: what is met there on an ordinary day
		public readonly Dictionary<int, Dictionary<int, double>> Steady = new();
		// Times of the slots of a type in a biome: 1 by day, 2 by night (3: both)
		public readonly Dictionary<(int biome, int type), int> Times = new();
		public readonly Dictionary<int, SpawnInfo> ByType = new();
	}

	// The evil biomes of a world: both in drunk (and zenith) worlds, else only the one it was generated with
	public enum WorldEvil { Both, Corruption, Crimson }

	public class BiomeSlotMap
	{
		public readonly List<BiomeSlot> Slots = new();
		public readonly Dictionary<(int biome, int original), BiomeSlot> ByKey = new();
		public readonly Dictionary<int, List<BiomeSlot>> ByOriginal = new();
		public int Classes;
		public int ClassesKeptVanilla;
		public int ExtraTarget;
		public WorldEvil Evil;
		// Slots whose owner also lives in that biome in vanilla (information: the redistribution doesn't forbid it)
		public int SameBiome;
		// Absent-evil slots whose owner got a present-evil slot to share, those whose owner spawns elsewhere instead (its
		// other slots, or a slot of its movement in another biome: the Corruption has no water slot for the Crimson's
		// fish), and those that found nothing at all (the tests: never)
		public int EvilSharedSlots;
		public int EvilElsewhere;
		public int EvilUnshared;
		// Planned spawn shares by band (BiomeSlots.PlannedShare), and the types with any slot at all
		internal readonly Dictionary<int, ShareTable> Shares = new();
		internal HashSet<int> SlotTypes;
		internal Dictionary<int, int> MeetTiers;
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

		// Share of the spawns of a present-evil slot that are one of its absent-evil owners
		public const double EvilShare = 0.5;

		// A class with fewer distinct owners is merged with the class of the tier pair below
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

		// Every kind of zombie and demon eye of the Forest night: vanilla picks one kind at random for each spawn, so each kind
		// has its own Forest slot and the plain Zombie's and Demon Eye's are only a few of them (the armed zombies of expert
		// mode and the torch zombies of the very first nights left aside). The critical stations' night hosts are the enemies
		// of all these slots (GuaranteedSources.CriticalHosts, PacingModel)
		public static readonly int[] ForestNightFamily =
		{
			NPCID.Zombie, NPCID.BaldZombie, NPCID.PincushionZombie, NPCID.SlimedZombie, NPCID.SwampZombie, NPCID.TwiggyZombie, NPCID.FemaleZombie,
			NPCID.DemonEye, NPCID.CataractEye, NPCID.SleepyEye, NPCID.DialatedEye, NPCID.GreenEye, NPCID.PurpleEye
		};

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
				// The world evil is synced with the world data; a map built before it arrived is rebuilt
				if (current != null && current.Evil == CurrentEvil) return current;
				current = Build(SeedSystem.WorldSeed, EnemiesConfig.Instance.BiomeExtraEnemies, CurrentEvil);
				ModContent.GetInstance<UltimateRandomizer>().Logger.Info(Summary(current) + $"; raised to hardmode: {string.Join(", ", raisedToHardmode.Select(Lang.GetNPCNameValue))}");
				return current;
			}
		}

		// Tests: plans with another map as this world's until disposed (the map must be built for this world's evil)
		public static IDisposable WithMap(BiomeSlotMap map)
		{
			BiomeSlotMap saved = current;
			current = map;
			return new Restore(() => current = saved);
		}

		private sealed class Restore : IDisposable
		{
			private Action undo;

			public Restore(Action undo) => this.undo = undo;

			public void Dispose()
			{
				undo?.Invoke();
				undo = null;
			}
		}

		public static WorldEvil CurrentEvil => Main.drunkWorld || Main.zenithWorld ? WorldEvil.Both : WorldGen.crimson ? WorldEvil.Crimson : WorldEvil.Corruption;

		// One line for the log and /rdebug biomes
		public static string Summary(BiomeSlotMap map) => $"Enemies by biome: {map.Slots.Count} slots, {map.Classes} classes, {map.ClassesKeptVanilla} kept vanilla, "
			+ $"{map.Slots.Count(s => s.Owner != s.Original)} changed, same-biome {map.SameBiome}, {map.Slots.Count(s => s.Extra != 0)} shared with extras, "
			+ $"evil {map.Evil}: {map.EvilSharedSlots} absent-evil slots shared, {map.EvilElsewhere} elsewhere ({map.EvilUnshared} without a partner)";

		public static int BandOf(int tier) => tier >= (int)Tier.PostPlantera ? 2 : tier >= (int)Tier.Hardmode ? 1 : 0;

		public static void Reset()
		{
			current = null;
			vanilla = null;
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

		public static BiomeSlotMap Build(int seed, float extraPercent) => Build(seed, extraPercent, CurrentEvil);

		public static BiomeSlotMap Build(int seed, float extraPercent, WorldEvil evil)
		{
			BiomeSlotMap map = new() { Evil = evil };
			List<Natural> naturals = Catalog();
			Dictionary<int, Natural> byId = naturals.ToDictionary(n => n.Id);
			AddSlots(map, naturals);

			bool Intolerant(int id) => byId.TryGetValue(id, out Natural n) ? n.Intolerant : EnemySwapPool.DaylightIntolerant(id);
			// Never an owner more than one tier above the slot, never across the hardmode or Plantera line
			bool Accepts(BiomeSlot slot, int owner)
			{
				int tier = byId[owner].Tier;
				// Critical slots never get a stronger owner: the first work bench must drop from weak enemies
				int above = slot.Critical ? 0 : 1;
				return (!slot.DaySurface || !Intolerant(owner)) && tier <= slot.Tier + above && BandOf(tier) <= slot.Band;
			}

			Dictionary<BiomeSlot, int> groupOf = new();
			foreach ((int groupKey, List<BiomeSlot> slots) in Groups(map))
			{
				foreach (BiomeSlot slot in slots) groupOf[slot] = groupKey;
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
			map.SameBiome = map.Slots.Count(s => byId[s.Owner].Biomes.Contains(s.Biome));
			if (evil != WorldEvil.Both) ShareAbsentEvil(map, seed, Accepts, groupOf);

			int enemySlots = map.Slots.Count(s => !s.Critter);
			map.ExtraTarget = (int)Math.Round(enemySlots * Math.Clamp(extraPercent, 0f, 100f) / 100f);
			if (map.ExtraTarget > 0) PlaceExtras(map, seed);

			foreach (BiomeSlot slot in map.Slots)
			{
				// An absent-evil owner spawns through the present-evil slot it shares (counted there)
				foreach (int id in Spawners(slot).Where(id => !slot.AbsentEvil || id != slot.Owner))
				{
					// Never earlier than its own tier, nor than the slot opens
					int type = TypeOf(id);
					int tier = Math.Max(Math.Max(NpcTiers.BaseTier(type), TierOf(id)), slot.Tier);
					map.SpawnTier[type] = Math.Min(map.SpawnTier.GetValueOrDefault(type, int.MaxValue), tier);
				}
				Index(map, slot);
			}
			return map;
		}

		// Every vanilla slot, owned by its original, in a fixed order
		private static void AddSlots(BiomeSlotMap map, List<Natural> naturals)
		{
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
		}

		private static void Index(BiomeSlotMap map, BiomeSlot slot)
		{
			map.ByKey[(slot.Biome, slot.Original)] = slot;
			if (!map.ByOriginal.TryGetValue(slot.Original, out List<BiomeSlot> list)) map.ByOriginal[slot.Original] = list = new List<BiomeSlot>();
			list.Add(slot);
		}

		// The vanilla spawns of a world: every slot keeps its original, the absent evil's slots never open
		public static BiomeSlotMap BuildVanilla(WorldEvil evil)
		{
			BiomeSlotMap map = new() { Evil = evil };
			AddSlots(map, Catalog());
			Func<int, bool> absent = evil switch
			{
				WorldEvil.Crimson => IsCorruption,
				WorldEvil.Corruption => IsCrimson,
				_ => _ => false
			};
			foreach (BiomeSlot slot in map.Slots)
			{
				slot.AbsentEvil = absent(slot.Biome);
				Index(map, slot);
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

		// Permutation groups: same movement, rarity and condition, same tier pair. A class with fewer than MinClassOwners
		// owners joins the group of the pair below; the lowest one joins the group above.
		private static List<(int key, List<BiomeSlot> slots)> Groups(BiomeSlotMap map)
		{
			List<(int key, List<BiomeSlot> slots)> groups = new();
			foreach (IGrouping<(int Movement, bool Rare, bool Conditional), BiomeSlot> family in map.Slots.GroupBy(s => (s.Movement, s.Rare, s.Conditional))
				.OrderBy(f => f.Key.Movement).ThenBy(f => f.Key.Rare).ThenBy(f => f.Key.Conditional))
			{
				List<List<BiomeSlot>> merged = new();
				foreach (IGrouping<int, BiomeSlot> tier in family.GroupBy(s => s.TierGroup).OrderBy(t => t.Key))
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

		private static bool IsCorruption(int biome) => biome == (int)SpawnBiome.Corruption || biome == (int)SpawnBiome.UndergroundCorruption;

		private static bool IsCrimson(int biome) => biome == (int)SpawnBiome.Crimson || biome == (int)SpawnBiome.UndergroundCrimson;

		// The same layer in the other evil (Corruption <-> Crimson, underground too)
		private static int MirrorEvil(int biome) => (SpawnBiome)biome switch
		{
			SpawnBiome.Corruption => (int)SpawnBiome.Crimson,
			SpawnBiome.Crimson => (int)SpawnBiome.Corruption,
			SpawnBiome.UndergroundCorruption => (int)SpawnBiome.UndergroundCrimson,
			SpawnBiome.UndergroundCrimson => (int)SpawnBiome.UndergroundCorruption,
			_ => -1
		};

		// The slots of the evil this world lacks never open (nobody stands in a Corruption of a Crimson world), so their owners
		// would never appear: each one shares a present-evil slot of its class (same layer first), the least shared first.
		// The present evil may have no slot of its kind at all (the Corruption has no water enemy for the Crimson's Blood
		// Feeder and Blood Jelly, nor for the fish the bigger classes put in their slots): such an owner keeps its other slots
		// if it has some, else it shares an open slot of its movement in another biome (within the tier rule, then only within
		// its band), so no enemy ends up spawning nowhere.
		// The corrupt/crimson desert and ice keys are merged into Desert and the snow caverns, which every world has.
		private static void ShareAbsentEvil(BiomeSlotMap map, int seed, Func<BiomeSlot, int, bool> accepts, Dictionary<BiomeSlot, int> groupOf)
		{
			Func<int, bool> absent = map.Evil == WorldEvil.Crimson ? IsCorruption : IsCrimson;
			Func<int, bool> present = map.Evil == WorldEvil.Crimson ? IsCrimson : IsCorruption;
			foreach (BiomeSlot slot in map.Slots.Where(s => absent(s.Biome))) slot.AbsentEvil = true;
			// Slots that open in this world and may take a shared owner (never a critical one)
			List<BiomeSlot> open = map.Slots.Where(s => !s.AbsentEvil && !s.Critical).ToList();
			List<BiomeSlot> hosts = open.Where(s => present(s.Biome)).ToList();
			Random rng = new(DetRandom.Hash(seed, RollKey.BiomeEvil, (int)map.Evil));
			bool Shares(BiomeSlot h, int owner) => h.Owner == owner || h.EvilShared != null && h.EvilShared.Contains(owner);
			void ShareLeast(List<BiomeSlot> options, int owner)
			{
				int least = options.Min(h => h.EvilShared?.Count ?? 0);
				List<BiomeSlot> best = options.Where(h => (h.EvilShared?.Count ?? 0) == least).ToList();
				BiomeSlot host = best[rng.Next(best.Count)];
				(host.EvilShared ??= new List<int>()).Add(owner);
			}
			// The slots whose owner has a present-evil slot of its kind are shared first: the others only spawn elsewhere
			// if nothing in the present evil took them
			List<(BiomeSlot slot, List<BiomeSlot> options)> absentSlots = new();
			foreach (BiomeSlot slot in map.Slots.Where(s => s.AbsentEvil))
			{
				int owner = slot.Owner;
				List<BiomeSlot> fit = hosts.Where(h => h.Critter == slot.Critter && h.Movement == slot.Movement && accepts(h, owner)).ToList();
				// Same class and layer, same class, same rarity and condition, then only the same movement
				List<BiomeSlot> options = new[]
				{
					fit.Where(h => groupOf[h] == groupOf[slot] && h.Biome == MirrorEvil(slot.Biome)).ToList(),
					fit.Where(h => groupOf[h] == groupOf[slot]).ToList(),
					fit.Where(h => h.Rare == slot.Rare && h.Conditional == slot.Conditional).ToList(),
					fit
				}.FirstOrDefault(list => list.Count > 0);
				absentSlots.Add((slot, options));
			}
			foreach ((BiomeSlot slot, List<BiomeSlot> options) in absentSlots.Where(a => a.options != null))
			{
				map.EvilSharedSlots++;
				// Already reachable through one of them (an owner of two absent slots)
				if (!options.Any(h => Shares(h, slot.Owner))) ShareLeast(options, slot.Owner);
			}
			foreach ((BiomeSlot slot, _) in absentSlots.Where(a => a.options == null))
			{
				int owner = slot.Owner;
				bool Kind(BiomeSlot h) => h.Critter == slot.Critter && h.Movement == slot.Movement;
				// Nothing of its kind in the present evil: already spawning in an open slot (its own, or shared before) that opens
				// no later than this one
				if (map.Slots.Any(s => !s.AbsentEvil && s.Band <= slot.Band && Shares(s, owner)))
				{
					map.EvilElsewhere++;
					continue;
				}
				int tier = TierOf(owner);
				// The present evil's slots of its kind were all too weak for it: only other biomes, opening no later than this
				// slot first
				List<BiomeSlot> all = open.Where(h => Kind(h) && !present(h.Biome)).ToList();
				List<BiomeSlot> Choose(List<BiomeSlot> kind)
				{
					List<BiomeSlot> strict = kind.Where(h => accepts(h, owner)).ToList();
					// Within the tier rule (same class, then same rarity and condition, then any), else only within its band
					// (never across the hardmode or Plantera line, never a daylight-intolerant owner in a day surface slot)
					return new[]
					{
						strict.Where(h => groupOf[h] == groupOf[slot]).ToList(),
						strict.Where(h => h.Rare == slot.Rare && h.Conditional == slot.Conditional).ToList(),
						strict,
						kind.Where(h => BandOf(tier) <= h.Band && h.Tier >= slot.Tier && (!h.DaySurface || !EnemySwapPool.DaylightIntolerant(owner))).ToList(),
						kind.Where(h => BandOf(tier) <= h.Band && (!h.DaySurface || !EnemySwapPool.DaylightIntolerant(owner))).ToList()
					}.FirstOrDefault(list => list.Count > 0);
				}
				List<BiomeSlot> elsewhere = Choose(all.Where(h => h.Band <= slot.Band).ToList()) ?? Choose(all);
				if (elsewhere == null)
				{
					map.EvilUnshared++;
					continue;
				}
				map.EvilElsewhere++;
				ShareLeast(elsewhere, owner);
			}
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
		// critter, critical or conditional slot (it should come outside events), never a day surface slot if it flees daylight;
		// never an evil slot that never opens or is already shared with the absent evil
		private static bool Fits(BiomeSlot slot, Natural extra, bool sameTier) =>
			!slot.Critter && slot.Extra == 0 && !slot.Critical && !slot.Conditional && !slot.AbsentEvil && slot.EvilShared == null
			&& slot.Movement == extra.Movement && slot.Rare == extra.Rare
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

		// Who spawns in a slot: its owner, half of the time its extra, and in a present-evil slot shared with absent-evil
		// owners, one of them half of the time (roll in [0, 1))
		public static int Pick(BiomeSlot slot, double roll)
		{
			if (slot.EvilShared is { Count: > 0 } shared)
			{
				if (roll < EvilShare) return shared[Math.Min(shared.Count - 1, (int)(roll / EvilShare * shared.Count))];
				roll = (roll - EvilShare) / (1 - EvilShare);
			}
			return slot.Extra != 0 && roll < ExtraShare ? slot.Extra : slot.Owner;
		}

		// Everyone Pick can return for a slot (owner first)
		public static IEnumerable<int> Spawners(BiomeSlot slot)
		{
			yield return slot.Owner;
			if (slot.Extra != 0) yield return slot.Extra;
			if (slot.EvilShared == null) yield break;
			foreach (int id in slot.EvilShared) yield return id;
		}

		// How Pick splits the spawns of a slot: the absent-evil owners it shares, then its extra, then its owner
		public static IEnumerable<(int id, double part)> SpawnParts(BiomeSlot slot)
		{
			double rest = 1;
			if (slot.EvilShared is { Count: > 0 } shared)
			{
				foreach (int id in shared) yield return (id, EvilShare / shared.Count);
				rest = 1 - EvilShare;
			}
			if (slot.Extra != 0)
			{
				yield return (slot.Extra, rest * ExtraShare);
				rest *= 1 - ExtraShare;
			}
			yield return (slot.Owner, rest);
		}

		// ---- Planned spawn shares ----

		// Weight of a slot among the spawns of its biome: rare slots seldom fill, conditional ones only with their weather,
		// season or event
		public const double RareSlotWeight = 0.1;
		public const double ConditionalSlotWeight = 0.5;

		private static BiomeSlotMap vanilla;

		// The vanilla spawns of this world (owner = original)
		public static BiomeSlotMap Vanilla
		{
			get
			{
				if (vanilla == null || vanilla.Evil != CurrentEvil) vanilla = BuildVanilla(CurrentEvil);
				return vanilla;
			}
		}

		public static double SlotWeight(BiomeSlot slot) => (slot.Rare ? RareSlotWeight : 1) * (slot.Conditional ? ConditionalSlotWeight : 1);

		// The map spawns follow: the redistributed one, else the vanilla one
		public static BiomeSlotMap SpawnMap => Active ? Current : Vanilla;

		// Slots counted among the spawns of a band (0 pre-hardmode, 1 hardmode, 2 post-Plantera): open by then, in this
		// world (not the absent evil), enemies only
		private static bool Counted(BiomeSlot slot, int band) => !slot.Critter && !slot.AbsentEvil && slot.Band <= band;

		private static SlotTime TimeOf(int flags) => flags == 1 ? SlotTime.Day : flags == 2 ? SlotTime.Night : SlotTime.Any;

		private static ShareTable Table(BiomeSlotMap map, int band)
		{
			if (map.Shares.TryGetValue(band, out ShareTable cached)) return cached;
			ShareTable table = new();
			Dictionary<int, double> totals = new();
			HashSet<int> common = new();
			foreach (BiomeSlot slot in map.Slots)
			{
				if (!Counted(slot, band)) continue;
				double weight = SlotWeight(slot);
				bool steady = !slot.Rare && !slot.Conditional;
				totals[slot.Biome] = totals.GetValueOrDefault(slot.Biome) + weight;
				if (!table.ByBiome.TryGetValue(slot.Biome, out Dictionary<int, double> byType)) table.ByBiome[slot.Biome] = byType = new Dictionary<int, double>();
				if (!table.Steady.TryGetValue(slot.Biome, out Dictionary<int, double> steadyByType)) table.Steady[slot.Biome] = steadyByType = new Dictionary<int, double>();
				int flags = slot.Time == SlotTime.Day ? 1 : slot.Time == SlotTime.Night ? 2 : 3;
				void Add(int id, double part)
				{
					int type = TypeOf(id);
					byType[type] = byType.GetValueOrDefault(type) + weight * part;
					if (steady) steadyByType[type] = steadyByType.GetValueOrDefault(type) + weight * part;
					table.Times[(slot.Biome, type)] = table.Times.GetValueOrDefault((slot.Biome, type)) | flags;
					if (!slot.Rare) common.Add(type);
				}
				// Split as Pick splits it: the absent-evil owners it shares, then its extra, then its owner
				double rest = 1;
				if (slot.EvilShared is { Count: > 0 } shared)
				{
					for (int k = 0; k < shared.Count; k++) Add(shared[k], EvilShare / shared.Count);
					rest = 1 - EvilShare;
				}
				if (slot.Extra != 0)
				{
					Add(slot.Extra, rest * ExtraShare);
					rest *= 1 - ExtraShare;
				}
				Add(slot.Owner, rest);
			}
			foreach ((int biome, Dictionary<int, double> byType) in table.ByBiome)
			{
				double total = totals[biome];
				Dictionary<int, double> steadyByType = table.Steady[biome];
				foreach (int type in byType.Keys.ToList()) byType[type] /= total;
				foreach (int type in steadyByType.Keys.ToList()) steadyByType[type] /= total;
				foreach (int type in byType.Keys) table.ByType[type] = new SpawnInfo { CommonSlot = common.Contains(type) };
			}
			map.Shares[band] = table;
			return table;
		}

		private static double ShareIn(BiomeSlotMap map, int band, int biome, int type, bool steady = false) =>
			(steady ? Table(map, band).Steady : Table(map, band).ByBiome).TryGetValue(biome, out Dictionary<int, double> byType) && byType.TryGetValue(type, out double share) ? share : 0;

		private static double VanillaKeep => Active ? Math.Clamp(EnemiesConfig.Instance.BiomeVanillaChance / 100.0, 0, 1) : 0;

		// Are the planned shares where enemies really spawn? Yes when spawns are redistributed by biome or not randomized;
		// other swap modes put any enemy anywhere: hints and guarantees then judge enemies by their rarity only
		public static bool SpawnsPlanned => Active || !EnemiesConfig.Instance.RandomizeEnemySpawns;

		private static double Mixed(int biome, int type, int tier, bool steady)
		{
			int band = BandOf(Tiers.Clamp(tier));
			if (!Active) return ShareIn(Vanilla, band, biome, type, steady);
			double keep = VanillaKeep;
			double share = (1 - keep) * ShareIn(Current, band, biome, type, steady);
			return keep > 0 ? share + keep * ShareIn(Vanilla, band, biome, type, steady) : share;
		}

		// Expected share of the enemy spawns of a biome that are this type (slime colours are Blue Slimes), with the slots open
		// at a tier: each slot weighs 1 (rare 0.1, conditional 0.5), split as Pick splits it. With the spawns redistributed by
		// biome, the vanilla share of BiomeVanillaChance is mixed in; otherwise the vanilla slots are used. Same on every
		// machine (seed, synced settings and world evil).
		public static double PlannedShare(int biome, int type, int tier = Tiers.Max) => Mixed(biome, type, tier, false);

		// The part of that share that comes from slots neither rare nor conditional (no weather, season or event needed)
		public static double SteadyShare(int biome, int type, int tier = Tiers.Max) => Mixed(biome, type, tier, true);

		// Every biome where a type spawns at a tier, the highest steady share first (then the whole share), with the time of
		// its slots there. The share given is the whole one
		public static List<(int biome, double share, SlotTime time)> PlannedBiomes(int type, int tier = Tiers.Max)
		{
			ShareTable table = Table(SpawnMap, BandOf(Tiers.Clamp(tier)));
			List<(int biome, double share, double steady, SlotTime time)> result = new();
			foreach ((int biome, Dictionary<int, double> byType) in table.ByBiome)
			{
				if (!byType.ContainsKey(type)) continue;
				double share = PlannedShare(biome, type, tier);
				if (share > 0) result.Add((biome, share, SteadyShare(biome, type, tier), TimeOf(table.Times[(biome, type)])));
			}
			return result.OrderByDescending(b => b.steady).ThenByDescending(b => b.share).ThenBy(b => b.biome).Select(b => (b.biome, b.share, b.time)).ToList();
		}

		// A biome holding many enemies gives each a small share (25 enemies in the Forest: 4 % each): an enemy counts as met
		// often there with half the average share of the biome's enemy types, or the cap when that is lower
		public const double RelativeFrequentShare = 0.5;

		// Enemy types by (map, vanilla mixed in, band, biome): the maps are rebuilt when the seed or settings change
		private static readonly Dictionary<(BiomeSlotMap map, bool mixed, int band, int biome), int> typeCounts = new();

		public static double FrequentShare(int biome, int tier, double cap)
		{
			int count = TypeCount(biome, BandOf(Tiers.Clamp(tier)));
			return count == 0 ? cap : Math.Min(cap, RelativeFrequentShare / count);
		}

		private static int TypeCount(int biome, int band)
		{
			BiomeSlotMap map = Active ? Current : Vanilla;
			bool mixed = Active && VanillaKeep > 0;
			if (typeCounts.TryGetValue((map, mixed, band, biome), out int cached)) return cached;
			if (typeCounts.Count > 4000) typeCounts.Clear();
			HashSet<int> types = new();
			void From(BiomeSlotMap map)
			{
				if (Table(map, band).ByBiome.TryGetValue(biome, out Dictionary<int, double> byType)) types.UnionWith(byType.Keys);
			}
			From(map);
			if (mixed) From(Vanilla);
			typeCounts[(map, mixed, band, biome)] = types.Count;
			return types.Count;
		}

		// The biome where a type is met the most at a tier, steadily first (biome -1: it spawns nowhere)
		public static (int biome, double share, SlotTime time) BestBiome(int type, int tier = Tiers.Max)
		{
			List<(int biome, double share, SlotTime time)> biomes = PlannedBiomes(type, tier);
			return biomes.Count > 0 ? biomes[0] : (-1, 0, SlotTime.Any);
		}

		// Every type's share of the enemy spawns of a biome in a map (no vanilla mix), for the tests
		public static Dictionary<int, double> SharesIn(BiomeSlotMap map, int biome, int tier = Tiers.Max) =>
			Table(map, BandOf(Tiers.Clamp(tier))).ByBiome.TryGetValue(biome, out Dictionary<int, double> byType) ? new Dictionary<int, double>(byType) : new Dictionary<int, double>();

		// Does a type spawn in at least one slot that is not rare, at a tier?
		public static bool SpawnsInCommonSlot(int type, int tier = Tiers.Max) =>
			Table(SpawnMap, BandOf(Tiers.Clamp(tier))).ByType.TryGetValue(type, out SpawnInfo info) && info.CommonSlot;

		// Does a type have any slot at all (as an original, owner, extra or shared owner, even in the absent evil)? Enemies
		// without any (modded ones without a bestiary biome) are judged by their bestiary rarity only
		public static bool HasAnySlot(int type)
		{
			BiomeSlotMap map = SpawnMap;
			if (map.SlotTypes == null)
			{
				HashSet<int> types = new();
				foreach (BiomeSlot slot in map.Slots)
				{
					types.Add(TypeOf(slot.Original));
					types.Add(TypeOf(slot.Owner));
					if (slot.Extra != 0) types.Add(TypeOf(slot.Extra));
					if (slot.EvilShared != null) types.UnionWith(slot.EvilShared.Select(TypeOf));
				}
				map.SlotTypes = types;
			}
			return map.SlotTypes.Contains(type);
		}

		// Has slots, but none that ever opens in this world (only in the absent evil): never met
		public static bool NeverSpawns(int type) => HasAnySlot(type) && !Table(SpawnMap, 2).ByType.ContainsKey(type);

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

		// Enemies spawning in the Forest slots of the day slimes or of the night zombies and demon eyes (ForestNightFamily) in a
		// map, as types (guaranteed drops are per type): owners and extras (event enemies, never hosts, are filtered by the
		// caller)
		public static List<int> ForestFamilyHosts(BiomeSlotMap map, bool day)
		{
			int[] originals = day ? CriticalDayOriginals : ForestNightFamily;
			return map.Slots.Where(s => s.Biome == (int)SpawnBiome.Forest && originals.Contains(s.Original) && !s.AbsentEvil).SelectMany(Spawners)
				.Select(TypeOf).Distinct().OrderBy(t => t).ToList();
		}

		// The tier from which a type is met in a map's slots (-1: in none). Like SpawnTier (its own tier, never below the
		// slot's), except in the starting Forest, where every enemy of the first band is met from the first day or night: one
		// tier below its own there (a Demon Eye, tier 1 for its 18 damage, is a first-night enemy)
		public static int MeetTier(int type)
		{
			int tier = MeetTiers(SpawnMap).GetValueOrDefault(type, -1);
			if (Active && VanillaKeep > 0)
			{
				int vanillaTier = MeetTiers(Vanilla).GetValueOrDefault(type, -1);
				if (vanillaTier >= 0 && (tier < 0 || vanillaTier < tier)) tier = vanillaTier;
			}
			return tier;
		}

		private static Dictionary<int, int> MeetTiers(BiomeSlotMap map)
		{
			if (map.MeetTiers != null) return map.MeetTiers;
			Dictionary<int, int> result = new();
			foreach (BiomeSlot slot in map.Slots)
			{
				if (slot.Critter || slot.AbsentEvil) continue;
				bool forest = slot.Biome == (int)SpawnBiome.Forest && slot.Band == 0;
				foreach (int id in Spawners(slot))
				{
					int type = TypeOf(id);
					int own = Math.Max(NpcTiers.BaseTier(type), TierOf(id));
					int tier = forest ? Math.Max(0, own - 1) : Math.Max(own, slot.Tier);
					if (!result.TryGetValue(type, out int old) || tier < old) result[type] = tier;
				}
			}
			map.MeetTiers = result;
			return result;
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

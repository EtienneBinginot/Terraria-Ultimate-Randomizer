using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Codex.Interface
{
	// A short note under or after a source in the codex ("night", "rain or event", "condition"), with the full sentences on
	// hover. Empty when the source always works
	public readonly struct CodexNote
	{
		public readonly string Short;
		public readonly string Full;

		public CodexNote(string shortText, string full)
		{
			Short = shortText ?? "";
			Full = full ?? "";
		}

		public bool IsEmpty => string.IsNullOrEmpty(Short);

		public static readonly CodexNote None = new("", "");

		public static CodexNote Join(CodexNote a, CodexNote b) => a.IsEmpty ? b : b.IsEmpty ? a
			: new CodexNote(a.Short + ", " + b.Short, (a.Full + " " + b.Full).Trim());
	}

	// When the sources the codex lists work: the time an enemy is met (in a biome, or anywhere), a weather or event its
	// slots need, the conditions of a planned drop. Read from the planned data (spawn map, drop plans, the same on every
	// machine) with HintFacts' texts, so the codex says what the hints say
	public static class CodexWhen
	{
		public static readonly Color NoteColor = new(150, 200, 255);

		private static readonly Dictionary<(int bag, int item), CodexNote> bagNotes = new();
		private static readonly Dictionary<(int npc, int item), CodexNote> dropNotes = new();
		private static long version = -1;
		private static bool expert;

		// The caches follow the codex structure (seed, settings, tier) and the difficulty
		private static void Follow()
		{
			if (version == CodexSystem.StructureVersion && expert == Main.expertMode) return;
			bagNotes.Clear();
			dropNotes.Clear();
			version = CodexSystem.StructureVersion;
			expert = Main.expertMode;
		}

		// The biome key of a sighting biome name (BiomeSlots.BiomeName), -1 when the spawn map has none
		private static int BiomeKey(string biome)
		{
			if (string.IsNullOrEmpty(biome)) return -1;
			if (!biome.Contains('/') && Enum.TryParse(biome, out SpawnBiome vanilla)) return (int)vanilla;
			foreach (int key in BiomeSlots.UsedModdedBiomes())
			{
				if (BiomeSlots.BiomeName(key) == biome) return key;
			}
			return -1;
		}

		// The slots an enemy type spawns through (the vanilla ones mixed in by biome too), in one biome or all (-1)
		private static List<BiomeSlot> SlotsOf(int type, int biome)
		{
			IEnumerable<BiomeSlot> slots = BiomeSlots.SpawnMap.Slots;
			if (BiomeSlots.Active && EnemiesConfig.Instance.BiomeVanillaChance > 0) slots = slots.Concat(BiomeSlots.Vanilla.Slots);
			return slots.Where(s => !s.Critter && (biome < 0 || s.Biome == biome) && HintFacts.SpawnsIn(s, type)).ToList();
		}

		// When an enemy is met through these slots: the time of its steady slots (all the same, else any hour), and whether
		// it needs a weather, a season or an event (no steady slot at all)
		public static (SlotTime time, bool conditional) TimeOf(List<BiomeSlot> slots)
		{
			if (slots.Count == 0) return (SlotTime.Any, false);
			List<BiomeSlot> steady = slots.Where(s => !s.Rare && !s.Conditional).ToList();
			bool conditional = steady.Count == 0 && slots.Any(s => s.Conditional);
			List<SlotTime> times = (steady.Count > 0 ? steady : slots).Select(s => s.Time).Distinct().ToList();
			return (times.Count == 1 ? times[0] : SlotTime.Any, conditional);
		}

		private static CodexNote TimeNote(SlotTime time, bool conditional)
		{
			CodexNote note = CodexNote.None;
			if (time != SlotTime.Any)
			{
				note = new CodexNote(CodexText.Get("Hint.When.Short." + time), CodexText.Get("Hint.When.Time", CodexText.Get("Hint.Time." + time)));
			}
			if (conditional) note = CodexNote.Join(note, new CodexNote(CodexText.Get("Hint.When.Short.Conditional"), CodexText.Get("Hint.When.Conditional")));
			return note;
		}

		// When an enemy is met in a biome of the sightings
		public static CodexNote Spawn(string biome, int netId)
		{
			if (!BiomeSlots.SpawnsPlanned) return CodexNote.None;
			int key = BiomeKey(biome);
			if (key < 0) return CodexNote.None;
			(SlotTime time, bool conditional) = TimeOf(SlotsOf(BiomeSlots.TypeOf(netId), key));
			return TimeNote(time, conditional);
		}

		// When an enemy is met at all (every biome together): zombies only at night...
		public static CodexNote Enemy(int netId)
		{
			if (!BiomeSlots.SpawnsPlanned) return CodexNote.None;
			(SlotTime time, bool conditional) = TimeOf(SlotsOf(BiomeSlots.TypeOf(netId), -1));
			return TimeNote(time, conditional);
		}

		private static CodexNote ConditionNote(List<string> conditions) => conditions.Count == 0 ? CodexNote.None
			: new CodexNote(CodexText.Get("WhenCondition"), CodexText.Get("Hint.When.Drop", string.Join("; ", conditions)));

		// What an enemy's planned drop of an item needs (a windy day, an event...), once its drops are all unlocked
		public static CodexNote DropConditions(int netId, int item)
		{
			Follow();
			if (dropNotes.TryGetValue((netId, item), out CodexNote cached)) return cached;
			CodexNote note = ConditionNote(HintFacts.PlanConditions(CodexStats.LuckyPlan(netId), item));
			if (dropNotes.Count > 2000) dropNotes.Clear();
			dropNotes[(netId, item)] = note;
			return note;
		}

		// An enemy as a source of an item: when it is met, and what its drop needs
		public static CodexNote Drop(int netId, int item) => CodexNote.Join(Enemy(netId), DropConditions(netId, item));

		// What a bag's planned content needs to give an item
		public static CodexNote Bag(int bag, int item)
		{
			Follow();
			if (bagNotes.TryGetValue((bag, item), out CodexNote cached)) return cached;
			CodexNote note = ConditionNote(HintFacts.PlanConditions(BagPlan.Plan(bag, lucky: true), item));
			if (bagNotes.Count > 2000) bagNotes.Clear();
			bagNotes[(bag, item)] = note;
			return note;
		}

		// Where a recipe must be made (near water, in the snow, in a graveyard...)
		public static CodexNote Recipe(int recipe)
		{
			string text = HintFacts.RecipeConditions(recipe);
			return text.Length == 0 ? CodexNote.None : new CodexNote(text, CodexText.Get("WhenRecipe", text));
		}
	}
}

using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Tests
{
	public static class BiomeTests
	{
		private const int Seeds = 20;
		private const float DefaultExtras = 8f;

		public static void Register()
		{
			TestRunner.Register("BiomeRedistributionComplete", BiomeRedistributionComplete);
			TestRunner.Register("BiomeSlotsRespectDaylight", BiomeSlotsRespectDaylight);
			TestRunner.Register("BiomeExtrasShare", BiomeExtrasShare);
			TestRunner.Register("CriticalHostsInForest", CriticalHostsInForest);
			TestRunner.Register("BiomeSlotsReachable", BiomeSlotsReachable);
			TestRunner.Register("NegativeNetIdSwap", NegativeNetIdSwap);
		}

		private static string Name(int id) => Lang.GetNPCNameValue(id);

		private static void Log(string message) => ModContent.GetInstance<UltimateRandomizer>().Logger.Info(message);

		private static int Seed(int i) => 1000 + i * 7919;

		// Every natural enemy keeps exactly its vanilla number of slots (at least one), with or without extras. Owners only
		// move inside their class (movement, rarity, condition, critter) and keep their tier; in a merged class (too few
		// owners in a tier) an owner may be one tier above the slot or any tier below, never across the hardmode line.
		// The big tier 0 ground class (most Forest slots) is never left vanilla.
		private static void BiomeRedistributionComplete()
		{
			Dictionary<int, int> vanilla = BiomeSlots.VanillaSlotCounts();
			if (vanilla.Count == 0) Check.Skip("no natural enemy has a bestiary biome");
			List<string> failures = new();
			int changedSeeds = 0;
			foreach (int id in new[] { NPCID.BlueSlime, NPCID.Zombie, NPCID.DemonEye, NPCID.Pinky, NPCID.Tim, NPCID.LostGirl, NPCID.DoctorBones })
			{
				if (!vanilla.ContainsKey(id)) failures.Add($"{Name(id)} is not a natural enemy");
			}
			for (int i = 0; i < Seeds; i++)
			{
				int seed = Seed(i);
				BiomeSlotMap map = BiomeSlots.Build(seed, 0f);
				// The class and tier of each enemy, from its own vanilla slots
				Dictionary<int, (int movement, bool rare, bool conditional, bool critter, int tier)> classOf = new();
				foreach (BiomeSlot slot in map.Slots) classOf[slot.Original] = (slot.Movement, slot.Rare, slot.Conditional, slot.Critter, slot.Tier);
				Dictionary<int, int> counts = new();
				foreach (BiomeSlot slot in map.Slots) counts[slot.Owner] = counts.GetValueOrDefault(slot.Owner) + 1;
				foreach ((int id, int count) in vanilla)
				{
					if (count < 1) failures.Add($"seed {seed}: {Name(id)} has no vanilla slot");
					if (counts.GetValueOrDefault(id) != count) failures.Add($"seed {seed}: {Name(id)} has {counts.GetValueOrDefault(id)} slots instead of {count}");
				}
				foreach (BiomeSlot slot in map.Slots)
				{
					if (!classOf.TryGetValue(slot.Owner, out var owner) || owner.movement != slot.Movement || owner.rare != slot.Rare
						|| owner.conditional != slot.Conditional || owner.critter != slot.Critter)
					{
						failures.Add($"seed {seed}: {Name(slot.Owner)} in the {BiomeSlots.BiomeName(slot.Biome)} slot of {Name(slot.Original)} (another class)");
					}
					else if (owner.tier != slot.Tier && (!slot.Merged || owner.tier > slot.Tier + 1 || BiomeSlots.BandOf(owner.tier) > slot.Band
						|| slot.Critical && owner.tier > slot.Tier))
					{
						failures.Add($"seed {seed}: {Name(slot.Owner)} (tier {owner.tier}) in the tier {slot.Tier} slot of {Name(slot.Original)}{(slot.Merged ? " (merged)" : "")}");
					}
				}
				List<BiomeSlot> forestGround = map.Slots.Where(s => s.Biome == (int)SpawnBiome.Forest && s.Movement == 0 && s.Tier == 0 && !s.Rare && !s.Conditional && !s.Critter).ToList();
				if (forestGround.Count > 1 && forestGround.All(s => s.Owner == s.Original)) failures.Add($"seed {seed}: the tier 0 ground enemies of the Forest stay vanilla");
				if (map.Slots.Any(s => s.Owner != s.Original)) changedSeeds++;
				// Same seed, same map
				BiomeSlotMap again = BiomeSlots.Build(seed, 0f);
				if (!again.Slots.Select(s => s.Owner).SequenceEqual(map.Slots.Select(s => s.Owner))) failures.Add($"seed {seed}: the map is not deterministic");

				// Extras only share slots: the owners stay the same
				BiomeSlotMap withExtras = BiomeSlots.Build(seed, DefaultExtras);
				if (!withExtras.Slots.Select(s => s.Owner).SequenceEqual(map.Slots.Select(s => s.Owner))) failures.Add($"seed {seed}: extras changed the owners");
				if (i == 0) Log($"Biome classes: {map.Classes}, {map.ClassesKeptVanilla} kept vanilla, {map.Slots.Count(s => s.Merged)} merged slots, {map.Slots.Count(s => s.Owner != s.Original)}/{map.Slots.Count} slots changed");
			}
			Check.That(changedSeeds == Seeds, $"only {changedSeeds}/{Seeds} seeds change any slot");
			Check.All(failures, "Enemies lost or moved across classes");
		}

		// No surface slot open by day holds an enemy that flees daylight or dies in it (the Gnome)
		private static void BiomeSlotsRespectDaylight()
		{
			List<string> failures = new();
			Check.That(EnemySwapPool.DaylightIntolerant(NPCID.Gnome), "the Gnome is not daylight-intolerant");
			Check.That(EnemySwapPool.DaylightIntolerant(NPCID.Zombie), "the Zombie is not daylight-intolerant");
			// Legacy modes: the day pool has no Gnome either
			if (EnemySwapPool.DaylightEnemies().Contains(NPCID.Gnome)) failures.Add("the Gnome is in the day pool of the per type / per spawn modes");
			int daySlots = 0;
			for (int i = 0; i < Seeds; i++)
			{
				BiomeSlotMap map = BiomeSlots.Build(Seed(i), DefaultExtras);
				foreach (BiomeSlot slot in map.Slots.Where(s => s.DaySurface))
				{
					daySlots++;
					if (EnemySwapPool.DaylightIntolerant(slot.Owner)) failures.Add($"{Name(slot.Owner)} in the {BiomeSlots.BiomeName(slot.Biome)} day slot of {Name(slot.Original)}");
				}
			}
			Check.That(daySlots > 0, "no day surface slot at all");
			Check.All(failures, "Daylight-intolerant enemies in day slots");
		}

		// BiomeExtraEnemies % of the enemy slots are shared with an extra (of a tier at most the slot's), none without the setting
		private static void BiomeExtrasShare()
		{
			if (!BiomeSlots.ExtraCandidates().Any()) Check.Skip("no event enemy can be an extra");
			List<string> failures = new();
			double total = 0;
			for (int i = 0; i < Seeds; i++)
			{
				int seed = Seed(i);
				if (BiomeSlots.Build(seed, 0f).Slots.Any(s => s.Extra != 0)) failures.Add($"seed {seed}: extras without the setting");
				BiomeSlotMap map = BiomeSlots.Build(seed, DefaultExtras);
				int enemySlots = map.Slots.Count(s => !s.Critter);
				int extras = map.Slots.Count(s => s.Extra != 0);
				double share = enemySlots == 0 ? 0 : 100.0 * extras / enemySlots;
				total += share;
				if (map.Slots.Any(s => s.Extra != 0 && (s.Critter || s.Critical))) failures.Add($"seed {seed}: an extra in a critter or critical slot");
				foreach (BiomeSlot slot in map.Slots.Where(s => s.Extra != 0 && BiomeSlots.TierOf(s.Extra) > s.Tier))
				{
					failures.Add($"seed {seed}: {Name(slot.Extra)} (tier {BiomeSlots.TierOf(slot.Extra)}) shares a tier {slot.Tier} slot");
				}
				if (extras != map.ExtraTarget) failures.Add($"seed {seed}: {extras} shared slots for a target of {map.ExtraTarget} ({BiomeSlots.FreeExtraPlaces(map)} free places)");
				if (System.Math.Abs(share - DefaultExtras) > 1) failures.Add($"seed {seed}: {share:0.0}% shared slots, expected {DefaultExtras}%");
			}
			Log($"Biome extras: {total / Seeds:0.0}% of the enemy slots on average (setting {DefaultExtras}%)");
			Check.All(failures, "Extra share");
		}

		// The critical stations (work bench...) drop from enemies that really spawn in the early Forest slots
		private static void CriticalHostsInForest()
		{
			if (!BiomeSlots.Active) Check.Skip("enemies are not redistributed by biome");
			List<GuaranteedSources.Requirement> critical = GuaranteedSources.Requirements.Where(r => r.Critical).ToList();
			if (critical.Count == 0) Check.Skip("no critical station is randomized");
			BiomeSlotMap map = BiomeSlots.Current;
			List<string> failures = new();
			HashSet<int> day = map.Slots.Where(s => s.Critical && s.Time != SlotTime.Night).Select(s => BiomeSlots.TypeOf(s.Owner)).ToHashSet();
			HashSet<int> night = map.Slots.Where(s => s.Critical && s.Time == SlotTime.Night).Select(s => BiomeSlots.TypeOf(s.Owner)).ToHashSet();
			if (day.Count == 0 || night.Count == 0) failures.Add("no critical Forest slot by day or by night");
			if (map.Slots.Any(s => s.Critical && s.Extra != 0)) failures.Add("a critical Forest slot is shared with an extra");
			foreach (GuaranteedSources.Requirement requirement in critical)
			{
				List<int> hosts = GuaranteedSources.HostsOf(requirement.Item).ToList();
				if (!hosts.Any(day.Contains)) failures.Add($"{Lang.GetItemNameValue(requirement.Item)}: no host spawns in the Forest by day ({string.Join(", ", hosts.Select(Name))})");
				if (!hosts.Any(night.Contains)) failures.Add($"{Lang.GetItemNameValue(requirement.Item)}: no host spawns in the Forest by night ({string.Join(", ", hosts.Select(Name))})");
			}
			Check.All(failures, "Critical stations on enemies outside the Forest");
		}

		// Every slot of an enemy with several slots is reached by some zone combination (simulated zones, not a player):
		// the lookup takes the first matching biome where the original has a slot
		private static void BiomeSlotsReachable()
		{
			BiomeSlotMap map = BiomeSlots.Build(Seed(0), 0f);
			SpawnZone[] layers = { SpawnZone.None, SpawnZone.DirtLayer, SpawnZone.RockLayer, SpawnZone.Underworld, SpawnZone.Sky };
			SpawnZone[] surface = { SpawnZone.Glowshroom, SpawnZone.Jungle, SpawnZone.UndergroundDesert, SpawnZone.Desert, SpawnZone.Snow, SpawnZone.Hallow, SpawnZone.Corrupt, SpawnZone.Crimson, SpawnZone.Beach };
			SpawnZone[] minis = { SpawnZone.None, SpawnZone.Dungeon, SpawnZone.Temple, SpawnZone.SpiderNest, SpawnZone.Graveyard, SpawnZone.Meteor, SpawnZone.Granite, SpawnZone.Marble };
			List<SpawnZone> biomeSets = new() { SpawnZone.None };
			for (int a = 0; a < surface.Length; a++)
			{
				biomeSets.Add(surface[a]);
				for (int b = a + 1; b < surface.Length; b++) biomeSets.Add(surface[a] | surface[b]);
			}
			List<List<int>> moddedSets = new() { new List<int>() };
			moddedSets.AddRange(BiomeSlots.UsedModdedBiomes().Select(b => new List<int> { b }));
			List<List<int>> lookups = new();
			foreach (SpawnZone layer in layers)
			{
				foreach (SpawnZone biomes in biomeSets)
				{
					foreach (SpawnZone mini in minis)
					{
						foreach (List<int> modded in moddedSets) lookups.Add(BiomeSlots.BiomesFor(layer | biomes | mini, modded));
					}
				}
			}
			List<string> failures = new();
			foreach ((int original, List<BiomeSlot> slots) in map.ByOriginal.Where(pair => pair.Value.Count >= 2))
			{
				HashSet<int> own = slots.Select(s => s.Biome).ToHashSet();
				HashSet<int> reached = new();
				foreach (List<int> lookup in lookups)
				{
					int first = lookup.FirstOrDefault(own.Contains, -1);
					if (first >= 0) reached.Add(first);
				}
				foreach (int biome in own.Where(b => !reached.Contains(b))) failures.Add($"{Name(original)} in {BiomeSlots.BiomeName(biome)}");
			}
			Check.All(failures, "Slots never reached");
		}

		// A slot owner may be a slime colour (negative net ID): the swap must index sets by type, not crash
		private static void NegativeNetIdSwap()
		{
			if (Main.netMode != NetmodeID.SinglePlayer) Check.Skip("single player only");
			Player player = Main.LocalPlayer;
			int index = NPC.NewNPC(new Terraria.DataStructures.EntitySource_SpawnNPC(), (int)player.Center.X + 1200, (int)player.Center.Y - 200, NPCID.Zombie);
			Check.That(index >= 0 && index < Main.maxNPCs, "could not spawn a zombie");
			NPC npc = Main.npc[index];
			try
			{
				EnemySwapSystem.Pending[index] = false;
				EnemySwapSystem.ApplySwap(npc, NPCID.Pinky);
				Check.Equal(NPCID.Pinky, npc.netID, "net ID after the swap");
				Check.Equal((int)NPCID.BlueSlime, npc.type, "type after the swap");
				// Through ProcessSlot when this world has a slime colour owner here
				if (EnemiesConfig.Instance.EnemySwapMode == EnemySwapMode.ByBiome)
				{
					BiomeSlotMap map = BiomeSlots.Current;
					foreach (int original in map.ByOriginal.Keys.Where(o => o > 0 && !NPCID.Sets.CountsAsCritter[o]).OrderBy(o => o))
					{
						npc.SetDefaults(original);
						int target = EnemySwapPool.BiomeTarget(npc);
						if (target >= 0) continue;
						for (int attempt = 0; attempt < 10 && npc.netID != target; attempt++)
						{
							npc.SetDefaults(original);
							EnemySwapSystem.Pending[index] = true;
							EnemySwapSystem.ProcessSlot(index, 100);
						}
						Check.Equal(target, npc.netID, $"{Name(original)} through ProcessSlot");
						Check.Equal(BiomeSlots.TypeOf(target), npc.type, "type after ProcessSlot");
						break;
					}
				}
			}
			finally
			{
				EnemySwapSystem.Pending[index] = false;
				npc.active = false;
			}
		}
	}
}

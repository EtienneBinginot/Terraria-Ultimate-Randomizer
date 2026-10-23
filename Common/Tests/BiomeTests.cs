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
			TestRunner.Register("EvilSlotsShared", EvilSlotsShared);
		}

		private static string Name(int id) => Lang.GetNPCNameValue(id);

		private static void Log(string message) => ModContent.GetInstance<UltimateRandomizer>().Logger.Info(message);

		private static int Seed(int i) => 1000 + i * 7919;

		// Every natural enemy keeps exactly its vanilla number of slots (at least one), with or without extras. Owners only
		// move inside their class (movement, rarity, condition, critter) and their tier pair (0-1, 2-3...), unless the pair was
		// merged with a neighbour (too few owners); an owner is never more than one tier above the slot (none above in the
		// critical slots), never across the hardmode or Plantera line.
		// The big tier 0 ground class (most Forest slots) is never left vanilla.
		private static void BiomeRedistributionComplete()
		{
			Dictionary<int, int> vanilla = BiomeSlots.VanillaSlotCounts();
			if (vanilla.Count == 0) Check.Skip("no natural enemy has a bestiary biome");
			List<string> failures = new();
			int changedSeeds = 0;
			long sameBiome = 0, totalSlots = 0;
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
					else if (owner.tier > slot.Tier + 1 || BiomeSlots.BandOf(owner.tier) > slot.Band || slot.Critical && owner.tier > slot.Tier
						|| !slot.Merged && owner.tier / 2 != slot.TierGroup)
					{
						failures.Add($"seed {seed}: {Name(slot.Owner)} (tier {owner.tier}) in the tier {slot.Tier} slot of {Name(slot.Original)}{(slot.Merged ? " (merged)" : "")}");
					}
				}
				List<BiomeSlot> forestGround = map.Slots.Where(s => s.Biome == (int)SpawnBiome.Forest && s.Movement == 0 && s.Tier == 0 && !s.Rare && !s.Conditional && !s.Critter).ToList();
				if (forestGround.Count > 1 && forestGround.All(s => s.Owner == s.Original)) failures.Add($"seed {seed}: the tier 0 ground enemies of the Forest stay vanilla");
				if (map.Slots.Any(s => s.Owner != s.Original)) changedSeeds++;
				sameBiome += map.SameBiome;
				totalSlots += map.Slots.Count;
				// Same seed, same map
				BiomeSlotMap again = BiomeSlots.Build(seed, 0f);
				if (!again.Slots.Select(s => s.Owner).SequenceEqual(map.Slots.Select(s => s.Owner))) failures.Add($"seed {seed}: the map is not deterministic");

				// Extras only share slots: the owners stay the same
				BiomeSlotMap withExtras = BiomeSlots.Build(seed, DefaultExtras);
				if (!withExtras.Slots.Select(s => s.Owner).SequenceEqual(map.Slots.Select(s => s.Owner))) failures.Add($"seed {seed}: extras changed the owners");
				if (i == 0) Log($"Biome classes: {map.Classes}, {map.ClassesKeptVanilla} kept vanilla, {map.Slots.Count(s => s.Merged)} merged slots, {map.Slots.Count(s => s.Owner != s.Original)}/{map.Slots.Count} slots changed");
			}
			// Information only: an owner may land in a biome where it also lives in vanilla
			Log($"Biome same-biome owners: {100.0 * sameBiome / System.Math.Max(1, totalSlots):0.0}% of the slots over {Seeds} seeds");
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

		// The critical stations (work bench...) drop from enemies that really spawn in the early slots of their biome: the
		// slots of the pacing model's early kills there (the Forest's day slimes and night zombies and eyes for the fallback)
		private static void CriticalHostsInForest()
		{
			if (!BiomeSlots.Active) Check.Skip("enemies are not redistributed by biome");
			List<GuaranteedSources.Requirement> critical = GuaranteedSources.Requirements.Where(r => r.Critical).ToList();
			if (critical.Count == 0) Check.Skip("no critical station is randomized");
			BiomeSlotMap map = BiomeSlots.Current;
			List<string> failures = new();
			// The enemies of the Forest slots of the day slimes, and of every kind of zombie and demon eye by night
			HashSet<int> day = BiomeSlots.ForestFamilyHosts(map, true).ToHashSet();
			HashSet<int> night = BiomeSlots.ForestFamilyHosts(map, false).ToHashSet();
			if (day.Count == 0 || night.Count == 0) failures.Add("no critical Forest slot by day or by night");
			if (map.Slots.Any(s => s.Critical && s.Extra != 0)) failures.Add("a critical Forest slot is shared with an extra");
			foreach (GuaranteedSources.Requirement requirement in critical)
			{
				List<int> hosts = GuaranteedSources.HostsOf(requirement.Item).ToList();
				GuaranteedSources.TryPlacement(requirement.Item, out GuaranteedSources.Placement place);
				if (!place.Fallback)
				{
					if (place.Biome < 0 || place.Biome >= BiomeSlots.ModdedBiomeStart || !PacingModel.EarlyBiomes.Contains((SpawnBiome)place.Biome))
					{
						failures.Add($"{Lang.GetItemNameValue(requirement.Item)}: in {(place.Biome >= 0 ? BiomeSlots.BiomeName(place.Biome) : "no biome")}, not an early biome");
						continue;
					}
					HashSet<int> early = PacingModel.EarlyOriginals((SpawnBiome)place.Biome).ToHashSet();
					HashSet<int> met = map.Slots.Where(s => s.Biome == place.Biome && early.Contains(s.Original) && !s.AbsentEvil).SelectMany(BiomeSlots.Spawners).Select(BiomeSlots.TypeOf).ToHashSet();
					if (!hosts.Any(met.Contains)) failures.Add($"{Lang.GetItemNameValue(requirement.Item)}: no host spawns in the early slots of {BiomeSlots.BiomeName(place.Biome)} ({string.Join(", ", hosts.Select(Name))})");
					continue;
				}
				if (!hosts.Any(day.Contains)) failures.Add($"{Lang.GetItemNameValue(requirement.Item)}: no host spawns in the Forest by day ({string.Join(", ", hosts.Select(Name))})");
				if (!hosts.Any(night.Contains)) failures.Add($"{Lang.GetItemNameValue(requirement.Item)}: no host spawns in the Forest by night ({string.Join(", ", hosts.Select(Name))})");
			}
			Check.All(failures, "Critical stations on enemies outside the early slots");
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

		// Without both evils, the slots of the missing one never open: each of their owners still spawns somewhere. Through a
		// present-evil slot (its owner, or shared with it, within the tier rule) whenever the present evil has a slot of its
		// kind that takes it; otherwise through its other slots, or a slot of its movement shared in another biome (never
		// across the hardmode or Plantera line). Pick returns the shared owners half of the time. Drunk worlds share nothing,
		// and the sharing never changes the owners.
		private static void EvilSlotsShared()
		{
			List<string> failures = new();
			int absentSlots = 0, unshared = 0, elsewhere = 0;
			for (int i = 0; i < Seeds; i++)
			{
				int seed = Seed(i);
				BiomeSlotMap both = BiomeSlots.Build(seed, DefaultExtras, WorldEvil.Both);
				if (both.Slots.Any(s => s.AbsentEvil || s.EvilShared != null)) failures.Add($"seed {seed}: evil slots shared in a world with both evils");
				foreach (WorldEvil evil in new[] { WorldEvil.Corruption, WorldEvil.Crimson })
				{
					BiomeSlotMap map = BiomeSlots.Build(seed, DefaultExtras, evil);
					if (!map.Slots.Select(s => s.Owner).SequenceEqual(both.Slots.Select(s => s.Owner))) failures.Add($"seed {seed} ({evil}): the sharing changed the owners");
					SpawnBiome surface = evil == WorldEvil.Crimson ? SpawnBiome.Crimson : SpawnBiome.Corruption;
					SpawnBiome underground = evil == WorldEvil.Crimson ? SpawnBiome.UndergroundCrimson : SpawnBiome.UndergroundCorruption;
					List<BiomeSlot> hosts = map.Slots.Where(s => s.Biome == (int)surface || s.Biome == (int)underground).ToList();
					List<BiomeSlot> open = map.Slots.Where(s => !s.AbsentEvil).ToList();
					if (hosts.Any(s => s.AbsentEvil)) failures.Add($"seed {seed} ({evil}): a present-evil slot is marked absent");
					foreach (BiomeSlot slot in map.Slots.Where(s => s.AbsentEvil))
					{
						absentSlots++;
						int owner = slot.Owner;
						string where = $"seed {seed} ({evil}): {Name(owner)} ({BiomeSlots.BiomeName(slot.Biome)} slot of {Name(slot.Original)})";
						if (hosts.Any(h => BiomeSlots.Spawners(h).Contains(owner))) continue;
						if (!open.Any(h => BiomeSlots.Spawners(h).Contains(owner)))
						{
							unshared++;
							failures.Add($"{where} never spawns");
							continue;
						}
						elsewhere++;
						// Outside the present evil only when no slot of its kind there could take it
						int tier = BiomeSlots.TierOf(owner);
						bool fits = hosts.Any(h => !h.Critical && h.Critter == slot.Critter && h.Movement == slot.Movement && tier <= h.Tier + 1 && BiomeSlots.BandOf(tier) <= h.Band
							&& (!h.DaySurface || !EnemySwapPool.DaylightIntolerant(owner)));
						if (fits) failures.Add($"{where} spawns outside the present evil, which has a slot for it");
					}
					foreach (BiomeSlot host in open.Where(h => h.EvilShared != null))
					{
						bool present = hosts.Contains(host);
						if (host.Critical || host.Extra != 0) failures.Add($"seed {seed} ({evil}): the {BiomeSlots.BiomeName(host.Biome)} slot of {Name(host.Original)} is also critical or shared with an extra");
						foreach (int shared in host.EvilShared)
						{
							int tier = BiomeSlots.TierOf(shared);
							if ((present && tier > host.Tier + 1) || BiomeSlots.BandOf(tier) > host.Band)
							{
								failures.Add($"seed {seed} ({evil}): {Name(shared)} (tier {tier}) shares the tier {host.Tier} slot of {Name(host.Original)} in {BiomeSlots.BiomeName(host.Biome)}");
							}
						}
						Check.Equal(host.EvilShared[0], BiomeSlots.Pick(host, 0.0), "Pick at 0");
						Check.Equal(host.Owner, BiomeSlots.Pick(host, BiomeSlots.EvilShare), "Pick at the evil share");
						Check.Equal(host.Owner, BiomeSlots.Pick(host, 0.999), "Pick near 1");
					}
					if (i == 0) Log($"Biome evil ({evil}): {map.EvilSharedSlots} absent-evil slots shared, {map.EvilElsewhere} elsewhere, {map.EvilUnshared} without a partner, "
						+ $"{hosts.Count(h => h.EvilShared != null)}/{hosts.Count} present-evil slots shared");
				}
			}
			if (absentSlots == 0) Check.Skip("no natural enemy lives in an evil biome");
			Log($"Biome evil: {unshared}/{absentSlots} absent-evil slots unreachable, {elsewhere} reached outside the present evil over {Seeds} seeds");
			Check.All(failures, "Absent-evil owners");
		}

		// A slot owner may be a slime colour (negative net ID): the swap must index sets by type, not crash
		private static void NegativeNetIdSwap()
		{
			if (Main.netMode != NetmodeID.SinglePlayer) Check.Skip("single player only");
			NPC npc = TestRunner.SpawnNear(NPCID.Zombie);
			Check.That(npc != null, "could not spawn a zombie");
			int index = npc.whoAmI;
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
				TestRunner.RemoveNpc(npc);
			}
		}
	}
}

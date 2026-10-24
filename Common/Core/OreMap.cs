using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace UltimateRandomizer.Common.Core
{
	// Mined ores: a permutation inside each tier among the ores of this world, so every ore of the world still exists
	// (as another ore's drop). Depends on the world's ore choices, which vanilla saves and sends to clients (message 7).
	// The permutation is over stable "slots" (a vanilla pair such as Copper/Tin, or a single ore), keyed by the seed only:
	// when an altar decides a hardmode pair, the mapping of every other ore stays the same.
	public static class OreMap
	{
		private static Dictionary<int, int> map;
		private static int mapSignature;
		private static int findSignature;

		// Vanilla ore pairs (tiles): only one of each pair generates in a normal world
		private static readonly (int a, int b)[] Pairs =
		{
			(TileID.Copper, TileID.Tin), (TileID.Iron, TileID.Lead), (TileID.Silver, TileID.Tungsten), (TileID.Gold, TileID.Platinum),
			(TileID.Demonite, TileID.Crimtane), (TileID.Cobalt, TileID.Palladium), (TileID.Mythril, TileID.Orichalcum), (TileID.Adamantite, TileID.Titanium)
		};

		// Pairs chosen by the altars during hardmode (WorldGen.SmashAltar), the others at world generation
		public const int FirstHardmodePair = 5;

		public static bool IsOre(int type) =>
			ContentSamples.ItemsByType.TryGetValue(type, out Item sample) && sample.createTile >= TileID.Dirt && TileID.Sets.Ore[sample.createTile];

		// The vanilla pair of an ore (-1: none)
		public static int PairOf(int type)
		{
			if (!ContentSamples.ItemsByType.TryGetValue(type, out Item sample)) return -1;
			for (int p = 0; p < Pairs.Length; p++)
			{
				if (sample.createTile == Pairs[p].a || sample.createTile == Pairs[p].b) return p;
			}
			return -1;
		}

		// The tile of each pair chosen by this world (-1: not chosen yet, both count as present)
		private static int Chosen(int pair) => pair switch
		{
			0 => WorldGen.SavedOreTiers.Copper,
			1 => WorldGen.SavedOreTiers.Iron,
			2 => WorldGen.SavedOreTiers.Silver,
			3 => WorldGen.SavedOreTiers.Gold,
			4 => WorldGen.crimson ? TileID.Crimtane : TileID.Demonite,
			5 => WorldGen.SavedOreTiers.Cobalt,
			6 => WorldGen.SavedOreTiers.Mythril,
			_ => WorldGen.SavedOreTiers.Adamantite
		};

		// Drunk worlds have both evils, and each altar smash alternates the hardmode ores (WorldGen.SmashAltar)
		private static bool BothVariants(int pair) => pair >= 4 && Main.drunkWorld;

		// Can this ore be mined in this world? Hardmode ores count as present until the altars choose them
		public static bool IsPresent(int type)
		{
			int p = PairOf(type);
			if (p < 0 || BothVariants(p)) return true;
			int chosen = Chosen(p);
			// Unknown (old world, or hardmode ore not chosen yet), or a modded value: no restriction
			if (chosen != Pairs[p].a && chosen != Pairs[p].b) return true;
			return ContentSamples.ItemsByType[type].createTile == chosen;
		}

		// Counted by Findability: the ores that can be mined in this world. Both hardmode ores of a pair always count, so
		// an altar smash never changes the world caches (CheckWorld only rebuilds them for the pairs decided at world
		// generation). The one that will be absent is harmless: a new ingredient asks for either ore of the pair
		// (OrePairGroups, AskedAsPair), and they get no guaranteed source; only their own vanilla bar recipes need one of them.
		public static bool IsFindable(int type) => IsHardmodePairOre(type) || IsPresent(type);

		// An ore of a pair decided by the altars: its presence changes during hardmode
		public static bool IsHardmodePairOre(int type) => PairOf(type) >= FirstHardmodePair;

		// An ore the recipe plan may only ask for through its pair's group ("Cobalt or Palladium Ore"): a hardmode pair ore
		// (the altars decide after the plan), or a pre-hardmode one whose pair this world has not recorded (an old world:
		// both count as present). The pre-hardmode ores of a world that recorded them are known from its creation: the real one
		public static bool AskedAsPair(int type)
		{
			int p = PairOf(type);
			if (p < 0) return false;
			if (p >= FirstHardmodePair) return true;
			if (BothVariants(p)) return false;
			int chosen = Chosen(p);
			return chosen != Pairs[p].a && chosen != Pairs[p].b;
		}

		private static int Signature(int pairs)
		{
			int signature = 17;
			for (int p = 0; p < pairs; p++) signature = signature * 31 + (BothVariants(p) ? -2 : Chosen(p));
			return signature;
		}

		public static int Get(int ore)
		{
			map ??= Build(IsPresent);
			return map.TryGetValue(ore, out int replacement) ? replacement : ore;
		}

		// The mapping for a given presence of the ores (tests simulate an altar deciding a pair)
		public static Dictionary<int, int> Build(Func<int, bool> present)
		{
			Dictionary<int, int> result = new();
			for (int t = 0; t < Tiers.Count; t++)
			{
				// Slots of the tier, in a fixed order: a pair's ores of this tier, or a single ore. Ordered by the items' full
				// names, not their IDs, so that adding or removing a mod doesn't reshuffle the vanilla ores
				List<int[]> slots = Enumerable.Range(1, ItemPool.Info.Length - 1)
					.Where(type => ItemPool.IsRandomizable(type) && ItemPool.Info[type].Tier == t && IsOre(type))
					.GroupBy(type => PairOf(type) >= 0 ? -1 - PairOf(type) : type)
					.Select(g => g.OrderBy(DetRandom.ItemKey).ThenBy(x => x).ToArray())
					.OrderBy(s => DetRandom.ItemKey(s[0])).ThenBy(s => s[0]).ToList();
				int[] perm = Enumerable.Range(0, slots.Count).ToArray();
				Random rng = DetRandom.Get(RollKey.OreShuffle, t);
				for (int i = perm.Length - 1; i > 0; i--)
				{
					int j = rng.Next(i + 1);
					(perm[i], perm[j]) = (perm[j], perm[i]);
				}
				int[][] presentOres = slots.Select(slot => slot.Where(present).ToArray()).ToArray();
				// One-to-one: a cycle of the permutation whose slots don't all have the same number of present ores (a pair
				// with both ores present next to slots with one: drunk worlds, undecided hardmode pairs, modded ores) stays
				// in place. Other cycles are untouched, so deciding a pair only changes the cycle it belongs to.
				bool[] inPlace = new bool[slots.Count];
				bool[] seen = new bool[slots.Count];
				for (int s = 0; s < slots.Count; s++)
				{
					if (seen[s]) continue;
					List<int> cycle = new();
					for (int c = s; !seen[c]; c = perm[c])
					{
						seen[c] = true;
						cycle.Add(c);
					}
					if (cycle.Any(c => presentOres[c].Length != presentOres[s].Length))
					{
						foreach (int c in cycle) inPlace[c] = true;
					}
				}
				for (int s = 0; s < slots.Count; s++)
				{
					if (inPlace[s]) continue;
					int[] from = presentOres[s];
					int[] to = presentOres[perm[s]];
					// Which ore of a pair stands for which ore of the other pair: chosen by the seed
					int choice = DetRandom.Get(RollKey.OreShuffle, t, DetRandom.ItemKey(slots[perm[s]][0])).Next(2);
					for (int k = 0; k < from.Length; k++)
					{
						result[from[k]] = to.Length switch
						{
							0 => from[k],
							1 => to[0],
							_ => to[(k + choice) % to.Length]
						};
					}
				}
			}
			// Absent ores (Extractinator, crates, placed blocks) stay themselves: not in the map
			return result;
		}

		// The world's ore choices changed (an altar decided a hardmode pair; message 7 on clients)
		public static void CheckWorld()
		{
			if (Signature(FirstHardmodePair) != findSignature) SeedSystem.NotifyConfigChanged();
			else if (Signature(Pairs.Length) != mapSignature) Reset();
		}

		public static void Reset()
		{
			map = null;
			mapSignature = Signature(Pairs.Length);
			findSignature = Signature(FirstHardmodePair);
		}
	}

	// "Either ore of the pair" recipe groups (and the hardmode pairs' bars), registered on every machine at load in the same
	// order, so their ids match everywhere: the recipe plan (computed on each machine) asks for them instead of an ore whose
	// presence it can't know (OreMap.AskedAsPair)
	public class OrePairGroups : ModSystem
	{
		private static readonly (string key, int a, int b, int ore)[] Groups =
		{
			("Mods.UltimateRandomizer.RecipeGroups.CopperOre", ItemID.CopperOre, ItemID.TinOre, ItemID.CopperOre),
			("Mods.UltimateRandomizer.RecipeGroups.IronOre", ItemID.IronOre, ItemID.LeadOre, ItemID.IronOre),
			("Mods.UltimateRandomizer.RecipeGroups.SilverOre", ItemID.SilverOre, ItemID.TungstenOre, ItemID.SilverOre),
			("Mods.UltimateRandomizer.RecipeGroups.GoldOre", ItemID.GoldOre, ItemID.PlatinumOre, ItemID.GoldOre),
			("Mods.UltimateRandomizer.RecipeGroups.CobaltOre", ItemID.CobaltOre, ItemID.PalladiumOre, ItemID.CobaltOre),
			("Mods.UltimateRandomizer.RecipeGroups.MythrilOre", ItemID.MythrilOre, ItemID.OrichalcumOre, ItemID.MythrilOre),
			("Mods.UltimateRandomizer.RecipeGroups.AdamantiteOre", ItemID.AdamantiteOre, ItemID.TitaniumOre, ItemID.AdamantiteOre),
			("Mods.UltimateRandomizer.RecipeGroups.CobaltBar", ItemID.CobaltBar, ItemID.PalladiumBar, ItemID.CobaltOre),
			("Mods.UltimateRandomizer.RecipeGroups.MythrilBar", ItemID.MythrilBar, ItemID.OrichalcumBar, ItemID.MythrilOre),
			("Mods.UltimateRandomizer.RecipeGroups.AdamantiteBar", ItemID.AdamantiteBar, ItemID.TitaniumBar, ItemID.AdamantiteOre)
		};

		// Registered id -> the ore whose pair decides whether the group is needed
		private static readonly Dictionary<int, int> ids = new();

		public static IEnumerable<int> Ids => ids.Keys;

		public static bool IsPairGroup(int id) => ids.ContainsKey(id);

		// Is this group a candidate in this world: its pair is unknown when planning (hardmode pairs always)
		public static bool Needed(int id) => ids.TryGetValue(id, out int ore) && OreMap.AskedAsPair(ore);

		public override void AddRecipeGroups()
		{
			ids.Clear();
			foreach ((string key, int a, int b, int ore) in Groups)
			{
				// Read when shown (the texts load after the recipe groups)
				RecipeGroup group = new(() => Language.GetTextValue(key), a, b);
				ids[RecipeGroup.RegisterGroup("UltimateRandomizer:" + key.Substring(key.LastIndexOf('.') + 1), group)] = ore;
			}
		}

		public override void Unload() => ids.Clear();
	}

	// Server, single player and clients: each one watches its own (synced) copy of the world's ore choices
	public class OreMapSystem : ModSystem
	{
		private int timer;

		public override void PostUpdateEverything()
		{
			if (!SeedSystem.WorldReady || ++timer < 60) return;
			timer = 0;
			OreMap.CheckWorld();
		}
	}
}

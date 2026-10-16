using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
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

		// Counted by Findability: the ores of this world. Both hardmode ores of a pair count (hardmode crates give both),
		// so the plans built on findability never change when an altar decides them.
		public static bool IsFindable(int type) => PairOf(type) >= FirstHardmodePair || IsPresent(type);

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
				// Slots of the tier, in a fixed order: a pair's ores of this tier, or a single ore
				List<int[]> slots = Enumerable.Range(1, ItemPool.Info.Length - 1)
					.Where(type => ItemPool.IsRandomizable(type) && ItemPool.Info[type].Tier == t && IsOre(type))
					.GroupBy(type => PairOf(type) >= 0 ? -1 - PairOf(type) : type)
					.Select(g => g.OrderBy(x => x).ToArray()).OrderBy(s => s[0]).ToList();
				int[] perm = Enumerable.Range(0, slots.Count).ToArray();
				Random rng = DetRandom.Get(RollKey.OreShuffle, t);
				for (int i = perm.Length - 1; i > 0; i--)
				{
					int j = rng.Next(i + 1);
					(perm[i], perm[j]) = (perm[j], perm[i]);
				}
				for (int s = 0; s < slots.Count; s++)
				{
					int[] from = slots[s].Where(present).ToArray();
					int[] to = slots[perm[s]].Where(present).ToArray();
					// While a pair is undecided, the ore it stands for is chosen by the seed; once decided, the chosen one
					int choice = DetRandom.Get(RollKey.OreShuffle, t, DetRandom.ItemKey(slots[perm[s]][0])).Next(2);
					for (int k = 0; k < from.Length; k++)
					{
						result[from[k]] = to.Length switch
						{
							0 => from[k],
							1 => to[0],
							_ => to[(from.Length == 1 ? choice : k + choice) % to.Length]
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

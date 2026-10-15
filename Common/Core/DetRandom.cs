using System;
using System.Collections.Generic;
using Terraria.ID;

namespace UltimateRandomizer.Common.Core
{
	// Keys separating each kind of roll, so two features never share the same random sequence
	public static class RollKey
	{
		public const int MobDrop = 1;
		public const int MobDropSlots = 2;
		public const int BagDrop = 3;
		public const int Shop = 4;
		public const int Recipe = 5;
		public const int BossHealth = 6;
		public const int BossDamage = 7;
		public const int Shuffle = 8;
		public const int Shimmer = 9;
		public const int Chest = 10;
		public const int Fishing = 11;
		public const int TileDrop = 12;
		public const int StartingKit = 13;
		public const int EnemyLife = 14;
		public const int EnemyDamage = 15;
		public const int EnemyDefense = 16;
		public const int EnemyScale = 17;
		public const int EnemySwap = 18;
		public const int TownNpc = 19;
		public const int Price = 20;
		public const int Fallback = 0xFA11;
	}

	// Deterministic randomness: the same world seed and keys always give the same results, on every machine.
	// Content is identified by its full name (stable when the mod list changes), never by numeric id or string.GetHashCode.
	public static class DetRandom
	{
		private static readonly Dictionary<int, int> npcKeys = new();
		private static int[] itemKeys = Array.Empty<int>();

		public static int Hash(int seed, params int[] keys)
		{
			unchecked
			{
				int hash = (int)2166136261u ^ seed;
				foreach (int key in keys)
				{
					hash = (hash ^ key) * 16777619;
					hash ^= hash >> 15;
				}
				return hash;
			}
		}

		public static Random Get(params int[] keys) => new Random(Hash(SeedSystem.WorldSeed, keys));

		public static int StableHash(string s)
		{
			unchecked
			{
				int hash = (int)2166136261u;
				foreach (char c in s)
				{
					hash = (hash ^ c) * 16777619;
				}
				return hash;
			}
		}

		public static void BuildItemKeys(int itemCount)
		{
			itemKeys = new int[itemCount];
			for (int i = 0; i < itemCount; i++)
			{
				itemKeys[i] = StableHash(ItemName(i));
			}
		}

		public static int ItemKey(int type) => type >= 0 && type < itemKeys.Length ? itemKeys[type] : StableHash(ItemName(type));

		public static int NpcKey(int type)
		{
			if (!npcKeys.TryGetValue(type, out int key))
			{
				key = StableHash(ContentSamples.NpcPersistentIdsByNetIds.TryGetValue(type, out string name) ? name : "npc" + type);
				npcKeys[type] = key;
			}
			return key;
		}

		public static string ItemName(int type) => ContentSamples.ItemPersistentIdsByNetIds.TryGetValue(type, out string name) ? name : "item" + type;

		public static void Unload()
		{
			npcKeys.Clear();
			itemKeys = Array.Empty<int>();
		}
	}
}

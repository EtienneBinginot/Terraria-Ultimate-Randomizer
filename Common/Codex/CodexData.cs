using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Codex
{
	// Where an item was received, besides enemy drops
	public enum CodexSource : byte { Chest, Pot, Tile, Tree, Bag, Fishing, Extractinator, Shimmer }

	public enum CodexOp : byte { Kill, Drop, Source, SourceKey, Recipe, Seen, Sighting, Chest, Hint }

	// One discovery. Counts are increments; a full sync is the whole codex as entries applied to an empty one.
	// Npc, From and Item are full names (stable across mod changes); From is the bag (bags) or the shimmered item (shimmer).
	public class CodexEntry
	{
		public CodexOp Op;
		public CodexSource Kind;
		public string Npc = "";
		public string From = "";
		public string Item = "";
		public string Biome = "";
		public int Count;
		// Recipe key, or chest position (x | y << 16)
		public int Key;
		// Chest: what it contained when first opened
		public List<(string item, int count)> Items;

		public static CodexEntry Kill(string npc, int count = 1) => new() { Op = CodexOp.Kill, Npc = npc, Count = count };

		public static CodexEntry Drop(string npc, string item, int count) => new() { Op = CodexOp.Drop, Npc = npc, Item = item, Count = count };

		public static CodexEntry Source(CodexSource kind, string from, string item, int count) => new() { Op = CodexOp.Source, Kind = kind, From = from ?? "", Item = item, Count = count };

		public static CodexEntry SourceKey(CodexSource kind, string from) => new() { Op = CodexOp.SourceKey, Kind = kind, From = from ?? "" };

		public static CodexEntry Recipe(int key) => new() { Op = CodexOp.Recipe, Key = key };

		public static CodexEntry Seen(string item) => new() { Op = CodexOp.Seen, Item = item };

		public static CodexEntry Sighting(string biome, string npc) => new() { Op = CodexOp.Sighting, Biome = biome, Npc = npc };

		public static CodexEntry Chest(int position, List<(string item, int count)> items) => new() { Op = CodexOp.Chest, Key = position, Items = items ?? new() };

		// A hint level bought from the Guide (Count is the level)
		public static CodexEntry Hint(string item, int level) => new() { Op = CodexOp.Hint, Item = item, Count = level };

		public static int ChestPosition(int x, int y) => (x & 0xFFFF) | (y << 16);

		// Identity of a set-like discovery, so a client does not send it twice while waiting for the server
		public string SetKey => Op switch
		{
			CodexOp.Recipe => "r" + Key,
			CodexOp.Seen => "s" + Item,
			CodexOp.Sighting => "g" + Biome + "|" + Npc,
			CodexOp.Chest => "c" + Key,
			CodexOp.SourceKey => "k" + (int)Kind + "|" + From,
			CodexOp.Hint => "h" + Item + "|" + Count,
			_ => null
		};
	}

	// Full names of items and NPCs, and back to ids (unknown names give 0: their mod is not loaded)
	public static class CodexNames
	{
		public static string Item(int type) => DetRandom.ItemName(type);

		public static string Npc(int netId) => ContentSamples.NpcPersistentIdsByNetIds.TryGetValue(netId, out string name) ? name : "npc" + netId;

		public static int ItemType(string name) => !string.IsNullOrEmpty(name) && ContentSamples.ItemNetIdsByPersistentIds.TryGetValue(name, out int type) ? type : 0;

		public static int NpcId(string name) => !string.IsNullOrEmpty(name) && ContentSamples.NpcNetIdsByPersistentIds.TryGetValue(name, out int id) ? id : 0;

		// Vanilla tiles by their TileID name, modded ones by their full name
		public static string Tile(int type)
		{
			if (type >= TileID.Count) return TileLoader.GetTile(type)?.FullName ?? "tile" + type;
			return TileID.Search.TryGetName(type, out string name) ? name : "tile" + type;
		}

		// -1 when unknown (0 is Dirt)
		public static int TileType(string name)
		{
			if (string.IsNullOrEmpty(name)) return -1;
			if (name.Contains('/')) return ModContent.TryFind(name, out ModTile tile) ? tile.Type : -1;
			return TileID.Search.TryGetId(name, out int type) ? type : -1;
		}
	}

	// Everything discovered in a world for its current seed: pure data, saved with the world and synced by CodexSystem
	public class CodexData
	{
		// Caps keep a broken mod or a long game from growing the save and the join packet without limit
		public const int MaxKeys = 5000;
		public const int MaxPerKey = 3000;
		public const int MaxSetSize = 60000;

		public static readonly int SourceKinds = Enum.GetValues<CodexSource>().Length;

		public int Seed;
		public bool HasSeed;

		public const int Format = 1;

		// Incremented on every change, and by what changed: counts only, new keys (items, enemies, sightings, chests,
		// seen items), or the discovered recipes. Caches rebuild only what moved.
		public int Version { get; private set; }
		public int CountVersion { get; private set; }
		public int KeyVersion { get; private set; }
		public int RecipeVersion { get; private set; }

		// Hash of the settings the recipes were randomized with when they were discovered (the recipe set is cleared
		// when the world is randomized again)
		public int RecipeStamp;
		public bool HasRecipeStamp;

		private enum Change { None, Count, Key, Recipe }

		public readonly Dictionary<string, int> Kills = new();
		public readonly Dictionary<string, Dictionary<string, int>> Drops = new();
		// By kind, then by source key ("" except bags and shimmer), then by item
		public readonly Dictionary<string, Dictionary<string, int>>[] Sources;
		public readonly HashSet<int> Recipes = new();
		public readonly HashSet<string> ItemsSeen = new();
		public readonly Dictionary<string, HashSet<string>> Sightings = new();
		public readonly HashSet<int> OpenedChests = new();
		// Hint levels bought from the Guide, by item (shared by the whole world)
		public readonly Dictionary<string, int> Hints = new();

		public CodexData()
		{
			Sources = new Dictionary<string, Dictionary<string, int>>[SourceKinds];
			for (int i = 0; i < SourceKinds; i++) Sources[i] = new Dictionary<string, Dictionary<string, int>>();
		}

		public void Clear()
		{
			Kills.Clear();
			Drops.Clear();
			foreach (Dictionary<string, Dictionary<string, int>> table in Sources) table.Clear();
			Recipes.Clear();
			ItemsSeen.Clear();
			Sightings.Clear();
			OpenedChests.Clear();
			Hints.Clear();
			Bump(Change.Key);
			Bump(Change.Recipe);
		}

		public void ClearRecipes()
		{
			Recipes.Clear();
			Bump(Change.Recipe);
		}

		private void Bump(Change change)
		{
			if (change == Change.None) return;
			Version++;
			if (change == Change.Count) CountVersion++;
			else if (change == Change.Key) KeyVersion++;
			else RecipeVersion++;
		}

		public bool Apply(CodexEntry e)
		{
			Change change = ApplyInner(e);
			Bump(change);
			return change != Change.None;
		}

		private Change ApplyInner(CodexEntry e)
		{
			switch (e.Op)
			{
				case CodexOp.Kill:
					return e.Count > 0 ? AddCount(Kills, e.Npc, e.Count, MaxKeys) : Change.None;
				case CodexOp.Drop:
					return e.Count > 0 ? AddCount(Inner(Drops, e.Npc), e.Item, e.Count, MaxPerKey) : Change.None;
				case CodexOp.Source:
					return (int)e.Kind < SourceKinds && e.Count > 0 ? AddCount(Inner(Sources[(int)e.Kind], e.From), e.Item, e.Count, MaxPerKey) : Change.None;
				case CodexOp.SourceKey:
					return (int)e.Kind < SourceKinds && !Sources[(int)e.Kind].ContainsKey(e.From) && Inner(Sources[(int)e.Kind], e.From) != null ? Change.Key : Change.None;
				case CodexOp.Recipe:
					return Recipes.Count < MaxSetSize && Recipes.Add(e.Key) ? Change.Recipe : Change.None;
				case CodexOp.Seen:
					return !string.IsNullOrEmpty(e.Item) && ItemsSeen.Count < MaxSetSize && ItemsSeen.Add(e.Item) ? Change.Key : Change.None;
				case CodexOp.Sighting:
					if (string.IsNullOrEmpty(e.Biome) || string.IsNullOrEmpty(e.Npc)) return Change.None;
					if (!Sightings.TryGetValue(e.Biome, out HashSet<string> npcs))
					{
						if (Sightings.Count >= MaxKeys) return Change.None;
						npcs = new HashSet<string>();
						Sightings[e.Biome] = npcs;
					}
					return npcs.Count < MaxPerKey && npcs.Add(e.Npc) ? Change.Key : Change.None;
				case CodexOp.Chest:
					if (OpenedChests.Count >= MaxSetSize || !OpenedChests.Add(e.Key)) return Change.None;
					if (e.Items != null)
					{
						Dictionary<string, int> chest = Inner(Sources[(int)CodexSource.Chest], "");
						foreach ((string item, int count) in e.Items)
						{
							if (count > 0) AddCount(chest, item, count, MaxPerKey);
						}
					}
					return Change.Key;
				case CodexOp.Hint:
				{
					// Levels only go up, one bought after the other
					if (string.IsNullOrEmpty(e.Item) || e.Count < 1 || e.Count > HintFacts.MaxLevel) return Change.None;
					int old = Hints.GetValueOrDefault(e.Item);
					if (e.Count <= old || (old == 0 && Hints.Count >= MaxKeys)) return Change.None;
					Hints[e.Item] = e.Count;
					return Change.Key;
				}
			}
			return Change.None;
		}

		// Would applying this change something? Counts always do; set-like discoveries only when new
		public bool WouldChange(CodexEntry e) => e.Op switch
		{
			CodexOp.SourceKey => (int)e.Kind < SourceKinds && !Sources[(int)e.Kind].ContainsKey(e.From),
			CodexOp.Recipe => !Recipes.Contains(e.Key),
			CodexOp.Seen => !ItemsSeen.Contains(e.Item),
			CodexOp.Sighting => !Sightings.TryGetValue(e.Biome, out HashSet<string> npcs) || !npcs.Contains(e.Npc),
			CodexOp.Chest => !OpenedChests.Contains(e.Key),
			CodexOp.Hint => e.Count > HintLevel(e.Item),
			_ => true
		};

		public int HintLevel(string item) => !string.IsNullOrEmpty(item) && Hints.TryGetValue(item, out int level) ? level : 0;

		private static Dictionary<string, int> Inner(Dictionary<string, Dictionary<string, int>> table, string key)
		{
			key ??= "";
			if (!table.TryGetValue(key, out Dictionary<string, int> inner))
			{
				if (table.Count >= MaxKeys) return null;
				inner = new Dictionary<string, int>();
				table[key] = inner;
			}
			return inner;
		}

		private static Change AddCount(Dictionary<string, int> table, string key, int count, int cap)
		{
			if (table == null || string.IsNullOrEmpty(key)) return Change.None;
			if (table.TryGetValue(key, out int old))
			{
				table[key] = (int)Math.Min((long)old + count, int.MaxValue);
				return Change.Count;
			}
			if (table.Count >= cap) return Change.None;
			table[key] = count;
			return Change.Key;
		}

		// The whole codex as entries: applied to an empty codex, they rebuild it exactly
		public IEnumerable<CodexEntry> AllEntries()
		{
			foreach ((string npc, int count) in Kills) yield return CodexEntry.Kill(npc, count);
			foreach ((string npc, Dictionary<string, int> items) in Drops)
			{
				foreach ((string item, int count) in items) yield return CodexEntry.Drop(npc, item, count);
			}
			for (int kind = 0; kind < SourceKinds; kind++)
			{
				foreach ((string from, Dictionary<string, int> items) in Sources[kind])
				{
					if (items.Count == 0) yield return CodexEntry.SourceKey((CodexSource)kind, from);
					foreach ((string item, int count) in items) yield return CodexEntry.Source((CodexSource)kind, from, item, count);
				}
			}
			foreach (int key in Recipes) yield return CodexEntry.Recipe(key);
			foreach (string item in ItemsSeen) yield return CodexEntry.Seen(item);
			foreach ((string biome, HashSet<string> npcs) in Sightings)
			{
				foreach (string npc in npcs) yield return CodexEntry.Sighting(biome, npc);
			}
			// Their content is already in the chest source above
			foreach (int position in OpenedChests) yield return CodexEntry.Chest(position, null);
			foreach ((string item, int level) in Hints) yield return CodexEntry.Hint(item, level);
		}

		// Sorted dump of the content, to compare two codexes (tests)
		public string Signature()
		{
			List<string> lines = new();
			foreach (CodexEntry e in AllEntries())
			{
				lines.Add($"{e.Op}|{e.Kind}|{e.Npc}|{e.From}|{e.Item}|{e.Biome}|{e.Count}|{e.Key}");
			}
			lines.Sort(StringComparer.Ordinal);
			return string.Join("\n", lines);
		}

		// Saved as parallel lists of names (stable when mods change; entries of unloaded mods are kept as they are)
		public void Save(TagCompound tag)
		{
			tag["format"] = Format;
			tag["seed"] = Seed;
			tag["hasSeed"] = HasSeed;
			tag["recipeStamp"] = RecipeStamp;
			tag["hasRecipeStamp"] = HasRecipeStamp;
			tag["killNpc"] = Kills.Keys.ToList();
			tag["killCount"] = Kills.Values.ToList();
			List<string> dropNpc = new(), dropItem = new();
			List<int> dropCount = new();
			foreach ((string npc, Dictionary<string, int> items) in Drops)
			{
				foreach ((string item, int count) in items)
				{
					dropNpc.Add(npc);
					dropItem.Add(item);
					dropCount.Add(count);
				}
			}
			tag["dropNpc"] = dropNpc;
			tag["dropItem"] = dropItem;
			tag["dropCount"] = dropCount;
			List<int> srcKind = new(), srcCount = new();
			List<string> srcFrom = new(), srcItem = new();
			for (int kind = 0; kind < SourceKinds; kind++)
			{
				foreach ((string from, Dictionary<string, int> items) in Sources[kind])
				{
					// An opened bag that gave nothing recorded is kept as an empty item
					if (items.Count == 0)
					{
						srcKind.Add(kind);
						srcFrom.Add(from);
						srcItem.Add("");
						srcCount.Add(0);
					}
					foreach ((string item, int count) in items)
					{
						srcKind.Add(kind);
						srcFrom.Add(from);
						srcItem.Add(item);
						srcCount.Add(count);
					}
				}
			}
			tag["srcKind"] = srcKind;
			tag["srcFrom"] = srcFrom;
			tag["srcItem"] = srcItem;
			tag["srcCount"] = srcCount;
			tag["recipes"] = Recipes.ToList();
			tag["seen"] = ItemsSeen.ToList();
			List<string> sightBiome = new(), sightNpc = new();
			foreach ((string biome, HashSet<string> npcs) in Sightings)
			{
				foreach (string npc in npcs)
				{
					sightBiome.Add(biome);
					sightNpc.Add(npc);
				}
			}
			tag["sightBiome"] = sightBiome;
			tag["sightNpc"] = sightNpc;
			tag["chests"] = OpenedChests.ToList();
			tag["hintItem"] = Hints.Keys.ToList();
			tag["hintLevel"] = Hints.Values.ToList();
		}

		public static CodexData Load(TagCompound tag)
		{
			CodexData data = new()
			{
				Seed = tag.GetInt("seed"),
				HasSeed = tag.GetBool("hasSeed"),
				RecipeStamp = tag.GetInt("recipeStamp"),
				HasRecipeStamp = tag.GetBool("hasRecipeStamp")
			};
			IList<string> killNpc = tag.GetList<string>("killNpc");
			IList<int> killCount = tag.GetList<int>("killCount");
			for (int i = 0; i < killNpc.Count && i < killCount.Count; i++) data.ApplyInner(CodexEntry.Kill(killNpc[i], killCount[i]));
			IList<string> dropNpc = tag.GetList<string>("dropNpc");
			IList<string> dropItem = tag.GetList<string>("dropItem");
			IList<int> dropCount = tag.GetList<int>("dropCount");
			for (int i = 0; i < dropNpc.Count && i < dropItem.Count && i < dropCount.Count; i++) data.ApplyInner(CodexEntry.Drop(dropNpc[i], dropItem[i], dropCount[i]));
			IList<int> srcKind = tag.GetList<int>("srcKind");
			IList<string> srcFrom = tag.GetList<string>("srcFrom");
			IList<string> srcItem = tag.GetList<string>("srcItem");
			IList<int> srcCount = tag.GetList<int>("srcCount");
			for (int i = 0; i < srcKind.Count && i < srcFrom.Count && i < srcItem.Count && i < srcCount.Count; i++)
			{
				if (srcKind[i] < 0 || srcKind[i] >= SourceKinds) continue;
				CodexSource kind = (CodexSource)srcKind[i];
				if (string.IsNullOrEmpty(srcItem[i])) data.ApplyInner(CodexEntry.SourceKey(kind, srcFrom[i]));
				else data.ApplyInner(CodexEntry.Source(kind, srcFrom[i], srcItem[i], srcCount[i]));
			}
			foreach (int key in tag.GetList<int>("recipes")) data.Recipes.Add(key);
			foreach (string item in tag.GetList<string>("seen")) data.ItemsSeen.Add(item);
			IList<string> sightBiome = tag.GetList<string>("sightBiome");
			IList<string> sightNpc = tag.GetList<string>("sightNpc");
			for (int i = 0; i < sightBiome.Count && i < sightNpc.Count; i++) data.ApplyInner(CodexEntry.Sighting(sightBiome[i], sightNpc[i]));
			foreach (int position in tag.GetList<int>("chests")) data.OpenedChests.Add(position);
			IList<string> hintItem = tag.GetList<string>("hintItem");
			IList<int> hintLevel = tag.GetList<int>("hintLevel");
			for (int i = 0; i < hintItem.Count && i < hintLevel.Count; i++) data.ApplyInner(CodexEntry.Hint(hintItem[i], hintLevel[i]));
			return data;
		}

		// Network form: names become ids (both sides run the same mods). Entries of unloaded mods are not sent.
		public static bool Write(BinaryWriter w, CodexEntry e)
		{
			int npc = 0, item = 0, from = 0;
			switch (e.Op)
			{
				case CodexOp.Kill:
				case CodexOp.Sighting:
					npc = CodexNames.NpcId(e.Npc);
					if (npc == 0) return false;
					break;
				case CodexOp.Drop:
					npc = CodexNames.NpcId(e.Npc);
					item = CodexNames.ItemType(e.Item);
					if (npc == 0 || item == 0) return false;
					break;
				case CodexOp.Source:
				case CodexOp.SourceKey:
					from = FromId(e.Kind, e.From);
					if (from == 0 && !string.IsNullOrEmpty(e.From)) return false;
					if (e.Op == CodexOp.Source && (item = CodexNames.ItemType(e.Item)) == 0) return false;
					break;
				case CodexOp.Seen:
				case CodexOp.Hint:
					item = CodexNames.ItemType(e.Item);
					if (item == 0) return false;
					break;
			}
			w.Write((byte)e.Op);
			switch (e.Op)
			{
				case CodexOp.Kill:
					w.Write(npc);
					w.Write7BitEncodedInt(e.Count);
					break;
				case CodexOp.Drop:
					w.Write(npc);
					w.Write7BitEncodedInt(item);
					w.Write7BitEncodedInt(e.Count);
					break;
				case CodexOp.Source:
					w.Write((byte)e.Kind);
					w.Write7BitEncodedInt(from);
					w.Write7BitEncodedInt(item);
					w.Write7BitEncodedInt(e.Count);
					break;
				case CodexOp.SourceKey:
					w.Write((byte)e.Kind);
					w.Write7BitEncodedInt(from);
					break;
				case CodexOp.Recipe:
					w.Write(e.Key);
					break;
				case CodexOp.Seen:
					w.Write7BitEncodedInt(item);
					break;
				case CodexOp.Hint:
					w.Write7BitEncodedInt(item);
					w.Write((byte)Math.Clamp(e.Count, 0, 255));
					break;
				case CodexOp.Sighting:
					w.Write(e.Biome ?? "");
					w.Write(npc);
					break;
				case CodexOp.Chest:
					w.Write(e.Key);
					List<(int type, int count)> items = new();
					if (e.Items != null)
					{
						foreach ((string name, int count) in e.Items)
						{
							int type = CodexNames.ItemType(name);
							if (type > 0 && count > 0) items.Add((type, count));
						}
					}
					w.Write7BitEncodedInt(items.Count);
					foreach ((int type, int count) in items)
					{
						w.Write7BitEncodedInt(type);
						w.Write7BitEncodedInt(count);
					}
					break;
			}
			return true;
		}

		public static CodexEntry Read(BinaryReader r)
		{
			CodexOp op = (CodexOp)r.ReadByte();
			switch (op)
			{
				case CodexOp.Kill:
					return CodexEntry.Kill(CodexNames.Npc(r.ReadInt32()), r.Read7BitEncodedInt());
				case CodexOp.Drop:
				{
					string npc = CodexNames.Npc(r.ReadInt32());
					string item = CodexNames.Item(r.Read7BitEncodedInt());
					return CodexEntry.Drop(npc, item, r.Read7BitEncodedInt());
				}
				case CodexOp.Source:
				{
					CodexSource kind = (CodexSource)r.ReadByte();
					string from = FromName(kind, r.Read7BitEncodedInt());
					string item = CodexNames.Item(r.Read7BitEncodedInt());
					return CodexEntry.Source(kind, from, item, r.Read7BitEncodedInt());
				}
				case CodexOp.SourceKey:
				{
					CodexSource kind = (CodexSource)r.ReadByte();
					return CodexEntry.SourceKey(kind, FromName(kind, r.Read7BitEncodedInt()));
				}
				case CodexOp.Recipe:
					return CodexEntry.Recipe(r.ReadInt32());
				case CodexOp.Seen:
					return CodexEntry.Seen(CodexNames.Item(r.Read7BitEncodedInt()));
				case CodexOp.Hint:
				{
					string item = CodexNames.Item(r.Read7BitEncodedInt());
					return CodexEntry.Hint(item, r.ReadByte());
				}
				case CodexOp.Sighting:
				{
					string biome = r.ReadString();
					return CodexEntry.Sighting(biome, CodexNames.Npc(r.ReadInt32()));
				}
				case CodexOp.Chest:
				{
					int position = r.ReadInt32();
					int count = r.Read7BitEncodedInt();
					if (count < 0 || count > Terraria.Chest.maxItems) throw new InvalidDataException("Chest with " + count + " items");
					List<(string, int)> items = new(count);
					for (int i = 0; i < count; i++)
					{
						string item = CodexNames.Item(r.Read7BitEncodedInt());
						items.Add((item, r.Read7BitEncodedInt()));
					}
					return CodexEntry.Chest(position, items);
				}
			}
			throw new InvalidDataException("Unknown codex entry " + (int)op);
		}

		// The source key on the wire: an item id, or a tile id + 1 for tiles (0: none)
		private static int FromId(CodexSource kind, string from)
		{
			if (string.IsNullOrEmpty(from)) return 0;
			if (kind == CodexSource.Tile) return CodexNames.TileType(from) + 1;
			return CodexNames.ItemType(from);
		}

		private static string FromName(CodexSource kind, int id) => id == 0 ? "" : kind == CodexSource.Tile ? CodexNames.Tile(id - 1) : CodexNames.Item(id);

		public string Summary()
		{
			StringBuilder sb = new();
			for (int kind = 0; kind < SourceKinds; kind++)
			{
				int items = Sources[kind].Values.Sum(t => t.Count);
				if (Sources[kind].Count == 0) continue;
				if (sb.Length > 0) sb.Append(", ");
				sb.Append((CodexSource)kind).Append(' ').Append(items);
			}
			return sb.Length == 0 ? "-" : sb.ToString();
		}
	}
}

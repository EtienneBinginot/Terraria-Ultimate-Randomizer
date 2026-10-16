using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader.IO;
using UltimateRandomizer.Common.Codex;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Tests
{
	// The codex data: what is recorded, saved and sent. Each test works on a fresh codex and puts the world's back.
	public static class CodexTests
	{
		public static void Register()
		{
			TestRunner.Register("CodexRecordsLoot", CodexRecordsLoot);
			TestRunner.Register("CodexRecipesFromGuide", CodexRecipesFromGuide);
			TestRunner.Register("CodexSaveLoad", CodexSaveLoad);
			TestRunner.Register("CodexSyncRoundTrip", CodexSyncRoundTrip);
		}

		private static CodexData FreshCodex() => new() { Seed = SeedSystem.WorldSeed, HasSeed = true };

		// A slime killed through NPCLoot: its kill and every item it dropped (coins and hearts aside) are recorded
		private static void CodexRecordsLoot()
		{
			if (Main.netMode != NetmodeID.SinglePlayer) Check.Skip("single player only");
			// Slime kills count towards the slime rain's boss
			if (Main.slimeRain) Check.Skip("slime rain in progress");
			CodexData saved = CodexSystem.Data;
			Player player = Main.LocalPlayer;
			int index = NPC.NewNPC(new EntitySource_DebugCommand("UltimateRandomizer test"), (int)player.Center.X + 1200, (int)player.Center.Y - 200, NPCID.BlueSlime);
			Check.That(index >= 0 && index < Main.maxNPCs, "could not spawn a slime");
			NPC npc = Main.npc[index];
			int key = CodexSystem.FightKey(npc);
			string creditId = ContentSamples.NpcBestiaryCreditIdsByNpcNetIds.TryGetValue(npc.netID, out string id) ? id : null;
			int bestiaryKills = creditId == null ? 0 : Main.BestiaryTracker.Kills.GetKillCount(creditId);
			int banner = Item.NPCtoBanner(npc.BannerID());
			bool hasBanner = banner > 0 && banner < NPC.killCount.Length;
			int bannerKills = hasBanner ? NPC.killCount[banner] : 0;
			List<int> slots = new();
			try
			{
				CodexSystem.Data = FreshCodex();
				// No banner this time (it is not loot anyway)
				if (hasBanner) NPC.killCount[banner] = 0;
				npc.playerInteraction[Main.myPlayer] = true;
				npc.life = 0;
				CodexSystem.SpawnLog = slots;
				npc.NPCLoot();
				CodexSystem.SpawnLog = null;
				Check.Equal(1, CodexSystem.KillCount(key), "kills recorded");

				Dictionary<int, int> spawned = new();
				foreach (int slot in slots.Distinct())
				{
					if (slot < 0 || slot >= Main.maxItems) continue;
					Item item = Main.item[slot];
					if (!item.active || item.IsAir || ItemID.Sets.CommonCoin[item.type] || ItemID.Sets.IsAPickup[item.type] || !LootSubstitutionItem.IsDeathLoot(npc, item.type)) continue;
					spawned[item.type] = spawned.GetValueOrDefault(item.type) + item.stack;
				}
				Check.That(spawned.Count > 0, "the slime dropped nothing");
				Dictionary<int, int> recorded = CodexSystem.DropsOf(key).ToDictionary(d => d.item, d => d.count);
				List<string> failures = new();
				foreach ((int type, int stack) in spawned)
				{
					if (recorded.GetValueOrDefault(type) != stack) failures.Add($"{Lang.GetItemNameValue(type)}: dropped {stack}, recorded {recorded.GetValueOrDefault(type)}");
				}
				foreach (int type in recorded.Keys)
				{
					if (!spawned.ContainsKey(type)) failures.Add($"{Lang.GetItemNameValue(type)} recorded but not dropped");
				}
				Check.All(failures, "drops recorded");
				CodexSystem.RefreshIndex();
				Check.That(CodexSystem.NpcsDropping(spawned.Keys.First()).Exists(d => d.npc == key), "reverse lookup of the drop");

				// Statue spawns are not kills (bestiary rule)
				npc.SpawnedFromStatue = true;
				CodexSystem.RecordKill(npc);
				Check.Equal(1, CodexSystem.KillCount(key), "kills after a statue kill");
			}
			finally
			{
				CodexSystem.SpawnLog = null;
				CodexSystem.Data = saved;
				npc.SpawnedFromStatue = false;
				npc.active = false;
				EnemySwapSystem.Pending[index] = false;
				CodexSystem.ClearSighting(index);
				foreach (int slot in slots)
				{
					if (slot >= 0 && slot < Main.maxItems && Main.item[slot].active) Main.item[slot].TurnToAir();
				}
				if (creditId != null) Main.BestiaryTracker.Kills.SetKillCountDirectly(creditId, bestiaryKills);
				if (hasBanner) NPC.killCount[banner] = bannerKills;
			}
		}

		// The Guide discovers the recipes using the item and those giving it
		private static void CodexRecipesFromGuide()
		{
			if (Main.netMode != NetmodeID.SinglePlayer) Check.Skip("single player only (clients wait for the server)");
			if (RecipeSystem.RecipeCount == 0) Check.Skip("no recipes");
			// An item that is both made by a recipe and used by another one
			int type = 0, giving = -1;
			for (int i = 0; i < Recipe.numRecipes && type == 0; i++)
			{
				Recipe recipe = Main.recipe[i];
				int result = recipe.createItem.type;
				if (recipe.Disabled || result <= ItemID.None) continue;
				for (int j = 0; j < Recipe.numRecipes; j++)
				{
					if (j != i && !Main.recipe[j].Disabled && Main.recipe[j].requiredItem.Exists(ingredient => ingredient.type == result))
					{
						type = result;
						giving = i;
						break;
					}
				}
			}
			if (type == 0) Check.Skip("no item is both made and used");

			HashSet<int> expected = new();
			for (int i = 0; i < Recipe.numRecipes; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (!recipe.Disabled && (recipe.createItem.type == type || recipe.requiredItem.Exists(ingredient => ingredient.type == type))) expected.Add(RecipeSystem.RecipeKey(i));
			}

			CodexData saved = CodexSystem.Data;
			Item guide = Main.guideItem;
			try
			{
				CodexSystem.Data = FreshCodex();
				Main.guideItem = new Item(type);
				CodexSystem.ScanGuide(force: true);
				CodexSystem.RefreshIndex();
				HashSet<int> found = CodexSystem.Data.Recipes;
				List<string> failures = new();
				foreach (int key in expected)
				{
					if (!found.Contains(key)) failures.Add("missing recipe key " + key);
				}
				// Recipes accepting the item through a group (any wood...) are found too, nothing else
				HashSet<int> allowed = new(expected);
				for (int i = 0; i < Recipe.numRecipes; i++)
				{
					if (RecipeSystem.InAcceptedGroup(i, type)) allowed.Add(RecipeSystem.RecipeKey(i));
				}
				foreach (int key in found)
				{
					if (!allowed.Contains(key)) failures.Add("unexpected recipe key " + key);
				}
				Check.All(failures, Lang.GetItemNameValue(type));
				Check.That(CodexSystem.RecipesGiving(type).Contains(giving), "the recipe giving the item is listed");
				Check.That(CodexSystem.RecipesUsing(type).Count > 0, "the recipes using the item are listed");
				Check.That(CodexSystem.HasSeen(type), "the item is seen");
			}
			finally
			{
				CodexSystem.Data = saved;
				Main.guideItem = guide;
				CodexSystem.ResetClientScan();
			}
		}

		// One discovery of each kind, with a slime colour (negative net id)
		private static List<CodexEntry> SampleEntries()
		{
			string slime = CodexNames.Npc(NPCID.BlueSlime);
			string pinky = CodexNames.Npc(NPCID.Pinky);
			string gel = CodexNames.Item(ItemID.Gel);
			string wood = CodexNames.Item(ItemID.Wood);
			return new List<CodexEntry>
			{
				CodexEntry.Kill(slime, 3),
				CodexEntry.Kill(pinky),
				CodexEntry.Drop(slime, gel, 5),
				CodexEntry.Drop(pinky, wood, 2),
				CodexEntry.Source(CodexSource.Pot, "", gel, 4),
				CodexEntry.Source(CodexSource.Bag, CodexNames.Item(ItemID.KingSlimeBossBag), wood, 7),
				CodexEntry.SourceKey(CodexSource.Bag, CodexNames.Item(ItemID.EyeOfCthulhuBossBag)),
				CodexEntry.Source(CodexSource.Shimmer, wood, gel, 1),
				CodexEntry.Source(CodexSource.Tile, CodexNames.Tile(TileID.Copper), CodexNames.Item(ItemID.TinOre), 3),
				CodexEntry.Source(CodexSource.Tile, CodexNames.Tile(TileID.Dirt), gel, 1),
				CodexEntry.Recipe(123456),
				CodexEntry.Seen(gel),
				CodexEntry.Sighting("Forest", slime),
				CodexEntry.Sighting("Forest", pinky),
				CodexEntry.Chest(CodexEntry.ChestPosition(100, 200), new List<(string, int)> { (wood, 50), (gel, 9) })
			};
		}

		private static CodexData Build(IEnumerable<CodexEntry> entries)
		{
			CodexData data = new() { Seed = 4242, HasSeed = true };
			foreach (CodexEntry e in entries) data.Apply(e);
			return data;
		}

		private static void CodexSaveLoad()
		{
			List<CodexEntry> entries = SampleEntries();
			// Entries of a mod that is not loaded any more are kept
			entries.Add(CodexEntry.Seen("NoSuchMod/NoSuchItem"));
			entries.Add(CodexEntry.Drop("NoSuchMod/NoSuchNpc", CodexNames.Item(ItemID.Gel), 2));
			entries.Add(CodexEntry.Kill("NoSuchMod/NoSuchNpc"));
			CodexData data = Build(entries);
			data.RecipeStamp = 77;
			data.HasRecipeStamp = true;
			TagCompound tag = new();
			data.Save(tag);
			MemoryStream stream = new();
			TagIO.ToStream(tag, stream, true);
			stream.Position = 0;
			CodexData loaded = CodexData.Load(TagIO.FromStream(stream, true));
			Check.Equal(4242, loaded.Seed, "seed");
			Check.Equal(77, loaded.RecipeStamp, "recipe stamp");
			Check.That(loaded.ItemsSeen.Contains("NoSuchMod/NoSuchItem"), "an item of an unloaded mod is kept");
			Check.That(loaded.HasSeed, "seed flag");
			Check.Equal(data.Signature(), loaded.Signature(), "content");
			Check.That(loaded.Sources[(int)CodexSource.Bag].ContainsKey(CodexNames.Item(ItemID.EyeOfCthulhuBossBag)), "an opened bag without content survives");
			Check.Equal(59, loaded.Sources[(int)CodexSource.Chest][""].Values.Sum(), "chest content");
		}

		private static void CodexSyncRoundTrip()
		{
			List<CodexEntry> entries = SampleEntries();
			MemoryStream stream = new();
			BinaryWriter writer = new(stream);
			int written = 0;
			foreach (CodexEntry e in entries)
			{
				if (CodexData.Write(writer, e)) written++;
			}
			// An item of a mod that is not loaded is not sent
			Check.That(!CodexData.Write(writer, CodexEntry.Seen("NoSuchMod/NoSuchItem")), "unknown item written");
			Check.Equal(entries.Count, written, "entries written");
			writer.Flush();
			stream.Position = 0;
			BinaryReader reader = new(stream);
			List<CodexEntry> read = new();
			for (int i = 0; i < written; i++) read.Add(CodexData.Read(reader));
			Check.Equal(stream.Length, stream.Position, "bytes read");
			Check.Equal(Build(entries).Signature(), Build(read).Signature(), "delta");

			// A full sync (every entry of a codex, applied to an empty one) rebuilds it exactly
			CodexData source = Build(entries);
			stream = new MemoryStream();
			writer = new BinaryWriter(stream);
			written = 0;
			foreach (CodexEntry e in source.AllEntries())
			{
				if (CodexData.Write(writer, e)) written++;
			}
			writer.Flush();
			stream.Position = 0;
			reader = new BinaryReader(stream);
			CodexData copy = new();
			for (int i = 0; i < written; i++) copy.Apply(CodexData.Read(reader));
			Check.Equal(source.Signature(), copy.Signature(), "full sync");

			// Packets: split in several payloads, every entry arrives once
			List<CodexEntry> many = new();
			for (int i = 0; i < 50; i++) many.AddRange(SampleEntries());
			List<(int count, byte[] bytes)> chunks = CodexSystem.SplitEntries(many, 200);
			Check.That(chunks.Count > 1, "entries were not split");
			CodexData split = new() { Seed = 4242, HasSeed = true };
			int total = 0;
			foreach ((int count, byte[] bytes) in chunks)
			{
				BinaryReader chunk = new(new MemoryStream(bytes));
				for (int i = 0; i < count; i++) split.Apply(CodexData.Read(chunk));
				Check.Equal(chunk.BaseStream.Length, chunk.BaseStream.Position, "bytes of a chunk");
				total += count;
			}
			Check.Equal(many.Count, total, "entries in the chunks");
			Check.Equal(Build(many).Signature(), split.Signature(), "split delta");

			// Batches: counts of the same thing are summed, set-like entries sent once
			List<CodexEntry> merged = CodexSystem.Merge(many);
			Check.Equal(entries.Count, merged.Count, "merged entries");
			Check.Equal(Build(many).Signature(), Build(merged).Signature(), "merged delta");

			// The server only takes a client's own kinds, with sane stacks, and chests that exist
			CodexEntry bag = CodexEntry.Source(CodexSource.Bag, CodexNames.Item(ItemID.KingSlimeBossBag), CodexNames.Item(ItemID.Gel), 1_000_000);
			Check.That(CodexSystem.AcceptFromClient(bag), "a client's bag entry is refused");
			Check.Equal(9999, bag.Count, "clamped stack");
			Check.That(!CodexSystem.AcceptFromClient(CodexEntry.Kill(CodexNames.Npc(NPCID.BlueSlime))), "a client's kill is accepted");
			Check.That(!CodexSystem.AcceptFromClient(CodexEntry.Source(CodexSource.Pot, "", CodexNames.Item(ItemID.Gel), 1)), "a client's pot entry is accepted");
			Check.That(!CodexSystem.AcceptFromClient(CodexEntry.Chest(CodexEntry.ChestPosition(0, 0), null)), "a chest that does not exist is accepted");
		}
	}
}

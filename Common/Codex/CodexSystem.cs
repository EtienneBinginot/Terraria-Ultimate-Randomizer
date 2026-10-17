using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Codex
{
	// The discoveries of this world (kills, drops, sources, recipes, items, enemies seen by biome), for the codex.
	// Saved with the world for its current seed (cleared when the seed changes). The server (or single player) records
	// kills, drops, world sources and sightings; clients record what only they see (bags, chests, fishing, Extractinator,
	// recipes, picked items) and send it to the server, which merges it and broadcasts every change in batches.
	public class CodexSystem : ModSystem
	{
		private static CodexData data = new();

		public static CodexData Data
		{
			get => data;
			internal set
			{
				data = value ?? new CodexData();
				index = null;
				version++;
				structureVersion++;
				lastDataVersion = data.Version;
				lastKeyVersion = data.KeyVersion;
				lastRecipeVersion = data.RecipeVersion;
			}
		}

		// For the interface: changes at most every few ticks while discoveries come in, and whenever the codex is replaced
		public static long Version => version;
		private static long version;
		private static int lastDataVersion;
		private const int VersionTicks = 15;

		// Moves only when something new is listed (new keys, recipes, codex replaced, recipes or tier changed), not for
		// counts: the interface rebuilds at once for these and refreshes counts more slowly
		public static long StructureVersion => structureVersion;
		private static long structureVersion;
		private static int lastKeyVersion, lastRecipeVersion;

		// Server: accepted changes waiting for the next broadcast. Client: discoveries waiting to be sent.
		private static readonly List<CodexEntry> outbox = new();
		// Client: set-like discoveries already sent, not yet confirmed by the server
		private static readonly HashSet<string> sent = new();
		private static int flushTimer;
		// Server: players that already received the full codex since they joined
		private static readonly bool[] fullSent = new bool[Main.maxPlayers + 1];
		// Recording happens on the game thread only (world generation and loading threads are ignored)
		private static int mainThread;

		private const int ServerFlushTicks = 60;
		private const int ClientFlushTicks = 30;
		public const int PacketBytes = 24000;
		private const int MaxEntriesPerPacket = 20000;

		// Entries a client may send: what happens only on its machine (the rest is recorded by the server)
		public static bool ClientMayRecord(CodexEntry e) => e.Op switch
		{
			CodexOp.Recipe or CodexOp.Seen or CodexOp.Chest or CodexOp.Hint => true,
			CodexOp.Source or CodexOp.SourceKey => e.Kind is CodexSource.Bag or CodexSource.Fishing or CodexSource.Extractinator,
			_ => false
		};

		public override void Load()
		{
			On_Item.NewItem_Inner += TrackNewItem;
			On_Item.GetShimmered += TrackShimmer;
			On_Player.ExtractinatorUse += TrackExtractinatorUse;
			On_Player.DropItemFromExtractinator += TrackExtractinator;
			SeedSystem.OnSeedChanged += OnSeedChanged;
			SeedSystem.OnSeedChanged += InvalidateRecipes;
			ProgressionSystem.OnTierChanged += InvalidateRecipes;
		}

		public override void Unload()
		{
			Data = new CodexData();
			outbox.Clear();
			sent.Clear();
			pendingHints.Clear();
			FullReceived = true;
		}

		public override void ClearWorld()
		{
			pendingReveals.Clear();
			pendingFound.Clear();
			revealedVersion = -1;
			Data = new CodexData();
			outbox.Clear();
			sent.Clear();
			pendingHints.Clear();
			FullReceived = true;
			flushTimer = 0;
			Array.Clear(fullSent);
			Array.Clear(sightState);
		}

		public override void SaveWorldData(TagCompound tag)
		{
			TagCompound codex = new();
			Data.Save(codex);
			tag["codex"] = codex;
		}

		public override void LoadWorldData(TagCompound tag)
		{
			CodexData loaded = null;
			try
			{
				if (tag.ContainsKey("codex"))
				{
					TagCompound codex = tag.GetCompound("codex");
					if (codex.GetInt("format") > CodexData.Format) Mod.Logger.Warn("The codex of this world was saved by a newer version of the mod: some of it may be lost.");
					loaded = CodexData.Load(codex);
				}
			}
			catch (Exception e)
			{
				Mod.Logger.Warn("The codex of this world could not be read: it starts empty.", e);
			}
			Data = loaded ?? new CodexData();
		}

		// The codex belongs to a seed: a new seed (command, new world) starts it again. A world randomized again with
		// other settings keeps its discoveries but not its recipes (they give other things now).
		private static void OnSeedChanged()
		{
			if (!SeedSystem.WorldReady || Main.netMode == NetmodeID.MultiplayerClient) return;
			bool reset = false, recipes = false;
			if (Data.HasSeed && Data.Seed != SeedSystem.WorldSeed)
			{
				Data.Clear();
				reset = true;
			}
			Data.Seed = SeedSystem.WorldSeed;
			Data.HasSeed = true;
			int stamp = DetRandom.StableHash(SeedSystem.CacheSignature());
			if (Data.HasRecipeStamp && Data.RecipeStamp != stamp && Data.Recipes.Count > 0)
			{
				Data.ClearRecipes();
				recipes = true;
			}
			Data.RecipeStamp = stamp;
			Data.HasRecipeStamp = true;
			if ((reset || recipes) && Main.netMode == NetmodeID.Server)
			{
				outbox.Clear();
				SendReset(-1);
				if (!reset) SendEntries(Data.AllEntries(), -1, -1);
			}
		}

		// ---- Recording ----

		public static void Submit(CodexEntry e)
		{
			if (!SeedSystem.WorldReady || e == null) return;
			if (mainThread != 0 && Environment.CurrentManagedThreadId != mainThread) return;
			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				// Applied when the server echoes it, so every machine counts it once
				if (!ClientMayRecord(e) || !Data.WouldChange(e)) return;
				string key = e.SetKey;
				if (key != null && !sent.Add(key)) return;
				outbox.Add(e);
				return;
			}
			if (!Data.Apply(e)) return;
			if (Main.netMode == NetmodeID.Server) outbox.Add(e);
			if (e.Op == CodexOp.Hint) QueueReveal(e);
			if (e.Op == CodexOp.Seen) QueueFound(e);
		}

		// ---- Reveals: the last level of a hint counts as discovering the enemy it names ----

		// Server (or single player): items whose last hint level was accepted, revealed once the hint facts are built. They
		// are built a little each tick (Warm), never while a packet is handled: a client must not wait for them
		private static readonly HashSet<string> pendingReveals = new();
		// HintFacts.CacheVersion of the last pass over every bought hint (old saves, facts rebuilt after a tier change)
		private static int revealedVersion = -1;

		public static void QueueReveal(CodexEntry hint)
		{
			if (hint.Count >= HintFacts.MaxLevel && Main.netMode != NetmodeID.MultiplayerClient && !string.IsNullOrEmpty(hint.Item)) pendingReveals.Add(hint.Item);
		}

		public static int PendingRevealCount => pendingReveals.Count;

		// Server (or single player): items seen in this world whose hint may not be fully known yet. A hint target found
		// anyway needs no hint: its last level is recorded for free (saved, synced, and its source revealed like a bought
		// one). The server only trusts its own codex: what it saw, or what a client reported seeing like any discovery
		private static readonly HashSet<string> pendingFound = new();

		public static void QueueFound(CodexEntry seen)
		{
			if (seen.Op == CodexOp.Seen && Main.netMode != NetmodeID.MultiplayerClient && !string.IsNullOrEmpty(seen.Item)) pendingFound.Add(seen.Item);
		}

		public static int PendingFoundCount => pendingFound.Count;

		// A hint target already seen: shown as fully known, never sold
		public static bool IsFound(int item) => HasSeen(item) && HintFacts.Ready && HintFacts.RawTarget(item) != null;

		// The level the interface shows: the last one for a found target
		public static int ShownHintLevel(int item) => IsFound(item) ? HintFacts.MaxLevel : HintLevel(item);

		private static void UnlockFound()
		{
			if (pendingFound.Count == 0) return;
			List<string> items = pendingFound.OrderBy(i => i, StringComparer.Ordinal).ToList();
			pendingFound.Clear();
			foreach (string name in items)
			{
				int item = CodexNames.ItemType(name);
				if (item <= 0 || !Data.ItemsSeen.Contains(name) || HintFacts.RawTarget(item) == null || Data.HintLevel(name) >= HintFacts.MaxLevel) continue;
				Submit(CodexEntry.Hint(name, HintFacts.MaxLevel));
			}
		}

		// Each tick on the server or in single player: warms the hint facts, then reveals what is waiting. force: build the
		// facts at once (tests)
		public static void ProcessReveals(bool force = false)
		{
			if (Main.netMode == NetmodeID.MultiplayerClient || !SeedSystem.WorldReady || !HintFacts.Ready) return;
			if (!HintFacts.Warmed)
			{
				if (!force)
				{
					HintFacts.Warm(2.0);
					return;
				}
				HintFacts.AllTargets();
			}
			if (revealedVersion != HintFacts.CacheVersion)
			{
				revealedVersion = HintFacts.CacheVersion;
				foreach ((string item, int level) in Data.Hints)
				{
					if (level >= HintFacts.MaxLevel) pendingReveals.Add(item);
				}
				// Old saves, and targets that changed with the tier or the settings
				pendingFound.UnionWith(Data.ItemsSeen);
			}
			UnlockFound();
			if (pendingReveals.Count == 0) return;
			List<string> items = pendingReveals.OrderBy(i => i, StringComparer.Ordinal).ToList();
			pendingReveals.Clear();
			foreach (string item in items) Reveal(item);
		}

		// The enemy or boss of the item's hint counts as discovered, through the usual entries (saved, and sent to
		// everyone): its page and portrait open (no kill is counted), the item shows among its drops, and it is seen in its
		// best biome (once, if it never was there). Running it again changes nothing
		private static void Reveal(string itemName)
		{
			try
			{
				int item = CodexNames.ItemType(itemName);
				HintFact fact = item > 0 ? HintFacts.For(item) : null;
				if (fact == null || fact.Kind is not (HintKind.Creature or HintKind.Boss) || fact.Npc == 0) return;
				string npc = CodexNames.Npc(RevealKey(fact));
				Submit(CodexEntry.Reveal(npc, itemName));
				string biome = fact.Biomes.FirstOrDefault();
				if (!string.IsNullOrEmpty(biome) && !(Data.Sightings.TryGetValue(biome, out Dictionary<string, int> seen) && seen.ContainsKey(npc))) Submit(CodexEntry.Sighting(biome, npc, 1));
			}
			catch (Exception e)
			{
				ModContent.GetInstance<UltimateRandomizer>().Logger.Warn($"Could not reveal the source of {itemName}: {e}");
			}
		}

		// The codex key of the enemy or boss a hint names (a boss for its parts, Retinazer for both Twins)
		public static int RevealKey(HintFact fact) => fact.Kind == HintKind.Boss
			? (BossParts.IsPart(fact.Npc) ? BossParts.Owner(fact.Npc) : IsTwin(fact.Npc) ? NPCID.Retinazer : fact.Npc)
			: fact.Npc;

		private static bool IsEaterSegment(int type) => type is NPCID.EaterofWorldsHead or NPCID.EaterofWorldsBody or NPCID.EaterofWorldsTail;

		private static bool IsTwin(int type) => type is NPCID.Retinazer or NPCID.Spazmatism;

		// The key a kill or a drop is counted under: the boss for its parts, Retinazer for both Twins, else the net id
		// (slime colours stay apart)
		public static int FightKey(NPC npc) => BossParts.IsPart(npc.type) ? BossParts.Owner(npc.type) : IsTwin(npc.type) ? NPCID.Retinazer : npc.netID;

		// Bestiary rules: a player took part, no statue spawns
		private static bool Credited(NPC npc) => !npc.SpawnedFromStatue && npc.GetWereThereAnyInteractions();

		private static bool AnyOtherActive(NPC npc, Func<int, bool> sameFight)
		{
			for (int i = 0; i < Main.maxNPCs; i++)
			{
				NPC other = Main.npc[i];
				if (i != npc.whoAmI && other.active && sameFight(other.type)) return true;
			}
			return false;
		}

		// One kill per fight: parts count through their boss, the Eater of Worlds with its last segment, the Twins with the last one
		public static void RecordKill(NPC npc)
		{
			if (!Credited(npc)) return;
			int type = npc.type;
			if (IsEaterSegment(type))
			{
				if (AnyOtherActive(npc, IsEaterSegment)) return;
			}
			else if (BossParts.IsPart(type)) return;
			else if (IsTwin(type) && AnyOtherActive(npc, IsTwin)) return;
			Submit(CodexEntry.Kill(CodexNames.Npc(FightKey(npc))));
		}

		public static void RecordDrop(int key, int item, int stack) => Submit(CodexEntry.Drop(CodexNames.Npc(key), CodexNames.Item(item), Math.Max(1, stack)));

		public static void RecordSource(CodexSource kind, int from, int item, int stack)
		{
			if (item <= ItemID.None) return;
			Submit(CodexEntry.Source(kind, from > 0 ? CodexNames.Item(from) : "", CodexNames.Item(item), Math.Max(1, stack)));
		}

		public static void RecordBagOpened(int bag) => Submit(CodexEntry.SourceKey(CodexSource.Bag, CodexNames.Item(bag)));

		// ---- Hints (bought on the client that talks to the Guide, after it paid) ----

		// Client: a level bought and sent, not confirmed by the server yet. A second click must not pay the same level
		// twice; a level the server refuses (or never confirms) is refunded, so the client never stays ahead of it.
		private sealed class PendingHint
		{
			public int Level;
			public long Price;
			public uint Tick;
			public Action Accepted;
		}

		private static readonly Dictionary<string, List<PendingHint>> pendingHints = new();
		public const int HintTimeoutTicks = 180;

		// Client: the server's full codex arrived since joining (hint levels are known); always true elsewhere
		public static bool FullReceived { get; private set; } = true;

		public static bool CanBuyHints => Main.netMode != NetmodeID.MultiplayerClient || FullReceived;

		public static int PendingHintCount => pendingHints.Values.Sum(list => list.Count);

		public static int HintLevel(int item)
		{
			string name = CodexNames.Item(item);
			int level = Data.HintLevel(name);
			if (pendingHints.TryGetValue(name, out List<PendingHint> list))
			{
				foreach (PendingHint pending in list) level = Math.Max(level, pending.Level);
			}
			return level;
		}

		// accepted runs once the level is recorded (at once in single player, on the server's echo in multiplayer)
		public static void RecordHint(int item, int level, long price = 0, Action accepted = null)
		{
			if (item <= ItemID.None) return;
			string name = CodexNames.Item(item);
			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				if (!pendingHints.TryGetValue(name, out List<PendingHint> list)) pendingHints[name] = list = new List<PendingHint>();
				list.Add(new PendingHint { Level = level, Price = price, Tick = Main.GameUpdateCount, Accepted = accepted });
				Submit(CodexEntry.Hint(name, level));
				return;
			}
			Submit(CodexEntry.Hint(name, level));
			if (Data.HintLevel(name) >= level) accepted?.Invoke();
		}

		// Client: the server's level arrived; pending levels up to it are confirmed
		private static void ConfirmHints(string name, int serverLevel, bool refundOthers)
		{
			if (!pendingHints.TryGetValue(name, out List<PendingHint> list)) return;
			foreach (PendingHint pending in list.ToList())
			{
				if (pending.Level <= serverLevel)
				{
					list.Remove(pending);
					pending.Accepted?.Invoke();
				}
				else if (refundOthers)
				{
					list.Remove(pending);
					DropPending(name, pending);
				}
			}
			if (list.Count == 0) pendingHints.Remove(name);
		}

		private static void DropPending(string name, PendingHint pending)
		{
			sent.Remove(CodexEntry.Hint(name, pending.Level).SetKey);
			Refund(pending.Price);
		}

		// The coins paid for a refused level, back to the local player
		public static void Refund(long price)
		{
			if (price <= 0 || Main.dedServ) return;
			Player player = Main.LocalPlayer;
			int[] split = Utils.CoinsSplit(price);
			int[] coins = { ItemID.CopperCoin, ItemID.SilverCoin, ItemID.GoldCoin, ItemID.PlatinumCoin };
			for (int i = 0; i < coins.Length; i++)
			{
				if (split[i] > 0) player.QuickSpawnItem(player.GetSource_Misc("HintRefund"), coins[i], split[i]);
			}
		}

		// Client: levels the server never confirmed are refunded
		private static void ExpireHints()
		{
			foreach (string name in pendingHints.Keys.ToList())
			{
				List<PendingHint> list = pendingHints[name];
				foreach (PendingHint pending in list.Where(p => Main.GameUpdateCount - p.Tick > HintTimeoutTicks).ToList())
				{
					list.Remove(pending);
					DropPending(name, pending);
				}
				if (list.Count == 0) pendingHints.Remove(name);
			}
		}

		private static void RefundAllHints()
		{
			foreach ((string name, List<PendingHint> list) in pendingHints.ToList())
			{
				foreach (PendingHint pending in list) DropPending(name, pending);
			}
			pendingHints.Clear();
		}

		private static void SendHintRefused(int toClient, CodexEntry e)
		{
			int item = CodexNames.ItemType(e.Item);
			if (item <= 0 || toClient < 0 || toClient >= Main.maxPlayers) return;
			ModPacket packet = NewPacket(PacketType.HintRefused);
			packet.Write7BitEncodedInt(item);
			packet.Write((byte)Math.Clamp(Data.HintLevel(e.Item), 0, 255));
			packet.Send(toClient);
		}

		// The last hint level shows the recipe: it counts as discovered (its result is not marked as seen)
		public static void RecordRecipeOnly(int recipeIndex)
		{
			if (recipeIndex < 0 || recipeIndex >= RecipeSystem.RecipeCount || recipeIndex >= Recipe.numRecipes) return;
			int key = RecipeSystem.RecipeKey(recipeIndex);
			if (!Data.Recipes.Contains(key)) Submit(CodexEntry.Recipe(key));
		}

		public static void RecordSeen(int item)
		{
			if (item > ItemID.None) Submit(CodexEntry.Seen(CodexNames.Item(item)));
		}

		public static void RecordChest(Chest chest)
		{
			if (chest == null) return;
			int position = CodexEntry.ChestPosition(chest.x, chest.y);
			if (Data.OpenedChests.Contains(position)) return;
			List<(string, int)> items = new();
			foreach (Item item in chest.item)
			{
				if (item != null && !item.IsAir) items.Add((CodexNames.Item(item.type), item.stack));
			}
			Submit(CodexEntry.Chest(position, items));
			foreach (Item item in chest.item)
			{
				if (item != null && !item.IsAir) RecordSeen(item.type);
			}
		}

		// A recipe seen at a station or in the Guide: its result and ingredients are seen too
		public static void RecordRecipe(int recipeIndex)
		{
			if (recipeIndex < 0 || recipeIndex >= RecipeSystem.RecipeCount || recipeIndex >= Recipe.numRecipes) return;
			int key = RecipeSystem.RecipeKey(recipeIndex);
			if (Data.Recipes.Contains(key) || sent.Contains("r" + key)) return;
			Submit(CodexEntry.Recipe(key));
			Recipe recipe = Main.recipe[recipeIndex];
			RecordSeen(recipe.createItem.type);
			foreach (Item ingredient in recipe.requiredItem) RecordSeen(ingredient.type);
		}

		// ---- Item sources (every Item.NewItem, after all OnSpawn hooks: the item is final) ----

		[ThreadStatic]
		internal static Item lastSpawned;
		[ThreadStatic]
		private static int shimmerFrom;
		[ThreadStatic]
		private static bool inExtractinatorUse;

		// Tests: the slots of the items spawned while it is set
		internal static List<int> SpawnLog;

		// Server: an expert bag is spawned once per player (NPC.DropItemInstanced); it counts once
		private static readonly HashSet<long> instancedThisTick = new();
		private static uint instancedTick;

		private static int TrackNewItem(On_Item.orig_NewItem_Inner orig, IEntitySource source, int x, int y, int width, int height, Item itemToClone, int type, int stack,
			bool noBroadcast, int pfix, bool noGrabDelay, bool reverseLookup)
		{
			Item outer = lastSpawned;
			lastSpawned = null;
			// The type asked for, before the randomizer's OnSpawn replaces it
			int original = itemToClone != null && !itemToClone.IsAir ? itemToClone.type : type;
			int result;
			try
			{
				result = orig(source, x, y, width, height, itemToClone, type, stack, noBroadcast, pfix, noGrabDelay, reverseLookup);
				// Early returns (loot blocked, world generation) never reach OnSpawn: nothing to record
				if (lastSpawned != null && !lastSpawned.IsAir)
				{
					SpawnLog?.Add(result);
					Classify(source, lastSpawned, original, noBroadcast);
				}
			}
			finally
			{
				lastSpawned = outer;
			}
			return result;
		}

		private static void Classify(IEntitySource source, Item item, int original, bool noBroadcast)
		{
			if (!SeedSystem.WorldReady || !ItemPool.Ready) return;
			bool authority = Main.netMode != NetmodeID.MultiplayerClient;
			switch (source)
			{
				case EntitySource_Loot loot when loot.Entity is NPC npc:
					if (!authority || IsPickupOrCoin(item.type) || !Credited(npc) || !LootSubstitutionItem.IsDeathLoot(npc, original)) return;
					if (noBroadcast && Main.netMode == NetmodeID.Server && !FirstInstance(npc, original)) return;
					RecordDrop(FightKey(npc), item.type, item.stack);
					break;
				case EntitySource_ItemOpen open when LootSubstitutionItem.IsBag(open.ItemType):
					if (IsPickupOrCoin(item.type)) return;
					RecordSource(CodexSource.Bag, open.ItemType, item.type, item.stack);
					break;
				case EntitySource_ShakeTree:
					if (authority && !IsPickupOrCoin(item.type)) RecordSource(CodexSource.Tree, 0, item.type, item.stack);
					break;
				case EntitySource_TileBreak tileBreak:
					if (!authority || IsPickupOrCoin(item.type)) return;
					if (TileDropItem.PotLoot)
					{
						RecordSource(CodexSource.Pot, 0, item.type, item.stack);
						break;
					}
					int tile = RandomizedTile(tileBreak, original);
					if (tile >= 0) Submit(CodexEntry.Source(CodexSource.Tile, CodexNames.Tile(tile), CodexNames.Item(item.type), Math.Max(1, item.stack)));
					break;
				default:
					if (shimmerFrom > 0 && authority) RecordSource(CodexSource.Shimmer, shimmerFrom, item.type, item.stack);
					break;
			}
		}

		// Tile breaks also come from walls, item frames, racks, mannequins, pylons, broken chests: only the tiles whose
		// drop the mod changes count (ores and gems, herbs). The tile is still there while its drops spawn.
		private static int RandomizedTile(EntitySource_TileBreak source, int original)
		{
			int x = source.TileCoords.X, y = source.TileCoords.Y;
			if (!WorldGen.InWorld(x, y) || original <= ItemID.None || !ContentSamples.ItemsByType.TryGetValue(original, out Item sample)) return -1;
			Tile tile = Main.tile[x, y];
			if (!tile.HasTile || sample.createWall > WallID.None) return -1;
			int type = tile.TileType;
			if (OreMap.IsOre(original) && TileID.Sets.Ore[type]) return type;
			// Herbs and plants, not their seeds
			if (WorldSourcesItem.IsPlantTile(type) && sample.createTile < TileID.Dirt) return type;
			return -1;
		}

		private static bool FirstInstance(NPC npc, int original)
		{
			if (instancedTick != Main.GameUpdateCount)
			{
				instancedTick = Main.GameUpdateCount;
				instancedThisTick.Clear();
			}
			return instancedThisTick.Add(((long)npc.whoAmI << 32) | (uint)original);
		}

		private static bool IsPickupOrCoin(int type) => type <= ItemID.None || ItemID.Sets.CommonCoin[type] || ItemID.Sets.IsAPickup[type];

		// Shimmer changes the item in place (and spawns the extra stack or the decrafted ingredients with NewItem).
		// Vanilla only shimmers in single player or on the server.
		private static void TrackShimmer(On_Item.orig_GetShimmered orig, Item item)
		{
			int before = item.type;
			int outer = shimmerFrom;
			shimmerFrom = before;
			try
			{
				orig(item);
			}
			finally
			{
				shimmerFrom = outer;
			}
			if (!item.IsAir && item.type != before && Main.netMode != NetmodeID.MultiplayerClient && SeedSystem.WorldReady && ItemPool.Ready)
			{
				RecordSource(CodexSource.Shimmer, before, item.type, item.stack);
			}
		}

		// The Chlorophyte Extractinator's trades also drop through DropItemFromExtractinator, but not from ExtractinatorUse
		private static void TrackExtractinatorUse(On_Player.orig_ExtractinatorUse orig, Player player, int extractType, int extractinatorBlockType)
		{
			bool outer = inExtractinatorUse;
			inExtractinatorUse = true;
			try
			{
				orig(player, extractType, extractinatorBlockType);
			}
			finally
			{
				inExtractinatorUse = outer;
			}
		}

		// Runs on the client using the Extractinator, with the final result (after the randomizer's ExtractinatorUse)
		private static void TrackExtractinator(On_Player.orig_DropItemFromExtractinator orig, Player player, int itemType, int stack)
		{
			orig(player, itemType, stack);
			if (inExtractinatorUse && player.whoAmI == Main.myPlayer && SeedSystem.WorldReady && !IsPickupOrCoin(itemType))
			{
				RecordSource(CodexSource.Extractinator, 0, itemType, stack);
			}
		}

		// ---- Sightings: natural spawns, in the biome they spawned in, once a player can see them ----

		// Per NPC slot (globals are recreated by SetDefaults/Transform): 0 none, 1 just spawned, 2 waiting to be seen
		private static readonly byte[] sightState = new byte[Main.maxNPCs + 1];
		private static readonly string[] sightBiome = new string[Main.maxNPCs + 1];
		private static readonly int[] sightTime = new int[Main.maxNPCs + 1];
		private const int SightTimeout = 60 * 60;

		public static void MarkSpawn(NPC npc, bool natural)
		{
			if (npc.whoAmI < 0 || npc.whoAmI >= Main.maxNPCs) return;
			sightState[npc.whoAmI] = natural ? (byte)1 : (byte)0;
		}

		public static void ClearSighting(int slot)
		{
			if (slot >= 0 && slot < Main.maxNPCs) sightState[slot] = 0;
		}

		private static void UpdateSightings()
		{
			bool check = Main.GameUpdateCount % 10 == 0;
			for (int i = 0; i < Main.maxNPCs; i++)
			{
				byte state = sightState[i];
				if (state == 0) continue;
				NPC npc = Main.npc[i];
				if (!npc.active)
				{
					sightState[i] = 0;
					continue;
				}
				// After the swap: the enemy that is really there
				if (EnemySwapSystem.Pending[i]) continue;
				if (state == 1)
				{
					int closest = npc.FindClosestPlayer();
					List<int> biomes = closest >= 0 && closest < Main.maxPlayers && Main.player[closest].active
						? BiomeSlots.BiomesOf(Main.player[closest], npc) : new List<int>();
					if (biomes.Count == 0)
					{
						sightState[i] = 0;
						continue;
					}
					sightBiome[i] = BiomeSlots.BiomeName(biomes[0]);
					sightTime[i] = 0;
					sightState[i] = 2;
					continue;
				}
				if (!check) continue;
				sightTime[i] += 10;
				if (sightTime[i] > SightTimeout)
				{
					sightState[i] = 0;
				}
				else if (InView(npc))
				{
					sightState[i] = 0;
					Submit(CodexEntry.Sighting(sightBiome[i], CodexNames.Npc(npc.netID)));
				}
			}
		}

		private static bool InView(NPC npc)
		{
			float rangeX = NPC.sWidth / 2f + npc.width;
			float rangeY = NPC.sHeight / 2f + npc.height;
			for (int p = 0; p < Main.maxPlayers; p++)
			{
				Player player = Main.player[p];
				if (!player.active || player.dead) continue;
				if (Math.Abs(player.Center.X - npc.Center.X) < rangeX && Math.Abs(player.Center.Y - npc.Center.Y) < rangeY) return true;
			}
			return false;
		}

		public override void PostUpdateEverything()
		{
			mainThread = Environment.CurrentManagedThreadId;
			if (Main.GameUpdateCount % VersionTicks == 0 && data.Version != lastDataVersion)
			{
				lastDataVersion = data.Version;
				version++;
				if (data.KeyVersion != lastKeyVersion || data.RecipeVersion != lastRecipeVersion)
				{
					lastKeyVersion = data.KeyVersion;
					lastRecipeVersion = data.RecipeVersion;
					structureVersion++;
				}
			}
			if (!SeedSystem.WorldReady) return;
			if (Main.netMode != NetmodeID.MultiplayerClient)
			{
				UpdateSightings();
				ProcessReveals();
			}
			if (Main.netMode == NetmodeID.SinglePlayer) return;
			if (Main.netMode == NetmodeID.MultiplayerClient && pendingHints.Count > 0) ExpireHints();
			if (++flushTimer < (Main.netMode == NetmodeID.Server ? ServerFlushTicks : ClientFlushTicks)) return;
			flushTimer = 0;
			Flush();
		}

		public static void OnPlayerLeft(int player)
		{
			if (player >= 0 && player < fullSent.Length) fullSent[player] = false;
		}

		// ---- Network ----

		private static void Flush()
		{
			if (outbox.Count == 0) return;
			List<CodexEntry> entries = Merge(outbox);
			outbox.Clear();
			// Clients send to the server; the server broadcasts to everyone (the sender applies its own discoveries then)
			SendEntries(entries, -1, -1);
		}

		// Counts of the same thing are summed (a miner breaks the same ore many times a second); set-like entries are sent once
		public static List<CodexEntry> Merge(IEnumerable<CodexEntry> entries)
		{
			List<CodexEntry> merged = new();
			Dictionary<string, CodexEntry> byKey = new();
			HashSet<string> sets = new();
			foreach (CodexEntry e in entries)
			{
				if (e.Op is CodexOp.Kill or CodexOp.Drop or CodexOp.Source or CodexOp.Sighting)
				{
					string key = $"{(int)e.Op}|{(int)e.Kind}|{e.From}|{e.Npc}|{e.Item}|{e.Biome}";
					if (byKey.TryGetValue(key, out CodexEntry first))
					{
						first.Count = (int)Math.Min((long)first.Count + e.Count, int.MaxValue);
						continue;
					}
					// A copy: the original may still be referenced elsewhere
					CodexEntry copy = new() { Op = e.Op, Kind = e.Kind, Npc = e.Npc, From = e.From, Item = e.Item, Biome = e.Biome, Count = e.Count };
					byKey[key] = copy;
					merged.Add(copy);
				}
				else if (e.SetKey == null || sets.Add(e.SetKey))
				{
					merged.Add(e);
				}
			}
			return merged;
		}

		private static ModPacket NewPacket(PacketType type)
		{
			ModPacket packet = ModContent.GetInstance<UltimateRandomizer>().GetPacket();
			packet.Write((byte)type);
			return packet;
		}

		// The entries as packet payloads of about maxBytes each: (entry count, bytes)
		public static List<(int count, byte[] bytes)> SplitEntries(IEnumerable<CodexEntry> entries, int maxBytes)
		{
			List<(int, byte[])> chunks = new();
			MemoryStream buffer = new();
			BinaryWriter writer = new(buffer);
			int count = 0;
			void Cut()
			{
				if (count == 0) return;
				writer.Flush();
				chunks.Add((count, buffer.ToArray()));
				buffer.SetLength(0);
				count = 0;
			}
			foreach (CodexEntry e in entries)
			{
				if (CodexData.Write(writer, e)) count++;
				writer.Flush();
				if (buffer.Length > maxBytes || count >= MaxEntriesPerPacket) Cut();
			}
			Cut();
			return chunks;
		}

		public static void SendEntries(IEnumerable<CodexEntry> entries, int toClient, int ignoreClient)
		{
			foreach ((int count, byte[] bytes) in SplitEntries(entries, PacketBytes))
			{
				ModPacket packet = NewPacket(PacketType.CodexEntries);
				packet.Write7BitEncodedInt(count);
				packet.Write(bytes);
				packet.Send(toClient, ignoreClient);
			}
		}

		private static void SendReset(int toClient)
		{
			ModPacket packet = NewPacket(PacketType.CodexReset);
			packet.Send(toClient);
		}

		// Client, entering a world: asks for the whole codex
		public static void RequestFull()
		{
			if (Main.netMode != NetmodeID.MultiplayerClient) return;
			FullReceived = false;
			NewPacket(PacketType.CodexRequestFull).Send();
		}

		public static void HandlePacket(PacketType type, BinaryReader reader, int whoAmI)
		{
			try
			{
				switch (type)
				{
					case PacketType.CodexRequestFull when Main.netMode == NetmodeID.Server:
						if (whoAmI < 0 || whoAmI >= Main.maxPlayers || fullSent[whoAmI]) return;
						fullSent[whoAmI] = true;
						// Pending changes go out first: the full codex below includes them, and the reset drops their copy
						Flush();
						SendReset(whoAmI);
						SendEntries(Data.AllEntries(), whoAmI, -1);
						NewPacket(PacketType.CodexFullDone).Send(whoAmI);
						break;
					case PacketType.CodexReset when Main.netMode == NetmodeID.MultiplayerClient:
						RefundAllHints();
						Data.Clear();
						sent.Clear();
						outbox.Clear();
						break;
					case PacketType.CodexFullDone when Main.netMode == NetmodeID.MultiplayerClient:
						FullReceived = true;
						break;
					case PacketType.HintRefused when Main.netMode == NetmodeID.MultiplayerClient:
					{
						string name = CodexNames.Item(reader.Read7BitEncodedInt());
						int level = reader.ReadByte();
						ConfirmHints(name, Math.Max(level, Data.HintLevel(name)), refundOthers: true);
						break;
					}
					case PacketType.CodexEntries:
						ReceiveEntries(reader, whoAmI);
						break;
				}
			}
			catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or FormatException)
			{
				ModContent.GetInstance<UltimateRandomizer>().Logger.Warn($"Malformed codex packet from {whoAmI}: {e.Message}");
			}
		}

		private static void ReceiveEntries(BinaryReader reader, int whoAmI)
		{
			int count = reader.Read7BitEncodedInt();
			if (count < 0 || count > MaxEntriesPerPacket) throw new InvalidDataException("Codex packet with " + count + " entries");
			for (int i = 0; i < count; i++)
			{
				CodexEntry e = CodexData.Read(reader);
				if (Main.netMode == NetmodeID.Server)
				{
					// A refused hint level is answered, so that its buyer gets the coins back
					if (!AcceptFromClient(e) || !Data.Apply(e))
					{
						if (e.Op == CodexOp.Hint) SendHintRefused(whoAmI, e);
						continue;
					}
					outbox.Add(e);
					if (e.Op == CodexOp.Hint) QueueReveal(e);
					if (e.Op == CodexOp.Seen) QueueFound(e);
				}
				else
				{
					Data.Apply(e);
					string key = e.SetKey;
					if (key != null) sent.Remove(key);
					if (e.Op == CodexOp.Hint) ConfirmHints(e.Item, Data.HintLevel(e.Item), refundOthers: false);
				}
			}
		}

		// What the server takes from a client: its kinds only, sane stacks, chests that exist
		public static bool AcceptFromClient(CodexEntry e)
		{
			if (!ClientMayRecord(e)) return false;
			Sanitize(e);
			if (e.Op == CodexOp.Chest && Chest.FindChest(e.Key & 0xFFFF, e.Key >> 16) < 0) return false;
			if (e.Op == CodexOp.Hint && !AcceptHint(e, Data, ProgressionSystem.UnlockedTier)) return false;
			return true;
		}

		// A hint level: the next one of a target offered now (the client paid for it, like it pays in shops)
		public static bool AcceptHint(CodexEntry e, CodexData data, int unlockedTier)
		{
			int item = CodexNames.ItemType(e.Item);
			if (item <= 0 || e.Count != data.HintLevel(e.Item) + 1 || e.Count > HintFacts.MaxLevel) return false;
			// Only the tier: the facts are not needed to accept a purchase (the reveal of the last level waits for them)
			HintTarget target = HintFacts.RawTarget(item);
			return target != null && target.Tier <= unlockedTier + 1;
		}

		// A client's stacks: never more than a few thousand per entry
		public static void Sanitize(CodexEntry e)
		{
			e.Count = Math.Clamp(e.Count, 1, 9999);
			if (e.Items == null) return;
			if (e.Items.Count > Chest.maxItems) e.Items.RemoveRange(Chest.maxItems, e.Items.Count - Chest.maxItems);
			for (int i = 0; i < e.Items.Count; i++) e.Items[i] = (e.Items[i].item, Math.Clamp(e.Items[i].count, 1, 9999));
		}

		// ---- Queries (for the codex interface) ----

		public static int KillCount(int netId) => Data.Kills.TryGetValue(CodexNames.Npc(netId), out int count) ? count : 0;

		// NPCs killed at least once (net ids; a boss's parts and both Twins are counted under the boss / Retinazer)
		public static List<int> KilledNpcs() => Data.Kills.Where(k => k.Value > 0).Select(k => CodexNames.NpcId(k.Key)).Where(id => id != 0).ToList();

		// Enemies killed or revealed by a hint (their page opens; a revealed one shows 0 kills)
		public static List<int> KnownNpcs() => KilledNpcs().Concat(Data.Revealed.Keys.Select(CodexNames.NpcId).Where(id => id != 0)).Distinct().ToList();

		public static bool IsRevealed(int netId) => Data.Revealed.ContainsKey(CodexNames.Npc(netId));

		// Drops found, then those a hint revealed (count 0)
		public static List<(int item, int count)> DropsOf(int netId)
		{
			string npc = CodexNames.Npc(netId);
			List<(int item, int count)> drops = Data.Drops.TryGetValue(npc, out Dictionary<string, int> items) ? ToTypes(items) : new List<(int, int)>();
			if (Data.Revealed.TryGetValue(npc, out HashSet<string> revealed))
			{
				foreach (string name in revealed.OrderBy(n => n, StringComparer.Ordinal))
				{
					int type = CodexNames.ItemType(name);
					if (type > 0 && !drops.Any(d => d.item == type)) drops.Add((type, 0));
				}
			}
			return drops;
		}

		public static bool HasOpenedBag(int bag) => Data.Sources[(int)CodexSource.Bag].ContainsKey(CodexNames.Item(bag));

		public static List<int> OpenedBags() => Data.Sources[(int)CodexSource.Bag].Keys.Select(CodexNames.ItemType).Where(t => t > 0).ToList();

		public static List<(int item, int count)> BagContents(int bag) =>
			Data.Sources[(int)CodexSource.Bag].TryGetValue(CodexNames.Item(bag), out Dictionary<string, int> items) ? ToTypes(items) : new List<(int, int)>();

		public static bool HasSeen(int item) => Data.ItemsSeen.Contains(CodexNames.Item(item));

		public static List<int> ItemsSeen() => Data.ItemsSeen.Select(CodexNames.ItemType).Where(t => t > 0).ToList();

		public static bool IsRecipeDiscovered(int recipeIndex) =>
			recipeIndex >= 0 && recipeIndex < RecipeSystem.RecipeCount && Data.Recipes.Contains(RecipeSystem.RecipeKey(recipeIndex));

		public static List<string> BiomesWhereSeen(int netId)
		{
			string npc = CodexNames.Npc(netId);
			return Data.Sightings.Where(s => s.Value.ContainsKey(npc)).Select(s => s.Key).ToList();
		}

		public static List<int> NpcsSeenIn(string biome) =>
			Data.Sightings.TryGetValue(biome, out Dictionary<string, int> npcs) ? npcs.Keys.Select(CodexNames.NpcId).Where(id => id != 0).ToList() : new List<int>();

		// How many natural spawns of the enemy were seen, in all biomes
		public static int SightingCount(int netId)
		{
			string npc = CodexNames.Npc(netId);
			int total = 0;
			foreach (Dictionary<string, int> npcs in Data.Sightings.Values) total += npcs.GetValueOrDefault(npc);
			return total;
		}

		// Share of the enemy among the spawns seen in the biome (0 to 1; 0 when never seen there)
		public static float SpawnShare(string biome, int netId)
		{
			if (!Data.Sightings.TryGetValue(biome, out Dictionary<string, int> npcs)) return 0f;
			long total = 0;
			foreach (int count in npcs.Values) total += count;
			return total <= 0 ? 0f : npcs.GetValueOrDefault(CodexNames.Npc(netId)) / (float)total;
		}

		public static List<(int npc, int count)> NpcsDropping(int item) => Index().DroppedBy.TryGetValue(item, out List<(int, int)> list) ? list : new List<(int, int)>();

		// Sources other than enemies: kind, from (the bag or shimmered item; the tile type for tiles; 0 otherwise), count
		public static List<(CodexSource kind, int from, int count)> SourcesOf(int item) =>
			Index().Sources.TryGetValue(item, out List<(CodexSource, int, int)> list) ? list : new List<(CodexSource, int, int)>();

		// Discovered recipes (indices in Main.recipe) giving or using the item, with the recipes as they are now
		public static List<int> RecipesGiving(int item) => Index().Giving.TryGetValue(item, out List<int> list) ? list : new List<int>();

		public static List<int> RecipesUsing(int item) => Index().Using.TryGetValue(item, out List<int> list) ? list : new List<int>();

		private static List<(int, int)> ToTypes(Dictionary<string, int> items) =>
			items.Select(p => (CodexNames.ItemType(p.Key), p.Value)).Where(p => p.Item1 > 0).OrderByDescending(p => p.Value).ToList();

		// Reverse lookups. Recipes: rebuilt when the discovered set or the applied recipes change. Drops and sources:
		// rebuilt at once for a new key, at most every RebuildTicks for counts only.
		private class QueryIndex
		{
			public int KeyVersion = -1, CountVersion = -1, RecipeVersion = -1;
			public bool RecipesStale = true;
			public uint BuiltAt;
			public Dictionary<int, List<(int, int)>> DroppedBy = new();
			public Dictionary<int, List<(CodexSource, int, int)>> Sources = new();
			public Dictionary<int, List<int>> Giving = new();
			public Dictionary<int, List<int>> Using = new();
		}

		private static QueryIndex index;
		private const int RebuildTicks = 30;

		private static void InvalidateRecipes()
		{
			if (index != null) index.RecipesStale = true;
			structureVersion++;
		}

		// Tests: forget the caches
		public static void RefreshIndex() => index = null;

		private static QueryIndex Index()
		{
			index ??= new QueryIndex();
			CodexData d = Data;
			if (index.RecipesStale || index.RecipeVersion != d.RecipeVersion) BuildRecipes(index, d);
			bool keys = index.KeyVersion != d.KeyVersion;
			bool counts = index.CountVersion != d.CountVersion && Main.GameUpdateCount - index.BuiltAt >= RebuildTicks;
			if (keys || counts) BuildSources(index, d);
			return index;
		}

		private static void BuildSources(QueryIndex q, CodexData d)
		{
			q.KeyVersion = d.KeyVersion;
			q.CountVersion = d.CountVersion;
			q.BuiltAt = Main.GameUpdateCount;
			q.DroppedBy = new Dictionary<int, List<(int, int)>>();
			q.Sources = new Dictionary<int, List<(CodexSource, int, int)>>();
			foreach ((string npcName, Dictionary<string, int> items) in d.Drops)
			{
				int npc = CodexNames.NpcId(npcName);
				if (npc == 0) continue;
				foreach ((string itemName, int count) in items)
				{
					int item = CodexNames.ItemType(itemName);
					if (item > 0) Add(q.DroppedBy, item, (npc, count));
				}
			}
			foreach ((string npcName, HashSet<string> items) in d.Revealed)
			{
				int npc = CodexNames.NpcId(npcName);
				if (npc == 0) continue;
				foreach (string itemName in items)
				{
					int item = CodexNames.ItemType(itemName);
					if (item > 0 && !(q.DroppedBy.TryGetValue(item, out List<(int, int)> by) && by.Any(b => b.Item1 == npc))) Add(q.DroppedBy, item, (npc, 0));
				}
			}
			for (int kind = 0; kind < CodexData.SourceKinds; kind++)
			{
				foreach ((string fromName, Dictionary<string, int> items) in d.Sources[kind])
				{
					int from = (CodexSource)kind == CodexSource.Tile ? Math.Max(0, CodexNames.TileType(fromName)) : CodexNames.ItemType(fromName);
					foreach ((string itemName, int count) in items)
					{
						int item = CodexNames.ItemType(itemName);
						if (item > 0) Add(q.Sources, item, ((CodexSource)kind, from, count));
					}
				}
			}
		}

		private static void BuildRecipes(QueryIndex q, CodexData d)
		{
			q.RecipeVersion = d.RecipeVersion;
			q.RecipesStale = false;
			q.Giving = new Dictionary<int, List<int>>();
			q.Using = new Dictionary<int, List<int>>();
			int recipes = Math.Min(Recipe.numRecipes, RecipeSystem.RecipeCount);
			for (int i = 0; i < recipes; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (recipe.Disabled || !d.Recipes.Contains(RecipeSystem.RecipeKey(i))) continue;
				Add(q.Giving, recipe.createItem.type, i);
				foreach (Item ingredient in recipe.requiredItem)
				{
					if (!q.Using.TryGetValue(ingredient.type, out List<int> list) || list[^1] != i) Add(q.Using, ingredient.type, i);
				}
			}
		}

		private static void Add<T>(Dictionary<int, List<T>> table, int key, T value)
		{
			if (!table.TryGetValue(key, out List<T> list))
			{
				list = new List<T>();
				table[key] = list;
			}
			list.Add(value);
		}

		// ---- Recipe discovery (client) ----

		// Recipes the Guide shows for an item (those using it) and those giving it
		public static List<int> GuideRecipes(int type)
		{
			List<int> found = new();
			if (type <= ItemID.None) return found;
			for (int i = 0; i < Recipe.numRecipes; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (recipe.Disabled) continue;
				if (recipe.createItem.type == type || recipe.requiredItem.Exists(ingredient => ingredient.type == type) || RecipeSystem.InAcceptedGroup(i, type)) found.Add(i);
			}
			return found;
		}

		private static int lastGuideItem;

		public static void ScanGuide(bool force = false)
		{
			int type = Main.guideItem?.type ?? 0;
			if (type == lastGuideItem && !force) return;
			lastGuideItem = type;
			if (type <= ItemID.None) return;
			RecordSeen(type);
			foreach (int i in GuideRecipes(type)) RecordRecipe(i);
		}

		// Recipes shown with the inventory open (at the stations nearby)
		public static void ScanAvailableRecipes()
		{
			int count = Math.Min(Main.numAvailableRecipes, Main.availableRecipe.Length);
			for (int i = 0; i < count; i++) RecordRecipe(Main.availableRecipe[i]);
		}

		public static void ResetClientScan() => lastGuideItem = 0;
	}
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using UltimateRandomizer.Common.Codex;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;

namespace UltimateRandomizer.Common.Features
{
	// Chests are filled at world creation: their content is randomized right after, once.
	// For worlds created before the mod, the host can run /rchests.
	// Every natural chest has a cap (ChestCap): its content never goes above it, nor past the hardmode wall for a
	// pre-hardmode chest. Worlds randomized before the caps are "tamed" once on load (TameChests: only the items above the
	// cap, in chests nobody opened). A few jackpot chests (JackpotChestChance) hold useful items above their cap and glow
	// until someone opens them. Server (or single player) side; the jackpot chest positions and the opened ones are sent
	// with the world data, each first opening is broadcast (PacketType.JackpotChestOpened).
	public class ChestSystem : ModSystem
	{
		public static bool ChestsRandomized { get; private set; }

		// Natural chests brought under their cap (worlds randomized before the caps existed): done once per world
		private static bool chestsTamed;
		private static bool jackpotChestsPlaced;

		// Jackpot chests (CodexEntry.ChestPosition), and the ones already opened by someone
		private static readonly HashSet<int> jackpotChests = new();
		private static readonly HashSet<int> openedJackpots = new();

		public static IReadOnlyCollection<int> JackpotChests => jackpotChests;

		public static bool IsUnopenedJackpot(int position) => jackpotChests.Contains(position) && !openedJackpots.Contains(position);

		public override void PostWorldGen()
		{
			if (LootConfig.Instance.RandomizeChests) RandomizeAllChests(existingWorld: false);
			// A new world: nothing to tame, and its jackpot chests (if any) are placed with the chests
			chestsTamed = true;
			jackpotChestsPlaced = true;
		}

		// Stations really hidden in chests (hints only point to chests for these), saved and sent with the world data
		private static readonly HashSet<int> placedStations = new();

		public static bool StationInChests(int item) => placedStations.Contains(item);

		// Changes what the hints know (HintFacts listens)
		public static event Action OnStationsChanged;

		private int scanTimer;

		public override void ClearWorld()
		{
			ChestsRandomized = false;
			chestsTamed = false;
			jackpotChestsPlaced = false;
			placedStations.Clear();
			jackpotChests.Clear();
			openedJackpots.Clear();
		}

		public override void Unload() => OnStationsChanged = null;

		public override void SaveWorldData(TagCompound tag)
		{
			tag["chestsRandomized"] = ChestsRandomized;
			tag["chestStations"] = placedStations.Select(DetRandom.ItemName).ToList();
			tag["chestsTamed"] = chestsTamed;
			tag["jackpotChestsPlaced"] = jackpotChestsPlaced;
			tag["jackpotChests"] = jackpotChests.ToList();
			tag["jackpotChestsOpened"] = openedJackpots.ToList();
		}

		public override void LoadWorldData(TagCompound tag)
		{
			ChestsRandomized = tag.GetBool("chestsRandomized");
			chestsTamed = tag.GetBool("chestsTamed");
			jackpotChestsPlaced = tag.GetBool("jackpotChestsPlaced");
			placedStations.Clear();
			foreach (string name in tag.GetList<string>("chestStations"))
			{
				if (ContentSamples.ItemNetIdsByPersistentIds.TryGetValue(name, out int type)) placedStations.Add(type);
			}
			jackpotChests.Clear();
			jackpotChests.UnionWith(tag.GetList<int>("jackpotChests"));
			openedJackpots.Clear();
			openedJackpots.UnionWith(tag.GetList<int>("jackpotChestsOpened"));
			OnStationsChanged?.Invoke();
		}

		public override void NetSend(BinaryWriter writer)
		{
			writer.Write7BitEncodedInt(placedStations.Count);
			foreach (int type in placedStations) writer.Write7BitEncodedInt(type);
			writer.Write7BitEncodedInt(jackpotChests.Count);
			foreach (int position in jackpotChests)
			{
				writer.Write(position);
				writer.Write(openedJackpots.Contains(position));
			}
		}

		public override void NetReceive(BinaryReader reader)
		{
			int count = reader.Read7BitEncodedInt();
			HashSet<int> received = new();
			for (int i = 0; i < count; i++) received.Add(reader.Read7BitEncodedInt());
			int jackpots = reader.Read7BitEncodedInt();
			jackpotChests.Clear();
			openedJackpots.Clear();
			for (int i = 0; i < jackpots; i++)
			{
				int position = reader.ReadInt32();
				jackpotChests.Add(position);
				if (reader.ReadBoolean()) openedJackpots.Add(position);
			}
			if (received.SetEquals(placedStations)) return;
			placedStations.Clear();
			placedStations.UnionWith(received);
			OnStationsChanged?.Invoke();
		}

		// Server or single player (never on clients). First tick of an ongoing world: the codex (opened chests) and the town
		// NPCs (player bases) are loaded, and the seed is active (PostWorldLoad ran). Then, a few times a second, who opened a
		// jackpot chest: the server sets Player.chest when it lets a player open a chest (MessageBuffer, message 31)
		public override void PostUpdateWorld()
		{
			if (!SeedSystem.WorldReady || !ItemPool.Ready) return;
			if (!chestsTamed || !jackpotChestsPlaced)
			{
				bool tame = !chestsTamed, place = !jackpotChestsPlaced;
				chestsTamed = true;
				jackpotChestsPlaced = true;
				// Only chests this mod randomized: the others keep their vanilla content (/rchests does it all later)
				if (ChestsRandomized)
				{
					if (tame)
					{
						(int chests, int items) = TameChests();
						Mod.Logger.Info($"Chests tamed: {items} items above their cap re-rolled in {chests} chests.");
					}
					if (place)
					{
						int placed = PlaceJackpotChests(existingWorld: true);
						Mod.Logger.Info($"Jackpot chests placed: {placed}.");
						if (placed > 0 && Main.netMode == NetmodeID.Server) NetMessage.SendData(MessageID.WorldData);
					}
				}
			}
			if (jackpotChests.Count == 0 || ++scanTimer < 10) return;
			scanTimer = 0;
			for (int p = 0; p < Main.maxPlayers; p++)
			{
				Player player = Main.player[p];
				if (!player.active || player.chest < 0 || player.chest >= Main.maxChests || Main.chest[player.chest] == null) continue;
				Chest chest = Main.chest[player.chest];
				int position = CodexEntry.ChestPosition(chest.x, chest.y);
				if (!IsUnopenedJackpot(position)) continue;
				openedJackpots.Add(position);
				if (Main.netMode == NetmodeID.Server)
				{
					ModPacket packet = Mod.GetPacket();
					packet.Write((byte)PacketType.JackpotChestOpened);
					packet.Write(position);
					packet.Write(player.name ?? "");
					packet.Send();
				}
				else JackpotChestEffects.Announce(chest.x, chest.y, player.name);
			}
		}

		// Client: a player opened a jackpot chest (sent once per chest by the server)
		public static void HandlePacket(BinaryReader reader)
		{
			int position = reader.ReadInt32();
			string name = reader.ReadString();
			if (Main.netMode != NetmodeID.MultiplayerClient) return;
			jackpotChests.Add(position);
			if (!openedJackpots.Add(position)) return;
			JackpotChestEffects.Announce(position & 0xFFFF, position >> 16, name);
		}

		// The highest tier a natural chest gives: one tier above the chest, never past the hardmode wall for a pre-hardmode
		// chest (biome chests and the Lihzahrd chest keep chestTier + 1)
		public static int ChestCap(int chestTier) =>
			chestTier >= (int)Tier.Hardmode ? Tiers.Clamp(chestTier + 1) : Math.Min(chestTier + 1, ItemPool.PreHardmodeCap);

		// The replacement of one chest slot. tame: an ongoing world's re-roll of an item above the cap (its own keys, no
		// surprise); otherwise the world creation roll (the same keys as before the caps)
		public static int RollChestSlot(int x, int y, int slot, int chestTier, int original, bool tame)
		{
			int cap = ChestCap(chestTier);
			RollRequest request = new()
			{
				Keys = tame ? new[] { RollKey.Chest, x, y, slot, RollKey.Tame } : new[] { RollKey.Chest, x, y, slot },
				// ChestTierBonus still counts, within the cap
				SourceTier = Math.Min(Tiers.Clamp(Math.Max(chestTier, ItemPool.TierOf(original)) + LootConfig.Instance.ChestTierBonus), cap),
				OriginalItem = original,
				Policy = CategoryPolicy.SameAsOriginal,
				TierJitterDown = 0,
				TierJitterUp = 1,
				// No jackpot in ordinary chests: the jackpot chests are the chests' jackpots
				AllowJackpot = false,
				// The world does not exist yet: progression can't lock chest content, the cap does
				IgnoreGating = true,
				MaxTier = cap
			};
			if (tame) return ItemPool.Roll(request);
			return ItemPool.RollReplacement(request, LootConfig.Instance.SurpriseChance, DetRandom.Get(RollKey.Chest, x, y, slot, RollKey.Surprise));
		}

		// Returns the number of chests changed. On an existing world, chests near houses and the spawn point are skipped:
		// they are probably the players' storage
		public static int RandomizeAllChests(bool existingWorld)
		{
			if (!ItemPool.Ready) return 0;
			int changed = 0;
			for (int c = 0; c < Main.maxChests; c++)
			{
				Chest chest = Main.chest[c];
				if (chest == null || !string.IsNullOrEmpty(chest.name)) continue;
				int chestTier = ChestTier(chest.x, chest.y);
				if (chestTier < 0 || (existingWorld && NearPlayerBase(chest.x, chest.y))) continue;
				for (int slot = 0; slot < chest.item.Length; slot++)
				{
					Item item = chest.item[slot];
					if (item == null || item.IsAir || DropHelper.KeepOriginal(item.type)) continue;
					int original = item.type;
					int replacement = RollChestSlot(chest.x, chest.y, slot, chestTier, original, tame: false);
					int stack = StackRules.Scaled(original, item.stack, replacement);
					item.SetDefaults(replacement);
					item.stack = stack;
					item.Prefix(-1);
				}
				changed++;
			}
			PlaceGuaranteedStations(existingWorld);
			ChestsRandomized = true;
			// Randomized with the caps: nothing to tame
			chestsTamed = true;
			if (!jackpotChestsPlaced)
			{
				PlaceJackpotChests(existingWorld);
				jackpotChestsPlaced = true;
			}
			OnStationsChanged?.Invoke();
			if (Main.netMode == NetmodeID.Server) NetMessage.SendData(MessageID.WorldData);
			return changed;
		}

		// Does this chest item go above the chest's cap (or may it never be given)?
		public static bool NeedsTaming(int type, int cap) => ItemPool.IsBanned(type) || ItemPool.TrueTierOf(type) > cap;

		// The chests an ongoing world tames: never one a player opened (codex of this seed), nor a jackpot chest
		public static bool ShouldTame(int x, int y, ISet<int> opened, ISet<int> jackpots)
		{
			int position = CodexEntry.ChestPosition(x, y);
			return !opened.Contains(position) && !jackpots.Contains(position);
		}

		// Re-rolls, in the natural chests nobody opened, the items above their chest's cap (or banned); the others stay.
		// Server or single player. Returns the chests changed and the items re-rolled
		public static (int chests, int items) TameChests()
		{
			if (!ItemPool.Ready) return (0, 0);
			HashSet<int> opened = CodexSystem.Data.OpenedChests;
			int chests = 0, items = 0;
			for (int c = 0; c < Main.maxChests; c++)
			{
				Chest chest = Main.chest[c];
				if (chest == null || !string.IsNullOrEmpty(chest.name)) continue;
				int chestTier = ChestTier(chest.x, chest.y);
				if (chestTier < 0 || NearPlayerBase(chest.x, chest.y) || !ShouldTame(chest.x, chest.y, opened, jackpotChests)) continue;
				// Open right now (multiplayer: its content was sent to that player)
				if (Chest.UsingChest(c) != -1) continue;
				int cap = ChestCap(chestTier);
				bool changed = false;
				for (int slot = 0; slot < chest.item.Length; slot++)
				{
					Item item = chest.item[slot];
					if (item == null || item.IsAir || DropHelper.KeepOriginal(item.type) || !NeedsTaming(item.type, cap)) continue;
					int original = item.type;
					int replacement = RollChestSlot(chest.x, chest.y, slot, chestTier, original, tame: true);
					int stack = StackRules.Scaled(original, item.stack, replacement);
					item.SetDefaults(replacement);
					item.stack = stack;
					item.Prefix(-1);
					items++;
					changed = true;
				}
				if (changed) chests++;
			}
			return (chests, items);
		}

		// A chest that may become a jackpot chest
		public struct ChestSpot
		{
			public int X, Y, Tier;
			public bool NearBase, Locked, HoldsStation;
		}

		// The jackpot chests among these chests: JackpotChestChance % of the eligible ones, at least 2 (when there are 2), in a
		// fixed random order. Eligible: a pre-hardmode chest that opens without a key (no locked dungeon or shadow chest, no
		// biome or Lihzahrd chest: their beam would tease for a long time), not near a player base, not opened, without a
		// guaranteed station
		public static List<int> SelectJackpotChests(List<ChestSpot> spots, ISet<int> opened, float chance, Random rng)
		{
			List<int> result = new();
			if (chance <= 0) return result;
			List<ChestSpot> eligible = spots.Where(s => s.Tier >= 0 && s.Tier < (int)Tier.Hardmode && !s.NearBase && !s.Locked && !s.HoldsStation
				&& !opened.Contains(CodexEntry.ChestPosition(s.X, s.Y))).OrderBy(s => s.X).ThenBy(s => s.Y).ToList();
			int count = Math.Min(eligible.Count, Math.Max(2, (int)Math.Round(eligible.Count * chance / 100f)));
			for (int i = 0; i < count; i++)
			{
				int j = rng.Next(i, eligible.Count);
				(eligible[i], eligible[j]) = (eligible[j], eligible[i]);
				result.Add(CodexEntry.ChestPosition(eligible[i].X, eligible[i].Y));
			}
			return result;
		}

		// The content of a jackpot chest: 2 to 4 useful items above the chest's cap (the madness tail from it: each extra tier
		// WildTierDecay times rarer, hardmode included, as every jackpot), some gold and a few potions. Deterministic (keys
		// from the chest's position). gap: the jackpot mark of each item (0 for coins and potions)
		public static List<(int type, int stack, int gap)> JackpotChestContent(int x, int y, int chestTier)
		{
			List<(int type, int stack, int gap)> content = new();
			int cap = ChestCap(chestTier);
			float decay = ProgressionConfig.Instance.WildTierDecay;
			Random rng = DetRandom.Get(RollKey.JackpotChest, x, y);
			int count = rng.Next(2, 5);
			for (int index = 0; index < count; index++)
			{
				int tier = ItemPool.WildTier(cap, 100f, decay, DetRandom.Get(RollKey.JackpotChest, x, y, index));
				for (int attempt = 0; attempt < 40; attempt++)
				{
					RollRequest request = new()
					{
						Keys = new[] { RollKey.JackpotChest, x, y, index, attempt },
						SourceTier = tier,
						OriginalItem = -1,
						Policy = CategoryPolicy.Weighted,
						IgnoreGating = true,
						MaxTier = tier
					};
					int item = ItemPool.Roll(request);
					if (!ItemPool.IsJackpotWorthy(item) || (cap < Tiers.Max && ItemPool.TrueTierOf(item) <= cap) || content.Any(e => e.type == item)) continue;
					(int min, _) = StackRules.Range(item);
					content.Add((item, Math.Max(1, min), Math.Max(1, ItemPool.TrueTierOf(item) - cap)));
					break;
				}
			}
			content.Add((ItemID.GoldCoin, 2 + cap + rng.Next(3), 0));
			int potions = rng.Next(2, 4);
			for (int i = 0; i < potions; i++)
			{
				int potion = ItemPool.PickAllowed(ItemCategory.Potion, cap, rng, new[] { RollKey.JackpotChest, x, y, 100 + i }, cap);
				(int min, int max) = StackRules.Range(potion);
				content.Add((potion, rng.Next(Math.Max(1, min), Math.Max(1, max) + 1), 0));
			}
			return content;
		}

		// Server or single player: chooses and fills the jackpot chests of the world (new world: after the guaranteed
		// stations, never in a chest holding one; ongoing world: never an opened chest)
		public static int PlaceJackpotChests(bool existingWorld)
		{
			float chance = LootConfig.Instance.JackpotChestChance;
			if (chance <= 0 || !ItemPool.Ready) return 0;
			List<ChestSpot> spots = new();
			Dictionary<int, Chest> byPosition = new();
			for (int c = 0; c < Main.maxChests; c++)
			{
				Chest chest = Main.chest[c];
				if (chest == null || !string.IsNullOrEmpty(chest.name)) continue;
				int chestTier = ChestTier(chest.x, chest.y);
				if (chestTier < 0) continue;
				byPosition[CodexEntry.ChestPosition(chest.x, chest.y)] = chest;
				spots.Add(new ChestSpot
				{
					X = chest.x,
					Y = chest.y,
					Tier = chestTier,
					NearBase = existingWorld && NearPlayerBase(chest.x, chest.y),
					Locked = Chest.IsLocked(chest.x, chest.y),
					// A chest open right now is left alone too
					HoldsStation = chest.item.Any(i => i != null && !i.IsAir && placedStations.Contains(i.type)) || Chest.UsingChest(c) != -1
				});
			}
			ISet<int> opened = existingWorld ? CodexSystem.Data.OpenedChests : new HashSet<int>();
			List<int> chosen = SelectJackpotChests(spots, opened, chance, DetRandom.Get(RollKey.JackpotChest));
			foreach (int position in chosen)
			{
				Chest chest = byPosition[position];
				List<(int type, int stack, int gap)> content = JackpotChestContent(chest.x, chest.y, ChestTier(chest.x, chest.y));
				for (int slot = 0; slot < chest.item.Length; slot++) chest.item[slot] = new Item();
				for (int i = 0; i < content.Count && i < chest.item.Length; i++)
				{
					(int type, int stack, int gap) = content[i];
					Item item = new(type, stack);
					if (gap == 0) item.Prefix(-1);
					else JackpotItem.Mark(item, gap, fresh: false);
					chest.item[i] = item;
				}
				jackpotChests.Add(position);
			}
			return chosen.Count;
		}

		// Hides a few copies of each basic station (work bench, furnace...) in wooden chests, in a fixed random order
		private static void PlaceGuaranteedStations(bool existingWorld)
		{
			int copies = ProgressionConfig.Instance.ChestStationGuarantee;
			List<int> stations = GuaranteedSources.StationRequirements().Select(r => r.Item).ToList();
			if (copies <= 0 || stations.Count == 0) return;
			List<Chest> chests = new();
			for (int c = 0; c < Main.maxChests; c++)
			{
				Chest chest = Main.chest[c];
				if (chest == null || !string.IsNullOrEmpty(chest.name) || !IsWoodenChest(chest.x, chest.y)) continue;
				if (existingWorld && NearPlayerBase(chest.x, chest.y)) continue;
				chests.Add(chest);
			}
			if (chests.Count == 0) return;
			chests.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
			Random rng = DetRandom.Get(RollKey.Guaranteed, RollKey.Chest);
			for (int i = chests.Count - 1; i > 0; i--)
			{
				int j = rng.Next(i + 1);
				(chests[i], chests[j]) = (chests[j], chests[i]);
			}
			int next = 0;
			foreach (int station in stations)
			{
				for (int copy = 0; copy < copies; copy++)
				{
					for (int tries = 0; tries < chests.Count; tries++)
					{
						Chest chest = chests[next++ % chests.Count];
						int slot = Array.FindIndex(chest.item, item => item == null || item.IsAir);
						if (slot < 0) continue;
						chest.item[slot] = new Item(station);
						placedStations.Add(station);
						break;
					}
				}
			}
		}

		private static bool IsWoodenChest(int x, int y)
		{
			if (!WorldGen.InWorld(x, y)) return false;
			Tile tile = Main.tile[x, y];
			return tile.HasTile && tile.TileType == TileID.Containers && tile.TileFrameX / 36 == 0;
		}

		private static bool NearPlayerBase(int x, int y)
		{
			const int Distance = 60;
			if (Math.Abs(x - Main.spawnTileX) < Distance && Math.Abs(y - Main.spawnTileY) < Distance) return true;
			for (int i = 0; i < Main.maxNPCs; i++)
			{
				NPC npc = Main.npc[i];
				if (npc.active && npc.townNPC && !npc.homeless && Math.Abs(x - npc.homeTileX) < Distance && Math.Abs(y - npc.homeTileY) < Distance) return true;
			}
			return false;
		}

		// How advanced a natural chest is, from its tile style. -1 = not a natural chest
		public static int ChestTier(int x, int y)
		{
			if (!WorldGen.InWorld(x, y)) return -1;
			Tile tile = Main.tile[x, y];
			if (!tile.HasTile) return -1;
			int style = tile.TileFrameX / 36;
			int depthTier = y > Main.worldSurface ? 1 : 0;
			if (tile.TileType == TileID.Containers)
			{
				return style switch
				{
					0 => depthTier,                     // Wooden
					1 => 1,                             // Gold
					2 or 4 => 3,                        // Locked gold (dungeon), locked shadow
					3 => 3,                             // Shadow (underworld)
					10 or 11 or 12 or 13 or 17 => 1,    // Ivy, frozen, living wood, skyware, water
					16 => 7,                            // Lihzahrd
					23 or 24 or 25 or 26 or 27 => 6,    // Biome chests (jungle, corruption, crimson, hallowed, frozen)
					32 or 50 or 51 => 1,                // Mushroom, granite, marble
					_ => depthTier
				};
			}
			if (tile.TileType == TileID.Containers2)
			{
				return style == 13 ? 6 : 1;             // Desert biome chest, otherwise sandstone/dead man's
			}
			return -1;
		}
	}

	// Clients and single player: a jackpot chest nobody opened yet shines (light, gold sparkles, a beam of light) while on
	// screen; its first opening bursts for everyone
	public class JackpotChestEffects : ModSystem
	{
		private static readonly Color Gold = new(255, 215, 80);

		// The chest's center (a chest is 2x2 tiles from its top-left corner)
		private static Vector2 Center(int position) => new(((position & 0xFFFF) + 1) * 16f, ((position >> 16) + 1) * 16f);

		private static bool OnScreen(Vector2 world, float margin) =>
			world.X > Main.screenPosition.X - margin && world.X < Main.screenPosition.X + Main.screenWidth + margin
			&& world.Y > Main.screenPosition.Y - margin && world.Y < Main.screenPosition.Y + Main.screenHeight + margin;

		public override void PostUpdateEverything()
		{
			if (Main.dedServ || Main.gameMenu || ChestSystem.JackpotChests.Count == 0) return;
			foreach (int position in ChestSystem.JackpotChests)
			{
				if (!ChestSystem.IsUnopenedJackpot(position)) continue;
				Vector2 center = Center(position);
				if (!OnScreen(center, 200f)) continue;
				Lighting.AddLight(center, Gold.ToVector3() * 1.1f);
				if (Main.rand.NextBool(5))
				{
					Dust dust = Dust.NewDustDirect(center - new Vector2(16f, 16f), 32, 32, DustID.TreasureSparkle, 0f, -1f, 0, default, 1.1f);
					dust.noGravity = true;
				}
			}
		}

		public override void PostDrawTiles()
		{
			if (Main.dedServ || ChestSystem.JackpotChests.Count == 0) return;
			bool begun = false;
			foreach (int position in ChestSystem.JackpotChests)
			{
				if (!ChestSystem.IsUnopenedJackpot(position)) continue;
				Vector2 center = Center(position);
				if (!OnScreen(center, 500f)) continue;
				if (!begun)
				{
					Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);
					begun = true;
				}
				JackpotEffects.BeamAt(Main.spriteBatch, center + new Vector2(0f, 16f) - Main.screenPosition, 2);
			}
			if (begun) Main.spriteBatch.End();
		}

		// The first opening of a jackpot chest, shown on every client (and in single player)
		public static void Announce(int x, int y, string playerName)
		{
			if (Main.dedServ) return;
			Vector2 center = new((x + 1) * 16f, (y + 1) * 16f);
			Rectangle box = new(x * 16, y * 16, 32, 32);
			CombatText.NewText(box, Gold, Language.GetTextValue("Mods.UltimateRandomizer.Jackpot.ChestPopup"), true, false);
			for (int i = 0; i < 60; i++)
			{
				Dust dust = Dust.NewDustDirect(box.TopLeft(), box.Width, box.Height, i % 2 == 0 ? DustID.GoldCoin : DustID.TreasureSparkle, 0f, 0f, 0, default, 1.6f);
				dust.velocity = Main.rand.NextVector2Circular(5f, 5f);
				dust.noGravity = true;
			}
			SoundEngine.PlaySound(SoundID.AchievementComplete.WithVolumeScale(1.2f), center);
			SoundEngine.PlaySound(SoundID.CoinPickup, center);
			Main.NewText(Language.GetTextValue("Mods.UltimateRandomizer.Jackpot.ChestChat", string.IsNullOrEmpty(playerName) ? "?" : playerName), Gold);
		}
	}
}

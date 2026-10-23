using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using UltimateRandomizer.Common.Configs;

namespace UltimateRandomizer.Common.Core
{
	// How far in the game an enemy is: bosses from a table, other enemies estimated from their base stats (other mods' NPCs
	// against the vanilla ones: ModdedTier)
	public static class NpcTiers
	{
		// A boss by its type (net IDs too): its sample is a boss, or it counts as one (Eater of Worlds segments...)
		public static bool IsBoss(int type) =>
			type > 0 && (NPCID.Sets.ShouldBeCountedAsBoss[type] || (ContentSamples.NpcsByNetId.TryGetValue(type, out NPC sample) && sample.boss));

		// A live NPC fighting as a boss
		public static bool IsBoss(NPC npc) => npc.boss || NPCID.Sets.ShouldBeCountedAsBoss[npc.type];

		private static readonly Dictionary<int, Tier> BossTiers = new()
		{
			[NPCID.KingSlime] = Tier.PostEyeOfCthulhu,
			[NPCID.EyeofCthulhu] = Tier.PostEyeOfCthulhu,
			[NPCID.EaterofWorldsHead] = Tier.PostEvilBoss,
			[NPCID.EaterofWorldsBody] = Tier.PostEvilBoss,
			[NPCID.EaterofWorldsTail] = Tier.PostEvilBoss,
			[NPCID.BrainofCthulhu] = Tier.PostEvilBoss,
			// Optional fights usually done before Skeletron (user decision): Queen Bee and Deerclops sit with the evil bosses
			[NPCID.QueenBee] = Tier.PostEvilBoss,
			[NPCID.SkeletronHead] = Tier.PostSkeletron,
			[NPCID.Deerclops] = Tier.PostEvilBoss,
			[NPCID.DD2DarkMageT1] = Tier.PostSkeletron,
			[NPCID.WallofFlesh] = Tier.Hardmode,
			[NPCID.PirateShip] = Tier.Hardmode,
			[NPCID.QueenSlimeBoss] = Tier.PostMechBoss,
			[NPCID.TheDestroyer] = Tier.PostMechBoss,
			[NPCID.Retinazer] = Tier.PostMechBoss,
			[NPCID.Spazmatism] = Tier.PostMechBoss,
			[NPCID.SkeletronPrime] = Tier.PostMechBoss,
			[NPCID.DD2OgreT2] = Tier.PostMechBoss,
			[NPCID.Plantera] = Tier.PostPlantera,
			[NPCID.MourningWood] = Tier.PostPlantera,
			[NPCID.Pumpking] = Tier.PostPlantera,
			[NPCID.Everscream] = Tier.PostPlantera,
			[NPCID.SantaNK1] = Tier.PostPlantera,
			[NPCID.IceQueen] = Tier.PostPlantera,
			[NPCID.Golem] = Tier.PostGolem,
			[NPCID.GolemHead] = Tier.PostGolem,
			[NPCID.DukeFishron] = Tier.PostGolem,
			[NPCID.HallowBoss] = Tier.PostGolem,
			[NPCID.MartianSaucerCore] = Tier.PostGolem,
			[NPCID.DD2Betsy] = Tier.PostGolem,
			[NPCID.CultistBoss] = Tier.PostCultist,
			[NPCID.LunarTowerSolar] = Tier.PostCultist,
			[NPCID.LunarTowerVortex] = Tier.PostCultist,
			[NPCID.LunarTowerNebula] = Tier.PostCultist,
			[NPCID.LunarTowerStardust] = Tier.PostCultist,
			[NPCID.MoonLordCore] = Tier.PostMoonLord,
			[NPCID.MoonLordHead] = Tier.PostMoonLord,
			[NPCID.MoonLordHand] = Tier.PostMoonLord
		};

		// Enemies that only spawn from a stage of the game on (terraria.wiki.gg, per enemy and event): the tier from which they are
		// a source of their drops (ItemClassifier.NpcSourceTier). Others spawn from the start, or are bosses (BossTiers)
		private static readonly Dictionary<int, Tier> SpawnTiers = BuildSpawnTiers();

		private static Dictionary<int, Tier> BuildSpawnTiers()
		{
			Dictionary<int, Tier> map = new();
			void Set(Tier tier, params int[] types)
			{
				foreach (int type in types) map[type] = tier;
			}
			// The Dungeon's own enemies, once Skeletron no longer guards it
			Set(Tier.PostSkeletron, NPCID.AngryBones, NPCID.AngryBonesBig, NPCID.AngryBonesBigMuscle, NPCID.AngryBonesBigHelmet, NPCID.DarkCaster, NPCID.CursedSkull,
				NPCID.DungeonSlime);
			// Old One's Army, tier 1: the Tavernkeep moves in after an evil boss
			Set(Tier.PostEvilBoss, NPCID.DD2GoblinT1, NPCID.DD2GoblinBomberT1, NPCID.DD2WyvernT1, NPCID.DD2JavelinstT1, NPCID.DD2SkeletonT1);
			// Hardmode only: biome enemies of hardmode, mimics, the Wyvern, hardmode Blood Moon and invasions (pirates, the Frost
			// Legion, the Goblin Summoner)
			Set(Tier.Hardmode, NPCID.WyvernHead, NPCID.Mimic, NPCID.IceMimic, NPCID.PresentMimic, NPCID.BigMimicCorruption, NPCID.BigMimicCrimson, NPCID.BigMimicHallow,
				NPCID.BigMimicJungle, NPCID.Pixie, NPCID.Unicorn, NPCID.Gastropod, NPCID.LightMummy, NPCID.DarkMummy, NPCID.BloodMummy, NPCID.Mummy, NPCID.Wraith,
				NPCID.PossessedArmor, NPCID.Werewolf, NPCID.Corruptor, NPCID.Clinger, NPCID.CursedHammer, NPCID.CrimsonAxe, NPCID.EnchantedSword, NPCID.IchorSticker,
				NPCID.FloatyGross, NPCID.Herpling, NPCID.Crimslime, NPCID.PigronCorruption, NPCID.PigronCrimson, NPCID.PigronHallow, NPCID.Slimer, NPCID.Slimer2,
				NPCID.SeekerHead, NPCID.DiggerHead, NPCID.Derpling, NPCID.AngryTrapper, NPCID.GiantTortoise, NPCID.IceTortoise, NPCID.IceElemental, NPCID.IcyMerman,
				NPCID.IceGolem, NPCID.Wolf, NPCID.ArmoredViking, NPCID.GiantBat, NPCID.GiantFlyingFox, NPCID.Arapaima, NPCID.MossHornet, NPCID.Moth, NPCID.JungleCreeper,
				NPCID.JungleCreeperWall, NPCID.BlackRecluse, NPCID.BlackRecluseWall, NPCID.RedDevil, NPCID.Lavabat, NPCID.SkeletonArcher, NPCID.ArmoredSkeleton,
				NPCID.RuneWizard, NPCID.ChaosElemental, NPCID.IlluminantBat, NPCID.IlluminantSlime, NPCID.ToxicSludge, NPCID.AnglerFish, NPCID.GreenJellyfish,
				NPCID.BloodJelly, NPCID.FungoFish, NPCID.FungiBulb, NPCID.GiantFungiBulb, NPCID.DesertBeast, NPCID.DesertScorpionWalk, NPCID.DesertScorpionWall,
				NPCID.DesertLamiaDark, NPCID.DesertLamiaLight, NPCID.DesertGhoul, NPCID.DesertGhoulCorruption, NPCID.DesertGhoulCrimson, NPCID.DesertGhoulHallow,
				NPCID.DesertDjinn, NPCID.SandShark, NPCID.SandsharkCorrupt, NPCID.SandsharkCrimson, NPCID.SandsharkHallow, NPCID.DuneSplicerHead, NPCID.SandElemental,
				NPCID.GiantWalkingAntlion, NPCID.GiantFlyingAntlion, NPCID.Medusa, NPCID.AngryNimbus, NPCID.WanderingEye, NPCID.Clown, NPCID.BloodEelHead,
				NPCID.GoblinShark, NPCID.BloodNautilus, NPCID.PirateDeckhand, NPCID.PirateCorsair, NPCID.PirateDeadeye, NPCID.PirateCrossbower, NPCID.PirateCaptain,
				NPCID.Parrot, NPCID.MisterStabby, NPCID.SnowmanGangsta, NPCID.SnowBalla, NPCID.GoblinSummoner, NPCID.ShadowFlameApparition);
			// Solar Eclipse (a mechanical boss beaten), the Reaper; Old One's Army tier 2
			Set(Tier.PostMechBoss, NPCID.Eyezor, NPCID.Frankenstein, NPCID.SwampThing, NPCID.Vampire, NPCID.VampireBat, NPCID.CreatureFromTheDeep, NPCID.Fritz,
				NPCID.ThePossessed, NPCID.Reaper, NPCID.DD2GoblinT2, NPCID.DD2GoblinBomberT2, NPCID.DD2WyvernT2, NPCID.DD2JavelinstT2, NPCID.DD2WitherBeastT2,
				NPCID.DD2DrakinT2, NPCID.DD2KoboldWalkerT2, NPCID.DD2KoboldFlyerT2, NPCID.DD2DarkMageT3);
			// After Plantera: the Jungle Temple (Temple Key), the dungeon's new enemies, the eclipse's late enemies, both moon events
			Set(Tier.PostPlantera, NPCID.Lihzahrd, NPCID.LihzahrdCrawler, NPCID.FlyingSnake, NPCID.GiantCursedSkull, NPCID.BoneLee, NPCID.Paladin, NPCID.SkeletonSniper,
				NPCID.TacticalSkeleton, NPCID.SkeletonCommando, NPCID.RaggedCaster, NPCID.RaggedCasterOpenCoat, NPCID.Necromancer, NPCID.NecromancerArmored,
				NPCID.DiabolistRed, NPCID.DiabolistWhite, NPCID.DungeonSpirit, NPCID.BlueArmoredBones, NPCID.BlueArmoredBonesMace, NPCID.BlueArmoredBonesNoPants,
				NPCID.BlueArmoredBonesSword, NPCID.RustyArmoredBonesAxe, NPCID.RustyArmoredBonesFlail, NPCID.RustyArmoredBonesSword, NPCID.RustyArmoredBonesSwordNoArmor,
				NPCID.HellArmoredBones, NPCID.HellArmoredBonesSpikeShield, NPCID.HellArmoredBonesMace, NPCID.HellArmoredBonesSword, NPCID.Mothron, NPCID.MothronEgg,
				NPCID.MothronSpawn, NPCID.Butcher, NPCID.DeadlySphere, NPCID.DrManFly, NPCID.Nailhead, NPCID.Psycho, NPCID.Scarecrow1, NPCID.Scarecrow2, NPCID.Scarecrow3,
				NPCID.Scarecrow4, NPCID.Scarecrow5, NPCID.Scarecrow6, NPCID.Scarecrow7, NPCID.Scarecrow8, NPCID.Scarecrow9, NPCID.Scarecrow10, NPCID.Splinterling,
				NPCID.Hellhound, NPCID.Poltergeist, NPCID.HeadlessHorseman, NPCID.ZombieElf, NPCID.ZombieElfBeard, NPCID.ZombieElfGirl, NPCID.GingerbreadMan,
				NPCID.ElfArcher, NPCID.Nutcracker, NPCID.NutcrackerSpinning, NPCID.Yeti, NPCID.ElfCopter, NPCID.Krampus, NPCID.Flocko);
			// Martian Madness (the probe comes after Golem); Old One's Army tier 3
			Set(Tier.PostGolem, NPCID.MartianProbe, NPCID.Scutlix, NPCID.ScutlixRider, NPCID.MartianWalker, NPCID.MartianDrone, NPCID.MartianTurret, NPCID.GigaZapper,
				NPCID.MartianEngineer, NPCID.MartianOfficer, NPCID.RayGunner, NPCID.GrayGrunt, NPCID.BrainScrambler, NPCID.DD2GoblinT3, NPCID.DD2GoblinBomberT3,
				NPCID.DD2WyvernT3, NPCID.DD2JavelinstT3, NPCID.DD2SkeletonT3, NPCID.DD2WitherBeastT3, NPCID.DD2DrakinT3, NPCID.DD2KoboldWalkerT3, NPCID.DD2KoboldFlyerT3,
				NPCID.DD2LightningBugT3, NPCID.DD2OgreT3);
			// The Celestial Pillars' enemies
			Set(Tier.PostCultist, NPCID.SolarCorite, NPCID.SolarCrawltipedeHead, NPCID.SolarDrakomire, NPCID.SolarDrakomireRider, NPCID.SolarSroller, NPCID.SolarSolenian,
				NPCID.SolarSpearman, NPCID.VortexHornet, NPCID.VortexHornetQueen, NPCID.VortexLarva, NPCID.VortexRifleman, NPCID.VortexSoldier, NPCID.NebulaBeast,
				NPCID.NebulaBrain, NPCID.NebulaHeadcrab, NPCID.NebulaSoldier, NPCID.StardustCellBig, NPCID.StardustCellSmall, NPCID.StardustJellyfishBig,
				NPCID.StardustSoldier, NPCID.StardustSpiderBig, NPCID.StardustSpiderSmall, NPCID.StardustWormHead);
			return map;
		}

		// The tier from which an enemy spawns (SpawnTiers), -1 when it spawns from the start or is not listed
		public static int SpawnTier(int type) => SpawnTiers.TryGetValue(type, out Tier tier) ? (int)tier : -1;

		private static readonly Dictionary<int, int> cache = new();

		public static IEnumerable<int> BossTypes => BossTiers.Keys;

		// Its tier comes from the boss table (not estimated from its stats)
		public static bool HasBossTier(int type) => BossTiers.ContainsKey(type);

		public static int BaseTier(int npcType)
		{
			if (cache.TryGetValue(npcType, out int tier)) return tier;
			if (BossTiers.TryGetValue(npcType, out Tier boss))
			{
				tier = (int)boss;
			}
			else if (ContentSamples.NpcsByNetId.TryGetValue(npcType, out NPC sample) && sample.type >= NPCID.Count)
			{
				tier = ModdedTier(npcType);
			}
			else if (sample != null)
			{
				(int life, int damage) = NormalStats(npcType);
				tier = Math.Max(LifeTier(life), DamageTier(damage));
				if (sample.boss) tier = Math.Max(tier, (int)Tier.PostEyeOfCthulhu);
			}
			else
			{
				tier = 0;
			}
			tier = Tiers.Clamp(tier);
			cache[npcType] = tier;
			return tier;
		}

		// The tier used for rolls: weak enemies stay weak, unless the option makes them follow progression
		public static int EffectiveTier(int npcType)
		{
			int tier = BaseTier(npcType);
			if (ProgressionConfig.Instance.ScaleWeakEnemiesWithProgression)
			{
				tier = Math.Max(tier, ProgressionSystem.UnlockedTier - 1);
			}
			return Tiers.Clamp(tier);
		}

		// Vanilla bosses a modded boss is compared with: the boss, the parts its fight adds to its life (Skeletron's hands, the
		// other twin, Prime's limbs, Golem's head and fists, the Moon Lord's head and hands), and the tier it opens. Not the
		// worms (the Eater of Worlds' segments, the Destroyer's shared 80,000 life would pull a late modded boss down to the
		// mechanical bosses) nor the event bosses
		private static readonly (int boss, int[] parts, Tier tier)[] ReferenceBosses =
		{
			(NPCID.KingSlime, Array.Empty<int>(), Tier.PostEyeOfCthulhu),
			(NPCID.EyeofCthulhu, Array.Empty<int>(), Tier.PostEyeOfCthulhu),
			(NPCID.BrainofCthulhu, Array.Empty<int>(), Tier.PostEvilBoss),
			(NPCID.QueenBee, Array.Empty<int>(), Tier.PostEvilBoss),
			(NPCID.Deerclops, Array.Empty<int>(), Tier.PostEvilBoss),
			(NPCID.SkeletronHead, new int[] { NPCID.SkeletronHand, NPCID.SkeletronHand }, Tier.PostSkeletron),
			(NPCID.WallofFlesh, Array.Empty<int>(), Tier.Hardmode),
			(NPCID.QueenSlimeBoss, Array.Empty<int>(), Tier.PostMechBoss),
			(NPCID.Retinazer, new int[] { NPCID.Spazmatism }, Tier.PostMechBoss),
			(NPCID.SkeletronPrime, new int[] { NPCID.PrimeCannon, NPCID.PrimeSaw, NPCID.PrimeVice, NPCID.PrimeLaser }, Tier.PostMechBoss),
			(NPCID.Plantera, Array.Empty<int>(), Tier.PostPlantera),
			(NPCID.Golem, new int[] { NPCID.GolemHead, NPCID.GolemFistLeft, NPCID.GolemFistRight }, Tier.PostGolem),
			(NPCID.DukeFishron, Array.Empty<int>(), Tier.PostGolem),
			(NPCID.HallowBoss, Array.Empty<int>(), Tier.PostGolem),
			(NPCID.CultistBoss, Array.Empty<int>(), Tier.PostCultist),
			(NPCID.MoonLordCore, new int[] { NPCID.MoonLordHead, NPCID.MoonLordHand, NPCID.MoonLordHand }, Tier.PostMoonLord)
		};

		// Reference stats (log of life, damage, defense) of the vanilla bosses, and of the natural enemies of each known spawn
		// tier (-1: the pre-hardmode enemies of no listed stage), built once
		private static List<(double life, double damage, double defense, int tier)> bossReferences;
		private static List<(double life, double damage, int tier)> enemyReferences;

		private static double Log(int value) => Math.Log(Math.Max(1, value));

		// The stats of a boss fight: its life with its parts', its (or its strongest part's) contact damage, its defense
		private static (int life, int damage, int defense) FightStats(int boss, int[] parts)
		{
			(int life, int damage, int defense) = SampleStats(boss);
			foreach (int part in parts)
			{
				(int l, int d, _) = SampleStats(part);
				life += l;
				damage = Math.Max(damage, d);
			}
			return (life, damage, defense);
		}

		private static void BuildReferences()
		{
			if (bossReferences != null) return;
			bossReferences = new List<(double, double, double, int)>();
			foreach ((int boss, int[] parts, Tier tier) in ReferenceBosses)
			{
				(int life, int damage, int defense) = FightStats(boss, parts);
				bossReferences.Add((Log(life), Log(damage), Log(defense + 1), (int)tier));
			}
			// Natural vanilla enemies (no boss, part, minion, town NPC, critter or harmless NPC), by the stage they spawn from. Not
			// the Old One's Army: its waves scale with the event, not with the stage it opens at
			Dictionary<int, List<(double life, double damage)>> byTier = new();
			for (int type = 1; type < NPCID.Count; type++)
			{
				if (!ContentSamples.NpcsByNetId.TryGetValue(type, out NPC sample) || sample.boss || IsBoss(type) || BossParts.IsPart(type) || BossParts.IsMinion(type)
					|| sample.townNPC || sample.friendly || NPCID.Sets.CountsAsCritter[type] || NPCID.Sets.ProjectileNPC[type] || NPCID.Sets.BelongsToInvasionOldOnesArmy[type]
					|| sample.lifeMax <= 5 || sample.damage <= 0) continue;
				(int life, int damage, _) = SampleStats(type);
				int tier = SpawnTier(type);
				if (!byTier.TryGetValue(tier, out List<(double, double)> list)) byTier[tier] = list = new List<(double, double)>();
				list.Add((Log(life), Log(damage)));
			}
			enemyReferences = new List<(double, double, int)>();
			foreach ((int tier, List<(double life, double damage)> list) in byTier.OrderBy(p => p.Key))
			{
				enemyReferences.Add((Median(list.Select(s => s.life)), Median(list.Select(s => s.damage)), tier));
			}
		}

		private static double Median(IEnumerable<double> values)
		{
			List<double> sorted = values.OrderBy(v => v).ToList();
			return sorted.Count == 0 ? 0 : sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
		}

		// An estimate for another mod's NPC (no table knows it, and no generic flag says when it spawns or which boss opens it;
		// its spawn chance can't be evaluated outside a world): from its normal-mode stats only. A boss: the tier of the vanilla
		// boss fight whose life (weighted most), damage and defense are closest on a log scale, at least after the Eye. Another
		// NPC: the stage of the vanilla natural enemies whose median life and damage are closest; when that is the pre-hardmode
		// group, the life and damage thresholds of the vanilla estimate split it (never above Skeletron's tier)
		public static int ModdedTier(int netId)
		{
			BuildReferences();
			(int life, int damage, int defense) = SampleStats(netId);
			double l = Log(life), d = Log(damage), f = Log(defense + 1);
			if (IsBoss(netId))
			{
				int best = (int)Tier.PostEyeOfCthulhu;
				double bestDistance = double.MaxValue;
				foreach ((double rl, double rd, double rf, int tier) in bossReferences)
				{
					double distance = 2 * (l - rl) * (l - rl) + (damage > 0 && rd > 0 ? (d - rd) * (d - rd) : 0) + 0.5 * (f - rf) * (f - rf);
					if (distance < bestDistance)
					{
						bestDistance = distance;
						best = tier;
					}
				}
				return Math.Max(best, (int)Tier.PostEyeOfCthulhu);
			}
			int stage = -1;
			double closest = double.MaxValue;
			foreach ((double rl, double rd, int tier) in enemyReferences)
			{
				double distance = (l - rl) * (l - rl) + (damage > 0 ? (d - rd) * (d - rd) : 0);
				if (distance < closest)
				{
					closest = distance;
					stage = tier;
				}
			}
			return stage >= 0 ? stage : Math.Min(Math.Max(LifeTier(life), DamageTier(damage)), (int)Tier.PostSkeletron);
		}

		// Set while NormalStats builds its NPC: EnemyStatsNPC leaves the world's multipliers out
		public static bool Sampling;

		private static readonly FieldInfo ExpertOverride = typeof(Main).GetField("_overrideForExpertMode", BindingFlags.NonPublic | BindingFlags.Static);
		private static readonly FieldInfo MasterOverride = typeof(Main).GetField("_overrideForMasterMode", BindingFlags.NonPublic | BindingFlags.Static);

		// Base life and damage of an NPC in a plain normal-mode world. ContentSamples are scaled by the game mode (and secret
		// seeds) of the last world played before the mods were (re)loaded: after an expert world, a reload made every enemy
		// look stronger, which changed the tiers, the enemy swaps and the drops of a world from one session to the next
		public static (int life, int damage) NormalStats(int netId)
		{
			(int life, int damage, _) = SampleStats(netId);
			return (life, damage);
		}

		// NormalStats with the defense
		private static (int life, int damage, int defense) SampleStats(int netId)
		{
			bool goodWorld = Main.getGoodWorld, remix = Main.remixWorld, zenith = Main.zenithWorld, tenth = Main.tenthAnniversaryWorld, hardMode = Main.hardMode;
			object expert = ExpertOverride?.GetValue(null), master = MasterOverride?.GetValue(null);
			try
			{
				Main.getGoodWorld = Main.remixWorld = Main.zenithWorld = Main.tenthAnniversaryWorld = Main.hardMode = false;
				ExpertOverride?.SetValue(null, (bool?)false);
				MasterOverride?.SetValue(null, (bool?)false);
				Sampling = true;
				NPC npc = new();
				npc.SetDefaults(netId, new NPCSpawnParams { gameModeData = GameModeData.NormalMode, strengthMultiplierOverride = 1f, playerCountForMultiplayerDifficultyOverride = 1 });
				return (npc.lifeMax, npc.damage, npc.defense);
			}
			finally
			{
				Sampling = false;
				Main.getGoodWorld = goodWorld;
				Main.remixWorld = remix;
				Main.zenithWorld = zenith;
				Main.tenthAnniversaryWorld = tenth;
				Main.hardMode = hardMode;
				ExpertOverride?.SetValue(null, expert);
				MasterOverride?.SetValue(null, master);
			}
		}

		private static int LifeTier(int life) => life switch
		{
			<= 60 => 0,
			<= 120 => 1,
			<= 200 => 2,
			<= 350 => 3,
			<= 600 => 4,
			<= 1000 => 5,
			<= 2500 => 6,
			<= 5000 => 7,
			_ => 8
		};

		private static int DamageTier(int damage) => damage switch
		{
			<= 14 => 0,
			<= 20 => 1,
			<= 28 => 2,
			<= 38 => 3,
			<= 55 => 4,
			<= 70 => 5,
			<= 90 => 6,
			<= 110 => 7,
			_ => 8
		};

		public static void Unload()
		{
			cache.Clear();
			bossReferences = null;
			enemyReferences = null;
		}
	}
}

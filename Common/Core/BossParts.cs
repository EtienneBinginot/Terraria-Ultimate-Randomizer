using System.Collections.Generic;
using Terraria.ID;

namespace UltimateRandomizer.Common.Core
{
	// Bosses made of several NPCs, and the minions bosses summon
	public static class BossParts
	{
		// Part -> the boss it belongs to: parts share the boss's stats and loot rolls
		private static readonly Dictionary<int, int> Owners = new()
		{
			[NPCID.EaterofWorldsBody] = NPCID.EaterofWorldsHead,
			[NPCID.EaterofWorldsTail] = NPCID.EaterofWorldsHead,
			[NPCID.TheDestroyerBody] = NPCID.TheDestroyer,
			[NPCID.TheDestroyerTail] = NPCID.TheDestroyer,
			[NPCID.SkeletronHand] = NPCID.SkeletronHead,
			[NPCID.PrimeCannon] = NPCID.SkeletronPrime,
			[NPCID.PrimeSaw] = NPCID.SkeletronPrime,
			[NPCID.PrimeVice] = NPCID.SkeletronPrime,
			[NPCID.PrimeLaser] = NPCID.SkeletronPrime,
			[NPCID.WallofFleshEye] = NPCID.WallofFlesh,
			[NPCID.GolemHead] = NPCID.Golem,
			[NPCID.GolemHeadFree] = NPCID.Golem,
			[NPCID.GolemFistLeft] = NPCID.Golem,
			[NPCID.GolemFistRight] = NPCID.Golem,
			[NPCID.MoonLordHead] = NPCID.MoonLordCore,
			[NPCID.MoonLordHand] = NPCID.MoonLordCore,
			[NPCID.MartianSaucer] = NPCID.MartianSaucerCore,
			[NPCID.MartianSaucerCannon] = NPCID.MartianSaucerCore,
			[NPCID.MartianSaucerTurret] = NPCID.MartianSaucerCore,
			[NPCID.PirateShipCannon] = NPCID.PirateShip
		};

		// Spawned by a boss during its fight: never a source of extra loot
		private static readonly HashSet<int> Minions = new()
		{
			NPCID.Creeper, NPCID.ServantofCthulhu, NPCID.TheHungry, NPCID.TheHungryII, NPCID.LeechHead, NPCID.LeechBody, NPCID.LeechTail,
			NPCID.Probe, NPCID.Bee, NPCID.BeeSmall, NPCID.PlanterasHook, NPCID.PlanterasTentacle, NPCID.Sharkron, NPCID.Sharkron2,
			NPCID.DetonatingBubble, NPCID.MoonLordFreeEye, NPCID.MoonLordLeechBlob, NPCID.CultistBossClone, NPCID.AncientCultistSquidhead,
			NPCID.QueenSlimeMinionBlue, NPCID.QueenSlimeMinionPink, NPCID.QueenSlimeMinionPurple
		};

		public static int Owner(int type) => Owners.TryGetValue(type, out int owner) ? owner : type;

		public static bool IsPart(int type) => Owners.ContainsKey(type);

		public static bool IsMinion(int type) => Minions.Contains(type);
	}
}

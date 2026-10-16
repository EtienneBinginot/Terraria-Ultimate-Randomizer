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

		// Spawned by a boss during its fight: never a source of extra loot. Minion -> the boss that summons it
		private static readonly Dictionary<int, int> Minions = new()
		{
			[NPCID.Creeper] = NPCID.BrainofCthulhu, [NPCID.ServantofCthulhu] = NPCID.EyeofCthulhu, [NPCID.TheHungry] = NPCID.WallofFlesh,
			[NPCID.TheHungryII] = NPCID.WallofFlesh, [NPCID.LeechHead] = NPCID.WallofFlesh, [NPCID.LeechBody] = NPCID.WallofFlesh,
			[NPCID.LeechTail] = NPCID.WallofFlesh, [NPCID.Probe] = NPCID.TheDestroyer, [NPCID.Bee] = NPCID.QueenBee, [NPCID.BeeSmall] = NPCID.QueenBee,
			[NPCID.PlanterasHook] = NPCID.Plantera, [NPCID.PlanterasTentacle] = NPCID.Plantera, [NPCID.Sharkron] = NPCID.DukeFishron,
			[NPCID.Sharkron2] = NPCID.DukeFishron, [NPCID.DetonatingBubble] = NPCID.DukeFishron, [NPCID.MoonLordFreeEye] = NPCID.MoonLordCore,
			[NPCID.MoonLordLeechBlob] = NPCID.MoonLordCore, [NPCID.CultistBossClone] = NPCID.CultistBoss, [NPCID.AncientCultistSquidhead] = NPCID.CultistBoss,
			[NPCID.QueenSlimeMinionBlue] = NPCID.QueenSlimeBoss, [NPCID.QueenSlimeMinionPink] = NPCID.QueenSlimeBoss,
			[NPCID.QueenSlimeMinionPurple] = NPCID.QueenSlimeBoss
		};

		public static int Owner(int type) => Owners.TryGetValue(type, out int owner) ? owner : type;

		public static bool IsPart(int type) => Owners.ContainsKey(type);

		public static bool IsMinion(int type) => Minions.ContainsKey(type);

		public static int MinionOwner(int type) => Minions.TryGetValue(type, out int owner) ? owner : type;

		// The boss a part or a minion follows (its own type for anything else)
		public static int Leader(int type) => IsPart(type) ? Owner(type) : MinionOwner(type);
	}
}

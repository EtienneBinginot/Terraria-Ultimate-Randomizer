using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader.IO;
using UltimateRandomizer.Common.Configs;
using UltimateRandomizer.Common.Core;
using UltimateRandomizer.Common.Features;

namespace UltimateRandomizer.Common.Tests
{
	// Bell-shaped enemy stats, giants and minis, individual variation and its network sync, loot bonus
	public static class StatTests
	{
		public static void Register()
		{
			TestRunner.Register("BellCurveShape", BellCurveShape);
			TestRunner.Register("BellCurveBounds", BellCurveBounds);
			TestRunner.Register("ExtremenessEffect", ExtremenessEffect);
			TestRunner.Register("GiantMiniFrequency", GiantMiniFrequency);
			TestRunner.Register("GiantMiniBounds", GiantMiniBounds);
			TestRunner.Register("GiantHitboxCap", GiantHitboxCap);
			TestRunner.Register("IndividualSyncRoundTrip", IndividualSyncRoundTrip);
			TestRunner.Register("ClientIndividualLife", ClientIndividualLife);
			TestRunner.Register("NoGiantMinions", NoGiantMinions);
			TestRunner.Register("TransformRerollsIndividual", TransformRerollsIndividual);
			TestRunner.Register("VariantGetsIndividual", VariantGetsIndividual);
			TestRunner.Register("TransformKeepsIndividual", TransformKeepsIndividual);
			TestRunner.Register("StatCacheBossFlag", StatCacheBossFlag);
			TestRunner.Register("LootBonusCap", LootBonusCap);
			TestRunner.Register("LootBonusApplied", LootBonusApplied);
			TestRunner.Register("DamageCapRespected", DamageCapRespected);
		}

		private static double Share(float[] samples, Func<float, bool> predicate) => samples.Count(predicate) / (double)samples.Length;

		private static float[] Samples(float min, float max, double extremeness, int count = 20000, int seed = 1)
		{
			Random rng = new(seed);
			return Enumerable.Range(0, count).Select(_ => StatCurve.Sample(rng, min, max, extremeness)).ToArray();
		}

		private static void BellCurveShape()
		{
			float[] samples = Samples(0.5f, 3f, 2.2);
			float[] sorted = samples.OrderBy(x => x).ToArray();
			float median = sorted[sorted.Length / 2];
			Check.That(Math.Abs(median - 1) < 0.05, $"median x{median:0.00} instead of x1");
			double near = Share(samples, x => x >= 1 / 1.1f && x <= 1.1f);
			double far = Share(samples, x => x >= 2f);
			Check.That(near > 0.25, $"only {near:P0} of the values near normal");
			Check.That(far > 0.03 && far < 0.15, $"{far:P1} of the values at x2 or more (expected a few %)");
			Check.That(near > far * 2, "extreme values are as common as normal ones");
		}

		private static void BellCurveBounds()
		{
			Random rng = new(5);
			List<string> failures = new();
			for (int i = 0; i < 200; i++)
			{
				float min = (float)(0.1 + rng.NextDouble() * 1.5);
				float max = min + (float)(rng.NextDouble() * 5);
				foreach (float x in Samples(min, max, 1 + rng.NextDouble() * 3, 200, i))
				{
					if (x < min - 0.0001f || x > max + 0.0001f) failures.Add($"x{x:0.000} outside [{min:0.00}, {max:0.00}]");
				}
			}
			Check.All(failures, "Values outside the range");
		}

		private static void ExtremenessEffect()
		{
			double flat = Share(Samples(0.5f, 3f, 1), x => x >= 2f);
			double steep = Share(Samples(0.5f, 3f, 3), x => x >= 2f);
			Check.That(flat > steep * 1.5, $"extreme values: {flat:P1} at rarity 1, {steep:P1} at rarity 3");
		}

		private static NPC Sample(int type)
		{
			NPC npc = new();
			npc.SetDefaults(type);
			return npc;
		}

		private static void GiantMiniFrequency()
		{
			EnemiesConfig cfg = EnemiesConfig.Instance;
			NPC npc = Sample(NPCID.Zombie);
			Check.That(npc.TryGetGlobalNPC(out EnemyIndividualNPC individual), "no individual data");
			Random rng = new(9);
			int giants = 0, minis = 0;
			const int Rolls = 10000;
			for (int i = 0; i < Rolls; i++)
			{
				individual.Roll(npc, rng);
				if (individual.Kind == EnemyKind.Giant) giants++;
				else if (individual.Kind == EnemyKind.Mini) minis++;
			}
			double giantShare = giants * 100.0 / Rolls;
			double miniShare = minis * 100.0 / Rolls;
			Check.That(Math.Abs(giantShare - cfg.GiantChance) <= 1, $"{giantShare:0.0}% giants, setting {cfg.GiantChance}%");
			Check.That(Math.Abs(miniShare - cfg.MiniChance) <= 1, $"{miniShare:0.0}% minis, setting {cfg.MiniChance}%");
		}

		private static void GiantMiniBounds()
		{
			EnemiesConfig cfg = EnemiesConfig.Instance;
			float variation = 1 + (cfg.RandomizeEnemyStats ? cfg.IndividualVariation / 100f : 0);
			NPC npc = Sample(NPCID.Zombie);
			npc.TryGetGlobalNPC(out EnemyIndividualNPC individual);
			Random rng = new(10);
			List<string> failures = new();
			for (int i = 0; i < 5000; i++)
			{
				individual.Roll(npc, rng);
				if (individual.Kind == EnemyKind.Giant && (individual.Size < cfg.GiantSizeMin / variation - 0.01f || individual.Size > cfg.GiantSizeMax * variation + 0.01f)) failures.Add($"giant x{individual.Size:0.00}");
				if (individual.Kind == EnemyKind.Mini && (individual.Size < cfg.MiniSizeMin / variation - 0.01f || individual.Size > cfg.MiniSizeMax * variation + 0.01f)) failures.Add($"mini x{individual.Size:0.00}");
			}
			Check.All(failures, "Giant or mini sizes outside their range");
		}

		private static void GiantHitboxCap()
		{
			int cap = EnemiesConfig.Instance.MaxGiantPixels;
			Random rng = new(11);
			foreach (int type in new[] { NPCID.Zombie, NPCID.BlueSlime, NPCID.GiantTortoise, NPCID.Mothron })
			{
				for (int i = 0; i < 300; i++)
				{
					NPC npc = Sample(type);
					npc.TryGetGlobalNPC(out EnemyIndividualNPC individual);
					individual.Roll(npc, rng);
					if (individual.Kind != EnemyKind.Giant) continue;
					individual.Apply(npc);
					Check.That(npc.width <= cap + 1 && npc.height <= cap + 1, $"{npc.TypeName} giant is {npc.width}x{npc.height} pixels (limit {cap})");
				}
			}
		}

		private static void IndividualSyncRoundTrip()
		{
			NPC server = Sample(NPCID.Zombie);
			server.TryGetGlobalNPC(out EnemyIndividualNPC sent);
			Random rng = new(12);
			for (int i = 0; i < 200 && sent.Kind != EnemyKind.Giant; i++) sent.Roll(server, rng);
			// Only carried by the sync here (adaptive power is off during tests)
			sent.AdaptiveLife = 1.7f;
			sent.AdaptiveDamage = 1.3f;
			using MemoryStream extra = new();
			using BinaryWriter extraWriter = new(extra);
			BitWriter bits = new();
			sent.SendExtraAI(server, bits, extraWriter);
			extraWriter.Flush();
			using MemoryStream packet = new();
			using BinaryWriter packetWriter = new(packet);
			bits.Flush(packetWriter);
			packetWriter.Write(extra.ToArray());
			packetWriter.Flush();
			packet.Position = 0;
			using BinaryReader reader = new(packet);
			NPC client = Sample(NPCID.Zombie);
			client.TryGetGlobalNPC(out EnemyIndividualNPC received);
			received.ReceiveExtraAI(client, new BitReader(reader), reader);
			Check.Equal(sent.Kind, received.Kind, "kind");
			Check.That(Math.Abs(sent.Life - received.Life) < 0.0001f && Math.Abs(sent.Damage - received.Damage) < 0.0001f, "health or damage multiplier");
			Check.That(Math.Abs(sent.Defense - received.Defense) < 0.0001f, "defense multiplier");
			Check.That(received.AdaptiveLife == 1.7f && received.AdaptiveDamage == 1.3f, $"adaptive multipliers x{received.AdaptiveLife}/x{received.AdaptiveDamage}");
			Check.That(Math.Abs(client.scale - Sample(NPCID.Zombie).scale * received.Size) < 0.01f, $"size x{client.scale} on the client");
		}

		// A zombie rolled with a seed that gives the wanted kind (same seed = same roll on every copy)
		private static int SeedFor(EnemyKind kind)
		{
			NPC npc = Sample(NPCID.Zombie);
			npc.TryGetGlobalNPC(out EnemyIndividualNPC individual);
			for (int seed = 0; seed < 5000; seed++)
			{
				individual.Roll(npc, new Random(seed));
				// Giants above x1 health so that a damaged giant can have more life than the unscaled lifeMax
				if (individual.Kind == kind && (kind == EnemyKind.Giant ? individual.Life > 1.05f : Math.Abs(individual.Life - 1) > 0.05f)) return seed;
			}
			return -1;
		}

		private static (NPC npc, EnemyIndividualNPC individual) Rolled(int seed)
		{
			NPC npc = Sample(NPCID.Zombie);
			npc.TryGetGlobalNPC(out EnemyIndividualNPC individual);
			individual.Roll(npc, new Random(seed));
			return (npc, individual);
		}

		// Packet 23 does not sync lifeMax: clients must scale it themselves, without overwriting the synced life
		private static void ClientIndividualLife()
		{
			EnemiesConfig cfg = EnemiesConfig.Instance;
			if (cfg.GiantChance <= 0 && cfg.MiniChance <= 0) Check.Skip("no giants nor minis");
			List<string> failures = new();
			int baseLife = Sample(NPCID.Zombie).lifeMax;
			int tested = 0;
			foreach (EnemyKind kind in new[] { EnemyKind.Giant, EnemyKind.Mini })
			{
				int seed = SeedFor(kind);
				if (seed < 0) continue;
				tested++;
				(NPC server, EnemyIndividualNPC serverStats) = Rolled(seed);
				Vector2 serverBottom = server.Bottom;
				serverStats.Apply(server, false, true);
				int expected = Math.Max(1, (int)(baseLife * serverStats.Life));
				if (server.lifeMax != expected || server.life != server.lifeMax) failures.Add($"{kind} server: {server.life}/{server.lifeMax}, expected {expected}/{expected}");
				if (Vector2.Distance(server.Bottom, serverBottom) > 1) failures.Add($"{kind} server: feet moved from {serverBottom} to {server.Bottom}");

				// Full health: vanilla set life to the client's unscaled lifeMax before ReceiveExtraAI
				(NPC full, EnemyIndividualNPC fullStats) = Rolled(seed);
				fullStats.Apply(full, true, true);
				if (full.lifeMax != server.lifeMax || full.life != full.lifeMax) failures.Add($"{kind} full client: {full.life}/{full.lifeMax}, server {server.lifeMax}");

				// Damaged: the synced life (from the server) must stay, even when it equals the unscaled lifeMax (giant)
				int synced = kind == EnemyKind.Giant ? Math.Min(server.lifeMax - 1, baseLife) : Math.Max(1, server.lifeMax / 2);
				(NPC damaged, EnemyIndividualNPC damagedStats) = Rolled(seed);
				damaged.life = synced;
				Vector2 position = damaged.position;
				damagedStats.Apply(damaged, true, false);
				if (damaged.lifeMax != server.lifeMax || damaged.life != synced) failures.Add($"{kind} damaged client: {damaged.life}/{damaged.lifeMax}, expected {synced}/{server.lifeMax}");
				if (damaged.position != position) failures.Add($"{kind} client: synced position moved from {position} to {damaged.position}");
			}
			if (tested == 0) Check.Skip("no giant nor mini roll found");
			Check.All(failures, "Individual health wrong on clients");
		}

		// Boss minions are never giants or minis, and never give the giant bonus drop
		private static void NoGiantMinions()
		{
			int[] minions = { NPCID.Creeper, NPCID.ServantofCthulhu, NPCID.TheHungry, NPCID.Bee, NPCID.Probe, NPCID.QueenSlimeMinionBlue, NPCID.MoonLordFreeEye };
			List<string> failures = new();
			Random rng = new(13);
			foreach (int type in minions)
			{
				if (RandomNpcDropRule.GiantBonusAllowed(type, type)) failures.Add($"{Lang.GetNPCNameValue(type)} can give the giant bonus drop");
				NPC npc = Sample(type);
				if (EnemyIndividualNPC.Scalable(npc)) failures.Add($"{npc.TypeName} can be scaled");
				npc.TryGetGlobalNPC(out EnemyIndividualNPC individual);
				for (int i = 0; i < 300; i++)
				{
					individual.Roll(npc, rng);
					if (individual.Kind == EnemyKind.Normal) continue;
					failures.Add($"{npc.TypeName} rolled {individual.Kind}");
					break;
				}
			}
			// Control: an ordinary enemy keeps its giant bonus
			if (!RandomNpcDropRule.GiantBonusAllowed(NPCID.Zombie, NPCID.Zombie)) failures.Add("a zombie cannot give the giant bonus drop");
			Check.All(failures, "Boss minions can be giants or give giant loot");
		}

		private static void TransformRerollsIndividual()
		{
			if (Main.netMode != NetmodeID.SinglePlayer) Check.Skip("single player only");
			if (!EnemiesConfig.Instance.RandomizeEnemySpawns) Check.Skip("enemy spawns are not randomized");
			Player player = Main.LocalPlayer;
			for (int attempt = 0; attempt < 10; attempt++)
			{
				int index = NPC.NewNPC(new EntitySource_SpawnNPC(), (int)player.Center.X + 1200, (int)player.Center.Y - 200, NPCID.BlueSlime);
				Check.That(index >= 0 && index < Main.maxNPCs, "could not spawn a slime");
				NPC npc = Main.npc[index];
				try
				{
					if (EnemiesConfig.Instance.EnemySwapMode == EnemySwapMode.ByBiome && BiomeSlots.TypeOf(EnemySwapPool.BiomeTarget(npc)) == NPCID.BlueSlime)
					{
						Check.Skip("the slime's slot keeps a slime here in this world");
					}
					EnemySwapSystem.ProcessSlot(index, 100);
					if (npc.type == NPCID.BlueSlime) continue;
					Check.That(npc.TryGetGlobalNPC(out EnemyIndividualNPC individual) && individual.Rolled, $"{npc.TypeName} has no individual stats after the swap");
					return;
				}
				finally
				{
					EnemySwapSystem.Pending[index] = false;
					npc.active = false;
				}
			}
			Check.That(false, "the slime was never swapped");
		}

		// Spawns a natural enemy off screen with a forced roll whose health multiplier is not 1 (seed search); null to skip
		private static NPC SpawnRolled(int type, out EnemyIndividualNPC rolled)
		{
			if (Main.netMode != NetmodeID.SinglePlayer) Check.Skip("single player only");
			Player player = Main.LocalPlayer;
			int index = NPC.NewNPC(new EntitySource_SpawnNPC(), (int)player.Center.X + 1200, (int)player.Center.Y - 200, type);
			Check.That(index >= 0 && index < Main.maxNPCs, $"could not spawn {Lang.GetNPCNameValue(type)}");
			NPC npc = Main.npc[index];
			EnemySwapSystem.Pending[index] = false;
			// Fresh globals and base stats, then a roll found by seed
			npc.SetDefaults(type);
			EnemyIndividualNPC probe = new();
			int seed = Enumerable.Range(1, 300).FirstOrDefault(s =>
			{
				probe.Roll(npc, new Random(s));
				return Math.Abs(probe.Life - 1) > 0.05f;
			});
			if (seed == 0)
			{
				npc.active = false;
				Check.Skip("no individual variation, giants or minis with these settings");
			}
			EnemyIndividualNPC.RollAndApply(npc, new Random(seed));
			rolled = npc.GetGlobalNPC<EnemyIndividualNPC>();
			return npc;
		}

		// A SetDefaults (Pinky variant) or a vanilla Transform (zombie variant) recreates the globals: the NPC gets its own
		// roll back, not a new one, with the health ratio it had
		private static void VariantGetsIndividual() => KeepsRollThrough(NPCID.BlueSlime, npc => npc.SetDefaults(NPCID.Pinky));

		private static void TransformKeepsIndividual() => KeepsRollThrough(NPCID.Zombie, npc => npc.Transform(NPCID.BaldZombie));

		private static void KeepsRollThrough(int type, Action<NPC> change)
		{
			NPC npc = SpawnRolled(type, out EnemyIndividualNPC before);
			try
			{
				(float life, float damage, EnemyKind kind, byte id) = (before.Life, before.Damage, before.Kind, before.RollId);
				npc.life = npc.lifeMax / 2;
				EnemyIndividualNPC.SnapshotRatio(npc);
				change(npc);
				Check.That(!npc.GetGlobalNPC<EnemyIndividualNPC>().Rolled, "the change kept the globals: nothing to test");
				EnemyIndividualNPC.RollMissing(npc);
				EnemyIndividualNPC after = npc.GetGlobalNPC<EnemyIndividualNPC>();
				Check.That(after.Rolled, $"{npc.TypeName} has no individual stats");
				Check.That(after.Life == life && after.Damage == damage && after.Kind == kind && after.RollId == id,
					$"{npc.TypeName} rolled again: life x{after.Life:0.00} {after.Kind}, was x{life:0.00} {kind}");
				float ratio = npc.life / (float)npc.lifeMax;
				Check.That(Math.Abs(ratio - 0.5f) < 0.05f, $"health ratio {ratio:0.00} instead of 0.50");
			}
			finally
			{
				EnemySwapSystem.Pending[npc.whoAmI] = false;
				npc.active = false;
			}
		}

		// Boss parts and their owner share a type but may ask with different boss flags: each flag has its own entry
		private static void StatCacheBossFlag()
		{
			EnemiesConfig cfg = EnemiesConfig.Instance;
			if (!cfg.RandomizeBossHealth && !cfg.RandomizeBossDamage) Check.Skip("boss stats are not randomized");
			int type = NPCID.EyeofCthulhu;
			// Clearing is harmless: entries are recomputed from the seed
			EnemyStatCache.Clear();
			EnemyStatCache.Get(type, false);
			EnemyMultipliers afterOther = EnemyStatCache.Get(type, true);
			EnemyStatCache.Clear();
			EnemyMultipliers fresh = EnemyStatCache.Get(type, true);
			Check.That(afterOther.Life == fresh.Life && afterOther.Damage == fresh.Damage, "the boss multipliers depend on an earlier non-boss lookup");
		}

		private static void LootBonusCap()
		{
			float previous = 1;
			for (float factor = 0.2f; factor < 30; factor *= 1.3f)
			{
				float bonus = EnemyIndividualNPC.Bonus(factor, 0.5f, 3f);
				Check.That(bonus >= previous - 0.0001f, "the bonus is not increasing");
				Check.That(bonus <= 3.0001f, $"bonus x{bonus} above the cap");
				if (factor <= 1) Check.That(Math.Abs(bonus - 1) < 0.0001f, $"bonus x{bonus} for a weak enemy");
				previous = bonus;
			}
		}

		private static void LootBonusApplied()
		{
			if (EnemiesConfig.Instance.LootBonusStrength <= 0) Check.Skip("no loot bonus");
			NPC npc = Sample(NPCID.Zombie);
			npc.TryGetGlobalNPC(out EnemyIndividualNPC individual);
			float normal = EnemyIndividualNPC.LootBonus(npc);
			individual.Life = 4;
			individual.Damage = 2;
			Check.That(EnemyIndividualNPC.LootBonus(npc) > normal, "a strong enemy gets no loot bonus");
		}

		// Defense is subtracted from damage, so the mod's damage multipliers together (type x variation x giant x adaptive)
		// must stay under MaxDamageMultiplier even with a huge power gap, for enemies, bosses and boss parts
		private static void DamageCapRespected()
		{
			float cap = Math.Max(1, EnemiesConfig.Instance.MaxDamageMultiplier);
			List<string> failures = new();
			float? previousOverride = AdaptivePower.PowerOverride;
			try
			{
				// Far above any world: adaptive damage reaches its own cap
				AdaptivePower.PowerOverride = 50;
				foreach (int type in new[] { NPCID.Zombie, NPCID.BlueSlime, NPCID.Werewolf, NPCID.EyeofCthulhu, NPCID.SkeletronHand, NPCID.Plantera })
				{
					NPC npc = Sample(type);
					if (!npc.TryGetGlobalNPC(out EnemyIndividualNPC individual)) continue;
					float typeDamage = EnemyStatCache.Get(BossParts.Owner(type), EnemyStatsNPC.IsBossLike(npc)).Damage;
					if (typeDamage > cap + 0.001f) failures.Add($"{npc.TypeName}: type x{typeDamage:0.00}");
					Random rng = new(type);
					for (int i = 0; i < 3000; i++)
					{
						individual.Roll(npc, rng);
						float total = typeDamage * individual.Damage;
						if (total > cap + 0.001f)
						{
							failures.Add($"{npc.TypeName} ({individual.Kind}): x{total:0.00}");
							break;
						}
					}
				}
				// A living boss measured again with even more power: the rise stops at the cap
				NPC boss = Sample(NPCID.EyeofCthulhu);
				// Outside the slots: RaiseAdaptive stores the roll of its slot, which a real NPC could get back
				boss.whoAmI = -1;
				if (boss.TryGetGlobalNPC(out EnemyIndividualNPC bossStats))
				{
					int baseDamage = boss.damage;
					bossStats.Roll(boss, new Random(3));
					bossStats.Damage = EnemyIndividualNPC.DamageCap(boss) / 2;
					bossStats.AdaptiveDamage = 1;
					bossStats.Apply(boss);
					bossStats.RaiseAdaptive(boss, bossStats.AdaptiveLife * 2, 100);
					float bossType = EnemyStatCache.Get(NPCID.EyeofCthulhu, true).Damage;
					if (bossType * bossStats.Damage > cap + 0.001f) failures.Add($"raised boss: x{bossType * bossStats.Damage:0.00}");
					// baseDamage already has the type multiplier
					if (boss.damage > baseDamage * EnemyIndividualNPC.DamageCap(boss) + 1) failures.Add($"raised boss: {boss.damage} damage from {baseDamage}");
				}
			}
			finally
			{
				AdaptivePower.PowerOverride = previousOverride;
			}
			Check.All(failures, "Damage multiplied beyond the cap");
		}
	}
}

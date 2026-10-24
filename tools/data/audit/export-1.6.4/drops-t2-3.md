# Audit drops-t2-3
Summary: 847 rows (all `normal`; expert/master variants are folded in as conditions), owners of tier 2-3: evil-world enemies,
hornets, dungeon enemies, pirates, town NPCs (when killed), Queen Bee, Deerclops, Eater of Worlds, Brain of Cthulhu, Skeletron.
The plan is coherent overall: bosses keep their bag, trophy, mask, pet and a full set of permuted drops; no boss lost a
progression item (Pwnhammer/Wall of Flesh are not in this chunk); guaranteed summons and stations sit on common hosts of
the right tier (found tier <= item tier + 1). Main issues: some "extra" drops 3-4 tiers above the owner (Chlorophyte Arrow
x144-175 at 17 % on Harpy, Spectre Pants, Golem Fist, Spooky Wings, big stacks of hardmode ammo), item tier
misclassification of Bezoar and Rocket III, and a Hellforge guardian that is a Crimson-only enemy in this Corruption world
(DemoniteOre present, CrimtaneOre absent in worldsources).

## Defects (most severe first)

### D1. "Extra" drops far above the owner tier — severity: skip
| owner (tier) | item (item tier / true tier) | stack | chance | kind |
|---|---|---|---|---|
| Harpie (2) | ChlorophyteArrow (5/5) | 144-175 | 17.1 % | extra |
| Devoreur d'ames / EaterofSouls (2) | SpectrePants (6/6, found never) | 1 | 0.40 % | extra |
| Viking mort-vivant (2) | SpookyWings (5/6) | 1 | 0.43 % | extra |
| Manchot cruel (2) | FrostStaff (5/4) | 1 | 0.17 % | extra |
| Fille perdue (3) | GolemFist (6/7) | 1 | 0.86 % | extra |
| Cerveau de Cthulhu (2, boss) | SuperStarCannon (3 / true 5) | 1 | 17.3 % | extra |
| Cerveau de Cthulhu (2, boss) | OnyxBlaster (4/4, found 5) | 1 | 13.9 % | extra |
| Mineur mort-vivant (2) | SpiderStaff (4/4) | 1 | 0.96 % | extra |
| Squelette de serpent (3) | MeteorStaff, LaserRifle, HelFire (all 4) | 1 | 0.4-2 % | extra |

Why: items 2+ tiers (up to 4) above the owner, weapons/armor/accessories/ammo, beyond TierAllowance 1. The 0.2-1 % ones
look like the jackpot shape (acceptable surprise). The real concern is the high-chance rows: Chlorophyte Arrow (Plantera-era
ammo, 3 tiers early, 17 % per Harpy kill, about 160 arrows) and Brain's SuperStarCannon/OnyxBlaster (10-17 % from a
tier-2 boss; the Brain is absent in this Corruption world, so low impact here but real in a Crimson world).
Suspected cause: the extra drop roll in `MobLootFeature` uses a wide tier window and does not cap ammo stacks.
Fix: cap extras at owner tier + 1 unless it is the jackpot path (chance <= 0.5 %); cap stacks of ammo above the owner tier.

### D2. Item tier classification wrong (Bezoar, Rocket III) — severity: bug
| item | tier / true / found | where | note |
|---|---|---|---|
| Bezoar | 4 / 0 / 5 | permuted Hornet drops on about 6 owners (SpikedJungleSlime x2, AngryBonesBigHelmet, Frelon variants) at 1-2 % | wiki: pre-Hardmode accessory, 1 %/1.99 % from Hornet, Moss Hornet, Toxic Sludge (https://terraria.wiki.gg/wiki/Bezoar) |
| RocketIII | 1 / 6 / never | Monstre grimacant (2), surprise, stack 188-201, 1 % | wiki: Hardmode post-Plantera ammo (https://terraria.wiki.gg/wiki/Rocket_III); tier 1 lets about 200 of them be rolled at tier 2 |

Why: Bezoar at tier 4 gives findability 5 for a pre-hardmode jungle drop. Rocket III at tier 1 passes tier gating at the
start while its true tier is 6. Suspected cause: `ItemClassifier` (source/recipe-based tier of these two). Fix: pin
Bezoar to tier 1-2, Rocket III (and IV) to tier >= 6.

### D3. Large stacks of hardmode ammo from tier-2/3 enemies — severity: absurd
| owner | item (tier/true) | stack | chance |
|---|---|---|---|
| Crane maudit (3) | IchorBullet (3/4) | 197-212 | 60 % |
| Fourmilion envahisseur geant (3) | CrystalDart (3/4) | 100-156 | 24 % |
| Devoreur (3) | HellfireArrow (2/3, found never) | 172-193 | 60 % |
| Harpie (2) | ChlorophyteArrow (5/5) | 144-175 | 17 % |
| Tim (2) | IchorBullet | 106-128 | 5 % |
| Frelon Fatty (2) | CursedBullet (3/4) | 113-150 | 10 % |
| Monstre grimacant (2) | RocketIII | 188-201 | 1 % |

Why: 60 % of about 200 hardmode bullets from a common dungeon enemy hands out next-tier ammo at once. Stacks look scaled
to max stack. Fix: stack by tier distance (item tier above owner tier -> cap about 50) or lower the chance.

## Questions (unsure, needs a decision)
- Q1. Hellforge guardian = BloodCrawlerWall (Tikenfer, Crimson-only) at 12.5 %; BloodMoonStarter also hosted there (its other
  host WallCreeperWall 2.2 % is fine). This world is Corruption. If absent-evil owners are not swapped (BiomeSlots
  `ShareAbsentEvil` may still make them spawn), the guardian never spawns. The Hellforge recipe (index 3020, tier 2 via
  Sawmill: FlinxFur x2, PinkGel x30, Hook x2, Fireblossom x12) keeps it craftable, so not a hard softlock. Check that
  `PlaceGuardian` filters `AbsentEvil` hosts (the export may predate world-evil resolution).
- Q2. WormFood and GoblinBattleStandard (item tier 0) sit at 1.67 % on 5 Hornet/Eater of Souls hosts, found at tier 1.
  GuideVoodooDoll (1.67 %), BloodMoonStarter (2.2 %), ShadowKey (2.3 %) are similar. Below the 2 % `MinFindableChance`:
  confirm guaranteed items are exempt.
- Q3. GoldenKey: tier 0 / true 3 / found "never", yet 1.517 % on about 9 dungeon/cavern hosts (Squelette, Viking, Hoplite,
  Manchot cruel, Squelette furieux...). Each is under 2 % hence "never", cumulatively fine; dungeon golden chests are
  tier-3 gating. Count the sum of sources or raise to 2 %?
- Q4. "extra" drops never count for findability (HellfireArrow 60 % at Devoreur: found never; WarAxeoftheNight 100 % at Brain:
  never). By design? Then a 100 % boss extra is not a source.
- Q5. Killed town NPCs give strong replaced drops: PartyGirl OnyxBlaster 25 % (t4), Stylist CobaltNaginata 12.5 % (t4),
  TaxCollector OrichalcumSword 12.5 % (t4), Mechanic FormatC 12.5 % (true 4), Princess RainbowRod 12.5 % (t5, IsHardmode),
  Guide SpiderGreaves 100 % (t4; Spider armor needs Hardmode Spider Fangs), Steampunker SnowSolution x7 (true tier 9).
  Mostly within allowance (+1) but the Guide row is a guaranteed armor piece. Intended?
- Q6. Owner tier labels differ inside one family: AngryBones (-13, BigHelmet, BigMuscle) tier 2 but AngryBonesBig and -14 tier 3;
  pirates owner tier 3 but spawn tier 4 while carrying CoinGun/PirateStaff/LuckyCoin/DiscountCard (t5, 0.025-0.1 %).
  Dungeon guardians (Solidifier t1, ImbuingStation t2, spawn tier 3) are found 0-1 tiers after the item tier (ImbuingStation
  found 3). Harmless, noted.

## Fine / checked
- Bosses (Queen Bee, Deerclops, Eater of Worlds, Brain, Skeletron) keep bag 100 %, trophy, mask, pet and 6-12 permuted
  drops; Queen Bee/Deerclops weapons at vanilla 25-33 %; no progression item lost. Master-only items (AviatorSunglasses t8
  on EoW, DeerclopsMasterTrophy) are Master conditions and decorative.
- Guaranteed summons/stations (WormFood, GoblinBattleStandard, SlimeCrown, WorkBench, HeavyWorkBench, TinkerersWorkshop,
  IceMachine, HoneyDispenser, Solidifier, BoneWelder, Hellforge, CrystalBall, MechdusaSummon) are on hosts of their
  tier, found tier = true tier (CrystalBall and MechdusaSummon true tier 4 on spawn-tier-4 hosts).
- 100 % chances are only materials, vanity (Bride set, TopHat, WizardHat), expert duplicates, Metal Detector/War Table (50 %/100 %).
  No weapon at 100 % apart from trivial boss extras (HornetStaff t0 on Queen Bee, IceBoomerang t2 on Deerclops, WarAxeoftheNight
  t1 on Brain).
- Dungeon enemies keep key/lock drops (GoldenKey 1.5 %, LockBox 0.5 %, ShadowKey guaranteed); no same-item duplicates on one
  owner beyond expert/normal pairs. Repeated plans across Hornet variant ids (-57/-58/-60/-62...) are variants, not defects.

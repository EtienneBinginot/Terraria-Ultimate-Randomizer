# Audit drops-t0-1
Summary: 1141 rows, 238 owners (tier 0: 588 rows, tier 1: 553 rows, including Eye of Cthulhu and King Slime). Only "normal" rows exist (no expert-only differences exported). Bosses keep their bags, pet items, ores/seeds and most own loot; no boss lost a progression drop. Guaranteed placements (summons, stations) sit on tier 0-1 enemies as designed. Main issues: 100 % stacks of potions on ordinary zombies, huge ammo stacks (100-225) at 10-35 % including hardmode ammo, a few far-above-tier items in rare extras, and tier classification oddities (Pedguin set tier 8, pet items tier 7, Binoculars tier 4).

## Defects (most severe first)
### D1. 100 % potion stacks dropped by the most common zombies and demon eyes — severity: absurd
| owner (id) | tier | item | stack | chance | kind |
|---|---|---|---|---|---|
| Zombie -54, ZombieRaincoat, Zombie -55 | 0/1 | InfernoPotion | 8 | 100 % | extra |
| Zombie -28, -29, BaldZombie | 0/1 | LifeforcePotion | 1-7 | 100 % | extra |
| Zombie -26, -27 | 0/1 | BattlePotion | 3-4 | 100 % | extra |
| Blood Zombie | 1 | RagePotion | 3 | 100 % | extra |
| Demon Eye (-41, GreenEye) | 1 | HunterPotion | 8 | 100 % | extra |
| Goblin Thief | 1 | TitanPotion | 1 | 100 % | extra |

Every kill of the most frequent night enemy gives 8 Inferno or up to 7 Lifeforce potions: a tier-1 potion stack on tier-0 spawns. It floods the inventory and trivializes the buff economy. Zombie -26 also lists BattlePotion twice. Suspected cause: MobLootFeature extra drop rule rolls chance 1 for potions (exact rule unknown). Suggested fix: cap extra-drop chance for potions (e.g. 35 %) or scale stack by chance; avoid chance 1 for kind=extra on non-boss owners.

### D2. Ammo stacks of 100-225 at 10-35 % from tier-0 critters/enemies, some of hardmode ammo — severity: skip
| owner | tier | item (item tier / true tier) | stack | chance |
|---|---|---|---|---|
| Rampant de tombes (Tombcrawler) | 0 | IchorDart (3 / 4) | 205-224 | 17 % |
| Frelon | 1 | CursedBullet (3 / 4) | 113-150 | 10 % |
| Salamandre | 1 | IchorDart (3 / 4) | 75-219 | 12 % |
| Salamandre | 1 | JestersArrow (1) | 112-218 | 24 % |
| Ecureuil de diamant | 0 | ClusterRocketII (0) | 182-198 | 35 % |
| Libellule doree | 0 | ExplodingBullet (0) | 181-223 | 35 % |
| Cyprinodon | 0 | GoldenBullet (0) | 201-207 | 20 % |
| Lapin de saphir | 0 | FlamingArrow (0) | 156-209 | 35 % |

and 14 more (LavaRocket x2, HoneyRocket x3, RainbowFlare x3, FrostburnArrow, BlueFlare, UnholyArrow 84-194 at 35 %...).
Ichor Darts and Cursed Bullets are hardmode ammo (https://terraria.wiki.gg/wiki/Ichor_Dart); 200+ of them at tier 0 with 17 % is a 3-4 tier skip in ammo power plus an absurd stack. The tier-0 ammo stacks are only a flood. Suggested fix: gate ammo extras by the tier gap, or cap ammo stack at ~50-99 and scale with tier. Cause: extra drops use full-stack ranges for ammo without tier consideration.

### D3. Items 3+ tiers above the owner at non-negligible chance — severity: skip
| owner | tier | item (item tier) | chance | kind |
|---|---|---|---|---|
| Devoreur d'ames (-11) | 1 | SpectrePants (6) | 0.40 % | extra |
| Squelette | 1 | FlaskofIchor (4) x3 | 8.6 % | extra |
| Squelette | 1 | DartPistol (5) | 0.67 % | extra |
| Salamandre | 1 | MagicDagger (4) | 1.14 % | extra |
| Peon gobelin | 0 | CarbonGuitar (3) | 1 % | permuted |
| Oeil de demon (GreenEye) | 1 | DaedalusStormbow (5) | 0.13 % | extra |
| Lava slime | 1 | KOCannon (4) | 0.4 % | permuted |
| Fee verte | 0 | Gatligator (4) | 0.27 % | extra |
| Zombie | 0 | SpiritFlame (4) | 0.18 % | extra |
| Ecureuil de saphir | 0 | CobaltDrill (4) | 0.09 % | extra |

and about 10 more (all under 0.2 %: SlimeStaff x5, DemonScythe, BreakerBlade, DD2 sentry, music boxes).
Spectre Pants is a post-Plantera armor (tier 6); 0.4 % per Eater of Souls kill is reachable within a few hundred kills. A Cobalt Drill at tier 0 skips the whole pre-hardmode pick progression (rare though). Suggested fix: clamp "extra" rolls to owner tier + 2 outside the jackpot; exclude pickaxes/drills/armor from the far-above pool.

### D4. Item tier classification oddities — severity: bug
| item | export tier | true tier | where seen |
|---|---|---|---|
| PedguinShirt / PedguinHat / PedguinPants | 8 | 8 | permuted on Goblin Sorcerer, Corrupt Penguin, Zombie (tier 1, 0.67 %) |
| EyeOfCthulhuPetItem, KingSlimePetItem | 7 | 1 | kept on own boss (25 %) |
| SlimySaddle | 7 | 1 | King Slime 25 % |
| Binoculars | 4 | 1 | EoC 2.5 % |
| SlimeHook | 3 | 1 | King Slime 33 % |
| EatersBone | 3 | 2 | King Slime 5 % |
| ShroomitePlating | 0 | 6 | Golden Butterfly 60 %, x51-65 |
| MartianCostumeShirt | 1 | 7 | EoC 100 % |

Pedguin set, pet items, saddle, Binoculars (https://terraria.wiki.gg/wiki/Binoculars) and Slime Hook are pre-hardmode vanilla drops. The tier column probably comes from the value/rarity heuristic in ItemClassifier (rare >= 1 gold adds a tier, around l.996-1010). Mostly cosmetic, but ShroomitePlating and MartianCostumeShirt show a tier that disagrees with their true tier by 6 (a 60 % x65 drop from a tier-0 critter, and a 100 % drop from EoC). Suggested fix: classify pet/vanity rares from their source NPC, and give ShroomitePlating its recipe tier.

## Questions
Q1. Creeper ("Suiveur", tier 1): TissueSample replaced by Gel x30 (50-67 %) and CrimtaneOre replaced by Hellstone x10-45 at 67 % (master/expert/normal rows). Hellstone (tier 2) is within allowance, but 26-45 per kill from a common crimson mob makes molten gear trivial. Intended?
Q2. EoC drops CrimtaneOre AND DemoniteOre (30-90, 100 %) and both CrimsonSeeds and CorruptSeeds with an empty conditions column. If vanilla's world-evil condition was lost, the world gets both ores. Probably harmless but unintended; check in game.
Q3. WormFood and GoblinBattleStandard are guaranteed at only 1.67 % per kill (below MinFindableChance 2 %) on Hornet / Eater of Souls (tier 1); Abeemination and DeerThing 3.3 %; SuspiciousLookingEye 3.9 %. Findability counts them as found; is 1/60 per kill acceptable for a boss summon?
Q4. Station guardians: IronAnvil only on Antlion (desert, tier 0), Furnace only on Frozen Zombie (snow, tier 1) although Furnace is tier 0; each at 12.5 %. A single-biome host can delay early smelting. Another Furnace source (chests, world) may exist.
Q5. Sand Slime: Bone (ranged weapon, tier 3) permuted at 100 % x2-6 and 97 %. Weapon at 100 % but it is the cheap vanilla throwing Bone.
Q6. Zombie (-54) drops RedHusk (tier 2 material) at 100 % (vanilla Cochineal Beetle 100 % moved). A common zombie giving it every kill.
Q7. King Slime SapphireStaff 100 % (tier 0 weapon, extra); EoC MartianCostumeShirt 100 %. Low tier, probably acceptable.

## Fine / checked
- Duplicates per owner are limited to mode-split rows (Creeper Gel/Hellstone x3 for master/expert/normal conditions), plus a few real pairs (Lens, Hook, AmethystStaff, BatBat, WhoopieCushion, SlimeStaff, PigPetItem on one owner): low impact.
- EoC and King Slime keep Bag (100 %), pet item (25 %), seeds/ores, mask/own loot; King Slime also gets EoW trophy and EatersBone (permuted). No boss lost a progression drop.
- ShadowDiamond (MagicStorage) first-kill drops on bosses are fine.
- 64 guaranteed rows all sit on owners of the item tier or tier+1 (stations, SlimeCrown 5.5 % on about 20 slimes, summons); none above tier 1.
- All items resolve in items-ref and the item tier column matches it.
- Block/bar stacks (30-70 at 35 % on critters) are bold but within the wanted randomness.

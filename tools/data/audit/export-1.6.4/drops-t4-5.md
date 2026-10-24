# Audit drops-t4-5
Summary: 1003 rows (514 of tier 4 owners, 489 of tier 5 owners). Mechanical bosses, Wall of Flesh and Queen Slime are coherent
(Pwnhammer 100 % on WoF, Hallowed Bar 15-30 on every mech boss, Soul of Might/Sight/Fright 25-40 each on a different boss, Waffle Iron
on all four with MechdusaKill, guaranteed summons and stations on 2-4 hosts at 3.8-12.5 %). Main problems: the hardmode souls Light, Night
and Flight are not protected and ended up late or unreachable in this Corruption world; the Temple Key has a circular host plus a 12-key
stack; some tier 6-9 items sit on tier 4-5 enemies with vanilla chances; Martian Madness owners have wrong tiers; normal and Expert rules
of one vanilla pair are permuted independently. No mimic, key mold or biome key rows exist in this chunk.

## Defects (most severe first)

### D1. Soul of Night and Soul of Flight have no usable source — severity: softlock
| item | tier / true / findability | only sources found |
|---|---|---|
| SoulofNight (521) | 4 / 4 / never | CrimsonFishingCrateHard 50 % 2-5 (bags.tsv). This world has Demonite and no Crimtane (Corruption world), so the Crimson crate cannot be fished. CorruptFishingCrateHard now gives Ichor instead. |
| SoulofFlight (575) | 4 / 4 / never | DD2DarkMageT3 (Old One's Army wave 3, owner tier 7) 100 % 10-20 and 5-10 (drops-t6-9.tsv) |

No tier 4-5 owner in this chunk drops either soul at any chance. Night is unobtainable without crafting; Flight comes 3 tiers late (tier 7 vs item
tier 4). Both are hard-gate materials (wings, hardmode armor and weapon recipes, Mechanical summons in vanilla).
Cause: only Might/Sight/Fright/Hallowed Bar are protected (Protection.cs:19, ItemClassifier.cs:870); Light/Night/Flight are plain "Uncommon" materials
(MaterialTiers.cs:367-372) permuted with everything; only Light got a guaranteed host (see D2), and fishing crates are not checked against the world's evil biome.
Fix: give Night and Flight guaranteed sources (>= 2 %) on common tier-4 enemies like Light, and do not count a crate source whose biome is absent.

### D2. Soul of Light only drops from a post-Plantera dungeon skeleton, at boss-size quantity — severity: skip / absurd
| owner | owner tier / spawn tier | item | stack | chance |
|---|---|---|---|---|
| RustyArmoredBonesSword (Squelette en armure rouillée) | 5 / 6 | SoulofLight (tier 4) | 16-18 | 100 % |

Only source of the item (the Hallowed crate row was replaced by Giant Harpy Feather). Findability says 5, but the host spawns only from tier 6
(hardmode dungeon skeletons appear after Plantera), so real first access is 2 tiers after the item tier 4. Once there, one common enemy gives as many
souls as a mech boss (25-40) with certainty, vs vanilla 1-3 at about 33 %.
Cause: guaranteed-source host choice likely uses owner tier (5) not spawn tier (6); stack scaled for findability. Fix: choose a spawn-tier-4 host (Hallow or
underground enemies) and cap the stack (e.g. 1-5 at < 100 %).

### D3. Temple Key host is circular and Queen Slime gives 12 keys — severity: inconsistent / absurd
| owner | tier / spawn | item | stack | chance | kind |
|---|---|---|---|---|---|
| Lihzahrd | 4 / 6 | TempleKey (tier 6) | 1 | 2.9 % | guaranteed |
| QueenSlimeBoss | 5 / - | TempleKey | 12-12 | 100 % | extra |

Lihzahrd live inside the sealed temple (bricks unbreakable until Golem, the key is the only entrance), so this host cannot be reached without the key.
The only working source is the Queen Slime extra: 12 keys of a tier-6 one-use item at tier 5, and 100 %. Wiki: https://terraria.wiki.gg/wiki/Temple_Key.
Cause: host choice ignores that the host is behind the item; extra-drop stack roller does not special-case one-use items. Fix: exclude temple-only enemies
as hosts for TempleKey, clamp its stack to 1.

### D4. Spectre Staff, Shroomite Digging Claw, Prismatic Lacewing on tier 4 owners — severity: skip
| owner | tier / spawn | item | true tier | stack | chance | kind |
|---|---|---|---|---|---|---|
| Fée | 4 / 4 | SpectreStaff | 6 | 1 | 9.75 % and 5 % | permuted |
| SkeletronHand (Squeletron) | 4 / -1 | ShroomiteDiggingClaw (findability never) | 6 | 1 | 4.5 % | extra |
| Médusa | 4 / 4 | EmpressButterfly (summons Empress of Light) | 6 | 2-4 | 15 % | extra |
| Gangster de neige | 4 / 4 | BBQRibs | 6 | 3-4 | 60 % | extra (food, harmless) |
| Nimbus furieux | 4 / 4 | PortalGunStation | 6 | 4-4 | 4.8 % | extra |

Items 2 tiers above the owner, with chances high enough to count as findable (>= 2 %). A Spectre Staff at about 15 % from a hardmode-start fairy,
a top drill and Empress summons before Plantera are skips beyond TierAllowance 1.
Cause: permuted rules keep the vanilla slot chance (Spectre Staff keeps 5 % / 9.75 %), and extra rolls use the item tier (EmpressButterfly tier 3, true 6).
Fix: scale the chance down (below the 2 % findable threshold) when true tier minus owner tier >= 2.

### D5. Tier 8 healing potions and tier 9 ammo as common drops — severity: skip / absurd
| owner | tier / spawn | item | true tier | stack | chance |
|---|---|---|---|---|---|
| Frelon de mousse (-18, -19, MossHornet: 3 rows) | 5 / -1, -1, 4 | SuperHealingPotion | 8 | 8-8 | 40 % each |
| Accrocheur (Clinger) | 5 / 4 | SuperHealingPotion | 8 | 4-4 | 20 % |
| Braconnier des sables | 5 / 4 | SuperHealingPotion | 8 | 1-7 | 5 % |
| Taré | 5 / 6 | SuperHealingPotion | 8 | 8-8 | 4 % |
| Boucher | 5 / 6 | MoonlordBullet | 9 | 76-114 | 2.1 % |
| Rétinaser, Spazmatisme | 5 / - | StyngerBolt | 7 (item tier 5) | 96-155 | 100 % each, no MissingTwin condition |
| Épouvantail | 5 / 6 | StyngerBolt | 7 | 197-215 | 100 % |
| Épouvantail | 5 / 6 | ChlorophyteArrow | 5 | 218-221 | 60 % |
| Épouvantail | 4-5 / 6 | SandSolution 11-14 at 24 % (true 9), CryocoreBrick 25-45 at 3.4 % (true 9) | | | |

and Archer elfe (ChlorophyteBullet 87-162 at 100 %, tier-fitting but huge). Eight Super Healing Potions at 40 % from Moss Hornets (very common) trivialise
healing 3 tiers early; ammo stacks of about 200 at 100 % from one scarecrow are far beyond normal.
Cause: the extra-drop roll uses the item tier (Super Healing Potion = 6, true 8), scales stacks for low-value consumables/ammo and gives chance 1 to "common" extras.
Fix: use true tier in the extra window, cap potions to 1-3 and <= 10 % above owner tier + 1, cap ammo stacks at about 50.

### D6. Martian Madness owners have wrong tiers; saucer drops a 100 % weapon — severity: bug / inconsistent
| owner | tier / spawn |
|---|---|
| MartianSaucer (Soucoupe martienne) | 4 / -1 (event miniboss, vanilla needs Golem) |
| MartianEngineer, RayGunner, BrainScrambler | 4 / 7 |
| MartianDrone, ScutlixRider | 5 / 7 |

Saucer extra rows (17): PygmyStaff 100 %, SuperAbsorbantSponge 100 %, SpectreGoggles 69 %, RazorbladeTyphoon 69 %, IlluminantHook 52 %, DD2SquireDemonSword 52 %,
TikiPants 52 % and 35 %, IceBow 35 %, BlizzardStaff 35 %, LaserMachinegun 10 %, DD2LightningAuraT3Popper 14 %, SnowmanCannon 7 %; item true tiers 5-7 rolled with owner tier 4.
Not a real skip while the event needs Golem (spawn tier 7), but owner tier 4 is wrong (https://terraria.wiki.gg/wiki/Martian_Saucer) and the roll window is built on it.
Cause: `NpcTiers` stat estimate instead of event tier. Fix: pin Martian Madness NPCs to tier 7.

### D7. Singular vanilla "always" rewards land on a common enemy at 100 % — severity: absurd (low)
| owner | tier | item | chance |
|---|---|---|---|
| Frelon (-65) | 4 | MetalDetector, WarTable | 50 % (100 % Expert) |
| SkeletronHand | 4 | BlackPearl 6-22, SuperManaPotion 6-7 | 100 % |
| Œil errant | 4 | IchorDart 79-80 | 100 % |
Cause: permuted rules (Lost Girl, Dark Mage T1) keep chance 1. Harmless items, note only.

## Questions
- Q1. Normal and Expert rules of one vanilla pair are permuted independently: 33 of 60 permuted Expert rows have no normal row on the same owner (e.g. SpectreStaff Expert 9.75 % on Ragged Caster but normal 5 % on Hell Armored Bones; MonkStaffT2 Expert on Unicorn, normal on Ogre; InfernoFork). Desired? Players of different difficulties find the same item on different enemies.
- Q2. Mimics are owner tier 6 in drops-t6-9 (vanilla hardmode mimics are tier 4): their loot (Philosopher's Stone, Titan Glove, Dual Hook, Cross Necklace) comes 2 tiers late. Outside my chunk, check NpcTiers.
- Q3. Guaranteed Pwnhammer / Mechanical Skull / Worm sit also on Rusty and Blue Armored Bones (spawn tier 6) although findability reports 4. Other hosts at spawn 4 exist (Mummy, Chaos Elemental), so harmless here; but does findability use owner tier instead of spawn tier (see D2)?
- Q4. MythrilAnvil (12.5 %) only on Zombie à spores (spawn -1) and AdamantiteForge only on Ice Elemental: single station hosts, presumably intended (station quest), but no second host.
- Q5. StyngerBolt: item tier 5 versus true tier 7 (classifier gap).

## Fine / checked
- 24 `kept` rows (kites, pet items, pirate-ship mount, WoF goat mount, bags): vanilla, harmless.
- Mech bosses: HallowedBar on all four, Might on both twins gated by MissingTwin, Sight on Prime, Fright on Queen Slime; no duplication.
- WoF: Pwnhammer 100 %, emblems 25 %, Clockwork/Laser Rifle 25 %, Breaker Blade, Fire Whip normal.
- Guaranteed hosts for MechanicalEye (Shark, Blood Feeder), Mechdusa's Summon (5 hosts), QueenSlimeCrystal (Ice Elemental, Spore Zombie): 3.8-9 %, above 2 %, reachable at tier 4.
- 100 % weapons: only PygmyStaff (D6), HamBat on both twins (tier 4, harmless), Waffle Iron (Mechdusa), Pwnhammer on WoF.
- Stacks above 30 are ammo, blocks or boss materials; no 9999 values; no repeated item on one owner beyond normal/Expert pairs.
- Dungeon vanilla drops (Keybrand, Wisp in a Bottle, Mace Whip, Magnet Sphere, Tactical Shotgun, Rocket Launcher, Inferno Fork, Shadowbeam, Spectre Staff on armored skeletons and casters) stay on spawn-tier-6 hosts: consistent.

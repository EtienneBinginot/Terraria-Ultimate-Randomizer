# Audit bags
Summary: 1309 rows over 62 bag/crate/lock box plans. Boss bags are in good shape: every bag keeps vanilla-style items
(mostly "kept" or bag-only items permuted between bags), the hard gates keep a reliable source at their own tier
(Pwnhammer 100 % in the WoF bag, Temple Key 100 % in the Plantera bag, Hallowed Bars 20-35 in the three mech bags, Beetle
Husk 18-23 in the Empress bag, Demon Heart 100 % in the Skeletron bag) and nothing in a boss bag is more than +2 tiers above it.
Crates, Present, Goodie Bag, lock boxes are "lucky final plans": rows far above the crate tier exist, but runtime gating
(`DropHelper.LockGap`, limit = unlocked tier + TierAllowance) makes them drop nothing until unlocked, so they are not skips by
themselves. Real findings: a 12-stack Temple Key in the Queen Slime bag, a tier misclassification of bag-only accessories
(Soaring Insignia tier 2 / true 7), unobtainable biome keys, and a few consequences of "locked entries drop nothing" on
low-tier bags (Herb Bag, Oyster, Can of Worms nearly empty).

## Defects (most severe first)
### D1. Queen Slime bag gives 12 Temple Keys (100 %) — severity: absurd (and a gate one tier early)
| bag | idx | item | stack | chance | kind | item tier / true / findability |
|---|---|---|---|---|---|---|
| QueenSlimeBossBag (bag tier 5) | 10 | TempleKey | 12-12 | 1 | extra | 6 / 6 / f5 |

A single key opens the temple door; 12 is absurd. It is also a Plantera gate (the key is the Plantera reward, 100 % in the
Plantera bag idx 2) handed out guaranteed one tier early (findability 5 vs true 6), so the Lihzahrd Temple / Golem line
opens before Plantera. Within TierAllowance 1, but it is a hard-gate item and the stack is the real oddity.
Cause: `RandomNpcDropRule.ExtraDrops` (boss "random extra drops") rolls the item and a stack scaled from its original; no
stack cap for keys/unique consumables. Fix: cap the stack of single-use keys/gates at 1 (or exclude TempleKey from extras),
keep Temple Key a Plantera-only source.

### D2. Tier classification of some bag-only items disagrees with vanilla / true tier — severity: bug
| item | category | `tier` | true tier | findability | where |
|---|---|---|---|---|---|
| EmpressFlightBooster (Soaring Insignia) | Accessory | 2 | 7 | 6 | ObsidianLockbox idx5 20 %, FishronBossBag idx12 100 % |
| SlimySaddle | MountPet | 7 | 1 | 1 | KingSlimeBossBag, GoldenCrateHard 8 % |
| HoneyedGoggles | MountPet | 7 | 2 | 2 | DeerclopsBossBag |
| Picksaw | Tool | 6 | 7 | 7 | GolemBossBag 33 % |
| PortalGun | Misc | 7 | 9 | 9 | MoonLordBossBag |

Soaring Insignia is an Empress of Light (tier 7) reward; classified tier 2 it is rolled as a cheap slot replacement
(`ReplacementRequest.SourceTier = max(bagTier, Info.Tier)`), e.g. as a 20 % entry of the Obsidian Lock Box (open at
tier 3 with a Shadow Key). Gating on `TrueTierOf` (7) should lock it at runtime, but the plan/journal show it, and the
`tier` column is clearly wrong in both directions. Cause: unknown (looks like the tier of bag-only items comes from the
bag the item was permuted into / its first source, not from its vanilla one). Fix: make bag-only items take their
vanilla boss tier; add a test like CoherenceTests (which already pins SlimySaddle <= 1, HoneyedGoggles <= 2).

### D3. Biome keys have no source at all — severity: inconsistent
| item | tier | true tier | findability | planned recipe tier |
|---|---|---|---|---|
| JungleKey (1533) | 7 | 4 | never | never |
| CorruptionKey (1534) | 7 | 4 | never | never |
| CrimsonKey (1535) | 7 | 4 | never | never |
| HallowedKey (1536) | 7 | 4 | never | never |
| DungeonDesertKey (4714) | 7 | 4 | never | never |
| FrozenKey (1537) | 7 | 4 | 8 | 8 (only RuneWizard 12 %, extra) |

No enemy drop, bag or recipe lists them (grep of all drops/recipes files), so the hardmode Biome Chests can never be
opened with default settings; FrozenKey only at tier 8 although true tier 4. Not in a bag; noticed while checking
lock boxes. Cause: their vanilla rule is a rare (1/2500) biome-mob drop under `MinFindableChance` 2 %, and no
`GuaranteedSources` entry covers keys other than ShadowKey. Fix: guarantee them like ShadowKey (hardmode biome mobs), or
confirm biome chest loot is meant to be optional.

## Questions (unsure, needs the user's or the developer's decision)
Q1. Fishron bag: RainbowCrystalStaff 100 % (extra, true tier 9, bag tier 7: +2 beyond the allowance) and Twins bag: StyngerBolt
96-155 100 % (true 7, bag 5: +2). (ShroomiteBreastplate 4 % in the Prime bag and Tsunami 13 % in the Plantera bag are +1, fine.)
In `BagPlan.PermutedPlan` extras of a lucky plan get `LockGap = 0`, so the export hides the lock; at runtime `Redistribute`
locks them (staff true 9 > bag floor 7 + 1). If so these are export-only artifacts; if not, a guaranteed Moon Lord summon
staff from Duke Fishron would be a real skip. Please confirm in game with `Plan(lucky:false)`.
Q2. Herb Bag (tier 1): 14 rows at 5.6 % each (expected 0.79 items, about half of the bags are empty), and no row is an herb or
seed any more: LavaBucket x30, LogicGate_XOR, GoldenSink, GoldenLamp, banners, Tuna, EmptyDropper. Vanilla gives several herbs
and seeds per bag. Bold randomness, but is a bag with nothing useful acceptable? Same for Oyster (1.2 expected: Prismite x15 at
1.3 %, CreamSoda 100 %), Can of Worms (GoldBird 100 %, no bait left), Capricorn Legs/Tail (a vanity hat / shader only).
Q3. Cultist bag: only SafemanSunHair (vanity, 14 %) and 10 gold (expected 1.14). Vanilla is nearly as poor, but with no
extras the Cultist bag (tier 7) is useless; the Moon Lord bag also holds a BossBagBetsy at 20 % (a bag in a bag, fine).
Q4. Eater of Worlds bag: DemoniteOre appears twice at 100 % with no condition (20-60 and 80-110, up to 170 ore in one bag) plus
ShadowScale 20-40 (and Master-only 110-135 / 30-50). The Brain bag has only one normal row (80-110). Is idx1 (20-60) a non-bag
boss-drop row leaking into the bag plan (normal vs expert mismatch)? Also the Plantera bag holds GrenadeLauncher twice (idx8
14 %, extra idx16 100 %): duplicate on one owner.
Q5. Locked-entry loss: because locked entries drop nothing and are not redistributed for non-boss bags, early crates lose a lot.
With true tiers: at unlocked limit 1, 83 % of the Lava Crate and 82 % of the Golden/Dungeon crate chance mass is locked; at
limit 2 (after EoC) Golden Crate 64 %, Dungeon Crate 63 %, Jungle Crate 48 %, Corrupt Hard 59 %; Wooden Crate only 14 %. Lava
crates have 19 % slots (CelestialStone, TerraFartMinecart, Ice Queen / Everscream trophies) that give nothing for most of the
early game. Intended?
Q6. Hardmode crates (`*Hard`) base their rolls on bag tier 1-3, so the lucky plan shows post-Plantera ores and flasks (SpectreBar
1-11 at 1-3 %, ShroomiteBar, ChlorophyteBar, FlaskofNanites/Venom, DirtSolution tier 9 7-15 at 25 %, BeetleWings 19 %,
CelestialStone, PaladinsShield 19 %). Gated by progression at runtime, so likely fine; but a hardmode crate fished right after
WoF shows mostly tier-4 content with the same ores repeated (HallowedFishingCrateHard has 6 MeteoriteBar rows, FloatingIsland
crates 5 Hellstone/Meteorite rows with stacks up to 45): same item many times on one owner (harmless but dull).
Q7. Golden Key: tier 0, true tier 3, findability "never" (skeleton drop 1.5 % < 2 %), yet craftable from ONE CrimstoneBrick at a
Sawmill (planned tier 2, also used x3 in many cheap recipes). So the Golden Lock Box / dungeon locked chests open at tier 2,
before Skeletron (true 3). `BagAccessTier(LockBox) = trueTiers[GoldenKey]` = 3 so the box itself is fine. Intended cheapness?
Q8. Lock Box: the vanilla Shadow Key slot became DarkBlueSolution x15 (33 %); ShadowKey now comes from 3 guaranteed enemies at
2.3 % (Harpy, Anomoure, giant antlion) and LavaCrateHard. Fine for Obsidian Lock Box access, noted for reference.
Q9. Present: all 32 rows are `IsHardmode` (vanilla), 1.07 expected items, weapons/hooks at 0.2-0.6 % (PhoenixBlaster, FairyBell
0.2 %, AntiGravityHook, VenomBullet x5-10) plus TruffleWorm 6.7 % (true 4, f6). Rare and gated; fine.
Q10. Goodie Bag lists RockGolemHead (stack 2) three times, SkeletronPrimeMask twice, StarHairpin twice, Fedora twice
(vanity duplicates, 57 rows instead of about 30 vanilla): harmless.

## Fine / checked
- All 22 boss bags: bag-only items permuted between bags keep sane chances (1.0 for key items, 0.1-0.33 for weapons,
  0.14 masks); no bag item is more than +1 tier above the bag except the extras in Q1 and D1.
- Hard gates by boss: Pwnhammer (WoF bag 100 %), Temple Key (Plantera bag 100 %), Hallowed Bar 20-35 (Destroyer/Twins/Prime),
  mech Souls (Might in Twins, Sight in Prime, Fright in Queen Slime), Beetle Husk (Empress bag 100 %), LunarOre 90-110 and
  Luminite-tier weapons in the Moon Lord bag, Demon Heart (Skeletron bag, condition NotUsedDemonHeart), Crimtane/Demonite and
  Shadow Scale/Tissue Sample in the EoC/EoW/Brain bags (biome-conditioned in the EoC bag) are all present.
- Stacks: no absurd stack in non-boss bags (max 75 for decorative blocks/moss at 33 %, 45 ores in hard crates); boss-bag stacks
  are vanilla (ore 20-135, Hallowed Bar 20-35, souls 25-40, coins) plus D1.
- Chances: no weapon or accessory at >= 50 % outside boss bags; 100 % rows are keys/lock boxes (LockBox in the Dungeon crates,
  ObsidianLockbox in Lava crates), CanOfWorms/Oyster/Capricorn fillers and gold coins.
- Conditions (NotRemixSeed/RemixSeed pairs in the Fishron bag and Lock Boxes, Expert/Master variants of RadiantJewel, NoPortalGun,
  NotUsedDemonHeart, IsHardmode on all Present rows) can all be met in a normal playthrough.
- Lock box access: Golden Lock Box <- Golden Key (craftable tier 2), Obsidian Lock Box <- Shadow Key (guaranteed sources tier 3).

# Audit recipes-t0-4
Summary: 652 recipes (index 2519-4637) whose result is tier 0. Mostly fine: 516 are doable at tier 0, the rest at tiers 1/2/3/4/5/6/8 (35/41/18/31/8/2/1).
All stations used (Workbench, Heavy WB, Sawmill, Loom, Furnace, Anvils, Kegs, Bookcases, Tables, Chairs, Bottles, Cooking Pot, Dye Vat,
Demon Altar) are tier 0 and findable at tier 0. No ingredient of tier >= 3 appears in a recipe doable at tier <= 2. No "value short" or
"repaired" flag appears. The early boss-summon recipes (SuspiciousLookingEye, WormFood, BloodySpine) are doable at tier 0 with tier-0
findable ingredients. Main issues: 28 shell recipes with an empty ingredient list, huge counts of valuable ingredients in jars/timers, and
extreme value inflation of a dozen cheap shell/utility recipes. No softlock found.

## Defects (most severe first)
### D1. Shell recipes with NO ingredients (free craft or dead recipe) — severity: inconsistent
| idx | result | flags | ingr tier T | doable |
|---|---|---|---|---|
| 3015 | LavaBucket | rebuilt,shell | 0 | 0 |
| 3104 | QueenStatue | rebuilt,shell | 0 | 0 |
| 3204 | Marshmallow | rebuilt,shell | 0 | 0 |
| 4000 | RichGravestone1 | rebuilt,shell | 0 | 0 |
| 4002 | RichGravestone3 | rebuilt,shell | 0 | 0 |
| 4160 | WraithStatue | rebuilt,shell | 0 | 0 |
| 3246 | LihzahrdWorkBench | shell | -1 | 0 |
| 3310-3318, 3328-3330 | Blue/Green/Pink Dungeon Chair/Table/WorkBench/Bookcase (12) | shell | -1 | 0 |
| 3364-3368 | Obsidian Chair/Table/WorkBench/Bookcase (4) | shell | -1 | 0 |
| 3409-3412 | Gothic Chair/Table/WorkBench/Bookcase (4) | shell | -1 | 0 |
| 3717 | SteampunkCup | shell | -1 | 0 |

(28 rows in total.) All these items are "never" in items-ref (findability and planned recipe tier), so the recipe looks dead, which is
plausible for the -1 ones. But the 6 "rebuilt,shell" rows with T=0 got an empty ingredient list from the planner: if such a recipe is live
it is a free craft (QueenStatue, LavaBucket, Marshmallow). Suspected cause: RecipePlanner rebuild returns no ingredients for these shells
(value too low or empty vanilla list) and the recipe is neither switched off nor flagged. Suggested fix: when a rebuilt shell gets 0
ingredients, switch the recipe off (flag it) or give it a minimal ingredient; add a test "no live recipe has an empty ingredient list".

### D2. Absurd ingredient counts of valuable items (value matching by multiplying copies) — severity: absurd
| idx | result | planned ingredients | doable |
|---|---|---|---|
| 2546 | BlueJellyfishJar | PinkGel x30, RedSnapper x28 (v3750 each) | 2 |
| 2548 | PinkJellyfishJar | Vertebrae x30, RedSnapper x28 | 2 |
| 2547 | GreenJellyfishJar | Glowstick x30, HeartreachPotion x30, InfernoPotion x30, VioletHusk x8 (98 items, 140k) | 2 |
| 2550 | PurpleEmperorButterflyJar | Bass x16 (v2500 each) | 2 |
| 2554 | TreeNymphButterflyJar | InactiveStoneBlock x30 (v1000 each, each itself needs Bomb) | 3 |
| 2567 | GreenFairyJar | DemoniteBrick x30 + 3 EnchantedNightcrawler | 2 |
| 2598 | Fake_GolfChest | ActiveStoneBlock x15 + Bomb x30 | 3 |
| 2600 | Candelabra | Pumpkin x25, Lens x30 | 0 |
| 2855-2866, 2957 | ActuationRod, OutletPump, Timer1/3/5Second (5 recipes), StorageDeactivator | Bomb x28-30 each | 3 |

Bomb x30 appears in about 9 recipes. Collecting 28 red snappers or 60 tier-1 potions for one jar is impractical although the total value
matches vanilla (175k): the planner satisfies the target value with a big count of one expensive item. Bomb (findable only at tier 3) is also
the single pattern of the whole electronics group. Suggested fix: cap the count by item value (e.g. <= 8 when value >= 1000, <= 12 for
fish/critters) and avoid reusing the same single ingredient across a group.

### D3. Cheap utility/shell recipes 10x-300x above the vanilla item value — severity: absurd (low)
| idx | result | vanilla item value -> planned recipe value |
|---|---|---|
| 3302 | WallSkeleton | 1 -> 300 |
| 3001 | Spike | 1 -> 275 (JungleSpores, SkyBluePaint, Feather, Moonglow ...) |
| 3157 / 3257 | PinkIceBlock / CrossGraveMarker | 1 -> 250 |
| 3228 / 2999 / 3243 / 3014 | SlushBlock, ClayBlock, HoneyBucket, WaterBucket | 1 -> 201-232 |
| 3067 / 3256 / 3156 / 3151 | SiltBlock, GraveMarker, PurpleIceBlock, BoneBlock | 1 -> 146-180 |
| 2895 | ThinIce (non shell) | 1 -> 201 (3 items) |
| 2885 | BeeHive | 5 -> 51 |
| 2919 | Abeemination | 250 -> 1405 |
| 2635 | DeepRedPaint | 50 -> 300 |

and about 20 more with factor >= 40 (the median shell ratio is 1.55, so these are outliers, probably a minimum of 3-4 distinct
ingredients or a value floor around 100-300). Single blocks worth 1 copper that cost 3-5 different herbs/paints can drain early herbs.
Not blocking. Suggested fix: floor of the rebuild = small multiple of the vanilla value, 1-2 ingredients when value < 10.

### D4. Swapped results with value collapse or explosion — severity: absurd (low)
| idx | result (swapped slot) | vanilla recipe value -> planned |
|---|---|---|
| 2566 | CrimsonHardenedSand | 50020 (critter) -> 50 (1 Leather) |
| 2602 | SlimeCandle | 25032 -> 80 (Leather + 6 Gel) |
| 2577 | MarbleColumn (x2) | 3782 -> 150 |
| 2639 | CoralSofa | 50 -> 4000 (GreenThread x2 at Dye Vat) |
| 2644 | VortexPlatform (x2) | 50 -> 3003 (NebulaBrick a8, ShroomitePlating a6, MartianConduitWall a7; doable 8) |
| 2598 | Fake_GolfChest | 3782 -> 24000 |

Randomness is wanted, but the value follows the new result item, so factors range from 0.001x to 80x versus the replaced slot. VortexPlatform
is craftable only at tier 8 (consistent with its tier); the slot's old result PurplePaint is made elsewhere (3225), no loss. Low severity.

## Questions
- Q1. Bomb (tier 0, true tier 3, findability 3, only recipe StrangeBrew x1 at Kegs, doable 3) and Wire (tier 0, true 3, findability 2) are
  tier 0 in classification but gated to tier 2-3. Intended (shop replaced by random stock)? Bomb is the hidden prerequisite of about 9 recipes.
- Q2. GoldenKey: findability "never" (drops are 1.5 %, under the 2 % threshold), only recipe CrimstoneBrick x1 at Sawmill (a2). CrimstoneBrick
  has a recipe from CrackedGreenBrick x2 at Furnaces (688), so no Crimson world needed: OK. Confirm that a one-ingredient tier-2 recipe is the
  intended supply for the dungeon key.
- Q3. Hellforge (tier 0, true tier 2, vanilla recipe tier never): FlinxFur x2, PinkGel x30 (a2), Hook x2 (a1), Fireblossom x12 (a2) at Sawmill,
  doable 2. Hellstone smelting is at best tier 2 and heavy (30 Pink Gel); not a softlock, but check PinkGel reliability.
- Q4. Hook: planned recipe XenonMoss x11 (findability never, recipe tier 1). Hook itself is findable at tier 1 by drops, so fine.
- Q5. Protected coin conversions (2934-2939) have doable tier 1 (circular pair, coin availability a1) although coins are the base of the
  economy. No recipe in this chunk uses coins as an ingredient, so no impact seen here.
- Q6. TruffleWorm: tier 3, true tier 4, findability 6; GiantHarpyFeather tier 5, availability 4. Both look off versus vanilla progression
  (Truffle Worm: Mushroom biome fishing in Hardmode; https://terraria.wiki.gg/wiki/Truffle_Worm). Only used in TruffleWormCage (2603, doable 6).
- Q7. Early boss summons: SuspiciousLookingEye = Flounder x3 at Demon Altar (2912), WormFood = Stinger x8 + Blinkroot x15 + OrangePaint x30
  (2917), BloodySpine = Daybloom x22 + Vine x2 (2918). All ingredients are tier-0 findable, but 30 Orange Paint is heavy for a tier-0 summon
  (OrangePaint shell recipe is doable at 0 and Waterleaf herb replacement also gives it).

## Fine / checked
- Station reachability: all stations are tier-0 craftable/findable; DemonAltar recipes (2912, 2917, 2918) and 12 DyeVat recipes are doable at 0.
  Multi-station recipes (Sawmill + Kegs, Chairs + Furnaces) are tier 0.
- Ingredient tiers: high-tier ingredients appear only in recipes whose doable tier matches (EmpressButterflyJar 6, TruffleWormCage 6, Lihzahrdian
  shells 5, VialofVenom 5 from Souls and Ancient Cloth).
- Shell results that are tier 0 by name but true tier 4-6 (VialofVenom, Nanites, GoldDust, EmptyBullet, SmokeBlock) have recipes doable
  at their true tier.
- Protected recipes (6 coin recipes) are unchanged; no "repaired", "value short" or "house" flags in this chunk.
- Value ratio of the 134 non-shell, non-swapped recipes: all within 0.6x-6x except the D3 outliers.
- Modded rows (MagicStorage StorageDeactivator 2957, CombinedStations1Item 2979 with 74 ingredient copies, UpgradeHellstone/BiomeGlobe as
  ingredients) behave like vanilla ones; CombinedStations1Item has the same many-copies style as D2 but is doable at tier 0.

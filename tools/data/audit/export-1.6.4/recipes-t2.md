# Audit recipes-t2
Summary: 508 recipes whose result is tier 2 (about 60 % dye/vanity/"shell" recipes). Stations (Furnaces, Kegs, DyeVat, Hellforge...) are all
reachable by tier 2, no circular recipe, no tier 2 weapon/armor obtained 2+ tiers early (swapped results are consistent with their
doable tier; the 142 recipes with doable tier > 2 are almost all vanity masks/trophies, fine). The main problem is item COUNT explosion:
the planner fills a coin value with 30 copies of a cheap-looking ingredient that is itself a deep, craft-only recipe (Fireblossom,
MagicStorage Upgrade, Jellyfish/Black Ink), so one recipe can need hundreds to thousands of raw items. Two recipes use an ingredient
that can never be obtained (Ice Mirror, Black Dragonfly).

## Defects (most severe first)
### D1. Fireblossom x30 / MeteoriteBar chain: thousands of raw items — severity: absurd
Fireblossom herb drops are replaced (herb Fireblossom -> Bass/Bass/Glowstick, HerbBag Fireblossom -> LavaBucket), so the only source of
the item is recipe 3049 = FleshBlock x15 + PinkPaint x2 + SpelunkerGlowstick x1 (Furnaces). PinkPaint (FossilOre + RottenChunk) and
SpelunkerGlowstick (FossilOre + Shiverthorn) each need a FossilOre (tier 1 desert mining). FleshBlock is 1 PinkGel at the MeatGrinder.
One Fireblossom therefore costs ~15 PinkGel + 4 FossilOre + RottenChunk + Shiverthorn; x30 = ~450 gel + ~120 FossilOre.
40 recipes of this chunk use Fireblossom, 31 of them at x30 (the stack cap).

| idx | result | planned ingredients (key) | raw cost |
|---|---|---|---|
| 2226 | MeteoriteBar | Fireblossom x30 | 450 gel + ~120 FossilOre for ONE bar (vanilla 3 ore) |
| 2278 | ImpStaff | MeteoriteBar x30 + Glowstick x30 | 30 bars = 900 Fireblossom |
| 2282 | FireproofBugNet | MeteoriteBar x25 + Bomb x30 | 750 Fireblossom |
| 2944 | MoltenPickaxe | CyanHusk x24 (each = 1 MeteoriteBar + Copper bar + Platinum bar + EnchantedNightcrawler) | 24 bars = 720 Fireblossom |
| 2243 | StarCannon | VioletHusk x30 (each = 1 CyanHusk) | 30 bars |
| 84, 132, 134, 2060.. | food, dyes, blocks | Fireblossom x30 + 1 filler (31 recipes) | ~450 gel each |
| 3020 | Hellforge (station, tier 0 chunk) | Fireblossom x12 | 180 gel + ~48 FossilOre, gates 18 recipes here |

MeteoriteBar's other sources are crates only (2.7 % per crate, 1-2 bars), so ImpStaff / Bug Net / Molten Pickaxe (findability never) are
effectively unobtainable although "doable tier 2" says otherwise. Molten Pickaxe matters: it is the pre-hardmode path to the 100 % pick.
Cause: RecipePlanner sizes counts from coin value (unit = ingredient value, capped at IngredientStackCap = 30; RecipePlanner.cs ~l.1798, ~2456)
and does not weigh the recipe depth of a craft-only intermediate. Fireblossom is "100 copper" but its crafted cost is ~230 coins at depth 1.
Fix: price craft-only ingredients by their recursive raw cost and cap total raw items; or exclude craft-only intermediates (Fireblossom,
husks, bars) from the "x30 filler" role; or let the herb keep Fireblossom as one of its replacement drops.

### D2. MagicStorage UpgradeHellstone used x2 to x14 — severity: absurd
MagicStorage/UpgradeHellstone (tier 2, findability never) has ONE recipe (2974): Vertebrae x30 + Sapphire x22 (Bottles). 18 recipes here use it.

| idx | result | count | raw cost |
|---|---|---|---|
| 2273 | MoltenFury | x14 | 420 Vertebrae + 308 Sapphire |
| 136 | BundleofBalloons | x12 (+3 accessories) | 360 Vertebrae + 264 Sapphire |
| 2776 | HorseshoeBundle | x9 + HellstoneBar x10 + 2 accessories | 270 Vertebrae + 198 Sapphire + 10 bars |
| 2230 | PurplePhaseblade | x4 + HellstoneBar x4 + PortableCraftingAccess | 120 Vertebrae + 88 Sapphire |
| 2896 | PanicNecklace | x4 + LifeCrystal | 120 + 88 |

and 13 more with x1-x3 (acceptable). A single bow needing 308 sapphires (a gem found in ones and twos) is not playable.
Cause: modded UpgradeHellstone value is huge (203750 per unit in the export) and the value planner sizes MoltenFury (20000 per bar value)
with 14 of them. Same root as D1. Fix: same; round counts down for ingredients whose own recipe needs 50+ raw items per unit.

### D3. Jellyfish / Black Ink / fish materials: hundreds of bricks per piece — severity: absurd
Damselfish, Stinkfish, RedHusk, PinkPricklyPear, PurpleMucos, BlackInk, White/BlackPearl, Green/PinkJellyfish are findability "never" and
craft-only. BlackInk (3241) = CrimstoneBrick x30 + Chain x30 (each a craft). They are then multiplied.

| idx | result | ingredient | raw cost |
|---|---|---|---|
| 3780 | GreenJellyfish | BlackInk x15 | 450 CrimstoneBrick + 450 Chain |
| 4445, 4454, 4455, 4457, 3964, 4025 | pylons, AlchemyTable, tuxedo pants | GreenJellyfish x1 each | same 450 + 450 |
| 4321 | PinkPearl | PinkJellyfish x3 + GreenJellyfish x3 | 6 jellyfish = ~2700 bricks + 2700 chain |
| 3781 | PinkJellyfish | GreenJellyfish x1 | 450 + 450 |
| 3664 | ManaHairDye | BlackInk x6 | 180 + 180 |
| 3240 | PurpleMucos | PinkPricklyPear x2 (= 2 BlackInk) | 60 + 60 |

Teleportation Pylons (most of the 8 here use a jellyfish, pearl or husks) and the Alchemy Table (a useful pre-hardmode station,
"doable tier 2" on paper) become 500-brick quests. Cause: as D1 (fish are "never" because fishing is runtime-random, so the planner
treats them as craft-only intermediates with a coin value). Fix: as D1; cap BlackInk-like intermediates to x1-x3.

### D4. Recipes planned with an ingredient of availability "never" — severity: inconsistent
| idx | result | bad ingredient | note |
|---|---|---|---|
| 2739 | CellPhone | IceMirror t2/anever (chest-only, no recipe, no drop) | doable "never"; sibling recipe 2738 works (d3) |
| 3265 | BallaHat | BlackDragonfly t1/anever (critter) | doable never; vanity |

Cause: the planner accepts ingredients whose availability is never (chest/critter-only items). Fix: exclude availability-never items as
ingredients, or give chest-only items a chest availability tier.

### D5. Ice Mirror has no obtainable source and a wrong category — severity: bug
IceMirror (id 3199): category BossSummon (wrong, it is a teleport tool), findability never, planned recipe tier never, no recipe in any chunk, no drop.
It is chest-only in vanilla; if the randomized chests never place it, it is lost (and D4 recipe dies). MagicMirror is similar (findability never)
but has two recipes (tier 1). Fix: reclassify both mirrors out of BossSummon and give IceMirror the "simple random recipe" fallback.

## Questions (unsure, needs a decision)
Q1. Hellstone and Meteorite ore items are never an ingredient of ANY recipe in the whole export (all recipe chunks checked). HellstoneBar is only
 PinkGel x30 + GoldBrickWall x25 (Furnaces, 2271; ~28 WebRopeCoil + 30 gel per bar) or crates (2.8-5 %). Creeper drops Hellstone 10-21 at 67 %
 (replacing CrimtaneOre). Is "ores no longer smelt" intended? Multi-bar recipes (MoltenBreastplate 2280 x11 = 330 gel + ~300 coils,
 HorseshoeBundle x10; 43 recipes use HellstoneBar) are very heavy.
Q2. Bomb is tier 0 and sold at tier 0 (first shop tier 0) but "availability 3" (shops do not count; drops only UndeadMiner/GemBunnyTopaz).
 14 tier-2 recipes use Bomb x30 and are pushed to doable tier 3 (NightsEdge 2285, PhoenixBlaster, FireproofBugNet, CellPhone 2738...). Same for
 Active/InactiveStoneBlock (tier 0, availability 3). Intended delay, or should shop stock count?
Q3. LifeCrystal (item tier 2) has a shell recipe doable at tier 0 (2987: GillsPotion x26 + LivingWoodChair x30 + Sapphire x8). Unlimited HP
 crystals from tier 0 (only costly, 15 uses). Wanted?
Q4. NightsEdge has two recipes (2285, 2286) both needing Cascade (tier 3): the chain is effectively tier 3 (true tier 3, consistent). 2286 also asks
 InactiveStoneBlock x30 + Actuator x24 + Tuna x5 + HellstoneBar (about 80 items). Fine?
Q5. LuckPotionGreater (216): Bass x30, EnchantedNightcrawler x25, RottenChunk x30, RecallPotion x30, ExplosiveBunny x29 = 144 items for one
 consumable (Lesser/normal luck potions also 5 ingredient types). Too much for a potion?
Q6. Utility shells delayed beyond tier 2: SharpeningStation (d5, TatteredBeeWing + HallowedBar), LogicGate_* x6 (d4), WarTable (d3), ImbuingStation (d3).
 About 100 vanity shell rows (masks, trophies of mech/lunar bosses) are classified tier 2 but doable 4-9: cosmetic only.

## Fine / checked
- Stations: Furnaces/Kegs/Sawmill/Loom/Bookcases/HeavyWorkBench/DyeVat/Bottles/CookingPots/Chairs/Tables/Anvils/TinkerersWorkbench are tier 0
 planned; Hellforge tier 2 (reachable, but see D1 cost); MeatGrinder tier 1; DemonAltar is a world object.
- Script check over all 508 rows: no ingredient availability above the recipe doable tier, no circular recipe, no station recipe needing itself
 (only the two "never" cases of D4).
- Value ratios beyond 3x or below 0.2x occur only on `swapped` rows (vanilla value belongs to the original result): expected. No tier 2 recipe
 yields a hardmode / Luminite / Soul item; swapped results (Bundle of Balloons, Horseshoe Bundle, Molten Pickaxe, Meteor armor, Necro Breastplate d3,
 BeetleMinecart d7) match their doable tier.
- Hellstone item has 4 junk recipes (620-626) from cheap blocks, harmless.
- Dyes (~90 rows): output x2/x1, DyeVat, value 6000-50000, fine.
- Abeemination, WormFood, SuspiciousLookingEye, BloodySpine and Skeletron summons are tier 0-1 results, not in this chunk.

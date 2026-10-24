# Audit recipes-t0-1
Summary: 652 recipes whose result is tier 0 (indices 0-804, all vanilla Terraria; flags: 587 rebuilt, 63 swapped, 2 house, no "value short" / "repaired" flag in this chunk, no lock key problem). 438 are doable at tier 0, the rest need tier 1-8 (almost all because of hardmode-only ingredients or a BoneWelder / Solidifier / Autohammer station).
Stations used by the doable-0 recipes are all Basic (WorkBenches, Furnaces, Anvils, Tables, Chairs, Sawmill, Loom, Bottles, Kegs, CookingPots, HeavyWorkBench, Bookcases) or LivingLoom / Solidifier (quest stations, doable 0 / 1): no unreachable station found. No circular dependency found (shelf/brick pairs on BoneWelder reference each other only through the vanilla column; planned ingredients are tier-0 materials).
Main issues: the two house recipes (Torch, Wood Platform) are absurdly expensive; 148 results carry "item tier 0" although their true tier is 2-8 (ammo, keys, hardmode blocks, lunar bricks); SeafoodDinner recipes cost up to 18 gold of ingredients; a few early items need very large ingredient piles.

## Defects (most severe first)

### D1. House recipes Torch and Wood Platform cost thousands of times their vanilla value — severity: absurd
| idx | result | planned ingredients | vanilla value | planned value | factor |
|---|---|---|---|---|---|
| 0 | Torch x3 | PlatinumOre x1 (v2250) + Flounder x1 (v750), no station | 6 | 3000 | 500x |
| 286 | WoodPlatform x2 | RedSnapper x1 (v3750), no station | 1 | 3750 | 3750x |

Torch is the most basic light source and a tier-0 staple (the player needs dozens; Flounder is ocean fishing, Platinum Ore must be mined). Torch findability tier is 1 (not found without crafting at tier 0) and WoodPlatform findability is "never", so crafting is the only access. A craft of 3 torches therefore eats an ore plus a fish; a stack of 30 torches = 10 platinum ore + 10 flounders. Wood Platform for housing needs one ocean fish per 2 platforms (same piece of ocean-only fish for every NPC house). Torch is used in only 4 planned recipes (vs 232 vanilla ones), so the damage is limited to the player's own use, but it is the very first thing a player crafts.
Cause: RecipePlanner.Rebuild, `Affordable(x) => house || ...` ("a house is built of anything found at once") drops the value cap for house recipes, and the target value is scaled from a stack-1 mismatch (a recipe yielding 3 torches is priced as 3000). Suggested fix: keep house recipes inside the value cap (MaxValueFactor x vanilla value + 1 silver) or restrict them to cheap, mass-available materials (Gel, Wood, Stone, Acorn, Cobweb, Daybloom...), never ores or fish.

### D2. 148 results are classified item-tier 0 although their true tier is 2 to 8 — severity: skip (possible) / bug (classification)
Of the 511 distinct results in the chunk, 148 have `tier` 0 but `true tier` >= 2 and findability "never" (only a late recipe). Categories: 95 Block, 39 Furniture, 8 Ammo, 5 Misc, 1 WeaponRanged.
| item | tier | true tier | note |
|---|---|---|---|
| NanoBullet, VenomBullet | 0 | 6 | vanilla recipe "never" |
| MiniNukeI, MiniNukeII | 0 | 6 | vanilla recipe "never"; stack 100, planned recipes need Spectre Bar, Turtle Shell... |
| CursedFlare | 0 | 4 | vanilla recipe "never" |
| PartyBullet, ExplodingBullet, GoldenBullet | 0 | 4 | need Pixie Dust (t4) |
| LightKey, NightKey | 0 | 4 | biome keys (hardmode chests) |
| HallowedTorch, Cursed/Ichor/Hallowed Campfire | 0 | 4 | |
| CobaltBrick, AdamantiteBeam, PalladiumColumn, TitanstoneBlock, BubblegumBlock, Pearlstone... | 0 | 4 | |
| ChlorophyteBrick / ShroomitePlating | 0 | 5 / 6 | |
| SolarBrick, NebulaBrick, VortexBrick, StardustBrick, Solar/Nebula/Vortex/Stardust platforms, SolarLamp | 0 | 8 | lunar-event blocks |
| MartianPlatform, Fake_MartianChest | 0 | 7 | |
| ... and 130 more (Living fire blocks, Echo blocks, hallowed/corrupt bricks, jars) | | | |

Why it is a defect: the item `tier` drives the tier the item is rolled at (drops, pots, chests, shops, surprise ingredients). With tier 0 the planner/loot can hand out 50 Venom or Nano Bullets, Mini Nukes, Lunar bricks, Chlorophyte/Shroomite blocks or hardmode keys to a tier-0 player (jackpot or surprise rolls included); the export shows them as "t0/a4..a8" in ingredient lists, so the recipe planner knows their real availability but the loot side may not. Check the drops/chests chunks for these names. Compare to the wiki: Venom Bullet / Nano Bullet / Cursed Flare are hardmode-only (https://terraria.wiki.gg/wiki/Venom_Bullet, https://terraria.wiki.gg/wiki/Nano_Bullet).
Suspected cause: ItemClassifier derives tier from the vanilla recipe; items with no vanilla recipe ("never": ammo, NightKey, living fire blocks) or whose recipe uses only tier-0 ingredient types (blocks/walls/bars of hardmode ore in a "brick" recipe) fall back to tier 0. Suggested fix: floor item tier by the tier of its planned/vanilla ingredients (true tier) or add an explicit table for ammo, keys and hardmode/lunar blocks.

### D3. ThornChakram (tier 0 weapon) is craftable only at tier 4 — severity: inconsistent
| idx | result | planned | station | doable |
|---|---|---|---|---|
| 411 | ThornChakram (swapped) | PlatinumOre x2 | CrystalBall | 4 |

Item tier 0, true tier 0, vanilla recipe tier 0, findability "never", planned recipe tier 4: the Jungle weapon is delayed by four tiers only because the swapped recipe picked a hardmode station (CrystalBall, Progression tier 4). Note also vanilla value of this slot is 3 against 4500 planned (the vanilla value shown is that of the slot's original recipe, so the factor is a swap artefact). Cause: swapped recipes keep the result's tier as ceiling but the station was chosen from the slot's tier; unknown exact rule (RecipePlanner station pick, `StationRecipeTiers`). Fix: when the result tier is 0 and the item has no other source, restrict the station to tier <= result tier (+allowance).

### D4. Seafood Dinner recipes priced like the fish they replace — severity: absurd
| idx | planned | planned value | note |
|---|---|---|---|
| 138 | Emerald x18 + IronBar x30, HeavyWorkBench | 180000 (18 gold) | original: Chaos Fish x2 |
| 150 | FallenStar x23 + TinBar x30 | 91250 | original: Prismite x2 |
| 143 | IronBar x20 | 30000 | |
| 140 | RedSnapper x5 + ClayBlock x30 | 18780 | |
| 149, 152, 147, 153, 137 ... | Platinum/Lens/Silver piles | 9000-18000 | |

A food with a vanilla sale value of a few copper (index 402, vanilla 2) has 16 recipes, one per fish of the vanilla fish group; the planner prices each one like its original fish pair (up to 300000). The player needs only the cheapest (151: Flounder x12, 9000), so this is not blocking, but it creates absurd rows (30 iron bars for a meal). Cause: RecipePlanner prices by `OriginalIngredientValue` of each slot; the per-fish recipes are not collapsed. Fix: cap the target value of an item to a multiple of the result's own value or of the cheapest sibling recipe.

### D5. Impractical piles of ingredients for early items — severity: absurd (mild)
| idx | result | planned | note |
|---|---|---|---|
| 677 | EmeraldStaff (tier 0 weapon, swapped) | SilverBar x16 + TinBar x29 | 45 bars, 80625 vs vanilla (8 gold-tier bars + emeralds); also Tin Ore is NOT generated in this world, TinBar comes from a Waterleaf recipe |
| 784 | SapphireRobe (swapped) | Amethyst x19 | 35625, ok count but 17000x the slot's vanilla value |
| 485 | ShellPileBlock | TinBar x27 | decor, 30375 |
| 138 | SeafoodDinner | IronBar x30 | see D4 |
| 299 | GemLockTopaz | TinBrick x30 + PumpkinSofa x30 | 11280 for a decoration |
| 330 | ObsidianSink | FrozenSlimeBlock x30 + PinkSlimeBlock x30 + LeadBrickWall x24 (84 pieces) | doable 1 |
| 506 | PurpleStainedGlass | 74 pieces | |
EmeraldStaff at 45 bars is the only one with gameplay weight (early magic weapon requires more metal than a full early armor set). Cause: swapped recipes take the slot's value as target and the partner's tier (see swap column). Fix: cap bar counts (e.g. <= 20 total ore/bar items) for tier-0 weapons.

### D6. Tier-1 gated recipes for tier-0 consumables / buffs — severity: inconsistent (low)
Potions and flares with an ingredient of availability tier 1: UnholyWater (idx 159: RagePotion t1), BloodWater (160: EndurancePotion t1), ObsidianSkinPotion (170, doable 1), RainbowFlare (34), RainbowCampfire (41), ExplosiveBunny (75). These are listed tier 0 but doable 1 because the planned ingredient is a potion/item only made after Eye of Cthulhu; harmless but their `tier` (0) does not match the recipe (1). Mostly consistent with the allowance; listed for completeness.

## Questions
Q1. Bomb (item tier 0, findability 3), Glowstick (2), PinkGel (2), Bass (2), Fireblossom (2), Vertebrae (2), AshBlock (2), Hook (1), Actuator/Wire (2) are tier-0 items first found at tier 1-3 but used as ingredients by many tier-0 recipes (e.g. "Bomb x30" in ~12 recipes of this chunk, all doable 3). Is it intended that a Bomb (sold by the Demolitionist from the start in vanilla; in this world slot 1 of Demolitionist sells RedSnapper and the Painter sells Bomb, see shops-t0) only arrives at tier 3? Results: LightKey/NightKey (Bomb x30), Fake_LivingWoodChest, Fake_GolfChest, Timers, ActuationRod, AnnouncementBox need Bomb x30 -> doable 3-4.
Q2. HealingPotion (idx 163) = ArcheryPotion x1 + Ale x2 + BambooBlockWall x6 at Bottles; LesserHealingPotion (161) = BattlePotion + LeadOre + ShadewoodChair x4. Both tier-0, findability "never", doable 0 only through a 2-step crafting chain (ArcheryPotion/BattlePotion are themselves crafted, value 1000 each). Healing potions are boss-fight essentials: is a 2-step chain acceptable? Planned value 1350 / 2350 vs vanilla 650 / 1300 (x2).
Q3. Campfire (idx 36) = AlphabetStatueB x2 (statues come from Statue Sculpture/Mason or chests?), doable 0: where does a tier-0 player get an Alphabet Statue? Not checked in worldsources (statue findability not examined).
Q4. 86 tier-0 recipes are doable only at tier 4 and 5-8 for lunar blocks; they are cosmetic, so no progression concern, but they inflate "never" findability (318 results tier 0 with findability never, mostly craft-only). OK if the journal only lists them as craft-only.
Q5. Bottle (idx 155) planned station WorkBenches instead of Furnaces, ingredients Moonglow + 2 SlushBlock (value 102 vs 2): Bottle (needed for all potions via Bottles station) now costs one herb + slush; fine for gameplay, only a note.

## Fine / checked
- All doable-0 recipes use only availability-0 ingredients and Basic or tier-0 quest stations (script check over 438 rows).
- Ratio planned/vanilla value for non-swapped, non-house doable-0 recipes: no row above ~70x except tiny base values (1-4 copper items rebuilt around a 100 value herb, e.g. Bottle, Bowl, platforms; value cap is silver based by design). No rows below 0.6x apart from swapped ones.
- Swapped rows: vanilla value column is the slot's, not the item's, so extreme factors (e.g. 17813x SapphireRobe) are artefacts of the slot, not of the item.
- Chlorophyte / Shroomite / lunar blocks recipes use ingredients of their own tier (a5-a8): no early skip through recipes of this chunk.
- Light/Night keys, hardmode torch, Cursed/Ichor flares: only craftable at tier 4 with Cobalt Bar / Ichor / Pearlstone, consistent.
- Station shells seen in other chunks (LivingLoom Flounder x30 + Vine x30 + Lens x15 at WorkBenches, doable 0; Solidifier, BoneWelder tier 3) are consistent with their recipes here (BoneWelder recipes doable 3, Solidifier recipes doable 1).
- Iron / Tin / Tungsten ore are not generated in this world (OreMap present False): IronBar, TinBar, TungstenBar are covered by planned recipes (IronBar: SilverOre + LeadOre at WorkBenches; TinBar: Waterleaf x8 at Bottles; TungstenBar: FallenStar x2), all doable 0.

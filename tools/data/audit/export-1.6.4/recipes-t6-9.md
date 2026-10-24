# Audit recipes-t6-9
Summary: 209 recipes (results tier 6 to 9) checked. The tier 8/9 content (Lunar bricks, fragment gear, Luminite gear, Zenith, Celestial Sigil,
Lunar Crafting Station) is coherent: every recipe is doable at its own tier or one tier later, no cycle, and the Sigil and the Lunar Crafting
Station come at tier 8 from pillar fragments only. Swapped rows show the vanilla columns of the original recipe of that slot (not of the result), so
vanilla/planned value ratios are meaningless for them (e.g. the Terra Blade row shows the Drill Containment Unit vanilla cost). Main issues: the
Shroomite Bar recipe (30 Spooky Wood + 30 walls per bar, 128 bars asked in this chunk), the MagicStorage "Upgrade (Blue Chlorophyte)" item used as
a universal ingredient (33 units, 30 Spider Fang + 30 Bombs each, no drop) including Beetle armor, and the Super Healing Potion tier.

## Defects (most severe first)
### D1. Shroomite Bar costs 30 Spooky Wood + 30 Spooky Wood Walls + 2 Ectoplasm — severity: absurd
Rows:
| idx | result | planned ingredients | station | doable |
|---|---|---|---|---|
| 2815 | ShroomiteBar | Ectoplasm x2; SpookyWood x30; SpookyWoodWall x30 | TinkerersWorkbench | 6 |

Bars asked elsewhere in this chunk (qty): ShroomiteBreastplate 30 (2444), SpectrePants 19 (2451), StardustBreastplate 19 (2495), SpectreMask 17 (2448),
ShroomiteHelmet 12 (2443), ShroomiteHeadgear 9 (2441), plus about 20 more in small amounts (Ghost Wings 4, Medallion 3, Wills Helmet 3, ...): 128 bars in total.
At 30 wood + 30 walls per bar, the Shroomite Breastplate alone is 900 wood and 900 walls (walls need more wood; SpookyWoodWall is "never" in findability, craftable at 6).
SpookyWood is only made 1:1 from a Spooky Twig (idx 309, twig value 125000) or a Broken Bat Wing (idx 885); the planner values it as 1 copper (item value),
so it believes the recipe is cheap (planned value 50060 for a bar worth 50000). Vanilla Spooky armor already wants 200 to 250 wood (idx 1564/1566 vanilla column),
so wood may be plentiful from Pumpkin Moon drops, but about 60 blocks per bar is far beyond what a hard-gate bar used by 14 recipes should cost.
Suspected cause: RecipePlanner rebuild picks counts from item value (v1 for blocks/walls), not from supply. Suggested fix: cap the count of low-value
block/wall ingredients (about 5 per craft) and do not use walls as ingredients of a hard-gate bar.

### D2. MagicStorage "Upgrade (Blue Chlorophyte)" is a universal vanilla ingredient with a heavy recipe and no drop — severity: absurd
Item 5595: tier 6, true 7, findability never; only source is idx 2970 (BeetleHusk x5, SpiderFang x30, MythrilBar x6, Bomb x30, MythrilAnvil), doable 7.
Spider Fang itself is tier 4 but findability 7 (no source >= 2 % before). Consumers in this chunk (qty): BeetleLeggings 5 (2439), BeetleHelmet 3 (2436),
BrokenHeroSword 2 (3446), BeetleWings 2 (2538), Moondial 2 (4633), SolarTablet 1 (2929, the Solar Eclipse summon), SolarMonolith 2 (2463), TerraFartMinecart 1,
Crownos/Skiph/Cenx/Aaron/Leinfors/Yoraiz0r vanity pieces 1 to 4 each, plus MagicStorage storage items. Total 14 recipes, 33 units.
33 units = 165 Beetle Husk, 990 Spider Fang, 990 Bombs, 198 Mythril Bars. The Beetle armor (tier 7 progression gear) alone needs 8 units
(40 husk, 240 fang, 240 bombs) plus 12 husk and 10 UpgradeHallowed for the shell. Impractical, and it makes a mod-only item a hard dependency of vanilla gear.
Cause: a modded material is picked by the rebuild like any other material, with x30 counts for cheap items (Bomb x30, SpiderFang x30).
Suggested fix: exclude modded upgrade/utility items from the vanilla ingredient pool, or cap ingredient counts at about 5 for items with findability never.

### D3. Super Healing Potion has tier 6 but is a post-Cultist item — severity: skip (1 tier) / bug
Row: idx 165 SuperHealingPotion: item tier 6, true tier 8, findability 7, planned recipe doable 8 (SolarBrick x14 a8, FragmentVortex x2 a8, ...).
Wiki: https://terraria.wiki.gg/wiki/Super_Healing_Potion (vanilla recipe needs the four pillar fragments, so tier 8). With tier 6 it can be rolled
(drops, chests, shops, pots) at tier 6 to 7, before the pillars, while its own craft is at 8. It is also an ingredient: WingsVortex x30 (2465) and
ShimmerMonolith x2 (4617). Cause: tier derived badly by the classifier (potion heuristic) instead of the fragment tier. Suggested fix: force tier 8.

### D4. Vanity shell recipes ask 27 Light/Dark Shards although no source reaches 2 % — severity: absurd
| idx | result | ingredient | station |
|---|---|---|---|
| 3439 | WillsLeggings | LightShard x27 | Tables |
| 3441 | JimsBreastplate | LightShard x27 | CookingPots |
| 3454 | DTownsLeggings | LightShard x27 | HeavyWorkBench |
| 4416 | SafemanDressLeggings | LightShard x27 | Sawmill |
| 4418 | FoodBarbarianArmor | LightShard x27 | Bookcases |
| 4108 | Yoraiz0rShirt | DarkShard x27 | Kegs |
| 4237 | LeinforsAccessory | DarkShard x27 | Bookcases |
| 3456 | CenxsDressPants | DarkShard x17 | Tables |
and 1 more. Totals: 135 Light Shards and 71 Dark Shards. Items 527/528 (tier 4) have findability "never" (only vanilla sub-2 % drops).
The same recipes also ask 30 Nebula/Vortex/Stardust/Solar Bricks (a8). Vanity only, so not progression, but the counts are not realistic.
Cause: shell recipe generator picks counts up to 30 regardless of sourcing. Suggested fix: cap ingredients with findability never at 3 to 5.

### D5. Tier labels that look wrong — severity: bug (minor)
| item | tier | true tier | note |
|---|---|---|---|
| SolarBrick / StardustBrick (4229/4232) | 0 | 8 | blocks classed tier 0, used x30 in about 12 recipes (a8) |
| DrillContainmentUnit (2768) | 7 | 9 | vanilla craft is 40 Luminite bars etc. |
| TerraBlade (757) | 7 | 6 | findability never, recipe 7 |
| BottomlessBucket (3031) | 6 | 0 | true tier 0 while its only recipe (3972) is 6 |
| ChippysCouch (4993) | 8 | 3 | craftable at 3 (Bomb x30, SharkFin x30) |
| LunarCraftingStation (3549) | 9 | 8 | recipe 4101 at 8 (consistent) |
Harmless individually. Brick tier 0 means jackpot/surprise rolls may place them anywhere early (they are only useful at 8+).

## Questions
- Q1. Lihzahrd Power Cell (1293): item tier 1, true tier 6, findability 5 (before the temple opens). Golem summon is tier 7. Intended? Its recipe is not in this chunk.
- Q2. Zenith (2290) in raw Luminite: CryocoreBrick x30 (6 Heavenforge each), MercuryBrick x30 (2 each), CosmicEmberBrickWall x30 (about 32): roughly 270 Luminite ore,
  plus LunarBlockSolar x30, Terrarian and Vampire Knives. Planned value 2.9M vs 4.8M vanilla, so in range, but is 270 ore available (LunarOre findability 9)?
- Q3. Spectre gear: SpectreHood 2449, SpectreRobe 2450, SpectrePickaxe 2452 each need Ectoplasm x30 (90 total), NaughtyPresent x17, HoneyAbsorbantSponge x18; ShroomiteBar needs 2 each.
  Ectoplasm is the post-Plantera dungeon drop; its shell recipe 3408 costs MartianConduitPlating x30 (= 30 Beetle Husk). Is the drop rate enough?
- Q4. Tier 8 results only craftable at 9: whole brick family (749-767, 2458-2462), DevDye 3967, BejeweledValkyrieBody 3999, Moonlord ammo (750, 2459). One tier late, all depend on LunarOre. Fine for progression.
- Q5. Tier 6 results whose recipe is only doable at 7: BeetleWings 2538, CelestialStone 2440, CelestialShell 2446, DestroyerEmblem 2844, SniperScope 2845, Moondial 4633.
  Forbidden Fragment (3783) is tier 4 with findability 7 but asked x13 to x23 (CelestialShell x23, PapyrusScarab x15, Terra Blade x7): high for a drop first found at 7.
- Q6. Vortex Leggings (2476) need the Drill Containment Unit (never found, craft = 30 Lunar Bars at 9) plus 9 bars: intended?
- Q7. Solar Tablet (Solar Eclipse summon): recipe now 1 Upgrade (Blue Chlorophyte) + 8 Turtle Shell, doable 7 vs true tier 6, so the Eclipse arrives a tier late (and via D2).

## Fine / checked
- Celestial Sigil (2930): FragmentVortex x30 + MartianConduitWall x30 at WorkBenches, doable 8, no Lunar Crafting Station needed. Moon Lord summon gated by pillars as wanted.
- Lunar Crafting Station (4101): FragmentStardust x22 + PinkGel x30, CookingPots, doable 8; every LunarCraftingStation recipe is 8 or 9, no cycle.
- Fragment crafts (759, 2472, 2482, 2492) convert fragments among themselves (a8 only): no pillar skip.
- Lunar brick family: Heavenforge(LunarOre) > Mercury > Astra > Cryocore > walls, a DAG with no cycle.
- Terra Blade (2522): doable 7, ingredients Forbidden Fragment, Chlorophyte, Spectre, Hallowed bars at CookingPots; not too early.
- Zenith, Luminite armors/pickaxes/drills/wings, Vortex/Nebula/Stardust/Solar sets: all doable 9 from Luminite bars, none earlier.
- Shroomite/Spectre armor pieces doable 6, basic stations (Furnaces, Anvils, Bookcases...), no station recipe needs itself.
- LihzahrdBrick (341, 584) craftable at 6 from Spooky Wood, harmless. LifeFruit (3274) at 5 is inside the allowance.
- Stacks: MoonlordBullet/Arrow 333, bricks 5, walls 4, vanilla-like; gear 1.

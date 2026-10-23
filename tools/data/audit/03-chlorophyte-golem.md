# Audit 03: Chlorophyte / Shroomite / Spectre / Golem era

Data: `ItemTiers.tsv` (this world). Columns are quoted as T (`tier`, bucket), TT (`true tier`, what gating uses:
`ItemPool.UnlockTier(TrueTier)`), fd (findability), vr (vanilla recipe tier), pr (planned recipe tier). Note: `pr` is
`plan.Availability[item]` = min(found, crafted), so `pr` <= `fd` always. "recipe" below means the randomized recipe is
the reason (`fd` > `pr`), "found" means a drop/shop already gives it at `pr`.

Code read: `RecipePlanner.ResultTier` / `TargetTier` (Common/Features/RecipePlanner.cs:124-131, 1326-1333),
`ItemPool.UnlockTier` (ItemPool.cs:345), `Findability.BaseTiers` (GuaranteedSources.cs:1018+),
`ItemClassifier.NpcSourceTier` (ItemClassifier.cs:424).

## C. Systemic causes (read first: they explain almost every row of A and B)

### C1. Recipes target U = TrueTier - TierAllowance, and the hardmode wall makes U(5) = 4 (the Chlorophyte Pickaxe bug)
- `RecipePlanner.ResultTier` = `UnlockTier(TrueTierOf(result))` = TT - 1 (TierAllowance default 1), floored at 4 in hardmode.
  `TargetTier` = max(min(RecipeTier, vanilla availability), ResultTier). So every crafted item is planned with a key
  ingredient of tier TT - 1.
- For TT 6 and 7 that is "one boss early" (Spectre/Shroomite before Plantera, Beetle before Golem). For **TT 5 it means
  the first minute of hardmode**: U(5) = Hardmode = 4, before any mechanical boss. All 20 Chlorophyte items are
  craftable at 4 (table B). The in-game report (Chlorophyte Pickaxe, Cursed Flame as key) is exactly this: in this
  dump its recipe is 30 Gold Starry Glass Block [4], whose own recipe key is Cursed Flame [4].
- Worse for tools: Chlorophyte Pickaxe/Drill have pick power 200, the power Chlorophyte Ore needs (wiki
  https://terraria.wiki.gg/wiki/Chlorophyte_Ore: "requires at least a Pickaxe Axe or Drax", i.e. all three mech bosses).
  A 200 pick at tier 4 unlocks the whole tier-5 ore economy at hardmode start, so the -1 cascades.
- Rule to fix (pick one, mind "balance keeps randomness": changing ResultTier re-plans keys of live worlds):
  1. Crafting gets no allowance: `ResultTier` = TrueTier (a recipe is an unlimited source, unlike a 1-in-N drop); or
  2. At least no allowance across the hardmode wall: `ResultTier` = TrueTier when TrueTier = PostMechBoss (U(5) = 4
     collapses a whole boss stage, the other tiers only lose "one boss"); and
  3. Mining gate on tools: `ResultTier(result)` >= `MiningTier` of the ores its pick power newly unlocks
     (pick >= 200 -> tier of Chlorophyte Ore = 5; >= 210 -> Lihzahrd Brick tier). Same rule for `Gated()` drops of
     pickaxes/drills in `Findability`/`DropHelper.LockGap`.

### C2. Allowance drops make tier-T materials "available" at T-1 for crafting
- `Findability.BaseTiers` adds drops at `UnlockTier(TrueTier)` (= T-1). That is fine for loot, but `Availability`
  then treats the material as available in bulk at T-1: Chlorophyte Ore fd4 (TT5), Chlorophyte Bar fd4, Spectre Bar
  fd5, Shroomite Bar fd5, Ectoplasm fd5 (TT6), Beetle Husk fd6, Lunar Tablet Fragment fd6 (TT7), Spooky Wood fd5.
  `vanilla recipe tier` of every Chlorophyte item is 4 for this reason, and `TargetTier` caps by it.
- Rule: for crafting availability (`Availability`, `vr`, `TargetTier`'s cap) count a drop at its TrueTier, not at
  `UnlockTier`; or require a material to be found from a tier-T source of decent chance/stack before using it as
  bulk (x12-x30) ingredient.

### C3. Non-boss enemy drops have source tier 0 (`ItemClassifier.NpcSourceTier`)
- `NpcSourceTier` returns 0 for every non-boss NPC; only drop *conditions* raise it. Enemies whose spawn is gated
  (Lihzahrd / Flying Snake: temple behind Plantera's Temple Key; Dungeon Spirit, Paladin, Skeleton Sniper, Tactical
  Skeleton, Rusty Armored Bones, Necromancer, Diabolist, Ragged Caster, Bones Lee, Giant Cursed Skull: post-Plantera
  dungeon; Giant Tortoise, Ice Tortoise, Ice Golem, Angry Trapper, Mimics: hardmode biome spawns) give no floor. The
  item's TrueTier then comes only from its rarity: low-rarity drops of late enemies are far too low (Lihzahrd Power
  Cell TT1, Lizard Egg TT3, Rifle Scope TT4, SWAT Helmet TT2, Butcher costume TT2), high-rarity (8) drops of
  post-Plantera enemies are one too high (TT7 instead of 6).
- Rule: a spawn-tier table for gated enemies (temple 6, post-Plantera dungeon 6, eclipse: base 5 / Mothron+Butcher+
  Nailhead+Dr Man Fly... post-Plantera 6, hardmode-only biome enemies 4), used by `NpcSourceTier` and as a floor
  *and* ceiling hint for their drops' TrueTier. Mothron is treated as a boss with stat tier 5; it only spawns after
  Plantera (https://terraria.wiki.gg/wiki/Mothron) -> 6.

### C4. Rarity 8 maps to tier 7 for Plantera-era items
Plantera drops/bag (Grenade Launcher, Wasp Gun, The Axe, Spore Sac), post-Plantera dungeon and eclipse drops, Terra
Blade, Broken Hero Sword all sit at TT7 though they are post-Plantera (6). Over-tiering (comes late), lower priority
than C1-C3. Fix with C3's table / a boss-source ceiling (an item whose earliest source is a tier-6 boss or enemy should
not be TT7 by rarity alone).

## A. Mis-tiered items (TrueTier, the column gating uses)

| id | internal name | current T/TT | correct | reason |
|---|---|---|---|---|
| 1293 | LihzahrdPowerCell | 1/1 | 6 | Lihzahrd chests / temple Lihzahrds, temple needs Temple Key from Plantera (https://terraria.wiki.gg/wiki/Lihzahrd_Power_Cell). C3. Biggest single mis-tier: a Golem summon can roll pre-hardmode |
| 2195 | LihzahrdFurnace | 1/1 | 6 | Only in Lihzahrd chests (or Steampunker post-Golem) (https://terraria.wiki.gg/wiki/Lihzahrd_Furnace); source "none" |
| 1172 | LizardEgg | 3/3 | 6 | Lihzahrd/Flying Snake temple drop (https://terraria.wiki.gg/wiki/Lizard_Egg). C3 |
| 1300 | RifleScope | 4/4 | 6 | Skeleton Sniper, post-Plantera dungeon (https://terraria.wiki.gg/wiki/Rifle_Scope). C3 |
| 1858 | SniperScope | 6/7 | 7 (TT ok) | Rifle Scope + Destroyer Emblem (Golem) - bucket 6 harmless |
| 1137-2836, 3906 | Lihzahrd furniture (Door, Chest, Chair, Table, WorkBench, Bed, Lamp, Candle, Lantern, Chandelier, Candelabra, Bathtub, Bookcase, Piano, Dresser, Sofa, Clock, Sink, Platform, PressurePlate, Statue, WatcherStatue, GuardianStatue) | 0/0, source "gathered" | 6 | Placed only inside the Jungle Temple (needs Temple Key), vanilla crafted from Lihzahrd Brick at the Lihzahrd Furnace. `PlacedByWorldGen` -> "gathered" tier 0. Decor mostly, but PressurePlate/Statues get planned recipes at tier 0 (B) |
| 1101 / 1102 | LihzahrdBrick / Wall | 6/6, 0/6 | 6 (ok) | Brick ok (pick 210 + temple). Wall bucket 0 harmless |
| 756 / 787 | MushroomSpear / Hammush | 6/6 | 5 | Truffle sells them after any mech boss (https://terraria.wiki.gg/wiki/Truffle); source tier already says 5 but sources can only raise |
| 1253 | FrozenTurtleShell | 5/5 | 4 | Ice Tortoise, any hardmode snow. Mild over-tier |
| 1519 | IceFeather | 5/4 | 4 (TT ok) | Ice Golem, hardmode blizzard; bucket 5 only |
| 1328 | TurtleShell | 6/4 (known 6, table 4) | 4 (TT ok) | Giant Tortoise, any hardmode jungle (https://terraria.wiki.gg/wiki/Turtle_Shell). Known tier 6 vs table 4 is inconsistent; TT 4 is right |
| 1316-1318 | TurtleHelmet/ScaleMail/Leggings | 6/6 | 5 | Chlorophyte Bar + 1 Turtle Shell at a hardmode anvil: post-mech (same wiki page). Unsure if 6 was a deliberate choice |
| 4409 | SpectreGoggles | 5/6 | 6 (TT ok) | Cyborg post-Plantera; bucket only |
| 1543-1545 | SpectrePaintbrush/Roller/Scraper | 1/6 | 6 (TT ok) | Bucket 1 (rarity 0) while made of Spectre Bars: the bucket is used by rolls (`lists[cat, tier]`), so they sit in tier-1 Misc lists and get re-picked; harmless but noisy |
| 1182 | Seedling | 3/6 | 6 (TT ok) | Plantera drop; bucket only |
| 4989 | EmpressFlightBooster | 2/7 | 7 (TT ok) | EoL expert bag; bucket 2 |
| 4468 | BeetleMinecart | 2/7 | 7 (TT ok) | bucket only |
| 1570 | BrokenHeroSword | 7/7 | 6 | Mothron, post-Plantera eclipse (https://terraria.wiki.gg/wiki/Mothron). C3/C4 |
| 757 | TerraBlade | 7/7 | 6 | True Night's Edge + True Excalibur + Broken Hero Sword (https://terraria.wiki.gg/wiki/Terra_Blade): post-Plantera. C4 |
| 758, 1155, 1305, 3336 | GrenadeLauncher, WaspGun, TheAxe, SporeSac | 7/7 | 6 | Plantera drop / bag. C4 |
| 671, 679, 759, 938, 1183, 1254, 1266, 1444, 1445, 1446, 1513, 4679 | Keybrand, TacticalShotgun, RocketLauncher, PaladinsShield, WispinaBottle, SniperRifle, MagnetSphere, ShadowbeamStaff, InfernoFork, SpectreStaff, PaladinsHammer, MaceWhip | 7/7 | 6 | Post-Plantera dungeon enemies. C3/C4 |
| 3098, 3107, 3105, 2770, 3292 | ButchersChainsaw, NailGun, ToxicFlask, MothronWings, TheEyeOfCthulhu | 7/7 | 6 | Post-Plantera eclipse enemies. C4 (unsure for MothronWings/TheEyeOfCthulhu: also 6) |
| 1826, 1845, 4680, 4793 | TheHorsemansBlade, NecromanticScroll, ScytheWhip, SpookyWoodMountItem | 7/7 | 6 | Pumpkin Moon (medallion from Ectoplasm, post-Plantera) |
| 1514, 4740 | SWATHelmet, ButcherMask (+Apron/Pants) | 2/2 | 6 (vanity, low impact) | Tactical Skeleton / Butcher drops. C3 |
| 579 / 990 | Drax / PickaxeAxe | 4/5 | 5 (TT ok) | All three mechs' souls; bucket 4 only. Listed because they are the vanilla 200-pick gate C1 relies on |
| 1293 vs 1141 | (consistency) | | | Temple Key TT6 but Power Cell TT1: the summon is reachable 5 tiers before the key that opens its altar |

Golem drops (Picksaw, Heat Ray, Staff of Earth, Golem Fist, Possessed Hatchet, Stynger, Sun Stone, Eye of the Golem),
Beetle Husk, Lunar Tablet Fragment, Solar Tablet, Fishron and EoL drops: TT7, correct. Ectoplasm TT6, Spectre/Shroomite
Bar TT6, Chlorophyte Ore/Bar TT5: correct.

## B. Recipes / availability earlier than the tier

All 57 family rows with `pr` < TT are exactly TT - 1 (C1 + C2). The ones that matter:

| id | internal name | TT | fd | pr | cause | planned ingredients (key) |
|---|---|---|---|---|---|---|
| 1230 | ChlorophytePickaxe | 5 | 5 | 4 | recipe, C1 | Gold Starry Glass Block x30 [4] (its own key: Cursed Flame [4]). Pick 200 at hardmode start |
| 1231 | ChlorophyteDrill | 5 | never | 4 | recipe, C1 | Crystal Block x30 [4]. Pick 200 |
| 1262 | ChlorophyteJackhammer | 5 | 6 | 4 | recipe | Forbidden Fragment x23 [4] |
| 1006 | ChlorophyteBar | 5 | 4 | 4 | found (C2) + recipe | **Hallowed Seeds x28 [4]**: Dryad shop item, so unlimited Chlorophyte Bars bought at early hardmode (see D) |
| 947 | ChlorophyteOre | 5 | 4 | 4 | found, C2 | allowance drop; pick 200 needed to mine |
| 1002-1005 | Chlorophyte Helmet/Headgear/PlateMail/Greaves | 5 | never | 4 | recipe | Cobalt Brick Wall x30 / Ancient Cloth x30 / Pearlsand x30 / Cobalt Bar x30 [4] |
| 1001 | ChlorophyteMask | 5 | never | 5 | ok | key Living Demon Fire Block [4] but Soul of Sight x15 [5] keeps it at 5 |
| 1226-1229, 1232-1234 | Chlorophyte Claymore, Saber, Partisan, Shotbow, Chainsaw, Greataxe, Warhammer | 5 | 4-never | 4 | recipe | single tier-4 keys (Fire Feather, Butterfly Dust, Living Demon Fire x30, Titanium Bar, Hallowed Seeds, Living Ichor Block, Palladium Bar) |
| 1179 | ChlorophyteBullet | 5 | 4 | 4 | found | Mythril Bar + Endless Quiver + Ichor Dart |
| 2789 / 2792 | ChlorophyteBrickWall / ChlorophyteBrick | 5 | never | 4 | recipe | decor, harmless |
| 1503-1507, 2189, 1543-1545 | Spectre Hood/Robe/Pants/Pickaxe/Hamaxe/Mask, paint tools | 6 | 5-never | 5 | recipe | Broken Bat Wing, Shroomite Bar, Dark Shard, Spectre Bar [5]... SpectrePickaxe (pick 200) at 5 is fine |
| 3261 | SpectreBar | 6 | 5 | 5 | found (C2) | Spooky Wood x30 [5] + Titanium Bar + Ectoplasm x1 [5] |
| 1546-1550, 2176, 2791, 2794 | Shroomite armor, Digging Claw, plating | 6 | 5-never | 5 | recipe | Spectre/Shroomite Bar [5]... |
| 1552 | ShroomiteBar | 6 | 5 | 5 | found (C2) | Turtle Shell x3 [6] + Beetle Husk x1 [6] + Iron Bar: its recipe is actually 6, it is the allowance drop that gives 5 |
| 2199-2202 | Beetle Helmet/ScaleMail/Shell/Leggings | 7 | never/7 | 6 | recipe | Martian Conduit Wall x30 [6], Solar Tablet Fragment x30 [6], Beetle Husk [6] |
| 2280, 4468 | BeetleWings, BeetleMinecart | 7 | 6 | 6 | found | Solar Tablet Fragment x30 [6] |
| 2218, 2766, 2767 | BeetleHusk, LunarTabletFragment, SolarTablet | 7 | 6 | 6 | found | Golem / temple drops at the fight tier: by design |
| 1102 | LihzahrdBrickWall | 6 | never | 5 | recipe | Ice Feather x1 [5] |
| 1151-1154 | LihzahrdPressurePlate, LihzahrdStatue, WatcherStatue, GuardianStatue | (TT0, should be 6) | never | **0** | recipe, from A (TT0) | Plague Block Wall/Amber Wall/Palm Fence x30; Living Fire Block x30; Confetti x2; Ale+Copper Ore+Jungle Spores+Sticky Bomb. Temple items craftable pre-boss; decor, but the pressure plate is a temple mechanism |
| 3906 | LihzahrdPlatform | (TT0) | never | 0 | recipe (vr5) | Large Diamond + Green Silly Balloon |
| 3677 | Fake_LihzahrdChest | 3 | never | 2 | recipe | Rotten Chunk + Trapped Bone Chest ... (decor) |

Not too early: True Excalibur (Spectre Bar x23, 5 = TT5), True Night's Edge (Bone Feather x10 [5] + Titanium Bar,
5 = TT5), Terra Blade (Vortex Brick x30 [7] + Solar Tablet Fragment, 7; later than its correct 6, see D), Picksaw/
Golem drops (6 = Golem fight tier, by design).

Side finding (outside the family, but it breaks the Terra Blade chain's vanilla column): Night's Edge, True Night's
Edge and Terra Blade have `vanilla recipe tier` = never because **Muramasa (155) is fd never / pr never in this world**
(only source: Golden Lock Box; Findability does not count it). Muramasa looks unobtainable here: worth a check by
whoever audits pre-hardmode dungeon items.

## D. Absurd planned recipes

| id | item | recipe | why |
|---|---|---|---|
| 1006 | ChlorophyteBar (TT5) | Hallowed Seeds x28 | Shop item (Dryad, hardmode, TT4) turned into unlimited post-mech bars on day one of hardmode. Combined with C1 every Chlorophyte item is a shopping trip. Key ingredients from shops should not make a material of a higher tier |
| 1230 | ChlorophytePickaxe (TT5, pick 200) | Gold Starry Glass Block x30 | Decorative glass for the progression pickaxe; itself keyed on Cursed Flame [4] |
| 1552 | ShroomiteBar (TT6) | Turtle Shell x3 + **Beetle Husk** x1 + Iron Bar x19 | Reverse: a Golem material (TT7) inside a post-Plantera bar; harmless here only because the bar is also an allowance drop |
| 757 | TerraBlade (TT7, should be 6) | Vortex Brick x30 [7] + Solar Tablet Fragment x30 | Reverse: a lunar-pillar block (Vortex Brick TT8, available 7 only through an allowance drop of the brick) for a post-Plantera sword |
| 1570 | BrokenHeroSword (swap result) | Turtle Shell x30 + Cannonball x30 | Fine tier-wise; noted because it makes Mothron optional |
| 2030, 2385, 2836 | LihzahrdBookcase / Piano / Sink | Starry Royal Brick Wall x30 [7], Solar Brick x30 [7], Vortex Brick x9 [7] | Reverse: lunar decor (TT8 materials) for temple furniture |
| 1151-1154 | Lihzahrd statues / pressure plate | trivial tier-0 junk (Confetti x2, Ale + Copper Ore...) | follows from A (Lihzahrd furniture TT0) |
| 1371, 4784, 4936, 4949 | Golem Trophy, EoL Mask, Fishron Master Trophy, EoL Master Trophy | Martian Conduit Wall x30 / Beetle Husk / Solar Tablet Fragment | boss trophies made craftable from Golem-era materials: decor, acceptable |

Unsure: Turtle armor tier (5 vs 6), MothronWings / TheEyeOfCthulhu (6 likely), ChlorophyteDye (2883, TT3, source none:
not checked).

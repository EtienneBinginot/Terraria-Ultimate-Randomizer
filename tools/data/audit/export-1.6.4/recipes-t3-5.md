# Audit recipes-t3-5
Summary: 513 recipes (items of tier 3 to 5: hardmode bars/armor/tools/wings/emblems, mech summons, hallowed and chlorophyte gear,
souls, master trophies, food, dyes). The tier layering is sound: no recipe is doable 2+ tiers before its result's true tier
(joined with items-ref), no station recipe needs itself, the hardmode bar/anvil/forge/crystal-ball chain is acyclic and fully d4,
the three mechanical summons are d4, hardmode pickaxes/drills are d4/d5 and need no mined hardmode ore (bars are crafted from
non-ore items), Bomb (the universal filler, 88 rows here) is buyable at the Painter and dropped by Gem Bunny.
Main issues: modded "Upgrade" intermediates used 17-30 times in vanilla recipes (absurd raw cost), and a never-obtainable critter
(Black Dragonfly) still used as an ingredient. 2 defects, 4 questions.

## Defects (most severe first)
### D1. Modded MagicStorage "Upgrade" items demanded 17-30 times in vanilla recipes — severity: absurd
| idx | result | ingredient | raw cost of the Upgrade units |
|---|---|---|---|
| 2799 | LavaWaders (recipe 1 of 5) | UpgradeCrimtane x30 | each = 2 ShadowDiamond + 30 RottenChunk + 30 AccentSlab + 30 Hook => 60 ShadowDiamond, 900 hooks... |
| 2421 | ChlorophyteDrill | UpgradeHallowed x30 | each = 8 HallowedBar + 30 SoulofLight + 30 Bomb => 240 bars, 900 souls, 900 bombs |
| 2430 | ChlorophytePartisan | UpgradeHallowed x28 | 224 bars, 840 souls, 840 bombs |
| 2816 | MoonShell | UpgradeHallowed x18 | 144 bars, 540 souls, 540 bombs |
| 2395 | HallowedMask | UpgradeHallowed x17 | 136 bars, 510 souls, 510 bombs |
| 2411 | Gungnir | UpgradeHallowed x7 (+5 ChlorophyteBar) | 56 bars, 210 souls |
Same pattern in other chunks (not exhaustive): BeetleShell UpgradeHallowed x10, DemonBow / BloodButcherer UpgradeCrimtane x7 / x10,
BeetleLeggings UpgradeBlueChlorophyte x5 (each unit = 5 BeetleHusk + 30 SpiderFang + 6 MythrilBar + 30 Bomb).
Why: the planner values an Upgrade at its flat coin value (UpgradeHallowed v20000, same as one HallowedBar), but crafting one costs
8 HallowedBar + 30 SoulofLight + 30 Bomb. A mech boss drops 15-30 HallowedBar, so ChlorophyteDrill (planned value 600000 vs vanilla
810000, looks fine on paper) really needs the bars of ~10 mech-boss kills. UpgradeHallowed has findability "never" (items-ref 5598),
so it can only be crafted.
Suspected cause: RecipePlanner budgets counts by coin value and does not expand a crafted intermediate's own recipe (modded
Upgrade* items are classified Material, tier 4/5). Suggested fix: cap the count of a crafting-only (findability never) ingredient
at ~3-5, or price crafted intermediates by recursive raw cost, or keep modded crafting-only "Upgrade*" items out of vanilla recipes.

### D2. Black Dragonfly (never obtainable) is still planned as an ingredient — severity: inconsistent
| idx | result | ingredient | note |
|---|---|---|---|
| 104 | GoldenDelight | BlackDragonfly x30 | other GoldenDelight recipes work |
| 105 | GoldenDelight | BlackDragonfly x30 | idem |
| 2620 | GoldGoldfishBowl | BlackDragonfly x30 | only recipe in this chunk |
| 2622 | GoldDragonflyJar | BlackDragonfly x30 | only recipe in this chunk |
| 1959 | YoyoBag (recipe 3 of 5) | BlackDragonfly x30 | other YoyoBag recipes work |
| 2388 | SpiritFlame | BlackDragonfly x6 | only recipe in this chunk; the weapon is still findable at tier 4 by drops |
Why: BlackDragonfly (id 4334, critter) has findability and recipe availability "never" (items-ref, findability-odd.tsv class
unobtainable), yet it is planned x6-x30 with availability `never`. These recipes can never be crafted. Low impact where
alternatives exist, but 2620 / 2622 / 2388 have no working recipe here.
Suspected cause: the ingredient picker accepts an availability-never item (critters assumed catchable?) and the dead recipe is kept.
Suggested fix: reject availability-never ingredients when picking, or give the critter a source (OrangeDragonfly is a3).

## Questions
Q1. Adamantite Forge (2359) needs 3 EaterofWorldsTrophy (+14 PalladiumBar). The trophy is only found as a 10 % King Slime drop
 (drops-t0-1), so about 30 summoned King Slime kills for a station used by non-critical recipes in this chunk. Intended "small quest" size?
Q2. GiantHarpyFeather (CobaltPickaxe 2299, TitaniumDrill x6 2366, TitaniumChainsaw x3...) is crafted (3414) from 10 DarkShard + 14
 LightShard; a LightShard is 30 PineTreeBlock + 29 AmethystGemsparkBlock (3123), DarkShard = 1 LightShard. One feather = 24 shards =
 ~1400 blocks. Mitigated: shards are sold (Painter/Dryad/Demolitionist/Clothier, 4.5-9 silver in shops-t4) and Queen Slime minions drop the
 feather at 100 %. The crafting route is unusable in practice; confirm shop/drop is the intended path.
Q3. MagicConch (4263) and DemonConch (4819) have findability never, so ShellphoneDummy (2740, not randomized, doable never) cannot
 be made: no Shellphone family in this world. Expected?
Q4. TitaniumBar (2909) is one recipe with 8 ingredient types (StrangeBrew, GrapeJuice, Bomb x7, Ichor, HellstoneBar, PixieDust,
 EmptyDropper x3, 30 WaterfallBlock) and gates CrystalBall (5 bars) -> the only AdamantiteBar recipe -> MythrilBar and MythrilAnvil.
 Bars are also findable at tier 4 so no softlock, but crafting is very heavy. (Vanilla value 1200000 in the export belongs to the
 swapped music-box slot, so the 0.1 value ratio is not a defect.)

## Fine / checked
- No row doable 2+ tiers below its result's true tier (join with items-ref). Rows with doable > item tier are late dyes, master
 trophies, Frost Core / Forbidden Fragment items and Venom flask/arrow (true tier 6): coherent.
- Spectre / Lightning / Frostspark Boots at tier 3: vanilla is Hermes or Flurry + Rocket Boots, a pre-Hardmode accessory
 (https://terraria.wiki.gg/wiki/Spectre_Boots), so d3 is right. TerrasparkBoots d4 (found at 5) is within the allowance.
- Mechanical Eye / Worm / Skull and Mechdusa Summon: d4, ingredients (Ichor, CobaltBar, FlaskofGold, OrichalcumBar, PixieDust) are tier 4
 and available; Mechdusa left as vanilla (3 summons).
- Hardmode bars chain (Palladium <- Bomb, Cobalt <- Palladium + Ichor, Orichalcum <- Cobalt, Adamantite <- CrystalBall, Mythril <-
 Adamantite, Titanium <- 8 items, Chlorophyte <- 8 CursedFlame; CursedFlame dropped by Herpling / Illuminant Slime at 100 %): acyclic,
 d4/d5. MythrilAnvil (AdamantiteBar x9), OrichalcumAnvil, TitaniumForge all d4.
- Pickaxes and drills (Cobalt, Palladium, Mythril, Orichalcum, Adamantite, Titanium, PickaxeAxe, Drax, Chlorophyte): d4 or d5, none
 needs mined hardmode ore.
- Ammo stacks 100-200, ingredient counts <= 30, at most 8 ingredient types. Planned/vanilla value ratios within 0.2-2.5 except
 harmless 1-for-1 swaps (Bone, Pearlwood, Ichor <- BlueJellyfish, StrangePlant).
- Bomb (findability 3, vanilla tier 0): Painter sells it (shops-t0), GemBunnyTopaz drops 23-29 at 35 %, recipe from StrangeBrew;
 Inactive/ActiveStoneBlock x30 = 60 bombs, affordable.
- Duplicated recipes (YoyoBag x5, LavaWaders x5, AvengerEmblem x4, GoldenDelight x13) are one per original slot, intended; off:twin rows
 are disabled twins.

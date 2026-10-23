# Audit 01: pre-hardmode ores, bars and what is made from them

Source: `ItemTiers.tsv` (this world: Copper / Lead / Tungsten / Gold, Crimson items found by loot too).
Reminder: "planned recipe tier" is `plan.Availability` (the earliest tier the item is available, loot included), not the
recipe alone. Default `TierAllowance` = 1, so `planned = true - 1` is by design (`ItemPool.UnlockTier`), not a bug.
Wiki references: terraria.wiki.gg pages named in each row.

Most of the family is fine: all copper..platinum ores, bars, tools, weapons, armor, watches, bricks, chandeliers are
true 0; Demonite/Crimtane ore and bars, Demon Bow, Light's Bane, War Axe, Blood Butcherer, Tendon Bow, Blood Lust
Cluster are 1; Shadow armor, Nightmare Pickaxe, The Breaker, Meteorite (wiki Meteorite: lands after the first EoW/BoC
kill), Meteorite Bar, Meteor armor, Space Gun, Star Cannon, Meteor Shot, Obsidian armor (Hellforge + Shadow Scale/Tissue,
wiki Obsidian armor) are 2; Phoenix Blaster (Handgun from the dungeon) and the Shadow Chest loot are 3.

## A. Mis-tiered items

### A1. Tissue Sample at 1 instead of 2 (and everything made from it)
| id | internal name | true tier | correct | reason |
|---|---|---|---|---|
| 1329 | TissueSample | 1 | 2 | Only from Brain of Cthulhu's Creepers (wiki Creeper: Tissue Sample 2-5, Crimtane Ore 5-12); Shadow Scale, its twin, is 2 |
| 792 | CrimsonHelmet | 1 | 2 | 10 Tissue Samples (Shadow Helmet, its twin, is 2) |
| 793 | CrimsonScalemail | 1 | 2 | 20 Tissue Samples |
| 794 | CrimsonGreaves | 1 | 2 | 15 Tissue Samples |
| 798 | DeathbringerPickaxe | 1 | 2 | 12 Crimtane Bars + 6 Tissue Samples (Nightmare Pickaxe is 2); pick 70 at true 1 also makes Hellstone (65) look minable at 1 |
| 801 | TheMeatball | 1 | 2 | 10 Crimtane Bars + 5 Tissue Samples (wiki The Meatball) |
| 797 | FleshGrinder | 1 | 2 | 10 Crimtane Bars + 5 Tissue Samples (wiki Flesh Grinder); The Breaker, its twin, is 2 |

(Blood Lust Cluster 799 = 10 Crimtane Bars like War Axe of the Night (wiki): 1 is right.)

### A2. Obsidian at 2 instead of 0
| id | internal name | true tier | correct | reason |
|---|---|---|---|---|
| 173 | Obsidian | 2 | 0 | Water + lava anywhere, needs pick power 55 = Gold/Platinum Pickaxe (wiki Obsidian). KnownTiers says PostEvilBoss |
| 193 | ObsidianSkull | 2 | 0 | 20 Obsidian at a Furnace |
| 192 / 330 | ObsidianBrick / ObsidianBrickWall | 2 | 0 | Obsidian at a Furnace |
| 1457 / 4507 | ObsidianPlatform / ObsidianBackEcho | 2 | 0 | Obsidian (brick) |
| 396 | ObsidianHorseshoe | 2 | 1 | Lucky Horseshoe (sky island chest, true 1) + Obsidian Skull |
| 907 | ObsidianWaterWalkingBoots | 4 | 0 | Water Walking Boots (water chest, true 0) + Obsidian Skull at Tinkerer (wiki Lava Waders tree); kept at rarity 4, see C3 |
| 908 | LavaWaders | 4 | 2 | Pre-hardmode (wiki Lava Waders): OWWB + Lava Charm + Obsidian Rose; 2 if the underworld is 2 (A3), see C3 |
| 397 | ObsidianShield | 2 | 3 | Cobalt Shield (dungeon gold chest, Golden Key: post-Skeletron) + Obsidian Skull (wiki Obsidian Shield). Cause: Cobalt Shield true 2 from "bag: Golden Lock Box" (dungeon family, see C4) |

### A3. Hellstone at 3 instead of 2 (and its gear)
Hellstone needs pick power 65 = Nightmare / Deathbringer Pickaxe, both post evil boss (wiki Hellstone); nothing about it
waits for Skeletron. `KnownTiers` (ItemClassifier.cs:20-21) and `MaterialTiers.cs:197-198` both say 3.
| id | internal name | true tier | correct |
|---|---|---|---|
| 174 / 175 | Hellstone / HellstoneBar | 3 | 2 |
| 122 / 217 | MoltenPickaxe / MoltenHamaxe | 3 | 2 |
| 231 / 232 / 233 | MoltenHelmet / MoltenBreastplate / MoltenGreaves | 3 | 2 |
| 120 / 121 | MoltenFury / FieryGreatsword | 3 | 2 |
| 2365 | ImpStaff | 3 | 2 |
| 265 | HellfireArrow | 3 | 2 |
| 119 | Flamarang | 3 | 2 (10 Hellstone Bars + Enchanted Boomerang, wiki Flamarang; Enchanted Boomerang is 2) |
| 214 / 3067 | HellstoneBrick / HellstoneBrickWall | 3 | 2 |
| 2618 / 2840 / 4110 | ObsidianChest / ObsidianSink / ToiletObsidian | 3 | 2 (unsure: raised by recipes, most likely the Hellstone/Hellforge chain) |
Unsure, same decision (underworld drops at rarity 3, never lowered because sources only raise): ObsidianRose 1323
(Fire Imp, 5%, wiki Obsidian Rose), MagmaStone 1322 (Hellbat), ObsidianSkullRose 4004, MoltenSkullRose 4003,
MoltenCharm 4038: 3 -> 2. Phoenix Blaster (Handgun), Sunfury, Hellwing Bow, Flamelash, Dark Lance, Flower of Fire stay 3.

### A4. Small ones
| id | internal name | true tier | correct | reason |
|---|---|---|---|---|
| 264 | GoldCrown | 1 | 0 | 5 Gold Bars + 1 Ruby at an anvil (wiki Gold Crown); rarity 1 never lowered |
| 715 | PlatinumCrown | 1 | 0 | Platinum twin |
| 743 | RubyStaff | 1 | 0 | Gold Bars + Rubies (unsure on exact counts), all tier 0 |
| 744 | DiamondStaff | 1 | 0 | Platinum Bars + Diamonds |
| 329 | ShadowKey | 2 | 3 | Golden Lock Box is a dungeon item (post-Skeletron), see C4 |
| 64, 96, 162, 115 / 800, 1256, 3062, 1290, 802 | Vilethorn, Musket, BallOHurt, ShadowOrb / TheUndertaker, CrimsonRod, CrimsonHeart, PanicNecklace, TheRottedFork | 2 | 1 (unsure) | Orbs/hearts break with any hammer or explosives (wiki Shadow Orb); the orb is not a source (tile), only the evil crate (2) is. 2 is defensible, 1 matches common play |
| 56 / 880 | DemoniteOre / CrimtaneOre | 1 | 1 (unsure 0) | Generated in the evil biome, pick 55 = Gold Pickaxe (wiki Demonite Ore); 1 (EoC drop) is a fine conservative choice |

## B. Recipes doable too early
Strictly (availability below `true - allowance`): **none** in this family. Every `planned < true` row is either the
allowance (true - 1) or loot findability in this world (e.g. ShadowScalemail / NightmarePickaxe 1: found at 1 by loot,
their recipe still uses Shadow Scale [2]).
Real "too early" exposure comes from A1: Tissue Sample items have true 1, so `ResultTier` = 0 and the planner may give
Crimson armor, Deathbringer Pickaxe, Flesh Grinder, The Meatball tier-0 ingredients (this seed kept Tissue Sample in
them, another seed may not). Same with Deathbringer (pick 70) planned at 0 opening Hellstone early.
Thematic but within allowance: the 20 Meteorite furniture pieces (3100-3180, 4141) are true 2 / planned 1 with no
meteorite at all (Deathweed, Antlion Mandible...); Obsidian bricks planned 1 (Deathweed, Tuna) while they should be 0.

## C. Systemic causes
1. **Boss minions count as normal enemies for sources.** `ItemClassifier.NpcSourceTier` uses `BossParts.Owner`, which
   only knows parts; Creeper is in `BossParts.Minions`, so its drops (Tissue Sample, Crimtane Ore) get source tier 0
   ("drop: Suiveur"). The material table then copied that (`material-tiers.json` TissueSample `"t": 1`, note "keeps
   computed tier"). Fix: use `BossParts.Leader(type)` in NpcSourceTier (minions at their boss's tier) and set the
   table's TissueSample to 2. Fixes all of A1 via RaiseByRecipes.
2. **Hand tiers of the hell materials are one step off.** KnownTiers: Obsidian PostEvilBoss -> PreBoss (pick 55, Gold /
   Platinum Pickaxe); Hellstone / HellstoneBar PostSkeletron -> PostEvilBoss (pick 65, Nightmare / Deathbringer), same
   in MaterialTiers.cs 197-198. Rule: an ore's tier = the tier of the first pickaxe with its pick power (Gold/Platinum
   55 = 0, Nightmare/Deathbringer 65 = 2, Molten 100 = 2, Cobalt/Palladium drills = 4, Pickaxe Axe/drills 200 = 5).
   Cascades to A2 and A3.
3. **Chest-lowered items do not lower what is crafted from them.** `LowerByRecipes` runs on bucket tiers before
   `LowerByWorldChests`; Water Walking Boots (rarity 4, water chest -> true 0) and Lava Charm (rarity 3, true 1) keep
   their crafted results at 4 (Obsidian Water Walking Boots, Lava Waders). Fix: after `LowerByWorldChests`, run one more
   recipe-lowering pass on true tiers for crafted, non-table items (min over recipes of max(ingredient true tiers,
   station)). Also rarity-tier 1 items made only of tier-0 parts (Gold/Platinum Crown, Ruby/Diamond Staff) stay 1: check
   why LowerByRecipes did not lower them (unsure: gems or the anvil station may count as tier 1).
4. **Cross-family (for the dungeon auditor):** Golden Lock Box contents are source tier 2 (Cobalt Shield, Shadow Key,
   Muramasa, Handgun, Blue Moon, Valor): the box is dungeon loot, so 3. Minishark (Arms Dealer, source 0) stays true 2
   because sources only raise. Ebonstone / Crimstone blocks are true 0 with pick-to-mine 65.

## D. Absurd planned recipes
| id | item | planned ingredients | why |
|---|---|---|---|
| 175 | HellstoneBar | Ember Wall x30, Fledgling Wings x1, Wire x2, Obsidian Pants x1 | an accessory and an armor piece consumed per single bar (Molten set needs ~60 bars) |
| 3503 | TinPickaxe | Demon Bow x1, Dart x1 | a tier-1 weapon eaten by a starter pickaxe (Demon Bow is findable at 0 in this world, still odd) |
| 2750 | MeteorStaff | Mythril Bar x2, Yellow Wrench, Chum Caster, Wall of Flesh Mask, Pearlsand x30 | a rare vanity boss drop as an ingredient of a weapon |
| 120 / 121 / 2365 / 123 | MoltenFury / FieryGreatsword / ImpStaff / MeteorHelmet | Actuated Stone x30 / Wire x30 / Hellstone Brick Wall x30 / Hellstone Brick Wall x30 | single cheap filler for tier 2-3 gear (walls are 4 per block) |
| 907 | ObsidianWaterWalkingBoots | Pearlstone Brick x30 | hardmode block for a pre-hardmode accessory (consequence of C3) |

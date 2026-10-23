# 07 - Mining progression audit (pickaxes, drills, ores, pick-gated tiles)

Sources: ItemTiers.tsv (this world), `RecipePlanner.cs` (MiningLevels, PickToMine, OresFrom, MiningRequirements, TargetTier,
Rebuilder clean/real), `GuaranteedSources.cs` (Findability.BaseTiers, WorldMaterials), `ItemPool.UnlockTier`
(TierAllowance default 1), IL of `Player.GetPickaxeDamage` (tools/ildump.sh), terraria.wiki.gg (Pickaxe power, Reaver Shark).

Pick power thresholds in the code match the IL (Meteorite 50, Demonite/Crimtane 55 below the surface, Obsidian 55,
Ebonstone/Crimstone/Pearlstone/Hellstone 65, Hellforge 65 in the underworld only, dungeon bricks 100 below the surface
outside the middle 30%, Cobalt/Palladium 100, Mythril/Orichalcum 110, Adamantite/Titanium 150, Chlorophyte 200,
Lihzahrd brick/altar 210, Luminite: no check). Source: https://terraria.wiki.gg/wiki/Pickaxe_power + IL.

## How findability treats mining (answer to the question)

- `Findability.BaseTiers` adds every ore of the world at `ItemPool.TierOf(ore)` with **no pick-power check** (comment: "mining
  is not gated, and the mining levels use it"). Pick power is only enforced indirectly by `RecipePlanner.MiningRequirements`,
  which guarantees a pickaxe of each level **no later than** the ore's tier. Nothing prevents a pickaxe from being planned
  **earlier** than its level, and the extra mining it allows is not counted anywhere.
- Ores and bars also come from drop/bag plans, counted at `UnlockTier(TrueTier)` = TrueTier - TierAllowance (1), floored
  at 4 in hardmode. So Chlorophyte Ore (TT 5) and Chlorophyte Bar are "found" at 4, Hellstone (TT 3) at 2, Spectre/
  Shroomite bars (TT 6) at 5, Luminite Bar (TT 9) at 8. The TSV confirms these come from BaseTier (the "vanilla recipe
  tier" column is computed with `Findability.BaseTier`, which excludes guaranteed sources): Chlorophyte Ore van=4.
- `WorldMaterials` lists Ebonstone and Crimstone at tier 0 although both need pick 65 (IL: unconditional check).
- Mining ores themselves are `Tainted` (never new ingredients), but a **bar** that is found through a drop has
  `clean = real` and becomes a normal new-ingredient candidate at its early tier.

The level check itself is fine: every ore's Tier >= the tier of the cheapest vanilla pickaxe able to mine it
(50: Tungsten/Gold t0 <= Meteorite 2; 55: Gold t0 <= Demonite/Crimtane 1, Obsidian 2; 65: Nightmare/Deathbringer t2 <=
Hellstone 3; 100/110/150: Molten t2-3, Cobalt t4, Mythril t4 <= 4; 200: Drax/Pickaxe Axe/Chlorophyte t5 <= 5;
210: Picksaw t7 vs Lihzahrd 6, but the temple needs Plantera's key anyway). The exceptions are Ebonstone/Crimstone (C2).

## A. Mis-tiered items

| id | internal name | tier (true) | correct | reason |
|---|---|---|---|---|
| 579 | Drax | 4 (5) | 5 | Hallowed Bars + Souls of Fright/Might/Sight: needs all 3 mechs (wiki: Drax). Tier column is the rarity tier |
| 990 | PickaxeAxe | 4 (5) | 5 | same recipe as Drax |
| 1294 | Picksaw | 6 (7) | 7 | Golem drop: post-Golem (boss drops = post-boss tier, like Pwnhammer 4) |
| 103 | NightmarePickaxe | 1 (2) | 2 | Demonite Bars + Shadow Scales (Eater of Worlds) |
| 798 | DeathbringerPickaxe | 1 (1) | 2 | Crimtane Bars + Tissue Samples (Brain of Cthulhu); inconsistent with Nightmare (TT 2) |
| 1329 | TissueSample | 1 (1) | 2 | dropped by BoC/Creepers ("drop: Suiveur", source tier 0): the Creeper is not mapped to its boss's tier, Shadow Scale is (2) |
| 174 / 175 / 122 | Hellstone / HellstoneBar / MoltenPickaxe | 3 (3) | 2 (debatable) | Hellstone needs pick 65 = Nightmare/Deathbringer (post evil boss); no Skeletron gate in vanilla. KnownTiers sets 3 on purpose (conservative, harmless direction): keep if intended |
| 61 / 836 | EbonstoneBlock / CrimstoneBlock | 0 (0) | 2 | need pick 65 everywhere (IL `GetPickaxeDamage`: types 25/203, `pickPower < 65 -> 0`); WorldMaterials says 0 |
| 221 | Hellforge | 0 (0) | 2 | found only in the underworld, needs pick 65 there; `Stations` already says Progression tier 2 (item tier disagrees) |
| 2341 | ReaverShark | 3 (3) | 0 | caught by fishing in the Ocean, no other requirement (https://terraria.wiki.gg/wiki/Reaver_Shark); pick 59 |
| 1320 | BonePickaxe | 2 (2) | 0 | Undead Miner drop (caverns, pre-boss); source tier is already 0 |
| 4059 | FossilPickaxe | 1 (1) | 0 (unsure) | Sturdy Fossil from desert fossil, pre-boss |
| 1917 | CnadyCanePickaxe | 1 (4) | 0-1 (unsure) | Present (Christmas, pre-hardmode too); true tier 4 comes from the Present's source tier |
| 1146-1149 | SuperDartTrap, FlameTrap, SpikyBallTrap, SpearTrap | 1 (1) | 7 (6 at best) | only obtainable by mining the temple (pick 210: Picksaw = post-Golem). Unobtainable here (find never), low impact |
| 1292 | LihzahrdAltar | 0 (0) | 7 (unsure) | pick 210 (IL type 237); low impact |
| 134 / 137 / 139 | Blue/Green/PinkBrick | 0 (0) | 3 (debatable) | dungeon (post-Skeletron), pick 100 below the surface outside the middle 30%. Tainted, so harmless |
| 173 / 56 | Obsidian 2, DemoniteOre 1 | | 0 (keep) | vanilla: Gold/Platinum pick (55/59) at tier 0 mines both. Conservative tiers, harmless direction |

Source-tier column anomalies (not used for Tier since KnownTiers win, but wrong data): hardmode ores/bars source tier 1-2
from "Caisse de bois perlé"/"Caisse de mithril"/"Caisse souillée" (hardmode crates should be 4); LaserDrill source 0
("drop: Embrouilleur", Martian Madness: 7); CrimtaneOre/TissueSample source 0 (Creeper).

## B. Recipes doable too early (mining family)

| id | internal name | tier | planned | ingredients | cause |
|---|---|---|---|---|---|
| 1230 | ChlorophytePickaxe | 5 | 4 | Gold Starry Block x30 [4] | vanilla availability 4 (bars "found" at 4 by allowance) -> TargetTier 4 -> trivial tier-4 block. **The reported bug** |
| 1231 | ChlorophyteDrill | 5 | 4 | Crystal Block x30 [4] | same |
| 947 | ChlorophyteOre | 5 | found 4 | (drop/bag) | UnlockTier(5) = 4 with allowance 1; ore needs pick 200 (post-mech) |
| 1006 | ChlorophyteBar | 5 | 4 | Hallowed Seeds x28 [4] | ore found at 4 -> bar recipe target 4 -> rebuilt with no ore at all: chlorophyte bars reliably at 4 |
| 1195 | OrichalcumPickaxe (165) | 4 | 4 | Chlorophyte Bar x11 [4] | tier-4 pickaxe needing bars of an ore that needs pick 200 (bar clean at 4 because "found") |
| 1202 | TitaniumPickaxe (190) | 4 | 4 | Chlorophyte Bar x8 [4]; Living Rainbow Dye x4 | same |
| 525 / 1221 | MythrilAnvil / TitaniumForge | 4 | 4 | Chlorophyte Bar x6 / x1 | same (hardmode stations gated by a post-mech bar, outside the family but same cause) |
| 1506 | SpectrePickaxe | 6 | 5 | Tattered Bee Wing x6 [5] | Spectre Bar found at 5 (TT 6 - 1) -> vanilla avail 5 |
| 2176 | ShroomiteDiggingClaw | 6 | 5 | Shroomite Bar x17 [5] | Shroomite Bar found/planned at 5 |
| 3261 / 1552 | SpectreBar / ShroomiteBar | 6 | 5 | Spooky Wood x30, Titanium Bar, Ectoplasm / Turtle Shell, Beetle Husk, Iron Bar | allowance |
| 2774, 2776, 2779, 2781, 2784, 2786, 3464, 3466 | Vortex/Nebula/Solar/Stardust Drill+Pickaxe | 9 | 8 | fragments + Luminite Bar [8] or mercury/celestial bricks | Luminite Bar found at 8 (TT 9 - 1): luminite comes only from the Moon Lord |
| 3467 | LunarBar | 9 | 8 | Luminite x4 [9] | the bar's availability is its findability 8 (drop with allowance) |
| 1101 | LihzahrdBrick | 6 | 5 | Spooky Wood x3 [5] | allowance (Tainted, so not spread further) |
| 174 / 175 | Hellstone / HellstoneBar | 3 | 2 | bar: Ember Wall x30, Emerging Wings, Wire, Obsidian Pants | allowance; vanilla-wise 2 is right (see A) |

Spread of the early bars (planned recipe tier < bar tier, items using them as ingredient): Chlorophyte Bar in ~40
tier-4 recipes (Cobalt Mask, Mythril Chainmail, Adamantite Breastplate, Titanium armor/weapons, Mythril Anvil, Titanium
Forge, Super Mana Potion, Mechanical Wheel Piece...), Spectre/Shroomite bars in ~45 tier-5 recipes (Ghost Wings, True
Excalibur, Shroomite/Spectre armor...), Luminite Bar in ~30 tier-8 recipes (lunar armor, wings, celestial furniture).

## C. Systemic causes

**C1. The tier allowance leaks into recipe targets (root cause of the Chlorophyte Pickaxe bug).**
`Findability.BaseTiers` counts a drop/bag item at `UnlockTier(TrueTier)` (TT - 1 with the default allowance). The vanilla
availability used by `Rebuilder.TargetTier` (`cap = min(RecipeTier, recipeTiers[proxy])`) is computed from it, so any
recipe whose vanilla ingredients can drop one tier early is retargeted a tier early and then rebuilt from ordinary
ingredients of that tier: a lucky, gated drop becomes a reliable recipe. The rebuilt bar (Chlorophyte Bar from Hallowed
Seeds) is then "clean" (found at 4, not Tainted) and spreads as a new ingredient into dozens of tier-4 recipes.
Fix rule: compute the vanilla availability used for TargetTier (and the `clean` candidate tiers) with findability
without the allowance (drops/bags at their TrueTier, i.e. `UnlockTier` with allowance 0), or floor TargetTier at
`ItemPool.TierOf(original result)` whenever the lower cap only comes from allowance-gated sources. Ores and bars of a
mining level should never be new-ingredient candidates below the level's MiningTier.

**C2. Gathered materials ignore pick power outside MiningLevels.** Ores are fine (each ore's Tier >= cheapest vanilla
pick that mines it), but `WorldMaterials` lists Ebonstone/Crimstone at 0 (need 65). Rule: any WorldMaterials entry with
`PickToMine > 0` gets `max(tier, tier of the cheapest vanilla pickaxe with pick >= PickToMine)` (Ebonstone/Crimstone -> 2).
Low impact (they are Tainted), but their vanilla recipes (bricks, walls, furniture) are targeted at 0.

**C3. Mining levels only guard lateness, never earliness.** `MiningRequirements` guarantees a pickaxe of each level by the
ore's tier; nothing stops a pickaxe being planned before it (Chlorophyte Pickaxe/Drill at 4 lets the player mine the
jungle's natural chlorophyte at 4, which Findability does not count, so everything planned from chlorophyte at 5 is
really reachable at 4). Rule: the planned availability (TargetTier and keyed result tier) of any item with
`pick >= L.pick` must be >= `MiningTier(L)` for the highest level L it opens (200 -> 5; 65 -> Hellstone's tier); and a
pickaxe's new ingredients must not include ores/bars of a level at or above its own pick power (Orichalcum/Titanium
Pickaxe with Chlorophyte bars). Also (minor, unsure): `MiningRequirements.Found` marks only the ores of the level as
unmined, not their bars, so a pickaxe whose recipe uses a dropped bar of its own level counts as available.

**C4. Boss-part / event NPC source tiers.** Creeper ("Suiveur") gives source tier 0 (Tissue Sample stays TT 1, so
Deathbringer TT 1 while Nightmare is 2); hardmode crates give source tier 1-2; Brain Scrambler (Laser Drill) 0. The
source pass maps boss parts and event NPCs to tier 0. Unsure whether it matters beyond these items.

## D. Absurd planned recipes (family only)

| id | item | ingredients | why |
|---|---|---|---|
| 778 | AdamantitePickaxe | Mechanical Wagon Piece x9; Orange Paint x30 | 9 copies of a one-per-Destroyer-bag item (expert only) for a tier-4 pickaxe; MechanicalWagonPiece TT 5 |
| 1195 / 1202 | Orichalcum / Titanium Pickaxe | Chlorophyte Bar x11 / x8 | next mining level's bar in a weaker pickaxe (see B, C3) |
| 1230 / 1231 | Chlorophyte Pickaxe / Drill | Gold Starry Block x30 / Crystal Block x30 | the 200-power pick from a decorative block (see B) |
| 385 | CobaltDrill | Pixie Dust x30; Antlion Mandible x30; Titan Potion x30 | 30 potions for a drill (mild, unsure) |
| 3503 | TinPickaxe | Demon Bow x1; Dart x1 | starter pickaxe from an evil-biome weapon (mild) |
| 1506 | SpectrePickaxe | Tattered Bee Wing x6 | rare Moss Hornet drop x6 (mild, unsure) |

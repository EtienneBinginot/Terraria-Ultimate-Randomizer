# 05 - Hardmode biome and event materials, event drops (audit)

Data: ItemTiers.tsv (5455 rows). Reminder of how the columns work (read in `ItemPool.Build` / `ItemClassifier`):
`tier` = rarity tier (`ComputeTier`), lowered by vanilla recipes = the **bucket**; `true tier` = max(tier, source tier)
unless MaterialTiers table / world chest = the **gate** (`Allowed`, `ResultTier`). So a `true tier` too low means the item
can be rolled too early; a `true tier` too high only makes it late. Recipe planner target = `UnlockTier(true) =
max(true - TierAllowance(1), 4 if true >= 4)`, so `planned = true - 1` is by design and not reported.

Wiki pages used: [Solar Eclipse](https://terraria.wiki.gg/wiki/Solar_Eclipse) (needs 1 mech boss; Reaper all mechs;
Mothron, Butcher, Deadly Sphere, Dr. Man Fly, Nailhead, Psycho need Plantera; painting drops per enemy),
[Lamia](https://terraria.wiki.gg/wiki/Lamia) (hardmode, underground desert), [Solar Tablet](https://terraria.wiki.gg/wiki/Solar_Tablet)
(8 fragments at Mythril anvil, 20% in Lihzahrd chests; fragments from Lihzahrds/Flying Snakes),
[Biome Mimics](https://terraria.wiki.gg/wiki/Biome_Mimics) (hardmode only, no mech requirement),
[Dark Mage](https://terraria.wiki.gg/wiki/Dark_Mage) (Squire's Shield / Apprentice's Scarf / eggs drop in Tier 1),
[Pirate Map](https://terraria.wiki.gg/wiki/Pirate_Map) (drops only in hardmode, but usable pre-hardmode).

Checked and fine: Cursed Flame, Ichor, Crystal Shard, Pixie Dust, Unicorn Horn, Light/Dark Shard, Frost Core, Forbidden
Fragment, Ancient Cloth, Spider Fang, Souls of Light/Night/Flight (KnownTiers 4), mech souls, Ectoplasm (6), Spooky Wood
(6), Spooky Twig / Black Fairy Dust (true 6), Giant Harpy Feather / Ice Feather (true 4), Feather (0), Tattered Cloth (0),
Snow Globe (true 4), Pumpkin Moon Medallion / Naughty Present (true 6), biome chest weapons (true 6 from world chests),
Martian Madness drops (7), OOA Ogre / tier-2 sentries (5), tier-3 sentries and Betsy (7), Eclipse pre-Plantera drops
(Death Sickle, Neptune's Shell, Eye Spring, Moon Stone, Broken Bat Wing: 5), Mimic drops (4), key molds (excluded: removed
from 1.4, correct).

## A. Mis-tiered items

### A1. True tier too LOW (can be rolled before the event exists) - the important ones

| id | internal name | tier / true | correct | reason |
|---|---|---|---|---|
| 1315 | PirateMap | 0 / 0 | 4 | Drops only in hardmode (global rule, wiki Pirate Map) but usable pre-HM: rolled at tier 0 it starts a **pre-hardmode Pirate Invasion** (Cutlass, Coin Gun, Lucky Coin...). Not protected. Source "none" (see C1) |
| 2806-2808 | MartianUniformHelmet/Torso/Pants | 1 / 1 | 7 | Martian Officer, Martian Madness (post-Golem); rarity 0 |
| 2803-2805 | MartianCostumeMask/Shirt/Pants | 1 / 1 | 7 | Martian Madness enemy drop; rarity 0 |
| 2801, 2802 | MoonMask, SunMask | 1 / 1 | 4 | Dark / Light Lamia, hardmode underground desert (wiki Lamia) |
| 3784-3786 | LamiaPants/Shirt/Hat | 3 / 3 | 4 | same |
| 1277-1280 | SailorHat, EyePatch, SailorShirt, SailorPants | 2 / 2 | 4 | Pirate Invasion drops (hardmode map) |
| 3263-3265 | BuccaneerBandana/Shirt/Pants | 1 / 1 | 4 | Pirate drops |
| 1704 1705 1710 1716 1720 2133 2137 2143 2147 2151 2155 2238 2379 2389 2405 2663 2843 3885 3910 (3904 Platform: 0) | Golden furniture set (Chair..Workbench, Chest) | 1 / 1 | 4 | Pirate drops only |
| 4740-4742 | ButcherMask/Apron/Pants | 2 / 2 | 6 | Butcher needs Plantera (wiki Solar Eclipse) |
| 4738, 4739 | DrManFlyMask / LabCoat | 2 / 2 | 6 | Dr. Man Fly needs Plantera |
| 5227 | AMachineforTerrarians | 0 / 0 | 6 | painting, Butcher (post-Plantera) |
| 5237 | WingsofEvil | 0 / 0 | 6 | painting, Mothron (post-Plantera) |
| 5223 | Requiem | 0 / 0 | 5 | painting, Reaper (all mech bosses) |
| 5236, 5239 | OcularResonance, Eyezorhead | 0 / 0 | 5 | paintings, Eyezor (eclipse, 1 mech) |
| 5260 | ThisIsGettingOutOfHand | 0 / 0 | 5 | painting, The Possessed |
| 5261 | Buddies | 0 / 0 | 5 | painting, Frankenstein / Swamp Thing |
| 5262 | MidnightSun | 0 / 0 | 5 | painting, Fritz |
| 1172 | LizardEgg | 3 / 3 | 6 | Lihzahrd (temple, post-Plantera). Cross-family (temple) |
| 1293 | LihzahrdPowerCell | 1 / 1 | 6 | Lihzahrd / temple chests. Protected, so low impact (never rolled), but its Info is wrong. Cross-family |

All but the Pirate Map are vanity/furniture (low gameplay impact, but they are "event loot" showing up in tier 0-3
pools and in recipes planned at 0-2, e.g. MartianCostumeShirt planned from Antlion Mandible x30 [1]).

### A2. True tier too HIGH (late by one tier; rarity 8 = tier 7 floor although the source is post-Plantera)

| id | internal name | tier / true | correct | reason |
|---|---|---|---|---|
| 1782 1784 1801 1802 1826 4680 | CandyCornRifle, JackOLanternLauncher, BatScepter, RavenStaff, TheHorsemansBlade, ScytheWhip | 7 / 7 | 6 | Pumpking, Pumpkin Moon (Ectoplasm medallion, post-Plantera); source tier 6 already computed |
| 1835 1845 4444 | StakeLauncher, NecromanticScroll, WitchBroom | 7 / 7 | 6 | Mourning Wood, same |
| 1910 1929 1928 1930 1914 1931 1946 | ElfMelter, ChainGun, ChristmasTreeSword, Razorpine, ReindeerBells, BlizzardStaff, SnowmanCannon | 7 / 7 | 6 | Frost Moon bosses, post-Plantera |
| 3098 3105 3106 3107 3249 | ButchersChainsaw, ToxicFlask, PsychoKnife, NailGun, DeadlySphereStaff | 7 / 7 | 6 | post-Plantera eclipse enemies (wiki) |
| 2770 3292 | MothronWings, TheEyeOfCthulhu | 7 / 7 | 6 | Mothron, post-Plantera (since 1.4.1, wiki) |
| 1570 | BrokenHeroSword | 7 / 7 (table) | 6 | MaterialTiers says 7; Mothron needs Plantera, not Golem (wiki Solar Eclipse). Fix the table entry |
| 2766 | LunarTabletFragment | 7 / 7 (table) | 6 | Lihzahrds / Flying Snakes in the temple, post-Plantera (wiki Solar Tablet). Fix the table entry |
| 2767 | SolarTablet | 7 / 7 | 6 | recipe of the fragments (raised from the table's 7) and 20% in Lihzahrd chests |
| 905 855 854 3033 2584 | CoinGun, LuckyCoin, DiscountCard, GoldRing, PirateStaff | 5 / 5 | 4 | Pirate Invasion, any time in hardmode (rarity 5/6 floor) |
| 3052-3054 | ShadowFlameBow / HexDoll / Knife | 5 / 5 | 4 | Goblin Summoner, hardmode Goblin Army |
| 3809, 3810 | ApprenticeScarf, SquireShield | 5 / 5 | 2 | Dark Mage, OOA Tier 1 (Tavernkeep after evil boss); wiki Dark Mage. Balance-wise 2 may feel early: vanilla says 2, maybe keep 3-4 by design (unsure) |
| 3855, 3857 | DD2PetGato, DD2PetDragon | 3 / 3 | 2 | Dark Mage Tier 1 |
| 3816 3818 3824 3829 3832 3828 | DD2ElderCrystalStand, T1 sentries (Flameburst, Ballista, Lightning Aura, Explosive Trap), DD2ElderCrystal | 3 / 3 | 2 | Tavernkeep shop, he arrives after the evil boss (TownNpcTiers already says 2; rarity 3 wins) |
| 1533-1537, 4714 | Jungle/Corruption/Crimson/Hallowed/Frozen/Desert biome keys | 7 / 7 | 4 | Hardmode global drop (1/2500 in the biome); only the chests wait for Plantera. Protected (never rolled), so mostly cosmetic for Info/findability; source "none" (C1) |
| 1328 | TurtleShell | 6 (KnownTiers) / 4 (table) | 4 | Giant Tortoise, hardmode jungle. KnownTiers PostPlantera contradicts the table's 4: bucket 6, gate 4. Pick one (4) |

Note (unsure): 517 MagicDagger true 1 comes from the "gold chest" world chest list (`ItemClassifier` WorldChests, read
from WorldGen by the author). Wiki lists it as a Mimic drop; I did not re-verify the chest code. If it is not really in
1.4.4 gold chests, it should be 4.

## B. Recipes doable too early (beyond the 1-tier allowance)

| id | internal name | tier / true | planned | ingredients | cause |
|---|---|---|---|---|---|
| 3091 | NightKey | 0 / 4 | 0 | Fallen Star x9 [0] | Protected result: `KeepRecipe` (IsRandomizable false) makes `Touchable` false, so `Gated` false and `ResultTier` = 0; the ingredients are still rebuilt (`IngredientsTouchable`) with the recipe tier from `RecipeTier` = `ItemPool.TierOf` = rarity tier 0. Vanilla: 15 Souls of Night (HM) |
| 3092 | LightKey | 0 / 4 | 0 | Recall Potion x10 [0]; Fallen Star x6 [0] | same |

Impact is small (the keys only do something in hardmode), but the rule is wrong for every protected result whose rarity
tier is below its true tier (TeaKettle, plus other families: see C3).

Recipes following a wrong true tier from A1 (MartianCostume/Uniform at 1, eclipse paintings at 0, Lamia vanity at 3,
pirate vanity at 1-2) are "correct" for the planner but wrong because of A1.

## C. Systemic causes

C1. **Global loot rules are not snapshotted.** `MobLootNPC.ModifyNPCLoot` captures `npcLoot.Get(false)` (no global
drops), so items only dropped by global rules have no source: Pirate Map (true 0), the six biome keys (true 7 from
rarity), and the Souls of Light/Night only get a source from crates. Fix: also capture
`Main.ItemDropsDB` global rules once (e.g. `ItemDropDatabase._globalEntries` / `GetRulesForNPCID(id, true)` minus the
local ones) as a "any enemy" source at tier 0 + `DropConditionTier` (PirateMap, *KeyCondition, SoulOf* already map to
Hardmode). Then Pirate Map -> 4, keys -> 4.

C2. **Non-boss enemies count as tier 0 drop sources** (`NpcSourceTier`), so items of hardmode/event-only enemies whose
rules carry no condition get the rarity tier. With low rarity (vanity, furniture, paintings) the gate is far too early
(A1); with high rarity it is only late. Fix: a small `EventNpcTiers` map used by `NpcSourceTier` (or `NpcTiers`):
pirates (PirateDeckhand, Corsair, Crossbower, Deadeye, Captain, Parrot, Flying Dutchman) 4; Goblin Summoner 4; Lamias,
Ghouls, mummies of the evils/hallow, Ice Golem, Sand Elemental, biome Mimics, Wyvern 4; Eyezor, Frankenstein, Swamp Thing,
Vampire, Creature from the Deep, Fritz, The Possessed 5; Reaper 5; Mothron, Butcher, Deadly Sphere, Dr. Man Fly,
Nailhead, Psycho 6; Lihzahrd, Flying Snake 6; all Martian Madness NPCs 7; DD2 Dark Mage 2, Ogre 5, Betsy 7. A more generic
option: `npc.SpawnedFromStatue`-free check of `Main.hardMode`-only spawns is not available at load, so a table is the
realistic fix.

C3. **Rarity is a floor even when a vanilla source is earlier.** `trueTier = max(tier, trueTiers)` where `tier` is the
rarity tier lowered only by recipes; sources can only raise. Rarity 8 (Yellow) is given to all post-Plantera event loot
(Pumpkin/Frost Moon, post-Plantera eclipse, biome chest weapons), so they gate at 7 instead of 6 (A2); OOA Tier 1 shop
items rarity 3 gate at 3 instead of 2; pirate/goblin HM drops rarity 5 gate at 5 instead of 4. World chests already have
a lowering pass (`LowerByWorldChests`); the same lowering from a boss drop / moon-event drop / shop source with a known
tier (source tier >= 4 and from a boss table or explicit condition) would fix A2. Keep it lower-only for those reliable
sources.

C4. **Protected recipe results are planned ungated with the rarity tier** (B): `RecipePlanner.ResultTier` returns 0 when
`!Gated(i)`, and `Gated` needs `Touchable`, which is false for every protected (non randomizable) result. Ingredients of
protected recipes are still rebuilt, targeting `RecipeTier` = rarity tier. Fix: for protected results, target
`max(RecipeTier, UnlockTier(TrueTier))` (or compute ResultTier from `IngredientsTouchable` instead of `Touchable`).

C5. (cross-family, fishing) Hallowed Crate 3207, Divine Crate 3986, Vile 3982 / Hematic 3983 crates have true tier 2
(rarity 2, no source) but are hardmode only (Hallow, or the Hard variants). They are why Crystal Shard / Soul of Light /
Soul of Night show "source tier 2"; KnownTiers saves those three, but anything else only found in those crates inherits 2.

C6. MaterialTiers table entries off by one: BrokenHeroSword 7 -> 6, LunarTabletFragment 7 -> 6 (A2). TurtleShell:
KnownTiers PostPlantera vs table 4 - remove the KnownTiers entry or align it.

## D. Absurd planned recipes (clear cases only)

| id | item | planned ingredients | why |
|---|---|---|---|
| 3091 / 3092 | NightKey / LightKey | Fallen Stars (+ Recall Potions) | pre-boss junk for a hardmode key (B) |
| 2767 | SolarTablet | Turtle Shell x1 [6] | a whole event summon for one material (vanilla 8 fragments); mild |
| 2861 | MartianConduitWall | Turtle Shell x1 [6] | a wall from a rare hardmode material (wall usually x4 per block); mild, reverse direction |
| 520 | SoulofLight | Explosive Powder x1; Green Team Block x7 | a key hardmode material from trivial blocks; doable at 4 so not a gating issue, just cheap |
| 2806-2808, 2803-2805 | Martian vanity | Lava Moss, tin/iron bars, Antlion Mandibles, fish | consequence of A1 (true 1) |

No other clear absurdities in this family.

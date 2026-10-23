# 06 - Pre-hardmode materials, pre-HM boss items, dungeon, pre-HM accessory chains, boss summons

Data: ItemTiers.tsv. "tier" = bucket tier (rarity, lowered by recipes); "true tier" (tt) = the tier the rolls and
recipe gating use (`ItemPool.TrueTierOf`). A wrong bucket tier with a correct true tier is only mentioned when it matters.
Wiki pages cited as terraria.wiki.gg/wiki/<Page>.

Most of the family is fine: Gel, Lens, Black Lens, Feather, Stinger, Vine, Jungle Spores, Hive, Cobweb, Silk, Antlion
Mandible, Shark Fin, Worm Tooth, Rotten Chunk, Vertebrae, Shadow Scale (tt2), gems (table 0), Large gems, Fallen Star
(tt0, table), mushrooms, herbs, Bottled Water and the pre-HM potions, Bone (known 3), Golden Key (tt3), King Slime / EoC /
EoW / BoC / Skeletron / WoF loot (Pwnhammer, emblems, Demon Heart: 4), Suspicious Looking Eye, Worm Food, Bloody Spine,
Abeemination, Guide Voodoo Doll, Goblin Battle Standard, Snow Globe (tt4), mech summons (tt4), medallions (6),
Celestial Sigil (8).

## A. Mis-tiered items (true tier wrong)

### A1. Too low (items available too early: the real risk)

| id | internal name | tt now | correct | reason |
|---|---|---|---|---|
| 1293 | LihzahrdPowerCell | 1 | 6 | Drops from Lihzahrd / Flying Snake (Jungle Temple only) and Lihzahrd chests, Golem needs Plantera (wiki: Lihzahrd_Power_Cell). Cause: C1 (non-boss NPC source = tier 0) |
| 1315 | PirateMap | 0 | 4 | Hardmode ocean enemy drop (global rule with `Conditions.PirateMap`): no source captured (st -1), keeps rarity 0. Cause: C6 |
| 1307 | ClothierVoodooDoll | 1 | 3 | Angry Bones / Dark Caster / Librarian Skeleton (Dungeon) 1/300 (wiki: Clothier_Voodoo_Doll). Cause: C1 |
| 1329 | TissueSample | 1 | 2 | Creeper (BoC minion) / BoC drop. MaterialTiers says 1 ("keeps computed tier") because the computed tier was wrong (C2). Shadow Scale, its twin, is 2 |
| 880 | CrimtaneOre | 1 | 1 (ok) / unsure | Also "drop: Suiveur" (Creeper) at st0, same cause C2; tier 1 is still right (mined with a 55 pick or from BoC) |
| 2673 | TruffleWorm | 3 | 4 | Only spawns in hardmode underground mushroom biomes. No source (st -1). Cause: C6 |
| 4961 | EmpressButterfly | 3 | 6 | Prismatic Lacewing: Hallow at night after Plantera. No source. Cause: C6 |
| 1172 | LizardEgg | 3 | 6 | Lihzahrd drop (temple). Cause: C1 (raised only by its rarity 3) |
| 2767 | SolarTablet | 7 | 6 | 8 Solar Tablet Fragments at a Mythril anvil; fragments from Lihzahrds / Flying Snakes / Lihzahrd chests, post-Plantera (wiki: Solar_Tablet_Fragment). Cause: MaterialTiers has LunarTabletFragment = 7, should be 6 (this one is too HIGH, listed here with its pair) |
| 165 | WaterBolt | 2 | 3 (unsure) | Dungeon shelves (deep) and Water Bolt Mimics; wiki: "can be obtained before Skeletron, but very dangerous" (wiki: Water_Bolt). Normal progression: 3. No source captured (st -1) |
| 113, 155, 156, 157, 163, 164 | MagicMissile, Muramasa, CobaltShield, AquaScepter, BlueMoon, Handgun | 2 | 3 | Locked gold dungeon chests or Golden Lock Box: both need a Golden Key (dungeon enemies, post-Skeletron). Cause: C4 |
| 329 | ShadowKey | 2 | 3 | Golden Lock Box / locked dungeon chest only. Cause: C4 |
| 3085 | LockBox (Golden Lock Box) | 2 | 3 | From Dungeon Crate; 1.4.4 fixed fishing dungeon items before Skeletron (wiki: Alchemy_Table) and it needs a Golden Key to open (wiki: Golden_Lock_Box) |
| 3205 | DungeonFishingCrate | 2 | 3 | Same 1.4.4 fix: no dungeon fishing loot before Skeletron (unsure for the crate itself; the wiki states it for dungeon items) |
| 5010 | TreasureMagnet | 2 | 3 | Shadow chest / Obsidian Lock Box: needs a Shadow Key (post-Skeletron). Cause: C4 (the other shadow chest items are 3 only thanks to rarity 3) |
| 4879 | ObsidianLockbox | 2 | 2 (ok) | Fishable in lava pre-HM; but its contents need a Shadow Key -> C4 |
| 3095 | TallyCounter | 2 | 3 | Angry Bones / dungeon drop. Cause: C1 |
| 932, 959 | BoneWand, AncientNecroHelmet | 2 | 3 | Angry Bones (dungeon) drops. Cause: C1 |
| 3000 | AlchemyTable | 2 | 3 | Dungeon only, "post-Skeletron crafting station" (wiki: Alchemy_Table). No source captured |

### A2. Too high (items hidden later than vanilla: less fun, not unsafe)

| id | internal name | tt now | correct | reason |
|---|---|---|---|---|
| 2430 | SlimySaddle | 7 | 1 | King Slime drop; rarity 8 decides. Cause: C3 |
| 2502 | HoneyedGoggles | 7 | 3 (QB tier) | Queen Bee drop; rarity 8. C3 |
| 1299 | Binoculars | 4 | 1 | EoC drop 1/40 (wiki: Binoculars, "the only one obtainable pre-Hardmode"). C3 |
| 2585 | SlimeHook | 3 | 1 | King Slime drop. C3 |
| 994, 3060 | EatersBone, BoneRattle | 3 | 2 | EoW / BoC pets. C3 |
| 3097, 3090, 2112, 2493, 256 | EoCShield, RoyalGel, EyeMask, KingSlimeMask, NinjaHood | 2 | 1 | KS/EoC loot; rarity 1 + value >= 1 gold gives +1 (ComputeTier). C3 |
| 3043 | MagicLantern | 3 | 0 | Skeleton Merchant, full moon (wiki: Magic_Lantern), pre-boss caverns. C3 |
| 4271 | BloodMoonStarter (Bloody Tear) | 2 | 0 | Bride/Groom 20% any time, Blood Moon enemies (wiki: Bloody_Tear). C3 |
| 216, 393, 18, 1303, 268 | Shackle, Compass, DepthMeter, JellyfishNecklace, DivingHelmet | 2 | 0 | Common pre-boss enemy drops (Zombie, cave enemies, jellyfish, shark). C3 |
| 1322 | MagmaStone | 3 | 0-1 | Hellbat / Lava bat drop in the Underworld, pre-HM. C3 |
| 128, 407 | RocketBoots, Toolbelt | 3 | 1 | Goblin Tinkerer shop (moves in at tier 1 per TownNpcTiers). C3 |
| 2204 | HoneyDispenser | 5 | 1 | Ivy chests 25% (wiki: Honey_Dispenser); missing from `WorldChests` "ivy chest", so only the Steampunker counts |
| 5114 | AbigailsFlower | 3 | 0 | Grows near tombstones "regardless of world progression" (wiki: Abigail's_Flower). No source captured (C6) |
| 4988 | QueenSlimeCrystal | 5 | 4 | Gelatin Crystal: Underground Hallow crystal shards, any hardmode (wiki: Gelatin_Crystal). No source (C6) |
| 3380 | FossilOre (Sturdy Fossil) | 1 | 0 | Tomb Crawler 50%, Extractinator on Desert Fossil (wiki: Sturdy_Fossil). Small |
| 560 | SlimeCrown | 1 | 0 | 20 Gel + Gold/Platinum Crown at a demon altar (wiki: Slime_Crown); crowns are tt1 because Ruby/Diamond were tier 1 before the material table set them to 0 (C5) |
| 264, 715 | GoldCrown, PlatinumCrown | 1 | 0 | Bars + Ruby / Diamond. C5 |
| 173 | Obsidian | 2 (known) | 0 | Pick power 55 since 1.4.3.3, Gold/Platinum pickaxe (wiki: Obsidian). Raises Obsidian Skull (tt2 -> 0), Obsidian Skin Potion (tt1 -> 0) and the obsidian accessory chain. Ore family overlap |
| 174 | Hellstone | 3 (known) | 2 (unsure) | Needs pick 65 (Nightmare / Deathbringer pickaxe, after the evil boss). Raises Flask of Fire, Obsidian Skull-based chains. Ore family overlap |
| 3096, 3037, 3120, 2373, 2374, 2375, 2423, 3225, 3118, 3099, 3119 | Sextant, WeatherRadio, FishermansGuide, HighTestFishingLine, AnglerEarring, TackleBox, FrogLeg, BalloonPufferfish, LifeformAnalyzer, Stopwatch, DPSMeter | 2 | 0 | Angler rewards, fishing catches, Traveling Merchant: no source captured (C6), tier from rarity 1 + value |
| 5070 | FlinxFur | 1 (table) | 0 (unsure) | Snow Flinx, underground snow, pre-boss. Table "keeps computed tier" |

Policy note (unsure): Queen Bee and Deerclops are PostSkeletron (3) in `NpcTiers.BossTiers`. Both are optional pre-boss
fights usually done before Skeletron; Bee Wax is 2 in the table while its only source (QB) is 3. Consider 2 for both.

## B. Recipes doable too early

The recurring pattern "planned recipe tier = true tier - 1" (Bee Wax 2 -> p1, Titan / Calming / Inferno / Love potion
-> p1-p2, Flask of Fire / Party / Poison 3 -> p2, Flask of Venom / Nanites 6 -> p5, Bee vanity, Skeletron Mask,
Deerclops vanity, Honey Balloon, Stinger / Sweetheart Necklace 3 -> p2, Super Healing Potion 8 -> p7) is by design:
`RecipePlanner.ResultTier` = `ItemPool.UnlockTier(TrueTier)` = true tier - TierAllowance (floor Hardmode). Not a bug;
it only hurts where the true tier itself is wrong (section A). Real cases:

| id | internal name | tt | planned | ingredients | likely cause |
|---|---|---|---|---|---|
| 3601 | CelestialSigil | 8 | 6 | Solar/Vortex/Nebula/Stardust Fragment x12 [8] | 2 below the true tier, more than the allowance; the shown recipe cannot be doable at 6. Probably a second live recipe or the fragments findable at 6 in this world through randomized loot (unsure) |
| 2192 | BoneWelder | 3 | 0 | (empty) | Planned recipe with no ingredient listed at tier 0, findability 0. Probably the guaranteed-station path (thematic station placed on common sources) rather than a recipe; check the dump (unsure) |
| 1293 | LihzahrdPowerCell | 1 | (findable 6) | - | No early recipe, but any roll gated at tt1 can hand Power Cells out pre-boss (A1) |
| 165 etc. | dungeon chest items | 2 | 2-3 | - | Rolls at tier 2 can give Muramasa / Cobalt Shield / Handgun / Magic Missile one tier early (A1, C4) |

## C. Systemic causes

C1. **Non-boss enemies are always a tier-0 source.** `ItemClassifier.NpcSourceTier` returns 0 for anything that is not
a boss (except the Dungeon Guardian). Drops of dungeon enemies (post-Skeletron), Jungle Temple enemies (post-Plantera)
and hardmode-only enemies (Mimics, Pixies, Skeleton Archers, Bones Lee...) never raise the true tier: an item with a low
rarity keeps a tiny tier (Lihzahrd Power Cell 1, Clothier Voodoo Doll 1, Tally Counter / Bone Wand / Necro Helmet 2,
Lizard Egg 3; outside the family: Magic Dagger tt1 from Mimics, Coal / Stink Potion from Jungle Mimic). Rule: give each
non-boss NPC a spawn tier: Bestiary biome tags (TheDungeon -> 3, the Temple -> 6), a hardmode-only list (or
`NpcTiers.BaseTier` stats tier >= 4 as a floor), events by their gate.

C2. **Creeper is a minion, not a part.** `NpcSourceTier` uses `BossParts.Owner`, but the Creeper is in `Minions`, so its
drops (Tissue Sample, Crimtane Ore) count as a tier-0 enemy drop. Fix: use `BossParts.Leader` there; set TissueSample to 2
in MaterialTiers (both the .cs and tools/data/material-tiers.json), like Shadow Scale.

C3. **A source never lowers the rarity tier.** `TrueTier = max(tier, trueTiers)` and `RaiseBySources` only raises; only
the material table and `WorldChests` can lower. Boss loot with a high rarity (Slimy Saddle 7, Honeyed Goggles 7,
Binoculars 4, Slime Hook 3, pets 3) and early loot with rarity 1 + value >= 1 gold (+1 in `ComputeTier`) sit 1 to 6 tiers
late. Rule: when an item's earliest source is a reliable fixed source (boss drop / boss bag at its boss tier, shop with a
known arrival, common enemy drop), lower the true tier to that source tier (it can stay in its bucket).

C4. **Lock boxes ignore their key.** A non-boss bag source is offered at `max(trueTiers[bag], condition)`; the Golden
Lock Box needs a Golden Key (tt3) and the Obsidian Lock Box a Shadow Key (tt3). `WorldChests` gives these items tier 3
("locked dungeon chest", "shadow chest") but it only lowers, so the bag source at 2 wins. Rule: bag tier of LockBox =
max(bag, GoldenKey), ObsidianLockbox = max(bag, ShadowKey); and Dungeon Crate >= 3 (1.4.4: no dungeon fishing loot before
Skeletron).

C5. **Material table applied after recipes and sources.** Results of recipes are computed from the pre-table tiers:
Ruby / Diamond are 1 before the table sets them to 0, so Gold / Platinum Crown and Slime Crown stay at 1. Rule: apply the
table (as fixed values) before `RaiseByRecipes` / `RaiseBySources`, or re-run the recipe raise from the table values.

C6. **Sources not captured** (st -1 "none"): global loot rules (Pirate Map), critters / bait (Truffle Worm, Prismatic
Lacewing), world tiles (Water Bolt books, Alchemy Table, Gelatin Crystal, Abigail's Flower, Honey Block from liquids),
fishing and Angler rewards, Traveling Merchant. They keep their rarity tier, which is badly wrong when the rarity is low
(Pirate Map 0) or high (Queen Slime Crystal 5). Rule: KnownTiers entries for the summon/critter/tile cases (PirateMap 4,
TruffleWorm 4, EmpressButterfly 6, QueenSlimeCrystal 4, LihzahrdPowerCell 6, WaterBolt 3, AlchemyTable 3,
AbigailsFlower 0, HoneyBlock 0), and read the global drop rules (`ItemDropDatabase` global entries) as sources.

C7. `WorldChests` "ivy chest" misses the Honey Dispenser (25%); Honey Dispenser stays at 5 (Steampunker).

## D. Absurd planned recipes (clear cases only)

| id | internal name | tt | planned ingredients | why absurd |
|---|---|---|---|---|
| 857 | SandstorminaBottle | 1 | Soul of Night x30 [4]; Celestial Cuffs x1 [4] | Pyramid-chest accessory made of hardmode souls and a hardmode accessory (another recipe makes p2; this one is useless) |
| 987 | BlizzardinaBottle | 1 | Chlorophyte Bar [4]; hook [4]; Amphibian Boots [2] | Ice-chest accessory from Chlorophyte |
| 2349 | WrathPotion | 1 | Shrimp; caged Seahorse; Soul of Fright x1 [5] | Mech-boss soul in a tier-1 potion (planned p1 comes from its other recipe) |
| 1129 | HiveWand | 3 | Chlorophyte Brick Wall x30 [4] | Queen Bee item from a hardmode wall |
| 932 | BoneWand | 2 | Cobalt Brick x30; Adamantite Bar [4]; Topaz Wall | Dungeon builder wand made of Adamantite |
| 4478 | LuckPotion | 2 | Actuator x30, Julia Butterfly x3, pet guide, Red Wrench, Vine x29, Titan Potion x30 | 30 Titan Potions for one Luck Potion: very costly for a minor buff |
| 2 | DirtBlock (outside the family, noticed) | 0 | Large Diamond x1; Hive x3 | Large gem spent on one Dirt Block |

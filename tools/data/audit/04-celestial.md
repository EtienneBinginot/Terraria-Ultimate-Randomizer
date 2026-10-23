# 04 - Celestial era (Lunatic Cultist, fragments, Luminite, Moon Lord, Zenith chain)

Data: `ItemTiers.tsv` (seed of the last ItemTierDump run), default settings (TierAllowance = 1, so
`UnlockTier(trueTier) = max(4, trueTier - 1)` and `RecipePlanner.ResultTier` = that unlock tier).

How to read it: with allowance 1, a true tier 9 item that is found or craftable at 8 is **by design** (drop gating and
recipe result tier both use `UnlockTier`). I only flag things below `UnlockTier(trueTier)` or wrong true tiers. A
script check over every non-excluded item with true tier >= 7 found **only 2 items** whose availability is below their
unlock tier (see B). They match the client.log line "2 item(s) placed below their tier".

## A. Mis-tiered items (true tier wrong vs vanilla)

| id | internal name | current T / tt | correct | reason |
|---|---|---|---|---|
| 3549 | LunarCraftingStation (Ancient Manipulator) | 9 / 9 | **8** | Dropped by the Lunatic Cultist (source tier 8 in the dump, and `Stations` already says Tier 8). Rarity 9 sets bucket 9, and true tier is never below the bucket. Needs a KnownTier 8. |
| 5401 | LunarRustBrick | 8 / 8 | **9** | Only from shimmering Luminite Brick (Luminite = Moon Lord). wiki: [Luminite Brick](https://terraria.wiki.gg/wiki/Luminite_Brick) |
| 5402 | DarkCelestialBrick | 8 / 8 | **9** | same (shimmer of Luminite Brick) |
| 5403 | AstraBrick | 8 / 8 | **9** | same |
| 5404 | CosmicEmberBrick | 8 / 8 | **9** | same |
| 5409 | LunarRustBrickWall | 8 / 8 (table 8) | **9** | made from its brick; MaterialTiers entry is 8 |
| 5410 | DarkCelestialBrickWall | 8 / 8 | **9** | made from its brick |
| 5411 | AstraBrickWall | 8 / 8 (table 8) | **9** | MaterialTiers entry is 8 |
| 5412 | CosmicEmberBrickWall | 8 / 8 (table 8) | **9** | MaterialTiers entry is 8 |
| 5413 | CryocoreBrickWall | 8 / 8 (table 8) | **9** | the brick is 9 in the table but its wall is 8 |
| 5414 | MercuryBrickWall | 8 / 8 (table 8) | **9** | same |
| 5415 | StarRoyaleBrickWall | 8 / 8 (table 8) | **9** | same |
| 5416 | HeavenforgeBrickWall | 8 / 8 | **9** | same |
| 4951 | TeleportationPylonVictory (Universal Pylon) | 8 / 8, source "shop: Zoologist" st0 | **9** | Sold only with a 100% Bestiary, which needs Moon Lord. wiki: [Pylons](https://terraria.wiki.gg/wiki/Pylons). The shop condition is not parsed (source tier 0). |
| 757 | TerraBlade | 7 / 7 | **6** | True Night's Edge + True Excalibur + Broken Hero Sword. The latest gate is Broken Hero Sword (Mothron, post-Plantera). wiki: [Terra Blade](https://terraria.wiki.gg/wiki/Terra_Blade) |
| 1570 | BrokenHeroSword | 7 / 7 (table 7, source Mothron st5) | **6** | Mothron spawns only after Plantera (since 1.4.1). wiki: [Mothron](https://terraria.wiki.gg/wiki/Mothron). Fix the MaterialTiers entry (7 -> 6). Terra Blade follows. |
| 1826 | TheHorsemansBlade | 7 / 7 (source Pumpking st6) | **6** | Pumpkin Moon = medallion after Plantera; rarity 8 holds it at 7 |
| 2766 | LunarTabletFragment (adjacent family) | 7 / 7 (table 7) | 6 (unsure) | Lihzahrd enemies in the temple, open after Plantera (temple key); Golem is not needed |
| 989 | EnchantedSword | 2 / 2 (gathered) | 0 (minor, unsure) | Sword shrines exist from world generation, reachable before any boss; rarity sets 2 |
| 3331 | CultistBossBag | 7 / 7, source none, **fd 8** | **exclude** | Unobtainable in vanilla: "The Lunatic Cultist ... ha[s] sprites for a Treasure Bag, even though [it does] not drop one" ([Treasure Bag](https://terraria.wiki.gg/wiki/Treasure_Bag)). It is still in the pool and is placed as loot at tier 8 in this world. Its vanilla contents are only a mask and gold. |

Checked and correct: the 4 fragments (8), LunarOre (9), LunarBar (9), LunarBrick (9), CelestialSigil (8), lunar
armor, wings, pickaxes, drills and hamaxes (9), the 4 fragment weapons, LunarHook, the fragment blocks and celestial
furniture (8), SuperHealingPotion (8), the 4 lunar dyes (8), VoidDye (9, needs 5 Luminite Bars:
[Void Dye](https://terraria.wiki.gg/wiki/Void_Dye)), VoidMonolith, MoonlordBullet/Arrow, DrillContainmentUnit,
PortalGun, GravityGlobe, LongRainbowTrailWings, the Moon Lord weapons (9), Zenith (9), Meowmere/StarWrath (9),
InfluxWaver (7, Martian Madness), Seedler (6), Excalibur / True Excalibur / True Night's Edge (5), CopperShortsword (0).

## B. Recipes or sources usable too early (below `UnlockTier(trueTier)`)

| id | internal name | tt (unlock) | availability | how | cause |
|---|---|---|---|---|---|
| 3549 | LunarCraftingStation | 9 (8), true value 8 (7) | **fd 6** | guaranteed source (progression station, `Stations` tier 8) | `GuaranteedSources.Assign`: "the item goes a tier lower only when its tier has no biome at all". Tiers 7 and 8 have no non-event natural enemies (Martian and pillar enemies are event enemies), so the station falls to a **post-Plantera** host. |
| 3601 | CelestialSigil | 8 (7) | **fd 6** (pl 6 from that) | guaranteed summon (`SummonTier` = MoonLord base tier - 1 = 8) | Same fallback. A Moon Lord summon on post-Plantera enemies skips Golem, Cultist and the pillars. Its recipe itself (4x12 fragments) is fine. |

Everything else with true tier >= 7 is at or above its unlock tier. The "planned recipe tier 8 < tier 9" cases are
allowance 1 working as designed: all lunar armor, tools, wings and Zenith; Luminite Bar is findable at 8.
Examples: VortexHelmet = Luminite Brick x30 + Vortex Fragment x12 [8], NebulaLeggings = Luminite Bar x9 [8],
Zenith fd 8.

## C. Systemic causes

1. **Tiers 7-8 have no guaranteed-source hosts.** Any requirement of tier 7 or 8 drops to tier 6 hosts. Today that is
   the Ancient Manipulator and the Celestial Sigil. Rule: below Hardmode the fallback is fine, but for post-Golem and
   post-Cultist requirements use boss hosts. Golem / Lunatic Cultist (the vanilla source of the Manipulator) or the
   four Celestial Pillars count as tier 8 hosts. Otherwise keep these two protected on their vanilla source and never
   place them below `UnlockTier(tier)`. Also consider a test asserting `placed below their tier == 0` for tiers >= 4.
2. **True tier is never below the rarity bucket** (`trueTier = max(tier, trueTiers)` unless from the table or a world
   chest). High-rarity items whose real source is earlier stay too high: LunarCraftingStation (rarity 9, Cultist),
   TheHorsemansBlade (rarity 8, Pumpkin Moon), TerraBlade (via BrokenHeroSword). Rule: when `RaiseBySources` finds a
   precise boss or event source tier, allow it to lower the true tier below the bucket (the bucket itself can stay, as
   the code wants). Otherwise add KnownTier overrides for these items.
3. **Shimmer-only items have no source.** The 8 celestial bricks (LunarRust, DarkCelestial, Astra, CosmicEmber,
   Cryocore, Mercury, StarRoyale, Heavenforge) come only from shimmering Luminite Brick. Their walls are crafted from
   them, and each wall crafts back into its brick. The dump shows `source none` and `vanilla recipe tier never` for
   them. Half of them got tier 8 from rarity, and MaterialTiers lists 6 of the walls at 8. In this world the
   brick/wall cycle is entered from fragment tier: HeavenforgeBrickWall <- Vortex Fragment Block x2 [8], then
   Heavenforge Brick <- its wall [8], Mercury Brick Wall <- Heavenforge Brick, Mercury Brick <- its wall, and so on.
   The whole family is available at 8. Rule: a shimmer transmutation is a source at the input's tier (Luminite Brick
   -> 9), and every brick and wall of this family gets true tier 9 (fix the 6 table entries and add the 4 bricks and
   2 walls). With allowance 1 they would still be usable at 8, but no longer as fragment-tier filler.
4. **Shop conditions not parsed for the Zoologist's bestiary items.** Universal Pylon shows source tier 0. Kite
   Crawltipede is fine at 8. Other bestiary-percentage items probably have the same issue (out of my family).
5. Minor: `PickToMine(LunarOre)` = 0. Placed Luminite needs pick power 225. Harmless while Luminite is never a world
   ore. Unsure whether OreMap can ever map an ore to it.

Note on obtainability (not a tier issue, unsure): several family items have findability `never` and no recipe:
Meowmere, Terrarian, SDMG, Seedler, TheHorsemansBlade, Starfury, EnchantedSword, MoonLordBossBag,
LongRainbowTrailWings, MoonLordPetItem. The Zenith recipe is rebuilt, so the Zenith does not need them. Chests,
shrines or drops below MinFindableChance may still give them.

## D. Absurd planned recipes

| id | item | planned recipe | why it is odd |
|---|---|---|---|
| 2758, 2759, 2765, 2774, 2776, 3524, 3464 ... | Lunar armor and tools (VortexBreastplate, VortexLeggings, SolarFlareLeggings, VortexDrill, VortexPickaxe, LunarHamaxeNebula, StardustDrill ...) | key = a celestial brick **wall** x30 (Cryocore / Mercury / Cosmic Ember / Lunar Rust Brick Wall) + fragments | 1 Luminite = 5 bricks = 20 walls, so "wall x30" is about 1.5 Luminite against 8 to 16 Luminite Bars (32 to 64 ore) in vanilla. Decorative derivatives of a key material make cheap keys. Suggest excluding walls (and maybe bricks) from key-ingredient choice, or setting them to Commonness "Common, never key". |
| 4956 | Zenith | key **0x33's Aviators** (dev vanity, [9] only through the Shrimpy Truffle chain) + Starlight, Arkhalis, Illegal Gun Parts x6 [3], Green Wrench [3], Sergeant United Shield, Honey Absorbent Sponge, Nebula Brick x30 | The ultimate weapon is keyed by a vanity item and padded with tier 3 junk. The design is respected (it gets 2 swords), but an endgame material key (Luminite Bar or fragments) would read better. Mild. |
| 3576 | LunarBlockStardust | Luminite Bar x1 | Endgame bar for a decorative block. Minor, it is tier 8 anyway. |
| 5402 | DarkCelestialBrick | Heavenforge Brick x5 | Fine in itself; listed only as part of the C.3 loop. |

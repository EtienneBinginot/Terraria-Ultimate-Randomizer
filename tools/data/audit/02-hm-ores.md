# Audit 02: hardmode ores, bars, souls, mech gear and their products

Scope: Cobalt/Palladium, Mythril/Orichalcum, Adamantite/Titanium (ores, bars, tools, weapons, armor, bricks/walls/beams/columns),
Hallowed bars and gear, the six souls, mechanical boss drops (bags, trophies, masks, minecart pieces, pets), mech summons,
crafted wings, soul weapons, Mythril/Orichalcum Anvil and Adamantite/Titanium Forge.

Overall the family is in good shape: every HM ore/bar/soul is hand-coded (KnownTiers, `tab=True kn=True`), the stations are
Progression stations at tier 4 (Stations.cs), every ore has the right `pick to mine` (100/110/150, Chlorophyte 200), and the
Hallowed gear, Pickaxe Axe/Drax, Excalibur, Gungnir, Light Disc, Megashark, Flamethrower, Optic Staff, Rainbow Rod, Avenger
Emblem, Mechanical Glove and True Night's Edge/True Excalibur are all at true tier 5. No family item is findable more than one tier
early.

Reading notes. The `[n]` after an ingredient is that ingredient's availability in this world (min of findability and
recipe), not its tier. With the default `TierAllowance = 1`, `ItemPool.UnlockTier` makes a tier-5 item droppable from tier 4, so
`find = tier - 1` and `planned recipe tier = tier - 1` are expected and not reported one by one (see C3).

## A. Mis-tiered items

### A1. Wrong true tier (affects caps, recipes and findability)

| id | internal name | tier / true tier | correct | reason |
|---|---|---|---|---|
| 786 | BoneWings | 5 / 5 | **6** | Soul of Flight x20 + Bone Feather. Bone Feather drops from Blue/Rusty/Hell Armored Bones only after Plantera (wiki: Bone_Feather). The table already has BoneFeather at 6, but the wings didn't inherit it (C1). |
| 822 | FrozenWings | 5 / 5 | **4** | Soul of Flight x20 + Ice Feather. Ice Feather drops from Ice Golem in Hardmode blizzards (wiki: Ice_Feather). The table has IceFeather at 4; the wings kept their rarity tier 5 (C1). |
| 785 | HarpyWings | 5 / 5 | **4** | Soul of Flight x20 + Giant Harpy Feather. The feather drops from Harpies, 1/150 (wiki: Giant_Harpy_Feather). The table has it at 4; the wings kept rarity 5 (C1). |
| 1518 | FireFeather (wing material, table) | 5 / **4** | **5** | Dropped only by Red Devil, in Hardmode after any mechanical boss (wiki: Fire_Feather). The table value 4 is wrong. Flame Wings (821) are at 5 by luck. |
| 1521 | TatteredBeeWing (wing material, table) | 5 / **4** | **5** | Moss Hornet drops it only after one mechanical boss (wiki: Tattered_Bee_Wing). The table value 4 is wrong. Bee Wings (1515) are at 5 by luck. |
| 5334 | MechdusaSummon (Ocram's Razor) | 3 / 3, source none | **4**, or exclude | Recipe: Mechanical Eye + Skull + Worm at a Mythril/Orichalcum Anvil, so it can't be below 4. It only works in For the Worthy + Remix worlds and says "It has no effect in this world" elsewhere (wiki: Ocram's_Razor). As a random drop or recipe result in a normal world it is a dud. Suggest tier 4 plus ban/exclusion outside those seeds. |
| 4803/4804/4805 | DestroyerPetItem / TwinsPetItem / SkeletronPrimePetItem | 7 / 7 (source tier 5) | 5? (unsure) | Master Mode mech boss drops. The relics/master trophies of the same bosses (4931-4933) are at 5, so these two Master-only groups are handled differently. Unsure: tier 7 for Master pets may be a deliberate convention. |

Checked and correct: Butterfly Wings 5 (Butterfly Dust: Moths after a mech boss), Bat Wings 5 (Broken Bat Wing: solar eclipse
vampires), Angel/Demon/Fairy Wings 4, Leaf/Spooky/Tattered Fairy/Festive Wings 6, True Night's Edge 5 (since 1.4.1 it takes
20 souls of each mech boss, no Broken Hero Sword; wiki: True_Night's_Edge), Destroyer Emblem 7 (Golem).

### A2. Bucket tier (`tier`) below true tier (bucket only; true tier is right)

These come from rarity. Rolls are capped by the true tier, so they mostly matter for bucket composition and for any code that
still reads `TierOf` (see B1).

| ids | items | tier / true tier |
|---|---|---|
| 524, 525, 1220, 1221 | Adamantite Forge, Mythril Anvil, Orichalcum Anvil, Titanium Forge | 3 / 4 (rarity 3). The station tier itself is correct (Stations.cs: 4). |
| 544, 556, 557 | Mechanical Eye / Worm / Skull | 3 / 4 |
| 579, 990, 578 | Drax, Pickaxe Axe, Hallowed Repeater | 4 / 5 |
| 415, 416, 420, 421, 604, 605, 1589, 1590 | Cobalt/Mythril Brick and walls, Adamantite Beam and wall, Palladium Column and wall | 0 / 4 |
| 1366-1369, 2106, 2107, 2113 | Mech boss trophies and masks | 2 / 5 |
| 3091, 3092 | Key of Night, Key of Light | 0 / 4 (see B1) |

### A3. Source tier mistakes (hidden by KnownTiers, so harmless here)
- SoulofFlight `st=0` ("drop: Wyvern"). Wyverns only spawn in Hardmode.
- Cobalt/Palladium Ore and Bar `st=1` (Pearlwood Crate), Mythril/Orichalcum/Adamantite/Titanium `st=2` (Mythril Crate, Corrupt
  Crate), Soul of Light/Night `st=2` (Hallowed/Corrupt Crate). Crates only give HM ores and souls in Hardmode.

  The hand-coded tier overrides all of these, but the same source logic sets the tier of unlisted items (C2).

## B. Recipes doable too early

| id | internal name | tier / true tier | planned recipe tier | planned ingredients | likely cause |
|---|---|---|---|---|---|
| 3092 | LightKey | 0 / 4 | **0** | Recall Potion x10 [0], Fallen Star x6 [0]. No key ingredient. | ResultTier should be U = UnlockTier(4) = 4, and LockResults should have added a tier-4 key, but `key` is empty. The keys are Protected (Protection.ProgressionMaterials) and their bucket tier is 0 (rarity 0, crafted by hand, so RecipeTier = TierOf = 0). The recipe was rebuilt at T=0 and the lock never applied. Unsure which branch skips it: check NeedsLock/Gated or the protected-item path for 3091/3092. |
| 3091 | NightKey | 0 / 4 | **0** | Fallen Star x9 [0]. No key ingredient. | Same as above. A pre-hardmode Key of Night/Light. Low gameplay impact (biome mimics only spawn in HM), but it breaks the "never before its tier" rule. |

No other family item has `planned recipe tier < true tier - 1` or a hardmode item doable before 4. Every remaining `pr = tt - 1`
case comes from the tier allowance (C3).

## C. Systemic causes

**C1. MaterialTiers corrections don't reach crafted products.** In `ItemPool` the order is: rarity tiers, `LowerByRecipes`,
`RaiseByRecipes`, `RaiseBySources`, and only then the MaterialTiers table. A product's tier is computed from its ingredients'
uncorrected rarity tiers. Examples:
- Bone Wings stay 5 although Bone Feather is 6 in the table.
- Frozen Wings and Harpy Wings stay 5 although Ice Feather and Giant Harpy Feather are 4.

Fix: apply the table (as fixed true tiers) before the recipe raise/lower passes, or re-run `RaiseByRecipes`/`LowerByRecipes`
after the table. A product would then be raised to its highest key ingredient's true tier and lowered when every ingredient is
lower. Scan every table material whose table value differs from its rarity tier, not just the wing materials.

**C2. NPC-drop source tier ignores Hardmode-only spawners and the Hardmode-only parts of crates.** Examples: Wyvern gives 0, the
Pearlwood/Mythril/Hallowed/Corrupt crates give 1-2 for HM ores and souls. Outside this family the same pattern shows on Pixie Dust,
Unicorn Horn, Dark Shard, Red Devil and Ice Golem materials, all `st=0`. Here KnownTiers/table hide it, but unlisted items that
depend on these sources get a too-early true tier.

Fix: an NPC's source tier should be at least Hardmode when the NPC only spawns in Hardmode (Wyvern, Pixie, Unicorn, mummies, Red
Devil, Ice Golem, Armored Bones...). Crate entries under a Hardmode condition (`Conditions.IsHardmode` in the crate rules) should
count from 4.

**C3. The allowance makes tier-5 items normal tier-4 ingredients (design, but worth knowing).** With `TierAllowance = 1`, a
tier-5 drop is findable at 4. The planner then uses it as a regular `[4]` ingredient and key. The clearest case is Chlorophyte:
- Chlorophyte Ore and Bar have true tier 5 (pick power 200, which needs a Pickaxe Axe/Drax, so tier 5 is right), but find = 4 in
  this world.
- About 49 tier-4 planned recipes use a chlorophyte item. In this family: Mythril Anvil (Chlorophyte Bar x6, its only planned
  ingredient), Orichalcum/Titanium Pickaxe, Titanium Waraxe, Titanium Mask, Mythril Sword/Chainmail, Palladium Pike/Leggings,
  Titanium Trident/Breastplate, Adamantite Breastplate, Cobalt Mask, Titanium Forge.
- Mechanical Eye uses Broken Bat Wing (true tier 5, solar eclipse). Mechanical Skull uses Marrow (true tier 5).

These recipes are only on time if that rare allowance drop happens. The pick-power chain itself is safe:
`RecipePlanner.MiningRequirements` guarantees a pickaxe per mining level.

Suggested rule: prefer key and new ingredients whose source-free tier (`TrueTier`) is at most the recipe tier. At minimum, never
make a true-tier-(T+1) item the only or key ingredient of a tier-T progression station (Mythril Anvil) or boss summon.

## D. Absurd planned recipes (clear cases only)

| id | item | planned ingredients | why it is odd |
|---|---|---|---|
| 525 | MythrilAnvil | Chlorophyte Bar x6 | The key hardmode station needs a post-mech bar normally mined with a 200-power pick. Reverse progression (see C3). The anvil is also findable at 4 in this world, so no softlock here. |
| 557 | MechanicalSkull | Princess Fish, Marrow, Platinum Bow, Adamantite Bar | Consumes a tier-5 weapon (Marrow) for a tier-4 boss summon. |
| 1184 | PalladiumBar | Adamantite Bar x1 | Reverse: the top-tier HM bar crafts the lowest one. Unsure, depends on the output stack. |
| 575 | SoulofFlight | Blue Solution x1, Iron Bar x1 | A cheap 1:1 soul (Blue Solution is bought from the Steampunker), so wings cost 20 solutions. Mild. Must-have simple recipe by design. |
| 495 / 3779 | RainbowRod / SpiritFlame | Flying Dutchman Relic x2 / Skeletron Prime Relic x2 | Relics are Master-Mode-only boss drops (unless randomized onto another source). Unsure: depends on this world's difficulty and drops. |
| 5334 | MechdusaSummon | Mechanical Eye + Skull + Worm (planned) | A consumable that does nothing in normal worlds (see A1). |

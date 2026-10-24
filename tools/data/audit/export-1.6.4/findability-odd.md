# Audit findability-odd (+ worldsources-chests)
Summary: no progression-critical item is unobtainable or softlocked in this world. The 515 "unobtainable" rows are
loot-only vanilla items (rare drops / chests / crates / fishing / critters), cut or unused items, vanity, furniture without
a vanilla recipe, boss bags and pets, banned mod internals. The 234 "far" rows are mostly boss trophies/masks (flat tier
label 2 or 7 versus the boss's real tier), crate loot and hardmode loot found 2 tiers after their label, nearly all with a
shop or a recipe at the right tier. The ore map is a coherent one-to-one permutation. Only minor inconsistencies (D1-D3)
and a few questions remain.

## Defects (most severe first)
### D1. Defender Medal only "found" at tier 9 (Betsy bag) — severity: bug (minor)
| id | item | tier | findability | source listed |
|---|---|---|---|---|
| 3817 | DefenderMedal | 0 | 9 | BossBagBetsy (bag) only |

Vanilla medals drop from every Old One's Army wave kill (DD2 medals are deliberately not touched, see CLAUDE.md), so they
are really obtainable from tier ~1-3 on (Eternia Crystal from the Tavernkeep). Findability does not count that source, so
anything judged by "findability tier" (hints, codex, guarantees) says tier 9. Not a softlock.
Suggested fix: manual findability source (event) for DefenderMedal, or leave it out of the "far" class.

### D2. Fireblossom herb table has a duplicate — severity: inconsistent (minor)
Herb table (HerbVariety 3): Fireblossom -> Bass; Bass; Glowstick. Also Actuator appears in three tables (Deathweed,
Shiverthorn, ViciousMushroom), Gel in two, VilePowder in two (Moonglow, VileMushroom).
The effective variety of the Fireblossom plant is 2 and Bass (a fish, tier 0) is twice as likely as the other one: indexes
are rolled independently in `WorldSources.HerbTable` / `HerbReplacement` (Request(RollKey.Herb, original, index)) with no
de-duplication. Repeats across different herbs are harmless; a repeat inside one herb defeats HerbVariety.
Suggested fix: re-roll an index whose result is already in that herb's table.

### D3. Tier labels far below the true tier create misleading "far" rows — severity: bug (export/classification, cosmetic)
| id | item | tier | true tier | findability |
|---|---|---|---|---|
| 1293 | LihzahrdPowerCell | 1 | 6 | 5 |
| 329 | ShadowKey | 1 | 3 | 3 |
| 1551 | Autohammer | 1 | 6 | 5 |
| 2195 | LihzahrdFurnace | 1 | 6 | 7 |
| 1339 | VialofVenom | 0 | 6 | 5 |
| 530 / 849 | Wire / Actuator | 0 | 3 | 2 |
| 221 | Hellforge | 0 | 2 | 2 |
| 1363-1371, 2105-2113, 3357... | boss trophies / masks | 2 | (boss tier) | 4-9 |

and about 20 more (pet items are labelled 7 but drop from tier 2-5 bosses; ChippysCouch t8 f3). Trophies, masks and pets
carry a flat tier (2 or 7) unrelated to their boss, so roughly 60 "far" rows are artefacts. The true tier is right in every
row I checked, so gating is not affected. Suggested fix: tier trophies/masks/pets from their boss (NpcTiers) in
`ItemClassifier`, and measure Findability gaps against the true tier.

## Questions (unsure, needs a decision)
Q1. Pot guaranteed items are EMPTY at every tier 0..9 in this export. `GuaranteedSources.PotItems` only keeps tool
requirements and tier-0 non-station requirements, which come from `RecipePlanner.MiningRequirements` (only when no pickaxe
is reachable in time) and `VanillaRequirements` (only in IngredientMode.Vanilla). So it may simply mean nothing is missing,
but CLAUDE.md says "tools and early materials also in pots": please confirm the ExtraRequirements providers
(RecipeFeature.cs:296-298) were registered at export time and that this is expected with default settings. Note that
progression tools (Pwnhammer, Shadow Key, Temple Key) and summons are `Station = true`, so they never go into pots; they
rely on enemy guardians (Shadow Key: AnomuraFungus, GiantWalkingAnt at tier 3; Lihzahrd Power Cell: EnchantedSword,
GiantFlyingFox at tier 5; Truffle Worm: HoppinJack, FungoFish, AnglerFish at tier 6), each a rare single drop.

Q2. Truffle Worm (Duke Fishron summon) lists HoppinJack among its hosts, a Halloween-only enemy. FungoFish and AnglerFish
make it reliable, but please check that guardians never rely only on seasonal or event enemies.

Q3. Station guardians arrive late versus their flat tier-1 label: BoneWelder f3, FleshCloningVaat f4, LesionStation f4,
SteampunkBoiler f5, BlendOMatic f5, LihzahrdFurnace f7. Their rebuilt recipes are available at the same tier and they
only gate furniture sets (recipe lists checked), so harmless; confirm intended. FleshCloningVaat and LesionStation recipes
are both rebuilt with Pearlwood and SkyBlueFlower, doable at 4, no cycle.

## Fine / checked
- Unobtainable rows by group (all harmless for progression):
  - Loot-only vanilla (chest, mimic, rare drop under 2 %): Cloud in a Bottle, Hermes Boots, Lucky Horseshoe, Shiny Red
    Balloon, Water Walking Boots, Magic Quiver, Starfury, Vilethorn, Musket, Dark Lance, Ice Blade, Katana, Cutlass, Slime
    Staff, Uzi, Staff of the Frost Hydra, Reaver Shark (a pickaxe, others exist), Ancient armor, Megaphone, Vitamins,
    Armor Polish, Compass, Sextant, DPS Meter, Ice Mirror (Magic Mirror and Cell Phone have planned recipes). They stay
    reachable through chest rolls and crates (not exported).
  - Fishing: quest fish are kept as catches (FishingFeature returns when itemDrop == attempt.questFish) and crates stay
    crates (CratesStayCrates), so fish/crates "never" are fine. Bait critters, butterflies, dragonflies are net catches.
  - Unused or cut content: Vortex/Nebula/Solar/Stardust axes, hammers, chainsaws and Nebula pickups, DD2 masks and
    javelin, Kobold backpack, Ogre/Dark Mage bags, Lihzahrd Altar and Work Bench, key molds, Color Only Dye.
  - Dungeon/Obsidian/Gothic/Golden furniture and Steampunk Cup: vanilla has no recipe (empty "shell" recipes, not planned).
  - Boss bags, pet items, music boxes, gem critters, golden critters, Shellphone family: drops or vanity.
  - ModLoader dev armor sets and MagicStorage Config_/Error_/Shimmer_/StorageCore*: flagged banned in items-ref.
- Summons: SuspiciousLookingEye 0, SlimeCrown 0, WormFood 1 (corruption world, BloodySpine skipped in SummonRequirements),
  Abeemination via recipe at 0, DeerThing 1, Mechanical Eye/Worm/Skull 4, Queen Slime crystal 4, Truffle Worm 6, Lihzahrd
  Power Cell 5, Celestial Sigil 8. Golden Key planned recipe 2. Clothier Voodoo Doll (never) is optional: Skeletron has
  other triggers (wiki: Clothier_Voodoo_Doll, 0.33 % dungeon drop).
- Pickaxes and hammers: Cobalt/Mythril/Adamantite/Titanium/Palladium 4; Nightmare/Deathbringer/Molten 2; Pwnhammer 4;
  Picksaw 7; no mining tool appears in "far" except a candy cane pickaxe and Platinum Hammer (recipe at 0).
- Ore map (OreMap, permutation inside a tier among the ores present in this world): tier 0: Copper tile gives Platinum ore,
  Silver tile gives Copper ore, Platinum tile gives Silver ore, Lead stays Lead; Iron, Gold, Tin, Tungsten absent, and their
  bars have planned recipes at 0. Consistent bijection over the 4 present ores. Meteorite and Hellstone swap (harmless, both
  tier 2, bars at 2, Hellforge at 2). Hardmode Cobalt, Mythril, Adamantite and Palladium, Orichalcum, Titanium rotate inside
  tier 4; both ores of each pair shown as present is by design (altars decide later). Demonite and Chlorophyte are alone in
  their tier and keep themselves.
- Herbs: HerbKeepChance 40 % keeps Daybloom, Moonglow, Blinkroot, Waterleaf, Shiverthorn and mushrooms findable at tier 0;
  Deathweed 1 and Fireblossom 2 match where they grow. Replacements are mostly tier-0 items, fish, gems, paints; the one
  hardmode item (Vial of Venom) is cut by MaxTier = HardmodeWall at runtime.
- Chest tier caps (tier 0..9 gives max 1,2,3,3,5,6,7,8,9,9): monotone, allowance 1 respected; clamps at 3 and 9.

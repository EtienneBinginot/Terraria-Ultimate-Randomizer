# Audit shops-t4
Summary: 897 slots at tier 4 (860 rolled, 37 unchanged = DD2 Bartender, paid in Defender Medals, normal). Rolled replacements
stay within the allowance (max `repl tier` 5 = tier 4 + 1). Prices follow the replaced item's own value, not the slot or the tier
(wanted randomness). Hardmode essentials (Purification Powder, Green/Purple Solution, Clentaminator, Jetpack, Rocket Boots...)
are not lost: they are re-added as guaranteed shop-only slots (`shops-added.tsv`). Main issues: teleportation pylons are lost,
a few added shop-only slots sell far too high-tier items, StyngerBolt tier mismatch, Red Solution has no guaranteed slot.
Prices are in copper (100 = 1 silver).

## Defects (most severe first)
### D1. Teleportation pylons (8 biome pylons) are practically unobtainable in this world — severity: softlock (QoL, not progression)
items-ref: findability `never`, planned recipe `never`, tier 2; none in shops-added at any tier. 169 vanilla pylon slots
at t4 are rolled to random items; only 9 became a pylon:
| item | tier | true | findability | appearances at t4 (random rolls) |
|---|---|---|---|---|
| TeleportationPylonPurity | 2 | 2 | never | 1 (Merchant 31) |
| TeleportationPylonUnderground | 2 | 2 | never | 4 (Dryad, PartyGirl, Painter, BestiaryGirl) |
| TeleportationPylonOcean | 2 | 2 | never | 3 (Painter, Stylist, SkeletonMerchant) |
| TeleportationPylonJungle | 2 | 2 | never | 1 (BestiaryGirl) |
| Snow / Desert / Hallow / Mushroom | 2 | 2 | never | 0 (also 0 at the t0 and t8 exports) |
The pylons are not part of the guaranteed shop-only list (unlike Solutions, Jetpack...), so whether a given pylon ever shows
up is pure luck, and four types never appear. Suspected cause: pylons are classified as ordinary Furniture without a
guarantee. Suggested fix: add the 8 pylons to the guaranteed shop-only list (one added slot each, tier 2+), or give them a
findability source.

### D2. Added (guaranteed shop-only) slots sell items 2-5 tiers above the stage — severity: skip
From shops-added.tsv (slot "from tier N" = first tier where the slot exists):
| NPC slot | item | item tier | true tier | slot from tier | price |
|---|---|---|---|---|---|
| Steampunker 48 | StaticHook | 9 | 5 | 4 | 697596 |
| PartyGirl 51 | PogoStick | 7 | 0 | 0 | 309283 |
| GoblinTinkerer 25 | RocketBoots | 3 | 1 | 0 | 78413 |
| GoblinTinkerer 26 | Toolbelt | 3 | 1 | 0 | 232366 |
| Truffle 17 | Hammush | 6 | 5 | 4 | 948544 |
| Truffle 18 | MushroomSpear | 6 | 5 | 4 | 816034 |
Static Hook (classified tier 9) is buyable from the first hardmode tier; Pogo Stick (tier 7) before any boss for 31 gold;
Hammush and Mushroom Spear (tier 6) from tier 4, beyond the allowance of 1. Rocket Boots and Toolbelt (tier 3) from tier 0
are early mobility, borderline. The "true tier" column is only the consequence of the added slot. Suspected cause: the added
slot starts at the NPC's own first tier, ignoring the item's tier. Fix: start the added slot at
`max(NPC tier, item tier - TierAllowance)`; but check the vanilla sale conditions first
(https://terraria.wiki.gg/wiki/Static_Hook, https://terraria.wiki.gg/wiki/Pogo_Stick), the classifier tier may be what is off.

### D3. StyngerBolt: tier 5, true tier 7, sold at tier 4 — severity: inconsistent
| slot | original | replacement | repl tier | true tier | price |
|---|---|---|---|---|---|
| Merchant 28 | Nail | StyngerBolt | 5 | 7 | 105 |
| ArmsDealer 13 | Nail | StyngerBolt | 5 | 7 | 126 |
True tier 7 is 3 above tier 4 although shop rolls should stay at tier + 1. The Nail slot is only shown with a Nail Gun in the
inventory, so the real impact is tiny, but the tier/true tier discrepancy (5 vs 7) suggests a classification bug (Nail's tier 7
may be inherited). Fix: check the tier of StyngerBolt in ItemClassifier, and cap rolls on true tier too.

### D4. Red Solution has no guaranteed source — severity: inconsistent
RedSolution: tier 3, true tier 5, findability `never`, no recipe, not in shops-added at any tier. It only exists as random
rolls (Merchant 27, BestiaryGirl 22 for 3180 and 3034 cu at t4... BestiaryGirl 0 slot is a fairy item). Green/Purple Solution
are re-added (Goblin Tinkerer 23/24 from tier 4), Sand/Snow from tier 8, but Red (vanilla Steampunker slot: Blood Moon
or eclipse + Crimson world) is not. Likely cause: the guarantee skips slots with a world condition. Fix: guarantee it
independently of the slot condition, or accept it if the world is Corruption (only useful for converting to Crimson).

## Questions
- Q1. Painter has the same item twice in one shop: SliceOfCake (24, 40), AdamantiteForge (27, 46), DD2ElderCrystalStand
  (22, 29). 83 slots, harmless; no duplicate in the other 23 NPCs. Dedupe per shop if desired.
- Q2. Ammo is priced at a flat 100 copper whatever its tier (CrystalBullet, IchorArrow, ChlorophyteBullet, HighVelocityBullet,
  CursedArrow, CrystalDart...), 14x the vanilla price of a Crystal Bullet (7 cu) but 1/50 of the replaced Cyborg slot
  (DryRocket 5000). Probably a deliberate floor; confirm.
- Q3. Cyborg 17: NightVisionHelmet -> MoonLordLegs classified "Armor" tier 2 (it is a vanity item in vanilla, so the category
  or tier may be off; https://terraria.wiki.gg/wiki/Moon_Lord%27s_Legs). Price 106587.
- Q4. SteampunkMinecart (tier 2, true tier 5) added on Steampunker from tier 4: expected?
- Q5. Prices are item-value-based: PinkPearl at Painter 1,223,548 cu (122 gold, tier 2), MagicStorage CombinedStations2Item
  1,461,026 on Golfer; the top-15 prices (110-226 gold, e.g. BestiaryGirl CelestialWand 2,260,260, Golfer GalaxyPearl
  1,784,728) are all cages/pets/mounts. Looks wanted; many are unaffordable at early hardmode.
- Q6. The t8 export has TeleportationPylonVictory 23 times (other chunk): intended?

## Fine / checked
- All 860 rolled slots: `repl tier` <= 5; only 41 rolls at tier 5, mostly vanity/mounts/trophies; no real progression skip
  (Hallowed Mask, Fire Gauntlet, Poison Staff, Shadowflame Knife, Dart Pistol are within allowance).
- Hardmode shop essentials: Purification Powder (Dryad slot 61, 187 cu), Green/Purple Solution (Goblin Tinkerer), Clentaminator
  and Jetpack (Steampunker), Rocket Boots/Toolbelt/Ruler (Goblin Tinkerer), Tiki armor, Leaf Wings, Hercules Beetle and Pygmy
  Necklace (Witch Doctor, shops-added), Cyborg and Wizard stock: all re-added elsewhere; nothing key found lost besides D1/D4.
- Cheap prices of high-tier items (HolyWater 323, SuperManaPotion 2870, solutions 1.7-3.4 k) come from the items' own values.
- DD2 Bartender (37 unchanged slots, medal prices 5-60): normal.

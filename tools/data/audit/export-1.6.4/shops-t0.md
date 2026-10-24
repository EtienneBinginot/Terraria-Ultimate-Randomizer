# Audit shops-t0
Summary: 897 rows (860 roll, 37 unchanged, 0 added slots in this chunk; the 43 tier-0 added slots are in shops-added.tsv).
The progression side is sound: replacements are tier 0-1 almost everywhere (530 t0/0, 186 t1/1; the few t2+ ones are dyes and
cosmetics), conditions are kept, no item is repeated inside a shop (NoDuplicatesInShop works), and every shop-only item with
no other source gets an added slot (checked against shops-added.tsv; the only ones without are the DD2 Bartender items,
kept unchanged, and RedSolution, impossible in this Corruption world). Main issue: items with value 0 (walls, blocks, ammo,
many furniture) use a 50 silver tier-0 fallback price, giving 0.5 to 1.25 gold for a rope, an acorn wall or a rocket.

## Defects (most severe first)
### D1. Value-0 items get the tier fallback price (50 silver x 1-2.5): cheap utility items cost about 1 gold - severity: absurd
Prices of rolled items cluster in exactly [5000, 12500] copper at tier 0 (and [10000, 25000] at tier 1, [20000, 50000] at
tier 2), which is `PriceRules.TierBasePrice[tier] * U(1, 2.5)`: the branch `StackRules.ValueOf(item) <= 0`. 222 of the 860
roll rows (26 %) fall in the fallback band: 79 Furniture, 39 Block, 36 Vanity, 22 Misc, 19 Material, 6 Ammo.
| npc#slot | original (vanilla price) | replacement | price |
|---|---|---|---|
| Merchant#13 | Rope (10) | StoneSlab (Block) | 7071 |
| Dryad#12 | Acorn (10) | SandstonePlatform | 9019 |
| Dryad#9 / #16 / #17 | GrassWall / FlowerWall / JungleWall (10) | RedStuccoWall / Rocks3Echo / BlueSlabWallUnsafe | 5367 / 9975 / 11139 |
| SkeletonMerchant#24 | Rope (10) | VineRope | 11615 |
| SkeletonMerchant#17 | BoneArrow (15) | WetRocket | 11429 |
| ArmsDealer#1 / #2 | SilverBullet (15) / TungstenBullet (18) | LavaRocket / DryRocket | 7991 / 9470 |
| ArmsDealer#3 | UnholyArrow (40) | PoisonDart | 10654 |
| Cyborg#0 | RocketI (50) | PoisonDart | 9585 |
| PartyGirl#31-33 | SillyStreamers (50) | IceBlock / WoodenSpike / SmoothSandstoneWall | 10201-12348 |
| Painter#1-19 | wallpapers (75) | GrayBrick, BoneBlock, CopperBrick... | 9198-10908 |
| ArmsDealer#9 | EmptyBullet (5) | Sapphire | 6452 |
and about 200 more (blocks, walls, ammo, cages, banners).
Why: a 10-copper Rope replaced by a Stone Slab at 70 silver, or a rocket / wall at 1 gold, is 700-1300x the vanilla price for
items players buy by the stack. The wanted randomness is the item, not an unaffordable price. I could not read `item.value`
(no tool in this audit); that Wet/Dry/Lava Rocket, Poison Dart and the walls have value 0 is inferred from the exact band
[5000, 12500]. Please confirm. Furniture rows in the band (Campfire, cages, paintings) may be the same cause (Question Q1).
Suspected cause: `PriceRules.Price` (Common/Core/PriceRules.cs): `value <= 0` falls back to `TierBasePrice[tier]` whatever
the category.
Fix: scale the fallback by category (Block/Ammo/Material/wall: a few silver, or the replaced slot's vanilla price x 1-2.5;
Furniture a few silver), keep the tier base only for gear classes.

### D2. Price comes only from the replacement: gear replacing a staple is very expensive, and the reverse - severity: absurd (minor)
Direct consequence of the value-based pricing, not a progression problem (all tier 0-1).
| npc#slot | original (vanilla) | replacement | price |
|---|---|---|---|
| Merchant#5 | CopperAxe (400) | TungstenPickaxe (Tool) | 12582 (31x) |
| Merchant#23 | Sickle (6000) | BladeofGrass | 27141 |
| GoblinTinkerer#1 | Ruler (1000) | GoldBroadsword | 19623 |
| GoblinTinkerer#5 | SpikyBall (80) | DemonBow | 22477 (281x) |
| Merchant#9 | LesserManaPotion (100) | Honeyfin | 10319 |
| Merchant#14 | Marshmallow (100) | RoastedBird | 12910 |
| TravellingMerchant#23 / #47 | Pho 7500 / PadThai 5500 | BowlofSoup / FroggleBunwich | 23864 / 23686 |
| DD2Bartender#0 | Ale (100) | FroggleBunwich | 10163 |
Acceptable for gear. Only a concern for food and potion staples that cost 100 copper in vanilla. Probably fine if D1 is fixed.

### D3. First shop is stripped of its staples, Bomb only at tier 3 - severity: inconsistent (minor)
Merchant at tier 0 sells no Torch (-> ScorpionCage), Rope (-> StoneSlab), Wooden Arrow (-> Flare), Lesser Healing Potion
(-> Ironskin) and no vanilla Pickaxe/Axe (-> RichMahoganyHammer 100, TungstenPickaxe 12582). The mod treats these as
craftable (planned recipe tier 0 for Torch, Wooden Arrow, Rope, Lesser Healing Potion), so nothing is unobtainable. Bomb has
findability 3 and planned recipe tier 3, while Demolitionist#1 Bomb -> RedSnapper; the only tier-0 Bombs are Painter#10
(SkyBluePaint -> Bomb, 563 copper) and SkeletonMerchant#23 (night visitor). Dynamite: recipe tier 1. Not a softlock (no early
gate needs Bombs), and it is "wanted randomness"; mentioned because the Merchant is the first shop a player sees.

## Questions (unsure, needs the user's or the developer's decision)
- Q1. Furniture, cage and painting prices at 5000-12500 (Campfire 7449, ScorpionCage 6415, BlueDragonflyJar 8647...): is
  `item.value` really 0 for them, or are these real values? Same band as D1, so likely the fallback. A dump of
  `ContentSamples` values would settle it.
- Q2. Added slots "from tier 0" (shops-added.tsv) sell PartyGirl PogoStick (tier 7, true tier 0, 309283) and Goblin
  Tinkerer RocketBoots / Toolbelt (tier 3, true 1). These are vanilla shop items, so presumably intended; Pogo Stick six
  tiers early looks like a skip. See terraria.wiki.gg/wiki/Pogo_Stick.
- Q3. Dyes (tier 2) are sold at tier 0 by the Dye Trader and Stylist (BrownDye, ReflectiveDye 45430 copper...): they come from
  tier-2 originals and are cosmetic. Fine unless dyes should respect "tier 2 = after the evil boss".
- Q4. Modded items in vanilla shops at tier 0: MagicStorage BiomeGlobe (Steampunker#28, Golfer#20), CraftingAccess
  (BestiaryGirl#42), StorageConnector (TravellingMerchant#62, 100 copper). Storage mods are classified as loot; accepted?
- Q5. Late-locked slots (Truffle, Steampunker, WitchDoctor "After defeating Plantera/Golem") roll tier 0-1 items at tier 0
  (SilverHelmet, GreenString...): harmless since the condition is kept, just a low-value late slot.

## Fine / checked
- Replacements above tier 1 at tier 0: only dyes (tier 2) and cosmetics whose true tier is later (vanity t1 / true 3-4,
  Misc t0 / true 3-4 like ChestLock, WhiteAndRedGarland, LovePotion). No weapon, armor, accessory or pickaxe beyond tier 1.
  Gear rows (DemonBow, WandofFrosting, Sandgun, FloatingTube, SlimeGun) are t1 with true tier 0-1.
  CreativeWings is the vanilla surface-chest Fledgling Wings, not a creative item.
- Unchanged slots: 37 rows, all DD2 Bartender (Defender Medal currency): correct.
- Duplicates: no replacement appears twice in one shop at tier 0. Only 4 rolls returned their own item.
- Shop-only coverage: every original with findability never and recipe never has an added slot (43 at tier 0 in shops-added.tsv:
  MiningHelmet, Flintlock, Minishark, Aglet, Flare, Marshmallow, Shuriken, ThrowingKnife, paints, MusketBall...). Pylons are
  craftable (planned recipe tier 2), so their slots can be rolled.
- Price extremes: highest 47.9 gold (vanity at the Travelling Merchant), lowest 100 copper (MinimumPrice respected). No tier-8
  item at 1 copper and no tier-0 item at 50 platinum.

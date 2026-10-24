# Audit shops-t8
Summary: 897 slots at unlocked tier 8 across 25 shops (860 rolled, 37 DD2 Bartender unchanged). No duplicate item inside one shop (except the 37 unchanged Bartender rows). No softlock found (shop-only items are all placed). Main issues: (1) item-value pricing gives absurd prices for a few vanity/misc items repeated in many shops, (2) heavy concentration on a few items across shops, (3) the stock does not scale much with the tier: 3-5 tier items are still ~35 % of slots (tiers 3:44, 4:34, 5:235, 6:144, 7:215, 8:222, 9:3), (4) a few tier-9 true-tier items sold before Moon Lord (within the allowance, but several are real post-ML gates).
Price scale: price = item.value x (1..2.5) (PriceRules), median 250k copper (25 g), max 4.97M (497 g).

## Defects (most severe first)
### D1. Absurd prices from raw item value — severity: absurd
| npc/slot | item | tier | price (copper) | vanilla slot price |
|---|---|---|---|---|
| Clothier 3 | DiamondRing (vanity) | 7 | 4,967,770 | 250,000 |
| Dryad 23 | DiamondRing | 7 | 4,858,504 | 30,000 |
| Pirate 2 | DiamondRing | 7 | 4,596,060 | 50,000 |
| Cyborg 13 | DiamondRing | 7 | 4,216,328 | 30,000 |
| TravellingMerchant 2 | DiamondRing | 7 | 4,131,362 | 400,000 |
| SantaClaus 8 | UltraAbsorbantSponge (Misc, tier 6, true 3) | 6 | 3,678,562 | 500 |
| Painter 1 | UltraAbsorbantSponge | 6 | 3,119,650 | 10,000 |
| TravellingMerchant 1 | GravityGlobe (accessory) | 7 (true 9) | 3,044,729 | 50,000 |
and 6 more DiamondRing rows (14 rows in total, 200-500 gold each).
Diamond Ring is a pure vanity (vanilla buy price 2 platinum = 200 gold, https://terraria.wiki.gg/wiki/Diamond_Ring); with the x1..2.5 multiplier it costs up to 497 gold and it is rolled in 14 of 25 shops. Whenever a cheap slot (pylon, 30000) is replaced by such an item, the shop shows an unaffordable decoration. UltraAbsorbantSponge (tier 6, true tier 3, vanilla value ~150 gold) is similar. Cause: PriceRules.Price uses item.value with no cap relative to the tier or to the original slot's price. Fix: cap the price by a tier-based ceiling (e.g. TierBasePrice x 4) or downweight items whose value is far above their tier in the roll; vanity/furniture of very high value should not be rolled into low-price slots.

### D2. Few items flood the shop pool across NPCs — severity: inconsistent
Counts of the same replacement over the 25 shops: TerraToilet 25, ChippysCouch 23, Moondial 23, TeleportationPylonVictory 23, MagicStorage StorageUnitBlueChlorophyte 23, Sundial 22, DeerclopsMasterTrophy 17, DiamondRing 14, QueenSlimeMasterTrophy 13, EverscreamMasterTrophy 11, EaterofWorldsMasterTrophy 10. TerraToilet (tier 7, true tier 6, price 150-350 copper) replaces 25 pylons/stations/furniture (TinkerersWorkshop, CrystalBall, ChristmasTree...) at 191-346 copper: a hardmode-tier item at pocket-change price, selling "rare" items everywhere. Modded MagicStorage StorageUnitBlueChlorophyte is in 23 shops. Mostly caused by the SameAsOriginal category policy on a Furniture pool (364 of 897 slots are Furniture) with noDuplicates applied per shop only. Fix: a world-wide soft duplicate penalty (or a per-item cap on shop appearances) for the shop roll; same-category furniture pool is small at tier 7-8.

### D3. Tier-9 (post Moon Lord) true-tier items sold at tier 8 — severity: skip (mostly by design)
74 slots have true tier 9 but only 3 have item tier 9. Rows that matter (progression value):
| npc/slot | item | tier / true | price |
|---|---|---|---|
| Steampunker 6 | LunarHamaxeNebula (Tool) | 9 / 9 | 597,845 |
| GoblinTinkerer 6 | RubblemakerMedium | 9 / 9 | 501,848 |
| BestiaryGirl 27 | RubblemakerMedium | 9 / 9 | 415,034 |
| Dryad 14, Santa 12, Truffle 6, Steampunker 28, PartyGirl 7, Painter 2, Golfer 1, BestiaryGirl 5 | PortalGun | 7 / 9 | 0.54M-1.18M |
| TravellingMerchant 1 | GravityGlobe | 7 / 9 | 3.04M |
| WitchDoctor 7 | LongRainbowTrailWings | 8 / 9 | 658,443 |
| 23 slots | TeleportationPylonVictory | 8 / 9 | 1.1M-2.5M |
| 25 block/wall slots | Cosmic Ember / Dark Celestial / Lunar Rust / Mercury / Cryocore / Heavenforge / Star Royale / Astra bricks and walls | 8 / 9 | 0.37M-0.83M |
and ~10 more. Only 3 are really tier 9; the others have tier 8 (or 7) and therefore pass the allowance of 1 tier, "true tier" 9 only because they have no other source. LunarHamaxeNebula (the luminite hamaxe: pick 225%) bought before Moon Lord at ~60 g is the one real skip; the Moon Lord Master Trophy sold from Dryad 27 (tier 5, true 9, 74k) is harmless decoration but its tier 5 is a classification issue (relics/master trophies tier 5 whatever the boss). Cause: ShopPlanner uses sourceTier = max(origTier, unlocked-1) + TierJitterUp 1, so tier 9 can enter at unlocked tier 8. Suggested fix: exclude Tool/Weapon/Armor/Accessory of tier 9 from shop rolls until tier 9 (cap TierJitterUp for those categories); optionally set Portal Gun / Gravity Globe tier to the real tier (they come from Martian Saucer / Queen Slime? tier 7 stored vs true 9 is a classification mismatch worth checking).

### D4. Cheap low-tier ammo/junk replaces hard late slots; stock does not scale — severity: inconsistent
Tier 3-4 items are 78 of 860 rolls, tier 5 are 235 (27 %). Tier 3 ammo (CursedDart, HolyArrow, CrystalDart, IchorDart, HighVelocityBullet...) at exactly 100 copper (MinimumPrice) fills Arms Dealer 1,2,3,11,13,14,15 and Cyborg 1-6, WitchDoctor 22, SkeletonMerchant 17,18: ~25 rows are ammo at 100-320 copper, as in a tier-0 shop (and the Arms Dealer/Cyborg shops end up with no useful late item). Also "Nail" ammo (tier 7, true 6) sold for 105-241 copper in 7 shops. Cause: the Ammo category pool at SameAsOriginal has only a few entries and their value is tiny; price is value-driven. Not a softlock; mention as stock quality. Fix: for slots with a vanilla price < MinimumPrice use tier-based floor price.

## Questions
- Q1. Is "shop-only" = True on 759 of 897 rows intended? It marks almost every row incl. rolled DiamondRing/Sundial (which have other sources), so the column may be mislabelled (or the item is only reachable via shops in this world, e.g. TerraToilet, pylons: findability never). If it is real, ~25 items depend on shops that can hide behind conditions (blood moon, full moon, "In a Graveyard biome") e.g. Dryad 4/10/11 sell CryocoreBrick/MercuryBrick only during Blood Moon/Graveyard, SkeletonMerchant DayBreak only during a full moon, Wings only on specific moon phases: reachable but slow.
- Q2. DD2Bartender: 37 unchanged slots (Squire/Monk/Huntress armor, Tier 7 items for 50-60 copper in DD2 coin pricing) - kept on purpose? Valued in Defender Medals, not coins; not a defect.
- Q3. SantaClaus 12 and others sell PortalGun (true tier 9, findability 9) at tier 8: confirm the Portal Gun tier of 7 is intended (vanilla: Martian Saucer drop, post-Golem + Martian Madness).
- Q4. Prices look scale-free: tier 3 items cost 100 copper and 3.1M (Ultra Sponge). Is a tier floor/ceiling wanted?

## Fine / checked
- No duplicate replacement inside one shop (noDuplicates works), apart from the 37 unchanged Bartender rows.
- Stock count scales per NPC (Merchant 37 ... TravellingMerchant 68), fashion vendors (Clothier 66) show many late vanity items at 0.2-0.6M copper, consistent with tier 8.
- Wings/Master trophy/Fragment slots: Fragments sold at 10-24k, cheap but allowed; Beetle Husk/Fragment materials sold at tier 7-8 are in-tier.
- No weapon/armor of tier 9 except LunarHamaxeNebula. No 1-copper late items (min price 100).

# Audit shops-added
Summary: 1083 rows = 149 distinct shop-only items, each added at exactly one merchant (no item duplicated across shops, no
duplicate slot) and repeated for every tier from its unlock tier on. All 149 have findability "never" and planned recipe tier
"never" in items-ref, so the rule is respected on paper. Prices are the vanilla value x 1..2.5 (checked on the wiki: Parrot
Cracker 375g -> 818g, Pogo Stick 25g -> 30.9g, Static Hook 50g -> 69.8g, Proximity Mine Launcher 35g -> 79g), as designed.
No added item sits more than the 1-tier allowance above its true tier. Main issues: 26 of the 149 items DO have a source
>= 2 % in this very export (so they are not obtainable "nowhere else"; the findability model ignores those sources), and the
100-copper price floor makes single ammo/consumables 7-15x their vanilla price. The merchant arrival tier is not in the
export, so "shop-only item on a late NPC" could not be checked from this file (see Questions).

## Defects (most severe first)
### D1. Items added as "obtainable nowhere else" although drops/bags >= 2 % list them — severity: inconsistent
Rows (first normal-mode source with chance >= 2 % in drops-*.tsv / bags.tsv; shop = added slot start tier):
| item | shop from tier | item tier | source (owner, owner tier, chance, kind) |
|---|---|---|---|
| MiningHelmet | 0 | 2 | SkeletonTopHat t2, 5 %, permuted |
| NightVisionHelmet | 0 | 2 | Crawdad2 t2 3.3 %, PirateDeadeye 3.3 % (permuted) |
| FlintlockPistol | 0 | 2 | PirateDeadeye 3 % |
| Ruler | 0 | 1 | Salamander3 t1, 2.4 % |
| ThrowingKnife | 0 | 0 | ArmedZombieEskimo t1, 2.4 % |
| RocketBoots | 0 | 3 | EyeballFlyingFish t3, 2.4 % |
| MechanicsRod | 2 | 2 | CrimsonPenguin t2, 8 % |
| IceRod | 4 | 4 | DiggerHead t4, 6 % |
| DiggingMoleMinecart / BubbleWand / GolfClubTitaniumIron | 1 / 1 / 4 | 2 / 2 / 4 | Crimson / Hallowed fishing crates, 20-25 % |
| MushroomSpear, TikiShirt, PygmyNecklace, FireworksLauncher | 4-6 | 6-7 | Nutcracker 3 %, GingerbreadMan 2.5 %, GoblinShark 5 %, WyvernHead 4.8 % |
| SpectreGoggles, TikiPants | 5 | 5-6 | MartianSaucer (+Cannon/Turret) 35-69 % |
| Flare, ClusterRocketII, RedBulb, Cannonball | 0/6/4/4 | 0 | gem squirrels 35 %, YellowCockatiel 7.5 %, Salamander4 5 % (critters) |
| FormatC, SnowSolution, SandSolution | 4, 8, 8 | 3 | Mechanic 12.5 %, Steampunker 100 %, Scarecrow10 24 % |
and 4 more (GolfBall from SkeletronHand 13.9 %, BallOfFuseWire from SandShark 2 %, others).
Why: the design rule is "only items obtainable nowhere else". Harmless for progression (the shop is a safety net) but the
shop slot then duplicates a real source and uses merchant capacity. For crates, town-NPC death drops and critters the sources
are arguably unreliable, so keeping the slot is defensible; for ordinary variants (SkeletonTopHat, Crawdad2, DiggerHead,
Salamander3, ArmedZombieEskimo, EyeballFlyingFish, CrimsonPenguin, SandShark, GoblinShark, WyvernHead) it is a model gap.
Suspected cause: `Findability` (GuaranteedSources.cs ~1290) only plans `EnemySwapPool.NaturalEnemies(t)` minus event enemies and
bosses; enemy variants, critters, town NPCs and fishing crates (BagContentTier never) are not counted although
`RandomNpcDropRule.Plan` gives them real drops. Suggested fix: count variant NPC types of a natural enemy (same netID family) as
sources at their base tier; leave critters/crates/events as "unreliable" on purpose, and document it.

### D2. Single ammo / consumables priced at the 100-copper floor — severity: absurd (low)
Rows (price in copper, vanilla buy price from wiki where known):
| item | shop | price | vanilla |
|---|---|---|---|
| MusketBall | Cyborg, tier 0 | 100 | 7 (x14) |
| Shuriken | Stylist, tier 0 | 100 | 15 (x6.7) |
| Stake | Painter/Decor, tier 0 | 100 | 15 (x6.7) |
| ThrowingKnife, BoneArrow, Flare, ExplosiveJackOLantern, Football, SmokeBomb, BeachBall, ShadowPaint, 4 base paints | various | 100 | a few copper |
Why: `Append` sells stack = 1 and `PriceRules.Price` clamps to `EconomyConfig.MinimumPrice` (100). One bullet for 1 silver is
>5x vanilla (brief, item 3). Not blocking (these items drop elsewhere too).
Suggested fix: floor the price at a fraction of the category, or sell ammo-like items with a stack (e.g. 25-50) at
price x stack, or lower the floor for Ammo/Misc stackables.

## Questions (unsure, needs the user's or the developer's decision)
Q1. Merchant arrival is not in the export. `from tier N` is only `UnlockTierOf(item)`; the real sale tier is max(arrival,
unlock) (ShopPermutation.SaleTier). Cyborg hosts 16 items at once from tier 6, others from tier 0. Please check the game log
line "Shop-only items: ... at a merchant arriving later than their tier + 1" (OutOfWindow) and consider adding a
sale tier / arrival column to the export.
Q2. Tier-0 consumables only buyable late: Cannonball (from 4), RocketI (5), ClusterRocketI (6), ClusterRocketII (6), EchoCoating
(5), Christmas lights/garlands/bulbs/toppers (4, about 28 items, 5-12 silver). True tier comes from the vanilla shop condition.
Fine for decoration; rockets/cannonballs leave Rocket/Cannon users without ammo until tier 5-6 unless dropped.
Q3. Allowance cases worth a look: Hammush and MushroomSpear (item tier 6, vanilla "after a mechanical boss") sell from tier 4 =
start of hardmode (948k / 816k copper); Leaf Wings (vanilla post-Plantera) sold by the Pirate from tier 5, 249g; Rocket Boots
and Toolbelt (tier 3) sold from tier 0 by the Goblin Tinkerer (true tier 1 -> unlock 0). Within TierAllowance=1, but Rocket Boots
and Toolbelt are movement/utility items bought before the first boss if the Goblin Tinkerer arrives early.
Q4. Mount/pet prices far above what the tier affords: ParrotCracker 818g at tier 4, Steampunk Wings 683g at tier 6, TikiTotem 258g,
Clentaminator 319g, DiggingMoleMinecart 98g (tier 1), LightningCarrot 83g (tier 2), BallOfFuseWire 106g (tier 2). Follows
vanilla x1..2.5; confirm that cosmetic items may be out of reach.
Q5. MartianSaucer is exported with owner tier 4 (drops-t4-5) while its Cannon/Turret are tier 7; the Martian Madness event
needs Golem. It drops SpectreGoggles (69 %) and TikiPants (35-52 %) as extras. Possibly an NpcTiers classification slip (outside
this chunk, but it feeds D1).
Q6. Painter/Decor hosts 10 added items (ExplosiveJackOLantern, GlowPaint, ReleaseDoves, Stake, Cannonball, bulbs...). Players
rarely open the Decor tab; only hints would tell them. Also Dryad hosts PurificationPowder, Clothier PaintedHorseSaddle.
Q7. DD2ElderCrystal is sold from tier 1 but its stand (DD2ElderCrystalStand) has planned recipe tier 3 and no other source, so the
Old One's Army starts at tier 3 at best; fine unless the event is expected earlier.
Q8. Shell recipes with no ingredients exist in recipes (index 3204 Marshmallow "rebuilt,shell", 3598 Cauldron "shell",
planned doable tier 0 / ingredient tier -1) while items-ref says planned recipe tier never. Presumably disabled at run time
(39 such shells in the export); confirm they cannot be crafted for free.

## Fine / checked
- 149 distinct items, 0 duplicated across merchants, 0 duplicate slots per shop/tier; the sale list only grows with tier.
- All 149 are findability never and planned-recipe never (consistent with the rule), apart from the sources of D1.
- Hardmode-and-later items (unlock >= 4) only start at tier >= 4; StaticHook (tier 9, true 5) starts at 4 (vanilla hardmode).
- Export slot numbers (up to 66) are indexes after the vanilla entries, not display slots; `ShopPermutation.Append` fills free
  slots below the 40-slot limit, then displaces the last rolled ones.
- Prices follow vanilla value x1..2.5 (floor 100); no tier-8 item at 1 copper, no tier-0 item at platinum.
- Rolled shop entries elsewhere may also give some added items (12-40 per tier sample): extra copies only, harmless.

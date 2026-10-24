# Audit recipes-t0-3
Summary: 652 recipes whose result is tier 0 (furniture, statues, wood/ore-bar gear, vanity, minecarts, station items).
No recipe is flagged "value short", "repaired", "protected" or "house" in this chunk. The tier 0 station chain is sound:
WorkBench (Wood + Daybloom, no station) -> Sawmill / Furnace / Chair / Bottle; Keg and Bookcase at Sawmill; Loom at Bottles;
IronAnvil at Chairs; LeadAnvil at Bookcases; CookingPot at Bookcases; HeavyWorkBench at Furnaces; GlassKiln at Anvils.
No self-reference, no cycle, and no recipe uses an ore absent from this world (Tin, Gold, Iron, Tungsten, Crimtane).
Main issues: compounded crafting costs (a craft-only ingredient needed in large counts) and very high Fallen Star / gem counts
for tier 0 items. The late doable tiers of furniture are by design.

## Defects (most severe first)
### D1. Nested craft-only ingredients with large counts (hidden cost explosion) — severity: absurd
| idx | item | ingredient line | cheapest planned recipe of that ingredient |
|---|---|---|---|
| 2139 | TungstenChainmail | PlatinumOre x30 + SeafoodDinner x19 | SeafoodDinner (find never): Tuna x7 / Flounder x12 / RedSnapper x4 / Lens x15 (idx 550, 151, 144, 139) -> about 76 RedSnapper, 133 Tuna or 285 Lens |
| 2124 | SapphireStaff | BunnyStew x6 + Flounder x30 | BunnyStew: Vine x4 (idx 100) -> 24 Vine + 30 Flounder |
| 2513 | TopazMinecart | EmeraldMinecart x1 + Terrarium x3 + FallenStar x8 | EmeraldMinecart = PalmWood x30 + FallenStar x30 (idx 2515) |
| 2514 | SapphireMinecart | EmeraldMinecart x1 + JungleYoyo x1 | same, plus a Yoyo (Lens x2, RedSnapper, Flounder x2...) |
| 2207 | SapphireHook | LargeTopaz x1 + GreenThread x21 | LargeTopaz = Ruby x7 (idx 1646, doable tier 1) |
| 2081 | IronHammer | ThornsPotion x3 + CreativeWings + PumpkinLeggings | PumpkinLeggings = ViciousPowder x11 + MonsterLasagna + CopperOre (idx 1785) |
| 1920 | TopazRobe | HerosPants x1 + TinBar x17 | HerosPants = DirtBomb x28 + DirtStickyBomb x14 + LeadBar x6 (idx 1875) |
Why: the planner checks availability (a0) of each ingredient line but not the multiplied cost of an ingredient that has no
source except crafting. TungstenChainmail is the worst: SeafoodDinner x19, each of which is a separate craft.
Suspected cause: `RecipePlanner` rebuilds one level only and the count roll ignores the recursive cost of craft-only ingredients.
Suggested fix: cap the count of a craft-only ingredient (findability "never") at about 3 to 5, or add its cheapest recipe cost to the
value-short check.

### D2. Very large Fallen Star counts for tier 0 gear — severity: absurd
Fallen Stars (tier 1 item, only fall at night) are asked 16 to 30 times for items doable at tier 0.
| idx | item | ingredient |
|---|---|---|
| 2515 | EmeraldMinecart | FallenStar x30 + PalmWood x30 |
| 2148 | GoldShortsword | FallenStar x21 |
| 2133 | TungstenHammer | FallenStar x20 |
| 1922 | EmeraldRobe | FallenStar x19 |
| 2166 | PlatinumBow | FallenStar x16 |
and 2513 TopazMinecart adds x8 on top of the EmeraldMinecart (38 stars in total). Another chunk has SeafoodDinner with FallenStar x23 (idx 150).
Why: 20 to 30 stars is many nights of play for a tier 0 hammer or sword. Cause: counts depend on coin value only (star v2500 should
already limit it) and time-gated items are not capped. Fix: cap time/event-gated items (FallenStar) at about 8 to 10.

### D3. Counts of 20 to 30 of gems / lenses for tier 0 armor — severity: absurd (mild)
| idx | item | ingredient |
|---|---|---|
| 2169 | PlatinumChainmail | IronBar x30 + Sapphire x21 |
| 2170 | PlatinumGreaves | Sapphire x24 |
| 2126 | SilverChainmail | Amethyst x24 |
| 2154 | GoldWatch | Amethyst x20 |
| 1919 | AmethystRobe | Lens x25 |
| 2269 | BeeGreaves | Lens x30 + Glowstick x30 |
| 2221 | GemLockEmerald | IronBar x8 + OrangeStainedGlass x30 + Stinger x30 |
Why: tier 0 gems are found only underground or in pots; 20+ of one gem for one piece is heavy but obtainable. Same count rule as D2.
Of 257 ingredient lines with count >= 25 in this chunk, most are cheap blocks, walls and herbs; only gem, fish, star and cooked-food lines matter.

## Questions (unsure, needs the user's or the developer's decision)
Q1. Blade of Grass (idx 2258, Stinger x21 at Furnaces), JungleYoyo (2261), Ivy Whip (2260) and jungle armor (2263-2265) are tier 0 and
 doable at tier 0 (all ingredients a0, jungle plants). Vanilla Blade of Grass is a jungle pre-hardmode weapon, 32 damage with fire
 (https://terraria.wiki.gg/wiki/Blade_of_Grass), while HornetStaff and BeeHeadgear are tier 2 in the classifier. Is tier 0 for the whole
 jungle set intended? It may be a 1 to 2 tier skip in the first boss window.
Q2. Bomb (tier 0) has findability tier 3 and planned recipe tier 3 (sources: UndeadMiner, shops that roll to other items, GemBunnyTopaz
 extra drop 35% x23-29). About 45 tier 0 recipes of this chunk (Fake_*Chest, Bone furniture, ChestLock, ActiveStoneBlock, vanity such as
 TuxedoShirt with Bomb x30) therefore become doable at tier 3 only. By design the planner follows availability, but is a tier 0 item found only at
 tier 3 intended? GemBunnyTopaz giving 23-29 bombs at 35% also looks generous.
Q3. Furniture/blocks are classified tier 0 although their true tier is later (Lihzahrd 6, Steampunk 5, Spooky 6, MythrilBrick 4,
 Fake_SolarChest / VortexSofa / SolarClock 8, MartianChest 7). Doable tiers are correct (they use late ingredients). Pearlwood weapons and armor
 (idx 1558-1563) have table tier 0 but true tier 4 and doable 4; being tier 0 they can be rolled as early rewards. Confirm this decorative
 classification is intended.
Q4. BirdStatue (idx 1717) is only craftable at SteampunkBoiler (doable tier 5; vanilla recipe tier "never", so a shell recipe) while other statues
 are doable at tier 0. Fine if shell recipes may take any station.
Q5. CreativeWings (vanilla item) is an ingredient (IronHammer idx 2081), classified tier 0 with findability 0, and appears in about 8 drop tables
 (SpikedIceSlime 3%, GemSquirrelTopaz 1.5%, crates 2.5%). The ban only covers modded "Creative*" items. Intended as normal loot?
Q6. SandFallBlock has two recipes (idx 1548, 2259), both only doable at tier 4, while it is a tier 0 item and an ingredient (x30) of SteampunkChair and
 PearlwoodBow. Harmless decoration, noted for the dependency.

## Fine / checked
- Doable tiers: 420 recipes at 0, 65 at 1, 14 at 2, 47 at 3, 39 at 4, 26 at 5, 15 at 6, 20 at 7, 6 at 8. Rows at 3 or more are all furniture,
 banners, vanity or blocks whose ingredients are tier 3 to 8 (Bomb, SpiderFang, BeetleHusk, Cursed Flame, Soul of Light, Fragments).
- Stations: every station used by d0 recipes (Sawmill, Loom, Bottles, Kegs, CookingPots, Bookcases, Anvils, Furnaces, Chairs, Tables, HeavyWorkBench,
 WorkBenches) has a d0 recipe; GlassKiln needs Anvils + 25 LeadOre + 19 Coral (ok); LivingLoom 0; HoneyDispenser, IceMachine, SkyMill, Solidifier 1;
 BoneWelder 3; FleshCloningVat 4; SteampunkBoiler 5; LihzahrdFurnace 7. No station recipe needs itself.
- Ores: bars are made from present ores or other stuff (TinBar from Waterleaf, GoldBar from Silk + CopperOre, TungstenBar from FallenStar x2,
 LeadBar from IronBar + TungstenBar, IronBar from SilverOre + LeadOre).
- Boss summon in the chunk: GoblinBattleStandard (idx 1866) doable 0; basic materials Wood, Silk, Glass, Hive, Leather, GreenThread, Chain doable 0.
- Value ratios: swapped rows carry the partner's vanilla value, so they cannot be compared; non-swapped rows are consistent with bar-based
 vanilla costs (copper bar 750, iron 1500); big ratios only come from the 100 minimum value of herbs and cheap walls.
- Result stacks normal (walls x4, bricks x5, Chain x15, MinecartTrack x50 like vanilla).
- Duplicate result rows (FleshBlock, SlimeBlock, DynastyWood, Throne, PeaceCandle, SunplateBlock, Flymeal, SandFallBlock) are vanilla alternative recipes.

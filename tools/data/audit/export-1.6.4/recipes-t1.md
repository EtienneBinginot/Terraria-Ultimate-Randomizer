# Audit recipes-t1
Summary: 807 recipes whose result has item tier 1. Evil summons are fine: Worm Food (idx 2917, doable tier 0: Stinger x8, Blinkroot x15, Orange Paint x30, Demon Altar)
and Bloody Spine (2918, tier 0: Daybloom x22, Vine x2, Demon Altar) are doable at tier 0 with ingredients findable at tier 0; Suspicious Looking Eye (2912) too.
Real gear and stations of this chunk (Tinkerer's Workshop, Extractinator, Meat Grinder, Solidifier, Magic Mirror, Demonite/Crimtane items, evil pickaxes) are doable at
tier 1-2, no softlock found. 531 recipes are doable at tier 1, 138 at tier 2. Main issues: (1) hidden crafting-chain cost of the Bomb intermediates, (2) rebuilt
recipes using ingredients that can never be obtained, (3) a batch of results labelled tier 1 whose true tier is 4-7 (classification, mostly cosmetic shells).

## Defects (most severe first)
### D1. Rebuilt recipes use ingredients whose availability is "never" — severity: softlock (cosmetic items only)
| idx | result | unobtainable ingredient | doable |
|---|---|---|---|
| 1966 | PinkEskimoCoat | BlackDragonfly x3 (t1/anever) | never |
| 3553 | WizardsHat | BlackDragonfly x7 | never |
| 4013 | BuccaneerBandana | BlackDragonfly x3 | never |
| 2940 | CookedMarshmallow | Marshmallow x1 (find never, no recipe makes it) | never |

Four recipes can never be crafted. BlackDragonfly (a critter item, find never) is picked as a tier-1 ingredient although nothing in this world gives it (no drop, no shop,
no recipe). Suspected cause: the RecipePlanner ingredient pool does not filter Critter items / availability "never" for rebuilt recipes. Fix: exclude ingredients whose
availability is never from rebuilt recipes. Cosmetic results only here, but the same hole could hit a needed item. CookedMarshmallow looks like a vanilla-recipe case
(Marshmallow has no source): check it is accepted or switched off.

### D2. Nested crafting chains make counts absurd (counts are value-matched, the cost of crafting the intermediate is ignored) — severity: absurd
| idx | result | heavy ingredient | what the ingredient costs itself |
|---|---|---|---|
| 2192 | Fleshcatcher | DryBomb x27 (+ IronBar x30, RainCloud x30) | DryBomb (217): FrozenSlimeBlock x30 + TopazGemsparkWall x29 each, about 1600 raw items |
| 2202 | CrimsonYoyo | DryBomb x30 + LeadBar x30 | same |
| 2150 | RubyStaff | DryBomb x28 | same |
| 2209 | RubyHook | WetBomb x30 | WetBomb (273): PinkPaint x30 + DiscWall x30 each |
| 2180 | FisherofSouls | WetBomb x29 | same |
| 1923 | RubyRobe | WetBomb x28 | same |
| 228 | FlinxFurCoat | WetBomb x16 | same |
| 2211 | AmberHook | HoneyBomb x30 | HoneyBomb -> DryBomb -> WetBomb chain |
| 2871 | Sandgun | LavaBomb x30 | LavaBomb (2983): RecallPotion x3, which needs HoneyBomb... chain |
| 2972 | MagicStorage UpgradeDemonite | RecallPotion x30 + Hook x30 + TinBar x28 | Recall: HoneyBomb + ObsidianSkin x2 + Battle x3 |
| 3797 | FishCostumeShirt | RecallPotion x19 | same |

Also Hook x20-30 appears in 15 tier-1 recipes (DemonBow, LightsBane, Magiluminescence, SkyMill, HoneyDispenser, EngineeringHelmet...): feasible (Turtle drops Hook 17-30 at
35 %, skeleton 4 %) but heavy; Bass / PinkGel / Deathweed / RottenChunk x30 are routine.
Why a defect: Bombs are valued 2500 as ingredients but each costs dozens of raw blocks, so a recipe with 27-30 of them needs 1000+ raw items (impractical, not just a few
farming runs). Suspected cause: RecipePlanner computes counts from the coin value of the ingredient, not from its recursive craft cost, and nothing caps craft-only
ingredients (find never). Fix: cap the count of ingredients that are themselves crafted-only (find never) to about 5-8, or weight their value by their craft cost.

### D3. Items labelled tier 1 whose true tier is 4-7 (planned recipes at 4-7) — severity: inconsistent (possible skip)
Result tier column is 1, but items-ref true tier and planned recipe tier are much higher.
| id | item | tier / true / planned recipe tier |
|---|---|---|
| 3430 | Autohammer | 1 / 6 / 5 (SpectreBar x23, doable 6) |
| 2871 | Sandgun | 1 / 0 / 4 (true tier 0, contradicts its own recipe at 4) |
| 3707 | LihzahrdFurnace | 1 / 6 / 7 |
| 2454-2456 | SpectrePaintbrush / Roller / Scraper | 1 / 6 / 6 |
| 3194 | LandMine | 1 / 6 / 5 |
| 366, 367, 423, 424 | EchoPlatform / Block / Wall | 1 / 6 / 6 |
| 2891 | EchoMonolith | 1 / 6 / 6 |
| 3833-3838 | Martian costume and uniform sets | 1 / 6-7 / 6 |
| 3557-3559 | Cyborg set | 1 / 6 / 5 |
| 3396 | SecretoftheSands | 1 / 1 / 4 (find 4) |
and about 60 more shell/vanity/furniture rows (Golden furniture, Santa/Parka/Elf/Scarecrow/Buccaneer sets, Soul Bottles, Logic Sensors, Cursed/Ichor Torch, Golden Chest...)
with true tier 4-5. Almost all are cosmetic, so low gameplay impact, but an item tier of 1 could let them roll early as drops/shop/chest items. Suspected cause: the tier
column is the base tier before ItemClassifier.RaiseByRecipes, which raises the true tier. To check: that loot rolls (ItemPool) read the true tier, especially for
Sandgun, Autohammer and the Spectre paint tools (a roll at tier 1 would be a skip).

## Questions (unsure, needs decision)
- Q1. Sandgun: item tier 1, true tier 0, no vanilla recipe, planned recipe tier 4 (LavaBomb x30 + LargeSapphire, Mythril Anvil). Vanilla it is a Hardmode Arms Dealer item
  (https://terraria.wiki.gg/wiki/Sandgun). Why true tier 0 (shop/loot source lowering)? If it can be rolled at tier 0-1 it is a progression skip.
- Q2. GoldenKey as ingredient (availability 2, true tier 3, find never, coin value shows v1) in 5 tier-1 cosmetic recipes (3342 Beanie, 3578 VampireShirt, 3588 WitchDress,
  4136 WirePipe, 4338 TimerOneFourthSecond). Availability 2 while its real source (dungeon, after Skeletron) is tier 3 and nothing finds it. Check the availability of keys.
- Q3. Wire and Actuator (true tier 3) are used as availability-2 ingredients x20-30 in about 12 vanity/timer recipes (via herb replacement drops). Fine with TierAllowance 1; confirm.
- Q4. MagicStorage items are ingredients of vanilla gear (UpgradeCrimtane x13 incl. BloodButcherer x10 and DemonBow x7, ShadowDiamond x11, UpgradeDemonite x9). They are
  craftable (availability 1), so no softlock, but needing a storage-mod item for vanilla weapons is odd for a player; by design for modded recipes?
- Q5. Value outliers (factor > ~5 between vanilla and planned value): Teacup 20 -> 300, MaidHead 3870 -> 57000, MagicStorage CrimsonAltar 2500 -> 124200, FlinxFur 500 -> 2351,
  EngineeringHelmet 10000 -> 42001; too cheap: HeartreachPotion 50000 -> 3100, LavaBomb 206250 -> 3000, DryBomb 8500 -> 1543, LogicSensor_Sun 219250 -> 9500; Sandgun 7000 -> 159375.
  Mostly quirks of non-craftable/shell items; Sandgun and Heartreach stand out.
- Q6. Worm Food needs Orange Paint x30 (3850 vs 3150 vanilla value): reachable (Waterleaf herb replacement, paint). Fine but 30 paints for an early summon is a chore; Bloody
  Spine Daybloom x22 similar. Not a defect, a comfort question.

## Fine / checked
- Evil summons: Worm Food (2917), Bloody Spine (2918), Suspicious Looking Eye (2912), Abeemination (2919) all doable at tier 0 with tier-0 findable ingredients. Worm Food is also
  a guaranteed drop (Hornet / Eater of Souls, 1.67 %).
- Tier-1 progression items: Tinkerer's Workshop (3065, d1), Extractinator (3212, d1 via IllegalGunParts), Meat Grinder (3211), Solidifier (3213), Magic Mirror (2736/2737, d1),
  Demonite Bar (2179, d1; Crimtane Ore is absent in this world but Crimtane Bar is craftable from Corrupt Seeds x30, 2191), Nightmare and Deathbringer pickaxes (d2), evil armors (d2),
  Fossil Pickaxe (d1). No station recipe needs itself; no loop found in this chunk.
- Tier 3+ stations in this chunk are shells that world generation also places (Lihzahrd Furnace d7 in the temple, Bone Welder d3, Flesh Cloning Vat / Lesion Station d4): fine.
- Stacks are sane (MeteorShot 70, Wormhole 3, Jester's Arrow 10). 20 rows doable at tier 0 (Diamond, Ruby, Amber, Fallen Star, Mana Crystal, Dye Vat from cheap ingredients):
  acceptable under the weird-recipes wish.

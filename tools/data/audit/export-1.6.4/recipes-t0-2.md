# Audit recipes-t0-2
Summary: 652 recipes whose result is tier 0 (mostly furniture, blocks, walls and shells). 364 are doable at tier 0, the rest at
tiers 1-8. No "value short" / "repaired" flags in this chunk (flags: rebuilt 652, randomized 648, swapped 48, 2 off:twin, 1 house).
No ingredient has an availability above its recipe's doable tier. Value ratios of non-swapped recipes doable 0-2 stay within x5 of
vanilla. The chunk is almost all decor, so progression risk is low. Main issues: a group of Lesion furniture sitting on a
hardmode-only station, one real weapon (Tungsten Bow) locked behind the Lunar station, and decor that eats hard-gate materials.

## Defects (most severe first)
### D1. LesionStation (Chambre de decomposition) is only reachable at tier 4, so 19 recipes are doable at 4 although rebuilt for T 1-2 — severity: inconsistent
| idx | result | ingredient tier T | doable |
|---|---|---|---|
| 1220 | LesionWorkbench | 1 | 4 |
| 1223 | LesionDoor | 2 | 4 |
| 1224 | LesionChair | 1 | 4 |
| 1225 | LesionChest | 2 | 4 |
| 1226 | LesionTable | 2 | 4 |
| 1227 | LesionSink | 1 | 4 |
| 1228 | LesionLantern | 2 | 4 |
| 1229 | LesionCandle | 2 | 4 |
| 1230 | Glass (swapped onto this station) | 0 | 4 |
| 1231 | LesionCandelabra | 2 | 4 |
| 1232 | LesionBed | 2 | 4 |
| 1233 | LesionDresser | 2 | 4 |
| 1234 | LesionBookcase | 1 | 4 |
| 1235 | LesionClock | 2 | 4 |
| 1236 | LesionSofa | 2 | 4 |

and 4 more (1237 piano T3, 1238 bathtub, 1239 chandelier, 1240 toilet).
Why: the item LesionStation is tier 1 (item tier 1, true tier 4, findability 4, vanilla recipe "never"). Its own planned recipe
(idx 2899, chunk recipes-t1) is Pearlwood x30 (t4) + SkyBlueFlower x7 (t2/a4), so the station itself is planned at tier 4. The 19
recipes were rebuilt for T 1-2 but can only be crafted at 4. Impact is cosmetic (furniture), and Glass has doable-0 recipes
elsewhere (idx 305, 1578), so nothing essential is lost.
Suspected cause: station recipe rebuild (RecipePlanner / StationRecipeTiers) lets a thematic tier-1 station take tier-4 ingredients
(tier allowance plus hardmode wood), while dependants are rebuilt for the nominal tier. Fix: cap a station recipe's ingredients at
its item tier (+allowance), or rebuild dependants for the station's true tier.

### D2. TungstenBow (tier 0 weapon) is craftable only at the Lunar Crafting Station (tier 8) — severity: inconsistent
| idx | result | station | doable | findability | planned recipe tier |
|---|---|---|---|---|---|
| 1253 | TungstenBow | LunarCraftingStation | 8 | never | 8 |

Ingredients are trivial (Daybloom x30, Shiverthorn x30, FallenStar x7) but the swapped recipe kept the vanilla station of the
original recipe (Solar Brick), so the plan is tier-0 ingredients at a tier-8 station. Other sources: Arms Dealer shop (tier 8,
250000 copper), PartyBunny extra 4 % (party only), ArmedZombiePincussion surprise 1.2 % (below the 2 % threshold). A basic tier-0 bow
therefore cannot be built before the Lunar Crafting Station. Cause: result swap onto a recipe that stays on a late station; the
swap does not check the host station against the new result's tier. Fix: refuse swaps whose host recipe station availability is above
result tier + allowance, or rebuild the station. Same pattern for decor on late stations (31 LunarCraftingStation, 6
TinkerersWorkbench at 8, 2 ImbuingStation, 2 Autohammer, 2 CrystalBall at 8): harmless for decor, only 1253 is a real weapon.

### D3. Decor recipes consuming one hard-gate material, value x1000+ over vanilla — severity: absurd (low)
| idx | result | cost | vanilla value | planned value |
|---|---|---|---|---|
| 1168 | MartianDoor | AncientBattleArmorMaterial x1 (t4/a7) | 6 | 50000 |
| 1175 | ToiletMartian | BeetleHusk x1 (t7) | 6 | 25000 |
| 1159 | MartianBathtub | BeetleHusk x1 | 14 | 25000 |
| 888 | MartianConduitWall (x4) | FrostCore x1 (a7) | 1 | 50000 |
| 1170 | MartianLamppost | MagicStorage UpgradeBlueChlorophyte x1 + SteampunkDoor | 53 | 50200 |
| 1169 | MartianDresser | MagicStorage UpgradeBlueChlorophyte x1 | 16 | 50017 |
| 1165 | MartianChandelier | FrostCore x1 + CobaltBrick | 404 | 50703 |
| 1163 | MartianHoverCandle | BeetleHusk x1 | 54 | 25150 |
| 1161 | MartianHolobookcase | Ectoplasm x1 + TurtleShell x1 | 15020 | 30300 |

and 4 more (1160, 1162, 1167, 1173). Factors up to x8000 vs the vanilla value; a 6-copper door costs a Beetle Husk. Not a softlock
(all doable 6-7, same tier as the material) and arguably "wanted" weirdness, but the cost is far from the vanilla value (brief
criterion 3). Suspected cause: value-matching in RecipePlanner picks a single expensive ingredient when the result value is
tiny and the pool has no cheap in-window item (SpookyWood x7 and MartianConduitPlating are the rare cheap ones). Optional fix:
cap the value ratio for results worth under 100 copper.

## Questions
- Q1. Idx 898 WoodenDoor (flag `house`): Wood x6 at Workbench became SilverOre x1 at Bookcases. SilverOre findability 0 and Bookcase
  (vanilla recipe never, planned 0) findability 0, so reachable, but it is a 2-step chain for the basic house door. Is another door
  type always available on tier 0? Looks OK but the only house recipe in the chunk.
- Q2. Idx 1169 and 1170 use `MagicStorage/UpgradeBlueChlorophyte` (modded, t6, true 7, findability never) as ingredient of vanilla
  Martian furniture. Fine if modded content is intended, but it adds a hard dependency on that mod's recipe being obtainable.
- Q3. Basic tier-0 items with late findability or recipe tier used as ingredients here: Bomb (tier 0, find 3, recipe 3), Javelin
  (tier 0, 3), ActiveStoneBlock / InactiveStoneBlock (true tier 3, never found), Glowstick and Bass (find 2), Torch (find 1). Bomb at
  3 while the Demolitionist sells it from the start in vanilla looks like a classification issue (outside this chunk) and pushes about
  12 Piano recipes here (idx 904, 922, 956, ...) to doable 3.
- Q4. Idx 1006 FrozenDresser planned value 207 vs 4522 and idx 1075 MagmaSnailCage 15030 vs 70: these are swapped recipes (vanilla
  value belongs to the swap partner), shown for completeness, not comparable.
- Q5. Idx 839 / 855 are `off:twin` (DirtBlock / StoneBlock twins switched off): intended; 6 DirtBlock and 6 StoneBlock recipes remain
  doable at 0.

## Fine / checked
- No recipe has an ingredient availability above its doable tier (script check on all 652 rows).
- Tier-0 recipes only use tier-0 stations: Sawmill 63, Kegs 22, HeavyWorkBench 21, Loom 16, CookingPots 15, LivingLoom 4. The 17
  Solidifier recipes (tier-1 station) are all doable 1.
- Non-swapped recipes doable 0-2: planned value within x5 of vanilla. Largest stack: PoisonDart x100 (CopperOre + VilePowder + wall).
- Basic items in this chunk (StoneBlock, DirtBlock, Wood, WorkBench) all keep doable-0 recipes: WorkBench idx 903 (Wood x4 +
  Daybloom, no station), Wood idx 859/861/863/1409, many StoneBlock/DirtBlock.
- Glass has doable-0 recipes elsewhere (idx 305, 1578).
- No circular or self-needing station recipes, no expert/normal data, no duplicated ingredient on one recipe found.

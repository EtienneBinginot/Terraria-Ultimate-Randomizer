# Audit drops-t6-9
Summary: 831 rows (258 permuted, 349 extra, 112 surprise, 90 kept, 22 guaranteed). The critical progression drops of tiers 6-9 are present and
reliable: Plantera gives the Temple Key at 100 %, Golem/Duke/Empress/Cultist/Moon Lord keep their bag, trophies and signature loot, the Empress
gives Beetle Husk 4-8 at 100 % (tier 7), the Cultist gives Ancient Manipulator + Celestial Sigil at 100 %, the 4 pillars give fragments at 100 %, Moon
Lord gives 70-90 Luminite at 100 %. Main issues: (1) the Temple Key is given x12 at 100 % by Queen Slime at tier 5 (found in the tier 4-5 data while
checking the key), which opens the Temple/Golem before Plantera; (2) boss "first extra slot" is 100 % for strong items (Plantera drops Grenade Launcher
twice); (3) Super Healing Potion floods the tier 6-8 common drops with stacks of 6-8; (4) the export lists full loot tables for boss parts that should not drop.

## Defects (most severe first)

### D1. Temple Key x12 at 100 % from Queen Slime (tier 5): Temple opens before Plantera — severity: skip (plus absurd stack)
Rows (from drops-t4-5.tsv and bags.tsv; same item as my focus list):
| owner | owner tier | item (tier 6, findability 5) | stack | chance | kind |
|---|---|---|---|---|---|
| QueenSlimeBoss | 5 | TempleKey | 12-12 | 1.0 | extra |
| QueenSlimeBossBag | 5 | TempleKey | 12-12 | 1.0 | extra |
| Lihzahrd (owner tier 4, spawn 6) | 4 | TempleKey | 1 | 0.02917 | guaranteed |
| Plantera (this chunk) | 6 | TempleKey | 1 | 1.0 | permuted (vanilla source, fine) |
Why: the key is Plantera's reward (findability 5 = tier 6 - 1 allowance, but here it is a 100 % boss first-slot drop). With the key the Lihzahrd door opens
(bricks are unbreakable until Plantera, the door is the entrance), the Power Cell is also findable at tier 5, so the Golem (tier 7) is reachable after
Queen Slime/mechs, skipping Plantera. A stack of 12 keys is absurd for a single-use item.
Cause: boss first extra slot always drops (MobLootFeature, "A boss always drops its first extra slot") + roll window allows true tier 6 on a tier 5 owner + stack scaling
(`DropHelper.ScaledRange`) not capped for key-like items. Fix: never roll gate keys (TempleKey, LihzahrdPowerCell, biome keys) as extra/surprise on owners below
their gate; cap key stacks at 1 (FrozenKey 1-3 on RuneWizard has the same problem).

### D2. Boss first extra slot at 100 %: strong items every kill, Plantera's Grenade Launcher duplicated — severity: absurd / inconsistent
| owner | idx | item (tier) | chance | kind |
|---|---|---|---|---|
| Plantera | 4 | GrenadeLauncher (7), kept 14 % first kill | 0.14286 | kept |
| Plantera | 19 | GrenadeLauncher (7) | 1.0 | extra |
| Golem | 15 | DD2BallistraTowerT3Popper (7) | 1.0 | extra |
| DukeFishron | 17 | RainbowCrystalStaff (9, locked until tier 8) | 1.0 | extra |
| HallowBoss | 15 | FlyingKnife (5) | 1.0 | extra |
| MartianSaucerCore | 9 | HeatRay (6) | 1.0 | extra |
| CultistBoss | 6 | SquireAltShirt (7) | 1.0 | extra |
| LunarTowerStardust | 2 | WingsNebula (9 wings) | 1.0 | extra |
| LunarTowerNebula / Solar / Vortex | 2 | DiamondRing / WaspGun / LeafBlower | 1.0 | extra |
Why: vanilla gives one weapon of a 7-weapon table at 14 %; here a fixed weapon, wings or accessory drops on every (farmable) kill. Wings at 100 % from the Stardust Pillar are a tier 9
item one tier early. Rainbow Crystal Staff (tier 9, true 9) is a 100 % Duke Fishron drop from tier 8 on. Allowed by the +1 allowance, but never the same item the boss already drops.
Fix (keeps randomness): 100 % only for materials/consumables; cap weapon/wings/accessory first slots at about 25 %, and exclude items already in the boss's kept/permuted table.

### D3. Super Healing Potion floods the tier 6-8 common drops with large stacks — severity: absurd (variety)
23 extras and 34 rows in total; many owners at 40-100 % with 5-8 potions per kill.
| owner | tier | stack | chance | kind |
|---|---|---|---|---|
| RockGolem | 6 | 8 and 2-6 | 1.0 and 1.0 | permuted + extra |
| BigMimicJungle | 7 | 7 | 1.0 | extra |
| ZombieElfGirl / Scarecrow3 / FungoFish | 6 | 6-8 / 7-8 / 3-7 | 0.6 each | extra |
| AngryTrapper / Splinterling / DD2DarkMageT3 | 7 | 1-8 / 4 / 6-7 | 0.6 each | extra |
| GolemHead and Golem fists | 7-8 | 6 | 1.0 | extra (parts, see Q1) |
| MossHornet (-20, -21) | 6 | 8 | 0.4 | extra |
| SandElemental, BloodEelHead, Yeti, Hellhound... | 6-8 | up to 8 | 0.4-0.5 | extra / surprise |
and 8 more. Item: tier 6, true tier 8, findability 7 (locked until tier 7), so the flood starts exactly at tier 7: 6-8 top healing potions at 50 % from ordinary enemies;
the common-drop slot of tier 7 is almost always this potion.
Cause: CommonDrop (CommonDropWeights) has few tier 6-7 potions and potion stacks scale up (StackRules). Fix: cap potion stacks near 3 from tier 6, or a per-item usage cap in the common-drop roll
(no item on more than N owners).

## Questions (unsure, needs the user's or the developer's decision)

### Q1. Boss parts have full loot plans in the export (owner tier 8, huge numbers)
Owners that are BossParts: TheDestroyerBody/Tail, PrimeCannon/Saw/Vice/Laser, WallofFleshEye, GolemHead/GolemHeadFree/GolemFistLeft/GolemFistRight, MoonLordHead/Hand,
MartianSaucerTurret/Cannon, DungeonGuardian. Tables like AdamantiteOre 18 at 100 %, FlaskofIchor 4-7 at 100 %, HolyArrow 218-225 at 100 %, TrueExcalibur 52 %, PlanteraBossBag x2 at 10 % (Destroyer
segments), MeowmereMinecart (tier 9) 1.2 % on each Prime arm, RazorbladeTyphoon 69 % on the saucer turret and cannon, BejeweledValkyrieWing 69 % and LunarHamaxeNebula 69 % on each Moon Lord head/hand.
`ExtraDropsAllowedNow` returns false when the owner is a boss and `npc.boss` is false, so these should never drop (Destroyer body, Prime arms, Golem parts). Please verify that `npc.boss` is
false for MoonLordHead/MoonLordHand in vanilla (three limbs x 69 % wings would be absurd), and have the export skip parts (their tier 8 also comes from stats, not the boss).

### Q2. Wyvern: six segment types, six different loot tables
WyvernHead/Legs/Body/Body2/Body3/Tail each carry their own permuted and extra table (GravityGlobe 5 % surprise only on Legs, LunarFlareBook tier 9 0.48 % on Legs, IceFeather 33 % on Tail,
PaladinsHammer/Shield only on Head). In vanilla the Wyvern shares life and the segment hit last drops its loot; here the result depends on which segment dies (comment in `SpawnOriginNPC.IsNpcChild`:
"the segment killed drops the worm's loot"). Intended? It also makes GravityGlobe/VoidDye (5-12 % sources on segments) show findability "never".

### Q3. BossBagBetsy findability 9 although sources exist at tier 7
BossBagBetsy (tier 7, true 7): DD2Betsy 100 % kept, AngryTrapper 4.8 % extra (tier 7), RustyArmoredBones 0.27 %, Prime parts, MoonLordCore 20 % -> findability 9, and PulseBow, VampireKnives,
DefenderMedal, ChargedBlasterCannon are "far" (9) because of it. Probably UnlockTierOf(bag) = 9 plus Betsy being an event boss not in `NpcTiers.BossTypes`. Same event exclusion for BrokenHeroSword
(Mothron 44 % / 25 %, findability never, craftable at 7) and Soul of Flight (only OOA tier 3 Dark Mage; it has a mod recipe at tier 4, recipes-t3-5 index 3135).

### Q4. Final boss bags as random drops
MoonLordBossBag (true 9) at 6 % (1-2 bags) from HeadlessHorseman (tier 8); QueenSlimeBossBag, TwinsBossBag, WallOfFleshBossBag (1-3 copies, 0.2-4.8 %) and GolemBossBag (2 % on CursedHammer) as
surprises or extras of ordinary tier 6 enemies; BossBagBetsy x3. Within the allowance (the lock keeps them until true tier - 1), but a bag is a bundle of the boss's whole tier. Intended as surprise items?

### Q5. Large ammo stacks at 100 %
StyngerBolt 198-205 at 100 % on GoblinSummoner (extra); MoonlordArrow (true 9) 210-224 at 100 % on HeadlessHorseman and 90-156 at 100 % on BloodNautilus; ChlorophyteArrow 173-224 at 100 % on CrimsonAxe
(permuted). Stack size is normal ammo scaling, but 100 % luminite ammo at tier 8 is generous.

### Q6. Hardmode enemies owned at tier 6-8 by stats (spawn tier 4)
Mimic, IceMimic, Big Mimics, Wyvern, Derpling, Giant Tortoise, Illuminant Bat, Giant Flying Fox, Arapaima, Angler Fish, Jellyfish, Fungo Fish, Moss Hornet, Cursed Hammer, Crimson Axe, Enchanted Sword,
Ice Golem, Sand Elemental, Goblin Shark/Summoner, Pirate Captain, Blood Nautilus/Eel take their drop tier from stats (`NpcTiers.BaseTier` = max(life, damage tier)), not from their spawn tier 4 (hardmode
start). Result: their drops are locked until tier 6-8, so early hardmode kills give little. Intended?

### Q7. LihzahrdPowerCell, LihzahrdFurnace, Autohammer, TruffleWorm: `tier` 1, 1, 1, 3 but true tier 6, 6, 6, 4
Only the true tier is hand-fixed (`ItemClassifier.TrueTierFixes`; "Tier and the buckets never change"). If any roll path (chests, pots, shops, surprise) uses `tier` instead of the true tier for its
window they could appear early (the Power Cell with the Temple Key from D1 would then be much easier). Please confirm the lock uses the true tier everywhere.

### Q8. Summons below 2 % on their hosts
NaughtyPresent and EmpressButterfly are guaranteed at 1.667 % on BlueArmoredBonesSword and RockGolem only (below MinFindableChance 2 %), while TruffleWorm and PumpkinMoonMedallion get 7.9 % on
three hosts. Intended for the Empress / Frost Moon summons?

## Fine / checked
- Plantera: bag, 7 weapons (first kill), Temple Key 100 %, pet, trophies. Golem: bag, 8 signature items, Picksaw 25 % (findability 7). Duke Fishron: bag, 5 weapons at 20 %, pet. Empress: Beetle Husk 4-8 at 100 %,
  wings, whips, Empress Blade. Cultist: Ancient Manipulator and Celestial Sigil guaranteed at 100 %. Pillars: fragments 100 % (12-60 normal, 24-100 expert). Moon Lord: Luminite 70-90 at 100 %,
  Ancient Manipulator 100 %, 8 weapons at 22 % each, Portal Gun 100 %, bag kept. Luminite only from Moon Lord (as vanilla).
- Ectoplasm is not in this chunk (DungeonSpirit 100 % in drops-t4-5). Lihzahrd Power Cell 3.4 % (EnchantedSword, GiantFlyingFox), Lihzahrd Furnace 12.5 % + Solar Tablet 4 % on tier 7 wall enemies
  (station quests as designed).
- Items two or more tiers above their owner at >= 1 %: only SuperHealingPotion (locked), DTownsWings 1.2 %, VortexBeater 1.6 %, Arkhalis 1.6 % (tier 8 on tier 6, allowance +1 over). Under 1 %:
  StardustCellStaff 0.6 %, MoonlordTurretStaff 0.43 % (Pumpking), LunarFlareBook 0.48 % (Wyvern): too rare to matter.
- No item repeated more than 3 times per owner except SpookyWood (normal/expert/master variants) and GreenJellyfish SuperHealingPotion (see D3). Owners -20/-21 (Moss Hornet variants) are identical.
- Event drops keep wave conditions; RemixSeed / DontStarve rows are exclusive world conditions, so no impossible condition in a normal playthrough.

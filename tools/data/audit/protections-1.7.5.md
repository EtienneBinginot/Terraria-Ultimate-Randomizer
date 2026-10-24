# Inventaire des protections et restrictions — Ultimate Randomizer 1.7.4

Recherche en lecture seule sur `main` (1b09d02). Chemins relatifs à `D:\Projets\UltimateRandomizer\Common\`.
Verdicts : **KEEP** (à garder), **LOOSEN** (à assouplir), **REMOVE** (à supprimer). « Casse » = ce qui casserait si on retirait la règle.
Point de départ : avec les réglages par défaut, la config ne protège déjà presque rien (ProtectedItems vide, ProtectProgressionMaterials off,
AutoProtectBossSummons off, StationProtection None). Les vraies restrictions sont maintenant **dans le code** (planificateur de recettes, pools,
politiques de catégorie), pas dans la config.

Note de l'utilisateur appliquée partout : un minerai (ou tout autre item « de minage ») ne bloque vraiment que si **le minage est sa seule
source à son tier** (pas de drop, de caisse, d'Extractinator, de boutique, ni de recette randomisée qui le donne). Je signale explicitement
toute **sur-protection** (règle plus large que le vrai blocage).

---

## 1. Items protégés (jamais remplacés, jamais tirés au hasard)

| Règle | Où | Ce que ça couvre | Pourquoi (commentaires) | Casse si retiré | Verdict |
|---|---|---|---|---|---|
| `ProtectedItems` (liste config) | Configs/ProgressionConfig.cs:137, Core/Protection.cs:105 | Vide par défaut (migration v1 a retiré les anciens défauts, :208-251) | Choix du joueur | Rien | KEEP (option du joueur) |
| `ProtectProgressionMaterials` | Protection.cs:17-23, :109 ; défaut false (ProgressionConfig.cs:141) | 20 items : Shadow Scale, Tissue Sample, Hallowed Bar, âmes mécaniques, 4 fragments, Luminite, Ectoplasm, 6 clés de biome, Light/Night Key | Ancienne sécurité ; remplacée par les clés de recette + hard gates | Rien avec off | KEEP comme option (off) |
| `AutoProtectBossSummons` | Protection.cs:116, défaut false | Toutes les invocations vanilla (SortingPriorityBossSpawns, + Guide Voodoo Doll, Prismatic Lacewing) | Off : randomisées + garanties sur ennemis (GuaranteedSources.SummonRequirements) | Rien avec off | KEEP comme option (off) |
| **Invocations moddées toujours protégées** | Protection.cs:116 (`type >= ItemID.Count`) | Toutes les invocations des autres mods | « leurs recettes et drops peuvent avoir des règles que les planners ne connaissent pas » | Une invocation moddée pourrait disparaître ou devenir trop tôt/trop tard (tier estimé) | LOOSEN : les randomiser comme les vanilla quand le tier du boss moddé est connu (NpcTiers.ModdedTier), garanties sur ennemis à `boss tier - 1` ; garder la protection seulement si le boss n'est pas identifié |
| `StationProtection` | Protection.cs:118-136 ; défaut None (ProgressionConfig.cs:149) | Stations de base / progression / thématiques selon le mode | Off : chaque station = petite quête (recette + gardien) | Rien avec None | KEEP comme option |
| **Stations vanilla « Unknown » toujours protégées** | Protection.cs:129 (`kind == Unknown`), Stations.cs:18, :85 | Toute tuile de station vanilla absente de `Stations.Known` (très peu, voire aucune en 1.4.4 : voir le log) | « rien ne les garantirait » | Station sans source → recettes impossibles | KEEP (coût quasi nul) ; un test pourrait vérifier que la liste est vide |
| **Stations moddées de progression toujours protégées** | Protection.cs:129 (`info.Modded && Progression`), Stations.cs:91-103 | Station d'un autre mod requise par ≥10 recettes | Garde ses sources vanilla (best effort) | Une enclume moddée pourrait disparaître si on ne garantit rien | LOOSEN : leur appliquer le même traitement « quête » (recette planifiée + gardien) que les stations vanilla de progression, à leur `ModdedInfo.Tier` |
| `SortedAsSummons` (pas des invocations) | Protection.cs:44-53 | 13 items : Life/Mana Crystal, Life Fruit, miroirs, Cell Phone, Shellphone (+3 modes), conques | Triés avec les invocations sans en être ; jamais garantis comme invocation | Ils seraient garantis/protégés comme des invocations | KEEP (c'est une correction de classement, pas une protection) |
| `Upgrades` / `Special` | Protection.cs:55-60 ; ItemPool.cs:240, :255, :362 ; DropHelper.cs:17 | Life Crystal, Mana Crystal, Life Fruit : jamais dans les tirages aléatoires, jamais remplacés là où ils tombent, une recette propre chacun (`RecipePlanner.SpecialRecipe` :688) | Éviter un spam de cristaux / un monde sans cristaux | Bonus de vie aléatoires en masse (déséquilibre), ou plus de cristaux du tout | LOOSEN léger : autoriser une petite chance qu'un drop « surprise » donne un upgrade de son tier (plafonné, p. ex. 1 sur 500), mais garder les sources d'origine + la recette |
| `WorldSummons` | Protection.cs:28 | Truffle Worm, Prismatic Lacewing, Lihzahrd Power Cell, Queen Slime Crystal | Randomisés + garantis sur ennemis | — (ce n'est pas une protection) | KEEP |
| `ProgressionTools` | Protection.cs:36-39 | Pwnhammer (HM), Shadow Key (post-Skeletron), Temple Key (post-Plantera) | Randomisés, garantis à leur tier | — | KEEP |
| `NeededFrom` (tier plancher des invocations d'événement) | Protection.cs:80-85 | 8 invocations (Goblin Battle Standard, Pirate Map, Snow Globe…) | Sinon elles remplissaient les hôtes du tier 0 | Garanties gaspillées tôt | KEEP |

## 2. Items exclus / bannis

| Règle | Où | Couvre | Pourquoi | Casse | Verdict |
|---|---|---|---|---|---|
| `IsExcluded` : Deprecated, ItemsThatShouldNotBeInInventory | Core/ItemClassifier.cs:121-122 | Items obsolètes ou interdits en inventaire | Pas de vrais items | Crash ou items fantômes | KEEP |
| `IsExcluded` : pickups (cœurs, étoiles, nanites…), pièces | ItemClassifier.cs:123 | Cœurs/étoiles/potions bulles, 4 pièces | Pas de vrais drops ; l'économie | Plus d'argent / de soin en combat (les cœurs sont la régénération de base) | KEEP |
| `IsExcluded` : Defender Medal, Etherian Mana | ItemClassifier.cs:125 | Monnaie et carburant de l'Armée de l'Ancien | Mécanique de l'événement et de la boutique du Bartender | Bartender inutilisable, sentinelles DD2 inutilisables | KEEP |
| `IsExcluded` : questItem | ItemClassifier.cs:126 | ~40 poissons de quête de l'Angler | Poisson de quête = quête de l'Angler | Quêtes impossibles | KEEP |
| `IsExcluded` : UnloadedItem, ExcludedItems/ExcludedMods de la config | ItemClassifier.cs:127-129 | Choix du joueur | — | — | KEEP |
| `BannedVanilla` | ItemClassifier.cs:86-95 | 9 : 3 guides « inactifs », Cultist Bag, 4 modes du Shellphone, Lihzahrd Altar | Inobtenables en vanilla | Items cassés/doublons | KEEP |
| `IsBanned` mods utilitaires / `Creative*` | ItemClassifier.cs:101-110 | Items de ModLoader, RecipeBrowser, Creative Storage Unit… | Triche (stockage créatif = tout) | Exploit | KEEP |
| `IsInternal` | ItemClassifier.cs:115-117 | Items internes de mods (affichages, placeholders) | Sans source et non autoloadés | Items cassés | KEEP |
| `MaterialTiers` null = banni | Core/MaterialTiers.cs:78, :332 | Closed Void Bag, Red Potion | Inobtenables | — | KEEP |
| Catégories `Unusable` (sac, invocation) comme ingrédient/résultat échangé | Features/RecipePlanner.cs:1861, :1877, :606, :2838, :3258 | Tous les sacs (~90 : boss bags, caisses, sacs de cadeaux) et invocations (~30) | « ils viennent du loot, une recette les sauterait » ; un sac comme ingrédient se consomme sans être ouvert | Invocations : boucles (la recette d'une invocation qui demande une autre invocation, garanties faussées). Sacs : surtout ennuyeux | KEEP pour les invocations. LOOSEN pour les sacs : un sac non-boss (caisse, sac cadeau) pourrait être un ingrédient *surprise* rare (c'est de l'aléatoire visible et sans risque de progression, la caisse a une source de pêche) |

## 3. Planificateur de recettes (RecipePlanner / RecipeSystem)

| Règle | Où | Couvre | Pourquoi | Casse | Verdict |
|---|---|---|---|---|---|
| **`Tainted` : minerais des niveaux de minage** | RecipePlanner.cs:1888, `MiningLevels` :3274-3283, `MiningOres` :3343 | Meteorite, Demonite, Crimtane, Hellstone, Cobalt/Palladium, Mythril/Orichalcum, Adamantite/Titanium, Chlorophyte, + Obsidian (≈12 items). Jamais nouvel ingrédient, jamais clé, jamais shell | « une chaîne de pioches pourrait sinon demander son propre bloc » : `MineRequirement` (:1370) et `MiningRequirements` (:3350) lisent les recettes **vanilla**, pas le plan | Une pioche planifiée pourrait demander un minerai qu'elle seule sait miner → softlock du minage | **LOOSEN fortement. SUR-PROTECTION** : (a) le blocage n'est réel que si le minage est la seule source au tier voulu ; beaucoup de ces minerais tombent aussi de caisses, sacs de boss (EoC/BoC : Demonite/Crimtane), de l'Extractinator, de drops permutés. (b) `PickaxeAllowed` (:1333) protège déjà les pioches. Remède : tier effectif d'un minerai = min(tier hors minage, max(tier du minerai, tier où une pioche du niveau est disponible **dans le plan**)), et garantir sans condition une pioche par niveau (`MiningRequirements` : retirer le `if (best <= oreTier) continue`) pour casser le cycle. Ensuite les minerais deviennent des ingrédients ordinaires |
| **`Tainted` : paires de minerais hardmode** | RecipePlanner.cs:1888, OreMap.cs:71-78 | Cobalt/Palladium, Mythril/Orichalcum, Adamantite/Titanium (6) | Présence décidée par les autels pendant le hardmode : le plan (calculé avant) ne doit pas en dépendre | Une recette demandant l'Orichalcum dans un monde Mythril → item jamais fabricable (sauf drop) | LOOSEN : ingrédient sous forme de **groupe de paire** (« Cobalt ou Palladium », un `RecipeGroup` créé par le mod) : le plan ne dépend plus de l'autel. Ou ingrédient tiré seulement parmi les minerais de la paire présente une fois décidée (moins propre : change le plan) |
| **`Tainted` : tout ce qui demande une pioche ≥ 50** | RecipePlanner.cs:1888 (`PickToMine >= 50`), `VanillaTilePick` :3289-3301 | Obsidian, Ebonstone, Crimstone, Pearlstone, briques de donjon (100), briques/pièges Lihzahrd (210), Hellforge… | Même raison (chaîne de pioches) | Idem | LOOSEN (même remède). **SUR-PROTECTION** : un bloc de donjon est aussi bien un butin de coffre ; la Pearlstone se convertit avec la Holy Water… |
| **Propagation de `Tainted` aux objets fabriqués** | RecipePlanner.cs:2155 (`clean`), :2689 (`Made`), :2166 (groupes) | Tout objet dont la meilleure recette passe par un item contaminé n'est jamais nouvel ingrédient (barres de minerai dans les recettes gardées vanilla, objets de recettes LeftAlone…) | « nor anything made of them » | Idem | LOOSEN/REMOVE une fois que le tier effectif du minerai tient compte de la pioche (la propagation devient alors un simple calcul de disponibilité) |
| **`VanillaKey` exclut `Tainted`** | RecipePlanner.cs:3203 | Une barre vanilla (Hellstone Bar, Cobalt Bar…) ne garde jamais son minerai comme clé | Même raison | Idem | LOOSEN : la barre d'un minerai qui *garde* son minerai comme clé est l'usage le plus naturel ; aujourd'hui les audits 1.7.4 (`tools/data/audit/progression-1.7.4/t2-3.md:7`, `t4-5.md:17`) montrent **zéro** recette utilisant un minerai HM/Hellstone/Meteorite → minerais HM inutiles. Garder la clé vanilla avec, p. ex., 50 % de chance |
| `ShellEligible` exclut minerais et Tainted | RecipePlanner.cs:918-921 | Pas de recette « shell » pour un minerai/bloc minable | Se trouvent dans le monde | Rien | KEEP (un minerai n'a pas besoin de recette) |
| `PickaxeAllowed` / `Fits` | RecipePlanner.cs:1331-1337 | Nouvel ingrédient jamais de tier vrai > max(résultat, T) ; pioche jamais faite d'un minerai qui demande sa propre puissance | Cohérence de progression, chaîne de pioches | Hardmode avant l'heure, softlock du minage | KEEP (c'est le vrai garde-fou ; à rendre conscient du plan, voir Tainted) |
| `Eligible` : stations garanties jamais nouveaux ingrédients | RecipePlanner.cs:1882-1895 | Work Bench, Furnace, Anvil, Table, Chair, Sawmill, Loom… + toutes les stations garanties (≈25 items) | Le plan des stations et les gardiens supposent qu'on les trouve comme ça | Une station consommée pour faire une autre station → boucle de layout possible | LOOSEN : autoriser une station de base comme ingrédient (rare, tier ≥ son tier, jamais dans la recette d'une station : le `StationLayout` :1758 l'ordonne déjà). Effet visuel fort (« 1 Loom + … ») |
| `Eligible` : jamais `SortedAsSummon` | RecipePlanner.cs:1883 | Miroirs, téléphones, conques, cristaux | « une recette ne doit pas consommer un Cell Phone » | Juste coûteux | LOOSEN : autoriser Magic/Ice Mirror et conques (ont des sources) ; garder l'interdit pour Cell Phone/Shellphone (chaîne de fabrication longue) et les upgrades |
| `IngredientReplaceable` | RecipePlanner.cs:1877 | Ingrédient vanilla protégé, upgrade, sac ou invocation : jamais remplacé | Ils restent dans la recette | Rien | KEEP |
| **Stations critiques : StartMaterials seulement** | RecipePlanner.cs:178-221, :244-248 | Work Bench (sans minerai), Furnace, Anvil, Table, Chair : seulement Wood, Stone, Dirt, Clay, Sand, Daybloom, Mushroom, Acorn, Cobweb, minerais de départ, Gel/Lens | Les premières minutes : sans établi on ne peut rien faire | Softlock au démarrage | KEEP (vraie protection de démarrage) |
| `StationMaterialTier` : Pumpkin = jamais | RecipePlanner.cs:227-240 | Citrouille (saisonnière), matériaux de biome à leur tier d'ouverture | Saison | Station infaisable hors Halloween | KEEP |
| `Randomized` : recettes « maison » | RecipePlanner.cs:264, RecipeFeature.cs:372-375, :2851 | Wooden Door, Wood Platform, Torch : gardent leur résultat, ingrédients de départ seulement | Maison constructible tout de suite (PNJ) | Pas de PNJ, pas de torches au début | KEEP (Torch au moins) ; LOOSEN possible pour Door/Platform (ont d'autres variantes) |
| `Touchable` : résultat protégé ou exclu garde son résultat | RecipeFeature.cs:367, :380 | Résultats protégés (peu par défaut) | Protection | — | KEEP |
| `LeftAlone` : recettes moddées | RecipeFeature.cs:385-398 | Off si `RandomizeModdedRecipes` off (défaut **on**) ; toujours si condition propre à un mod ou mod exclu | Condition du mod perdue/déplacée | Recette moddée cassée | KEEP (déjà minimal) |
| Recettes à condition de monde | ItemClassifier.cs:925-951 (`WorldConditions`, `SetsTier`) | Ne fixent pas le tier (Crimson/Corrupt/Drunk/Remix/Zenith) | Pas valable dans tous les mondes | Tier faux | KEEP (n'empêche pas la randomisation) |
| `Swappable` : échange de résultat | RecipePlanner.cs:599-608 | Jamais un upgrade, banni, sac, invocation, ni **une station** | Stations : leur recette est planifiée pour leur tier (« stations are never swapped ») | Station donnée par une recette d'un autre tier → layout cassé | KEEP pour les stations ; c'est surtout le **taux** qui limite, voir ci-dessous |
| **`RecipeResultSwapChance` = 10 %** | Configs/CraftingConfig.cs:64-65 | 90 % des recettes gardent leur résultat | Prudence | — | **LOOSEN : 30-50 %** (les paires gardent tout fabricable, intra-tier) |
| Échanges par tier (sauf Chaos) | RecipePlanner.cs:556-560 | Échanges dans le même tier seulement | Progression | Item HM fabricable trop tôt | KEEP (Coherent) |
| `SwapFits` / `VanillaLoops` / `KeptValueFits` / `HostStationsFit` | RecipePlanner.cs:516-533, :612-675 | Pas de 1 Wood → 4 Wood, pas de boucle par ingrédient gardé, pas de fragment gardé pour un item bon marché, stations gardées ≤ tier | Boucles infinies, duplication, cohérence | Exploits de duplication | KEEP |
| `KeepOriginals` + `LoopOff` | RecipePlanner.cs:2395-2470 | Recette non reconstructible : garde ses ingrédients vanilla, remplace ceux qui bouclent ; coupée si boucle irréparable et résultat obtenu ailleurs | Pas de boucle, jamais d'item perdu | Boucles / items perdus | KEEP |
| Jumelles (`Twin`) | RecipePlanner.cs:1659-1708 | Copies identiques d'une recette coupées | Doublons | Rien | KEEP |
| `OneOff` / `OneOffCap` = 3, `SlowIngredient`, `CountCap` | RecipePlanner.cs:1257-1330 | Reliques, items Expert/Master, rares : 3 max ; herbes, poissons, étoiles : 10 max | Recettes pénibles (30 Fireblossoms) | Grind énorme | KEEP (équilibrage, pas protection) |
| `MaxUseShare` 5 % / `MinUseCap` 12 | RecipePlanner.cs:1937-1938 | Un matériau ≤ 5 % des recettes | Variété | Un matériau partout | KEEP |
| Recettes de station : stations thématiques jamais assignées | RecipePlanner.cs:1747-1751, Stations.cs:15 | 14 stations thématiques (Glass Kiln, Bone Welder, Living Loom, Sky Mill…) ne sont jamais données à d'autres recettes ; Demon Altar non plus | Garanties seulement pour leurs propres recettes | Un objet commun demandant une Sky Mill (île céleste) | LOOSEN : les assigner rarement à des recettes de tier ≥ tier de leur placeur (elles sont garanties sur un gardien quand non protégées). L'autel (tuile du monde) : KEEP |
| `StationKeepChance` = 30 % | CraftingConfig.cs:122 | 30 % des recettes gardent leurs stations | Prudence | — | LOOSEN : 15 % |
| Catégories de shell : jamais armes/armures/potions/munitions | RecipePlanner.cs:909-917, RecipeFeature.cs:179-186 | Un objet sans recette (drop seulement) n'obtient une recette que si c'est bloc/meuble/matériau/vanité/teinture/nourriture | « ils viennent du loot » | Rien de cassé, juste moins d'aléatoire | LOOSEN : quelques shells pour équipement (rares, tier verrouillé par une clé : `LockSpecials` le fait déjà pour les upgrades) |
| Surprises d'un résultat empilable : catégories empilables seulement | RecipePlanner.cs:2833-2840 | Une barre ne demande jamais d'équipement par unité | Absurde (« un pantalon par barre ») | — | KEEP |
| `DisableDecraftOfRandomizedResults` | CraftingConfig.cs:57, RecipeFeature.cs:459 | Pas de décraft shimmer des résultats randomisés | Exploit de duplication | Duplication | KEEP |

## 4. Drops (ennemis, boss), DropHelper, permutation

| Règle | Où | Couvre | Pourquoi | Casse | Verdict |
|---|---|---|---|---|---|
| `DropHelper.KeepOriginal` | Features/DropHelper.cs:16-17 | Non randomisable (protégé/exclu : pièces, cœurs…), **sacs**, upgrades | Le contenu du sac est remplacé à la place | Sac de boss remplacé → loot du boss perdu | KEEP pour boss bags ; LOOSEN pour les caisses/sacs non-boss dans les drops ordinaires (un Goodie Bag tombé peut devenir une autre caisse) |
| Politique `SameAsOriginal` (+ `SurpriseChance` 8 %) | DropHelper.cs:45-47, LootConfig.cs:85 | 92 % des remplacements gardent la catégorie | Lisibilité, équilibre | — | LOOSEN : surprise 15-20 % (préférence de l'utilisateur pour le loot visible) |
| `Restrictive` (difficulté, seeds spéciaux, NeverTrue) | Features/DropPermutation.cs:32-46 | Les entrées Expert/Master/seeds ne bougent qu'entre places de même classe | Un item Expert-only reste Expert-only | Items inobtenables en Classic | KEEP |
| **`Seasonal` → entrée gardée vanilla** | DropPermutation.cs:51-56, :92, :98 | Noël, Halloween, vent (cerfs-volants), PlayerNeedsHealing, NamedNPC, IA spécifique, LivingFlames, Pirate Map : la **règle entière** reste vanilla | Un contenu déplacé tomberait au mauvais moment ou jamais | Items saisonniers inobtenables | LOOSEN : permuter ces entrées **entre places de la même condition saisonnière** (comme `Restrictive`, une classe d'accès) au lieu de les garder ; et ne garder vanilla que l'entrée concernée, pas toute la règle |
| Règles non « complètes » jamais enveloppées | DropPermutation.cs:101-124, :704-715 | Règles moddées, objets du corps des slimes, drops par joueur, OneFromRulesRule… | Le rapport ne liste pas tout | Items silencieux perdus | KEEP (technique) |
| Conditions moddées = Restrictive | DropPermutation.cs:82-83 | Entrées avec une condition d'un mod | Inconnue | — | KEEP |
| `IsPermutable` : boss, parties, sbires, PNJ de ville, bestioles, projectiles, inoffensifs exclus de la permutation ennemis | DropPermutation.cs:326-342 | — | Les boss ont la leur (BossPermutation) ; sbires = farm | Farm de loot gratuit | KEEP |
| Place synthétique pour ennemis vanilla seulement | DropPermutation.cs:337-339 | Ennemi moddé sans drop | « souvent un segment de ver ou un sbire » | Farm | KEEP |
| Groupes de tier + `GateBands` | DropPermutation.cs:16, :241-300 | Un contenu ne bouge que dans son tier ; un hard gate jamais plus bas | Progression | Hard gates trop tôt | KEEP |
| Matériaux/minerais/barres jamais « surprises » ; minerais/barres sans plafond de copies | DropPermutation.cs:490-509 | Les matériaux restent où les recettes les attendent | Recettes faisables | Matériaux rares | KEEP pour les matériaux de départ (Gel, Lens) ; LOOSEN pour les autres : ils sont comptés par Findability, donc un matériau surprise serait vu par le planner |
| `PermutationExtraWeights` : Material/OreBar = 0 | LootConfig.cs:34-58 | Les slots extra ne donnent jamais de matériau | « les drops permutés en donnent déjà » | — | KEEP (choix de dosage) |
| Drops globaux non permutés | LootSnapshot.cs:26-28, LootSubstitution.cs:23-24 | Âmes de lumière/nuit, clés de biome, Pirate Map, etc. : en mode Permutation la règle globale tourne telle quelle | Hors tables par ennemi | — | LOOSEN : les âmes et la Pirate Map pourraient entrer dans la permutation (clés de biome : déjà garanties) |
| `IsDeathLoot` | LootSubstitution.cs:70-79 | Bannières, salves du Big Mimic, médailles DD2, potions de soin de boss | Pas des tables de drop | Bannières aléatoires → codex/bestiaire cassé | KEEP |
| Boss absent du monde (Eater/Brain) laissé vanilla | BossPermutation.cs:137-147, :512, :528 | Le boss de l'autre corruption | Personne ne peut l'affronter | Contenu envoyé dans le vide | KEEP |
| Parties de boss / sbires sans drop | MobLootFeature.cs:25, :398-407, :736-748 ; Core/BossParts.cs | Corps du Destroyer, mains, etc. ; sbires jamais loot | Farm | Farm | KEEP |
| `GateAbove` : hard gate / usage unique au-dessus de sa source rejoué | MobLootFeature.cs:411-430 | Temple Key sur Queen Slime, etc. | Le Temple ouvert avant Plantera | Saut de progression | KEEP |
| Pas de bonus géant pour les boss | EnemiesConfig.cs:65-72 (BossIndividualVariation 0, BossGiantChance 0) | Boss jamais géants | Difficulté | — | LOOSEN : 3-5 % de boss géants (stats bornées par `MaxDamageMultiplier`) |

## 5. Sacs, coffres, boutiques, pêche, tuiles, monde

| Règle | Où | Couvre | Pourquoi | Casse | Verdict |
|---|---|---|---|---|---|
| Contenus gardés dans les sacs | BagLootFeature.cs:108-114 | Upgrades/sacs/protégés restent | KeepOriginal | — | KEEP |
| Plafond des coffres `ChestCap` | ChestFeature.cs:191-194, :215 | tier+1, mur du hardmode pour un coffre pré-HM, hard gate ≤ tier du coffre | Pas d'équipement HM au début | Progression cassée | KEEP (les coffres jackpot apportent déjà l'exception) |
| Coffres : `SameAsOriginal`, pas de jackpot ordinaire | ChestFeature.cs:204-212 | — | Les coffres jackpot sont les jackpots | — | KEEP |
| Stations jamais en coffre ni en pot (sauf outils/matériaux de tier 0) | GuaranteedSources.cs:17-20, :398-410 | Stations = quêtes | Le design « quête » | — | KEEP (choix de design) |
| Monnaie spéciale (Bartender DD2, médailles) laissée telle quelle | ShopFeature.cs:32-38 | Boutique du Tavernkeep | Monnaie à part | Médailles achetant n'importe quoi | KEEP ; LOOSEN possible : randomiser parmi les items DD2 entre eux |
| Boutiques `SameAsOriginal`, **sans surprise** | ShopFeature.cs:119-140 | Toutes les boutiques | Lisibilité | — | LOOSEN : chance de surprise (p. ex. 10 %) avec `WithinUnlockedTier` gardé |
| `WithinUnlockedTier` (armes/armures/accessoires/outils) | ShopFeature.cs:168-169 | Jamais au-dessus du tier débloqué | Pas de hamaxe post-ML avant ML | Saut de progression | KEEP |
| Items vendus uniquement en boutique : slot ajouté | ShopPermutation.cs:12-20, :250-298 | Items sans autre source ; pylônes et Red Solution toujours ajoutés | Rien de perdu | Items perdus | KEEP |
| Marchands non fiables exclus (Princess, Skeleton Merchant, Traveling Merchant, Santa) | ShopPermutation.cs:15-17 | Pour les slots ajoutés | Arrivée inconnue | Item perdu | KEEP |
| Pêche : poisson de quête, non-randomisable | FishingFeature.cs:18 | Quêtes de l'Angler | Quête | Quête impossible | KEEP |
| `CratesStayCrates` (défaut on) | LootConfig.cs:221 | Une caisse pêchée reste une caisse | Lisibilité | — | KEEP comme option |
| Minerais minés : permutation **dans le tier**, parmi les minerais présents | Core/OreMap.cs:10-16, :94-151 | Chaque minerai du monde existe encore ; minerais absents restent eux-mêmes | Chaîne de pioches, tous les minerais existent | Minerai inaccessible | KEEP |
| Seuls les minerais et les pots sont randomisés à la casse | TileDropFeature.cs:13, :64-70 | Blocs (cendre, pierre, glace…) jamais | Construction, mobilité | Monde inutilisable pour bâtir | KEEP ; LOOSEN optionnel : gemmes (tuile ExposedGems) et pierres précieuses de biome |
| Herbes : graines et items plaçant une tuile gardés ; `HerbKeepChance` 40 % | WorldSourcesFeature.cs:34, :85 ; LootConfig.cs:242 | Les plantes continuent de pousser | Ne pas éteindre les herbes | Plus d'herbes du tout | KEEP les graines ; LOOSEN chance de garde 40 → 25 % |
| `KeepAcorns` (on) | WorldSourcesFeature.cs:30 | Glands des arbres secoués | Les arbres repoussent | Plus de bois | KEEP |
| Shimmer : seules les transmutations existantes, même catégorie, même tier (jitter 0), ≤ mur du HM, valeur ≤ 3× | ShimmerFeature.cs:45-79 | ~300 transmutations | Pas de machine à argent | Exploit de valeur | LOOSEN : jitter +1 et 10 % de surprise de catégorie ; garder le plafond de valeur et le mur |
| Kit de départ : catégories fixes, bannis rejoués | StartingKitFeature.cs:18-33 | Armes, accessoires, outils, potions au tier 0 | — | — | KEEP |
| Ordre des PNJ : Guide, Truffle, Princess, PNJ secourus gardent le choix vanilla | TownNpcOrderFeature.cs:112 | Leur condition d'arrivée est spéciale | Conditions spéciales | PNJ jamais là | KEEP |

## 6. Échanges d'ennemis et stats

| Règle | Où | Couvre | Pourquoi | Casse | Verdict |
|---|---|---|---|---|---|
| `Excluded` : Dungeon Guardian, Martian Probe, Target Dummy, cultistes, piliers lunaires, Pumpkin/Frost Moon, Mothron | EnemySwapFeature.cs:66-82 | ~60 PNJ | Existent seulement pendant un événement ou cassent ailleurs (Probe → invasion martienne, cultistes → Lunatic) | Invasions déclenchées, événements bloqués (piliers : le bouclier compte les kills) | KEEP pour Probe, cultistes, piliers, Dungeon Guardian ; LOOSEN pour Pumpkin/Frost Moon et Mothron comme **extras** du mode par biome (comme l'éclipse), car les vagues comptent seulement les kills pendant la lune |
| Éclipse : pas d'échange, extras seulement | EnemySwapFeature.cs:84-89 | 14 ennemis | Pas naturels | — | KEEP |
| `ExcludedFromDaySurface` Gnome | :91-92 | Pétrifié au soleil | — | Ennemi inutile | KEEP |
| `CritterExcluded` | :94-102 | Lacewing, Truffle Worm, lave, gemmes, dorés | Invocation, valeur, collection | Invocation perdue, argent | KEEP |
| `ExcludedAiStyles` (12, 13, 28, 29, 47, 55) | :104-105 | Plantes, mains de boss, Hungry, Creepers | Dépendent d'un parent/ancre | Crash ou PNJ figé | KEEP |
| Boss, ShouldBeCountedAsBoss, PNJ de ville, projectiles | :301-304, :311-313 | — | — | Boss qui apparaît partout ; PNJ de ville tués | KEEP |
| Vers / multi-parties, sans bannière, ≤ 5 PV, invulnérables, amicaux | :314-320 | Vers (Devourer, Giant Worm, Bone Serpent…) | Transformation dangereuse (segments) | Segments orphelins | KEEP (technique) |
| `SwapEventEnemies` = false | EnemiesConfig.cs:223-225 | Ennemis d'événement jamais échangés | Prudence | — | LOOSEN : on, sauf invasions scriptées (déjà exclues de `ExtraMovement` :293-298) |
| Stats : PNJ de ville, amicaux, bestioles, projectiles | EnemyStatsFeature.cs:24, :209-218 | — | — | — | KEEP |
| Vers et parties de boss gardent leur taille ; sbires jamais géants/minis ; pas de minis pour les boss | EnemyStatsFeature.cs:216-218, :262 | — | Segments séparés | Trous dans les vers | KEEP |

## 7. Gating de progression (garde-fous généraux, pour mémoire)

| Règle | Où | Verdict |
|---|---|---|
| `TierGating`, `TierAllowance` 1, `PreHardmodeCap` = PostSkeletron, hard gates (Luminite, Chlorophyte, Ectoplasm, âmes mécaniques, Hallowed Bar, Beetle Husk… ItemClassifier.cs:879-883) | ItemPool.cs:428-460 | KEEP : c'est le cœur du « cohérent ». Le Chaos les enlève déjà |
| `PickGates` (Hellstone 65, Chlorophyte 200, Lihzahrd 210) dans les vrais tiers | ItemClassifier.cs:771-787 | KEEP |
| Garanties jamais en dessous de leur tier ; jamais dans le biome qu'elles ouvrent | GuaranteedSources.cs:493, :545, :591 | KEEP |

---

## Top 10 des assouplissements à plus fort impact

1. **Les minerais redeviennent des ingrédients (Tainted).** Aujourd'hui aucun minerai Meteorite/Demonite/Crimtane/Hellstone/HM/Chlorophyte ni l'Obsidienne n'est jamais ingrédient (les audits 1.7.4 trouvent zéro usage : les minerais HM ne servent à rien). C'est de la sur-protection : on bloque tout pour un risque qui n'existe que si le minage est la seule source. Remède : le tier effectif d'un minerai = min(source hors minage, max(tier du minerai, première pioche du niveau dans le plan)), plus une pioche garantie sans condition par niveau (`MiningRequirements`), avec `PickaxeAllowed` gardé. Retirer ensuite la propagation de la contamination aux objets fabriqués (RecipePlanner.cs:1888, :2155, :2689).
2. **Les barres gardent leur minerai comme clé** (`VanillaKey` exclut Tainted, :3203) : Hellstone Bar = Hellstone + …, Cobalt Bar = Cobalt + …, avec une forte chance. C'est le moyen le plus direct de rendre le minage utile.
3. **Les minerais de paires HM passent par un groupe « l'un ou l'autre »** (`RecipeGroup` Cobalt/Palladium, etc.) au lieu d'être bannis : le plan ne dépend plus des autels (OreMap.cs:71-78).
4. **`RecipeResultSwapChance` de 10 % à 30-50 %** (CraftingConfig.cs:64) : avec les paires et le blocage par tier, le risque est nul, et l'aléatoire devient bien visible.
5. **Les entrées saisonnières sont permutées entre elles** au lieu de garder vanilla toute la règle (DropPermutation.cs:51-56, :92-98).
6. **Des surprises dans les boutiques** (aujourd'hui 0 %, ShopFeature.cs:119-140), en gardant `WithinUnlockedTier` ; et une surprise de drop à 15-20 % au lieu de 8 %.
7. **Shimmer plus audacieux** : jitter de tier +1 et 10 % de surprise de catégorie, en gardant le plafond de valeur et le mur du hardmode (ShimmerFeature.cs:56-79).
8. **Stations comme ingrédients et stations thématiques assignables** (RecipePlanner.cs:1882-1895, :1747-1751) : rares, à leur tier, jamais dans la recette d'une station (le layout l'ordonne déjà). `StationKeepChance` de 30 à 15 %.
9. **Invocations et stations de progression moddées traitées comme les vanilla** (Protection.cs:116, :129) quand leur tier est estimé : garanties sur ennemis au lieu d'une protection d'office.
10. **Ennemis d'événement échangeables + quelques boss géants** : `SwapEventEnemies` on (invasions scriptées toujours exclues), Pumpkin/Frost Moon et Mothron comme extras par biome, `BossGiantChance` 3-5 % (EnemiesConfig.cs:71, :224 ; EnemySwapFeature.cs:66-82).

À garder absolument : StartMaterials des stations critiques, maison/torches, `PickaxeAllowed`/`Fits`, hard gates et `GateAbove`, `Restrictive`, `ChestCap`, `WithinUnlockedTier`, `IsExcluded` (pièces, pickups, médailles DD2, poissons de quête), boucles/jumelles/`KeptValueFits`, exclusions techniques d'échange (vers, styles d'IA, boss, PNJ de ville, piliers, cultistes, Probe).

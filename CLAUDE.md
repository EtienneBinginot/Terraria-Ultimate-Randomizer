# Ultimate Randomizer

tModLoader 1.4.4 mod (tModLoader 2026.8.x) that randomizes nearly everything in Terraria: enemy and boss drops, bags,
chests, pots, fishing, ores, trees, herbs, Extractinator, Angler rewards, shops, recipes (results, ingredients,
stations), shimmer, enemy stats (bell curve, giants and minis), enemy spawns, town NPC arrival order, starting kit.
Everything is deterministic per world seed, follows progression tiers, and stays obtainable by default.

## Ground rules
- Talk to the user in **French**. Code, comments, commit messages and English localization in English.
- **Never modify the old mod tRandomizer** (`D:\Projets\tRandomizer`, `ModSources\tRandomizer`, `Mods\tRandomizer.tmod`):
  the user still plays on it. This mod has its own internal name `UltimateRandomizer`.
- The user likes **bold, visible randomness** (real giants, frequent varied loot), always configurable, and everything
  must remain findable according to progression with default settings.
- One commit per phase or fix group; end commit messages with the attribution line given by the harness.
- Large work: plan first (plan mode), ask the open questions, then delegate phases to agents (`.claude/agents/`), and
  verify yourself (`bash tools/verify.sh`) before each commit.
- Handoff of the current work in progress: `C:\Users\etien\.claude\plans\ultimate-randomizer-next.md`.

## Checking your work (no .NET SDK on this machine)
- `bash tools/verify.sh`: Roslyn type-check of the whole mod against the installed tModLoader assemblies, both hjson
  files parse, and every setting/enum/header/test/text key has an en-US and fr-FR entry. Must print `VERIFY: OK`.
- The real build is in game: Workshop > Develop Mods > Build + Reload (the user does it).
- In-game tests: debug panel (key K) > Tests, or `/rtest` (both need Client settings > Debug tools on, off by default). Read the results yourself with `bash tools/test-results.sh`
  (never ask the user to copy them). Pacing tests log `Pacing:`, `Drops:`, `First work bench:` values.
- Vanilla/tML behaviour: `bash tools/api.sh "<Type.FullName>" "<regex>"` (member signatures),
  `bash tools/ildump.sh <Type.FullName> <Method>` / `callers "Type::Method"` (IL from tModLoader.dll), and
  `D:\Jeux\Jeux Steam\steamapps\common\tModLoader\tModLoader.xml` (API docs). Confirm assumptions in IL before relying on them.
- Editing helpers (Python, `tools/py/`): `ed.py` (exact replacements, keeps CRLF), `loc.py` (insert lines in both
  hjson files), `cfgloc.py` (config fields, headers, enum values, whole classes in the `Configs` section).
  Heredocs in the Bash tool can break on some content: write the Python script to a file and run it.
- `tools/` and `CLAUDE.md` are excluded from the mod build (`build.txt` buildIgnore, `.csproj` Compile Remove).

## Architecture (`Common/`)
- `Core/`: pure logic and data.
  - `ItemPool` + `ItemClassifier`: every item classified in a category (`ItemCategory`) and a tier (`Tier`, 0 PreBoss
    to 9 PostMoonLord), protected or excluded. `ItemPool.Roll(RollRequest)` is the single way to pick a random item
    (category policy, weights, tier jitter, jackpot, progression gating, tier window); `RollReplacement` adds the
    "surprise" (any category, loot weights). `Shuffled` = guaranteed shuffle mode.
  - `DetRandom` (+ `RollKey` constants): deterministic randomness from the world seed and stable keys (item/NPC full
    names). Planners must never use `Main.rand`, chest contents or anything not synced.
  - `SeedSystem` (seed, `OnSeedChanged`), `Tier.cs` (`ProgressionSystem.UnlockedTier`), `WorldCaches.Reset` (in
    ItemPool.cs: cleared on seed/config change), `Protection`, `Stations` (station catalog: Basic / Progression /
    Thematic / Unknown), `OreMap`, `StatCurve` (bell curve), `StackRules`, `DropRates`, `NpcTiers`, `BossParts`,
    `LootSnapshot` (original drop tables captured at load), `Obtainability`.
- `Configs/`: server-side `RandomizerConfig` subclasses (+ `ClientConfig`); `WeightTable` base for category weights
  (fields named exactly like `ItemCategory` values).
- `Features/`: one file per randomized system. Notable ones:
  - `MobLootFeature` (`RandomNpcDropRule`: extra drops, common drop, guaranteed drops, loot bonus, giant drop; `Plan()`
    used by journal/debug/findability), `LootSubstitution` (swaps items in `GlobalItem.OnSpawn` by source),
    `DropHelper`, `BagLootFeature`, `ChestFeature`, `TileDropFeature` (ores, pots), `WorldSourcesFeature` (trees,
    herbs, Extractinator, Angler), `FishingFeature`, `ShopFeature`, `ShimmerFeature`.
  - `GuaranteedSources` (+ `Findability`): items that must be findable (boss summons, progression tools, vanilla
    ingredients) placed on the common enemies of one biome of their tier (tools and early materials also in pots); tier
    at which every item is found without crafting. Crafting stations (with randomized recipes) are small quests instead:
    a recipe rebuilt by `RecipePlanner` for their tier (critical ones from start materials only, `StartMaterials`) and
    one guardian enemy (`PlaceGuardian`, `StationGuardianChance`, a tenth once found in the world); never pots nor chests.
  - `RecipeFeature` (`RecipeSystem`: stores originals, applies/restores results, ingredients, stations) and
    `RecipePlanner` (availability fixed point, ingredient and station plans, `StationRecipeTiers`).
  - `EnemyStatsFeature` (type multipliers, `EnemyIndividualNPC`: individual variation, giants/minis, network sync,
    loot bonus), `EnemySwapFeature` (spawn swaps), `PacingModel` (simulated early hour).
- `Tests/`: in-game test suite (`TestRunner`, `Check`), one file per area; `Debug/`: debug panel (K) and tools;
  `Journal/`: discovery journal (J); `Commands/`: `/rseed`, `/rchests`, `/rdebug`, `/rtest`, `/rjournal`.
- `Localization/en-US_*.hjson`, `fr-FR_*.hjson`: tModLoader rewrites (normalizes) them on build; commit them as they are.

## tModLoader pitfalls learned the hard way
- Per-entity globals (`InstancePerEntity`) are recreated by every `SetDefaults`/`Transform` (vanilla calls SetDefaults
  right after `NewNPC` for variants, blood moon conversions...): keep cross-tick state per slot (`EnemySwapSystem.Pending`).
- Don't change an entity's type inside another hook (other mods' globals become stale): swaps happen in `PostUpdateNPCs`.
- `GlobalItem.OnSpawn` sources: drops are `EntitySource_Loot` (also banners, Big Mimic bursts, DD2 medals, boss
  potions: filter them), bags `EntitySource_ItemOpen`, tiles `EntitySource_TileBreak` (the tile still exists during
  `KillTile_DropItems`; pots remove their tile first, hence the `SpawnThingsFromPot` detour), trees `EntitySource_ShakeTree`.
- Changing `Recipe.requiredItem` at run time requires rebuilding tML's private `Recipe.requiredItemQuickLookup`
  (`CreateRequiredItemQuickLookups`, by reflection) and `Recipe.UpdateWhichItemsAreMaterials()`, else the crafting
  menu checks the old ingredients. `requiredTile` and `CraftingRecipeIndices` (shimmer decraft) are read live.
- NPC sync (message 23) sends `life` but **not `lifeMax`**; position is synced before `ReceiveExtraAI`. Individual
  stats are rolled on the server in `OnSpawn` and sent with `SendExtraAI`/`ReceiveExtraAI`.
- `Main.player[255]` exists and is never in a graveyard; vanilla daylight-despawn checks read global time/event state.
- `ModConfig` `[DefaultValue]` is applied by tML, not by `new`: compare defaults by reading the attribute.
- Recipes: other mods' recipes (`Recipe.Mod` not null and not ModLoader; our shells aside) are randomized like vanilla ones
  unless `CraftingConfig.RandomizeModdedRecipes` is off (`RecipeSystem.LeftAlone`; `IsModded` is for stats/logs only). A
  modded recipe sets the tier of a modded result only (`ItemClassifier.IsTierRecipe`, load time, whatever the setting);
  `RecipeGroup` ingredients are kept.
- Other mods' content (generic, best effort): NPC tiers are estimated from stats (`NpcTiers.ModdedTier`: bosses vs the
  vanilla boss fights, enemies vs the vanilla enemies of each spawn stage); their drops and boss bags are permuted with
  everyone's (only vanilla rule types are wrapped); a modded station is Progression (>= 10 recipes: protected, assignable
  from its tier) or Thematic, at its placer's true tier (`Stations.ModdedInfo`). Storage mods' items are loot; utility
  mods (ModLoader, RecipeBrowser), `Creative*` and internal items (`ItemClassifier.IsInternal`) are banned.

## Conventions
- Match the surrounding style: tabs, braces on new lines, short comments explaining *why*, no XML doc spam.
- Every new config field needs `[DefaultValue]` (+ `[Range]`/`[Increment]` for numbers), and a Label + Tooltip in
  en-US and fr-FR (`tools/py/cfgloc.py`); headers and enum values too. The `ConfigTexts` test and `verify.sh` check it.
- Every new in-game test: `TestRunner.Register("Id", Method)` in its area file, registered in `TestRunner.EnsureRegistered`,
  plus a `Tests.Id` line in both hjson files. Tests must not leave side effects (remove spawned NPCs, restore globals),
  must `Check.Skip` when settings make them meaningless, and test planners with explicit parameters, not by changing
  the config.
- Bump `version` in `build.txt` for each delivered batch.

---
name: add-setting
description: Adds or changes a configuration setting of Ultimate Randomizer (field, default, range, header, enum, English and French texts) the way the project expects. Use whenever a feature needs a new option or a default changes.
---

1. Pick the config class by theme (`Common/Configs/`): `GeneralConfig` (mode, seed, exclusions), `ProgressionConfig`
   (gating, protection, guarantees), `LootConfig` (drops, bags, world sources, variety), `EconomyConfig` (shops,
   stacks), `CraftingConfig` (recipes, ingredients, stations, shimmer), `EnemiesConfig` (stats, giants, swaps,
   loot bonus), `ExtrasConfig` (starting kit, town NPCs), `ClientConfig` (client-side only: UI, messages).
   Server configs derive from `RandomizerConfig` (changes trigger `SeedSystem.NotifyConfigChanged` -> caches rebuilt).
2. Declare the field next to related ones, with a short comment, `[DefaultValue(x)]` (floats: `1.5f`), and for numbers
   `[Range(min, max)]` + `[Increment(step)]`; `[DrawTicks]` for enums; `[ReloadRequired]` only when the value is read
   at load (item classification, protection). Add `[Header("Key")]` on the first field of a new section.
   Category weight tables derive from `WeightTable` with int fields named exactly like `ItemCategory` values.
3. Texts, both languages, with a script file run by Python (`import sys; sys.path.insert(0, "tools/py")`):

   ```python
   import cfgloc as C
   C.header("LootConfig", "Variety", "Variety", "Variété")
   C.field("LootConfig", "MyField", "FieldBefore",           # None = at the end of the class
           ("English label", "English tooltip: what it does, what the values mean."),
           ("Libellé français", "Infobulle française."))
   C.enum_value("MyEnum", "Value", ("English", ""), ("Français", ""))
   C.label("LootConfig", "Existing", (..., ...), (..., ...))  # rewrite an existing field's texts
   C.remove_field("LootConfig", "Removed")
   ```
4. If the default affects a pacing target, check `Common/Tests/PacingTests.cs` (`RequireDefaults`) and the targets.
   If it changes what must stay findable, check `GuaranteedSources`/`Findability` and their tests.
5. Changing a field's type or name resets it to the default for existing users: mention it to the user.
6. Run `bash tools/verify.sh` (the `verify` skill).

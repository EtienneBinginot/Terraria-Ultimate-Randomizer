---
name: add-test
description: Writes an in-game automated test for Ultimate Randomizer (registered in the test runner, with English and French names) that can really fail, has no side effects and skips cleanly. Use for every feature or fix, and when a bug was found that no test caught.
---

Tests run inside a loaded world (debug panel K > Tests, or `/rtest`); results go to `client.log`.

1. Put the test in the file of its area (`Common/Tests/`: Core, Loot, World, Config, Enemy, Variety, Source, Pacing,
   Guarantee, Recipe, Stat...). A new file needs a `public static void Register()` called from
   `TestRunner.EnsureRegistered`.
2. Register it: `TestRunner.Register("ShortPascalId", Method);`.
3. Write it with `Check`:
   - `Check.That(condition, "what is wrong")`, `Check.Equal(expected, actual, "what")`;
   - collect many problems in a `List<string>` and end with `Check.All(failures, "Summary")` (shows the first 5);
   - `Check.Skip("reason")` when the settings or the situation make the test meaningless (feature off, multiplayer
     client, event running, non-default settings for value targets).
4. Make it able to fail: assert the real property (for example call `Recipe.FindRecipes()` and inspect
   `Main.availableRecipe`, not the planner's own data). Avoid tautologies (comparing a cache with itself).
5. No side effects: remove NPCs you spawn (`npc.active = false`, clear `EnemySwapSystem.Pending[slot]`), restore any
   global you change (`try/finally`), never change config values; pass explicit parameters to pure functions instead.
   Prefer deterministic `new Random(seed)` over `Main.rand`. Spawning NPCs: single player only, off screen.
6. Value-style tests (pacing, rates) log the measured value with the mod logger so it can be read later.
7. Add the test's name in both hjson files under `Tests` (`tools/py/loc.py`: `add("PreviousTestId:", {"en": [...], "fr": [...]})`),
   one short sentence saying what is guaranteed.
8. Run `bash tools/verify.sh`.

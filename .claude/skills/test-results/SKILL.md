---
name: test-results
description: Reads the result of the last in-game test run of Ultimate Randomizer (and the pacing values, mod errors) from tModLoader's client.log, and turns failures into fixes. Use whenever the user says they ran the tests, played, or reports a problem in game.
---

1. Read the results yourself (never ask the user to paste them):

   ```bash
   bash tools/test-results.sh
   ```

   It prints the mod version that was loaded, the summary line (`N passed, N failed, N skipped`), every non-passed
   test with its detail, the logged values (`Pacing:` equipment per hour, `Drops:` items per kill,
   `First work bench:` minutes), and errors logged by the mod since its last load.
2. Check the loaded version matches the last commit's `build.txt` version. If it is older, the user has not rebuilt:
   say so instead of debugging stale code.
3. For more context (stack traces, the user's session), read
   `D:\Jeux\Jeux Steam\steamapps\common\tModLoader\tModLoader-Logs\client.log` around the relevant lines.
   `TestSkipped` stack traces logged as "silently caught exceptions" are normal.
4. For each failure decide whether the code or the test is wrong (tests can be too strict, depend on settings or
   events, or be blind). Look at the test in `Common/Tests/` and the code it checks before changing either.
5. Pacing targets with default settings: 15 to 25 pieces of equipment per hour, at least 0.6 items per kill, first
   work bench after 15 to 30 minutes. Tune defaults (and say which) when a value is out of range.
6. Report to the user in French: what passed, what failed and why, what you will change.

---
name: verify
description: Checks the Ultimate Randomizer mod offline before a commit or a hand-off - C# compile against the installed tModLoader, hjson validity, and complete en/fr texts for every setting, enum, header, test and text key. Use after any code or localization change.
---

Run from the repository root:

```bash
bash tools/verify.sh
```

It must end with `VERIFY: OK` (exit code 0). Otherwise:
- `RESULT: success=False` / `error CSxxxx` lines: fix the C# errors shown (file(line,col)). An unknown member usually
  means a wrong API name: check it with `bash tools/api.sh "<Type.FullName>" "<regex>"`, never guess.
- `MethodInvocationException ... Hjson`: an hjson file no longer parses (unbalanced braces, bad quotes). Values with
  `:` or leading special characters must be quoted (`"..."`).
- `en-US missing: [...]` / `fr-FR missing: [...]`:
  - `Class.Field` -> add Label + Tooltip with `tools/py/cfgloc.py` (`field`, `header`, `enum_value`), see the
    `add-setting` skill;
  - `Tests.Id` -> add the test's line under `Tests` (see the `add-test` skill);
  - `Debug.X` / `used key X` -> add the text key in both files (`tools/py/loc.py` `add`).

This only proves the mod compiles and is complete. Behaviour is checked in game by the user (Build + Reload, then
tests), read with the `test-results` skill.

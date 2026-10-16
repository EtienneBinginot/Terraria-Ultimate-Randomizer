---
name: deliver
description: Closes a batch of Ultimate Randomizer work - verification, optional adversarial review, version bump, commit, handoff update and a French summary telling the user what to build and test. Use when a planned phase or set of fixes is finished.
---

1. `bash tools/verify.sh` must print `VERIFY: OK`.
2. For any batch with real logic changes, run the `tml-reviewer` agent on the commit range (in the background) and fix
   High/Medium findings before or right after delivering; list the rest in the handoff document.
3. Bump `version` in `build.txt` (minor for features, patch for fixes).
4. Commit (one commit per phase/fix group): English message, a title line and a short body listing the changes, the
   attribution line given by the harness at the end. `tModLoader` may have normalized the hjson files: commit them too.
5. Update `C:\Users\etien\.claude\plans\ultimate-randomizer-next.md` (done / remaining / review results) when it exists.
6. Tell the user, in French and briefly: what changed in game, what to do (Build + Reload, then tests from the K panel
   or `/rtest`), which tests or values matter most, and anything they should know about their current world (for
   example chests already randomized). Then read the results with the `test-results` skill when they come back.

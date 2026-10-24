# Progression audit of one Ultimate Randomizer world (version 1.7.4, default settings, Crimson world, seed 1119453773)

Ultimate Randomizer (Terraria tModLoader mod) randomizes drops, bags, recipes (results, ingredients, stations), shops,
herbs, ores, deterministically per seed. ONE question for you: **in this world, at each tier of your band, can a normal
player obtain everything needed to reach the next tier, in practical amounts?** You hunt for BLOCKERS (softlocks) and big
delays, not for oddities. Randomness itself (a torch made of a herb, a weapon from a slime) is wanted and never a defect.

Folder: C:\Users\etien\AppData\Local\Temp\claude\D--Projets-UltimateRandomizer\eb5302d7-d325-4bc4-8d5d-ad546d3c1fa5\scratchpad\prog\
- `data/`: the raw export (tab separated, one header line; `data/README.txt` explains every column): items.tsv,
  recipes.tsv, drops.tsv, bags.tsv, shops.tsv, findability.tsv, worldsources.tsv, chests.tsv. Use grep/awk/python
  (python with -I) to query them; don't read the big files whole.
- `chunks/`: compact per-tier copies (recipes-t*.tsv, drops-t*.tsv, items-ref.tsv...) if easier to read.
- Mod source (read only, to understand a rule): D:\Projets\UltimateRandomizer\Common\. Never edit anything.
- Vanilla facts: terraria.wiki.gg (WebFetch), e.g. https://terraria.wiki.gg/wiki/Pickaxes, boss pages, ore pages.

## Tiers
0 PreBoss, 1 PostEyeOfCthulhu, 2 PostEvilBoss, 3 PostSkeletron, 4 Hardmode (after Wall of Flesh), 5 PostMechBoss,
6 PostPlantera, 7 PostGolem, 8 PostCultist, 9 PostMoonLord. "never" = not obtainable that way.
- items.tsv `true tier` = the tier the mod assigns; `findability tier` = first tier found WITHOUT crafting (drops >= 2 %
  per kill, bags, world, herbs, guaranteed sources); shops are NOT counted there (check shops.tsv: an item sold from a tier
  is obtainable, mind the NPC's arrival); `planned recipe tier` = earliest tier a recipe (randomized) makes it.
- recipes.tsv `planned ingredients` = `Item xN [tier T, avail A, value V]` (A = availability tier of that ingredient),
  `planned stations`, `planned doable tier`. A recipe needs ALL its ingredients and stations; stations are items too
  (their own recipe / drops / guardian).
- drops.tsv: per enemy/boss (`owner`), `chance` per kill, `conditions`, `kind` (permuted, extra, surprise, guaranteed).
  The export is the "lucky" plan: at run time drops above the unlocked tier + 1 are locked (they wait), so an item listed
  on a low enemy may only drop later. Guaranteed drops are the mod's safety net for must-have items.
- Global vanilla drop rules not listed in drops.tsv still run: Souls of Light/Night (underground Hallow/evil hardmode
  enemies), Pirate Map, Mechanical boss summons' hardmode drop... Biome keys are guaranteed by the mod (drops.tsv).
- Ores: worldsources.tsv has this world's ore map (which ore replaces which); mining needs the pickaxe power of the ORE
  (wiki), the pickaxe comes from recipes/drops (items.tsv `pick power`, `pick to mine`).

## How to work
For each tier of your band, list what the vanilla progression needs (bosses and their summons or spawn conditions,
the pickaxe power to mine the next ores, crafting stations, keys, hard-gate materials) and trace in this world HOW each is
obtained: the concrete source rows (recipe index + its ingredients' sources, enemy + chance, shop + NPC + tier). Follow the
chain down to found materials. Note practical amounts (a recipe needing 3 items each from a 1 % drop is a big delay).
Report only real problems; when you are not sure, put it under Questions with the rows.

## Output
Write ONE markdown file `out/<your band>.md` (create `out/`), English, at most ~200 lines:
```
# Progression <band>
Verdict: BLOCKED / DELAYED / OK, in 2-4 lines.
## Path per tier (short: each requirement -> how it is obtained, with row references)
## Blockers (an item needed to progress that cannot be obtained at its tier: rows, why, suggested fix)
## Big delays (obtainable but 2+ tiers late or only from very rare sources / huge amounts)
## Questions
```
Final reply: the file path and the verdict.

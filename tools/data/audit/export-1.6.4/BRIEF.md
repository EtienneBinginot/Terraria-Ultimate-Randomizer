# Coherence audit of one Ultimate Randomizer world (version 1.6.4, default settings, new world)

Ultimate Randomizer is a Terraria (tModLoader 1.4.4) mod that randomizes drops, bags, recipes, shops, world sources,
deterministically per world seed. You audit ONE chunk of the world export (your chunk file is given in your prompt).
Folder: C:\Users\etien\AppData\Local\Temp\claude\D--Projets-UltimateRandomizer\eb5302d7-d325-4bc4-8d5d-ad546d3c1fa5\scratchpad\audit2\
- `chunks/<your chunk>`: tab separated, one header line. Read it all (in parts if needed).
- `chunks/items-ref.tsv`: every item (id, internal name, French name, category, tier, true tier, findability tier, vanilla
  and planned recipe tier, protected/excluded/banned, mod). Do NOT read it whole: grep it for the items you need.
- `chunks/worldsources-chests.txt`: herbs, pots, ores of this world, chest caps, and the export README (columns).
- Source code (read only, to explain a cause): D:\Projets\UltimateRandomizer\Common\ (Core/, Features/). Never edit anything there.

## Tiers
0 PreBoss, 1 PostEyeOfCthulhu, 2 PostEvilBoss (Eater of Worlds / Brain of Cthulhu), 3 PostSkeletron, 4 Hardmode (after Wall
of Flesh), 5 PostMechBoss, 6 PostPlantera, 7 PostGolem, 8 PostCultist, 9 PostMoonLord. "never" = not obtainable that way.
- item `tier`: the tier the mod assigns to an item (vanilla progression). `true tier`: once its real sources are counted.
- `findability tier`: first tier an item is found WITHOUT crafting (drops >= MinFindableChance 2 % per kill, bags, world,
  herbs, guaranteed sources). Rare drops under 2 % are listed as sources but do not count. Ores: only this world's ores.
- `planned recipe tier` / recipe `planned doable tier`: earliest tier the item / recipe is available (ingredients
  and stations reachable). recipes ingredient notation: `Item xN tT/aA/vV` = item tier T, availability tier A, coin value V.
- Default TierAllowance = 1: a rolled item may be up to 1 tier above its place (planned = true - 1 is BY DESIGN).
  A jackpot (2 % at runtime, not in the export) may give much higher; the export has none.

## What is WANTED (do NOT report these as problems)
The user wants bold, visible randomness: weird recipes (a torch made of a statue), weapons from slimes, furniture from
bosses, random shop stock, items of another category, surprise ingredients, swapped recipe results. Randomness itself is
never a defect. Vanilla being different is never a defect by itself.

## What IS a defect (report these)
1. Softlock / unobtainable: an item needed for progression (boss summons and their ingredients, pickaxes / mining levels,
   crafting stations, keys, hardmode/Plantera/Golem/Cultist gates, materials of key recipes) that can't be obtained at its
   tier in this world, or only much later (2+ tiers), or only from something rare/unreliable.
2. Progression skip: something clearly too strong too early (a recipe / drop / bag / shop slot giving an item 2+ tiers above
   where it is obtained, beyond the allowance), especially weapons, armor, accessories, pickaxes, wings, hard-gate materials
   (Luminite, Souls, Ectoplasm, Chlorophyte, Beetle Husk, Hallowed Bar...).
3. Absurd numbers: stacks or counts making a recipe impractical (e.g. 9999 walls, 30 trapped chests for a torch), prices
   absurd for the tier (a tier-8 item for 1 copper at tier 0, or a tier-0 item for 50 platinum), chances that make a
   frequent item rare or a rare item 1/1 (a weapon at 100 %), recipe values far from the vanilla value (factor > ~5).
4. Inconsistencies inside the data: a recipe whose ingredients/stations are only reachable after its result's tier while
   the result is needed earlier; circular dependencies; a station recipe needing itself; an item whose findability says
   never while sources list it at >= 2 %; same item duplicated many times on one owner; expert vs normal mismatches that lose
   an item; conditions that can never be met in a normal playthrough (e.g. a drop restricted to a world seed).
5. Anything that looks like an export or classification bug (wrong tier for an item compared to vanilla progression:
   check terraria.wiki.gg when unsure, e.g. https://terraria.wiki.gg/wiki/<Item_Name>). Use the wiki only for vanilla facts.

## Method
- Scan systematically; group similar findings (one finding with a table of rows rather than 30 findings).
- For each finding, try to name the cause in the mod (a rule, a planner, a setting) when you can see it, else "unknown".
- Check vanilla facts on the wiki when your conclusion depends on them; cite the page.
- Be precise and conservative: if not sure it is a defect, put it under "Questions" rather than "Defects".

## Output
Write ONE markdown file `out/<your chunk name without .tsv>.md` (create the `out` folder if needed), in English, at most
~250 lines:
```
# Audit <chunk>
Summary: 3-6 lines (what is fine, the main issues).
## Defects (most severe first)
### D1. <short title> — severity: softlock | skip | absurd | inconsistent | bug
Rows: table (id/index, item, key columns) — at most 15 rows, then "and N more".
Why it is a defect (wiki link if used). Suspected cause. Suggested fix.
## Questions (unsure, needs the user's or the developer's decision)
## Fine / checked
```
Your final reply: just the path of the file and the number of defects and questions.

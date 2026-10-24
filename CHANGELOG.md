# Changelog

All notable changes to Ultimate Randomizer. Versions follow `0.MINOR.PATCH` until the first stable release (1.0.0):
a new minor version for a batch of features, a patch version for fixes and tuning.

**Old numbering.** Versions 1.0.0 to 1.9.1 were renumbered 0.10.0 to 0.19.1 (1.N.x became 0.(10+N).x; 1.1.1 to 1.1.3
became 0.11.0 to 0.11.2, since no 1.1.0 existed). The mod went from 0.9.3 to 1.0.0 by mistake; it was never meant as
a stable release. Git tags `v0.x.y` mark the last commit of each version.

## [0.20.1] - 2026-10-24
### Changed
- Jackpot per kill 1.2 % to 0.8 %: the first run measured 2.7 jackpots per early hour, now about 2.2 (one every
  27 minutes).
### Fixed
- A recipe could get one ingredient too many when its station lock was replaced by a key (a Bone Candelabra of 5).

## [0.20.0] - 2026-10-24
### Added
- Kill jackpots: every enemy kill has its own small chance of a jackpot, whatever the drop style (setting "Jackpot
  per kill", 1.2 % by default, less on weak enemies). Aims at about one jackpot every 30 minutes early in the game.
  Before the Wall of Flesh, a kill jackpot rarely crosses into hardmode items.
- Items in jackpot chests get the jackpot effects, sound and chat message when they are first picked up.
### Changed
- Jackpots far above your progression are rarer (tier decay 3 to 4), so that more jackpots do not mean hardmode items
  in the first hour.
- Version numbers start again from 0.x (see "Old numbering" above); full changelog since 0.1.0.
- Workshop description in English, with a separate French version.
### Fixed
- Expert and Master: the codex no longer lists a boss's normal-mode-only drops, which come in its treasure bag instead.
  Nothing was unobtainable: the bag always held the same randomized items.

## [0.19.1] - 2026-10-24
- Fixed: swapped event spawns credited the wrong moon event (moon groups read the wrong way round).
- Magic Storage hint test; group slots in the world export.

## [0.19.0] - 2026-10-24
- Event enemies join the enemy swaps both ways, and swapped event spawns still count for their event.
- Graveyard and Meteor slots are not steady; rare enemies are never swap targets; event spawns share one chance (100 %
  by default).

## [0.18.3] - 2026-10-24
- Guide hints cover Magic Storage: storage network pieces and storage unit upgrades in a Storage tab.

## [0.18.2] - 2026-10-24
- An item found without crafting breaks a station ingredient chain (station ingredients stay rare).

## [0.18.1] - 2026-10-24
- Fixed: station locks whose station comes earlier are keyed; shimmer test skips surprises; station doable tier in the
  export.

## [0.18.0] - 2026-10-24
- Recipe diversity: no result swaps, two ingredients above tier 0, station locks, crafted keys in sparse tiers,
  high-tier materials never come down.
- Generic mining levels, station keys, shimmer stackability, date-bound sources, modded summon tiers.
- High-tier stations are hard gates; vanilla sources keep their high-tier drops; mining needs above every pickaxe stay
  unreachable.

## [0.17.5] - 2026-10-24
- Looser ingredient tiers weighted by tier size; an absent world evil is never a findability source.
- Ores are ingredients again, in ore pair groups; fishing is never a planning source.
- Loosened protections: seasonal drops permuted, shop and shimmer surprises, stations as rare ingredients, modded boss
  summons and stations guaranteed.

## [0.17.4] - 2026-10-24
- Crates mix materials without repeats, Truffle Worm at hardmode, station variants keep their tier.
- Export coherence check script and progression audit reports.

## [0.17.3] - 2026-10-24
- Keys made earlier by station shells are replaced; crafted keys are rare.

## [0.17.2] - 2026-10-24
- Ingredient counts follow the vanilla counts; value only caps them.

## [0.17.1] - 2026-10-24
- Gel and Lens keep every permuted copy; recipe tiers skip excluded items.

## [0.17.0] - 2026-10-24
- Review fixes for tiers, shops, drops and recipes; herbs are slow ingredients.

## [0.16.5] - 2026-10-24
- Forced surprises roll again when they draw their own item.
- Item buckets follow the final true tiers; audited tier fixes; mirrors are not boss summons; herb tables without
  duplicates.
- Small stacks and no early gates in random drops; biome keys guaranteed; strong enemies' tables at their spawn tier.
- Shops: affordable prices, world-wide spread, guaranteed pylons, added slots on time.
- Recipes: count caps for crafted-only and slow ingredients, relaxed value raising, simple house recipes.

## [0.16.4] - 2026-10-24
- Rebuilt station recipes never use only their vanilla ingredient types.
- Permuted copy caps per access class, early copies kept first.

## [0.16.3] - 2026-10-24
- Drop permutation caps the permuted copies of each item; the others become surprises.
- Station guardian test counts only the enemies that may guard a station.

## [0.16.2] - 2026-10-24
- Crafting stations become small quests: a rebuilt recipe for their tier (from start materials for the critical ones)
  and one guardian enemy as a backup; stations found in the world are rarer.

## [0.16.1] - 2026-10-24
- Bestiary and Recipe Browser show only the drops unlocked now.
- Fixed: the Demon Altar counted as never available with Magic Storage.

## [0.16.0] - 2026-10-24
- Fixed the test failures of the previous version: availability fixed point run to the end, raised values within the
  use caps, critters' frequent drop in the permutation style.

## [0.15.2] - 2026-10-23
- Shop-only items get added shop slots only when they are obtainable nowhere else.
- Other mods' content: station adjacency floors, restrictive modded conditions, modded recipes with mod conditions or
  excluded mods left alone, reliable drops.
- Protections cleanup: progression materials, mirrors, world summons, Pwnhammer and Temple Key randomized and
  guaranteed; hard gates never permuted below their tier; absent evil boss left vanilla; mining protection option
  removed; Shadow Key guaranteed, Temple Key never in the Temple.
- Cache invalidation fixes (spawn settings, world evil, Journey difficulty, Magic Storage, shops, shimmer);
  permutation-style critters get the enemies' extra slots.

## [0.15.1] - 2026-10-23
- Other mods' content: randomized recipes, tiers, stations, permuted drops, storage items as loot.
- Shop-only items: tier-aware sources, arrival tiers, hard gates, situational events and multiplayer.
- World export test (TSV) for offline coherence audits.
- Fixed: shop duplicates, rare guaranteed hosts, weak bag hints, short recipe values, bestiary variants.

## [0.15.0] - 2026-10-23
- Item tiers from audits: spawn tiers, reliable sources, hard gates, mining floors.
- Recipes: sane values and stacks, gated protected results, progression-safe ingredients.
- Guaranteed items never placed below their tier; progression bosses host the tiers without natural enemies.
- Shop permutation between merchants (shop style, shop surprise chance); random rolls everywhere, this world's shop-only
  items kept at reliable places.

## [0.14.2] - 2026-10-23
- Enemy drops by permutation (new default style): original drops shuffled between enemies of the same tier and access
  class, plus random extra slots and surprises.
- Boss permutation: boss drops and treasure bag contents shuffled between bosses of the same tier group.
- Bestiary and Recipe Browser show the world's drops (client option).
- Recipe results stay their item, with rare pair swaps and wilder ingredients; rebuilt recipes never come before their
  item's tier.
- Shells never make an item earlier than the plan relies on.
- Shop planner as a pure function, with shop coherence tests.

## [0.14.1] - 2026-10-23
- Recipe review fixes.

## [0.14.0] - 2026-10-23
- Every recipe's ingredients rebuilt from a fuzzy vanilla shape.
- Shell recipes for items no recipe makes.

## [0.13.4] - 2026-10-23
- Code cleanup.
- Fewer jackpots early and never in bags; pots give only unfound guarantees; guaranteed drops shared in multiplayer.
- Hardmode wall, true tiers, banned items, chest caps, jackpot chests, uncapped jackpot tail.
- True tiers from vanilla sources; material tiers table (421 reviewed vanilla materials).
- Locked drops instead of stand-ins: drops and bags use their final plan, locked entries do not drop.
- Recipes: key ingredient of the result's tier, commonness-weighted ingredients, upgrade recipes, house recipes.
- House hints, Guide ghost, Magic Storage kit.

## [0.13.3] - 2026-10-22
- Boss summons offered when their boss is next; hints for later shown greyed and never sold.
- Later hints show when the item itself comes; the Guide's tier on hover.
- Simplifications: one tier-gating formula, shared codex and test helpers.

## [0.13.2] - 2026-10-22
- Codex shows when sources work: enemy times by biome, drop and bag conditions, recipe places.
- The work bench stays in the Forest; late low-rarity materials keep their tier.
- Guaranteed items: most allowed biomes qualify, full hosts taken softly, one chance per item.

## [0.13.1] - 2026-10-22
- Hints say when every source works.
- Guaranteed items never overload a host; varied nearby biomes by tier; start stations in early biomes; jackpot chances
  back to 2 % / 5 %.
- Item tiers follow vanilla recipes: a crafted item is never later than its best recipe.

## [0.13.0] - 2026-10-22
- Guaranteed items spread over the enemies of one biome, by day and by night, at most 4 items per host; hints name
  every host.

## [0.12.5] - 2026-10-22
- Hints: full source tree for recipes, search box, Crafting and Bosses tabs; debug tools off by default.
- Magic Storage rebuilds its recipe caches when the randomized recipes change.
- Jackpots: weak enemies get a share of the chances, critters and spawned children none.

## [0.12.4] - 2026-10-22
- Loot bonus: damage counts twice in an enemy's strength.
- Credits (Pootouf & Nouke) and descriptions ready for publishing.

## [0.12.3] - 2026-10-22
- Enemy damage stays sane: total damage cap, adaptive power goes into health.

## [0.12.2] - 2026-10-17
- Guaranteed items spread over many common enemies, chances from spawn shares.
- Own biome names and descriptions; creature hints name the biome.
- Jackpots show how many tiers ahead they are, rarer and only useful items.
- Giant and mini bonus drops.
- Mod icon at the sizes tModLoader expects.

## [0.12.1] - 2026-10-17
- Guaranteed sources: critical stations first, relative frequency, no lost hosts.
- Guide hints: tabs by progression, found items unlocked, click opens the codex.
- Jackpots a little rarer, always useful, clearly shown.
- Debug panel endgame kit; new mod icon.

## [0.12.0] - 2026-10-17
- Random progression stations and boss summons, guaranteed on frequent enemies.
- Hints lead to one frequent source and reveal the enemy in the codex.

## [0.11.2] - 2026-10-17
- Reset-all button in the mod config; drop chances and spawn shares in the codex.

## [0.11.1] - 2026-10-17
- Hints from the Guide for crafting stations and boss summons.

## [0.11.0] - 2026-10-17
- Bigger biome classes; the absent world evil is shared with the present one.

## [0.10.3] - 2026-10-17
- Longer enemy drop tables, more madness, smooth first opening of the codex.

## [0.10.2] - 2026-10-16
- Codex: no raw text keys, no lag with many discoveries.

## [0.10.1] - 2026-10-16
- Codex: no stutter while open, Escape fix.

## [0.10.0] - 2026-10-16
- Codex interface on J: enemies, items, craft tree, bags, biomes.

## [0.9.3] - 2026-10-16
- Rarer critical station drops: first work bench back to about 20 minutes.

## [0.9.2] - 2026-10-16
- Codex data: kills, drops, sources, recipes and biome sightings.

## [0.9.1] - 2026-10-16
- Enemies redistributed between the biomes.

## [0.9.0] - 2026-10-16
- More loot and madness: loot abundance x3, critter drops, wild tier tail (jackpots).

## [0.8.1] - 2026-10-16
- Loot abundance: more drops on every enemy.

## [0.8.0] - 2026-10-16
- Adaptive power: enemies and bosses scale with the player's gear.

## [0.7.0] - 2026-10-16
- Fewer vanilla recipes: mining unprotected, crafted value, furniture surprise.

## [0.6.3] - 2026-10-16
- Fixes: variants, event hosts, groups, config saves, ore caches.

## [0.6.2] - 2026-10-16
- Ores present in the world, consistent recipe plan, bag findability.

## [0.6.1] - 2026-10-16
- Fixed giant health on multiplayer clients and minion giant drops.

## [0.6.0] - 2026-10-16
- Debug inspector and setting checks.
- Frequent small drops, rarer work benches.
- Fixed: the crafting menu checked the old ingredients; Extractinator ores 1 for 1.

## [0.5.0] - 2026-10-16
- Fun default settings: frequent loot, rarer items reachable, more randomness.
- Loot and ingredient weight tables, surprise rolls, tier window, ore permutation.
- Shaken trees, herbs and plants, Extractinator and Angler rewards randomized.
- Pacing model; basic stations found in the world, guaranteed sources, findability.
- Recipe ingredients and stations randomized, always doable.
- Bell-curve enemy stats, giants and minis, individual variation, loot bonus.

## [0.4.0] - 2026-10-15
- Debug panel (K) and in-game automated test suite.

## [0.3.0] - 2026-10-15
- Review fixes.

## [0.2.0] - 2026-10-15
- Discovery journal, random enemy spawns, town NPC arrival order.

## [0.1.0] - 2026-10-15
- First version: item pool and tiers, enemy loot randomization (vanilla-like drop tables), random shops, recipes and
  shimmer, enemy stats, chests, fishing, ores, pots, starting kit.

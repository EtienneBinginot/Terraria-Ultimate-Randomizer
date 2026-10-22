# Ultimate Randomizer

A complete randomizer for Terraria (tModLoader 1.4.4) that shuffles nearly everything: loot, chests, crafting, shops,
enemies, their stats and where they live. Every world gets its own seed, the randomness follows your progression, and
with the default settings everything you need stays findable.

Originally made for the YouTube videos of **Pootouf & Nouke**: [youtube.com/@pounouke](https://www.youtube.com/@pounouke)

## What gets randomized
**Loot**
- Enemy and boss drops: the original drops are replaced, and every enemy gets extra random drops with vanilla-like rates
  (materials often, equipment rarely), plus a small frequent drop. Critters drop things too. Loot abundance is
  adjustable (x3 by default).
- Treasure bags, fishing crates and other bags, chests of new worlds, pots, fishing, shaken trees, herbs and plants,
  the Extractinator and the Angler's rewards.
- Mined ores are swapped with each other (one ore always gives the same other ore in a world).
- Jackpots: now and then a drop aims a few tiers ahead of your progression, with light and sound.

**Crafting and economy**
- Recipe results, ingredients and crafting stations, planned so that every recipe can still be made with what the world
  offers. Recipes go back to normal when you leave the world.
- Shimmer transmutations, NPC shops (value-based prices), the random starting kit of each new character.
- The arrival order of town NPCs: the Wizard may move in with 50 silver, the Merchant after Skeletron...

**Enemies**
- Spawns redistributed between the biomes: the jungle may be full of skeletons, the snow of hornets.
- Stats on a bell curve: most enemies stay close to normal, a few are much tougher or weaker. Health, defense and size
  vary a lot; damage stays within x3 in total, because defense is subtracted from it in Terraria.
- Giants (up to 4 times bigger, much more health, an extra useful drop) and minis.
- Boss health and damage, fixed for the world.
- Adaptive power: if the randomizer hands you gear far ahead of the world, enemies scale with your equipment (mostly
  their health), so an early lucky weapon stays fun instead of breaking the game.

## Modes
- **Coherent** (default): items keep their category and tier and unlock as you defeat bosses. No Zenith from the first
  slime... unless you get very, very lucky.
- **Guaranteed shuffle**: every item is swapped with another one of the same category and tier.
- **Chaos**: anything from anywhere.

## Always obtainable
Progression items (Pwnhammer, keys, Lihzahrd Power Cell...) are protected. Basic crafting stations, boss summons and the
vanilla ingredients you need are guaranteed on common enemies, in pots and in wooden chests, and every tier of pickaxe
can be found in time. All of this can be changed in the settings.

## Randomizer codex (J key)
Everything you discovered: the drops of the enemies you killed, the content of the bags you opened, the craft tree, the
enemies of each biome. The Guide sells **hints** (Hint button) about crafting stations, pickaxes and boss summons, more
precise at each level, with a search box and separate Crafting and Bosses tabs. When an item is crafted, the hint gives the
whole tree: every ingredient and station, down to the enemy that drops it, the ore to mine or what to gather.

## Settings
Everything is configurable in Mod Configuration, in English and French: modes, seed, drop rates and abundance, surprise
chances (an item becoming something of another kind), tiers allowed ahead of progression, enemy stat ranges, giants,
adaptive power, protections, hint prices... Each page has a button to restore its defaults.

## Multiplayer
The seed is saved with the world and shared with every player; enemy stats, recipes and the codex are synced.

## Commands
- `/rseed` shows the world seed; `/rseed set <number>` and `/rseed random` change it (host only)
- `/rjournal` opens the codex (same as the J key)
- `/rchests confirm` randomizes the chests of a world created before the mod (host only, once)

Debug tools (K panel, `/rdebug`, `/rtest`) are off by default: they reveal the whole randomization. They can be enabled
in the client settings.

Do not enable it together with tRandomizer.

## Credits
Based on [tRandomizer](https://github.com/ImplicitSaber/tRandomizer) by ImplicitSaber (MIT).
Ultimate Randomizer by Pootouf, MIT license.

---
name: tml-lookup
description: Finds how vanilla Terraria / tModLoader really works (API names and signatures, IL of a method, who calls what, entity sources, what is synced or cached) using the repository's offline tools. Use before relying on any vanilla behaviour or API you have not verified in this conversation.
---

From the repository root:

- Members and signatures:
  `bash tools/api.sh "Terraria.ModLoader.GlobalItem" "OnSpawn|Extractinator"`
  (several types separated by commas; nested types with `+`, e.g. `Terraria.ID.NPCID+Sets`; ID constants:
  `bash tools/api.sh "Terraria.ID.ItemID" "WorkBench"`).
- IL of a method (all overloads): `bash tools/ildump.sh Terraria.WorldGen KillTile_DropItems`
- Who calls a method: `bash tools/ildump.sh callers "NPC::NewNPC"` (short type name::method)
- Values of a set blob / constants: `bash tools/ildump.sh blob <field>`, `bash tools/ildump.sh const Terraria.ID.NPCID 13`
- API documentation: grep `D:\Jeux\Jeux Steam\steamapps\common\tModLoader\tModLoader.xml`.

Reading IL tips: `ldarg.0` is `this` for instance methods; `ldsfld Main::x` reads a global; follow `br*` targets
(`IL_xxxx`) to see conditions; `callvirt ...Loader::Hook` shows where tML hooks run relative to vanilla code.

Known facts already checked (see CLAUDE.md "pitfalls"): globals recreated by SetDefaults/Transform, item sources of
drops/bags/tiles/trees, recipe ingredient quick-lookup cache, message 23 does not sync lifeMax.

For a broader question, delegate to the `tml-researcher` agent.

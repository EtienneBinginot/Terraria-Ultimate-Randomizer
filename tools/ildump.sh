#!/bin/bash
# Dumps the IL of vanilla/tModLoader methods from tModLoader.dll (Mono.Cecil), to check what the game really does.
# Usage:
#   bash tools/ildump.sh Terraria.WorldGen ShakeTree          IL of every overload of a method
#   bash tools/ildump.sh callers "NPC::NewNPC"                 methods calling a method (Type::Method, short type name)
#   bash tools/ildump.sh blob <field>                          int values of a <PrivateImplementationDetails> array blob
#   bash tools/ildump.sh const Terraria.ID.ItemID 3817         names of the constants with that value
# Source of the tool: tools/ildump/Program.cs.txt (prebuilt in tools/ildump/out; reads the tModLoader.dll path in the source).
TOOLS="$(cd "$(dirname "$0")" && pwd)"
"$TOOLS/ildump/out/ildump.exe" "$@"

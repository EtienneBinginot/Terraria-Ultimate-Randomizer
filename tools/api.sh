#!/bin/bash
# Lists the public members of tModLoader / Terraria types, with signatures.
# Usage: bash tools/api.sh "Terraria.NPC,Terraria.ModLoader.GlobalNPC" "regex filter"
TOOLS="$(cd "$(dirname "$0")" && pwd)"
pwsh -NoProfile -File "$TOOLS/api.ps1" -Types "$1" -Filter "${2:-}" 2>&1 | grep -v "ValidateType()\|\.Register()\|SetupContent()"

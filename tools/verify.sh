#!/bin/bash
# Full offline check of the mod: C# type-check against the installed tModLoader, both localization files parse,
# and every setting / test / text key has an English and French entry.
# Usage: bash tools/verify.sh        (exit code 0 = everything is fine)
TOOLS="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(dirname "$TOOLS")"
# PowerShell needs Windows paths
command -v cygpath >/dev/null && ROOT="$(cygpath -m "$ROOT")"
TML="${TML_DIR:-D:/Jeux/Jeux Steam/steamapps/common/tModLoader}"
status=0
out=$(pwsh -NoProfile -File "$TOOLS/compile.ps1" 2>&1)
echo "$out" | tail -25
echo "$out" | grep -q "success=True errors=0" || status=1
for l in en-US fr-FR; do
  pwsh -NoProfile -c "\$ErrorActionPreference='Stop'; Add-Type -Path '$TML/Libraries/hjson/3.0.0/lib/netstandard1.3/Hjson.dll'; [void][Hjson.HjsonValue]::Load('$ROOT/Localization/${l}_Mods.UltimateRandomizer.hjson'); 'hjson OK $l'" || status=1
done
python "$TOOLS/py/check_loc.py" || status=1
[ $status -eq 0 ] && echo "VERIFY: OK" || echo "VERIFY: FAILED"
exit $status

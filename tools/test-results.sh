#!/bin/bash
# Shows the result of the last in-game test run (debug panel K > Tests, or /rtest), with the failures in full
# and the values logged by the pacing tests. Reads tModLoader's client.log.
# Usage: bash tools/test-results.sh
TML="${TML_DIR:-D:/Jeux/Jeux Steam/steamapps/common/tModLoader}"
LOG="$TML/tModLoader-Logs/client.log"
[ -f "$LOG" ] || { echo "No client.log at $LOG"; exit 1; }
echo "Mod version loaded: $(grep -o 'UltimateRandomizer (Ultimate Randomizer) v[0-9.]*' "$LOG" | tail -1)"
start=$(grep -n "\[UltimateRandomizer\]: Test results:" "$LOG" | tail -1 | cut -d: -f1)
[ -n "$start" ] || { echo "No test run in this log."; exit 1; }
grep -n "\[UltimateRandomizer\]: \(Pacing\|Drops\|Jackpots\|First work bench\):" "$LOG" | tail -6
# PlanDiff (legacy vs current planning rules): the summary and its examples of the last run
grep -n "\[UltimateRandomizer\]: PlanDiff" "$LOG" | tail -13
# TierCoherence (measurements for the recipe redesign): every line of its last run
tc=$(grep -n "\[UltimateRandomizer\]: TierCoherence: begin" "$LOG" | tail -1 | cut -d: -f1)
[ -n "$tc" ] && sed -n "${tc},\$p" "$LOG" | grep "\[UltimateRandomizer\]: TierCoherence:" | sed 's/^.*\]: TierCoherence:/TierCoherence:/'
# TierAudit (doubtful item tiers, full list in TierAudit.tsv): every line of its last run
ta=$(grep -n "\[UltimateRandomizer\]: TierAudit: [0-9]* vanilla items audited" "$LOG" | tail -1 | cut -d: -f1)
[ -n "$ta" ] && sed -n "${ta},\$p" "$LOG" | grep "\[UltimateRandomizer\]: TierAudit:" | sed 's/^.*\]: TierAudit:/TierAudit:/'
sed -n "${start}p" "$LOG"
sed -n "$((start + 1)),\$p" "$LOG" | awk '/^  \[/{print; next} {exit}' | grep -v "^  \[Passed\]"
echo "--- errors logged by the mod since the last load:"
load=$(grep -n "Lancement du Mod : UltimateRandomizer\|Loading Mod: UltimateRandomizer\|Mod Load: UltimateRandomizer" "$LOG" | tail -1 | cut -d: -f1)
sed -n "${load:-1},\$p" "$LOG" | grep -n "ERROR\|\[UltimateRandomizer\].*\(Exception\|crashed\)" | head -20

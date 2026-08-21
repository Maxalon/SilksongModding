#!/usr/bin/env bash
# Turns a discovery playthrough's BepInEx log into a deduplicated check list.
# BepInEx truncates LogOutput.log on every launch, so each run is already a clean slice.
set -eu

LOG="${1:-/mnt/games/SteamLibrary/steamapps/common/Hollow Knight Silksong/BepInEx/LogOutput.log}"
[ -f "$LOG" ] || { echo "no log at $LOG" >&2; exit 1; }

# Every section is "print a heading, then whatever the filter produced, or (none)".
# Filters are run with pipefail off on purpose: a grep that matches nothing is a normal outcome here.
section() {
  local heading="$1"; shift
  local out
  out="$( "$@" || true )"
  printf '\n=== %s ===\n' "$heading"
  if [ -n "$out" ]; then printf '%s\n' "$out"; else echo "  (none)"; fi
}

# Drops the "[Info   :SilksongModding] " prefix so the payload lines up.
lines() { grep -F "$1" "$LOG" 2>/dev/null | sed 's/^\[[^]]*\] //' || true; }

all_pickups()  { lines "[check] pickup key='" | awk '!seen[$0]++'; }
unstable()     { lines "[check] pickup key='" | grep -F 'hierarchy-fallback-UNSTABLE' | awk '!seen[$0]++'; }
namederived()  { lines "[check] pickup key='" | grep -F 'namederived' | awk '!seen[$0]++'; }
declared()     { lines '[declared]' | awk '!seen[$0]++'; }
drops()        { lines '[drop]' | awk '!seen[$0]++'; }
grants()       { lines '[grant]' | awk '!seen[$0]++'; }
fsm_grants()   { lines '[fsm]' | awk '!seen[$0]++'; }
spawns()       { lines '[spawn]' | sed "s/ iid=[-0-9]*//" | awk '!seen[$0]++'; }
breakables()   { lines '[breakable]' | awk '!seen[$0]++'; }
pooled()       { lines '[pooled]' | awk '!seen[$0]++'; }
hits()         { lines '[hit]' | awk '!seen[$0]++'; }
fsm_spawns()   { lines '[fsmspawn]' | awk '!seen[$0]++'; }

# One key reported by several distinct instance ids. NOT proof of a collision on its own: a scene reload
# creates a fresh instance of the same authored object, so re-entering a room five times produces five ids
# for one pickup. Simultaneity is what makes it a collision, and only a scene dump can show that - see
# summarize-dumps.sh, which counts objects present at one moment. Read this section as "worth checking".
shared_keys() {
  lines "[check] pickup key='" \
    | sed -n "s/.*key='\([^']*\)'.*iid=\([-0-9]*\).*/\1\t\2/p" | sort -u \
    | awk -F'\t' '{n[$1]++} END {for (k in n) if (n[k]>1) printf "  %s  <- %d distinct objects share this key\n", k, n[k]}' \
    | sort
}
swaps()        { lines '[check] swap '; }
warnings()     { grep -E '\[(check|drop|placements)\]' "$LOG" | grep -i 'warn' | sed 's/^\[[^]]*\] //'; }

# A key that appears with two different vanilla items is not a usable identity.
collisions() {
  lines "[check] pickup key='" \
    | sed -n "s/.*key='\([^']*\)'.*vanilla='\([^']*\)'.*/\1\t\2/p" | sort -u \
    | awk -F'\t' '{n[$1]++; v[$1]=v[$1]" "$2} END {for (k in n) if (n[k]>1) printf "  %s ->%s\n", k, v[k]}' \
    | sort
}

paste_ready() {
  lines "[check] pickup key='" \
    | sed -n "s|.*key='\([^']*\)'.*vanilla='\([^']*\)'.*|        { \"\1\", \"\" },  // vanilla: \2|p" \
    | awk '!seen[$0]++'
}

echo "harvested from: $LOG"
section "pickups seen (deduplicated, first-seen order)" all_pickups
section "UNSTABLE keys (hierarchy fallback - never a durable identity)" unstable
section "name-derived keys (fine for a scene-placed object, fatal for a spawned copy - check below)" namederived
section "item locations the scene DECLARES via SavedItemTrackerMarker" declared
section "one key, several instance ids (may be scene reloads - confirm against a dump)" shared_keys
section "key collisions (same key, different vanilla item)" collisions
section "enemy drop systems observed" drops
section "items actually granted, and what granted them" grants
section "FSM-granted items, and the scene object that owns them" fsm_grants
section "what spawned each pickup (loot has no identity of its own)" spawns
section "breakable props broken, and whether they carry a usable key" breakables
section "pooled pickups spawned, and what spawned them" pooled
section "objects the player hit, and whether they carry a usable key" hits
section "FSM spawn actions, and the scene object whose FSM ran them" fsm_spawns
section "swaps actually applied" swaps
section "warnings" warnings
section "paste-ready SlicePlacements entries (fill in the replacement item name)" paste_ready

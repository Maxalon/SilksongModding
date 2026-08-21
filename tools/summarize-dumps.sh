#!/usr/bin/env bash
# Aggregates the per-scene dumps written by SceneDump into a check-candidate overview.
# Dumps are produced by playing with Discovery/DumpScenesOnLoad enabled; this reads them offline.
set -eu

DIR="${1:-/mnt/games/SteamLibrary/steamapps/common/Hollow Knight Silksong/BepInEx/SilksongModding-dumps}"
[ -d "$DIR" ] || { echo "no dumps at $DIR" >&2; exit 1; }

section() {
  local heading="$1"; shift
  local out
  out="$( "$@" || true )"
  printf '\n=== %s ===\n' "$heading"
  if [ -n "$out" ]; then printf '%s\n' "$out"; else echo "  (none)"; fi
}

scenes() {
  for f in "$DIR"/*.txt; do
    [ -e "$f" ] || continue
    printf '  %-28s %s\n' "$(basename "$f" .txt)" "$(grep -c "^object=" "$f" || true) objects of interest"
  done
}

# An item's archetype is decided by which fact line it appears on, so they are counted separately.
by_kind() {
  for kind in pickup declared fsmitem breakable enemydrop pools; do
    printf '  %-10s %s\n' "$kind" "$(cat "$DIR"/*.txt 2>/dev/null | grep -c "^  $kind " || true)"
  done
}

items() {
  cat "$DIR"/*.txt 2>/dev/null \
    | sed -n "s/^  \([a-z]*\) .*item='\([^']*\)'.*/\1\t\2/p" \
    | sort -u | awk -F'\t' '{printf "  %-10s %s\n", $1, $2}'
}

# Objects whose key is derived from their own name. Fine for a scene-placed object, fatal for a runtime
# copy - so these are the ones to eyeball, not to discard.
namederived() {
  grep -h "^object=" "$DIR"/*.txt 2>/dev/null | grep -F "NAMEDERIVED" \
    | sed -n "s/^object='\([^']*\)'.*ownerKey='\([^']*\)'.*/  \2   <- \1/p" | sort -u
}

# A key that two placed objects share is not an identity at all.
duplicate_keys() {
  grep -h "^object=" "$DIR"/*.txt 2>/dev/null \
    | sed -n "s/.*ownerKey='\([^']*\)'.*/\1/p" | grep -v '^<' | sed 's/ NAMEDERIVED//' \
    | sort | uniq -d | sed 's/^/  /'
}

keyless() {
  grep -h "^object=" "$DIR"/*.txt 2>/dev/null | grep -F "ownerKey='<no-persistentbool>'" \
    | sed -n "s/^object='\([^']*\)'.*/  \1/p" | sort -u | head -40
}

echo "dumps from: $DIR"
section "scenes dumped" scenes
section "check candidates by archetype" by_kind
section "distinct items seen, by how they are granted" items
section "name-derived keys (usable if scene-placed, fatal if a spawned copy)" namederived
section "keys shared by two or more objects (not an identity)" duplicate_keys
section "objects with no persistent key at all (first 40)" keyless

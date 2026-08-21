#!/usr/bin/env bash
# Turns the in-game inspector (Cinematic Unity Explorer + CUEP) on and off between launches.
#
# BepInEx loads every DLL under plugins/, and has no per-plugin switch. The explorer's own
# "Hide On Startup" only hides its UI - the ~5MB of assemblies still load and still cost startup time,
# which is the thing that hurts on quick scene-dump runs. So the only real off switch is to move the
# packages out of the plugins tree.
#
# The CinematicUnityExplorer/ working directory is deliberately left in place: it holds Scripts/ (your
# startup.cs), data.cfg and Logs/, and contains no assemblies, so leaving it costs nothing and keeps your
# settings and scripts across toggles.
set -eu

GAME="${SILKSONG_DIR:-/mnt/games/SteamLibrary/steamapps/common/Hollow Knight Silksong}"
ENABLED="$GAME/BepInEx/plugins"
DISABLED="$GAME/BepInEx/plugins-disabled"
PACKAGES="Yukikaco-Cinematic_Unity_Explorer shownyoung-CUEP"

[ -d "$ENABLED" ] || { echo "no plugins folder at $ENABLED" >&2; exit 1; }

status() {
  for package in $PACKAGES; do
    if [ -d "$ENABLED/$package" ]; then
      printf '  %-36s on\n' "$package"
    elif [ -d "$DISABLED/$package" ]; then
      printf '  %-36s off\n' "$package"
    else
      printf '  %-36s missing\n' "$package"
    fi
  done
}

case "${1:-status}" in
  on)
    mkdir -p "$ENABLED"
    for package in $PACKAGES; do
      [ -d "$DISABLED/$package" ] && mv "$DISABLED/$package" "$ENABLED/$package"
    done
    rmdir "$DISABLED" 2>/dev/null || true
    echo "explorer enabled:"; status
    ;;
  off)
    mkdir -p "$DISABLED"
    for package in $PACKAGES; do
      [ -d "$ENABLED/$package" ] && mv "$ENABLED/$package" "$DISABLED/$package"
    done
    echo "explorer disabled:"; status
    ;;
  status)
    echo "explorer packages:"; status
    ;;
  *)
    echo "usage: $0 [on|off|status]" >&2; exit 2
    ;;
esac

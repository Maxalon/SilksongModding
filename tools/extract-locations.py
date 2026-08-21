#!/usr/bin/env python3
"""Extract candidate randomizer checks from Silksong's asset bundles, offline.

Discovering checks by playing costs one session per room and cannot see what never loads. Everything a
check is made of is already sitting in the shipped bundles, so this reads them directly: one pass over
~590 scenes instead of ~590 playthroughs.

What it knows to look for comes from the runtime work in Randomizer/ - the archetypes, the component
fields, and the fact that a blank PersistentBoolItem ID means the game falls back to the GameObject name.
This is the same knowledge, applied statically.

Run:  tools/.venv/bin/python tools/extract-locations.py --out tools/locations.json
"""
from __future__ import annotations

import argparse
import collections
import json
import os
import sys
import warnings

warnings.filterwarnings("ignore")

import UnityPy

UNITY_VERSION = "6000.0.50f1"
DEFAULT_GAME = "/mnt/games/SteamLibrary/steamapps/common/Hollow Knight Silksong"

# Components worth reporting, by the class name recorded in monoscripts.bundle. Resolved to PathIDs at
# runtime rather than hardcoded, so a game update that reshuffles ids is a non-event.
WANTED = [
    "CollectableItemPickup",
    "SavedItemTrackerMarker",
    "PersistentBoolItem",
    "PlayMakerFSM",
    "Breakable",
    "HealthManager",
]


def aa_dir(game: str) -> str:
    return os.path.join(game, "Hollow Knight Silksong_Data", "StreamingAssets", "aa", "StandaloneLinux64")


def load(path: str):
    UnityPy.config.FALLBACK_UNITY_VERSION = UNITY_VERSION
    return UnityPy.load(path)


def build_script_map(aa: str) -> dict[int, str]:
    """MonoScript PathID -> class name. A MonoBehaviour only identifies its type by this pointer."""
    bundle = next(f for f in os.listdir(aa) if f.endswith("_monoscripts.bundle"))
    env = load(os.path.join(aa, bundle))
    out = {}
    for obj in env.objects:
        if obj.type.name != "MonoScript":
            continue
        tree = obj.read_typetree()
        name = tree.get("m_ClassName")
        if name:
            out[obj.path_id] = name
    return out


def build_item_map(aa: str) -> dict[int, str]:
    """Item asset PathID -> asset name, so a pickup's item pointer resolves to something readable.

    Keyed on PathID alone. The pointers also carry a file id, but resolving that means mapping every
    bundle's external references; PathIDs are 64-bit and collisions across the two files involved have not
    been observed. Revisit if an item ever resolves to an obviously wrong name.
    """
    root = os.path.join(aa, "dataassets_assets_assets")
    out = {}
    for entry in sorted(os.listdir(root)):
        env = load(os.path.join(root, entry))
        for obj in env.objects:
            if obj.type.name != "MonoBehaviour":
                continue
            try:
                tree = obj.read_typetree()
            except Exception:
                continue
            name = tree.get("m_Name")
            if name:
                out[obj.path_id] = name
    return out


class Scene:
    """One scene bundle, indexed enough to answer 'what is this object and where does it sit?'."""

    def __init__(self, path: str):
        self.env = load(path)
        self.objects: list = list(self.env.objects)
        self.scene_name = self._read_scene_name(path)
        self.gameobjects: dict[int, dict] = {}
        self.transforms: dict[int, dict] = {}
        self.transform_of: dict[int, int] = {}

        for obj in self.objects:
            if obj.type.name == "GameObject":
                self.gameobjects[obj.path_id] = obj.read_typetree()
            elif obj.type.name == "Transform":
                tree = obj.read_typetree()
                self.transforms[obj.path_id] = tree
                owner = tree.get("m_GameObject", {}).get("m_PathID")
                if owner:
                    self.transform_of[owner] = obj.path_id

    def _read_scene_name(self, path: str) -> str:
        """The real scene name, which bundle filenames do not preserve.

        Bundle stems are lowercased (`tut_01`), but the game keys persistence on the scene name as Unity
        reports it (`Tut_01`), so using the stem would produce keys that never match a save. The
        AssetBundle's container holds the authored asset path, which does preserve case.
        """
        for obj in self.objects:
            if obj.type.name != "AssetBundle":
                continue
            try:
                container = obj.read_typetree().get("m_Container") or []
            except Exception:
                break
            for entry in container:
                asset = entry[0] if isinstance(entry, (list, tuple)) else entry
                if isinstance(asset, str) and asset.lower().endswith(".unity"):
                    return os.path.splitext(os.path.basename(asset))[0]
            break
        return os.path.splitext(os.path.basename(path))[0]

    def name_of(self, go_id: int) -> str:
        return (self.gameobjects.get(go_id) or {}).get("m_Name") or "<unknown>"

    def path_of(self, go_id: int) -> str:
        """Hierarchy path, which is what makes an object identifiable when its name is not unique."""
        parts, seen = [], set()
        current = self.transform_of.get(go_id)
        while current and current not in seen:
            seen.add(current)
            tree = self.transforms.get(current)
            if not tree:
                break
            owner = tree.get("m_GameObject", {}).get("m_PathID")
            parts.append(self.name_of(owner) if owner else "<unknown>")
            current = tree.get("m_Father", {}).get("m_PathID") or None
        return "/".join(reversed(parts)) if parts else self.name_of(go_id)


def pointers(value, found: list[int]) -> None:
    """Collect every PathID in a nested structure. FSM parameters bury object references deeply."""
    if isinstance(value, dict):
        if "m_PathID" in value and "m_FileID" in value:
            if value["m_PathID"]:
                found.append(value["m_PathID"])
            return
        for item in value.values():
            pointers(item, found)
    elif isinstance(value, list):
        for item in value:
            pointers(item, found)


def extract_scene(path: str, scripts: dict[int, str], items: dict[int, str]) -> list[dict]:
    scene = Scene(path)
    scene_name = scene.scene_name
    by_object: dict[int, dict] = {}

    def record(go_id: int) -> dict:
        if go_id not in by_object:
            by_object[go_id] = {
                "scene": scene_name,
                "object": scene.name_of(go_id),
                "path": scene.path_of(go_id),
                "key": None,
                "facts": [],
            }
        return by_object[go_id]

    for obj in scene.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        try:
            tree = obj.read_typetree()
        except Exception:
            continue

        kind = scripts.get(tree.get("m_Script", {}).get("m_PathID"))
        if kind not in WANTED:
            continue
        go_id = tree.get("m_GameObject", {}).get("m_PathID")
        if not go_id:
            continue

        if kind == "CollectableItemPickup":
            ptr = (tree.get("item") or {}).get("m_PathID")
            record(go_id)["facts"].append(
                {"kind": "pickup", "item": items.get(ptr, f"<unresolved:{ptr}>") if ptr else None}
            )

        elif kind == "SavedItemTrackerMarker":
            found: list[int] = []
            pointers(tree.get("items"), found)
            for ptr in found:
                record(go_id)["facts"].append({"kind": "declared", "item": items.get(ptr, f"<unresolved:{ptr}>")})

        elif kind == "PersistentBoolItem":
            data = tree.get("itemData") or {}
            # Blank ID is the common case; the game fills it from the GameObject name at runtime, so the
            # effective key is reconstructed here the same way rather than reported as missing.
            ident = data.get("ID") or scene.name_of(go_id)
            where = data.get("SceneName") or scene_name
            entry = record(go_id)
            entry["key"] = f"{where}:{ident}"
            entry["key_authored"] = bool(data.get("ID"))

        elif kind == "PlayMakerFSM":
            fsm = tree.get("fsm") or {}
            for state in fsm.get("states") or []:
                action_data = state.get("actionData") or {}
                names = action_data.get("actionNames") or []
                if not any("CollectableItem" in n for n in names):
                    continue
                found = []
                pointers(action_data.get("fsmObjectParams"), found)
                pointers(action_data.get("unityObjectParams"), found)
                named = [items[p] for p in found if p in items]
                record(go_id)["facts"].append(
                    {
                        "kind": "fsmitem",
                        "fsm": fsm.get("name"),
                        "state": state.get("name"),
                        "actions": [n.split(".")[-1] for n in names if "CollectableItem" in n],
                        "items": named,
                    }
                )

        elif kind in ("Breakable", "HealthManager"):
            found = []
            pointers(tree.get("itemDropGroups"), found)
            named = [items[p] for p in found if p in items]
            if named:
                record(go_id)["facts"].append(
                    {"kind": "breakable" if kind == "Breakable" else "enemydrop", "items": named}
                )

    return [e for e in by_object.values() if e["facts"]]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-dir", default=DEFAULT_GAME)
    parser.add_argument("--out", default="tools/locations.json")
    parser.add_argument("--scene", action="append", help="scene bundle stem; repeatable. Default: all")
    args = parser.parse_args()

    aa = aa_dir(args.game_dir)
    if not os.path.isdir(aa):
        print(f"no bundles at {aa}", file=sys.stderr)
        return 1

    print("indexing scripts and items ...", file=sys.stderr)
    scripts = build_script_map(aa)
    items = build_item_map(aa)
    print(f"  {len(scripts)} scripts, {len(items)} item assets", file=sys.stderr)

    scenes_dir = os.path.join(aa, "scenes_scenes_scenes")
    stems = sorted(os.path.splitext(f)[0] for f in os.listdir(scenes_dir) if f.endswith(".bundle"))
    if args.scene:
        wanted = {s.lower() for s in args.scene}
        stems = [s for s in stems if s.lower() in wanted]

    results, counts = [], collections.Counter()
    for index, stem in enumerate(stems, 1):
        try:
            found = extract_scene(os.path.join(scenes_dir, stem + ".bundle"), scripts, items)
        except Exception as error:
            print(f"  {stem}: FAILED {type(error).__name__}: {error}", file=sys.stderr)
            continue
        results.extend(found)
        for entry in found:
            for fact in entry["facts"]:
                counts[fact["kind"]] += 1
        if len(stems) > 1 and index % 25 == 0:
            print(f"  {index}/{len(stems)} scenes, {len(results)} objects", file=sys.stderr)

    os.makedirs(os.path.dirname(args.out) or ".", exist_ok=True)
    with open(args.out, "w") as handle:
        json.dump(results, handle, indent=1, sort_keys=True)

    print(f"\nscenes: {len(stems)}   objects of interest: {len(results)}   -> {args.out}")
    for kind, total in counts.most_common():
        print(f"  {kind:12s} {total}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

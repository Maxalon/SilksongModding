#!/usr/bin/env python3
"""Join a hand-authored check list onto the checks extracted from the bundles, by position.

Our extraction knows what every candidate *is* - scene, object, hierarchy path, item asset, persistence
key - but nothing about which ones the community considers real checks, or what it takes to reach them.
A hand-authored randomizer knows both, but identifies a check by a human label with no link to a game
object.

Names do not bridge the two. Their labels ("shell shard cache moss grotto 1") and our object names
("Shell Shard Fossil Large Uni") share almost no tokens, and a token-overlap join produced confident
nonsense - "shell shard cache deep docks 8" matched to "Dock Worker (5)". Worse, their own two files only
agree on 56 of ~390 names, so even they do not have one vocabulary.

Position does bridge it. Their map manifest records a scene and world coordinates per check; we can sum
local positions up the transform chain. On the checks verified by hand these agree to three decimals -
"Beast Shard: Marrowmaw" in Tut_01 at (101.233429, 17.286209) is our Bone Thumper at (101.233, 17.286).
A nearest-neighbour join within a small radius is therefore exact rather than heuristic, and reports its
own distance so a bad match is visible instead of silent.

Run:  tools/.venv/bin/python tools/map-logic.py --manifest CheckMapMarkerManifest.cs
"""
from __future__ import annotations

import argparse
import collections
import json
import math
import re

# World units. Their coordinates are recorded to six decimals and agree with ours to three, so anything
# beyond a fraction of a unit is a different object rather than a rounding difference.
EXACT = 0.5
NEAR = 3.0

MANIFEST_ROW = re.compile(
    r'new MapCheckPosition\(\s*"((?:[^"\\]|\\.)*)"\s*,\s*"([^"]*)"\s*,\s*([-\d.]+)f\s*,\s*([-\d.]+)f'
)


def parse_manifest(path: str) -> list[dict]:
    rows = MANIFEST_ROW.findall(open(path).read())
    return [
        {"name": name.replace('\\"', '"'), "scene": scene, "pos": (float(x), float(y))}
        for name, scene, x, y in rows
    ]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", required=True, help="CheckMapMarkerManifest.cs from the AP randomizer")
    parser.add_argument("--locations", default="tools/locations.json")
    parser.add_argument("--out", default="tools/logic-map.json")
    args = parser.parse_args()

    theirs = parse_manifest(args.manifest)
    ours = json.load(open(args.locations))

    ours_by_scene: dict[str, list] = collections.defaultdict(list)
    for entry in ours:
        if entry.get("pos"):
            ours_by_scene[entry["scene"]].append(entry)

    matched, ambiguous, unmatched = [], [], []
    claimed: set[int] = set()

    for check in theirs:
        candidates = ours_by_scene.get(check["scene"], [])
        scored = sorted(
            (
                (math.dist(check["pos"], tuple(entry["pos"])), entry)
                for entry in candidates
                if entry.get("pos")
            ),
            key=lambda pair: pair[0],
        )
        if not scored or scored[0][0] > NEAR:
            unmatched.append(check)
            continue

        distance, best = scored[0]
        record = {
            "their_name": check["name"],
            "scene": check["scene"],
            "distance": round(distance, 3),
            "our_key": best.get("key"),
            "our_object": best["object"],
            "our_path": best["path"],
            "items": sorted({
                item
                for fact in best["facts"]
                for item in ([fact["item"]] if fact.get("item") else fact.get("items") or [])
                if item
            }),
            "kinds": sorted({fact["kind"] for fact in best["facts"]}),
        }
        if distance <= EXACT:
            claimed.add(id(best))
            matched.append(record)
        else:
            ambiguous.append(record)

    json.dump(
        {"matched": matched, "ambiguous": ambiguous,
         "unmatched_theirs": unmatched,
         "unclaimed_ours": [e for e in ours if id(e) not in claimed and e.get("pos")]},
        open(args.out, "w"), indent=1,
    )

    print(f"their checks: {len(theirs)}    our candidates: {len(ours)}")
    print(f"\nexact  (<= {EXACT}u): {len(matched)}")
    print(f"near   (<= {NEAR}u): {len(ambiguous)}")
    print(f"unmatched:         {len(unmatched)}")

    kinds = collections.Counter(kind for m in matched for kind in m["kinds"])
    print("\nwhat their checks turn out to be, in our terms:")
    for kind, total in kinds.most_common():
        print(f"  {kind:12s} {total}")

    keyed = sum(1 for m in matched if m["our_key"])
    print(f"\nmatched checks that have a persistence key: {keyed}/{len(matched)}")
    print(f"-> {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

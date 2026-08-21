#!/usr/bin/env python3
"""Reduce the extracted candidates to the check database the mod ships.

extract-locations.py casts wide on purpose - 1157 candidates including every enemy that drops anything.
This narrows that to what the runtime can actually place today: scene-placed CollectableItemPickup objects
carrying a persistence key, and shop slots. Both have a proven swap - SetItem with keepPersistence: true
for a pickup, and the ShopItem asset's savedItem field for a shop slot.

Each entry carries the hierarchy path as well as the key. Most keys are unique within their scene, but a
few are not - the game derives a blank PersistentBoolItem ID from the GameObject name, and a scene can hold
two objects with the same name. The runtime matches on key and falls back to path to break those ties,
rather than silently placing an item on the wrong one.

Run:  tools/.venv/bin/python tools/build-check-db.py
"""
from __future__ import annotations

import argparse
import collections
import json


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--locations", default="tools/locations.json")
    parser.add_argument("--out", default="Randomizer/checks.json")
    args = parser.parse_args()

    extracted = json.load(open(args.locations))
    checks = []
    for entry in extracted:
        if not entry.get("key"):
            continue
        for fact in entry["facts"]:
            if fact["kind"] not in ("pickup", "shop") or not fact.get("item"):
                continue
            record = {"kind": fact["kind"]}
            if fact["kind"] == "shop":
                # A shop slot also carries a price, and the range its own shop charges. Randomizing
                # against that range rather than a global one keeps each shop recognisable.
                record["shop"] = fact.get("shop", "")
                record["cost"] = fact.get("cost", 0)
                record["costLow"], record["costHigh"] = fact.get("costRange", [0, 0])
            record.update(
                {
                    # The runtime's CheckId renders as "{kind}:{scene}:{local}", and a placement is looked
                    # up by exactly that string. Emitting the composed form here keeps the two definitions
                    # from drifting apart in a way nothing would catch until a seed silently did nothing.
                    "id": "pickup:" + entry["key"],
                    "key": entry["key"],
                    "path": entry["path"],
                    "scene": entry["scene"],
                    "item": fact["item"],
                }
            )
            checks.append(record)

    checks.sort(key=lambda c: (c["scene"], c["path"], c["key"]))
    shared = {k for k, n in collections.Counter(c["id"] for c in checks).items() if n > 1}
    for check in checks:
        if check["id"] in shared:
            check["ambiguous"] = True

    with open(args.out, "w") as handle:
        json.dump({"formatVersion": 1, "checks": checks}, handle, indent=1, sort_keys=True)

    items = collections.Counter(c["item"] for c in checks)
    print(f"checks: {len(checks)}   scenes: {len({c['scene'] for c in checks})}")
    print(f"keys needing a path tiebreaker: {len(shared)}")
    print(f"distinct vanilla items: {len(items)}")
    print(f"-> {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A BepInEx 5 plugin for Hollow Knight: Silksong, at an early scaffolding stage.

**Long-term goal:** a broad randomizer — not only items, but objects, interactions, scenes and events, all
driven by user-selectable settings. Planned progression:

1. Full item randomizer (first big milestone).
2. Transition randomizer options.
3. Configurable check pools — options for what counts as a check, up to things like "grass rando", where
   every cuttable/destructible grass is its own check.

Randomizer vocabulary used throughout: a **check** (aka location) is any place that can hold a randomized
item; a **placement** maps one check to one item; **logic** is the reachability rules that keep a seed
completable. None of this exists yet.

### Direction decided (2026-07-30)

A Silksong randomizer is already underway upstream, run by the Hollow Knight Randomizer 4 / ItemChanger
team. `ItemChanger.Silksong` (homothetyhk) is mature — 608/621 items, 176/551 locations, daily commits — but
is **not** published to NuGet or Thunderstore (its own `allow-release` is `'false'`), so it must be vendored
or pinned from source. What upstream has *not* built is the randomizer itself: logic data, placement
generation and a settings menu are open issues in their org stub repo.

**The decision: build on `ItemChanger.Silksong` for now, and treat it as replaceable.** This project aims
past what IC covers — object, interaction, scene and event randomization — so IC is expected to be either
merged in or superseded later.

Two consequences that bind day-to-day work:

- **Keep the seam IC-shaped but not IC-dependent.** Placements keyed by name, item granting funnelled
  through one narrow export. If swapping the backend is ever a rewrite rather than a day's work, the
  abstraction has already failed.
- **Licensing — settled, do not re-raise.** `ItemChanger.Silksong`, `ItemChanger.Core` and `RandomizerCore`
  are all LGPL-2.1. The maintainer is indifferent to licensing and happy with copyleft, so **LGPL-2.1 to
  match upstream** is the default answer for this project, and merging upstream source in later is fine.
  The only live obligation is distribution-side and applies even to a free release: ship LGPL assemblies as
  separate DLLs (do not ILMerge them) and include their source or a pointer to it. Intended release channel
  is NexusMods, free.

### Game domain notes (from the maintainer, who has played it — not yet cross-checked against code)

These shape randomizer design and are hard to recover by reading the assembly:

- **Two currencies.** *Rosaries* are the main currency, spent in shops and at "payment stations" such as
  benches and bell stations, some of which must be paid for before they activate. *Shards* are a second
  currency used for crafting — specifically to refill spent uses of red tools.
- **Deconstructable items.** Some world pickups grant a real inventory item that the player later
  *deconstructs* into currency, rather than paying out currency on the spot. A "rosary string" item
  deconstructs into 30 rosaries; a "beast shard" item deconstructs into shards. These therefore flow through
  the item system and are legitimate randomizer checks — but relocating them also relocates the player's
  currency income, which has balance consequences.
- **Collection is often contact-based.** Several pickups (the enemy-dropped beast shard, mossberries) are
  collected by simply running over them, with no interaction prompt; others (the rosary string) use an
  interaction. This differs from Hollow Knight, where pickups required an interact press — so a randomizer
  must hook *every* grant path, and HK prior art cannot be copied blindly here.
- **Currency never gates progression, and is not a logic term.** Both currencies are farmable from the very
  start of the game with no upgrades or progression required — renewable without consuming any check, even
  if farming takes time. The payment-activated stations are conveniences, not progression: benches are
  respawn points plus healing/crafting, and bell stations are fast travel unlocked per station once reached.
  So randomizer logic can treat currency as always eventually available, and shop slots as logically free
  locations once the shop is physically reachable — no affordability modelling needed. By the same token,
  relocating currency-bearing items is a pacing question, not a softlock risk, so they can safely join the
  general item pool.

Useful early-game reference pickups, spanning four distinct archetypes: the beast shard dropped by a large
enemy in the secret area right of the starting spawn (enemy drop, contact pickup); the mossberries in
Mosshome (attack the prop, then contact pickup); the rosary string in Mosshome above the spawn (static world
object, interaction pickup); and the first shop in Bone Bottom (shop slot).

**What is actually implemented today:** an opt-in `RANDOMIZER` title-menu entry, a seed-entry page,
persistence of the chosen seed onto one save slot, and the first slice of check handling — discovery
logging for the `CollectableItemPickup` archetype plus a placement table that swaps a pickup's granted
item (`Randomizer/`). There is still no fill algorithm, no logic, and no settings; the placement table is
hardcoded and currently empty.

The seeded Hornet colour tint and `NewGameDiagnosticsPatch` were placeholders proving that seed data
survives new-game creation and save reload. Both have been removed now that `RandomizerPlacements` loads
off the same two hooks.

Package metadata is still template-default (`thunderstore.toml` says "Example mod description",
`README.md` is one line), and CI's `allow-release` output is hardcoded `'false'`, so nothing publishes.

The maintainer is an experienced programmer but new to game modding, and most of the existing code was
generated by an AI agent. Explain the game-side/Unity/BepInEx reasoning behind patching decisions rather
than assuming it is familiar.

## Build

```bash
dotnet build                 # Debug; also installs into the game and packages for Thunderstore
dotnet build -c Release      # adds PathMap (anonymized debug symbols; breakpoints stop working)
dotnet pack -o nuget/        # NuGet package, so other mods could reference this one
```

`dotnet build` is not just a compile. The `CopyAndPackageMod` target in `SilksongModding.csproj` runs after
**every** build and:

1. copies `SilksongModding.dll` + `.pdb` into `$(SilksongPluginsFolder)/hfellisch-$(AssemblyTitle)/`
   (currently `/mnt/games/SteamLibrary/steamapps/common/Hollow Knight Silksong/BepInEx/plugins/hfellisch-SilksongModding/`),
2. wipes and rebuilds `thunderstore/temp` and `thunderstore/dist`,
3. runs `dotnet tool restore` + `dotnet tcli build`, producing `thunderstore/dist/hfellisch-SilksongModding-<version>.zip`.

So building *is* installing. There is no separate install step, and no way to skip the packaging without
editing the csproj.

`SilksongPath.props` holds the machine-local game path, is gitignored, and is imported only if present so CI
can build without it. Regenerate it with `dotnet new silksongpath`.

**There are no tests and no test project.** Nothing to run for a single test; verification is manual and
in-game (below).

Expect one benign warning locally: `CSC : warning CS9057` — the Harmonize analyzer targets a newer Roslyn
than the installed .NET 8 SDK. CI uses .NET 10.x and does not hit it.

## Verifying a change in-game

There is no automated harness; the loop is build → launch → read the log.

```bash
dotnet build
cd "/mnt/games/SteamLibrary/steamapps/common/Hollow Knight Silksong" && ./run_bepinex.sh
```

The Rider run configuration **"Run Modded Silksong"** (`.run/Run Modded Silksong.run.xml`) does exactly
this: builds the solution, then runs `run_bepinex.sh` in the game directory.

- **Log:** `<game>/BepInEx/LogOutput.log`. Calls through `Logger.Log*` (BaseUnityPlugin) appear as
  `[Info   :SilksongModding]`; bare `Debug.Log` appears as `[Info   : Unity Log]` and is harder to find —
  prefer the plugin logger.
- **Mod save data written by DataManager:**
  `~/.config/unity3d/Team Cherry/Hollow Knight Silksong/<steam-id>/Modded/user<N>/OncePerSave/io.github.hfellisch.silksongmodding.json.dat`
  (`<N>` is the 1-based save-slot index). Vanilla saves are `user<N>.dat` in the `<steam-id>` directory.
  Checking whether that file exists is exactly how `RandomizerSlotPresentation` decides to draw the
  `RANDOMIZED` tag on a profile card.

## Tooling: inspect one object, dump them all

Two tools, answering different questions. Neither replaces the other.

**In-game inspector** — [Cinematic Unity Explorer](https://thunderstore.io/c/hollow-knight-silksong/p/Yukikaco/Cinematic_Unity_Explorer/)
(a Silksong fork of UnityExplorer) plus [CUEP](https://new.thunderstore.io/c/hollow-knight-silksong/p/shownyoung/CUEP/),
installed under `BepInEx/plugins/`. Toggle with **F7**. Mouse Inspect picks whatever is under the cursor and
shows every component, field and property live. The reason it matters here is the **FSM viewer**: inspect a
`PlayMakerFSM` and "Open Fsm Inspector" gives states, events, variables and transition recording. Since the
props in this game are PlayMaker-driven, that is the tool for "what *is* this thing and how does it work?".

**C# console** (inside the explorer) — for one-off questions and live experiments, where a rebuild is
disproportionate. Scripts at `BepInEx/plugins/CinematicUnityExplorer/Scripts/startup.cs` run at launch;
anything else is pasted in. Snippets worth keeping live in `tools/console/`.

Two limits decide what belongs there. The console compiles against the **real** assemblies, so the
publicizer does not apply and every private member needs reflection — the exact members this codebase
otherwise touches for free. And it is an interpreter, so nothing is checked before it runs, which is how
several bugs here were caught. **Console for exploration, plugin for anything systematic**; promote a
snippet into compiled code once it earns its place, and never maintain the same logic in both.

In that console the green **Compile** button is Run; there is no separate one. The dropdown next to it is a
Help menu that *inserts example snippets over the editor contents*, not a mode selector.

**Toggling the inspector** — `tools/explorer.sh on|off|status`. BepInEx loads every DLL under `plugins/`
and has no per-plugin switch, and the explorer's own "Hide On Startup" only hides its UI while still paying
the load cost. The script moves the packages in and out of the plugins tree, leaving the
`CinematicUnityExplorer/` working directory (Scripts, config, logs) in place since it holds no assemblies.
Turn it off for quick dump runs.

**Scene dumper** (`Randomizer/SceneDump.cs`) — for "where are all of them?". On scene load it writes every
item-bearing object to `BepInEx/SilksongModding-dumps/<scene>.txt`, then `tools/summarize-dumps.sh`
aggregates them. Controlled by `Discovery/DumpScenesOnLoad` in
`BepInEx/config/io.github.hfellisch.silksongmodding.cfg`; turn it off for ordinary play.

```bash
# play through the areas you want, then:
tools/summarize-dumps.sh
```

It records five archetypes per object — `pickup`, `declared` (`SavedItemTrackerMarker`), `fsmitem`,
`breakable`, `enemydrop` — alongside the object's persistent key. Three things make it worth more than the
probes it supplements:

- **It sees inactive objects**, which a playthrough by definition cannot.
- **It reads PlayMaker actions statically.** `FsmState.Actions` deserializes on access, so a mossberry's
  item — a serialized `FsmObject` on a `CollectableItemAction` inside one state of one FSM — can be read
  without going anywhere near the bush. That archetype was previously observable only by hitting it.
- **It proves key collisions offline.** Two placed objects reporting one `ownerKey` is a collision, full
  stop; the summarizer flags it. Establishing that at runtime needed several instances to be observed
  spawning.

Caveat, and the reason it is opt-in: reading a machine's states materialises its actions early. PlayMaker
would do the same on entering the state, so it is not a behaviour change, but it is work done ahead of time
on every scene transition.

## The check-discovery loop

Checks are not enumerated by decompiling — they are harvested by playing. `PickupCheckPatch` logs one
`[check] pickup key='...'` line for every `CollectableItemPickup` that runs `Start`, and the
`EnemyDropDiagnostics` probes log which of the three enemy-drop systems produced a given drop.

```bash
dotnet build
cd "/mnt/games/SteamLibrary/steamapps/common/Hollow Knight Silksong" && ./run_bepinex.sh
# play through the area you want checks for, then quit
tools/harvest-checks.sh          # reduces LogOutput.log to a deduplicated check list
```

BepInEx truncates `LogOutput.log` on every launch, so each run is already a clean slice. The harvester
splits the result into sections that each mean something:

- **UNSTABLE keys** — two kinds, both meaning "not a durable identity":
  - `hierarchy-fallback-UNSTABLE`: no `PersistentBoolItem` key and no `PlayerData` flag, so the key is a
    hierarchy path. Names and sibling order are not guaranteed.
  - `persistentbool-namefallback-UNSTABLE`: **a persistent save key that is not actually unique.** This
    one is the dangerous case, because it arrives looking authoritative. `PersistentItem.EnsureSetup`
    fills in a blank ID from the GameObject's name:

    ```csharp
    if (string.IsNullOrEmpty(itemData.ID)) itemData.ID = base.name;
    ```

    So an authored, scene-serialized ID *is* an identity, while a blank one silently becomes the prefab
    name — and every copy spawned from that prefab reports the same key. The first discovery run turned up
    five separate pickups all keyed `pickup:Tut_01:Collectable Item Pickup`. The tell is `data.ID` equalling
    the owning object's name, which is what the probe checks.
- **one key, several distinct objects** — the same collision seen from the log side, via distinct `iid=`
  values. This catches what the vanilla-item check below cannot: several objects sharing a key *and*
  granting the same item. `iid=` (instance id) and `path=` are logged for exactly this reason — a key
  appearing five times is either one pickup seen five times or five pickups sharing a non-identity, and
  only the instance id separates them.
- **key collisions** — one key seen with two different vanilla items, which means the key is not an
  identity and `CheckId` needs another component for that archetype.
- **items actually granted** — from the `[grant]` probe on `CollectableItemManager.AddItem`, the
  chokepoint every grant funnels through, with a filtered call stack naming what granted it. Useless for
  *placing* a check (the location context is gone by then) but the fastest way to identify the archetype
  of a pickup that `PickupCheckPatch` never sees at all.
- **swaps actually applied** — the only proof a placement took effect.

**Log values that can contain spaces are single-quoted** (`key='pickup:Mosstown_01:Rosary String'`).
Item and scene names routinely contain spaces, and an unquoted value in a space-delimited `key=value`
line cannot be parsed back out. Keep new discovery logging in that shape or the harvester silently
truncates it.

### Placement mechanisms — both proven in-game (2026-08-21)

Randomizing an item is a solved problem for both archetypes found so far. Each was verified by swapping a
real pickup live and collecting it.

| Archetype | Where the item lives | How to replace it | Verified by |
| --- | --- | --- | --- |
| Scene-placed `CollectableItemPickup` | serialized `SavedItem` on the component | `SetItem(item, keepPersistence: true)` | the tutorial rosary string granted a Mossberry |
| PlayMaker FSM | serialized `FsmObject` on a `CollectableItemAction` | set `Item.Value` from a prefix on `CollectableItemAction.OnEnter` | the mossberry granted a Rosary_Set_Frayed |

Two details that are load-bearing rather than incidental:

- **`keepPersistence: true` is mandatory.** The default overload runs `Object.Destroy(persistent)`, which
  destroys the very `PersistentBoolItem` the check is keyed on — taking the "already collected" record with
  it, so the pickup would respawn forever.
- **Hook `OnEnter`, do not pre-swap the FSM value.** `OnEnter` reads the item and hands it straight to
  `DoAction`, so a prefix is read by the very next statement. Pre-swapping the serialized value also works
  today, but the berry is a *pooled* object that may be reinitialised; the prefix cannot be undone that way.

What is **not** solved is identity, and that is now the only thing standing between here and a fill
algorithm. See the archetype notes below: a spawned copy inherits its prefab's name and therefore the
authored object's key, so a placement keyed on `Tut_01:Collectable Item Pickup` would also swap every
rosary the statue drops. The discriminator available is "present at scene load", which `SceneDump` records.

Also observed, and deferred: a swapped pickup keeps its **original appearance** — the berry still looked
like a berry while granting a rosary. Matching the visual to the item is a substantial separate piece of
work (Hollow Knight randomizers ship a whole item-appearance layer for it), not a blocker for a first fill.

### Archetypes confirmed so far (two discovery runs, tutorial area)

Both runs covered the same route. What they establish:

| Pickup | Grant path | Keyable? |
| --- | --- | --- |
| Statue rosary drops (`Rosary_Set_Frayed`) | `CollectableItemPickup.DoPickupAction` via an interact coroutine | **No** — five spawned copies, one shared key |
| Beast/monster shard (`Great Shard`) | `CollectableItemPickup.DoPickupInstant` from `Update` (contact) | **No** — `HealthManager.SpawnItemDrop`, no per-drop persistence |
| Mossberry | **PlayMaker FSM** — `CollectableItemCollect.DoAction` ← `CollectableItemAction.OnEnter` | **No** — see below |

Two conclusions that shape the design:

- **`PickupCheckPatch` does not cover the early-game examples.** Mossberries never reach
  `CollectableItemPickup` at all: the item is a serialized `FsmObject` on an FSM action, so randomizing one
  means replacing `Item.Value`, not calling `SetItem`. That is a second placement mechanism, not a second
  key format.
- **Loot cannot be keyed on itself; it must be keyed on its source.** Five statue drops logged five
  distinct instance ids under one persistent ID, all with a root-level hierarchy path — they are prefab
  copies spawned into the scene, and nothing about a copy distinguishes it. The candidate identity is the
  object that *produced* the loot (the breakable prop, the enemy, the FSM owner), which is authored into
  the scene and may carry a real key. `PickupSpawnProbe` (on `SetItem`, where the spawner is still on the
  stack) and `FsmItemGrantProbe` (which logs `FsmStateAction.Owner`) exist to find out.

#### Tut_01, settled by dump (2026-08-21)

The first scene's real check inventory is two objects, and establishing that corrected two earlier claims.

| Object | Item | Key |
| --- | --- | --- |
| `Collectable Item Pickup` | `Rosary_Set_Frayed` | `Tut_01:Collectable Item Pickup` |
| `Bone Thumper` (enemy) | `Great Shard` | `Tut_01:Bone Thumper` |

- **Enemy drops are viable checks after all.** `HealthManager`'s table has no *per-drop* persistence, but
  that is a narrower fact than "farmable": what decides it is whether the **enemy** stays dead, and
  `Bone Thumper` carries a `PersistentBoolItem` (`enemyPersists=True`). So the check keys on the enemy, not
  the drop. The earlier decision to defer enemy drops was based on the wrong fact.
- **The tutorial statue is not a check.** `Shell Shard Fossil Large Uni` is a `BreakableHolder` pooling only
  `Shell Shard 01/02/03`; `Bone Chest` pools only `Geo Small/Med/Large`. Both are pure currency, which is
  farmable and never gates progression, so neither is a location. A `Breakable` flings currency through
  `FlingUtils` entirely separately from `itemDropGroups`, so "drops something" and "holds a check" are
  different claims — the dump records `items=` and `currency=` apart for exactly this reason.
- **The five colliding rosary keys were probably one object across five scene loads.** Only one rosary
  pickup exists in Tut_01 and nothing else there drops rosary strings. Instance ids cannot separate "five at
  once" from "one, five times"; only a dump shows simultaneity. The collision problem is real for genuinely
  spawned loot (`Collectable Item Pickup Instant(Clone)`) but narrower than first written.

Also in Tut_01: roughly 25 `moss_ball_break` / `moss_stalac` breakables with no items, no currency and no
persistence — pure decoration, and precisely the population a "grass rando" option would draw from. They are
already identifiable in the dumps.

#### The mossberry, isolated (one bush, nothing else touched)

```
[fsm] action=CollectableItemCollect item='Mossberry' fsm='Control' state='Collect'
      owner='Mossberry Pickup(Clone)' ownerKey='<no-persistentbool>' keyFrom='-'
      ownerComponents='...,PlayMakerFSM,ObjectBounce,...,PersonalObjectPool,...'
```

The berry is a **pooled prefab copy with no `PersistentBoolItem` anywhere on its chain** and no parent —
the worst case for identity, and worse than the spawned pickups, which at least had a (useless) key. It
also has no `CollectableItemPickup` component at all, so it is invisible to both `PickupCheckPatch` and
`PickupSpawnProbe`. Being pooled, it is not even freshly constructed per pickup.

So the mossberry check must be **the bush**, and the bush appears in no probe so far: it is not a pickup,
and it did not spawn one. Note what this rules out — a `Breakable` carrying `itemDropGroups` spawns a
`CollectableItemPickup` and calls `SetItem`, which `PickupSpawnProbe` would have caught. It logged nothing,
so whatever the bush is, it hands out berries some other way (its own FSM, or `Breakable.onBreak`).

A follow-up run with `BreakableProbe` and `PooledPickupSpawnProbe` active returned **nothing from either**,
on a route that broke a wall and a bush. Both negatives are informative:

- **These props are not `Breakable`.** `Break(float, float, float)` is the sole method of that name, so
  `BreakSelf`/`BreakFromBreaker` funnel through it and the probe cannot have missed a real one. A wall that
  visibly broke produced no line, so the destructible-prop layer here is FSM-driven, not component-driven.
- **The berry is not pooled.** `PersonalObjectPool` sitting on the berry is a pool manager it carries for
  its *own* use — it is not evidence the berry came from a pool, which was a misreading. The berry is
  plainly instantiated.

So the bush is a PlayMaker object, and the probes that should reach it are `HitProbe` (what did the player
slash?) and `FsmSpawnProbe` (which FSM ran a spawn action, and on what object). The latter patches each
spawn action's *declared* `OnEnter` rather than the shared base, and exists specifically to read
`FsmStateAction.Owner` — a stack walk cannot recover that, because a frame names a type, not an instance.
Nine of ten actions are patchable; `SpawnObjectFromGlobalPoolOverTimeV2` has no declared `OnEnter` and is
skipped, so it spawns from an update instead.

Both are broad probes (`HitTaker.Hit` is the funnel for every hit in the game), which is affordable only on
a deliberately minimal route. They are discovery scaffolding, not something to keep on during play.

#### The bush, identified

```
[fsmspawn] action=SpawnObjectFromGlobalPool fsm='Control' state='Break' owner='moss_berry_fruit'
           ownerPath='moss_berry_vine/moss_berry_fruit' ownerKey='Tut_02:moss_berry_fruit NAMEDERIVED'
           ownerComponents='...,PersistentBoolItem,PlayMakerFSM,SavedItemTrackerMarker,...'
```

The bush is `moss_berry_fruit`, and it **does** carry a `PersistentBoolItem`. Its FSM `Control` spawns the
berry on state `Break`; the berry's own FSM grants the item on state `Collect`. `HitProbe` stayed silent, so
these props do not take damage through `HitTaker` at all — they use PlayMaker collision proxies, which is
why the FSM probe is what found it.

**Correction to an earlier reading, and the important one:** a name-derived persistent ID is *not*
automatically unusable. The game persists this bush's own broken state under `Tut_02:moss_berry_fruit`, so
that key has to be unique within the scene or the game itself would misbehave — consistent with the bush
not respawning. Name-derived is fatal only for objects *spawned from a prefab at runtime*, where every copy
inherits one name. So the flag marks a question, not a verdict, and the proof of an actual collision is
several distinct instance ids reporting one key — which the harvester checks separately. The tags now read
`persistentbool-authored` / `persistentbool-namederived`, with `UNSTABLE` reserved for the hierarchy
fallback that never works.

This also means the two archetypes need different treatment for the *same* reason, not different ones:

- **Scene-placed source objects** (the bush) have workable keys today.
- **Their spawned loot** (the berry, the statue's rosaries) never will. The check is the source; the drop is
  just its payload.

#### `SavedItemTrackerMarker` — a possible shortcut past playing the game

The bush also carries `SavedItemTrackerMarker`, a component holding a serialized `SavedItem[]` — the
developers' own annotation of "this object yields these items". Nothing found so far reads it at runtime, so
it may be editor-time metadata, possibly incomplete or stale. But if it is applied consistently it is an
authored check list, and `SceneCheckScan` dumps every marker in a loaded scene (including inactive objects,
which a playthrough would miss) with its identity and items. Comparing marker count against known pickups
per scene is how to find out whether it can be trusted. Treat a marker as a lead, never as proof.

## Architecture

### Plugin lifetime and the static bridge

`SilksongModdingPlugin` (`SilksongModdingPlugin.cs`) applies all Harmony patches in `Awake` and calls
`UnpatchSelf` in `OnDestroy`. It deliberately does **not** use `Harmony.PatchAll`: that is all-or-nothing,
so one patch class whose target method or injected field no longer resolves throws out of `Awake` and
leaves the plugin with zero patches applied — the title menu included, which looks nothing like the actual
cause. `ApplyPatchesIndependently` runs each class through its own `CreateClassProcessor(...).Patch()`
inside a try/catch, so a broken probe is logged by name and skipped. Discovery probes reach deep into game
internals pinned only by the `Silksong.GameLibs` version, so expect individual ones to break on game
updates. Harmony patch classes must be static, so they cannot touch the MonoBehaviour
instance directly. The plugin therefore stores itself in a private static field and exposes `internal static`
bridge methods (`StartManagedCoroutine`, `TryAttachRandomizerData`, `QueueTestTintForCurrentSave`, the
`Log*` helpers). Anything a patch needs from the live plugin goes through one of those.

`[BepInAutoPlugin(id: "io.github.hfellisch.silksongmodding")]` (Hamunii.BepInEx.AutoPlugin, a source
generator) synthesizes the `BepInPlugin` attribute and the `Id`/`Name`/`Version` members from
`AssemblyTitle` and `Version` in `Directory.Build.props` — that is why the class is `partial` and why there
is no visible `BepInPlugin` attribute.

### The randomized-save flow

Five pieces, roughly one per file, chained by the title-menu → setup → slot → in-game path:

| File | Role |
| --- | --- |
| `RandomizerTitleMenu` (in `SilksongModdingPlugin.cs`) | Clones the native START button into a `RANDOMIZER` entry, repositions the menu, rewires keyboard/controller navigation. |
| `RandomizerSetupMenu.cs` | The seed page. Hijacks the native Options screen instead of building a new one. |
| `RandomizerSaveFlow.cs` | Static handoff of one pending seed to exactly one empty save slot. |
| `RandomizerSlotPresentation.cs` | `MonoBehaviour` added to each of the four save-slot buttons: draws the `RANDOMIZED` tag, suppresses slot actions during creation, gates premature clicks. |
| `RandomizerSaveData.cs` | The persisted payload (`FormatVersion`, `Seed`). Generated placements and settings belong here. |

`RandomizerSetupMenu` does not create its own screen. It hides every native `Selectable` and `Text` under
`UIManager.optionsMenuScreen`, parents its own UI there, and reactivates the originals on exit — so its
lifecycle is a set of coroutines that wait on `UIManager` transitions.

`RandomizerSaveFlow` is deliberately inert unless a seed is pending: `TryHandleSlotSelection` returns `true`
(run the original method) when `pendingSeed` is null, so a normal title-menu run is untouched. While a seed
*is* pending, occupied slots are redirected to their clear-save prompt and only an empty slot accepts the
seed, and Back on the slot screen returns to the seed page rather than the title menu — the two screens are
one two-step wizard, so leaving it has to be a deliberate act that also clears the pending seed.

Two things that flow from the game's own transitions rather than from choice:

- `TryHandleProfileBack` patches the `UIGoToMainMenu` **wrapper**, not the `GoToMainMenu` coroutine it
  starts. Suppressing an iterator method from a prefix leaves the caller handing a null enumerator to
  `StartCoroutine`; the void wrapper declines cleanly.
- It tears the profile screen down itself via `HideSaveProfileMenu` before showing the seed page.
  `GoToOptionsMenu` only knows how to fade out the main menu and a few options sub-screens — arriving from
  `SAVE_PROFILES` it matches none of them and would layer the seed page over a still-visible slot screen.

Beware `HideSaveProfileMenu` fading the screen out with `disable: false`: the GameObject stays active at
alpha zero, so "is the profile screen gone?" must be answered from `UIManager.menuState`, never from
`activeInHierarchy`.

### Persistence via Silksong.DataManager

The plugin implements `IOnceSaveDataMod<RandomizerSaveData>`. DataManager writes `OnceSaveData` once, right
after `GameManager.StartNewGame` completes, and restores it into the property whenever that save is loaded.
So the seed must be assigned to `OnceSaveData` **before** `StartNewGame` runs — which is what
`TryAttachRandomizerData` does at slot selection.

Two hooks read it back, covering both entry paths into a game:

- `NewGameDiagnosticsPatch` postfix — a brand-new randomized game.
- `RandomizerSaveLoadPatch` postfix on `GameManager.SetLoadedGameData` — loading an existing save. It carries
  `[HarmonyAfter("org.silksong-modding.datamanager")]` because `OnceSaveData` is not populated until
  DataManager's own patch has run. **Any future load-time hook needs the same ordering.**

`RandomizerSlotPresentation.SaveDataFileName` hardcodes `io.github.hfellisch.silksongmodding.json.dat`,
which is derived from the plugin id — if the id in `[BepInAutoPlugin]` ever changes, that constant must
change with it.

## Working with game code

Game assemblies come from the `Silksong.GameLibs` NuGet package, pinned to a game build via its version
suffix (`1.2.0-silksong1.0.30000`). The reference DLLs live at
`~/.nuget/packages/silksong.gamelibs/<version>/ref/netstandard2.1/` — `Assembly-CSharp.dll` holds ~7,300
game types. They are stripped and **publicized**, so most otherwise-private game members are directly
callable at compile time; decompile these DLLs (ILSpy, Rider's decompiler) to find hook points. Bumping the
game version means bumping this package reference.

**Namespace trap:** Team Cherry's own menu classes live *inside* the `UnityEngine.UI` namespace, next to
real Unity types — `MenuButton`, `SaveSlotButton`, `ClearSaveButton`, `RestoreSaveButton`, `MenuSelectable`,
`PauseMenuButton`, and more. Other game types (`GameManager`, `UIManager`, `HeroController`,
`MainMenuOptions`, `SaveGameData`) sit in the global namespace, and `GlobalEnums` is a namespace, not a
class. That is why the code writes `global::UnityEngine.UI.SaveSlotButton` and `global::GameManager`
everywhere; keep that qualification when adding patches, it is what keeps the game's `SaveSlotButton`
distinguishable from anything Unity ships.

Patch non-public or overloaded methods by string name plus explicit parameter types, as
`RandomizerSaveLoadPatch` does for `SetLoadedGameData`.

The `Harmonize` package supplies analyzers for Harmony patch signatures (injection parameter names, patch
shape) — heed its warnings, they catch runtime-only patch failures at compile time.

## UI patching conventions established here

These encode failures already hit; keep following them:

- Cloning a native `MenuButton` also clones its persistent `UnityEvent` and any other `IEventSystemHandler`
  components. Replace `OnSubmitPressed` with a fresh `UnityEvent` **and** destroy the inherited handlers
  (`RemoveInheritedSubmitHandlers`), or a cloned button will still start a normal game.
- Destroy `AutoLocalizeTextUI` on cloned buttons before setting a label, or localization overwrites it.
- Build replacement UI while the target screen is still hidden, and restore native content only after the
  screen has fully deactivated (`while (ui.optionsMenuScreen.gameObject.activeInHierarchy) yield return null`).
  Doing either one frame early causes a visible flash of the real screen.
- Pointer clicks call `SaveSlotButton.OnSubmit` while `UIManager` is still animating the cards in.
  `RandomizerSlotPresentation.IsReadyForInteraction` gates on the card actually being visible; reuse it
  rather than assuming a click is legitimate.
- Use the Unity `Object` truthiness operator (`if (!button)`) rather than `!= null` for anything that may
  have been destroyed.

### Controller navigation is a separate wiring from clicking — always check both

Silksong is a controller-first game, and a mouse click and a controller press reach a button by completely
different routes. A click dispatches straight to whatever is under the pointer, so a button only has to
*exist* to be clickable. A controller or the keyboard walks Unity's `Navigation` graph outward from the
currently selected object, so an entry nothing points at is unreachable however plainly it is drawn. **A
menu entry verified only with a mouse has not been verified.** This is not hypothetical — the `RANDOMIZER`
title entry shipped unreachable on a controller for exactly this reason.

`MenuNavigation.cs` holds the shared helpers. Which one applies depends on who owns the screen:

- **Screens the game owns** (title menu, options, pause): navigation belongs to a `MenuButtonList`.
  `SetupActive()` rebuilds `selectOnUp`/`selectOnDown` for all of its `entries` into a wrap-around ring,
  so anything written onto a `Selectable` from outside is discarded at the next rebuild. Register the
  entry in the list instead — `MenuNavigation.TryInsertAfter`. Joining also earns the entry the list's
  `cancelAction` assignment and last-selected tracking, so it behaves like a native one.
  - **Then force a rebuild.** `UIManager.ShowMenu` calls `SetupActive()` on each show, but the title
    screen never goes through it — `mainMenuScreen` is a `CanvasGroup` faded in by hand, not a
    `MenuScreen`. There, `SetupActive()` runs only from the list's `Start()` (once, at scene load) or from
    `OnEnable()` when `isDirty`. An entry added after that sits in `entries` and never enters the ring.
- **Screens built from scratch** (the seed page): there is no list to join, so wire the ring by hand with
  `MenuNavigation.WireVerticalRing`, which reproduces `SetupActive`'s wrap-around shape.

Two traps specific to cloned buttons:

- A clone inherits `cancelAction` from its source. Cloning the title screen's START button carries
  `GoToExitPrompt`, so Back on a page of ours would raise the quit prompt — and the built-in cancel
  actions call `UIManager` navigation directly, skipping a hijacked screen's teardown and leaving the
  native content hidden. Use `MenuNavigation.SetCancelHandler`, which sets `cancelAction = DoNothing` and
  installs an `EventTrigger` Cancel entry (the extension point `MenuSelectable.hasCancelEventTrigger`
  exists for). Both handlers fire, which is why the built-in one must be neutralised, not just ignored.
  **Never reuse an `EventTrigger` already on a clone.** Stripping inherited handlers disables and destroys
  them, and both halves bite: `Object.Destroy` is deferred to end of frame so `GetComponent` still returns
  the component, and `ExecuteEvents` filters recipients on `isActiveAndEnabled` so a disabled one never
  receives the event. Attaching the Cancel entry to that corpse fails in a way that looks like a
  navigation bug — `OnCancel` still runs its `ForceDeselect` (because `hasCancelEventTrigger` was set), so
  the cursor is cleared while nothing navigates. `SetCancelHandler` destroys any existing one immediately
  and adds a fresh one.
- A clone also inherits `selectOnLeft`/`selectOnRight`, which may point at controls on a different,
  hidden screen. `WireVerticalRing` clears them.

A third trap, and the one most likely to strand a player: **`MenuButton.OnSubmit` clears the selection.**

```csharp
if (buttonType != MenuButtonType.Activate) { ForceDeselect(); }
```

`ForceDeselect` sets the selection to null *and* sets `deselectWasForced`, which suppresses the game's own
`ValidateDeselect` restore. For a button that hands off to another screen that is correct — the next
screen selects something itself. For a button that acts **in place**, it leaves nothing selected, and a
controller navigates outward from the selected object, so there is no direction to move and no way to
recover: the screen is dead until the mouse is used. Cloned buttons inherit `Proceed`, so:

- Set `buttonType = MenuButtonType.Activate` on any button that stays on its own page.
- On any early-return path where a `Proceed` button does *not* navigate (a validation failure, say),
  re-select something explicitly — the deselect has already happened by the time `OnSubmitPressed` runs.

The same trap bites from the other direction: **a Harmony prefix that returns `false` to suppress a native
`OnSubmit` also suppresses that method's `ForceDeselect`** — and other UI may be depending on it. Silksong's
in-slot prompts claim the cursor through `PreselectOption.HighlightDefault`, which is called with
`deselect: false` and bails out if anything is still selected:

```csharp
if (!deselect && current.currentSelectedGameObject != null
    && current.currentSelectedGameObject.activeInHierarchy) return;
```

So a prompt raised while the triggering button is still selected never takes the selection, and the cursor
stays on the screen behind it — the prompt looks modal but is not. `RandomizerSaveFlow` hits this exactly:
it suppresses `SaveSlotButton.OnSubmit` to redirect an occupied slot to its clear prompt, and has to call
`PlaySubmitSound()` and `ForceDeselect()` itself, which is precisely what the native `ClearSaveButton` does
(`base.OnSubmit(eventData); ForceDeselect();`). When replacing a native submit, check what the original did
*besides* its main effect.

Text fields do not belong in a navigation ring at all. A focused `InputField` consumes the D-pad for
caret movement, so the cursor enters and cannot leave. Keep them out with
`MenuNavigation.ExcludeFromRing` and treat them as keyboard/mouse controls, giving controller users
another route to the same result (the seed page pairs the seed box with a GENERATE button). Then make
sure leaving the field hands the cursor back — `InputField.onEndEdit` is the hook — or a mouse user who
clicks into it strands the selection on a control nothing can navigate away from.

## Versioning and release

`<Version>` in `Directory.Build.props` is the single source of truth: it feeds the generated `BepInPlugin`
version, the Thunderstore package version (pre-release suffixes are stripped for Thunderstore, which cannot
take them), the NuGet package version, and the `v<version>` git tag CI creates.

Runtime dependencies are declared in two places that must stay in sync: `[BepInDependency]` attributes on
the plugin class, and `[package.dependencies]` in `thunderstore/thunderstore.toml`.

Publishing is currently disabled — `.github/workflows/build-publish.yml` hardcodes the `allow-release` job
output to `'false'`. Flip it to `'true'` for the first real release; Thunderstore/NuGet steps additionally
require the `THUNDERSTORE_API_KEY` / `NUGET_API_KEY` repository secrets.

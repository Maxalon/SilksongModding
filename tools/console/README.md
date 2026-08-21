# Console scripts

Snippets for the **C# Console** in Cinematic Unity Explorer (F7 → C# Console). They answer one-off
questions against a running game, where a plugin rebuild would be disproportionate.

## Running one

The green **Compile** button is Run. There is no separate Run button.

The dropdown beside it is a **Help menu**: picking an entry *inserts an example over the editor contents*.
It is not a mode selector, and it will discard what you pasted.

So: clear the editor → paste → Compile.

## Making a script say something

The output box shows the **return value** of the code. A script that is a block of statements returns
nothing, so the console reports "no output" even when the script ran perfectly. That is not an error, and
it is the single most confusing thing about this console.

Every script here therefore does both:

- calls `Log(...)` for progress, which writes to the Explorer's Log panel, and
- ends with a bare expression, whose value lands in the output box as a one-line verdict.

They also mirror important lines through `UnityEngine.Debug.Log`, which puts them in
`BepInEx/LogOutput.log` where they can be read after the fact rather than caught on screen.

## Helpers available in scope

From `UnityExplorer.CSConsole.ScriptInteraction`, verified against the installed build:

| Helper | Use |
| --- | --- |
| `Log(obj)` | Write to the Explorer's Log panel. |
| `Inspect(obj)` | Open the Inspector on an object — a script can hand you the thing it found. |
| `CurrentTarget` | Whatever the Inspector currently has open, so a script can act on your selection. |
| `AllTargets` | Every object currently inspected. |
| `Start(ienumerator)` / `Stop(coro)` | Run a coroutine, for anything that must wait or poll. |
| `Copy(obj)` / `Paste()` | The Explorer's **own** clipboard panel, not the OS clipboard. |
| `GetUsing()` / `GetVars()` / `GetClasses()` | What the evaluator currently has defined. |

## Writing scripts for this game

- **No publicizer here.** The console compiles against the real assemblies, so private members that the
  plugin reaches directly (`Breakable.itemDropGroups`, `MenuButtonList.entries`) need reflection.
- **Avoid a top-level `return`.** The code is evaluated as a statement block, not a method body.
- **Scene objects only.** `Resources.FindObjectsOfTypeAll<T>()` also returns prefabs and assets; filter on
  `gameObject.scene.IsValid()`.
- `Scripts/startup.cs` under the plugin folder runs automatically at launch, for anything wanted every run.

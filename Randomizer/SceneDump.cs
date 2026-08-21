using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SilksongModding.Randomizer;

/// <summary>
/// Writes every item-bearing object in a scene to a file, so check discovery becomes an offline
/// text-processing problem instead of a playthrough.
/// </summary>
/// <remarks>
/// <para>
/// The probes elsewhere in this folder answer one question per play session, which does not scale to a
/// whole game. This walks the loaded scene instead and records what is <i>there</i>, whether or not the
/// player goes near it — including objects that are inactive, which a playthrough can never observe. Walk
/// the game once with this on and the dumps can be re-read for questions nobody had thought of yet.
/// </para>
/// <para>
/// It deliberately does not dump everything. A full hierarchy is thousands of objects per scene and buries
/// the signal; an object earns a line by carrying something that can hold or grant an item. Use the
/// in-game explorer (Cinematic Unity Explorer) for "what is this one object?" — the two tools answer
/// different questions and neither replaces the other.
/// </para>
/// <para>
/// <b>Reading FSM actions has a side effect.</b> <c>FsmState.Actions</c> deserializes on first access
/// (<c>actions ?? (actions = actionData.LoadActions(this))</c>), so dumping materialises actions for
/// machines that have not run yet. PlayMaker would do this itself the moment the state was entered, so it
/// is not a behaviour change, but it is real work done early — a reason to keep this opt-in rather than
/// always-on.
/// </para>
/// </remarks>
internal static class SceneDump
{
    private static readonly HashSet<string> AlreadyDumped = new();

    /// <summary>Where dumps are written; alongside the BepInEx log, which is where you already look.</summary>
    internal static string DumpDirectory =>
        Path.Combine(BepInEx.Paths.BepInExRootPath, "SilksongModding-dumps");

    internal static void DumpOnce(Scene scene)
    {
        if (!scene.IsValid() || !AlreadyDumped.Add(scene.name))
        {
            return;
        }

        try
        {
            Dump(scene);
        }
        catch (Exception error)
        {
            // A diagnostic must never be able to break a scene transition.
            SilksongModdingPlugin.LogCheckWarning($"[dump] scene '{scene.name}' failed: {error}");
        }
    }

    private static void Dump(Scene scene)
    {
        StringBuilder report = new();
        report.Append("# scene '").Append(scene.name).Append("'\n");

        int described = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            described += Describe(root.transform, report);
        }

        Directory.CreateDirectory(DumpDirectory);
        string path = Path.Combine(DumpDirectory, Sanitise(scene.name) + ".txt");
        report.Append("# objects of interest: ").Append(described).Append('\n');
        File.WriteAllText(path, report.ToString());

        SilksongModdingPlugin.LogCheck($"[dump] scene='{scene.name}' objects={described} file='{path}'");
    }

    /// <summary>Walks the subtree, appending a block for anything that can hold or grant an item.</summary>
    private static int Describe(Transform node, StringBuilder report)
    {
        int described = 0;
        StringBuilder facts = new();

        try
        {
            AppendPickup(node, facts);
            AppendMarker(node, facts);
            AppendBreakable(node, facts);
            AppendEnemyDrop(node, facts);
            AppendPooledPrefabs(node, facts);
            AppendFsmItems(node, facts);
        }
        catch (Exception error)
        {
            facts.Append("  error '").Append(error.GetType().Name).Append("'\n");
        }

        if (facts.Length > 0)
        {
            described++;
            report.Append("object='").Append(GrantDiagnostics.HierarchyPathOf(node)).Append("' ")
                  .Append("active=").Append(node.gameObject.activeInHierarchy).Append(' ')
                  .Append(GrantDiagnostics.DescribeOwnerIdentity(node.gameObject)).Append('\n')
                  .Append(facts);
        }

        foreach (Transform child in node)
        {
            described += Describe(child, report);
        }

        return described;
    }

    private static void AppendPickup(Transform node, StringBuilder facts)
    {
        foreach (global::CollectableItemPickup pickup in node.GetComponents<global::CollectableItemPickup>())
        {
            global::SavedItem item = pickup.Item;
            facts.Append("  pickup item='").Append(item ? item.name : "<null>")
                 .Append("' type=").Append(item ? item.GetType().Name : "-")
                 .Append(" unique=").Append(item && item.IsUnique).Append('\n');
        }
    }

    private static void AppendMarker(Transform node, StringBuilder facts)
    {
        foreach (global::SavedItemTrackerMarker marker in node.GetComponents<global::SavedItemTrackerMarker>())
        {
            foreach (global::SavedItem item in marker.Items)
            {
                facts.Append("  declared item='").Append(item ? item.name : "<null>").Append("'\n");
            }
        }
    }

    /// <summary>
    /// Records destructible props, separating the loot that could be a check from loot that never can.
    /// </summary>
    /// <remarks>
    /// A <c>Breakable</c> flings currency through <c>FlingUtils</c> entirely independently of its
    /// <c>itemDropGroups</c>, so "drops something" and "holds a check" are different claims. Currency is
    /// farmable from the start and never gates progression, so a prop that only sheds rosaries or shards
    /// is not a location at all — and telling that apart from a prop holding a real item is exactly the
    /// question the tutorial statue raised. Both are recorded so the distinction is visible rather than
    /// assumed.
    /// </remarks>
    private static void AppendBreakable(Transform node, StringBuilder facts)
    {
        foreach (global::Breakable breakable in node.GetComponents<global::Breakable>())
        {
            string items = GrantDiagnostics.DescribeDropGroups(breakable);
            string currency = DescribeCurrency(breakable);

            if (items == "<none>" && currency.Length == 0)
            {
                continue;
            }

            facts.Append("  breakable items=").Append(items == "<none>" ? "<none>" : items)
                 .Append(" currency='").Append(currency.Length > 0 ? currency : "none")
                 .Append("' hits=").Append(breakable.hitsToBreak).Append('\n');
        }
    }

    private static string DescribeCurrency(global::Breakable breakable)
    {
        StringBuilder described = new();
        Append(described, "rosariesSmall", breakable.smallGeoDrops);
        Append(described, "rosariesMedium", breakable.mediumGeoDrops);
        Append(described, "rosariesLarge", breakable.largeGeoDrops);
        Append(described, "rosariesLargeSmooth", breakable.largeSmoothGeoDrops);
        Append(described, "shards", breakable.shellShardDrops);
        return described.ToString();

        static void Append(StringBuilder into, string label, global::TeamCherry.SharedUtils.MinMaxInt amount)
        {
            if (amount.End <= 0)
            {
                return;
            }

            if (into.Length > 0)
            {
                into.Append(' ');
            }

            into.Append(label).Append('=').Append(amount.Start).Append('-').Append(amount.End);
        }
    }

    private static void AppendEnemyDrop(Transform node, StringBuilder facts)
    {
        foreach (global::PersistentEnemyItemDrop drop in node.GetComponents<global::PersistentEnemyItemDrop>())
        {
            // The one enemy-drop system with a per-drop persistence key, so unlike the generic table it
            // could carry a check. Worth recording wherever it appears.
            facts.Append("  enemydrop component=PersistentEnemyItemDrop object='")
                 .Append(drop.name).Append("'\n");
        }

        foreach (global::HealthManager enemy in node.GetComponents<global::HealthManager>())
        {
            // The generic table has no PER-DROP persistence, which is a narrower statement than "this is
            // farmable". Whether the item can be obtained twice depends on whether the ENEMY stays dead,
            // and that is recorded by the object's own key on the line above. Reporting the table fact and
            // letting the key answer the rest avoids the earlier mistake of writing off enemy drops
            // wholesale on the strength of the table alone.
            string drops = GrantDiagnostics.DescribeDropGroups(
                GrantDiagnostics.ValueOfMember(enemy, "itemDropGroups"));
            if (drops != "<none>" && drops != "<empty>")
            {
                bool enemyPersists = enemy.GetComponentInParent<global::PersistentBoolItem>(includeInactive: true);
                facts.Append("  enemydrop table=HealthManager drops=").Append(drops)
                     .Append(" perDropPersistence=false enemyPersists=").Append(enemyPersists).Append('\n');
            }
        }
    }

    /// <summary>
    /// Records what an object pre-pools, which is how a spawner is linked to the thing it spawns.
    /// </summary>
    /// <remarks>
    /// The mossberry bush and its berry are both in the scene at load, the berry inactive and unparented,
    /// so nothing in the hierarchy connects them. The bush carries a <c>PersonalObjectPool</c>, and the
    /// prefab it pools is the link — which matters because a placement has to be keyed on the bush but
    /// applied to the berry.
    /// </remarks>
    private static void AppendPooledPrefabs(Transform node, StringBuilder facts)
    {
        foreach (global::PersonalObjectPool pool in node.GetComponents<global::PersonalObjectPool>())
        {
            if (pool.startupPool == null)
            {
                continue;
            }

            foreach (global::StartupPool entry in pool.startupPool)
            {
                if (entry.prefab)
                {
                    facts.Append("  pools prefab='").Append(entry.prefab.name)
                         .Append("' size=").Append(entry.size).Append('\n');
                }
            }
        }
    }

    /// <summary>
    /// Records items granted from inside a PlayMaker machine, which nothing else can see.
    /// </summary>
    /// <remarks>
    /// This is the whole reason the dumper is worth building. A mossberry's item is a serialized
    /// <c>FsmObject</c> on a <c>CollectableItemAction</c> buried in one state of one FSM — invisible to
    /// component scans, and previously only observable by walking up and hitting the bush.
    /// </remarks>
    private static void AppendFsmItems(Transform node, StringBuilder facts)
    {
        foreach (global::PlayMakerFSM machine in node.GetComponents<global::PlayMakerFSM>())
        {
            if (machine.Fsm == null)
            {
                continue;
            }

            global::HutongGames.PlayMaker.FsmState[] states;
            try
            {
                states = machine.FsmStates;
            }
            catch (Exception)
            {
                continue;
            }

            foreach (global::HutongGames.PlayMaker.FsmState state in states ?? Array.Empty<global::HutongGames.PlayMaker.FsmState>())
            {
                global::HutongGames.PlayMaker.FsmStateAction[] actions;
                try
                {
                    // Forces deserialization for machines that have not run; see the type remarks.
                    actions = state.Actions;
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (global::HutongGames.PlayMaker.FsmStateAction action in
                         actions ?? Array.Empty<global::HutongGames.PlayMaker.FsmStateAction>())
                {
                    if (action is not global::HutongGames.PlayMaker.Actions.CollectableItemAction itemAction)
                    {
                        continue;
                    }

                    global::CollectableItem? item = itemAction.Item?.Value as global::CollectableItem;
                    facts.Append("  fsmitem fsm='").Append(machine.FsmName)
                         .Append("' state='").Append(state.Name)
                         .Append("' action=").Append(action.GetType().Name)
                         .Append(" item='").Append(item ? item!.name : "<null>").Append("'\n");
                }
            }
        }
    }

    private static string Sanitise(string name)
    {
        StringBuilder safe = new(name.Length);
        foreach (char character in name)
        {
            safe.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), character) >= 0 ? '_' : character);
        }

        return safe.Length > 0 ? safe.ToString() : "unnamed";
    }
}

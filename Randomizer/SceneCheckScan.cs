using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SilksongModding.Randomizer;

/// <summary>
/// Enumerates the item locations a scene <i>declares</i>, rather than the ones a playthrough happens to
/// touch.
/// </summary>
/// <remarks>
/// <para>
/// <c>SavedItemTrackerMarker</c> is the game's own annotation: a component carrying a serialized
/// <c>SavedItem[]</c>, saying "this object yields these items". Team Cherry put it on the mossberry bush.
/// If it is used consistently, it is a developer-authored check list, and reading it turns discovery from
/// "walk the whole game and watch the log" into "load a scene and dump it" — including for pickups that
/// are awkward or slow to reach.
/// </para>
/// <para>
/// Whether it <i>is</i> used consistently is exactly what this scan is meant to establish, by reporting how
/// many markers a scene holds against how many pickups that scene is known to contain. Treat a marker as a
/// lead, not as proof: nothing found so far shows the game reading these at runtime, so they may be
/// editor-time metadata that is incomplete or stale.
/// </para>
/// </remarks>
internal static class SceneCheckScan
{
    internal static IEnumerator ScanWhenReady(Scene scene)
    {
        // One frame, so every Awake in the scene has run: PersistentBoolItem fills in its ID lazily and
        // would otherwise be read before it has one.
        yield return null;

        if (!scene.IsValid())
        {
            yield break;
        }

        int found = 0;
        int loaded = 0;
        System.Text.StringBuilder elsewhere = new();

        // FindObjectsOfTypeAll rather than FindObjectsOfType: markers on inactive objects are exactly the
        // ones a playthrough would miss, so excluding them would defeat the purpose. It also returns
        // prefabs and assets, which is what the scene comparison filters out.
        foreach (global::SavedItemTrackerMarker marker in
                 Resources.FindObjectsOfTypeAll<global::SavedItemTrackerMarker>())
        {
            if (!marker)
            {
                continue;
            }

            Scene home = marker.gameObject.scene;
            if (!home.IsValid())
            {
                // A prefab or imported asset rather than a placed object. Counted separately: it still
                // proves the component is in use somewhere, which a zero scene count alone does not.
                continue;
            }

            loaded++;
            if (home != scene)
            {
                if (elsewhere.Length < 200)
                {
                    elsewhere.Append(elsewhere.Length > 0 ? "," : string.Empty).Append(home.name);
                }

                continue;
            }

            found++;
            SilksongModdingPlugin.LogCheck(
                $"[declared] items='{DescribeItems(marker)}' " +
                $"{GrantDiagnostics.DescribeOwnerIdentity(marker.gameObject)}");
        }

        // loadedTotal is the control this scan was missing. A per-scene zero is ambiguous on its own — it
        // reads the same whether the scene has no markers or the scan is simply not finding any. A
        // non-zero total with a zero for this scene separates those two outright.
        SilksongModdingPlugin.LogCheck(
            $"[declared] scene='{scene.name}' markers={found} loadedTotal={loaded} " +
            $"otherScenes='{(elsewhere.Length > 0 ? elsewhere.ToString() : "-")}'");
    }

    private static string DescribeItems(global::SavedItemTrackerMarker marker)
    {
        System.Text.StringBuilder described = new();
        foreach (global::SavedItem item in marker.Items)
        {
            if (described.Length > 0)
            {
                described.Append(',');
            }

            described.Append(item ? item.name : "<null>");
        }

        return described.Length > 0 ? described.ToString() : "<empty>";
    }
}

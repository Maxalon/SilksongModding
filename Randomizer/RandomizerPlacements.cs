using System.Collections.Generic;

namespace SilksongModding.Randomizer;

/// <summary>
/// The placement table: which replacement item each check should grant.
/// </summary>
/// <remarks>
/// <para>
/// This is process-global state, so the fill and the clear are deliberately written as a pair. Unity keeps
/// a loaded assembly's statics alive across quit-to-title and across save-slot switches, so a table that is
/// only ever filled will leak a randomized layout into the next save the player opens — including a vanilla
/// one. <see cref="Clear"/> must stay wired to leaving the game.
/// </para>
/// <para>
/// For the first slice the table is hardcoded. It will later be populated from
/// <see cref="RandomizerSaveData"/>, which is why the fill already runs off save-load rather than off
/// plugin startup.
/// </para>
/// </remarks>
internal static class RandomizerPlacements
{
    private static readonly Dictionary<string, string> Active = new();

    /// <summary>True once a save's placements have been loaded.</summary>
    internal static bool IsLoaded { get; private set; }

    /// <summary>
    /// Populates the table for the save that is being entered. Safe to call more than once.
    /// </summary>
    internal static void LoadForCurrentSave()
    {
        Active.Clear();

        // Read back from the save rather than regenerated from the seed, so a save in progress keeps the
        // layout it was created with even if the database or the fill changes underneath it.
        foreach (KeyValuePair<string, string> placement in SilksongModdingPlugin.CurrentPlacements)
        {
            Active[placement.Key] = placement.Value;
        }

        IsLoaded = true;
        SilksongModdingPlugin.LogCheck($"[placements] loaded {Active.Count} placement(s).");
    }

    /// <summary>
    /// Looks up a placement, preferring an entry qualified by hierarchy path.
    /// </summary>
    /// <remarks>
    /// Most checks are keyed by id alone. Where a scene holds two checks reporting the same id — a blank
    /// persistence id is filled in from the GameObject name, and names repeat — the generator stores them
    /// under "id|path". Trying the qualified form first means those two receive different items instead of
    /// both taking whichever was written last.
    /// </remarks>
    internal static bool TryGetReplacement(in CheckId check, string hierarchyPath, out string replacementItemName)
    {
        string id = check.ToString();
        return Active.TryGetValue(id + "|" + hierarchyPath, out replacementItemName!)
               || Active.TryGetValue(id, out replacementItemName!);
    }

    /// <summary>
    /// Drops the table when leaving a save, so it cannot bleed into the next one that is opened.
    /// </summary>
    internal static void Clear()
    {
        if (!IsLoaded && Active.Count == 0)
        {
            return;
        }

        Active.Clear();
        IsLoaded = false;

        // Shop stock lives on shared ScriptableObjects, so leaving a save has to undo those edits or the
        // next save opened - vanilla or not - inherits this seed's shops.
        ShopPlacements.RestoreAll();

        SilksongModdingPlugin.LogCheck("[placements] cleared on leaving the save.");
    }
}

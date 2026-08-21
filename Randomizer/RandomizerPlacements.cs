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
    /// <summary>
    /// Hardcoded placements for the vertical slice, keyed by <see cref="CheckId.ToString"/>.
    /// </summary>
    /// <remarks>
    /// Leave this empty for the first run. The pickup patch logs a <c>[check] pickup key='...'</c> line
    /// for every pickup it sees, so play to the target pickup and then run
    /// <c>tools/harvest-checks.sh</c>, which reduces <c>BepInEx/LogOutput.log</c> to a deduplicated check
    /// list and emits paste-ready entries for this table.
    /// <para>
    /// Use a non-unique replacement. <c>CollectableItemPickup.CheckActivation</c> hides any pickup whose
    /// item reports <c>!CanGetMore()</c>, so placing a unique item the save already owns makes the check
    /// silently vanish from the world with no error.
    /// </para>
    /// <example>
    /// <code>
    /// { "pickup:Mosstown_01:Rosary String", "Mossberry" },
    /// </code>
    /// </example>
    /// </remarks>
    private static readonly Dictionary<string, string> SlicePlacements = new();

    private static readonly Dictionary<string, string> Active = new();

    /// <summary>True once a save's placements have been loaded.</summary>
    internal static bool IsLoaded { get; private set; }

    internal static bool TryGetReplacement(in CheckId check, out string replacementItemName) =>
        Active.TryGetValue(check.ToString(), out replacementItemName!);

    /// <summary>
    /// Populates the table for the save that is being entered. Safe to call more than once.
    /// </summary>
    internal static void LoadForCurrentSave()
    {
        Active.Clear();
        foreach (KeyValuePair<string, string> placement in SlicePlacements)
        {
            Active[placement.Key] = placement.Value;
        }

        IsLoaded = true;
        SilksongModdingPlugin.LogCheck($"[placements] loaded {Active.Count} placement(s).");
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
        SilksongModdingPlugin.LogCheck("[placements] cleared on leaving the save.");
    }
}

namespace SilksongModding;

/// <summary>
/// Immutable data written alongside a randomized save by Silksong.DataManager.
/// Future randomizer settings and generated placements belong here as well.
/// </summary>
public sealed class RandomizerSaveData
{
    public int FormatVersion { get; set; } = 1;

    public string Seed { get; set; } = string.Empty;

    /// <summary>
    /// The generated layout: check id to the item that check now holds.
    /// </summary>
    /// <remarks>
    /// Stored rather than regenerated on load. The fill is deterministic, so recomputing it from the seed
    /// would usually agree — but only while the check database and the fill algorithm never change. Saving
    /// the result means an in-progress save keeps the layout it was played with, instead of silently
    /// reshuffling under the player when the mod updates.
    /// </remarks>
    public System.Collections.Generic.Dictionary<string, string> Placements { get; set; } = new();

    /// <summary>
    /// Randomized shop prices, keyed the same way as <see cref="Placements"/>.
    /// </summary>
    /// <remarks>
    /// Separate from the placements because a price is not a placement: only shop slots have one, and the
    /// two are decided by different rules - what a slot holds comes from the shared item pool, what it
    /// costs comes from its own shop's range.
    /// </remarks>
    public System.Collections.Generic.Dictionary<string, int> Prices { get; set; } = new();
}

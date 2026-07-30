namespace SilksongModding;

/// <summary>
/// Immutable data written alongside a randomized save by Silksong.DataManager.
/// Future randomizer settings and generated placements belong here as well.
/// </summary>
public sealed class RandomizerSaveData
{
    public int FormatVersion { get; set; } = 1;

    public string Seed { get; set; } = string.Empty;
}

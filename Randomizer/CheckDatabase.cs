using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;

namespace SilksongModding.Randomizer;

/// <summary>One place an item can be placed, as extracted from the game's own bundles.</summary>
internal sealed class CheckEntry
{
    /// <summary>Matches <see cref="CheckId.ToString"/>, which is how a placement is looked up.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Hierarchy path, used only to break ties between checks sharing an id.</summary>
    public string Path { get; set; } = string.Empty;

    public string Scene { get; set; } = string.Empty;

    /// <summary>What this check holds in an unrandomized game; the pool is built from these.</summary>
    public string Item { get; set; } = string.Empty;

    /// <summary>True when another check in the same scene reports the same id.</summary>
    public bool Ambiguous { get; set; }
}

/// <summary>
/// The shipped list of checks, read once from an embedded resource.
/// </summary>
/// <remarks>
/// Generated offline by <c>tools/extract-locations.py</c> and narrowed by <c>tools/build-check-db.py</c>,
/// rather than discovered by playing. It covers the scene-placed <c>CollectableItemPickup</c> archetype
/// only — the one whose swap mechanism is proven — so a seed built on it is deliberately partial rather
/// than complete.
/// </remarks>
internal static class CheckDatabase
{
    private const string ResourceName = "SilksongModding.Randomizer.checks.json";

    private static IReadOnlyList<CheckEntry>? checks;

    internal static IReadOnlyList<CheckEntry> Checks => checks ??= Load();

    private sealed class Document
    {
        public int FormatVersion { get; set; }

        public List<CheckEntry> Checks { get; set; } = new();
    }

    private static IReadOnlyList<CheckEntry> Load()
    {
        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                SilksongModdingPlugin.LogCheckWarning(
                    $"[checks] embedded resource '{ResourceName}' is missing; no checks are available.");
                return Array.Empty<CheckEntry>();
            }

            using StreamReader reader = new(stream);
            Document? document = JsonConvert.DeserializeObject<Document>(reader.ReadToEnd());
            List<CheckEntry> loaded = document?.Checks ?? new List<CheckEntry>();
            SilksongModdingPlugin.LogCheck($"[checks] loaded {loaded.Count} check(s) from the database.");
            return loaded;
        }
        catch (Exception error)
        {
            // A missing or malformed database must leave the game vanilla rather than half-randomized.
            SilksongModdingPlugin.LogCheckWarning($"[checks] could not read the database: {error}");
            return Array.Empty<CheckEntry>();
        }
    }
}

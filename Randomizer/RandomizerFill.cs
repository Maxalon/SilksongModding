using System;
using System.Collections.Generic;

namespace SilksongModding.Randomizer;

/// <summary>
/// Decides which item each check holds, from a seed.
/// </summary>
/// <remarks>
/// <para>
/// This is a plain shuffle of the vanilla items across the checks that hold them: every item that exists
/// still exists, and every check still holds exactly one. It is <b>not</b> logic-aware. Nothing stops an
/// ability being placed behind the very check that needs it, so a seed can be unwinnable, and that is a
/// known and accepted property of this stage rather than an oversight — reachability data is a separate
/// piece of work.
/// </para>
/// <para>
/// The shuffle uses its own generator rather than <see cref="Random"/>. A seed has to produce the same
/// layout for everyone who types it, and <c>System.Random</c>'s sequence is an implementation detail that
/// has changed between runtimes before. A few lines of xorshift are worth not having a seed mean different
/// things on different machines.
/// </para>
/// </remarks>
internal static class RandomizerFill
{
    internal static Dictionary<string, string> Generate(string seed, IReadOnlyList<CheckEntry> checks)
    {
        Dictionary<string, string> placements = new(checks.Count);
        if (checks.Count == 0)
        {
            return placements;
        }

        List<string> pool = new(checks.Count);
        foreach (CheckEntry check in checks)
        {
            pool.Add(check.Item);
        }

        Shuffle(pool, seed);

        for (int index = 0; index < checks.Count; index++)
        {
            placements[PlacementKey(checks[index])] = pool[index];
        }

        return placements;
    }

    /// <summary>
    /// The string a placement is stored under.
    /// </summary>
    /// <remarks>
    /// Normally the check id. Where a scene holds two checks reporting the same id — the game derives a
    /// blank persistence id from the GameObject name, and names repeat — the path is appended so the two
    /// stay distinguishable instead of one silently overwriting the other.
    /// </remarks>
    internal static string PlacementKey(CheckEntry check) =>
        check.Ambiguous ? check.Id + "|" + check.Path : check.Id;

    private static void Shuffle(IList<string> items, string seed)
    {
        ulong state = Hash(seed);
        for (int index = items.Count - 1; index > 0; index--)
        {
            int swap = (int)(NextRandom(ref state) % (ulong)(index + 1));
            (items[index], items[swap]) = (items[swap], items[index]);
        }
    }

    /// <summary>FNV-1a over the seed text, so identical seeds start the generator identically.</summary>
    private static ulong Hash(string seed)
    {
        ulong hash = 14695981039346656037;
        foreach (char character in seed)
        {
            hash ^= character;
            hash *= 1099511628211;
        }

        return hash == 0 ? 1 : hash;
    }

    private static ulong NextRandom(ref ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return state;
    }
}

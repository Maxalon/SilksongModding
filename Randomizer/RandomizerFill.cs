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
    /// <summary>The layout, plus the prices that go with any shop slots in it.</summary>
    internal sealed class Result
    {
        internal Dictionary<string, string> Placements { get; } = new();

        internal Dictionary<string, int> Prices { get; } = new();
    }

    internal static Result Generate(string seed, IReadOnlyList<CheckEntry> checks)
    {
        Result result = new();
        if (checks.Count == 0)
        {
            return result;
        }

        // One pool across both archetypes on purpose: a tool that was sold in a shop can turn up in the
        // world, and a world pickup can turn up for sale. Shuffling them separately would keep each
        // archetype's items inside it and make the shuffle far less interesting.
        List<string> pool = new(checks.Count);
        foreach (CheckEntry check in checks)
        {
            pool.Add(check.Item);
        }

        ulong state = Hash(seed);
        Shuffle(pool, ref state);

        for (int index = 0; index < checks.Count; index++)
        {
            CheckEntry check = checks[index];
            result.Placements[PlacementKey(check)] = pool[index];

            if (check.IsShop)
            {
                result.Prices[PlacementKey(check)] = PriceFor(check, ref state);
            }
        }

        return result;
    }

    /// <summary>
    /// A price drawn from the shop's own range, rather than from the item now sitting in the slot.
    /// </summary>
    /// <remarks>
    /// Pricing by the item would leak the layout: an expensive tag would advertise a good item before the
    /// player ever spoke to the shopkeeper. Pricing by the shop keeps a slot's cost uninformative while
    /// still varying between seeds, and keeps each shop's spread recognisably its own.
    /// </remarks>
    private static int PriceFor(CheckEntry check, ref ulong state)
    {
        int low = check.CostLow > 0 ? check.CostLow : check.Cost;
        int high = check.CostHigh > 0 ? check.CostHigh : check.Cost;
        if (high <= low)
        {
            return low > 0 ? low : check.Cost;
        }

        int span = high - low + 1;
        int price = low + (int)(NextRandom(ref state) % (ulong)span);

        // Rounded to something a price tag would plausibly show, rather than 337 rosaries.
        int rounded = price >= 100 ? price / 10 * 10 : price / 5 * 5;
        return rounded < low ? low : rounded;
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

    private static void Shuffle(IList<string> items, ref ulong state)
    {
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

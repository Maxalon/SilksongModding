using System;

namespace SilksongModding.Randomizer;

/// <summary>
/// Identity of a place that can hold a randomized item.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Kind"/> is what keeps two checks apart that would otherwise collide. The game reuses
/// <c>(SceneName, ID)</c> across component types, and some archetypes are not scene-keyed at all — shop
/// slots, for instance, live on a shared <c>ShopItem</c> asset with no scene. So the kind is part of the
/// key rather than metadata hanging off it.
/// </para>
/// <para>
/// <see cref="ToString"/> is the serialized form. It is what gets written into save data and the spoiler
/// log, so it must stay stable: changing the format invalidates every existing randomized save.
/// </para>
/// </remarks>
internal readonly struct CheckId : IEquatable<CheckId>
{
    /// <summary>A scene-placed <c>CollectableItemPickup</c>.</summary>
    internal const string PickupKind = "pickup";

    internal CheckId(string kind, string scope, string local)
    {
        Kind = kind;
        Scope = scope;
        Local = local;
    }

    /// <summary>Which archetype this check belongs to; see <see cref="PickupKind"/>.</summary>
    internal string Kind { get; }

    /// <summary>Base scene name, or an empty string for checks that are not scene-scoped.</summary>
    internal string Scope { get; }

    /// <summary>Archetype-specific identity within the scope.</summary>
    internal string Local { get; }

    public override string ToString() => $"{Kind}:{Scope}:{Local}";

    public bool Equals(CheckId other) =>
        Kind == other.Kind && Scope == other.Scope && Local == other.Local;

    public override bool Equals(object? obj) => obj is CheckId other && Equals(other);

    public override int GetHashCode() => ToString().GetHashCode();
}

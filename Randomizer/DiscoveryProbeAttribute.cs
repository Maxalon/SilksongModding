using System;

namespace SilksongModding.Randomizer;

/// <summary>
/// Marks a patch class that exists only to observe the game, never to change it.
/// </summary>
/// <remarks>
/// These earned their keep while the archetypes were unknown, and several are deliberately broad —
/// <c>HitProbe</c> hooks the funnel every hit in the game passes through, and <c>FsmSpawnProbe</c> fires
/// on every PlayMaker spawn action. That cost is fine on a deliberately minimal route and wasteful during
/// real play, so marked classes are skipped entirely unless discovery is switched on: not merely silenced,
/// but never patched in, which is the only version of "off" that costs nothing.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class DiscoveryProbeAttribute : Attribute
{
}

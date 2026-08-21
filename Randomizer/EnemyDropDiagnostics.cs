using System;
using System.Collections;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace SilksongModding.Randomizer;

/// <summary>
/// Read-only probes over the three systems that can make a dead enemy leave an item behind.
/// </summary>
/// <remarks>
/// <para>
/// Decompiling cannot settle which of these produces any given drop, because that is decided by
/// serialized scene data rather than by code. So this logs all three and the question gets answered by
/// walking to the enemy and killing it.
/// </para>
/// <para>
/// The answer matters well beyond curiosity, because the three differ in whether a check can exist at all:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>PersistentEnemyItemDrop</c> — has a real per-drop key (<c>"{enemy}_item_{itemName}"</c> recorded in
/// <c>SceneData.PersistentBools</c>), so it can carry a randomizer check.
/// </description></item>
/// <item><description>
/// <c>HealthManager.itemDropGroups</c> — no per-drop persistence whatsoever. Every kill re-drops, so any
/// check placed here would be infinitely farmable.
/// </description></item>
/// <item><description>
/// <c>CorpseItems</c> — rerolls its whole table in <c>Start</c> with zero persistence, and grants through
/// <c>GenericPickup</c> rather than <c>CollectableItemPickup</c>.
/// </description></item>
/// </list>
/// <para>
/// If a drop turns out to come from either of the latter two, the honest outcome may be excluding it from
/// the pool rather than randomizing it badly.
/// </para>
/// </remarks>
internal static class EnemyDropDiagnostics
{
    internal static void LogSpawn(string source, Component? owner, string detail)
    {
        // Unity truthiness, not `!= null`. These probes fire while things are dying: a HealthManager
        // spawns its drop during its own destruction, and reading `.gameObject` off a component whose
        // native half is already gone throws NullReferenceException rather than returning null.
        GameObject? ownerObject = owner ? owner!.gameObject : null;
        string scene = ownerObject ? ownerObject!.scene.name : "<destroyed>";
        string name = ownerObject ? ownerObject!.name : "<destroyed>";
        SilksongModdingPlugin.LogCheck($"[drop] {source} scene='{scene}' object='{name}' {detail}");
    }

    /// <summary>
    /// Renders a private drop-table collection whose element type is a private nested struct.
    /// </summary>
    internal static string DescribeItems(object? items, string itemFieldName)
    {
        if (items is not IList list || list.Count == 0)
        {
            return "items=<none>";
        }

        StringBuilder described = new($"itemCount={list.Count} items=");
        for (int index = 0; index < list.Count; index++)
        {
            object? entry = list[index];
            if (entry == null)
            {
                continue;
            }

            FieldInfo? itemField = entry.GetType().GetField(
                itemFieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            object? item = itemField?.GetValue(entry);
            described.Append('\'').Append(item is UnityEngine.Object named ? named.name : "<unreadable>").Append('\'');
            if (index < list.Count - 1)
            {
                described.Append(',');
            }
        }

        return described.ToString();
    }
}

/// <summary>
/// The only enemy-drop system with a per-drop persistence key, and therefore the only one that could
/// carry a check as-is.
/// </summary>
[HarmonyPatch(typeof(global::PersistentEnemyItemDrop), "Awake")]
internal static class PersistentEnemyItemDropProbe
{
    [HarmonyPostfix]
    private static void Postfix(
        global::PersistentEnemyItemDrop __instance,
        global::SavedItem ___item,
        global::CollectableItemPickup ___dropPrefab)
    {
        try
        {
            // Awake early-returns when the item is null or already maxed, so a line with wired=False is
            // itself informative: the component is present but will never drop for this save.
            bool wired = ___dropPrefab && ___item;
            EnemyDropDiagnostics.LogSpawn(
                "PersistentEnemyItemDrop",
                __instance,
                $"item='{(___item ? ___item.name : "<null>")}' " +
                $"itemType={(___item ? ___item.GetType().Name : "-")} wired={wired}");
        }
        catch (Exception error)
        {
            SilksongModdingPlugin.LogCheckWarning($"[drop] PersistentEnemyItemDrop probe failed: {error}");
        }
    }
}

/// <summary>
/// The generic enemy drop table. Has no per-drop persistence, so anything it produces re-drops on every
/// kill.
/// </summary>
[HarmonyPatch(typeof(global::HealthManager), "SpawnItemDrop",
    new[]
    {
        typeof(global::SavedItem),
        typeof(int),
        typeof(global::CollectableItemPickup),
        typeof(Transform),
        typeof(int),
    })]
internal static class HealthManagerDropProbe
{
    [HarmonyPostfix]
    private static void Postfix(global::HealthManager __instance, global::SavedItem dropItem, int count, int limit)
    {
        try
        {
            EnemyDropDiagnostics.LogSpawn(
                "HealthManager.SpawnItemDrop",
                __instance,
                $"item='{(dropItem ? dropItem.name : "<null>")}' count={count} limit={limit} " +
                // limit > 0 routes through ObjectPool.Spawn, which recycles a GameObject and does not
                // re-run Awake — so recycled pickups keep their listeners and collected flags across kills.
                $"pooled={(limit > 0)}");
        }
        catch (Exception error)
        {
            SilksongModdingPlugin.LogCheckWarning($"[drop] HealthManager probe failed: {error}");
        }
    }
}

/// <summary>
/// Corpse loot. Rerolls its table on every <c>Start</c> and persists nothing.
/// </summary>
[HarmonyPatch(typeof(global::CorpseItems), "Start")]
internal static class CorpseItemsProbe
{
    [HarmonyPostfix]
    private static void Postfix(global::CorpseItems __instance)
    {
        try
        {
            // Read the table off the instance rather than as an injected `___pickupItems` parameter. Its
            // element type is the nested CorpseItems.ItemPickupSingle, so the only parameter type that
            // reads naturally is `object` — and that asks Harmony to widen a List<T> field into an object
            // slot, which is not a contract Harmony documents. Game assemblies are publicized, so this
            // reaches the same private field with no injection rules involved.
            EnemyDropDiagnostics.LogSpawn(
                "CorpseItems",
                __instance,
                EnemyDropDiagnostics.DescribeItems(__instance.pickupItems, "Item"));
        }
        catch (Exception error)
        {
            SilksongModdingPlugin.LogCheckWarning($"[drop] CorpseItems probe failed: {error}");
        }
    }
}

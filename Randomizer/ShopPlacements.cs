using System;
using System.Collections.Generic;
using HarmonyLib;

namespace SilksongModding.Randomizer;

/// <summary>
/// Applies a seed's layout and prices to shop stock, and puts the shop back afterwards.
/// </summary>
/// <remarks>
/// <para>
/// A <c>ShopItem</c> is a <c>ScriptableObject</c>, not a scene object: one asset, shared by every save in
/// the session. Writing to it is how a shop slot gets randomized, and it is also why this class exists at
/// all — an edited asset stays edited until something puts it back. Without <see cref="RestoreAll"/> a
/// player who quits to the title and loads a <i>vanilla</i> save would find that save's shops still
/// selling the randomized stock at randomized prices.
/// </para>
/// <para>
/// Every field written is snapshotted the first time it is touched, so restoring returns the exact vanilla
/// value rather than a value the mod believes to be vanilla.
/// </para>
/// </remarks>
internal static class ShopPlacements
{
    private sealed class Original
    {
        internal global::SavedItem? Item;

        internal int Cost;
    }

    private static readonly Dictionary<global::ShopItem, Original> Untouched = new();

    /// <summary>Rewrites a shop's stock in place, if the current save has a layout for it.</summary>
    internal static void Apply(global::ShopItem[]? stock)
    {
        if (stock == null || !RandomizerPlacements.IsLoaded)
        {
            return;
        }

        foreach (global::ShopItem slot in stock)
        {
            if (!slot)
            {
                continue;
            }

            try
            {
                ApplyOne(slot);
            }
            catch (Exception error)
            {
                SilksongModdingPlugin.LogCheckWarning($"[shop] '{slot.name}' failed: {error}");
            }
        }
    }

    private static void ApplyOne(global::ShopItem slot)
    {
        string key = "shop:" + slot.name;
        bool hasItem = SilksongModdingPlugin.CurrentPlacements.TryGetValue(key, out string replacementName);
        bool hasPrice = SilksongModdingPlugin.CurrentPrices.TryGetValue(key, out int price);
        if (!hasItem && !hasPrice)
        {
            return;
        }

        if (!Untouched.ContainsKey(slot))
        {
            Untouched[slot] = new Original { Item = slot.savedItem, Cost = slot.cost };
        }

        if (hasItem)
        {
            global::CollectableItem? replacement = ResolveItem(replacementName, slot.name);
            if (replacement)
            {
                slot.savedItem = replacement;
            }
        }

        if (hasPrice)
        {
            slot.cost = price;
        }

        SilksongModdingPlugin.LogCheck(
            $"[shop] swap key='{key}' to='{(slot.savedItem ? slot.savedItem.name : "<null>")}' cost={slot.cost}");
    }

    private static global::CollectableItem? ResolveItem(string name, string slotName)
    {
        try
        {
            return global::CollectableItemManager.GetItemByName(name);
        }
        catch (Exception error)
        {
            SilksongModdingPlugin.LogCheckWarning(
                $"[shop] '{slotName}': could not resolve '{name}' ({error.GetType().Name}); left vanilla.");
            return null;
        }
    }

    /// <summary>Returns every edited asset to the value it shipped with.</summary>
    internal static void RestoreAll()
    {
        if (Untouched.Count == 0)
        {
            return;
        }

        foreach (KeyValuePair<global::ShopItem, Original> entry in Untouched)
        {
            if (!entry.Key)
            {
                continue;
            }

            entry.Key.savedItem = entry.Value.Item;
            entry.Key.cost = entry.Value.Cost;
        }

        SilksongModdingPlugin.LogCheck($"[shop] restored {Untouched.Count} shop slot(s) to vanilla.");
        Untouched.Clear();
    }
}

/// <summary>
/// Rewrites shop stock as it is handed to the menu.
/// </summary>
/// <remarks>
/// <c>SetStock</c> is where the menu receives the array it is about to display, which is the last point
/// before the player sees a price or an item name. Patching the display itself would mean chasing every
/// label; patching the data means the menu renders the randomized stock by itself.
/// </remarks>
[HarmonyPatch(typeof(global::ShopMenuStock), nameof(global::ShopMenuStock.SetStock))]
internal static class ShopStockPatch
{
    [HarmonyPrefix]
    private static void Prefix(global::ShopItem[] newStock)
    {
        try
        {
            ShopPlacements.Apply(newStock);
        }
        catch (Exception error)
        {
            SilksongModdingPlugin.LogCheckWarning($"[shop] stock patch failed: {error}");
        }
    }
}

/// <summary>Catches stock that was assigned before the menu was opened.</summary>
[HarmonyPatch(typeof(global::ShopMenuStock), nameof(global::ShopMenuStock.SpawnStock))]
internal static class ShopSpawnPatch
{
    [HarmonyPrefix]
    private static void Prefix(global::ShopMenuStock __instance)
    {
        try
        {
            ShopPlacements.Apply(__instance.stock);
        }
        catch (Exception error)
        {
            SilksongModdingPlugin.LogCheckWarning($"[shop] spawn patch failed: {error}");
        }
    }
}

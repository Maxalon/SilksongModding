using System;
using HarmonyLib;
using UnityEngine;

namespace SilksongModding.Randomizer;

/// <summary>
/// Binds the scene-placed <c>CollectableItemPickup</c> archetype — by a wide margin the most common
/// randomizable pickup in the game.
/// </summary>
/// <remarks>
/// <para>
/// One prefix does both jobs on purpose: it logs every pickup it sees (which is how the check database
/// gets built — one playthrough enumerates the pool) and, when a placement exists for that check, swaps
/// the granted item before the game ever reads it.
/// </para>
/// <para>
/// <b>Why <c>Start</c> and not <c>Awake</c>.</b> Unity runs <c>Awake</c> synchronously inside
/// <c>Instantiate</c>, and every spawner in the game (<c>HealthManager.SpawnItemDrop</c>,
/// <c>PersistentEnemyItemDrop.DropItem</c>, <c>Breakable</c>, <c>SilkGrubCocoon</c>) calls
/// <c>SetItem</c> on the statement immediately after instantiating — so a swap done in <c>Awake</c> is
/// overwritten microseconds later. A <i>prefix</i> on <c>Start</c> still runs before <c>Start</c>'s own
/// call to <c>Setup() → CheckActivation()</c>, which is the read that matters.
/// </para>
/// <para>
/// <b>Why the swap happens here and not at the grant.</b> <c>SavedItem.TryGet</c>,
/// <c>CollectableItem.Collect</c> and <c>CollectableItemManager.AddItem</c> look like universal choke
/// points, and they are — but by the time execution reaches them the location context is gone, so two
/// different checks granting the same item are indistinguishable. Every hook must sit somewhere that
/// still knows <i>which check</i> produced the item. This is the one design decision here that would be
/// a rewrite rather than a fix.
/// </para>
/// </remarks>
[HarmonyPatch(typeof(global::CollectableItemPickup), "Start")]
internal static class PickupCheckPatch
{
    [HarmonyPrefix]
    private static void Prefix(
        global::CollectableItemPickup __instance,
        global::PersistentBoolItem ___persistent,
        global::InteractEvents ___interactEvents,
        global::TrackTriggerObjects ___pickupTrigger,
        string ___playerDataBool)
    {
        // A diagnostic must never be able to break a pickup.
        try
        {
            Apply(__instance, ___persistent, ___interactEvents, ___pickupTrigger, ___playerDataBool);
        }
        catch (Exception error)
        {
            SilksongModdingPlugin.LogCheckWarning($"[check] pickup inspection failed: {error}");
        }
    }

    private static void Apply(
        global::CollectableItemPickup pickup,
        global::PersistentBoolItem persistent,
        global::InteractEvents interactEvents,
        global::TrackTriggerObjects pickupTrigger,
        string playerDataBool)
    {
        global::SavedItem vanilla = pickup.Item;
        CheckId check = DescribeCheck(pickup, persistent, playerDataBool, out string idMode);

        // Contact and interaction both converge on DoPickupAction, so this is presentation only —
        // except that the contact path passes breakIfAtMax:true, which auto-deconstructs an at-cap
        // item into currency instead of refusing it. Worth knowing which kind a check is.
        string collectMode = interactEvents ? "interact"
            : pickupTrigger ? "contact"
            : "neither";

        // Names routinely contain spaces ("Rosary String"), so every value that holds one is quoted.
        // Without that, a space-delimited key=value line cannot be parsed back out of the log — which is
        // the whole point of emitting it, since the check database is built by harvesting these lines.
        // iid and path are what make a repeated key readable. A key logged five times is either one
        // pickup seen five times or five pickups sharing a key that is not an identity, and only the
        // instance id separates those two cases.
        SilksongModdingPlugin.LogCheck(
            $"[check] pickup key='{check}' idmode={idMode} collect={collectMode} " +
            $"vanilla='{(vanilla ? vanilla.name : "<null>")}' " +
            $"vanillaType={(vanilla ? vanilla.GetType().Name : "-")} " +
            $"unique={(vanilla && vanilla.IsUnique)} " +
            $"iid={pickup.GetInstanceID()} path='{GrantDiagnostics.HierarchyPathOf(pickup.transform)}'");

        if (!RandomizerPlacements.TryGetReplacement(check, out string replacementName))
        {
            return;
        }

        global::CollectableItem? replacement = ResolveReplacement(replacementName, check);
        if (replacement == null)
        {
            return;
        }

        // keepPersistence:true is load-bearing. The default overload argument runs
        // Object.Destroy(persistent), which would destroy the very PersistentBoolItem this check is
        // keyed on — taking the "already collected" record with it, so the check would respawn forever.
        pickup.SetItem(replacement, keepPersistence: true);

        SilksongModdingPlugin.LogCheck(
            $"[check] swap key='{check}' from='{(vanilla ? vanilla.name : "<null>")}' to='{replacement.name}'");
    }

    private static global::CollectableItem? ResolveReplacement(string replacementName, in CheckId check)
    {
        global::CollectableItem? replacement;
        try
        {
            // Resolves through ManagerSingleton<CollectableItemManager>.Instance, which is noisy and can
            // throw if the manager has not spawned yet — hence the guard rather than a null check alone.
            replacement = global::CollectableItemManager.GetItemByName(replacementName);
        }
        catch (Exception error)
        {
            SilksongModdingPlugin.LogCheckWarning(
                $"[check] key='{check}': could not resolve '{replacementName}' yet ({error.GetType().Name}); left vanilla.");
            return null;
        }

        if (!replacement)
        {
            // CollectableItemManager.InternalAddItem opens with `if (!IsItemInMasterList(item) ...) return;`
            // so an item outside the master list is granted into the void, silently.
            SilksongModdingPlugin.LogCheckWarning(
                $"[check] key='{check}': '{replacementName}' is not in the master list; left vanilla.");
            return null;
        }

        return replacement;
    }

    /// <summary>
    /// Derives the check's identity, preferring keys the game itself persists.
    /// </summary>
    private static CheckId DescribeCheck(
        global::CollectableItemPickup pickup,
        global::PersistentBoolItem persistent,
        string playerDataBool,
        out string idMode)
    {
        // Best case: the game's own save key. Reading through the ItemData property matters — it calls
        // EnsureSetup() for us, whereas the raw serialized fields are empty strings until that runs.
        if (persistent)
        {
            global::PersistentItemData<bool> data = persistent.ItemData;
            if (data != null && !string.IsNullOrEmpty(data.ID))
            {
                // EnsureSetup fills a blank ID with the GameObject's name:
                //
                //     if (string.IsNullOrEmpty(itemData.ID)) itemData.ID = base.name;
                //
                // Flagged, but deliberately NOT called unusable. For an object the scene author placed,
                // a name-derived ID is perfectly serviceable — the game persists that object's own state
                // under this exact key, so it has to be unique within the scene or the game itself would
                // break. It is only fatal for objects spawned from a prefab at runtime, where every copy
                // inherits one name. The flag cannot tell those apart on its own; the proof is several
                // distinct instance ids reporting one key, which the harvester checks separately.
                bool looksNameDerived = string.Equals(data.ID, persistent.name, StringComparison.Ordinal);
                idMode = looksNameDerived ? "persistentbool-namederived" : "persistentbool-authored";
                return new CheckId(CheckId.PickupKind, data.SceneName ?? string.Empty, data.ID);
            }
        }

        string scene = BaseSceneNameOf(pickup.gameObject);

        // Second best: a PlayerData flag. Unique-item pickups destroy their PersistentBoolItem in Awake,
        // and SetPlayerDataBool nulls `persistent`, so these legitimately have no persistent-bool key.
        if (!string.IsNullOrEmpty(playerDataBool))
        {
            idMode = "playerdatabool";
            return new CheckId(CheckId.PickupKind, scene, $"pd:{playerDataBool}");
        }

        // Fallback: hierarchy path. Flagged loudly because it is NOT a durable key — spawned drops get it,
        // and their names and sibling order are not guaranteed stable. Do not build placements on these
        // without checking them first.
        idMode = "hierarchy-fallback-UNSTABLE";
        return new CheckId(CheckId.PickupKind, scene, GrantDiagnostics.HierarchyPathOf(pickup.transform));
    }

    private static string BaseSceneNameOf(GameObject owner)
    {
        string raw = owner.scene.name ?? string.Empty;
        try
        {
            // Collapses the game's scene-variant suffixes (_boss, _bellway, ...) onto one name. Note this
            // makes (SceneName, ID) non-injective across variants; collisions get reported, not assumed away.
            return global::GameManager.GetBaseSceneName(raw);
        }
        catch (Exception)
        {
            return raw;
        }
    }
}

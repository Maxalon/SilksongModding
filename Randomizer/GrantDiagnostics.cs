using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace SilksongModding.Randomizer;

/// <summary>
/// Shared helpers for the discovery probes that answer "what kind of check is this, and what owns it?".
/// </summary>
internal static class GrantDiagnostics
{
    /// <summary>
    /// Names the game code on the current call stack, nearest frame first.
    /// </summary>
    /// <remarks>
    /// The chokepoints worth probing are shared by every archetype, so the item alone says nothing about
    /// which one produced it — the caller chain is the distinguishing information. Unity, Harmony and
    /// MonoMod frames are dropped because the plumbing between game frames tells us nothing.
    /// </remarks>
    internal static string DescribeCallers()
    {
        StackTrace stack = new(fNeedFileInfo: false);
        StringBuilder described = new();

        for (int index = 0; index < stack.FrameCount && described.Length < 300; index++)
        {
            System.Reflection.MethodBase? method = stack.GetFrame(index)?.GetMethod();
            Type? owner = method?.DeclaringType;
            if (owner == null)
            {
                continue;
            }

            string ns = owner.Namespace ?? string.Empty;
            if (ns.StartsWith("System", StringComparison.Ordinal)
                || ns.StartsWith("UnityEngine", StringComparison.Ordinal)
                || ns.StartsWith("HarmonyLib", StringComparison.Ordinal)
                || ns.StartsWith("MonoMod", StringComparison.Ordinal)
                || owner == typeof(GrantDiagnostics)
                || owner.Namespace == typeof(GrantDiagnostics).Namespace)
            {
                continue;
            }

            if (described.Length > 0)
            {
                described.Append(" < ");
            }

            described.Append(owner.Name).Append('.').Append(method!.Name);
        }

        return described.Length > 0 ? described.ToString() : "<no game frames>";
    }

    internal static string HierarchyPathOf(Transform leaf)
    {
        StringBuilder path = new(leaf.name);
        for (Transform parent = leaf.parent; parent != null; parent = parent.parent)
        {
            path.Insert(0, '/').Insert(0, parent.name);
        }

        return path.ToString();
    }

    /// <summary>
    /// Describes the scene object a grant belongs to, and whether it carries a usable check key.
    /// </summary>
    /// <remarks>
    /// This is the question the first two discovery runs turned into the central one. Loot pickups are
    /// spawned copies of a prefab and share one another's persistent ID, so they cannot be keyed
    /// individually — but the thing that <i>produced</i> them (a breakable prop, an enemy, an FSM object)
    /// is authored into the scene and may well have a real key. Whether it does is what this reports.
    /// </remarks>
    internal static string DescribeOwnerIdentity(GameObject? owner)
    {
        if (!owner)
        {
            return "owner='<destroyed>' ownerKey='<none>'";
        }

        // Deliberately distinguishes the three "no usable key" cases from each other. "No PersistentBoolItem
        // anywhere up the chain" and "one exists but its ID is prefab-derived" call for completely
        // different responses, and collapsing both to <none> would hide that.
        string key = "<no-persistentbool>";
        string keyFrom = "-";

        global::PersistentBoolItem persistent =
            owner!.GetComponentInParent<global::PersistentBoolItem>(includeInactive: true);

        if (persistent)
        {
            keyFrom = persistent.name;
            global::PersistentItemData<bool> data = persistent.ItemData;
            key = data == null || string.IsNullOrEmpty(data.ID)
                ? "<empty-id>"
                // Same caveat as the pickup probe: an ID equal to the owning object's name was filled in
                // by EnsureSetup rather than authored. Usable for a scene-placed object, fatal for a
                // runtime copy — the tag says which question to ask, not which answer to give.
                : $"{data.SceneName}:{data.ID}"
                  + (string.Equals(data.ID, persistent.name, StringComparison.Ordinal) ? " NAMEDERIVED" : string.Empty);
        }

        // keyFrom matters as much as the key. GetComponentInParent walks upward, so a usable key may come
        // from an ancestor rather than the granting object itself — a berry parented under the bush that
        // spawned it would report the bush's key, and that ancestor is the real check.
        return $"owner='{owner.name}' ownerPath='{HierarchyPathOf(owner.transform)}' " +
               $"ownerKey='{key}' keyFrom='{keyFrom}' ownerComponents='{DescribeComponents(owner)}'";
    }

    /// <summary>
    /// Renders a <c>Breakable</c>'s serialized loot table.
    /// </summary>
    /// <remarks>
    /// Reflective because the entries are private nested types whose item member sits on a generic base
    /// (<c>ProbabilityBase&lt;T&gt;</c>), so it is reached by walking the hierarchy and accepting either a
    /// field or a property rather than by naming the type.
    /// </remarks>
    internal static string DescribeDropGroups(global::Breakable? breakable) =>
        !breakable ? "<none>" : DescribeDropGroups((object?)breakable!.itemDropGroups);

    /// <summary>
    /// Renders any serialized loot table shaped as "groups of drops, each drop naming an item".
    /// </summary>
    /// <remarks>
    /// <c>Breakable</c> and <c>HealthManager</c> both use that shape but with unrelated private nested
    /// types, one an array and the other a list, so this walks by member name rather than by type.
    /// Reaching the item also means climbing to a generic base (<c>ProbabilityBase&lt;T&gt;</c>), and the
    /// member is a field in one and a property in the other.
    /// </remarks>
    internal static string DescribeDropGroups(object? groups)
    {
        if (groups is not System.Collections.IEnumerable groupList)
        {
            return "<none>";
        }

        StringBuilder described = new();
        foreach (object? group in groupList)
        {
            if (group == null || ValueOfMember(group, "Drops") is not System.Collections.IEnumerable drops)
            {
                continue;
            }

            foreach (object? drop in drops)
            {
                if (drop == null)
                {
                    continue;
                }

                if (described.Length > 0)
                {
                    described.Append(',');
                }

                described.Append('\'')
                         .Append(ValueOfMember(drop, "Item") is UnityEngine.Object named && named
                             ? named.name
                             : "<null>")
                         .Append('\'');
            }
        }

        return described.Length > 0 ? described.ToString() : "<empty>";
    }

    /// <summary>Reads a named field or property, climbing base types, whatever its accessibility.</summary>
    internal static object? ValueOfMember(object owner, string member)
    {
        const System.Reflection.BindingFlags Lookup =
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.DeclaredOnly;

        for (Type? declaring = owner.GetType(); declaring != null; declaring = declaring.BaseType)
        {
            object? value = declaring.GetField(member, Lookup)?.GetValue(owner)
                            ?? declaring.GetProperty(member, Lookup)?.GetValue(owner);
            if (value != null)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Lists the component types on an object, which is the quickest read on what archetype it is.
    /// </summary>
    private static string DescribeComponents(GameObject owner)
    {
        // Long enough for a real enemy or prop. The previous cap silently dropped the tail of the list,
        // which hid a PersistentBoolItem on an enemy and very nearly produced the wrong conclusion about
        // whether its drop could be a check. Truncation is now stated rather than silent.
        const int Budget = 700;

        StringBuilder described = new();
        int omitted = 0;

        foreach (Component component in owner.GetComponents<Component>())
        {
            if (!component)
            {
                continue;
            }

            if (described.Length > Budget)
            {
                omitted++;
                continue;
            }

            if (described.Length > 0)
            {
                described.Append(',');
            }

            described.Append(component.GetType().Name);
        }

        if (omitted > 0)
        {
            described.Append(",...+").Append(omitted).Append("-more");
        }

        return described.ToString();
    }
}

/// <summary>
/// Logs every item the game actually grants, and what granted it.
/// </summary>
/// <remarks>
/// <c>CollectableItemManager.AddItem</c> is the chokepoint every grant funnels through, which makes it
/// useless for <i>placing</i> a check — by the time execution arrives the location context is gone — but
/// ideal for answering "what archetype is this?". It is what identified mossberries as FSM-granted rather
/// than a pickup, after two playthroughs in which they never appeared at all.
/// <para>
/// The stack walk is the expensive part, so this stays a discovery-time probe: grants happen a handful of
/// times a minute, not per frame.
/// </para>
/// </remarks>
[HarmonyPatch(typeof(global::CollectableItemManager), nameof(global::CollectableItemManager.AddItem))]
[DiscoveryProbe]
internal static class ItemGrantProbe
{
    [HarmonyPostfix]
    private static void Postfix(global::CollectableItem item, int amount)
    {
        try
        {
            SilksongModdingPlugin.LogCheck(
                $"[grant] item='{(item ? item.name : "<null>")}' " +
                $"itemType={(item ? item.GetType().Name : "-")} amount={amount} " +
                $"via='{GrantDiagnostics.DescribeCallers()}'");
        }
        catch (Exception error)
        {
            SilksongModdingPlugin.LogCheckWarning($"[grant] probe failed: {error}");
        }
    }
}

/// <summary>
/// The FSM-granted archetype — mossberries and anything else handed over by a PlayMaker action.
/// </summary>
/// <remarks>
/// <para>
/// These never pass through <c>CollectableItemPickup</c> at all. The item is a serialized <c>FsmObject</c>
/// on the action itself, and the grant happens in <c>CollectableItemAction.OnEnter</c> when the FSM
/// reaches that state. Randomizing one therefore means replacing <c>Item.Value</c> rather than calling
/// <c>SetItem</c>, and keying one means identifying the FSM's owning GameObject — which is what this logs.
/// </para>
/// <para>
/// Patching the abstract base's <c>OnEnter</c> covers every subclass, because subclasses override
/// <c>DoAction</c> rather than <c>OnEnter</c>.
/// </para>
/// </remarks>
[HarmonyPatch(typeof(global::HutongGames.PlayMaker.Actions.CollectableItemAction), "OnEnter")]
internal static class FsmItemGrantProbe
{
    [HarmonyPrefix]
    private static void Prefix(global::HutongGames.PlayMaker.Actions.CollectableItemAction __instance)
    {
        try
        {
            // Six actions derive from this base, and three of them (CollectableItemGetData*) only read
            // item data — they change nothing and would just add noise to a focused discovery run. The
            // mutating ones (Collect, Take) are what a randomizer has to care about. Drop the filter if a
            // question ever turns on what merely *reads* an item.
            if (__instance.GetType().Name.StartsWith("CollectableItemGetData", StringComparison.Ordinal))
            {
                return;
            }

            object? value = __instance.Item?.Value;
            global::CollectableItem? item = value as global::CollectableItem;

            // Placement for the FSM archetype happens right here, and this is the reason to hook OnEnter
            // rather than pre-swapping the serialized value. OnEnter reads the item and hands it straight
            // to DoAction:
            //
            //     CollectableItem collectableItem = Item.Value as CollectableItem;
            //     if (collectableItem != null) DoAction(collectableItem);
            //
            // so replacing Item.Value in a prefix is read by the very next statement and cannot be undone
            // by the action reinitialising itself, which a pre-swap on a pooled object could be.
            if (item && SilksongModdingPlugin.TryGetFsmItemSwap(item!.name, out string toName))
            {
                global::CollectableItem? replacement = ResolveItem(toName);
                if (replacement)
                {
                    __instance.Item!.Value = replacement;
                    SilksongModdingPlugin.LogCheck(
                        $"[fsmswap] '{item.name}' -> '{replacement!.name}' on '{__instance.Owner?.name}' " +
                        $"fsm='{__instance.Fsm?.Name}' state='{__instance.State?.Name}'");
                    item = replacement;
                }
            }

            SilksongModdingPlugin.LogCheck(
                $"[fsm] action={__instance.GetType().Name} " +
                $"item='{(item ? item!.name : "<null>")}' " +
                $"fsm='{__instance.Fsm?.Name ?? "<none>"}' state='{__instance.State?.Name ?? "<none>"}' " +
                $"{GrantDiagnostics.DescribeOwnerIdentity(__instance.Owner)}");
        }
        catch (Exception error)
        {
            SilksongModdingPlugin.LogCheckWarning($"[fsm] probe failed: {error}");
        }
    }

    private static global::CollectableItem? ResolveItem(string name)
    {
        try
        {
            // Resolves through the item manager's master list. An item outside it is granted into the
            // void silently, so a failure here has to leave the vanilla item alone rather than guess.
            return global::CollectableItemManager.GetItemByName(name);
        }
        catch (Exception error)
        {
            SilksongModdingPlugin.LogCheckWarning(
                $"[fsmswap] could not resolve '{name}' ({error.GetType().Name}); left vanilla.");
            return null;
        }
    }
}

/// <summary>
/// Identifies what spawned each pickup, which is the missing half of the loot archetype.
/// </summary>
/// <remarks>
/// Every spawner calls <c>SetItem</c> immediately after instantiating a pickup, so the spawner is still on
/// the stack here. That matters because spawned pickups cannot be keyed on themselves — five statue drops
/// in one run all reported the identical persistent ID — so the check identity has to come from whatever
/// produced them, and this is what names it.
/// </remarks>
[HarmonyPatch(typeof(global::CollectableItemPickup), nameof(global::CollectableItemPickup.SetItem))]
[DiscoveryProbe]
internal static class PickupSpawnProbe
{
    [HarmonyPostfix]
    private static void Postfix(global::CollectableItemPickup __instance, global::SavedItem newItem)
    {
        try
        {
            SilksongModdingPlugin.LogCheck(
                $"[spawn] pickup='{(__instance ? __instance.name : "<destroyed>")}' " +
                $"iid={(__instance ? __instance.GetInstanceID() : 0)} " +
                $"item='{(newItem ? newItem.name : "<null>")}' " +
                $"by='{GrantDiagnostics.DescribeCallers()}'");
        }
        catch (Exception error)
        {
            SilksongModdingPlugin.LogCheckWarning($"[spawn] probe failed: {error}");
        }
    }
}

/// <summary>
/// Breakable props — the leading candidate for what a spawned pickup's check should actually be keyed on.
/// </summary>
/// <remarks>
/// <para>
/// <c>Breakable</c> is the first source archetype seen that carries real persistence: it owns a
/// <c>PersistentBoolItem</c> and saves its own <c>isBroken</c> through <c>OnGetSaveState</c>. If the
/// mossberry bush is one of these, its key is the check key, and the berry's total lack of identity stops
/// mattering — the check is "this bush", not "that berry".
/// </para>
/// <para>
/// The drop groups are logged too, because a <c>Breakable</c> that carries them spawns a
/// <c>CollectableItemPickup</c> and calls <c>SetItem</c>, which is a placement point we already know how
/// to use. An empty group list means this prop hands out its loot some other way — through
/// <c>onBreak</c> or its own FSM — and needs a different placement mechanism even though the key is fine.
/// </para>
/// </remarks>
[HarmonyPatch(typeof(global::Breakable), nameof(global::Breakable.Break),
    new[] { typeof(float), typeof(float), typeof(float) })]
[DiscoveryProbe]
internal static class BreakableProbe
{
    [HarmonyPostfix]
    private static void Postfix(global::Breakable __instance)
    {
        try
        {
            SilksongModdingPlugin.LogCheck(
                $"[breakable] {GrantDiagnostics.DescribeOwnerIdentity(__instance ? __instance.gameObject : null)} " +
                $"hitsToBreak={(__instance ? __instance.hitsToBreak : -1)} " +
                $"dropGroups='{GrantDiagnostics.DescribeDropGroups(__instance)}'");
        }
        catch (Exception error)
        {
            SilksongModdingPlugin.LogCheckWarning($"[breakable] probe failed: {error}");
        }
    }
}

/// <summary>
/// Names whatever spawns a pooled pickup, which is the one thing the mossberry run could not establish.
/// </summary>
/// <remarks>
/// <para>
/// The berry arrives as <c>Mossberry Pickup(Clone)</c> with no <c>PersistentBoolItem</c> anywhere on its
/// chain and a <c>PersonalObjectPool</c> on it, so it is a pooled prefab copy and can never be a check in
/// its own right. The bush that produced it never appears in any other probe: it is not a
/// <c>CollectableItemPickup</c>, and it did not spawn one, so neither the pickup nor the spawn probe sees
/// it. Catching the pooled spawn is what connects the berry back to its source.
/// </para>
/// <para>
/// Pooling is used for a great deal more than pickups — effects, projectiles, audio — so this is filtered
/// hard by prefab name. The filter is a heuristic, not a guarantee: widen it if a pickup archetype turns up
/// that is not named for what it is.
/// </para>
/// </remarks>
[HarmonyPatch(typeof(global::ObjectPool), nameof(global::ObjectPool.Spawn),
    new[] { typeof(GameObject), typeof(Transform), typeof(Vector3), typeof(Quaternion), typeof(bool) })]
[DiscoveryProbe]
internal static class PooledPickupSpawnProbe
{
    [HarmonyPostfix]
    private static void Postfix(GameObject prefab, GameObject __result)
    {
        try
        {
            if (!prefab || !LooksLikeAPickup(prefab.name))
            {
                return;
            }

            SilksongModdingPlugin.LogCheck(
                $"[pooled] prefab='{prefab.name}' " +
                $"spawned='{(__result ? __result.name : "<null>")}' " +
                $"by='{GrantDiagnostics.DescribeCallers()}'");
        }
        catch (Exception error)
        {
            SilksongModdingPlugin.LogCheckWarning($"[pooled] probe failed: {error}");
        }
    }

    private static bool LooksLikeAPickup(string name) =>
        name.IndexOf("Pickup", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("Berry", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("Shard", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("Rosary", StringComparison.OrdinalIgnoreCase) >= 0;
}

/// <summary>
/// Logs what the player actually hits, which is the most direct way to identify a destructible prop.
/// </summary>
/// <remarks>
/// <para>
/// The mossberry bush appears in no other probe: it is not a <c>CollectableItemPickup</c>, it did not
/// spawn one, and it is not a <c>Breakable</c> — the breakable probe stayed silent even for a wall that
/// visibly broke, so these props are FSM-driven rather than component-driven. Asking "what did the player
/// slash?" sidesteps having to guess which system owns them.
/// </para>
/// <para>
/// <c>HitTaker.Hit</c> is the funnel: both other overloads delegate to this one, so patching it alone
/// catches every hit in the game. That also makes it noisy in combat — it is a discovery-time probe, and
/// it earns its keep on a deliberately minimal route rather than during play.
/// </para>
/// </remarks>
[HarmonyPatch(typeof(global::HitTaker), nameof(global::HitTaker.Hit),
    new[] { typeof(GameObject), typeof(global::HitInstance), typeof(int), typeof(HashSet<global::IHitResponder>) })]
[DiscoveryProbe]
internal static class HitProbe
{
    [HarmonyPrefix]
    private static void Prefix(GameObject targetGameObject)
    {
        try
        {
            if (!targetGameObject)
            {
                return;
            }

            SilksongModdingPlugin.LogCheck($"[hit] {GrantDiagnostics.DescribeOwnerIdentity(targetGameObject)}");
        }
        catch (Exception error)
        {
            SilksongModdingPlugin.LogCheckWarning($"[hit] probe failed: {error}");
        }
    }
}

/// <summary>
/// Identifies the scene object whose FSM spawns a pickup, by probing PlayMaker's spawn actions.
/// </summary>
/// <remarks>
/// <para>
/// The berry is instantiated rather than taken from the global pool — the <c>PersonalObjectPool</c> on it
/// is a pool manager it carries for its own use, not evidence that it came from one, which is why the
/// pooled-spawn probe saw nothing. Silksong inherits Hollow Knight's habit of driving props from PlayMaker,
/// so the spawn almost certainly happens in one of these actions.
/// </para>
/// <para>
/// The point of patching the action rather than the spawn itself is <c>FsmStateAction.Owner</c>: it names
/// the GameObject whose FSM is running, which is the bush. A stack walk cannot recover that, because a
/// stack frame names a type, not an instance.
/// </para>
/// </remarks>
[HarmonyPatch]
[DiscoveryProbe]
internal static class FsmSpawnProbe
{
    private static readonly string[] SpawnActions =
    {
        "HutongGames.PlayMaker.Actions.SpawnObjectFromGlobalPool",
        "HutongGames.PlayMaker.Actions.SpawnObjectFromGlobalPoolV2",
        "HutongGames.PlayMaker.Actions.SpawnObjectFromGlobalPoolDelay",
        "HutongGames.PlayMaker.Actions.SpawnObjectFromGlobalPoolOverTime",
        "HutongGames.PlayMaker.Actions.SpawnObjectFromGlobalPoolOverTimeV2",
        "HutongGames.PlayMaker.Actions.CreateObject",
        "HutongGames.PlayMaker.Actions.CreateObjectV2",
        "HutongGames.PlayMaker.Actions.CreateObjectsRandom",
        "HutongGames.PlayMaker.Actions.SpawnFromPool",
        "HutongGames.PlayMaker.Actions.SpawnFromPoolV2",
    };

    [HarmonyTargetMethods]
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        foreach (string actionName in SpawnActions)
        {
            Type? action = AccessTools.TypeByName(actionName);
            if (action == null)
            {
                continue;
            }

            // DeclaredMethod, not Method: each action overrides OnEnter, and the base implementation is
            // shared by every action in the game — patching that would fire constantly and tell us nothing.
            System.Reflection.MethodBase? onEnter = AccessTools.DeclaredMethod(action, "OnEnter");
            if (onEnter != null)
            {
                yield return onEnter;
            }
        }
    }

    [HarmonyPostfix]
    private static void Postfix(global::HutongGames.PlayMaker.FsmStateAction __instance)
    {
        try
        {
            SilksongModdingPlugin.LogCheck(
                $"[fsmspawn] action={__instance.GetType().Name} " +
                $"fsm='{__instance.Fsm?.Name ?? "<none>"}' state='{__instance.State?.Name ?? "<none>"}' " +
                $"{GrantDiagnostics.DescribeOwnerIdentity(__instance.Owner)}");
        }
        catch (Exception error)
        {
            SilksongModdingPlugin.LogCheckWarning($"[fsmspawn] probe failed: {error}");
        }
    }
}

/// <summary>
/// Watches how the floating mask shards and spool fragments actually hand over their contents.
/// </summary>
/// <remarks>
/// <para>
/// These are a third archetype. They carry a <c>SavedItemTrackerMarker</c> declaring what they give and a
/// <c>PersistentBoolItem</c> to remember being taken, but no <c>CollectableItemPickup</c> and no
/// <c>CollectableItemAction</c> — so neither the pickup patch nor the FSM item patch sees them, and 27 of
/// them (14 mask shards, 13 spool fragments) sit outside the pool.
/// </para>
/// <para>
/// Their FSM's Get state runs <c>SetPlayerDataBool</c> and two <c>CallMethodProper</c> calls, and the
/// parameters of those are packed into PlayMaker's serialized action blob, which the bundle extractor
/// cannot read without its own decoder. At runtime the same actions are ordinary objects with readable
/// fields, so this reports which method is called and which flag is set — the two things needed to know
/// where a substitution belongs.
/// </para>
/// <para>
/// Filtered to owners carrying a <c>SavedItemTrackerMarker</c>. Both actions are used all over the game,
/// and unfiltered they would bury the handful of lines that matter.
/// </para>
/// </remarks>
[DiscoveryProbe]
[HarmonyPatch]
internal static class CollectableCutsceneProbe
{
    [HarmonyTargetMethods]
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        foreach (string name in new[]
                 {
                     // Cutscene plumbing, which is what the first pass turned out to be catching:
                     // GetCState, RelinquishControl, StopAnimationControl, and an isInvincible flag.
                     "HutongGames.PlayMaker.Actions.CallMethodProper",
                     "HutongGames.PlayMaker.Actions.SetPlayerDataBool",
                     "HutongGames.PlayMaker.Actions.AddHeroInputBlocker",

                     // The grant itself. Mask shards and spool fragments raise PlayerData.heartPieces and
                     // PlayerData.silkSpoolParts, and PrefabCollectable/HeartPieceOrb only handle the popup
                     // and the flying-orb effect - so the increment has to be one of these.
                     "HutongGames.PlayMaker.Actions.IncrementPlayerDataInt",
                     "HutongGames.PlayMaker.Actions.PlayerDataIntAdd",
                     "HutongGames.PlayMaker.Actions.SetPlayerDataInt",
                     "HutongGames.PlayMaker.Actions.SendEventByName",
                 })
        {
            Type? action = AccessTools.TypeByName(name);
            System.Reflection.MethodBase? onEnter =
                action == null ? null : AccessTools.DeclaredMethod(action, "OnEnter");
            if (onEnter != null)
            {
                yield return onEnter;
            }
        }
    }

    [HarmonyPostfix]
    private static void Postfix(global::HutongGames.PlayMaker.FsmStateAction __instance)
    {
        try
        {
            GameObject? owner = __instance.Owner;
            if (!owner)
            {
                return;
            }

            // The marker may sit on a parent rather than the object running the FSM: these pickups carry
            // three machines across a small hierarchy, and the first pass only looked at the exact owner,
            // which is one way the grant could have been missed entirely.
            if (!owner!.GetComponentInParent<global::SavedItemTrackerMarker>(includeInactive: true)
                && !owner.GetComponentInChildren<global::SavedItemTrackerMarker>(includeInactive: true))
            {
                return;
            }

            SilksongModdingPlugin.LogCheck(
                $"[cutscene] action={__instance.GetType().Name} " +
                $"fsm='{__instance.Fsm?.Name}' state='{__instance.State?.Name}' " +
                $"{DescribeParameters(__instance)} " +
                $"{GrantDiagnostics.DescribeOwnerIdentity(owner)}");
        }
        catch (Exception error)
        {
            SilksongModdingPlugin.LogCheckWarning($"[cutscene] probe failed: {error}");
        }
    }

    /// <summary>Reports the action's own string fields, which is where the method and flag names live.</summary>
    private static string DescribeParameters(global::HutongGames.PlayMaker.FsmStateAction action)
    {
        StringBuilder described = new();
        foreach (System.Reflection.FieldInfo field in action.GetType().GetFields(
                     System.Reflection.BindingFlags.Instance
                     | System.Reflection.BindingFlags.Public
                     | System.Reflection.BindingFlags.NonPublic))
        {
            object? value = field.GetValue(action);
            string? text = value switch
            {
                global::HutongGames.PlayMaker.FsmString fsmString => fsmString.Value,
                global::HutongGames.PlayMaker.FsmInt fsmInt => fsmInt.Name is { Length: > 0 } named
                    ? named + "=" + fsmInt.Value
                    : fsmInt.Value.ToString(),
                global::HutongGames.PlayMaker.FsmEvent fsmEvent => fsmEvent.Name,
                string plain => plain,
                _ => null,
            };

            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            if (described.Length > 0)
            {
                described.Append(' ');
            }

            described.Append(field.Name).Append("='").Append(text).Append('\'');
        }

        return described.Length > 0 ? described.ToString() : "params=<none>";
    }
}

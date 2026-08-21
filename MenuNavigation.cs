using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SilksongModding;

/// <summary>
/// Controller and keyboard navigation helpers shared by every screen this mod adds.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a visible button is not necessarily a reachable one.</b> A mouse click dispatches straight to
/// whatever is under the pointer, so a button merely has to exist to be clickable. A controller or the
/// keyboard instead walks Unity's <see cref="Navigation"/> graph outward from the currently selected
/// object, so an entry that nothing points at is unreachable no matter how plainly it is drawn. Anything
/// added to a menu therefore has to be linked into navigation explicitly. Since Silksong is played on a
/// controller by default, a screen that only works with a mouse is a broken screen.
/// </para>
/// <para>
/// <b>Why writing <see cref="Navigation"/> directly is not enough on the game's own screens.</b>
/// <c>MenuButtonList.SetupActive</c> rebuilds <c>selectOnUp</c>/<c>selectOnDown</c> for every entry it
/// owns into a wrap-around ring, and <c>UIManager.ShowMenu</c> calls it every single time a menu screen is
/// shown. Anything written onto a <see cref="Selectable"/> from outside that list survives only until the
/// next show. On those screens the entry has to go into the list itself — see
/// <see cref="TryInsertAfter"/>. On screens this mod builds from scratch there is no list to join, and
/// wiring the ring by hand with <see cref="WireVerticalRing"/> is the correct answer.
/// </para>
/// </remarks>
internal static class MenuNavigation
{
    /// <summary>The game-owned navigation list governing <paramref name="member"/>, if there is one.</summary>
    /// <remarks>
    /// Resolved the same way <c>MenuSelectable.OnCancel</c> resolves its own <c>parentList</c>, so this
    /// finds the list the game would consider responsible for the control.
    /// </remarks>
    internal static global::MenuButtonList? FindOwningList(Selectable? member)
    {
        if (!member)
        {
            return null;
        }

        // includeInactive is essential, not defensive. The default overload matches only components on
        // *active* GameObjects, and menus are routinely wired up while their screen is still switched
        // off — the title screen in particular is configured before UIManager activates it. Without this
        // the list is invisible at exactly the moment registration happens.
        return member!.GetComponentInParent<global::MenuButtonList>(includeInactive: true);
    }

    /// <summary>
    /// Renders the ancestor chain of <paramref name="member"/> and where navigation lists sit on it.
    /// </summary>
    /// <remarks>
    /// Which screens carry a <c>MenuButtonList</c> is serialized scene data, so it cannot be read out of
    /// the assembly — it has to be observed at runtime. This exists to answer that question from a log.
    /// </remarks>
    internal static string DescribeAncestry(Selectable? member)
    {
        if (!member)
        {
            return "<none>";
        }

        System.Text.StringBuilder described = new();
        for (Transform? node = member!.transform; node != null; node = node.parent)
        {
            if (described.Length > 0)
            {
                described.Append(" < ");
            }

            described.Append('\'').Append(node.name).Append('\'');
            described.Append(node.gameObject.activeSelf ? "" : "(inactive)");
            if (node.GetComponent<global::MenuButtonList>())
            {
                described.Append("[MenuButtonList]");
            }
        }

        return described.ToString();
    }

    /// <summary>
    /// Registers <paramref name="inserted"/> with a game navigation list, directly after
    /// <paramref name="anchor"/>.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the entry is registered — including when it already was. <c>false</c> when this list
    /// does not own <paramref name="anchor"/>, which makes the call safe to attempt against every list in
    /// order to find the right one.
    /// </returns>
    /// <remarks>
    /// Joining the list rather than hand-wiring navigation buys more than durability. <c>SetupActive</c>
    /// also assigns <c>cancelAction</c> for top-level menus and subscribes the entry to the list's
    /// last-selected tracking, so a registered entry behaves exactly like a native one — it remembers the
    /// cursor position and answers the Back button correctly, both for free.
    /// </remarks>
    internal static bool TryInsertAfter(
        global::MenuButtonList? list,
        Selectable? anchor,
        Selectable? inserted)
    {
        if (!list || !anchor || !inserted)
        {
            return false;
        }

        global::MenuButtonList.Entry[] entries = list!.entries;
        if (entries == null)
        {
            return false;
        }

        int anchorIndex = -1;
        for (int index = 0; index < entries.Length; index++)
        {
            global::MenuButtonList.Entry entry = entries[index];
            if (entry == null)
            {
                continue;
            }

            // SetupActive runs on every menu show, so re-registration attempts are the normal case
            // rather than the exception. Answering "already done" keeps the array from growing.
            if (entry.selectable == inserted)
            {
                return true;
            }

            if (entry.selectable == anchor)
            {
                anchorIndex = index;
            }
        }

        if (anchorIndex < 0)
        {
            return false;
        }

        List<global::MenuButtonList.Entry> rebuilt = new(entries);
        rebuilt.Insert(anchorIndex + 1, new global::MenuButtonList.Entry { selectable = inserted });
        list.entries = rebuilt.ToArray();
        return true;
    }

    /// <summary>
    /// Links a screen's controls into one wrap-around up/down ring.
    /// </summary>
    /// <remarks>
    /// Deliberately the same shape <c>MenuButtonList.SetupActive</c> builds — last wraps to first — so a
    /// hand-built screen feels identical to a native one under the stick. Entries that are null or already
    /// destroyed are dropped, so callers can pass a fixed list without pre-filtering.
    /// </remarks>
    internal static void WireVerticalRing(IReadOnlyList<Selectable?> order)
    {
        List<Selectable> ring = new(order.Count);
        foreach (Selectable? candidate in order)
        {
            if (candidate)
            {
                ring.Add(candidate!);
            }
        }

        if (ring.Count < 2)
        {
            return;
        }

        for (int index = 0; index < ring.Count; index++)
        {
            Navigation navigation = ring[index].navigation;
            navigation.mode = Navigation.Mode.Explicit;
            navigation.selectOnUp = ring[(index + ring.Count - 1) % ring.Count];
            navigation.selectOnDown = ring[(index + 1) % ring.Count];

            // Cloned buttons inherit left/right targets from the screen they came from, which would let a
            // stick flick jump to a control on a different, currently hidden menu.
            navigation.selectOnLeft = null;
            navigation.selectOnRight = null;

            ring[index].navigation = navigation;
        }
    }

    /// <summary>
    /// Routes the Back button (controller B / keyboard Escape) on <paramref name="selectable"/> to
    /// <paramref name="onCancel"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This matters more than it looks. <c>MenuSelectable.OnCancel</c> switches on a serialized
    /// <c>cancelAction</c>, and a button cloned from the title screen's START inherits
    /// <c>GoToExitPrompt</c> along with everything else — so Back on a page of ours would raise the
    /// quit-game prompt. Worse, the built-in actions call <c>UIManager</c> navigation directly, which
    /// would leave a hijacked screen without running its teardown.
    /// </para>
    /// <para>
    /// <c>DoNothing</c> disables that switch, and an <c>EventTrigger</c> Cancel entry supplies our own
    /// behaviour — the extension point the game itself checks for via <c>hasCancelEventTrigger</c>. Both
    /// handlers receive the event (Unity dispatches to every <c>ICancelHandler</c> on the object), which
    /// is why the built-in one has to be neutralised rather than merely ignored.
    /// </para>
    /// </remarks>
    internal static void SetCancelHandler(Selectable? selectable, Action onCancel)
    {
        if (!selectable)
        {
            return;
        }

        // Never reuse an EventTrigger that is already on the object. Cloned buttons have their inherited
        // IEventSystemHandler components stripped by disabling and destroying them, and both halves of
        // that are traps here: Object.Destroy is deferred to the end of the frame, so GetComponent still
        // hands the component back, and ExecuteEvents filters recipients on isActiveAndEnabled, so a
        // disabled one silently never receives the event. Attaching the Cancel entry to that corpse
        // produces exactly the observed failure — MenuSelectable.OnCancel still runs its ForceDeselect
        // because hasCancelEventTrigger was set, so the cursor is cleared, while the handler that was
        // supposed to navigate never fires. Destroying immediately and adding fresh removes the ordering
        // question entirely.
        EventTrigger stale = selectable!.gameObject.GetComponent<EventTrigger>();
        if (stale)
        {
            UnityEngine.Object.DestroyImmediate(stale);
        }

        EventTrigger trigger = selectable.gameObject.AddComponent<EventTrigger>();
        EventTrigger.Entry cancelEntry = new() { eventID = EventTriggerType.Cancel };
        cancelEntry.callback.AddListener(_ => onCancel());
        trigger.triggers.Add(cancelEntry);

        if (selectable is not MenuSelectable menuSelectable)
        {
            return;
        }

        menuSelectable.cancelAction = global::GlobalEnums.CancelAction.DoNothing;

        // MenuSelectable caches these in Awake/OnEnable. Whether that has already happened depends on
        // whether the clone's parent was active at Instantiate time, so assigning them directly keeps the
        // cancel sound and the deselect working either way.
        menuSelectable.eventTrigger = trigger;
        menuSelectable.hasCancelEventTrigger = true;
    }

    /// <summary>Moves the highlight the way the game's own menus do, without the select sound.</summary>
    internal static void Select(Selectable? selectable)
    {
        if (!selectable)
        {
            return;
        }

        global::UIManager.HighlightSelectableNoSound(selectable);
    }

    /// <summary>
    /// Takes a control out of the navigation graph, leaving it reachable only by pointer.
    /// </summary>
    /// <remarks>
    /// Mode <c>None</c> means nothing can navigate <i>into</i> it and it hands off <i>nowhere</i>. That
    /// second half is the point for a text field: while one is focused it consumes the D-pad for caret
    /// movement, so a field left inside a ring becomes a trap the cursor cannot leave.
    /// </remarks>
    internal static void ExcludeFromRing(Selectable? selectable)
    {
        if (!selectable)
        {
            return;
        }

        Navigation navigation = selectable!.navigation;
        navigation.mode = Navigation.Mode.None;
        selectable.navigation = navigation;
    }

    /// <summary>True when anything at all currently holds the selection.</summary>
    /// <remarks>
    /// A null selection is a dead end for a controller: navigation walks outward from the selected
    /// object, so with nothing selected there is no direction to walk in and the screen stops responding.
    /// </remarks>
    internal static bool HasSelection()
    {
        return EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null;
    }

    /// <summary>True when <paramref name="selectable"/> already holds the selection.</summary>
    internal static bool IsSelected(Selectable? selectable)
    {
        if (!selectable || EventSystem.current == null)
        {
            return false;
        }

        return EventSystem.current.currentSelectedGameObject == selectable!.gameObject;
    }
}

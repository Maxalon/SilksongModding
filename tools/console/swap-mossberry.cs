// Proves (or disproves) the FSM placement mechanism, without writing any plugin code for it.
//
// A mossberry's item is a serialized FsmObject on a CollectableItemCollect action inside the berry's
// FSM. Everything else about that archetype is understood; the open question is simply whether
// overwriting Item.Value before the state runs actually changes what the player receives.
//
// SUPERSEDED by the plugin's Discovery/FsmItemSwap config, which does the same substitution from a
// prefix on CollectableItemAction.OnEnter and needs no console interaction. Kept as a worked example of
// what the console is for, and as the fallback if a question ever needs answering without a rebuild.
//
// To run it: F7 -> C# Console, clear the editor, paste, then click the green "Compile" button, which is
// this console's Run. (The dropdown beside it is a Help menu that inserts example snippets and will
// overwrite the editor - do not pick from it.) Do this in the mossberry room BEFORE slashing the bush.
//   - Granted item is Rosary_Set_Frayed -> the mechanism works, and the archetype is randomizable.
//   - Granted item is still Mossberry   -> the action re-reads its item on entry, and placement has to
//                                          hook CollectableItemAction.OnEnter instead.
//
// Safe to run: it edits an in-memory FSM action on a pooled object. Nothing here is persisted, and the
// swap is undone by reloading the scene.

// No early `return` anywhere below: the console evaluates this as a statement block, not a method body,
// so a top-level return may be rejected outright.
var replacement = CollectableItemManager.GetItemByName("Rosary_Set_Frayed");
int swapped = 0;
int inspected = 0;

if (!replacement)
{
    Log("[console] replacement item not found; is the name right?");
    UnityEngine.Debug.Log("[console] replacement item not found; is the name right?");
}
else
{
foreach (var machine in UnityEngine.Resources.FindObjectsOfTypeAll<PlayMakerFSM>())
{
    // Scene objects only: FindObjectsOfTypeAll also returns prefabs and assets.
    if (!machine || !machine.gameObject.scene.IsValid() || machine.Fsm == null)
        continue;

    var states = machine.FsmStates;
    if (states == null)
        continue;

    foreach (var state in states)
    {
        var actions = state == null ? null : state.Actions;
        if (actions == null)
            continue;

        foreach (var action in actions)
        {
            var itemAction = action as HutongGames.PlayMaker.Actions.CollectableItemAction;
            if (itemAction == null || itemAction.Item == null)
                continue;

            inspected++;
            var current = itemAction.Item.Value as CollectableItem;
            if (current == null || current.name != "Mossberry")
                continue;

            itemAction.Item.Value = replacement;
            swapped++;
            var where = "[console] swapped on '" + machine.gameObject.name
                + "' fsm='" + machine.FsmName + "' state='" + state.Name + "'";
            Log(where);
            UnityEngine.Debug.Log(where);
        }
    }
}

}

var verdict = "[console] item actions inspected=" + inspected + " swapped=" + swapped
    + (swapped > 0 ? " -- OK, now slash the bush" : " -- nothing to swap (already done, or wrong room)");
Log(verdict);
UnityEngine.Debug.Log(verdict);

// A bare expression last, so the console's output box shows a verdict. Without one the box reads
// "no output" even on a completely successful run, which reads exactly like the script never fired.
verdict

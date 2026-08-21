// Tests the OTHER archetype: a scene-placed CollectableItemPickup, the counterpart to the FSM pickup
// proved by swap-mossberry.cs. Swaps every pickup granting a Rosary_Set_Frayed to grant a Mossberry.
//
// Run it (F7 -> C# Console, clear editor, paste, click the green Compile) while standing next to the
// rosary string pickup, then interact with it.
//   - You receive a Mossberry -> this archetype is randomizable too, and both are now proven.
//   - You receive a rosary    -> the pickup caches its item somewhere else and placement has to hook the
//                                grant instead.
//
// keepPersistence: true is not optional. The default overload runs Object.Destroy(persistent), which
// would destroy the very PersistentBoolItem the check is keyed on - taking the "already collected"
// record with it, so the pickup would respawn forever.
//
// Safe to run: it changes an in-memory field. Nothing is written to the save, and reloading the scene
// restores the vanilla item.

var replacement = CollectableItemManager.GetItemByName("Mossberry");
int swapped = 0;
int seen = 0;

if (!replacement)
{
    Log("[console] 'Mossberry' is not in the master list; aborting.");
}
else
{
foreach (var pickup in UnityEngine.Resources.FindObjectsOfTypeAll<CollectableItemPickup>())
{
    // Scene objects only: FindObjectsOfTypeAll also returns prefabs and assets.
    if (!pickup || !pickup.gameObject.scene.IsValid())
        continue;

    seen++;
    var current = pickup.Item;
    var currentName = current ? current.name : "<null>";
    var line = "[console] pickup '" + pickup.gameObject.name + "' in '"
        + pickup.gameObject.scene.name + "' grants '" + currentName + "'"
        + (pickup.gameObject.activeInHierarchy ? "" : " (inactive)");
    Log(line);
    UnityEngine.Debug.Log(line);

    if (currentName != "Rosary_Set_Frayed")
        continue;

    pickup.SetItem(replacement, true);
    swapped++;

    var done = "[console] swapped '" + pickup.gameObject.name + "' -> '" + replacement.name + "'";
    Log(done);
    UnityEngine.Debug.Log(done);
}
}

var verdict = "[console] pickups seen=" + seen + " swapped=" + swapped
    + (swapped > 0 ? " -- OK, now interact with the pickup" : " -- nothing matched (wrong room, or already collected)");
Log(verdict);
UnityEngine.Debug.Log(verdict);

// Bare expression last, so the output box shows a verdict instead of "no output".
verdict

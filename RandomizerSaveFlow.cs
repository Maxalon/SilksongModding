namespace SilksongModding;

/// <summary>
/// Carries an explicitly chosen seed from the setup page to exactly one empty
/// profile slot. It never changes the behaviour of a regular title-menu run.
/// </summary>
internal static class RandomizerSaveFlow
{
    private static string? pendingSeed;
    private static bool suppressSlotControls;
    private static bool controlReleaseScheduled;

    internal static bool IsCreatingRandomizedSave => pendingSeed != null;

    internal static bool ShouldSuppressSlotControls => suppressSlotControls;

    internal static void Begin(string seed)
    {
        pendingSeed = seed;
        suppressSlotControls = true;
    }

    internal static void CancelPendingSeed()
    {
        pendingSeed = null;
        ReleaseSlotControlsAfterProfileScreenHides();
    }

    internal static bool TryHandleSlotSelection(global::UnityEngine.UI.SaveSlotButton slot)
    {
        if (pendingSeed == null)
        {
            return true;
        }

        if (slot.saveFileState != global::UnityEngine.UI.SaveSlotButton.SaveFileStates.Empty)
        {
            // During randomized-save creation, occupied slots deliberately act as
            // their native clear action. Empty slots retain the game's normal New
            // Game behavior below.
            slot.ClearSavePrompt();
            return false;
        }

        string seed = pendingSeed;
        CancelPendingSeed();
        return SilksongModdingPlugin.TryAttachRandomizerData(slot.SaveSlotIndex, seed);
    }

    private static void ReleaseSlotControlsAfterProfileScreenHides()
    {
        if (!suppressSlotControls || controlReleaseScheduled)
        {
            return;
        }

        controlReleaseScheduled = true;
        SilksongModdingPlugin.StartManagedCoroutine(ReleaseSlotControlsWhenHidden());
    }

    private static System.Collections.IEnumerator ReleaseSlotControlsWhenHidden()
    {
        global::UIManager? ui = global::UIManager.instance;
        while (ui != null && ui.saveProfileScreen.gameObject.activeInHierarchy)
        {
            yield return null;
        }

        suppressSlotControls = false;
        controlReleaseScheduled = false;
    }
}

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
    private static bool returningToSeedPage;

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
        returningToSeedPage = false;
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

            // Suppressing the original OnSubmit means MenuButton's own presentation never runs, so the
            // two parts of it that matter are reproduced here — exactly what the native ClearSaveButton
            // does (`base.OnSubmit(eventData); ForceDeselect();`).
            slot.PlaySubmitSound();

            // ForceDeselect is load-bearing, not polish. The prompt claims the cursor through
            // PreselectOption.HighlightDefault, which is called with deselect:false and therefore returns
            // early if anything is still selected:
            //
            //     if (!deselect && current.currentSelectedGameObject != null
            //         && current.currentSelectedGameObject.activeInHierarchy) return;
            //
            // Leave the slot selected and the prompt silently declines the selection, so the cursor stays
            // on the save-slot ring and the yes/no buttons cannot be reached at all. ForceDeselect also
            // sets deselectWasForced, which is what stops MenuSelectable.ValidateDeselect from restoring
            // the slot a frame later via SetSelectedGameObject(prevSelectedObject).
            slot.ForceDeselect();

            slot.ClearSavePrompt();
            return false;
        }

        string seed = pendingSeed;
        CancelPendingSeed();
        return SilksongModdingPlugin.TryAttachRandomizerData(slot.SaveSlotIndex, seed);
    }

    /// <summary>
    /// Sends Back on the save-slot screen to the seed page instead of the title menu, but only while a
    /// randomized save is actually being created.
    /// </summary>
    /// <returns><c>true</c> if this took over the navigation and the caller should not run.</returns>
    /// <remarks>
    /// <para>
    /// The slot buttons carry <c>CancelAction.GoToMainMenu</c>, and <c>UIGoBack</c> has no
    /// <c>SAVE_PROFILES</c> case, so Back falls through to <c>UIGoToMainMenu</c> and leaves the flow
    /// entirely — discarding the seed on the way out. Within this flow the slot screen is the second step
    /// of a two-step wizard, so Back belongs one step up.
    /// </para>
    /// <para>
    /// The profile screen is torn down explicitly first. <c>GoToOptionsMenu</c> only knows how to fade out
    /// the main menu and a handful of options sub-screens; arriving from <c>SAVE_PROFILES</c> it would
    /// match none of them and simply layer the seed page over a still-visible slot screen.
    /// </para>
    /// </remarks>
    internal static bool TryHandleProfileBack(global::UIManager ui)
    {
        if (pendingSeed is not { } seed)
        {
            return false;
        }

        if (!ui || ui.menuState != global::GlobalEnums.MainMenuState.SAVE_PROFILES)
        {
            return false;
        }

        // Nothing stops UI input during the teardown below, so a second Back can arrive mid-transition.
        // Swallow it: starting a second teardown would build two seed pages, and declining it would drop
        // the player out to the title menu halfway through leaving.
        if (returningToSeedPage)
        {
            return true;
        }

        returningToSeedPage = true;
        SilksongModdingPlugin.StartManagedCoroutine(ReturnToSeedPage(ui, seed));
        return true;
    }

    private static System.Collections.IEnumerator ReturnToSeedPage(global::UIManager ui, string seed)
    {
        try
        {
            // The game's own teardown, so the slots animate away exactly as they do on any other exit.
            yield return ui.StartCoroutine(ui.HideSaveProfileMenu(updateBlackThread: true));

            // The seed stays pending across this, so returning to the slots later still works.
            RandomizerSetupMenu.Show(seed);
        }
        finally
        {
            // In a finally so a failure part-way cannot leave Back permanently swallowed, which would
            // strand the player on the slot screen with no way out but picking a slot.
            returningToSeedPage = false;
        }
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

        // Wait on the menu state, not on the screen object. HideSaveProfileMenu fades the profile screen
        // out with `disable: false`, so its GameObject stays active at alpha zero — an activeInHierarchy
        // test never becomes false and this loop would spin forever, leaving the slot controls suppressed
        // for the rest of the session. Every route off that screen changes the menu state, so that is the
        // condition that actually means "no longer showing".
        while (ui && ui!.menuState == global::GlobalEnums.MainMenuState.SAVE_PROFILES)
        {
            yield return null;
        }

        suppressSlotControls = false;
        controlReleaseScheduled = false;
    }
}

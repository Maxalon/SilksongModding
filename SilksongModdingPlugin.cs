using System.Collections;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Silksong.DataManager;
using SilksongModding.Randomizer;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SilksongModding;

[BepInAutoPlugin(id: "io.github.hfellisch.silksongmodding")]
[BepInDependency("org.silksong-modding.datamanager")]
public partial class SilksongModdingPlugin : BaseUnityPlugin, IOnceSaveDataMod<RandomizerSaveData>
{
    private Harmony? harmony;

    /// <summary>
    /// Whether entering a scene writes a check-candidate dump for it.
    /// </summary>
    /// <remarks>
    /// Opt-in because dumping reads every PlayMaker machine in the scene, and reading a machine's states
    /// deserializes its actions early (see <see cref="Randomizer.SceneDump"/>). Harmless, but not work to
    /// do on every scene transition of an ordinary playthrough.
    /// </remarks>
    private BepInEx.Configuration.ConfigEntry<bool>? dumpScenes;

    /// <summary>
    /// A single item substitution applied to PlayMaker-granted pickups, as <c>From=To</c>.
    /// </summary>
    /// <remarks>
    /// Stands in for a placement table while there is no fill algorithm, purely to establish that the FSM
    /// archetype can be randomized at all. Empty disables it, which is the default: this changes what the
    /// player receives and has no business running unasked.
    /// </remarks>
    private static BepInEx.Configuration.ConfigEntry<string>? fsmItemSwap;
    private static ManualLogSource? log;
    private static SilksongModdingPlugin? pluginInstance;

    /// <summary>
    /// DataManager writes this once after a randomized game begins and reloads it
    /// whenever that save is loaded again.
    /// </summary>
    public RandomizerSaveData? OnceSaveData { get; set; }

    private void Awake()
    {
        pluginInstance = this;
        log = Logger;
        harmony = new Harmony(Id);
        ApplyPatchesIndependently(harmony);

        dumpScenes = Config.Bind(
            "Discovery",
            "DumpScenesOnLoad",
            true,
            "Write a list of every item-bearing object in each scene to BepInEx/SilksongModding-dumps/. "
            + "Each scene is dumped once per session. Turn this off for ordinary play.");

        fsmItemSwap = Config.Bind(
            "Discovery",
            "FsmItemSwap",
            string.Empty,
            "Experiment: substitute one PlayMaker-granted item for another, written as 'From=To' "
            + "(for example 'Mossberry=Rosary_Set_Frayed'). Empty disables it. This exists to prove the "
            + "FSM placement mechanism works; it is not a placement table.");

        // Subscribed rather than patched: Unity already publishes exactly this event, so guessing at a
        // game-side scene-entry hook would add a breakage point for nothing.
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;

        Logger.LogInfo($"Plugin {Name} ({Id}) has loaded.");
    }

    /// <summary>
    /// Applies each patch class on its own, so one that fails to resolve cannot take the others down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Harmony.PatchAll</c> is all-or-nothing: it walks the assembly and the first patch class whose
    /// target method or injected field no longer exists throws straight out of <c>Awake</c>, leaving the
    /// plugin with <i>zero</i> patches applied — the title menu included. Nothing in the log points at the
    /// culprit beyond a stack trace, and the symptom (the RANDOMIZER entry silently missing) looks nothing
    /// like the cause.
    /// </para>
    /// <para>
    /// That is the wrong failure mode here. Check discovery works by carrying speculative probes into game
    /// internals, and those internals are only pinned as tightly as the <c>Silksong.GameLibs</c> version —
    /// a game update can invalidate one probe's field name at any time. Patching class by class turns
    /// "the mod is dead" into "one probe is missing, and the log names it".
    /// </para>
    /// </remarks>
    private void ApplyPatchesIndependently(Harmony target)
    {
        int applied = 0;
        int failed = 0;

        foreach (System.Type candidate in AccessTools.GetTypesFromAssembly(
                     System.Reflection.Assembly.GetExecutingAssembly()))
        {
            try
            {
                // Returns null for any type that is not a Harmony patch class, so this needs no filter.
                if (target.CreateClassProcessor(candidate).Patch() is { Count: > 0 })
                {
                    applied++;
                }
            }
            catch (System.Exception error)
            {
                failed++;
                Logger.LogError($"Patch class {candidate.Name} was skipped: {error.Message}");
            }
        }

        Logger.LogInfo($"Applied {applied} patch class(es){(failed > 0 ? $", skipped {failed} after errors" : string.Empty)}.");
    }

    private IEnumerator Start()
    {
        while (!RandomizerTitleMenu.TryInstall())
        {
            yield return null;
        }
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        StartCoroutine(ScanAndDump(scene));
    }

    private IEnumerator ScanAndDump(UnityEngine.SceneManagement.Scene scene)
    {
        // One frame so every Awake in the scene has run; PersistentBoolItem fills in its ID lazily and
        // PlayMakerFSM has no Fsm to read before then.
        yield return null;
        yield return Randomizer.SceneCheckScan.ScanWhenReady(scene);

        if (dumpScenes is { Value: true })
        {
            Randomizer.SceneDump.DumpOnce(scene);
        }
    }

    private void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        harmony?.UnpatchSelf();
        if (pluginInstance == this)
        {
            pluginInstance = null;
        }
    }

    /// <summary>
    /// Check discovery and placement logging. Deliberately routed through the plugin logger rather than
    /// <c>Debug.Log</c>, so the lines carry the <c>[Info :SilksongModding]</c> tag and stay greppable
    /// in <c>BepInEx/LogOutput.log</c> among the game's own chatter.
    /// </summary>
    internal static void LogCheck(string message)
    {
        log?.LogInfo(message);
    }

    internal static void LogCheckWarning(string message)
    {
        log?.LogWarning(message);
    }

    /// <summary>Menu construction and navigation wiring.</summary>
    internal static void LogMenu(string message)
    {
        log?.LogInfo(message);
    }

    internal static void LogMenuWarning(string message)
    {
        log?.LogWarning(message);
    }

    /// <summary>Resolves the configured substitution for <paramref name="fromName"/>, if any.</summary>
    internal static bool TryGetFsmItemSwap(string fromName, out string toName)
    {
        toName = string.Empty;
        if (fsmItemSwap?.Value is not { Length: > 0 } setting)
        {
            return false;
        }

        int separator = setting.IndexOf('=');
        if (separator <= 0 || separator == setting.Length - 1)
        {
            return false;
        }

        if (!string.Equals(setting.Substring(0, separator).Trim(), fromName, System.StringComparison.Ordinal))
        {
            return false;
        }

        toName = setting.Substring(separator + 1).Trim();
        return toName.Length > 0;
    }

    internal static void LogTitleMenuInstalled()
    {
        log?.LogInfo("Added the opt-in RANDOMIZER entry to the title menu.");
    }

    internal static void LogRandomizerSelected()
    {
        log?.LogInfo("Randomizer title-menu entry selected.");
    }

    internal static Coroutine StartManagedCoroutine(IEnumerator routine)
    {
        if (pluginInstance == null)
        {
            throw new System.InvalidOperationException("The mod plugin is not available to start a coroutine.");
        }

        return pluginInstance.StartCoroutine(routine);
    }

    /// <summary>
    /// The layout attached to the save currently open, or empty when the save is not randomized.
    /// </summary>
    /// <remarks>
    /// DataManager populates <see cref="OnceSaveData"/> only for saves the mod wrote, so a vanilla save
    /// yields nothing here and every hook that consults it stays inert.
    /// </remarks>
    internal static System.Collections.Generic.IReadOnlyDictionary<string, string> CurrentPlacements =>
        pluginInstance?.OnceSaveData?.Placements
        ?? (System.Collections.Generic.IReadOnlyDictionary<string, string>)
           new System.Collections.Generic.Dictionary<string, string>();

    internal static bool TryAttachRandomizerData(int slot, string seed)
    {
        if (pluginInstance == null)
        {
            return false;
        }

        // The layout is generated here, not on load. DataManager writes OnceSaveData once, straight after
        // StartNewGame, so this is the last moment at which anything can be attached to the new save.
        System.Collections.Generic.Dictionary<string, string> placements =
            Randomizer.RandomizerFill.Generate(seed, Randomizer.CheckDatabase.Checks);

        pluginInstance.OnceSaveData = new RandomizerSaveData { Seed = seed, Placements = placements };
        pluginInstance.Logger.LogInfo(
            $"Prepared randomizer seed {seed} for save slot {slot} with {placements.Count} placement(s).");
        return true;
    }

}

[HarmonyPatch(typeof(global::GameManager), nameof(global::GameManager.StartNewGame))]
internal static class RandomizerNewGamePatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        // A randomized new game has its OnceSaveData prepared before StartNewGame; a regular New Game
        // has none, so this stays inert for it.
        RandomizerPlacements.LoadForCurrentSave();
    }
}

[HarmonyPatch(typeof(global::GameManager), "SetLoadedGameData", new[] { typeof(global::SaveGameData), typeof(int) })]
[HarmonyAfter("org.silksong-modding.datamanager")]
internal static class RandomizerSaveLoadPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        // The normal profile-selection load path. DataManager has populated OnceSaveData by the time this
        // runs — hence the ordering attribute — so any future load of placements from save data belongs
        // here rather than earlier.
        RandomizerPlacements.LoadForCurrentSave();
    }
}

[HarmonyPatch(typeof(global::UnityEngine.UI.SaveSlotButton), nameof(global::UnityEngine.UI.SaveSlotButton.OnSubmit))]
internal static class RandomizerSaveSlotPatch
{
    [HarmonyPrefix]
    private static bool Prefix(global::UnityEngine.UI.SaveSlotButton __instance)
    {
        // Pointer clicks call OnSubmit directly while UIManager is still animating
        // cards. Do not accept that premature submit.
        if (!RandomizerSlotPresentation.IsReadyForInteraction(__instance))
        {
            return false;
        }

        return RandomizerSaveFlow.TryHandleSlotSelection(__instance);
    }
}

/// <summary>
/// Redirects Back on the save-slot screen to the seed page while a randomized save is being created.
/// </summary>
/// <remarks>
/// Patches the <c>UIGoToMainMenu</c> wrapper rather than the <c>GoToMainMenu</c> coroutine it starts.
/// Suppressing an iterator method from a prefix would leave the caller passing a null enumerator to
/// <c>StartCoroutine</c>; the wrapper is a plain void, so declining it is clean. The seed page's own exit
/// also runs through here, which is why the handler checks the menu state rather than merely whether a
/// seed is pending.
/// </remarks>
[HarmonyPatch(typeof(global::UIManager), nameof(global::UIManager.UIGoToMainMenu))]
internal static class RandomizerProfileBackPatch
{
    [HarmonyPrefix]
    private static bool Prefix(global::UIManager __instance)
    {
        try
        {
            return !RandomizerSaveFlow.TryHandleProfileBack(__instance);
        }
        catch (System.Exception error)
        {
            // Falling through to the native main-menu navigation is the safe failure. Swallowing the
            // exception and returning false would leave Back doing nothing at all, stranding the player
            // on the slot screen.
            SilksongModdingPlugin.LogMenuWarning($"Save-slot back navigation failed: {error}");
            return true;
        }
    }
}

[HarmonyPatch(typeof(global::UIManager), nameof(global::UIManager.GoToMainMenu))]
internal static class RandomizerProfileExitPatch
{
    [HarmonyPrefix]
    private static void Prefix(global::UIManager __instance)
    {
        if (__instance.menuState == global::GlobalEnums.MainMenuState.SAVE_PROFILES)
        {
            RandomizerSaveFlow.CancelPendingSeed();
        }

        // Unity keeps plugin statics alive across quit-to-title, so placements must be dropped when the
        // save is left or they leak into whichever save is opened next — including a vanilla one.
        RandomizerPlacements.Clear();
    }
}

[HarmonyPatch(typeof(global::UIManager), nameof(global::UIManager.GoToProfileMenu))]
internal static class RandomizerSlotPresentationPatch
{
    [HarmonyPostfix]
    private static void Postfix(global::UIManager __instance)
    {
        RandomizerSlotPresentation.InstallAll(__instance);
    }
}

[HarmonyPatch(typeof(global::UIManager), nameof(global::UIManager.ConfigureMenu))]
internal static class TitleMenuSetupPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        RandomizerTitleMenu.TryInstall();
    }
}

[HarmonyPatch(typeof(global::MainMenuOptions), nameof(global::MainMenuOptions.ConfigureNavigation))]
internal static class TitleMenuNavigationPatch
{
    [HarmonyPostfix]
    private static void Postfix(global::MainMenuOptions __instance)
    {
        RandomizerTitleMenu.RewireNavigation(__instance);
    }
}

/// <summary>
/// Keeps the RANDOMIZER entry inside the game's own navigation list.
/// </summary>
/// <remarks>
/// <c>SetupActive</c> is where controller navigation for a screen is actually decided: it rebuilds every
/// owned entry's up/down links into a wrap-around ring, and <c>UIManager.ShowMenu</c> calls it on each
/// menu show. Registering from a prefix means our entry is present <i>before</i> the ring is computed, on
/// every rebuild, including after a quit-to-title has replaced the list instance outright.
/// <para>
/// This fires for every <c>MenuButtonList</c> in the game, not just the title screen's. That is safe
/// because registration is keyed on the list actually owning the START button, so every other list
/// declines the insert.
/// </para>
/// </remarks>
[HarmonyPatch(typeof(global::MenuButtonList), nameof(global::MenuButtonList.SetupActive))]
internal static class TitleMenuNavigationListPatch
{
    [HarmonyPrefix]
    private static void Prefix(global::MenuButtonList __instance)
    {
        // This runs ahead of every menu rebuild in the game, so an exception escaping here would break
        // menus wholesale. Losing our entry from one rebuild is by far the better failure.
        try
        {
            RandomizerTitleMenu.EnsureRegisteredIn(__instance);
        }
        catch (System.Exception error)
        {
            SilksongModdingPlugin.LogMenuWarning($"Title menu navigation registration failed: {error}");
        }
    }
}

internal static class RandomizerTitleMenu
{
    private const string ButtonName = "SilksongModding_RandomizerButton";

    private static MenuButton? randomizerButton;

    /// <summary>
    /// True once the entry is registered with the title screen's <c>MenuButtonList</c>, which then owns
    /// its navigation. Only when that fails does this class wire navigation itself.
    /// </summary>
    private static bool joinedGameNavigationList;

    internal static bool TryInstall()
    {
        if (randomizerButton)
        {
            return true;
        }

        global::UIManager? ui = global::UIManager.instance;
        global::MainMenuOptions? menu = ui?.mainMenuButtons;
        if (menu == null || !menu.startButton || !menu.optionsButton)
        {
            return false;
        }

        Transform parent = menu.startButton.transform.parent;
        GameObject clone = Object.Instantiate(menu.startButton.gameObject, parent);
        clone.name = ButtonName;
        clone.transform.SetSiblingIndex(menu.startButton.transform.GetSiblingIndex() + 1);

        randomizerButton = clone.GetComponent<MenuButton>();
        if (!randomizerButton)
        {
            Object.Destroy(clone);
            return false;
        }

        // The original button may route through a persistent Unity event. Replace it so that
        // this explicitly opt-in entry can never begin a normal game by accident.
        randomizerButton.OnSubmitPressed = new UnityEvent();
        randomizerButton.OnSubmitPressed.AddListener(OnRandomizerSelected);

        RemoveInheritedSubmitHandlers(clone);

        foreach (AutoLocalizeTextUI localizer in clone.GetComponentsInChildren<AutoLocalizeTextUI>(includeInactive: true))
        {
            Object.Destroy(localizer);
        }

        Text? label = clone.GetComponentInChildren<Text>(includeInactive: true);
        if (label)
        {
            label.text = "RANDOMIZER";
        }

        InsertIntoVisualLayout(menu, randomizerButton);
        JoinGameNavigationList(menu, reportOutcome: true);
        SilksongModdingPlugin.LogTitleMenuInstalled();
        return true;
    }

    /// <summary>
    /// Re-registers the entry with <paramref name="list"/> if that list is the title screen's.
    /// </summary>
    /// <remarks>
    /// Called from a <c>MenuButtonList.SetupActive</c> prefix, so it runs for every list in the game and
    /// has to identify the right one itself. <see cref="MenuNavigation.TryInsertAfter"/> does that by
    /// refusing any list that does not already contain the START button.
    /// </remarks>
    internal static void EnsureRegisteredIn(global::MenuButtonList list)
    {
        if (!randomizerButton)
        {
            return;
        }

        global::MainMenuOptions? menu = global::UIManager.instance?.mainMenuButtons;
        if (menu == null || !menu.startButton)
        {
            return;
        }

        if (!MenuNavigation.TryInsertAfter(list, menu.startButton, randomizerButton))
        {
            return;
        }

        if (!joinedGameNavigationList)
        {
            // Registration can land here rather than at install time, because this runs whenever the
            // list rebuilds. Saying so distinguishes "joined late" from "never joined" in the log.
            joinedGameNavigationList = true;
            SilksongModdingPlugin.LogMenu(
                "Title menu: RANDOMIZER joined the game's MenuButtonList during a navigation rebuild.");
        }
    }

    /// <summary>
    /// Puts the entry into the navigation list that owns the title screen, falling back to hand-wiring.
    /// </summary>
    /// <remarks>
    /// The fallback exists because which screens carry a <c>MenuButtonList</c> is serialized scene data,
    /// not something that can be settled by reading the assembly. If the title screen ever has none, the
    /// explicit wiring below is still better than no controller navigation at all — and the log line says
    /// which path was taken, so a single run answers the question.
    /// </remarks>
    private static void JoinGameNavigationList(global::MainMenuOptions menu, bool reportOutcome)
    {
        // Wire by hand first, unconditionally. If a list is found below, its rebuild overwrites these
        // same fields with the correct ring a moment later, so this costs nothing; if one is not found,
        // it is the only thing keeping the entry reachable. Doing it in this order means the list lookup
        // can never leave the menu worse off than not attempting it.
        RewireNavigationManually(menu);

        global::MenuButtonList? list = MenuNavigation.FindOwningList(menu.startButton);
        joinedGameNavigationList = MenuNavigation.TryInsertAfter(list, menu.startButton, randomizerButton);

        if (joinedGameNavigationList)
        {
            // Rebuild immediately rather than waiting for the list to do it. The title screen is a
            // CanvasGroup faded in by hand, not a MenuScreen shown through UIManager.ShowMenu, so it never
            // gets ShowMenu's per-show SetupActive call. The list's own Start has almost certainly already
            // run by now, which would leave the new entry sitting in `entries` and absent from the ring
            // the controller actually walks.
            list!.SetupActive();

            if (reportOutcome)
            {
                SilksongModdingPlugin.LogMenu(
                    "Title menu: RANDOMIZER joined the game's MenuButtonList; controller navigation is native.");
            }

            return;
        }

        if (reportOutcome)
        {
            SilksongModdingPlugin.LogMenuWarning(
                "Title menu: no MenuButtonList owns the START button, so RANDOMIZER navigation is hand-wired. " +
                $"START ancestry: {MenuNavigation.DescribeAncestry(menu.startButton)}");
        }
    }

    /// <summary>
    /// Navigation written straight onto the buttons. Only correct when no <c>MenuButtonList</c> is present.
    /// </summary>
    /// <remarks>
    /// When one <i>is</i> present it rebuilds these same fields on every menu show, so anything written
    /// here is discarded moments later — which is exactly why the controller used to skip this entry while
    /// a mouse click still worked.
    /// </remarks>
    internal static void RewireNavigation(global::MainMenuOptions menu)
    {
        if (joinedGameNavigationList)
        {
            return;
        }

        RewireNavigationManually(menu);
    }

    private static void RewireNavigationManually(global::MainMenuOptions menu)
    {
        if (!randomizerButton || !menu.startButton || !menu.optionsButton)
        {
            return;
        }

        Navigation startNavigation = menu.startButton.navigation;
        startNavigation.mode = Navigation.Mode.Explicit;
        startNavigation.selectOnDown = randomizerButton;
        menu.startButton.navigation = startNavigation;

        Navigation randomizerNavigation = randomizerButton.navigation;
        randomizerNavigation.mode = Navigation.Mode.Explicit;
        randomizerNavigation.selectOnUp = menu.startButton;
        randomizerNavigation.selectOnDown = menu.optionsButton;
        randomizerButton.navigation = randomizerNavigation;

        Navigation optionsNavigation = menu.optionsButton.navigation;
        optionsNavigation.mode = Navigation.Mode.Explicit;
        optionsNavigation.selectOnUp = randomizerButton;
        menu.optionsButton.navigation = optionsNavigation;
    }

    private static void InsertIntoVisualLayout(global::MainMenuOptions menu, MenuButton button)
    {
        Transform parent = menu.startButton.transform.parent;
        if (parent.GetComponent<LayoutGroup>())
        {
            return;
        }

        RectTransform startTransform = menu.startButton.GetComponent<RectTransform>();
        RectTransform optionsTransform = menu.optionsButton.GetComponent<RectTransform>();
        RectTransform randomizerTransform = button.GetComponent<RectTransform>();
        if (!startTransform || !optionsTransform || !randomizerTransform)
        {
            return;
        }

        // A full step, not half of one. Everything below shifts down by a full step regardless, so a
        // half-step here only made the gaps uneven (half above the entry, one and a half below) without
        // saving any vertical room.
        Vector2 spacing = optionsTransform.anchoredPosition - startTransform.anchoredPosition;
        randomizerTransform.anchoredPosition = startTransform.anchoredPosition + spacing;

        ShiftDownIfPresent(menu.optionsButton, spacing);
        ShiftDownIfPresent(menu.achievementsButton, spacing);
        ShiftDownIfPresent(menu.extrasButton, spacing);
        ShiftDownIfPresent(menu.quitButton, spacing);
    }

    private static void ShiftDownIfPresent(MenuButton? button, Vector2 spacing)
    {
        if (!button)
        {
            return;
        }

        RectTransform transform = button.GetComponent<RectTransform>();
        if (transform)
        {
            transform.anchoredPosition += spacing;
        }
    }

    private static void RemoveInheritedSubmitHandlers(GameObject clone)
    {
        foreach (MonoBehaviour component in clone.GetComponents<MonoBehaviour>())
        {
            if (component is IEventSystemHandler && component is not MenuButton)
            {
                component.enabled = false;
                Object.Destroy(component);
            }
        }
    }

    private static void OnRandomizerSelected()
    {
        SilksongModdingPlugin.LogRandomizerSelected();
        RandomizerSetupMenu.Show();
    }
}

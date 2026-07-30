using System.Collections;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Silksong.DataManager;
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
        harmony.PatchAll();

        Logger.LogInfo($"Plugin {Name} ({Id}) has loaded. New Game diagnostics are enabled.");
    }

    private IEnumerator Start()
    {
        while (!RandomizerTitleMenu.TryInstall())
        {
            yield return null;
        }
    }

    private IEnumerator ApplyTestHornetTintWhenReady()
    {
        while (!global::HeroController.SilentInstance)
        {
            yield return null;
        }

        // Let the player hierarchy finish its scene-entry setup before changing its mesh tint.
        yield return null;

        global::HeroController hero = global::HeroController.SilentInstance;
        if (!hero.AnimCtrl || !hero.AnimCtrl.animator || !hero.AnimCtrl.animator.Sprite)
        {
            Logger.LogWarning("Hornet tint test skipped because the animated player sprite was not ready.");
            yield break;
        }

        RandomizerSaveData? saveData = OnceSaveData;
        if (saveData?.Seed is not { Length: > 0 } seed)
        {
            Logger.LogInfo("No randomizer data is attached to this save; leaving Hornet's test tint unchanged.");
            yield break;
        }

        Color originalColor = hero.AnimCtrl.animator.Sprite.color;
        Color tint = Color.HSVToRGB(SeedToHue(seed), 0.65f, 1f);
        tint.a = originalColor.a;
        hero.AnimCtrl.animator.Sprite.color = tint;
        Logger.LogInfo($"Applied seeded temporary full-Hornet tint for {seed}: {tint}.");
    }

    private void OnDestroy()
    {
        harmony?.UnpatchSelf();
        if (pluginInstance == this)
        {
            pluginInstance = null;
        }
    }

    internal static void LogNewGameRequest(int slotId, bool permadeathMode, bool bossRushMode)
    {
        log?.LogInfo(
            $"New Game requested: slot={slotId}, permadeath={permadeathMode}, bossRush={bossRushMode}."
        );
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

    internal static bool TryAttachRandomizerData(int slot, string seed)
    {
        if (pluginInstance == null)
        {
            return false;
        }

        pluginInstance.OnceSaveData = new RandomizerSaveData { Seed = seed };
        pluginInstance.Logger.LogInfo($"Prepared randomizer seed {seed} for save slot {slot}.");
        return true;
    }

    internal static void QueueTestTintForCurrentSave()
    {
        if (pluginInstance != null)
        {
            pluginInstance.StartCoroutine(pluginInstance.ApplyTestHornetTintWhenReady());
        }
    }

    private static float SeedToHue(string seed)
    {
        // A stable small hash: identical seed text always produces identical tint,
        // independent of Unity's global random state.
        uint hash = 2166136261;
        foreach (char character in seed)
        {
            hash ^= character;
            hash *= 16777619;
        }

        return (hash & 0x00ffffff) / 16777216f;
    }
}

[HarmonyPatch(typeof(global::GameManager), nameof(global::GameManager.StartNewGame))]
internal static class NewGameDiagnosticsPatch
{
    [HarmonyPrefix]
    private static void Prefix(global::GameManager __instance, bool permadeathMode, bool bossRushMode)
    {
        SilksongModdingPlugin.LogNewGameRequest(
            __instance.profileID,
            permadeathMode,
            bossRushMode
        );
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        // A randomized new game has its OnceSaveData prepared before StartNewGame.
        // A regular New Game has no such data, so this remains a no-op for it.
        SilksongModdingPlugin.QueueTestTintForCurrentSave();
    }
}

[HarmonyPatch(typeof(global::GameManager), "SetLoadedGameData", new[] { typeof(global::SaveGameData), typeof(int) })]
[HarmonyAfter("org.silksong-modding.datamanager")]
internal static class RandomizerSaveLoadPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        // This is the normal profile-selection load path. DataManager has loaded
        // the save's OnceSaveData before this postfix, if it is a randomized save.
        SilksongModdingPlugin.QueueTestTintForCurrentSave();
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

internal static class RandomizerTitleMenu
{
    private const string ButtonName = "SilksongModding_RandomizerButton";

    private static MenuButton? randomizerButton;

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
        RewireNavigation(menu);
        SilksongModdingPlugin.LogTitleMenuInstalled();
        return true;
    }

    internal static void RewireNavigation(global::MainMenuOptions menu)
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

        Vector2 spacing = optionsTransform.anchoredPosition - startTransform.anchoredPosition;
        randomizerTransform.anchoredPosition = startTransform.anchoredPosition + spacing / 2f;

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

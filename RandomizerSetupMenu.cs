using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SilksongModding;

internal sealed class RandomizerSetupMenu
{
    private static RandomizerSetupMenu? instance;

    private readonly global::UIManager ui;
    private readonly GameObject content;
    private readonly List<GameObject> hiddenNativeObjects = new();
    private readonly Font menuFont;
    private readonly Material? menuMaterial;
    private readonly Color menuTextColor;
    private readonly InputField seedInput;
    private readonly Text statusText;
    private readonly MenuButton generateButton;
    private readonly MenuButton startButton;
    private readonly MenuButton cancelButton;

    private RandomizerSetupMenu(global::UIManager ui)
    {
        this.ui = ui;

        Text? textTemplate = ui.mainMenuButtons.startButton.GetComponentInChildren<Text>(includeInactive: true);
        menuFont = textTemplate?.font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        menuMaterial = textTemplate?.material;
        menuTextColor = textTemplate?.color ?? Color.white;

        HideNativeOptionsContent();
        content = new GameObject("SilksongModding_RandomizerSetup", typeof(RectTransform));
        content.transform.SetParent(ui.optionsMenuScreen.transform, worldPositionStays: false);
        Stretch(content.GetComponent<RectTransform>());

        CreateText(content.transform, "Title", "RANDOMIZER", 40, TextAnchor.MiddleCenter, new Vector2(0f, 250f), new Vector2(800f, 56f));
        CreateText(
            content.transform,
            "Description",
            "CHOOSE A SEED. THE SAME SEED WILL PRODUCE THE SAME RANDOMIZER RESULT.",
            20,
            TextAnchor.MiddleCenter,
            new Vector2(0f, 180f),
            new Vector2(900f, 48f)
        );
        CreateText(content.transform, "SeedLabel", "SEED", 23, TextAnchor.MiddleCenter, new Vector2(0f, 110f), new Vector2(560f, 28f));

        seedInput = CreateSeedInput(content.transform);
        statusText = CreateText(
            content.transform,
            "Status",
            string.Empty,
            18,
            TextAnchor.MiddleCenter,
            new Vector2(0f, 0f),
            new Vector2(820f, 36f)
        );

        generateButton = CreateNativeButton(content.transform, "Generate", "GENERATE", new Vector2(0f, -90f), GenerateSeed);
        startButton = CreateNativeButton(content.transform, "Start", "START RANDOMIZED GAME", new Vector2(0f, -170f), ConfirmSeed);
        cancelButton = CreateNativeButton(content.transform, "Cancel", "CANCEL", new Vector2(0f, -250f), Hide);

        // GENERATE acts in place; the other two leave the page. That distinction is exactly what
        // MenuButtonType encodes: MenuButton.OnSubmit calls ForceDeselect() for every type except
        // Activate, which clears the selection outright. For a button that hands off to another screen
        // that is correct, because the next screen selects something itself — but a button that stays put
        // would leave nothing selected, and ForceDeselect sets deselectWasForced, which suppresses the
        // game's own restore. The controller would then have no cursor and no way to get one back.
        // The clones inherit Proceed from the title screen's START button, so this has to be set.
        generateButton.buttonType = MenuButton.MenuButtonType.Activate;

        // Nothing on this page belongs to a MenuButtonList, so the ring is wired by hand. Without it the
        // controller cannot reach any of these: the buttons are clones and still carry the title screen's
        // up/down targets, which point at buttons on a screen that is not even showing.
        //
        // The seed field is deliberately not in the ring. A focused InputField consumes the D-pad for
        // caret movement, so including it makes the cursor enter and never leave. It is a keyboard/mouse
        // control: click it to type, and use GENERATE on a controller.
        MenuNavigation.WireVerticalRing(new Selectable[] { generateButton, startButton, cancelButton });
        MenuNavigation.ExcludeFromRing(seedInput);

        // Leaving the field must hand the cursor back to the ring. Without this a mouse user who clicks
        // into the seed box strands the selection on a control nothing can navigate away from.
        seedInput.onEndEdit.AddListener(_ => ReturnSelectionToRing());

        // Back must run this page's own teardown. The clones inherit cancelAction from the title screen's
        // START button, so without this Back would raise the quit-game prompt while the native Options
        // content is still hidden — leaving a broken Options screen behind.
        MenuNavigation.SetCancelHandler(generateButton, Hide);
        MenuNavigation.SetCancelHandler(startButton, Hide);
        MenuNavigation.SetCancelHandler(cancelButton, Hide);
        MenuNavigation.SetCancelHandler(seedInput, CancelFromSeedInput);
    }

    /// <summary>
    /// Back while editing the seed backs out of the field first, and only then off the page.
    /// </summary>
    /// <remarks>
    /// A single-stage Back would close the whole page mid-edit, which is the one genuinely destructive
    /// thing this screen can do to a controller user's input.
    /// </remarks>
    private void CancelFromSeedInput()
    {
        if (seedInput.isFocused)
        {
            seedInput.DeactivateInputField();
            ReturnSelectionToRing();
            return;
        }

        Hide();
    }

    /// <summary>
    /// Puts the cursor back on a navigable control when it would otherwise be left on the seed field or
    /// on nothing at all.
    /// </summary>
    /// <remarks>
    /// Only acts when the selection is actually stranded, so a click that moves focus straight to another
    /// button is left alone rather than being yanked back.
    /// </remarks>
    private void ReturnSelectionToRing()
    {
        if (MenuNavigation.HasSelection() && !MenuNavigation.IsSelected(seedInput))
        {
            return;
        }

        MenuNavigation.Select(generateButton);
    }

    /// <summary>
    /// Opens the seed page, optionally restoring a seed the player already chose.
    /// </summary>
    /// <param name="initialSeed">
    /// Carried back in when returning from save-slot selection, so backing out of that screen does not
    /// silently discard the seed the player picked. A fresh one is generated when this is null.
    /// </param>
    internal static void Show(string? initialSeed = null)
    {
        if (instance != null)
        {
            return;
        }

        SilksongModdingPlugin.StartManagedCoroutine(ShowAfterNativeTransition(initialSeed));
    }

    private static IEnumerator ShowAfterNativeTransition(string? initialSeed)
    {
        global::UIManager? ui = global::UIManager.instance;
        if (ui == null || ui.optionsMenuScreen == null || ui.mainMenuButtons == null)
        {
            yield break;
        }

        // Build the replacement while the Options screen is still hidden.  Its native
        // controls are therefore disabled before UIManager fades that screen in, so
        // there is no one-frame flash of the real Options page.
        RandomizerSetupMenu page = new(ui);
        instance = page;
        page.seedInput.text = string.IsNullOrEmpty(initialSeed) ? CreateSeed() : initialSeed!;

        ui.UIGoToOptionsMenu();
        while (ui.menuState != global::GlobalEnums.MainMenuState.OPTIONS_MENU)
        {
            yield return null;
        }

        // Select the primary action rather than the text field. A seed is already generated by the time
        // this page appears, so START is the useful default for both input methods — and a controller
        // cannot type into an activated InputField anyway, while an active field swallows the D-pad for
        // caret movement, which would trap the cursor on the one control it landed on.
        //
        // Asserted over several frames because UIManager.ShowMenu highlights the screen's own default
        // after the fade, which can land later than this coroutine resumes.
        for (int frame = 0; frame < 5; frame++)
        {
            // Held as a local: a cancel landing inside these frames clears `instance` and destroys the
            // page, and the loop must not keep reasserting selection onto a dead button.
            if (instance != page)
            {
                yield break;
            }

            if (!MenuNavigation.IsSelected(page.startButton))
            {
                MenuNavigation.Select(page.startButton);
            }

            yield return null;
        }
    }

    private void GenerateSeed()
    {
        seedInput.text = CreateSeed();
        statusText.text = "GENERATED A NEW SEED.";

        // Deliberately does not activate the input field. Doing so would move a controller user's cursor
        // into a control they cannot type in, off the button they just pressed.
    }

    private void ConfirmSeed()
    {
        string seed = seedInput.text.Trim();
        if (seed.Length == 0)
        {
            statusText.text = "ENTER A SEED OR GENERATE ONE FIRST.";

            // START is a Proceed button, so MenuButton.OnSubmit already cleared the selection before
            // handing control here. On this path the page does not go anywhere, so the cursor has to be
            // put back or the screen stops answering the controller entirely.
            MenuNavigation.Select(startButton);
            return;
        }

        statusText.text = "SELECT AN EMPTY SAVE SLOT.";
        RandomizerSaveFlow.Begin(seed);
        SilksongModdingPlugin.StartManagedCoroutine(GoToSaveSelection());
        Debug.Log($"[SilksongModding] Randomizer seed accepted: {seed}");
    }

    private IEnumerator GoToSaveSelection()
    {
        // Native profile selection is retained for the actual save-slot operation.
        // Keep this screen's native Options controls hidden until it has faded away.
        ui.UIGoToProfileMenu();
        while (ui.optionsMenuScreen.gameObject.activeInHierarchy)
        {
            yield return null;
        }

        RestoreNativeOptionsContent();
        UnityEngine.Object.Destroy(content);
        instance = null;
    }

    private void Hide()
    {
        // Leaving this page abandons the creation flow, so the seed must stop being pending. Without
        // this, a seed chosen here could survive back to the title menu and then attach itself to the
        // next slot picked through an ordinary New Game.
        RandomizerSaveFlow.CancelPendingSeed();
        SilksongModdingPlugin.StartManagedCoroutine(HideAfterNativeTransition());
    }

    private IEnumerator HideAfterNativeTransition()
    {
        // Keep the native Options content disabled until this screen is completely
        // hidden. Restoring it any earlier makes it flash during the fade-out.
        ui.UILeaveOptionsMenu();
        while (ui.optionsMenuScreen.gameObject.activeInHierarchy)
        {
            yield return null;
        }

        RestoreNativeOptionsContent();

        UnityEngine.Object.Destroy(content);
        instance = null;
    }

    private void RestoreNativeOptionsContent()
    {
        foreach (GameObject nativeObject in hiddenNativeObjects)
        {
            if (nativeObject)
            {
                nativeObject.SetActive(true);
            }
        }
    }

    private void HideNativeOptionsContent()
    {
        HashSet<GameObject> toHide = new();
        foreach (Selectable selectable in ui.optionsMenuScreen.GetComponentsInChildren<Selectable>(includeInactive: true))
        {
            toHide.Add(selectable.gameObject);
        }
        foreach (Text text in ui.optionsMenuScreen.GetComponentsInChildren<Text>(includeInactive: true))
        {
            toHide.Add(text.gameObject);
        }

        foreach (GameObject nativeObject in toHide)
        {
            if (nativeObject.activeSelf)
            {
                hiddenNativeObjects.Add(nativeObject);
                nativeObject.SetActive(false);
            }
        }
    }

    private MenuButton CreateNativeButton(Transform parent, string name, string label, Vector2 anchoredPosition, UnityAction action)
    {
        GameObject clone = UnityEngine.Object.Instantiate(ui.mainMenuButtons.startButton.gameObject, parent);
        clone.name = $"SilksongModding_{name}Button";

        MenuButton button = clone.GetComponent<MenuButton>();
        if (!button)
        {
            UnityEngine.Object.Destroy(clone);
            throw new InvalidOperationException("The title-menu button template did not contain a MenuButton.");
        }

        button.OnSubmitPressed = new UnityEvent();
        button.OnSubmitPressed.AddListener(action);
        RemoveInheritedSubmitHandlers(clone);

        foreach (AutoLocalizeTextUI localizer in clone.GetComponentsInChildren<AutoLocalizeTextUI>(includeInactive: true))
        {
            UnityEngine.Object.Destroy(localizer);
        }

        Text? buttonLabel = clone.GetComponentInChildren<Text>(includeInactive: true);
        if (buttonLabel)
        {
            buttonLabel.text = label;
            buttonLabel.alignment = TextAnchor.MiddleCenter;
        }

        RectTransform transform = clone.GetComponent<RectTransform>();
        transform.anchorMin = new Vector2(0.5f, 0.5f);
        transform.anchorMax = new Vector2(0.5f, 0.5f);
        transform.pivot = new Vector2(0.5f, 0.5f);
        transform.anchoredPosition = anchoredPosition;
        return button;
    }

    private static void RemoveInheritedSubmitHandlers(GameObject clone)
    {
        foreach (MonoBehaviour component in clone.GetComponents<MonoBehaviour>())
        {
            if (component is IEventSystemHandler && component is not MenuButton)
            {
                component.enabled = false;
                UnityEngine.Object.Destroy(component);
            }
        }
    }

    private GameObject CreatePanel(Transform parent, string name, Color color)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, worldPositionStays: false);
        panel.GetComponent<Image>().color = color;
        return panel;
    }

    private Text CreateText(
        Transform parent,
        string name,
        string value,
        int fontSize,
        TextAnchor alignment,
        Vector2 anchoredPosition,
        Vector2 size
    )
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, worldPositionStays: false);

        Text text = textObject.GetComponent<Text>();
        text.font = menuFont;
        text.material = menuMaterial;
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = menuTextColor;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        RectTransform transform = textObject.GetComponent<RectTransform>();
        transform.anchorMin = new Vector2(0.5f, 0.5f);
        transform.anchorMax = new Vector2(0.5f, 0.5f);
        transform.pivot = new Vector2(0.5f, 0.5f);
        transform.anchoredPosition = anchoredPosition;
        transform.sizeDelta = size;
        return text;
    }

    private InputField CreateSeedInput(Transform parent)
    {
        GameObject inputObject = CreatePanel(parent, "SeedInput", new Color(0.01f, 0.01f, 0.02f, 0.88f));
        RectTransform inputTransform = inputObject.GetComponent<RectTransform>();
        inputTransform.anchorMin = new Vector2(0.5f, 0.5f);
        inputTransform.anchorMax = new Vector2(0.5f, 0.5f);
        inputTransform.pivot = new Vector2(0.5f, 0.5f);
        inputTransform.anchoredPosition = new Vector2(0f, 58f);
        inputTransform.sizeDelta = new Vector2(560f, 56f);

        InputField input = inputObject.AddComponent<InputField>();
        Text text = CreateText(inputObject.transform, "Text", string.Empty, 26, TextAnchor.MiddleLeft, Vector2.zero, Vector2.zero);
        Stretch(text.GetComponent<RectTransform>(), 14f, 10f);
        Text placeholder = CreateText(inputObject.transform, "Placeholder", "ENTER A CUSTOM SEED", 23, TextAnchor.MiddleLeft, Vector2.zero, Vector2.zero);
        placeholder.color = new Color(menuTextColor.r, menuTextColor.g, menuTextColor.b, 0.45f);
        Stretch(placeholder.GetComponent<RectTransform>(), 14f, 10f);

        input.textComponent = text;
        input.placeholder = placeholder;
        input.targetGraphic = inputObject.GetComponent<Image>();
        input.characterLimit = 64;
        return input;
    }

    private static string CreateSeed()
    {
        return Guid.NewGuid().ToString("N").Substring(0, 12).ToUpperInvariant();
    }

    private static void Stretch(RectTransform transform, float horizontalInset = 0f, float verticalInset = 0f)
    {
        transform.anchorMin = Vector2.zero;
        transform.anchorMax = Vector2.one;
        transform.offsetMin = new Vector2(horizontalInset, verticalInset);
        transform.offsetMax = new Vector2(-horizontalInset, -verticalInset);
    }
}

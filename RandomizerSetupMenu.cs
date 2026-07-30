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

        CreateNativeButton(content.transform, "Generate", "GENERATE", new Vector2(0f, -90f), GenerateSeed);
        CreateNativeButton(content.transform, "Start", "START RANDOMIZED GAME", new Vector2(0f, -170f), ConfirmSeed);
        CreateNativeButton(content.transform, "Cancel", "CANCEL", new Vector2(0f, -250f), Hide);
    }

    internal static void Show()
    {
        if (instance != null)
        {
            return;
        }

        SilksongModdingPlugin.StartManagedCoroutine(ShowAfterNativeTransition());
    }

    private static IEnumerator ShowAfterNativeTransition()
    {
        global::UIManager? ui = global::UIManager.instance;
        if (ui == null || ui.optionsMenuScreen == null || ui.mainMenuButtons == null)
        {
            yield break;
        }

        // Build the replacement while the Options screen is still hidden.  Its native
        // controls are therefore disabled before UIManager fades that screen in, so
        // there is no one-frame flash of the real Options page.
        instance = new RandomizerSetupMenu(ui);
        instance.seedInput.text = CreateSeed();

        ui.UIGoToOptionsMenu();
        while (ui.menuState != global::GlobalEnums.MainMenuState.OPTIONS_MENU)
        {
            yield return null;
        }

        EventSystem.current?.SetSelectedGameObject(instance.seedInput.gameObject);
        instance.seedInput.ActivateInputField();
    }

    private void GenerateSeed()
    {
        seedInput.text = CreateSeed();
        statusText.text = "GENERATED A NEW SEED.";
        seedInput.ActivateInputField();
    }

    private void ConfirmSeed()
    {
        string seed = seedInput.text.Trim();
        if (seed.Length == 0)
        {
            statusText.text = "ENTER A SEED OR GENERATE ONE FIRST.";
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

    private GameObject CreateNativeButton(Transform parent, string name, string label, Vector2 anchoredPosition, UnityAction action)
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
        return clone;
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

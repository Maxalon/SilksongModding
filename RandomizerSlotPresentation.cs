using System.IO;
using Silksong.DataManager;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SilksongModding;

/// <summary>
/// Adds the lightweight randomized-save marker to native profile cards and,
/// exclusively during randomized-save creation, removes their extra actions.
/// </summary>
internal sealed class RandomizerSlotPresentation : MonoBehaviour
{
    private const string SaveDataFileName = "io.github.hfellisch.silksongmodding.json.dat";

    private global::UnityEngine.UI.SaveSlotButton? slot;
    private GameObject? randomizedTag;
    private global::UnityEngine.UI.ClearSaveButton? clearButton;
    private global::UnityEngine.UI.RestoreSaveButton? restoreButton;
    private float nextRefreshTime;
    private bool controlsSuppressed;
    private bool clearButtonWasActive;
    private bool restoreButtonWasActive;
    private bool selectionVisualEnabled;

    internal static void InstallAll(global::UIManager ui)
    {
        Install(ui.slotOne);
        Install(ui.slotTwo);
        Install(ui.slotThree);
        Install(ui.slotFour);
    }

    private static void Install(global::UnityEngine.UI.SaveSlotButton? slot)
    {
        if (!slot)
        {
            return;
        }

        RandomizerSlotPresentation presentation = slot.GetComponent<RandomizerSlotPresentation>();
        if (!presentation)
        {
            presentation = slot.gameObject.AddComponent<RandomizerSlotPresentation>();
        }

        presentation.RefreshNow();
    }

    private void Awake()
    {
        slot = GetComponent<global::UnityEngine.UI.SaveSlotButton>();
        clearButton = GetComponentInChildren<global::UnityEngine.UI.ClearSaveButton>(includeInactive: true);
        restoreButton = GetComponentInChildren<global::UnityEngine.UI.RestoreSaveButton>(includeInactive: true);
        randomizedTag = CreateTag();
    }

    private void OnEnable()
    {
        selectionVisualEnabled = false;
        if (slot && slot.highlight)
        {
            slot.highlight.gameObject.SetActive(false);
        }
    }

    private void LateUpdate()
    {
        SetCreationModeControls(RandomizerSaveFlow.ShouldSuppressSlotControls);
        EnableSelectionVisualWhenCardIsVisible();

        if (Time.unscaledTime >= nextRefreshTime)
        {
            RefreshNow();
        }
    }

    private void EnableSelectionVisualWhenCardIsVisible()
    {
        if (selectionVisualEnabled || !slot || !slot.highlight)
        {
            return;
        }

        bool cardVisible = slot.activeSaveSlot.alpha >= 0.9f || slot.newGameText.alpha >= 0.9f || slot.defeatedText.alpha >= 0.9f;
        if (!cardVisible)
        {
            return;
        }

        selectionVisualEnabled = true;
        slot.highlight.gameObject.SetActive(true);
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == slot.gameObject)
        {
            slot.highlight.ResetTrigger("hide");
            slot.highlight.SetTrigger("show");
        }
    }

    internal void RefreshNow()
    {
        nextRefreshTime = Time.unscaledTime + 0.25f;
        if (!slot || !randomizedTag)
        {
            return;
        }

        string dataPath = Path.Combine(DataPaths.OnceSaveDataDir(slot.SaveSlotIndex), SaveDataFileName);
        randomizedTag.SetActive(File.Exists(dataPath));
    }

    internal static bool IsReadyForInteraction(global::UnityEngine.UI.SaveSlotButton slot)
    {
        RandomizerSlotPresentation presentation = slot.GetComponent<RandomizerSlotPresentation>();
        return !presentation || presentation.selectionVisualEnabled;
    }

    private GameObject CreateTag()
    {
        GameObject strip = new GameObject("SilksongModding_RandomizedTag", typeof(RectTransform));
        Transform parent = slot != null ? slot.activeSaveSlot.transform : transform;
        strip.transform.SetParent(parent, worldPositionStays: false);

        RectTransform stripTransform = strip.GetComponent<RectTransform>();
        stripTransform.anchorMin = new Vector2(0.5f, 1f);
        stripTransform.anchorMax = new Vector2(0.5f, 1f);
        stripTransform.pivot = new Vector2(0.5f, 0f);
        stripTransform.anchoredPosition = new Vector2(0f, 18f);
        stripTransform.sizeDelta = new Vector2(330f, 46f);

        // Layered, increasingly opaque black panels give the label a soft radial
        // falloff without introducing a new sprite or shader into the game's UI.
        for (int layer = 0; layer < 8; layer++)
        {
            float amount = layer / 7f;
            GameObject backdrop = new GameObject($"Falloff{layer}", typeof(RectTransform), typeof(Image));
            backdrop.transform.SetParent(strip.transform, worldPositionStays: false);
            RectTransform backdropTransform = backdrop.GetComponent<RectTransform>();
            backdropTransform.anchorMin = new Vector2(0.5f, 0.5f);
            backdropTransform.anchorMax = new Vector2(0.5f, 0.5f);
            backdropTransform.pivot = new Vector2(0.5f, 0.5f);
            backdropTransform.sizeDelta = new Vector2(350f - layer * 8f, 62f - layer * 4f);
            backdrop.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.025f + amount * 0.075f);
        }

        GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelObject.transform.SetParent(strip.transform, worldPositionStays: false);
        Text label = labelObject.GetComponent<Text>();
        Text? template = slot?.slotNumberText.GetComponentInChildren<Text>(includeInactive: true) ?? slot?.locationText;
        label.font = template?.font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        label.material = template?.material;
        label.color = template?.color ?? Color.white;
        label.fontStyle = FontStyle.Bold;
        label.fontSize = 22;
        label.alignment = TextAnchor.MiddleCenter;
        label.text = "RANDOMIZED";

        RectTransform labelTransform = labelObject.GetComponent<RectTransform>();
        labelTransform.anchorMin = Vector2.zero;
        labelTransform.anchorMax = Vector2.one;
        labelTransform.offsetMin = Vector2.zero;
        labelTransform.offsetMax = Vector2.zero;
        return strip;
    }

    private void SetCreationModeControls(bool isCreationMode)
    {
        if (isCreationMode)
        {
            if (!controlsSuppressed)
            {
                controlsSuppressed = true;
                clearButtonWasActive = clearButton && clearButton.gameObject.activeSelf;
                restoreButtonWasActive = restoreButton && restoreButton.gameObject.activeSelf;
            }

            if (clearButton && clearButton.gameObject.activeSelf)
            {
                clearButton.gameObject.SetActive(false);
            }

            if (restoreButton && restoreButton.gameObject.activeSelf)
            {
                restoreButton.gameObject.SetActive(false);
            }

            return;
        }

        if (!controlsSuppressed)
        {
            return;
        }

        controlsSuppressed = false;
        if (clearButton)
        {
            clearButton.gameObject.SetActive(clearButtonWasActive);
        }

        if (restoreButton)
        {
            restoreButton.gameObject.SetActive(restoreButtonWasActive);
        }
    }
}

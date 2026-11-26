using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Экранный HUD для мышиного управления.
///
/// Всё создаётся кодом в рантайме (не в сцене), поэтому добавление
/// мышиного управления не требует правок сериализованной сцены.
///
/// Элементы:
///  * панель действий справа снизу: ВЗЯТЬ, БРОСИТЬ, ПРЫЖОК, ПРИСЕСТЬ, БЕГ;
///  * экранная клавиатура 0-9 + ЗАБОЙ/ОК для ввода кода от двери;
///  * подсказка о текущем действии и подсказка по управлению мышью.
/// </summary>
[DisallowMultipleComponent]
public class MouseHud : MonoBehaviour
{
    [Header("Ссылки")]
    [SerializeField] private MouseGameplayController gameplay;
    [SerializeField] private CodeMonitor codeMonitor;
    [SerializeField] private Canvas canvas;

    [Header("Цвета")]
    [SerializeField] private Color panelColor = new Color(0f, 0f, 0f, 0.55f);
    [SerializeField] private Color buttonColor = new Color(1f, 1f, 1f, 0.12f);
    [SerializeField] private Color buttonHighlight = new Color(1f, 0.85f, 0.2f, 0.9f);
    [SerializeField] private Color textColor = Color.white;

    [Header("Позиции")]
    [SerializeField] private Vector2 panelSize = new Vector2(520f, 120f);
    [SerializeField] private Vector2 panelOffset = new Vector2(-24f, 24f);
    [SerializeField] private Vector2 keypadSize = new Vector2(360f, 260f);
    [SerializeField] private Vector2 keypadOffset = new Vector2(24f, 24f);

    private readonly List<Button> actionButtons = new List<Button>();
    private readonly List<Image> actionButtonImages = new List<Image>();

    private RectTransform actionPanel;
    private RectTransform keypadPanel;
    private TMP_Text promptText;
    private TMP_Text hintText;
    private CanvasGroup keypadGroup;
    private bool built;

    void Awake()
    {
        if (!canvas) canvas = GetComponentInParent<Canvas>();
        if (!gameplay) gameplay = FindFirstObjectByType<MouseGameplayController>();

        // Монитор кода лежит в сцене, а не в префабе игрока — ищем его сами.
        if (!codeMonitor) codeMonitor = FindFirstObjectByType<CodeMonitor>();
    }

    void Start()
    {
        Build();
    }

    void Update()
    {
        if (!built) Build();

        UpdatePrompt();
        UpdateKeypadVisibility();
    }

    // ================= ПОСТРОЕНИЕ =================

    void Build()
    {
        if (built || canvas == null) return;

        EnsureEventSystem();

        BuildActionPanel();
        BuildKeypad();

        built = true;
    }

    static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;

        var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        go.hideFlags = HideFlags.None;
    }

    void BuildActionPanel()
    {
        actionPanel = CreatePanel("MouseActionPanel", canvas.transform, panelSize, panelOffset, panelColor);

        // Подсказка над кнопками действий.
        promptText = CreateText("ActionPrompt", actionPanel, "Нажмите на объект мышью", 26f, TextAlignmentOptions.Center);
        SetRect(promptText.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -6f), new Vector2(0f, 34f));

        // Ряд кнопок действий.
        string[] labels = { "ВЗЯТЬ", "БРОСИТЬ", "ПРЫЖОК", "ПРИСЕСТЬ", "БЕГ" };
        UnityEngine.Events.UnityAction[] actions =
        {
            () => gameplay.RequestInteract(),
            () => gameplay.RequestUseOrThrow(),
            () => gameplay.RequestJump(),
            () => gameplay.ToggleCrouch(),
            () => gameplay.ToggleSprint()
        };

        float buttonWidth = 100f;
        float gap = 4f;
        float totalWidth = labels.Length * buttonWidth + (labels.Length - 1) * gap;
        float startX = -totalWidth * 0.5f + buttonWidth * 0.5f;

        for (int i = 0; i < labels.Length; i++)
        {
            UnityEngine.Events.UnityAction action = actions[i];
            Button button = CreateButton("Action_" + labels[i], actionPanel, labels[i], 22f, buttonWidth, 56f);
            button.GetComponent<RectTransform>().anchoredPosition = new Vector2(startX + i * (buttonWidth + gap), -62f);
            actionButtons.Add(button);
            actionButtonImages.Add(button.GetComponent<Image>());
        }

        // Подсказка по управлению.
        hintText = CreateText("ControlHint", actionPanel,
            "Мышь: тянуть — осмотр, клик — действие/идти, колесо — предмет",
            16f, TextAlignmentOptions.Center);
        SetRect(hintText.rectTransform, new Vector2(0f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 6f), new Vector2(0f, 24f));
    }

    void BuildKeypad()
    {
        keypadPanel = CreatePanel("MouseKeypad", canvas.transform, keypadSize, keypadOffset, panelColor);
        keypadGroup = keypadPanel.gameObject.AddComponent<CanvasGroup>();
        keypadGroup.alpha = 0f;
        keypadGroup.blocksRaycasts = false;
        keypadGroup.interactable = false;

        TMP_Text title = CreateText("KeypadTitle", keypadPanel, "КОД ДВЕРИ", 22f, TextAlignmentOptions.Center);
        SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -8f), new Vector2(0f, 30f));

        // Клавиатура 0-9: 5 в ряд, два ряда.
        for (int i = 0; i < 10; i++)
        {
            int digit = i;
            int row = i / 5;
            int col = i % 5;

            Button button = CreateButton("Digit_" + digit, keypadPanel, digit.ToString(), 28f, 58f, 52f);
            button.GetComponent<RectTransform>().anchoredPosition =
                new Vector2(-116f + col * 58f, -110f - row * 58f);

            button.onClick.AddListener(() =>
            {
                if (codeMonitor != null) codeMonitor.PressDigit(digit);
            });
        }

        Button backspace = CreateButton("Keypad_Backspace", keypadPanel, "ЗАБОЙ", 18f, 130f, 44f);
        backspace.GetComponent<RectTransform>().anchoredPosition = new Vector2(-75f, -232f);
        backspace.onClick.AddListener(() =>
        {
            if (codeMonitor != null) codeMonitor.Backspace();
        });

        Button ok = CreateButton("Keypad_OK", keypadPanel, "ОК", 20f, 110f, 44f);
        ok.GetComponent<RectTransform>().anchoredPosition = new Vector2(70f, -232f);
        ok.onClick.AddListener(() =>
        {
            if (codeMonitor != null) codeMonitor.Submit();
        });

        Button close = CreateButton("Keypad_Close", keypadPanel, "ЗАКРЫТЬ", 16f, 110f, 32f);
        close.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -26f);
        close.onClick.AddListener(() =>
        {
            if (codeMonitor != null) codeMonitor.EndInput();
        });
    }

    // ================= ОБНОВЛЕНИЕ =================

    void UpdatePrompt()
    {
        if (promptText == null) return;

        string prompt = null;
        if (gameplay != null)
        {
            var interactor = gameplay.GetComponent<PlayerInteractor>();
            if (interactor != null && interactor.Focused != null)
                prompt = interactor.Focused.InteractionPrompt;
        }

        promptText.text = string.IsNullOrEmpty(prompt)
            ? "Нажмите на объект мышью"
            : prompt.ToUpperInvariant();
    }

    void UpdateKeypadVisibility()
    {
        if (keypadGroup == null) return;

        bool show = codeMonitor != null && codeMonitor.IsEntering;
        float target = show ? 1f : 0f;

        if (!Mathf.Approximately(keypadGroup.alpha, target))
        {
            keypadGroup.alpha = Mathf.MoveTowards(keypadGroup.alpha, target, Time.unscaledDeltaTime * 6f);
            bool interactive = keypadGroup.alpha > 0.9f;
            keypadGroup.blocksRaycasts = interactive;
            keypadGroup.interactable = interactive;
        }
    }

    // ================= ХЕЛПЕРЫ =================

    RectTransform CreatePanel(string name, Transform parent, Vector2 size, Vector2 offset, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.sizeDelta = size;
        rect.anchoredPosition = offset;

        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;

        return rect;
    }

    TMP_Text CreateText(string name, Transform parent, string content, float size, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);

        var text = go.GetComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = textColor;
        text.raycastTarget = false;
        text.textWrappingMode = TMPro.TextWrappingModes.NoWrap;

        return text;
    }

    Button CreateButton(string name, Transform parent, string label, float fontSize, float width, float height)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);

        var rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(width, height);

        var image = go.GetComponent<Image>();
        image.color = buttonColor;

        var button = go.GetComponent<Button>();
        button.targetGraphic = image;

        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.6f, 1.4f, 0.6f, 1f);
        colors.pressedColor = new Color(1.2f, 1.1f, 0.5f, 1f);
        colors.selectedColor = Color.white;
        button.colors = colors;

        TMP_Text text = CreateText("Label", go.transform, label, fontSize, TextAlignmentOptions.Center);
        SetRect(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        return button;
    }

    static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }
}

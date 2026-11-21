using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Подсветка кнопки меню: при наведении или выборе с клавиатуры фон становится чёрным,
/// а подпись — белой. Референсы находятся автоматически, если не назначены в инспекторе.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public class MenuButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    [Header("Элементы")]
    [Tooltip("Фон кнопки. Если пусто — берётся Image с этого объекта.")]
    [SerializeField] private Image background;
    [Tooltip("Подпись кнопки. Если пусто — ищется TMP_Text в детях.")]
    [SerializeField] private TMP_Text label;

    [Header("Цвета")]
    [SerializeField] private Color normalBackground = Color.white;
    [SerializeField] private Color hoverBackground = new Color(0.06f, 0.06f, 0.06f, 1f);
    [SerializeField] private Color normalLabel = new Color(0.06f, 0.06f, 0.06f, 1f);
    [SerializeField] private Color hoverLabel = Color.white;

    [Header("Анимация")]
    [Tooltip("Скорость перехода между состояниями.")]
    [SerializeField, Min(1f)] private float transitionSpeed = 16f;
    [Tooltip("Масштаб кнопки при наведении. 1 — без увеличения.")]
    [SerializeField, Min(1f)] private float hoverScale = 1.02f;

    private RectTransform rect;
    private Vector3 baseScale = Vector3.one;
    private bool hovered;
    private bool selected;
    private float current;

    private bool IsHighlighted => hovered || selected;

    private void Awake()
    {
        if (background == null)
            background = GetComponent<Image>();

        if (label == null)
            label = GetComponentInChildren<TMP_Text>();

        rect = transform as RectTransform;

        if (rect != null)
            baseScale = rect.localScale;

        Apply(0f);
    }

    private void OnDisable()
    {
        hovered = false;
        selected = false;

        if (!gameObject.activeInHierarchy)
            return;

        current = 0f;
        Apply(0f);
    }

    private void Update()
    {
        float target = IsHighlighted ? 1f : 0f;

        if (Mathf.Approximately(current, target))
            return;

        current = Mathf.MoveTowards(current, target, transitionSpeed * Time.unscaledDeltaTime);
        Apply(current);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
    }

    public void OnSelect(BaseEventData eventData)
    {
        selected = true;
    }

    public void OnDeselect(BaseEventData eventData)
    {
        selected = false;
    }

    private void Apply(float weight)
    {
        if (background != null)
            background.color = Color.Lerp(normalBackground, hoverBackground, weight);

        if (label != null)
            label.color = Color.Lerp(normalLabel, hoverLabel, weight);

        if (rect != null && hoverScale > 1f)
            rect.localScale = Vector3.Lerp(baseScale, baseScale * hoverScale, weight);
    }
}

using TMPro;
using UnityEngine;

/// <summary>
/// Показывает только одну цифру общего кода.
/// Индекс листка: 0 — первая цифра, 1 — вторая и т.д.
/// </summary>
[DisallowMultipleComponent]
public class CodeSheet : MonoBehaviour
{
    [Header("Листок")]
    [Tooltip("Порядковый номер листка: 0 — первая цифра кода, 1 — вторая и т.д.")]
    [SerializeField] private int sheetIndex = -1;
    [Tooltip("Определить номер автоматически по цифре в конце имени объекта, например Paper_With_Code_2.")]
    [SerializeField] private bool autoDetectIndexFromName = true;

    [Header("Текст")]
    [Tooltip("Текст для цифры. Если пусто, найдётся первый TMP-текст в детях.")]
    [SerializeField] private TMP_Text codeText;

    /// <summary>Индекс листка в общем коде.</summary>
    public int SheetIndex => sheetIndex;

    /// <summary>Цифра, которая сейчас показана на листке.</summary>
    public string Digit { get; private set; } = string.Empty;

    /// <summary>TMP-текст, в который пишется цифра.</summary>
    public TMP_Text CodeText => codeText;

    private void Awake()
    {
        if (autoDetectIndexFromName && sheetIndex < 0)
            sheetIndex = DetectIndexFromName();

        if (codeText == null)
            codeText = FindTextInChildren();
    }

    private void OnEnable()
    {
        SecretCodeManager.CodeChanged -= Refresh;
        SecretCodeManager.CodeChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        SecretCodeManager.CodeChanged -= Refresh;
    }

    private void OnValidate()
    {
        if (sheetIndex < -1) sheetIndex = -1;
    }

    /// <summary>Задаёт индекс листка при автоматической настройке менеджером.</summary>
    public void SetSheetIndex(int index)
    {
        sheetIndex = Mathf.Max(0, index);
        Refresh();
    }

    /// <summary>Обновляет цифру на листке.</summary>
    public void Refresh()
    {
        if (codeText == null)
            codeText = FindTextInChildren();

        if (codeText == null) return;

        Digit = SecretCodeManager.Instance.GetDigit(sheetIndex);
        codeText.text = Digit;
    }

    private int DetectIndexFromName()
    {
        int end = name.Length;
        int start = end;

        while (start > 0 && char.IsDigit(name[start - 1])) start--;

        if (start == end) return 0;

        return int.TryParse(name.Substring(start, end - start), out int index) ? index : 0;
    }

    private TMP_Text FindTextInChildren()
    {
        TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
        return texts.Length > 0 ? texts[0] : null;
    }
}

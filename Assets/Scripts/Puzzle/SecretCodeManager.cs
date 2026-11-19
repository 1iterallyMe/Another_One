using System;
using System.Text;
using UnityEngine;

/// <summary>
/// Генерирует общий код из нескольких цифр (по умолчанию 4 — по числу листков)
/// и хранит его на время игры. Код можно сохранять между запусками (PlayerPrefs),
/// а также сверять с ним ввод на мониторе.
/// Если компонента нет в сцене — он создаётся автоматически при запуске игры.
/// </summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public class SecretCodeManager : MonoBehaviour
{
    private const string DefaultSaveKey = "SecretCode";

    [Header("Код")]
    [Tooltip("Сколько цифр в коде. По умолчанию 4 — по количеству листков.")]
    [SerializeField, Min(1)] private int codeLength = 4;
    [Tooltip("Сохранять код между запусками игры. Если выключено — новый код при каждом запуске.")]
    [SerializeField] private bool persistAcrossSessions = false;
    [Tooltip("Ключ сохранения в PlayerPrefs. Должен быть уникальным, если в игре несколько кодов.")]
    [SerializeField] private string saveKey = DefaultSaveKey;
    [Tooltip("Писать сгенерированный код в Console — удобно для проверки.")]
    [SerializeField] private bool logCodeToConsole = true;

    [Header("Листки")]
    [Tooltip("Автоматически находить листки по имени и записывать в них цифры кода.")]
    [SerializeField] private bool autoBindSheets = true;
    [Tooltip("Начало имени листка. Число в конце имени задаёт порядок: Paper_With_Code_0 — первая цифра.")]
    [SerializeField] private string sheetNamePrefix = "Paper_With_Code_";

    private static SecretCodeManager instance;
    private static string sessionCode;

    /// <summary>Единственный экземпляр менеджера. Создаётся автоматически при первом обращении.</summary>
    public static SecretCodeManager Instance
    {
        get
        {
            if (instance == null)
            {
                SecretCodeManager found = FindFirstObjectByType<SecretCodeManager>();
                instance = found != null ? found : CreateInstance();
            }

            return instance;
        }
    }

    public static bool HasInstance => instance != null;

    /// <summary>Событие после генерации нового кода (в том числе через Regenerate).</summary>
    public static event Action CodeChanged;

    /// <summary>Текущий код целиком, например "4821".</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Количество цифр в коде.</summary>
    public int Length => codeLength;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        LoadOrGenerateCode();
    }

    private void Start()
    {
        if (autoBindSheets) BindSheetsByName();
        ApplyToAllSheets();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void OnValidate()
    {
        codeLength = Mathf.Max(1, codeLength);
        if (string.IsNullOrEmpty(saveKey)) saveKey = DefaultSaveKey;
    }

    /// <summary>Возвращает цифру кода по индексу: 0 — первая, 1 — вторая и т.д.</summary>
    public string GetDigit(int index)
    {
        if (string.IsNullOrEmpty(Code) || index < 0 || index >= Code.Length)
            return string.Empty;

        return Code[index].ToString();
    }

    /// <summary>Проверяет, совпадает ли введённая строка с текущим кодом.</summary>
    public bool Verify(string input)
    {
        if (string.IsNullOrEmpty(input)) return false;
        return string.Equals(input.Trim(), Code, StringComparison.Ordinal);
    }

    /// <summary>Генерирует новый код и сразу обновляет все листки.</summary>
    public void Regenerate()
    {
        Code = GenerateCode();
        sessionCode = Code;
        SaveCode();
        ApplyToAllSheets();
        CodeChanged?.Invoke();

        if (logCodeToConsole)
            Debug.Log($"[SecretCode] Новый код: {Code}", this);
    }

    /// <summary>Удаляет сохранённый код из PlayerPrefs (пригодится при тестировании).</summary>
    [ContextMenu("Сбросить сохранённый код")]
    public void ClearSavedCode()
    {
        PlayerPrefs.DeleteKey(saveKey);
        PlayerPrefs.Save();
    }

    /// <summary>Обновляет текст на всех листках сцены.</summary>
    public void ApplyToAllSheets()
    {
        CodeSheet[] sheets = FindObjectsByType<CodeSheet>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (CodeSheet sheet in sheets)
            sheet.Refresh();
    }

    private void LoadOrGenerateCode()
    {
        codeLength = Mathf.Max(1, codeLength);

        if (persistAcrossSessions)
        {
            string saved = PlayerPrefs.GetString(saveKey, string.Empty);
            if (IsValidCode(saved))
            {
                Code = saved;
                sessionCode = saved;

                if (logCodeToConsole)
                    Debug.Log($"[SecretCode] Загружен сохранённый код: {Code}", this);

                return;
            }
        }
        else if (IsValidCode(sessionCode))
        {
            // Код уже создан в этой сессии — переживает перезагрузку сцены (например, после смерти).
            Code = sessionCode;

            if (logCodeToConsole)
                Debug.Log($"[SecretCode] Код текущей сессии: {Code}", this);

            return;
        }

        Code = GenerateCode();
        sessionCode = Code;
        SaveCode();

        if (logCodeToConsole)
            Debug.Log($"[SecretCode] Сгенерирован код: {Code}", this);
    }

    private string GenerateCode()
    {
        StringBuilder builder = new StringBuilder(codeLength);

        for (int i = 0; i < codeLength; i++)
        {
            // Первая цифра не ноль — код должен быть настоящим N-значным числом.
            int minValue = i == 0 ? 1 : 0;
            builder.Append(UnityEngine.Random.Range(minValue, 10));
        }

        return builder.ToString();
    }

    private void SaveCode()
    {
        if (!persistAcrossSessions) return;

        PlayerPrefs.SetString(saveKey, Code);
        PlayerPrefs.Save();
    }

    private bool IsValidCode(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length != codeLength) return false;

        foreach (char symbol in value)
        {
            if (!char.IsDigit(symbol)) return false;
        }

        return true;
    }

    private void BindSheetsByName()
    {
        if (string.IsNullOrEmpty(sheetNamePrefix)) return;

        Transform[] allTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (Transform candidate in allTransforms)
        {
            if (!candidate.name.StartsWith(sheetNamePrefix, StringComparison.Ordinal)) continue;
            if (!TryParseTrailingNumber(candidate.name, out int index)) continue;
            if (index < 0 || index >= codeLength) continue;

            CodeSheet sheet = candidate.GetComponent<CodeSheet>();
            if (sheet == null) sheet = candidate.gameObject.AddComponent<CodeSheet>();

            // Ручной индекс из инспектора не перезаписываем — задаём только если он ещё не определён.
            if (sheet.SheetIndex < 0) sheet.SetSheetIndex(index);
        }
    }

    private static bool TryParseTrailingNumber(string value, out int number)
    {
        number = -1;

        int start = value.Length;
        while (start > 0 && char.IsDigit(value[start - 1])) start--;

        if (start == value.Length) return false;

        return int.TryParse(value.Substring(start), out number);
    }

    private static SecretCodeManager CreateInstance()
    {
        GameObject go = new GameObject(nameof(SecretCodeManager));
        return go.AddComponent<SecretCodeManager>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstanceExists()
    {
        if (instance != null) return;
        if (FindFirstObjectByType<SecretCodeManager>() != null) return;

        CreateInstance();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        sessionCode = null;
        CodeChanged = null;
    }
}


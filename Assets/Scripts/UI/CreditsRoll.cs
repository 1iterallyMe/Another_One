using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Прокручивает титры снизу вверх и после окончания загружает сцену меню.
/// Вешается на объект с TMP_Text (например, Title в сцене титров).
/// </summary>
[DisallowMultipleComponent]
public class CreditsRoll : MonoBehaviour
{
    [Header("Что прокручиваем")]
    [Tooltip("Объект с текстом титров. Если пусто — берётся RectTransform этого объекта.")]
    [SerializeField] private RectTransform creditsContent;
    [Tooltip("Текст титров. Нужен, чтобы посчитать высоту. Если пусто — ищется на этом объекте.")]
    [SerializeField] private TMP_Text creditsText;

    [Header("Движение")]
    [Tooltip("Скорость прокрутки в пикселях UI в секунду.")]
    [SerializeField, Min(1f)] private float scrollSpeed = 80f;
    [Tooltip("Отступ под нижним краем экрана, с которого начинается текст.")]
    [SerializeField, Min(0f)] private float startPadding = 150f;
    [Tooltip("Отступ над верхним краем экрана, на котором текст заканчивает прокрутку.")]
    [SerializeField, Min(0f)] private float endPadding = 150f;
    [Tooltip("Пауза перед началом прокрутки, сек.")]
    [SerializeField, Min(0f)] private float startDelay = 1f;
    [Tooltip("Прокручивать по неигровому времени (работает при Time.timeScale = 0).")]
    [SerializeField] private bool useUnscaledTime = true;
    [Tooltip("Запускать прокрутку автоматически при старте сцены.")]
    [SerializeField] private bool playOnStart = true;

    [Header("Переход в меню")]
    [Tooltip("Имя сцены меню. Сцена должна быть добавлена в Build Settings.")]
    [SerializeField] private string menuSceneName = "Menu_Scene";
    [Tooltip("Запасная сцена, если сцены меню нет в Build Settings. Можно оставить пустой.")]
    [SerializeField] private string fallbackSceneName = "Amir";
    [Tooltip("Пауза после конца титров перед загрузкой меню, сек.")]
    [SerializeField, Min(0f)] private float endDelay = 0.5f;
    [Tooltip("Загружать сцену асинхронно.")]
    [SerializeField] private bool loadAsync = true;
    [Tooltip("Разрешить пропустить титры клавишей.")]
    [SerializeField] private bool allowSkip = true;
    [SerializeField] private KeyCode skipKey = KeyCode.Escape;

    private RectTransform viewport;
    private Vector2 initialPosition;
    private float startY;
    private float endY;
    private float travelTime;
    private Coroutine rollRoutine;
    private bool loading;

    private float DeltaTime => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

    private void Awake()
    {
        ResolveReferences();
        FixZeroCanvasScale();

        // Титры — неигровая сцена: снимаем возможную паузу и освобождаем курсор,
        // чтобы работала кнопка Menu и прокрутка не зависела от предыдущей сцены.
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    /// <summary>
    /// Страховка от случайно выставленного нулевого масштаба Canvas:
    /// иначе UI-титры не видны вообще.
    /// </summary>
    private void FixZeroCanvasScale()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
            return;

        Vector3 scale = canvas.transform.localScale;
        if (Mathf.Abs(scale.x) > 0.0001f || Mathf.Abs(scale.y) > 0.0001f)
            return;

        canvas.transform.localScale = Vector3.one;
        Debug.LogWarning($"[{nameof(CreditsRoll)}] У Canvas был нулевой масштаб — включил 1, иначе титры не видны.", canvas);
    }

    private void Start()
    {
        if (playOnStart)
            Begin();
    }

    /// <summary>Запускает прокрутку титров сначала.</summary>
    public void Begin()
    {
        if (loading)
            return;

        if (rollRoutine != null)
            StopCoroutine(rollRoutine);

        if (!Prepare())
            return;

        rollRoutine = StartCoroutine(RollRoutine());
    }

    /// <summary>Пропускает титры и сразу загружает меню. Можно вешать на кнопку Menu.</summary>
    public void SkipToMenu()
    {
        if (loading)
            return;

        if (rollRoutine != null)
        {
            StopCoroutine(rollRoutine);
            rollRoutine = null;
        }

        LoadMenu();
    }

    private void ResolveReferences()
    {
        if (creditsContent == null)
            creditsContent = transform as RectTransform;

        if (creditsText == null)
            creditsText = GetComponent<TMP_Text>();

        if (creditsContent != null)
            viewport = creditsContent.parent as RectTransform;
    }

    private bool Prepare()
    {
        ResolveReferences();

        if (creditsContent == null)
        {
            Debug.LogError($"[{nameof(CreditsRoll)}] Не найден RectTransform титров.", this);
            return false;
        }

        if (creditsText == null)
        {
            Debug.LogError($"[{nameof(CreditsRoll)}] Не найден TMP_Text с текстом титров.", this);
            return false;
        }

        creditsText.ForceMeshUpdate();

        if (string.IsNullOrWhiteSpace(creditsText.text))
            Debug.LogWarning($"[{nameof(CreditsRoll)}] Текст титров пуст — добавь текст в «{creditsText.name}».", creditsText);

        float contentHeight = creditsText.preferredHeight;
        if (contentHeight <= 0.01f)
            contentHeight = creditsContent.rect.height;

        if (contentHeight <= 0.01f)
        {
            Debug.LogError($"[{nameof(CreditsRoll)}] Не удалось определить высоту текста титров.", this);
            return false;
        }

        float viewportHeight = GetViewportHeight();
        initialPosition = creditsContent.anchoredPosition;

        // textBounds точнее, чем preferredHeight: учитывает вертикальное выравнивание
        // и реальные границы текста, даже если он выходит за RectTransform.
        Bounds textBounds = creditsText.textBounds;
        bool hasBounds = textBounds.size.y > 0.01f;
        float bottomOffset = hasBounds ? textBounds.min.y : -contentHeight * creditsContent.pivot.y;
        float topOffset = hasBounds ? textBounds.max.y : contentHeight * (1f - creditsContent.pivot.y);

        // Титры начинаются так, чтобы первая строка была под экраном,
        // и заканчиваются, когда последняя строка уходит над экраном.
        startY = -viewportHeight * 0.5f - startPadding - topOffset;
        endY = viewportHeight * 0.5f + endPadding - bottomOffset;

        if (endY <= startY)
        {
            Debug.LogWarning($"[{nameof(CreditsRoll)}] Высота текста меньше высоты экрана — прокрутка будет очень короткой.", this);
            endY = startY + 1f;
        }

        travelTime = (endY - startY) / Mathf.Max(1f, scrollSpeed);

        creditsContent.anchoredPosition = new Vector2(initialPosition.x, startY);
        return true;
    }

    private float GetViewportHeight()
    {
        if (viewport != null && viewport.rect.height > 0.01f)
            return viewport.rect.height;

        Canvas canvas = GetComponentInParent<Canvas>();
        float scaleFactor = canvas != null ? Mathf.Max(0.0001f, canvas.scaleFactor) : 1f;
        return Screen.height / scaleFactor;
    }

    private IEnumerator RollRoutine()
    {
        if (startDelay > 0f)
            yield return Wait(startDelay);

        float elapsed = 0f;

        while (elapsed < travelTime)
        {
            if (allowSkip && Input.GetKeyDown(skipKey))
            {
                rollRoutine = null;
                LoadMenu();
                yield break;
            }

            elapsed += DeltaTime;
            float t = Mathf.Clamp01(elapsed / travelTime);
            creditsContent.anchoredPosition = new Vector2(initialPosition.x, Mathf.Lerp(startY, endY, t));
            yield return null;
        }

        creditsContent.anchoredPosition = new Vector2(initialPosition.x, endY);

        if (endDelay > 0f)
            yield return Wait(endDelay);

        rollRoutine = null;
        LoadMenu();
    }

    private IEnumerator Wait(float seconds)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += DeltaTime;
            yield return null;
        }
    }

    private void LoadMenu()
    {
        if (loading)
            return;

        loading = true;

        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        string sceneName = ResolveMenuScene();

        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError($"[{nameof(CreditsRoll)}] Не найдена сцена меню «{menuSceneName}» и запасная «{fallbackSceneName}». Добавь их в Build Settings.", this);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
            return;
        }

        if (loadAsync)
            StartCoroutine(LoadSceneRoutine(sceneName));
        else
            SceneManager.LoadScene(sceneName);
    }

    private string ResolveMenuScene()
    {
        if (!string.IsNullOrWhiteSpace(menuSceneName) && Application.CanStreamedLevelBeLoaded(menuSceneName))
            return menuSceneName;

        if (!string.IsNullOrWhiteSpace(fallbackSceneName) && Application.CanStreamedLevelBeLoaded(fallbackSceneName))
        {
            Debug.LogWarning($"[{nameof(CreditsRoll)}] Сцена меню «{menuSceneName}» не добавлена в Build Settings. Загружаю запасную «{fallbackSceneName}».", this);
            return fallbackSceneName;
        }

        return null;
    }

    private IEnumerator LoadSceneRoutine(string sceneName)
    {
        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);

        if (operation == null)
        {
            Debug.LogError($"[{nameof(CreditsRoll)}] Не удалось загрузить сцену «{sceneName}».", this);
            loading = false;
            yield break;
        }

        while (!operation.isDone)
            yield return null;
    }

    private void OnValidate()
    {
        ResolveReferences();
    }
}


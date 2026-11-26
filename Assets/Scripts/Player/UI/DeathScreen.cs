using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasGroup))]
public class DeathScreen : MonoBehaviour
{
    [Header("Элементы")]
    public TMP_Text titleText;
    public Button restartButton;
    public Button menuButton;

    [Header("Текст по умолчанию")]
    [Tooltip("Этот текст покажется, если ловушка не передала свой")]
    public string defaultTitle = "ВЫ УМЕРЛИ";

    [Header("Подписи кнопок")]
    [Tooltip("Подпись кнопки перезапуска (в сцене английская — переводим на русский).")]
    public string restartLabel = "ЗАНОВО";
    [Tooltip("Подпись кнопки выхода в меню.")]
    public string menuLabel = "В МЕНЮ";

    private const string restartButtonLabel = "Restart";
    private const string menuButtonLabel = "Menu";
    private const string LegacyTitle = "You are death";

    [Header("Меню")]
    public string menuSceneName = "";

    [Header("Появление")]
    public float fadeTime = 1f;
    public bool pauseWhenShown = true;

    CanvasGroup group;
    bool shown;

    /// <summary>
    /// TMP-шрифт с кириллицей. В сцене у надписей стоит LiberationSans SDF,
    /// в котором нет русских букв — такие надписи просто не рисуются.
    /// </summary>
    private static TMP_FontAsset ResolveCyrillicFont()
    {
        var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        foreach (var f in fonts)
        {
            if (f == null) continue;
            if (f.name == "LiberationSansCyrillic SDF") return f;
        }
        return TMP_Settings.defaultFontAsset;
    }

    void Awake()
    {
        group = GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        Time.timeScale = 1f;

        ApplyCyrillicFont();

        if (titleText != null) titleText.text = ResolveTitleText();
        if (restartButton != null) restartButton.onClick.AddListener(Restart);
        if (menuButton != null) menuButton.onClick.AddListener(ToMenu);
    }

    /// <summary>
    /// Заголовок экрана смерти. В префабе сохранён английский вариант,
    /// поэтому переводим его на русский.
    /// </summary>
    private string ResolveTitleText()
    {
        if (string.IsNullOrEmpty(defaultTitle) || defaultTitle == LegacyTitle)
            return "ВЫ УМЕРЛИ";
        return defaultTitle;
    }

    /// <summary>Назначает шрифт с кириллицей всем надписям экрана смерти.</summary>
    void ApplyCyrillicFont()
    {
        var font = ResolveCyrillicFont();
        if (font == null) return;

        var texts = GetComponentsInChildren<TMP_Text>(true);
        foreach (var t in texts)
        {
            if (t == null) continue;
            t.font = font;
        }

        // Надписи на кнопках в сцене английские — переводим на русский.
        SetButtonLabel(restartButton, restartLabel);
        SetButtonLabel(menuButton, menuLabel);
    }

    /// <summary>Заменяет надпись на кнопке, если она ещё не задана.</summary>
    void SetButtonLabel(Button button, string label)
    {
        if (button == null || string.IsNullOrEmpty(label)) return;

        var text = button.GetComponentInChildren<TMP_Text>(true);
        if (text == null) return;

        // Не трогаем надпись, если сцена задаёт свою (сравниваем с исходной).
        if (text.text == restartButtonLabel || text.text == menuButtonLabel || string.IsNullOrEmpty(text.text))
            text.text = label;
    }

    // ПРИНИМАЕМ ТЕКСТ ОТ ЛОВУШКИ
    public void Show(string message = "")
    {
        if (shown) return;
        shown = true;

        // Экран может лежать в сцене выключенным: тогда Awake не вызывался,
        // CanvasGroup не инициализирован и корутину запустить нельзя.
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        if (group == null) group = GetComponent<CanvasGroup>();
        if (group != null)
        {
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
        }

        // Awake не вызывался (объект был выключен в сцене) — шрифт с кириллицей
        // и русские подписи кнопок назначаем здесь.
        ApplyCyrillicFont();

        // Если текст передан, меняем его на экране
        if (titleText != null && !string.IsNullOrEmpty(message))
        {
            titleText.text = message;
        }

        StartCoroutine(FadeIn());
    }

    IEnumerator FadeIn()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        group.blocksRaycasts = true;

        for (float t = 0f; t < fadeTime; t += Time.unscaledDeltaTime)
        {
            group.alpha = t / fadeTime;
            yield return null;
        }

        group.alpha = 1f;
        group.interactable = true;
        if (pauseWhenShown) Time.timeScale = 0f;
    }

    public void Restart()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void ToMenu()
    {
        Time.timeScale = 1f;

        if (!string.IsNullOrEmpty(menuSceneName))
        {
            SceneManager.LoadScene(menuSceneName);
            return;
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void OnDestroy()
    {
        // Страховка: не оставляем игру на паузе после выгрузки сцены / остановки Play Mode.
        Time.timeScale = 1f;
    }
}
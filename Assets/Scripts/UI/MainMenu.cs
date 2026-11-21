using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Главное меню игры: запускает игровую сцену, открывает титры и закрывает приложение.
/// Вешается на Canvas сцены меню, кнопки и элементы появления назначаются в инспекторе.
/// </summary>
[DisallowMultipleComponent]
public class MainMenu : MonoBehaviour
{
    [Header("Сцены")]
    [Tooltip("Сцена, которая загружается по кнопке «Играть». Должна быть в Build Settings.")]
    [SerializeField] private string gameSceneName = "Amir";
    [Tooltip("Сцена титров. Должна быть в Build Settings.")]
    [SerializeField] private string creditsSceneName = "Сredits_Scene";

    [Header("Кнопки")]
    [SerializeField] private Button playButton;
    [SerializeField] private Button creditsButton;
    [SerializeField] private Button quitButton;

    [Header("Появление")]
    [Tooltip("Группа с содержимым меню, которая плавно проявляется при запуске сцены.")]
    [SerializeField] private CanvasGroup menuGroup;
    [Tooltip("Чёрная заливка поверх экрана, которая плавно исчезает при запуске.")]
    [SerializeField] private Image fadeOverlay;
    [Tooltip("Длительность появления и исчезновения, сек.")]
    [SerializeField, Min(0f)] private float fadeDuration = 0.7f;

    [Header("Версия")]
    [Tooltip("Текст в углу экрана. Если пусто — версия не выводится.")]
    [SerializeField] private TMP_Text versionText;
    [SerializeField] private string versionPrefix = "ВЕРСИЯ ";

    private bool loading;

    private void Awake()
    {
        // Меню — неигровая сцена: снимаем возможную паузу и освобождаем курсор.
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (versionText != null)
            versionText.text = versionPrefix + Application.version;

        if (playButton != null) playButton.onClick.AddListener(StartGame);
        if (creditsButton != null) creditsButton.onClick.AddListener(OpenCredits);
        if (quitButton != null) quitButton.onClick.AddListener(QuitGame);

        if (fadeOverlay != null)
        {
            fadeOverlay.raycastTarget = false;
            SetOverlayAlpha(1f);
        }

        if (menuGroup != null)
        {
            menuGroup.alpha = 0f;
            menuGroup.interactable = false;
            menuGroup.blocksRaycasts = false;
        }
    }

    private void Start()
    {
        StartCoroutine(FadeInRoutine());
    }

    /// <summary>Кнопка «Играть».</summary>
    public void StartGame()
    {
        LoadScene(gameSceneName);
    }

    /// <summary>Кнопка «Титры».</summary>
    public void OpenCredits()
    {
        LoadScene(creditsSceneName);
    }

    /// <summary>Кнопка «Выход»: в редакторе выключает Play Mode.</summary>
    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private IEnumerator FadeInRoutine()
    {
        if (menuGroup != null)
            menuGroup.blocksRaycasts = true;

        yield return CrossFadeRoutine(0f, 1f, 1f, 0f);

        if (menuGroup != null)
        {
            menuGroup.alpha = 1f;
            menuGroup.interactable = true;
        }

        if (fadeOverlay != null)
            fadeOverlay.gameObject.SetActive(false);
    }

    /// <summary>Проверяет имя сцены и запускает загрузку с чёрной заливкой.</summary>
    private void LoadScene(string sceneName)
    {
        if (loading)
            return;

        if (string.IsNullOrWhiteSpace(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"[{nameof(MainMenu)}] Сцена «{sceneName}» недоступна. Добавь её в Build Settings.", this);
            return;
        }

        StartCoroutine(LoadRoutine(sceneName));
    }

    private IEnumerator LoadRoutine(string sceneName)
    {
        loading = true;
        SetButtonsInteractable(false);

        if (fadeOverlay != null)
        {
            fadeOverlay.gameObject.SetActive(true);
            fadeOverlay.raycastTarget = true;
        }

        yield return CrossFadeRoutine(1f, 0f, 0f, 1f);

        Time.timeScale = 1f;

        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);

        if (operation == null)
        {
            Debug.LogError($"[{nameof(MainMenu)}] Не удалось загрузить сцену «{sceneName}». Добавь её в Build Settings.", this);
            loading = false;
            SetButtonsInteractable(true);
            if (fadeOverlay != null)
                fadeOverlay.raycastTarget = false;
            StartCoroutine(FadeInRoutine());
            yield break;
        }

        while (!operation.isDone)
            yield return null;
    }

    /// <summary>Плавно меняет прозрачность меню и чёрной заливки одновременно.</summary>
    private IEnumerator CrossFadeRoutine(float groupFrom, float groupTo, float overlayFrom, float overlayTo)
    {
        float duration = Mathf.Max(0.0001f, fadeDuration);

        for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            float progress = Mathf.Clamp01(elapsed / duration);

            if (menuGroup != null)
                menuGroup.alpha = Mathf.Lerp(groupFrom, groupTo, progress);

            SetOverlayAlpha(Mathf.Lerp(overlayFrom, overlayTo, progress));

            yield return null;
        }

        if (menuGroup != null)
            menuGroup.alpha = groupTo;

        SetOverlayAlpha(overlayTo);
    }

    private void SetOverlayAlpha(float alpha)
    {
        if (fadeOverlay == null)
            return;

        Color color = fadeOverlay.color;
        color.a = alpha;
        fadeOverlay.color = color;
    }

    private void SetButtonsInteractable(bool value)
    {
        if (playButton != null) playButton.interactable = value;
        if (creditsButton != null) creditsButton.interactable = value;
        if (quitButton != null) quitButton.interactable = value;
    }
}

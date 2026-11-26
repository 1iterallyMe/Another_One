using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Главное меню игры: запускает игровую сцену, открывает титры и закрывает приложение.
/// Вешается на Canvas сцены меню, кнопки и элементы появления назначаются в инспекторе.
/// </summary>
[DisallowMultipleComponent]
public class MainMenu : MonoBehaviour
{
    private const string MusicMutedKey = "AnotherOne.MusicMuted";

    [Header("Сцены")]
    [Tooltip("Сцена, которая загружается по кнопке «Играть». Должна быть в Build Settings.")]
    [SerializeField] private string gameSceneName = "Amir";
    [Tooltip("Сцена титров. Должна быть в Build Settings.")]
    [SerializeField] private string creditsSceneName = "Credits_Scene";

    [Header("Кнопки")]
    [SerializeField] private Button playButton;
    [SerializeField] private Button creditsButton;
    [Tooltip("Кнопка с краткой памяткой по управлению.")]
    [SerializeField] private Button controlsButton;
    [Tooltip("Кнопка переключения музыки меню и титров.")]
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitButton;
    [Tooltip("Кнопка, которая получает фокус при открытии меню.")]
    [SerializeField] private Button firstSelectedButton;

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
    [Tooltip("Информационная строка для управления и состояния музыки.")]
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private string controlsMessage = "WASD — ДВИЖЕНИЕ\nМЫШЬ — ОСМОТРЕТЬСЯ\nE — ВЗАИМОДЕЙСТВИЕ\nESC — МЕНЮ";

    [Header("Звук")]
    [Tooltip("Музыка меню. Если пусто, будет найден первый AudioSource в сцене.")]
    [SerializeField] private AudioSource menuMusic;
    [SerializeField, Min(0f)] private float statusDuration = 7f;

    private bool loading;
    private Coroutine statusRoutine;

    public static bool MusicMuted => PlayerPrefs.GetInt(MusicMutedKey, 0) == 1;

    public static void SetMusicMuted(bool muted)
    {
        PlayerPrefs.SetInt(MusicMutedKey, muted ? 1 : 0);
        PlayerPrefs.Save();
    }

    private void Awake()
    {
        ResolveReferences();

        // Меню — неигровая сцена: снимаем возможную паузу и освобождаем курсор.
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (versionText != null)
            versionText.text = versionPrefix + Application.version;

        AddListener(playButton, StartGame);
        AddListener(creditsButton, OpenCredits);
        AddListener(controlsButton, ShowControls);
        AddListener(settingsButton, ToggleMusic);
        AddListener(quitButton, QuitGame);

        if (menuMusic != null)
            menuMusic.mute = MusicMuted;

        SetStatus("ВЫБЕРИТЕ ДЕЙСТВИЕ", 0f);

        if (fadeOverlay != null)
        {
            fadeOverlay.gameObject.SetActive(true);
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

    /// <summary>Показывает краткую памятку, не покидая меню.</summary>
    public void ShowControls()
    {
        SetStatus(controlsMessage, statusDuration);
    }

    /// <summary>Переключает музыку и сохраняет настройку для сцены титров.</summary>
    public void ToggleMusic()
    {
        bool muted = !MusicMuted;
        SetMusicMuted(muted);

        if (menuMusic != null)
            menuMusic.mute = muted;

        SetStatus(muted ? "МУЗЫКА: ВЫКЛЮЧЕНА" : "МУЗЫКА: ВКЛЮЧЕНА", statusDuration);
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

        SelectFirstButton();

        if (fadeOverlay != null)
            fadeOverlay.gameObject.SetActive(false);
    }

    /// <summary>Проверяет имя сцены и запускает загрузку с чёрной заливкой.</summary>
    private void LoadScene(string sceneName)
    {
        if (loading)
            return;

        if (string.IsNullOrWhiteSpace(sceneName) || !SceneLoadUtility.Exists(sceneName))
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

        if (menuGroup != null)
        {
            menuGroup.interactable = false;
            menuGroup.blocksRaycasts = false;
        }

        if (fadeOverlay != null)
        {
            fadeOverlay.gameObject.SetActive(true);
            fadeOverlay.raycastTarget = true;
        }

        yield return CrossFadeRoutine(1f, 0f, 0f, 1f);

        Time.timeScale = 1f;

        AsyncOperation operation = SceneLoadUtility.LoadAsync(sceneName);

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
        if (controlsButton != null) controlsButton.interactable = value;
        if (settingsButton != null) settingsButton.interactable = value;
        if (quitButton != null) quitButton.interactable = value;
    }

    private void ResolveReferences()
    {
        if (playButton == null) playButton = FindButton("Play Button");
        if (creditsButton == null) creditsButton = FindButton("Credits Button");
        if (controlsButton == null) controlsButton = FindButton("Controls Button", "Credits Button (1)");
        if (settingsButton == null) settingsButton = FindButton("Settings Button", "Credits Button (2)");
        if (quitButton == null) quitButton = FindButton("Quit Button");
        if (firstSelectedButton == null) firstSelectedButton = playButton;

        Transform root = transform;
        if (statusText == null)
            statusText = FindText(root, "Menu Status");
        if (versionText == null)
            versionText = FindText(root, "Version");

        if (menuMusic == null)
        {
            GameObject musicObject = GameObject.Find("Menu Music");
            if (musicObject != null)
                menuMusic = musicObject.GetComponent<AudioSource>();
        }
    }

    private Button FindButton(params string[] names)
    {
        foreach (string name in names)
        {
            Transform candidate = transform.Find("Menu Root/" + name);
            Button button = candidate != null ? candidate.GetComponent<Button>() : null;
            if (button != null)
                return button;
        }

        return null;
    }

    private static TMP_Text FindText(Transform root, string objectName)
    {
        Transform candidate = root.Find("Menu Root/" + objectName);
        return candidate != null ? candidate.GetComponent<TMP_Text>() : null;
    }

    private static void AddListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
            return;

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    private void SelectFirstButton()
    {
        if (firstSelectedButton == null || EventSystem.current == null)
            return;

        EventSystem.current.SetSelectedGameObject(null);
        EventSystem.current.SetSelectedGameObject(firstSelectedButton.gameObject);
    }

    private void SetStatus(string message, float clearAfter)
    {
        if (statusText == null)
            return;

        if (statusRoutine != null)
            StopCoroutine(statusRoutine);

        statusText.text = message;

        if (clearAfter > 0f)
            statusRoutine = StartCoroutine(ClearStatusRoutine(clearAfter));
    }

    private IEnumerator ClearStatusRoutine(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        statusText.text = "ВЫБЕРИТЕ ДЕЙСТВИЕ";
        statusRoutine = null;
    }
}

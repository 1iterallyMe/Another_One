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

    [Header("Меню")]
    public string menuSceneName = "";

    [Header("Появление")]
    public float fadeTime = 1f;
    public bool pauseWhenShown = true;

    CanvasGroup group;
    bool shown;

    void Awake()
    {
        group = GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        Time.timeScale = 1f; 

        if (titleText != null) titleText.text = defaultTitle;
        if (restartButton != null) restartButton.onClick.AddListener(Restart);
        if (menuButton != null) menuButton.onClick.AddListener(ToMenu);
    }

    // ПРИНИМАЕМ ТЕКСТ ОТ ЛОВУШКИ
    public void Show(string message = "")
    {
        if (shown) return;
        shown = true;

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
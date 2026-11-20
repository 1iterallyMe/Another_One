using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Триггер-переход между сценами: когда игрок входит в него — загружается указанная сцена.
/// Работает и с асинхронной загрузкой, и с точкой появления в новой сцене.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class SceneTrigger : MonoBehaviour
{
    [Header("Сцена")]
    [Tooltip("Имя сцены, которая загрузится, например Alexander. Сцена должна быть добавлена в Build Profiles / Build Settings.")]
    [SerializeField] private string sceneName = "";

    [Header("Кто считается игроком")]
    [Tooltip("Проверять, что в объекте есть FPSController.")]
    [SerializeField] private bool detectByPlayerComponent = true;
    [Tooltip("Дополнительно проверять тег (у игрока он Player). Пусто — тег не проверяется.")]
    [SerializeField] private string playerTag = "Player";
    [Tooltip("Слои, которые могут активировать триггер.")]
    [SerializeField] private LayerMask playerMask = ~0;

    [Header("Переход")]
    [Tooltip("Задержка перед загрузкой сцены, сек. 0 — сразу.")]
    [SerializeField, Min(0f)] private float loadDelay = 0f;
    [Tooltip("Загружать сцену асинхронно (без фриза).")]
    [SerializeField] private bool loadAsync = true;
    [Tooltip("Срабатывать только один раз за запуск сцены.")]
    [SerializeField] private bool triggerOnce = true;
    [Tooltip("Необязательно: имя объекта-точки появления в следующей сцене. Пусто — игрок появится там, где он стоит в сцене.")]
    [SerializeField] private string spawnPointName = "";

    // Имя точки появления передаётся в статический обработчик, потому что после
    // загрузки сцены этот объект (и его поля) уже уничтожаются.
    private static string pendingSpawnPoint;

    private bool isLoading;
    private bool used;
    private bool warnedAboutScene;

    private void Awake()
    {
        Collider trigger = GetComponent<Collider>();

        if (trigger == null)
        {
            Debug.LogError($"[{nameof(SceneTrigger)}] На объекте нет коллайдера. Добавь, например, BoxCollider и включи Is Trigger.", this);
            enabled = false;
            return;
        }

        if (!trigger.isTrigger)
        {
            trigger.isTrigger = true;
            Debug.LogWarning($"[{nameof(SceneTrigger)}] Коллайдер не был триггером — включил Is Trigger автоматически.", this);
        }

        CheckScene();
    }

    private void OnValidate()
    {
        // Удобно: при добавлении скрипта коллайдер сразу становится триггером.
        Collider trigger = GetComponent<Collider>();
        if (trigger != null && !trigger.isTrigger)
            trigger.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isLoading) return;
        if (triggerOnce && used) return;
        if (!IsPlayer(other)) return;

        CheckScene();
        if (warnedAboutScene) return;

        used = true;
        StartCoroutine(LoadRoutine());
    }

    /// <summary>Проверяет, что объект — игрок.</summary>
    private bool IsPlayer(Collider other)
    {
        if (other == null) return false;

        int layerBit = 1 << other.gameObject.layer;
        if ((playerMask.value & layerBit) == 0) return false;

        bool checkComponent = detectByPlayerComponent;
        bool checkTag = !string.IsNullOrEmpty(playerTag);

        // Если оба способа проверки выключены — реагируем на любой объект с разрешённого слоя.
        if (!checkComponent && !checkTag) return true;

        if (checkComponent)
        {
            if (other.GetComponentInParent<FPSController>() != null) return true;
            if (other.attachedRigidbody != null &&
                other.attachedRigidbody.GetComponentInParent<FPSController>() != null) return true;
        }

        if (checkTag)
        {
            if (other.CompareTag(playerTag)) return true;
            if (other.transform.root.CompareTag(playerTag)) return true;
        }

        return false;
    }

    private IEnumerator LoadRoutine()
    {
        isLoading = true;

        if (loadDelay > 0f)
            yield return new WaitForSeconds(loadDelay);

        pendingSpawnPoint = spawnPointName;

        if (!string.IsNullOrEmpty(pendingSpawnPoint))
            SceneManager.sceneLoaded += HandleSceneLoaded;

        if (loadAsync)
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);

            if (operation == null)
            {
                Debug.LogError($"[{nameof(SceneTrigger)}] Не удалось загрузить сцену «{sceneName}». Добавь её в Build Profiles / Build Settings.", this);
                SceneManager.sceneLoaded -= HandleSceneLoaded;
                pendingSpawnPoint = string.Empty;
                isLoading = false;
                yield break;
            }

            while (!operation.isDone)
                yield return null;
        }
        else
        {
            SceneManager.LoadScene(sceneName);
        }
    }

    /// <summary>После загрузки сцены переносит игрока в точку появления (если она задана).</summary>
    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;

        if (string.IsNullOrEmpty(pendingSpawnPoint))
            return;

        string pointName = pendingSpawnPoint;
        pendingSpawnPoint = string.Empty;

        GameObject point = GameObject.Find(pointName);

        if (point == null)
        {
            Debug.LogWarning($"[{nameof(SceneTrigger)}] В сцене «{scene.name}» не найден объект-точка появления «{pointName}». Игрок остался на месте.");
            return;
        }

        FPSController player = Object.FindFirstObjectByType<FPSController>();
        if (player == null)
        {
            Debug.LogWarning($"[{nameof(SceneTrigger)}] В сцене «{scene.name}» не найден игрок (FPSController).");
            return;
        }

        Rigidbody body = player.GetComponent<Rigidbody>();
        Quaternion rotation = Quaternion.Euler(0f, point.transform.eulerAngles.y, 0f);

        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = point.transform.position;
            body.rotation = rotation;
        }

        player.transform.SetPositionAndRotation(point.transform.position, rotation);
    }

    /// <summary>Предупреждает, если сцена не указана или её нет в Build Settings.</summary>
    private void CheckScene()
    {
        if (warnedAboutScene) return;

        if (string.IsNullOrWhiteSpace(sceneName))
        {
            warnedAboutScene = true;
            Debug.LogWarning($"[{nameof(SceneTrigger)}] Не указано имя сцены для перехода.", this);
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            warnedAboutScene = true;
            Debug.LogWarning($"[{nameof(SceneTrigger)}] Сцена «{sceneName}» недоступна. Добавь её в Build Profiles / Build Settings.", this);
        }
    }

    private void OnDrawGizmos()
    {
        Collider trigger = GetComponent<Collider>();
        if (trigger == null) return;

        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.7f);
        Gizmos.DrawWireCube(trigger.bounds.center, trigger.bounds.size);
    }
}

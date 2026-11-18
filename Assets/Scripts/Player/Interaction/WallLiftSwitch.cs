using System.Collections;
using UnityEngine;

/// <summary>
/// Интерактивный переключатель: анимирует нажатие кнопки, играет звук,
/// опускает стену вниз, ждёт и возвращает её.
/// Игрок активирует объект через существующий PlayerInteractor кнопкой E.
/// </summary>
public class WallLiftSwitch : InteractableBase
{
    [Header("Wall movement")]
    [SerializeField] private Transform wall;
    [SerializeField, Min(0f)] private float dropDistance = 3f;
    [SerializeField, Min(0.01f)] private float movementSpeed = 2f;
    [SerializeField, Min(0f)] private float waitAtBottom = 2f;

    [Header("Respawn on press")]
    [Tooltip("Префаб целого объекта, который появится, если текущий был разбит (например, Vase.prefab).")]
    [SerializeField] private GameObject respawnPrefab;
    [Tooltip("Точка появления (пустой объект в сцене). Если пусто — используется место исходного объекта на старте сцены.")]
    [SerializeField] private Transform respawnPoint;
    [Tooltip("Текущий экземпляр в сцене. Не обязательно заполнять — найдётся автоматически.")]
    [SerializeField] private BreakableObject currentInstance;

    [Header("Button animation")]
    [Tooltip("Animator кнопки. Если пусто — берётся с этого же объекта.")]
    [SerializeField] private Animator buttonAnimator;
    [Tooltip("Имя состояния в контроллере кнопки (клип Button_Use_Animation).")]
    [SerializeField] private string pressStateName = "Button_Use_Animation";
    [Tooltip("Дополнительная пауза в нажатом положении, сек.")]
    [SerializeField, Min(0f)] private float pressedHoldTime = 0f;

    [Header("Button sound")]
    [Tooltip("Клипы нажатия. Если массив пуст — используется сгенерированный короткий щелчок.")]
    [SerializeField] private AudioClip[] pressClips;
    [Tooltip("Источник звука. Если не задан — создастся автоматически (3D).")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField, Range(0f, 1f)] private float pressVolume = 0.8f;
    [Tooltip("Диапазон случайной высоты тона, чтобы звуки не повторялись.")]
    [SerializeField] private Vector2 pressPitchRange = new Vector2(0.97f, 1.03f);

    private Vector3 wallStartPosition;
    private bool isMoving;
    private float pressClipLength = -1f;
    private Coroutine buttonRoutine;

    private Vector3 fallbackSpawnPosition;
    private Quaternion fallbackSpawnRotation;

    private static AudioClip generatedClickClip;

    private int PressStateHash => Animator.StringToHash(pressStateName);

    private void Awake()
    {
        if (wall != null)
            wallStartPosition = wall.position;

        if (buttonAnimator == null)
            buttonAnimator = GetComponent<Animator>();
        if (buttonAnimator == null)
            buttonAnimator = GetComponentInChildren<Animator>(true);

        SetupAudio();

        // Запоминаем исходный экземпляр и его позицию — это запасная точка появления.
        if (currentInstance == null)
            currentInstance = FindFirstObjectByType<BreakableObject>();

        if (currentInstance != null)
        {
            fallbackSpawnPosition = currentInstance.transform.position;
            fallbackSpawnRotation = currentInstance.transform.rotation;
        }
    }

    private void Start()
    {
        // Кнопка должна стоять в исходном положении, а не проигрывать клип на старте.
        ResetButtonPose();
    }

    protected override void OnDisable()
    {
        base.OnDisable();

        if (buttonRoutine != null)
        {
            StopCoroutine(buttonRoutine);
            buttonRoutine = null;
        }

        ResetButtonPose();
    }

    public override bool CanInteract(PlayerInteractor interactor)
    {
        return base.CanInteract(interactor) && wall != null && !isMoving;
    }

    public override void Interact(PlayerInteractor interactor)
    {
        if (wall == null || isMoving)
            return;

        PlayButtonFeedback();
        TryRespawnObject();
        StartCoroutine(DropAndRaiseWall());
    }

    /// <summary>Если объект разбит (уничтожен) — создать его заново в настроенной точке.</summary>
    public void TryRespawnObject()
    {
        // Пока исходный объект существует (в сцене или в руках) — дубликат не создаём.
        if (currentInstance != null) return;

        if (respawnPrefab == null)
        {
            Debug.LogWarning($"[{nameof(WallLiftSwitch)}] Не назначен Respawn Prefab — новый объект не будет создан.", this);
            return;
        }

        Vector3 position = respawnPoint != null ? respawnPoint.position : fallbackSpawnPosition;
        Quaternion rotation = respawnPoint != null ? respawnPoint.rotation : fallbackSpawnRotation;

        GameObject instance = Instantiate(respawnPrefab, position, rotation);
        currentInstance = instance.GetComponentInChildren<BreakableObject>();
    }

    private void PlayButtonFeedback()
    {
        PlayPressSound();

        if (buttonAnimator == null || buttonAnimator.runtimeAnimatorController == null)
            return;

        if (buttonRoutine != null)
            StopCoroutine(buttonRoutine);

        buttonRoutine = StartCoroutine(AnimateButtonPress());
    }

    // Клип кнопки проигрывается вниз, при необходимости держится и возвращается назад.
    private IEnumerator AnimateButtonPress()
    {
        int stateHash = PressStateHash;
        float length = Mathf.Max(0.01f, GetPressClipLength());

        buttonAnimator.speed = 1f;
        buttonAnimator.Play(stateHash, 0, 0f);

        yield return new WaitForSeconds(length);

        if (pressedHoldTime > 0f)
            yield return new WaitForSeconds(pressedHoldTime);

        buttonAnimator.speed = -1f;
        buttonAnimator.Play(stateHash, 0, 1f);

        yield return new WaitForSeconds(length);

        ResetButtonPose();
        buttonRoutine = null;
    }

    private void ResetButtonPose()
    {
        if (buttonAnimator == null || buttonAnimator.runtimeAnimatorController == null)
            return;

        buttonAnimator.speed = 0f;
        buttonAnimator.Play(PressStateHash, 0, 0f);
    }

    private float GetPressClipLength()
    {
        if (pressClipLength > 0f)
            return pressClipLength;

        // Значение по умолчанию совпадает с длиной Button_Use_Animation.
        pressClipLength = 0.33f;

        if (buttonAnimator != null && buttonAnimator.runtimeAnimatorController != null)
        {
            foreach (AnimationClip clip in buttonAnimator.runtimeAnimatorController.animationClips)
            {
                if (clip != null && clip.name == pressStateName)
                {
                    pressClipLength = clip.length;
                    break;
                }
            }
        }

        return pressClipLength;
    }

    private void SetupAudio()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 1f; // 3D-звук
            audioSource.minDistance = 1f;
            audioSource.maxDistance = 25f;
        }

        audioSource.playOnAwake = false;
        audioSource.loop = false;
    }

    private void PlayPressSound()
    {
        if (audioSource == null)
            return;

        AudioClip clip = GetRandomPressClip();
        if (clip == null)
            return;

        audioSource.pitch = Random.Range(pressPitchRange.x, pressPitchRange.y);
        audioSource.PlayOneShot(clip, pressVolume);
    }

    private AudioClip GetRandomPressClip()
    {
        if (pressClips != null && pressClips.Length > 0)
        {
            int start = Random.Range(0, pressClips.Length);
            for (int i = 0; i < pressClips.Length; i++)
            {
                AudioClip clip = pressClips[(start + i) % pressClips.Length];
                if (clip != null)
                    return clip;
            }
        }

        return GetGeneratedClickClip();
    }

    // Страховочный звук, если в инспекторе не назначены клипы нажатия.
    private static AudioClip GetGeneratedClickClip()
    {
        if (generatedClickClip != null)
            return generatedClickClip;

        const int sampleRate = 44100;
        const float duration = 0.06f;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float time = i / (float)sampleRate;
            float envelope = Mathf.Exp(-time * 60f);
            float body = Mathf.Sin(2f * Mathf.PI * 170f * time) * 0.5f;
            float snap = Mathf.Sin(2f * Mathf.PI * 900f * time) * 0.2f * Mathf.Exp(-time * 180f);
            samples[i] = (body + snap) * envelope;
        }

        generatedClickClip = AudioClip.Create("GeneratedButtonClick", sampleCount, 1, sampleRate, false);
        generatedClickClip.SetData(samples, 0);
        return generatedClickClip;
    }

    private IEnumerator DropAndRaiseWall()
    {
        isMoving = true;

        Vector3 loweredPosition = wallStartPosition + Vector3.down * dropDistance;
        yield return MoveWallTo(loweredPosition);

        if (waitAtBottom > 0f)
            yield return new WaitForSeconds(waitAtBottom);

        yield return MoveWallTo(wallStartPosition);
        isMoving = false;
    }

    private IEnumerator MoveWallTo(Vector3 targetPosition)
    {
        while ((wall.position - targetPosition).sqrMagnitude > 0.0001f)
        {
            wall.position = Vector3.MoveTowards(
                wall.position,
                targetPosition,
                movementSpeed * Time.deltaTime);
            yield return null;
        }

        wall.position = targetPosition;
    }
}

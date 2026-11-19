using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>Направление, в котором стена/дверь уезжает при нажатии.</summary>
public enum WallMoveDirection
{
    [InspectorName("Вниз")] Down,
    [InspectorName("Вверх")] Up,
    [InspectorName("Влево")] Left,
    [InspectorName("Вправо")] Right,
    [InspectorName("Вперёд")] Forward,
    [InspectorName("Назад")] Backward,
    [InspectorName("Своё направление")] Custom
}

/// <summary>
/// Интерактивный переключатель: анимирует нажатие кнопки, играет звук,
/// двигает стену/дверь в выбранном направлении. Активируется игроком (E) или ударом мяча.
/// При включённом Keep Open дверь остаётся открытой и не возвращается обратно.
/// Также умеет разово создавать предмет в заданной точке.
/// </summary>
public class WallLiftSwitch : InteractableBase
{
    [Header("Wall movement")]
    [SerializeField] private Transform wall;
    [Tooltip("Двигать стену/дверь при нажатии. Выключите, если кнопка должна только создавать предмет.")]
    [SerializeField] private bool moveWallOnPress = true;
    [Tooltip("Направление, в котором уезжает стена/дверь.")]
    [SerializeField] private WallMoveDirection moveDirection = WallMoveDirection.Down;
    [Tooltip("Своё направление. Используется только при Move Direction = Custom.")]
    [SerializeField] private Vector3 customDirection = Vector3.down;
    [Tooltip("Считать направление относительно поворота стены, а не мировых осей.")]
    [SerializeField] private bool directionIsLocal = false;
    [Tooltip("Расстояние, на которое стена/дверь уезжает в выбранном направлении.")]
    [FormerlySerializedAs("dropDistance")]
    [SerializeField, Min(0f)] private float moveDistance = 3f;
    [SerializeField, Min(0.01f)] private float movementSpeed = 2f;
    [Tooltip("Пауза в открытом положении перед возвратом. Не используется, если включено «Keep Open».")]
    [SerializeField, Min(0f)] private float waitAtBottom = 2f;

    [Header("Door behaviour")]
    [Tooltip("Если включено — после нажатия дверь остаётся открытой и не возвращается обратно.")]
    [SerializeField] private bool keepOpen = true;

    [Header("Ball activation (удар мячом)")]
    [Tooltip("Разрешить активацию кнопки брошенным предметом (мячом/ящиком) при физическом ударе.")]
    [SerializeField] private bool allowBallActivation = true;
    [Tooltip("Минимальная скорость соударения (м/с), при которой срабатывает кнопка.")]
    [SerializeField, Min(0f)] private float minBallImpactSpeed = 0.2f;
    [Tooltip("Слои, удары по которым активируют кнопку.")]
    [SerializeField] private LayerMask ballActivationMask = ~0;
    [Tooltip("Реагировать только на предметы с компонентом PickupObject (мяч, ящик), игнорируя осколки и прочий мусор.")]
    [SerializeField] private bool requirePickupObject = true;

    [Header("Spawn item on press")]
    [Tooltip("Включить: при нажатии кнопка создаст предмет в указанной точке.")]
    [SerializeField] private bool spawnItemOnPress = false;
    [Tooltip("Префаб предмета, который создаётся при нажатии.")]
    [SerializeField] private GameObject itemPrefab;
    [Tooltip("Точка появления предмета. Если пусто — используется Respawn Point, затем позиция кнопки + смещение.")]
    [SerializeField] private Transform itemSpawnPoint;
    [Tooltip("Смещение от кнопки, если точка появления не назначена.")]
    [SerializeField] private Vector3 itemSpawnOffset = Vector3.zero;
    [Tooltip("Создавать предмет только один раз за игру.")]
    [SerializeField] private bool spawnItemOnlyOnce = true;

    /// <summary>Дверь остаётся открытой после нажатия (можно менять в рантайме).</summary>
    public bool KeepOpen
    {
        get => keepOpen;
        set => keepOpen = value;
    }

    /// <summary>Разрешена ли активация кнопки ударом мяча/предмета.</summary>
    public bool AllowBallActivation
    {
        get => allowBallActivation;
        set => allowBallActivation = value;
    }

    /// <summary>Двигать стену/дверь при нажатии (можно менять в рантайме).</summary>
    public bool MoveWallOnPress
    {
        get => moveWallOnPress;
        set => moveWallOnPress = value;
    }

    /// <summary>Направление движения стены/двери.</summary>
    public WallMoveDirection MoveDirection
    {
        get => moveDirection;
        set => moveDirection = value;
    }

    /// <summary>Включена ли разовая генерация предмета при нажатии.</summary>
    public bool SpawnItemOnPress
    {
        get => spawnItemOnPress;
        set => spawnItemOnPress = value;
    }

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

    [Header("Wall movement sound")]
    [Tooltip("Включить звук движения стены/двери (скрежет и гул во время движения).")]
    [SerializeField] private bool playWallMoveSound = true;
    [Tooltip("Клипы скрежета/движения стены. Если массив пуст — используется сгенерированный шум движения.")]
    [SerializeField] private AudioClip[] wallMoveClips;
    [Tooltip("Источник звука движения. Должен быть отдельным (не тот, что играет щелчок кнопки). Если пусто — создастся автоматически.")]
    [SerializeField] private AudioSource wallMoveAudioSource;
    [SerializeField, Range(0f, 1f)] private float wallMoveVolume = 0.8f;
    [Tooltip("Диапазон случайной высоты тона, чтобы звук не повторялся от нажатия к нажатию.")]
    [SerializeField] private Vector2 wallMovePitchRange = new Vector2(0.92f, 1.08f);
    [Tooltip("Плавное появление звука в начале движения, сек.")]
    [SerializeField, Min(0f)] private float wallMoveFadeIn = 0.12f;
    [Tooltip("Плавное затухание звука в конце движения, сек.")]
    [SerializeField, Min(0f)] private float wallMoveFadeOut = 0.18f;
    [Tooltip("Небольшая пауза без движения (сек), после которой звук начинает затухать. Защищает от «дребезга» на стыках.")]
    [SerializeField, Min(0f)] private float wallMoveSoundStopDelay = 0.08f;
    [Tooltip("Писать в Console, когда запускается звук движения стены (для отладки).")]
    [SerializeField] private bool wallMoveDebugLog = false;

    private const string WallSoundChildName = "WallMoveSound";

    private Vector3 wallStartPosition;
    private bool isMoving;
    private bool hasSpawnedItem;
    private float pressClipLength = -1f;
    private Coroutine buttonRoutine;

    // Состояние звука движения стены (звук идёт, пока стена реально смещается).
    private Vector3 lastWallPosition;
    private bool hasLastWallPosition;
    private float wallSoundTargetVolume;
    private float wallStillTimer;
    private float nextWallSoundPlayAttempt;

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
        SetupWallAudio();

        // Запоминаем исходный экземпляр и его позицию — это запасная точка появления.
        if (currentInstance == null)
            currentInstance = FindFirstObjectByType<BreakableObject>();

        if (currentInstance != null)
        {
            fallbackSpawnPosition = currentInstance.transform.position;
            fallbackSpawnRotation = currentInstance.transform.rotation;
        }

        if (spawnItemOnPress && itemPrefab == null)
        {
            Debug.LogWarning(
                $"[{nameof(WallLiftSwitch)}] «Spawn Item On Press» включён, но Item Prefab не назначен.", this);
        }
    }

    private void OnEnable()
    {
        // После повторного включения объекта заново запоминаем позицию стены,
        // чтобы не «дёрнуть» звук из-за накопленного смещения.
        if (wall != null)
        {
            lastWallPosition = wall.position;
            hasLastWallPosition = true;
        }
    }

    private void Start()
    {
        // Кнопка должна стоять в исходном положении, а не проигрывать клип на старте.
        ResetButtonPose();

        // Запоминаем стартовую позицию стены для отслеживания движения.
        if (wall != null)
        {
            lastWallPosition = wall.position;
            hasLastWallPosition = true;
        }
    }

    private void Update()
    {
        UpdateWallMoveSound();
    }

    protected override void OnDisable()
    {
        base.OnDisable();

        if (buttonRoutine != null)
        {
            StopCoroutine(buttonRoutine);
            buttonRoutine = null;
        }

        isMoving = false;
        StopWallSoundImmediate();
        ResetButtonPose();
    }

    public override bool CanInteract(PlayerInteractor interactor)
    {
        return base.CanInteract(interactor) && CanActivate();
    }

    /// <summary>Можно ли активировать кнопку прямо сейчас.</summary>
    private bool CanActivate()
    {
        return CanMoveWall() || CanSpawnItem();
    }

    /// <summary>Можно ли сейчас двигать стену/дверь этой кнопкой.</summary>
    private bool CanMoveWall()
    {
        // Дверь уже открыта/движется (в т.ч. другой кнопкой) — повторно не двигаем.
        return moveWallOnPress && wall != null && !isMoving && IsWallAtStart();
    }

    /// <summary>Можно ли сейчас создать предмет.</summary>
    private bool CanSpawnItem()
    {
        return spawnItemOnPress && itemPrefab != null && (!spawnItemOnlyOnce || !hasSpawnedItem);
    }

    private bool IsWallAtStart()
    {
        return wall != null && (wall.position - wallStartPosition).sqrMagnitude < 0.0001f;
    }

    public override void Interact(PlayerInteractor interactor)
    {
        Activate();
    }

    /// <summary>Активация кнопки: игроком (E) или ударом мяча.</summary>
    private void Activate()
    {
        bool canMoveWall = CanMoveWall();
        bool canSpawnItem = CanSpawnItem();

        if (!canMoveWall && !canSpawnItem)
            return;

        PlayButtonFeedback();

        // Старое поведение с вазой: вызываем только если префаб для респавна задан,
        // чтобы кнопки-спавнеры без этой настройки не писали предупреждения.
        if (respawnPrefab != null)
            TryRespawnObject();

        if (canSpawnItem)
            SpawnItem();

        if (canMoveWall)
            StartCoroutine(MoveWallRoutine());
    }

    /// <summary>Создать предмет в заданной точке. Возвращает созданный объект (или null).</summary>
    public GameObject SpawnItem()
    {
        if (itemPrefab == null)
        {
            Debug.LogWarning($"[{nameof(WallLiftSwitch)}] Не назначен Item Prefab — предмет не создан.", this);
            return null;
        }

        if (spawnItemOnlyOnce && hasSpawnedItem)
            return null;

        Transform point = itemSpawnPoint != null ? itemSpawnPoint : respawnPoint;
        Vector3 position = point != null ? point.position : transform.position + itemSpawnOffset;
        Quaternion rotation = point != null ? point.rotation : transform.rotation;

        GameObject instance = Instantiate(itemPrefab, position, rotation);
        hasSpawnedItem = true;
        return instance;
    }

    /// <summary>Сбросить флаг «предмет уже создан», чтобы кнопка могла создать его снова.</summary>
    public void ResetItemSpawn()
    {
        hasSpawnedItem = false;
    }

    /// <summary>Вернуть стену/дверь в исходное положение.</summary>
    public void ResetWall()
    {
        if (wall == null || isMoving)
            return;

        StartCoroutine(ResetWallRoutine());
    }

    private IEnumerator ResetWallRoutine()
    {
        isMoving = true;
        yield return MoveWallTo(wallStartPosition);
        isMoving = false;
    }

    // Активация мячом: шар с Rigidbody бьёт по коллайдеру кнопки.
    private void OnCollisionEnter(Collision collision)
    {
        if (!allowBallActivation || collision.collider == null)
            return;

        if (!CanActivate())
            return;

        int layerBit = 1 << collision.collider.gameObject.layer;
        if ((ballActivationMask.value & layerBit) == 0)
            return;

        // Rigidbody ударившего объекта (у самой кнопки Rigidbody нет).
        Rigidbody otherBody = collision.collider.attachedRigidbody;
        if (otherBody == null || otherBody.isKinematic)
            return;

        if (requirePickupObject && collision.collider.GetComponentInParent<PickupObject>() == null)
            return;

        float impactSpeed = collision.relativeVelocity.magnitude;
        if (impactSpeed < minBallImpactSpeed)
            return;

        Activate();
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

        // Возврат кнопки. Отрицательный Animator.speed в Unity запрещён,
        // поэтому тот же клип проигрывается вручную от конца к началу.
        buttonAnimator.speed = 0f;

        float normalizedTime = 1f;
        while (normalizedTime > 0f)
        {
            normalizedTime -= Time.deltaTime / length;
            buttonAnimator.Play(stateHash, 0, Mathf.Clamp01(normalizedTime));
            yield return null;
        }

        ResetButtonPose();
        buttonRoutine = null;
    }

    private void ResetButtonPose()
    {
        if (buttonAnimator == null || buttonAnimator.runtimeAnimatorController == null)
            return;

        // При выключении объекта Animator (он может быть на дочернем объекте) уже неактивен —
        // Play() выдал бы предупреждение «Game object with animator is inactive».
        if (!buttonAnimator.isActiveAndEnabled || !buttonAnimator.gameObject.activeInHierarchy)
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

    // ================= WALL MOVEMENT SOUND =================

    /// <summary>
    /// Готовит отдельный источник звука движения. Он создаётся на объекте кнопки
    /// (её объект всегда активен), поэтому Play() не может «не сыграть».
    /// Если в инспекторе назначен свой отдельный источник (например, на стене) — используется он.
    /// </summary>
    private void SetupWallAudio()
    {
        EnsureWallAudio();
    }

    private void EnsureWallAudio()
    {
        if (!playWallMoveSound || wall == null)
            return;

        // Уже есть рабочий отдельный источник — ничего не создаём.
        if (IsWallAudioUsable())
            return;

        // Если в инспекторе назначен отдельный активный источник (не тот, что играет щелчок) — берём его.
        if (wallMoveAudioSource != null &&
            wallMoveAudioSource != audioSource &&
            wallMoveAudioSource.enabled &&
            wallMoveAudioSource.gameObject.activeInHierarchy)
        {
            wallMoveAudioSource.playOnAwake = false;
            wallMoveAudioSource.loop = true;
            return;
        }

        // Иначе создаём свой источник на кнопке.
        Transform host = transform;
        Transform existing = host.Find(WallSoundChildName);
        AudioSource source = existing != null ? existing.GetComponent<AudioSource>() : null;

        if (source == null)
        {
            GameObject soundObject = new GameObject(WallSoundChildName);
            soundObject.transform.SetParent(host, false);
            source = soundObject.AddComponent<AudioSource>();
            source.spatialBlend = 1f; // 3D-звук
            source.minDistance = 2f;
            source.maxDistance = 30f;
            source.dopplerLevel = 0f; // стена едет — не «воем» от эффекта Доплера
        }

        source.enabled = true;
        source.playOnAwake = false;
        source.loop = true;
        source.volume = 0f;

        wallMoveAudioSource = source;

        // Если своих клипов нет — заранее готовим сгенерированный звук,
        // чтобы он не создавался с задержкой в момент нажатия кнопки.
        if (wallMoveClips == null || wallMoveClips.Length == 0)
            _ = MoveSound.GeneratedMoveClip;

        if (wallMoveDebugLog)
            Debug.Log($"[{nameof(WallLiftSwitch)}] Источник звука стены: {source.gameObject.name} (активен: {source.gameObject.activeInHierarchy})", this);
    }

    /// <summary>Источник существует, включён, его объект активен и это не источник щелчка кнопки.</summary>
    private bool IsWallAudioUsable()
    {
        return wallMoveAudioSource != null &&
               wallMoveAudioSource != audioSource &&
               wallMoveAudioSource.enabled &&
               wallMoveAudioSource.gameObject.activeInHierarchy;
    }

    // Звук идёт всё время, пока стена реально смещается — в любую сторону
    // (и вниз, и обратно вверх), независимо от того, кто её двигает.
    private void UpdateWallMoveSound()
    {
        if (!playWallMoveSound || wall == null || wallMoveVolume <= 0.0001f)
        {
            StopWallSoundImmediate();
            return;
        }

        // Источник мог ещё не создаться, потеряться или оказаться на выключенном объекте.
        EnsureWallAudio();
        if (!IsWallAudioUsable())
            return;

        if (!hasLastWallPosition)
        {
            lastWallPosition = wall.position;
            hasLastWallPosition = true;
            return;
        }

        Vector3 delta = wall.position - lastWallPosition;
        lastWallPosition = wall.position;

        if (delta.sqrMagnitude > 0.00000001f)
        {
            // Стена едет — звук должен звучать.
            wallStillTimer = 0f;
            wallSoundTargetVolume = wallMoveVolume;

            // Если источник замолчал (остановился, клип кончился, объект выключали) —
            // запускаем заново. Небольшая пауза между попытками, чтобы не спамить Play().
            if (!IsWallSoundPlaying() && Time.time >= nextWallSoundPlayAttempt)
            {
                nextWallSoundPlayAttempt = Time.time + 0.25f;
                PlayWallSound();
            }
        }
        else
        {
            // Стена стоит. Небольшая пауза защищает от дребезга на стыках сегментов.
            wallStillTimer += Time.deltaTime;

            if (wallSoundTargetVolume > 0f && wallStillTimer >= wallMoveSoundStopDelay)
                wallSoundTargetVolume = 0f;
        }

        // Плавно подводим громкость к целевой.
        float fadeTime = wallSoundTargetVolume > wallMoveAudioSource.volume ? wallMoveFadeIn : wallMoveFadeOut;
        float step = fadeTime > 0.0001f
            ? (wallMoveVolume / fadeTime) * Time.deltaTime
            : float.MaxValue;

        wallMoveAudioSource.volume = Mathf.MoveTowards(
            wallMoveAudioSource.volume,
            wallSoundTargetVolume,
            step);

        // Затухли до нуля — останавливаем источник.
        if (wallSoundTargetVolume <= 0f && wallMoveAudioSource.volume <= 0.0001f && wallMoveAudioSource.isPlaying)
            wallMoveAudioSource.Stop();
    }

    private bool IsWallSoundPlaying()
    {
        return wallMoveAudioSource != null &&
               wallMoveAudioSource.isPlaying &&
               wallMoveAudioSource.clip != null;
    }

    private void PlayWallSound()
    {
        AudioClip clip = MoveSound.GetRandomClip(wallMoveClips);
        if (clip == null)
            return;

        AudioSource source = wallMoveAudioSource;

        // Начинаем «с чистого листа»: Stop() сбрасывает позицию и состояние источника.
        source.Stop();
        source.clip = clip;
        source.loop = true;
        source.pitch = Random.Range(wallMovePitchRange.x, wallMovePitchRange.y);
        source.volume = 0f;
        source.Play();

        if (wallMoveDebugLog)
            Debug.Log($"[{nameof(WallLiftSwitch)}] Звук движения стены: {clip.name} (источник: {source.gameObject.name})", this);
    }

    private void StopWallSoundImmediate()
    {
        wallSoundTargetVolume = 0f;
        wallStillTimer = 0f;
        hasLastWallPosition = false;
        nextWallSoundPlayAttempt = 0f;

        if (wallMoveAudioSource == null)
            return;

        if (wallMoveAudioSource.isPlaying)
            wallMoveAudioSource.Stop();

        wallMoveAudioSource.volume = 0f;
    }

    private IEnumerator MoveWallRoutine()
    {
        isMoving = true;

        Vector3 openPosition = wallStartPosition + GetMoveOffset();
        yield return MoveWallTo(openPosition);

        // Дверь должна остаться открытой — назад не возвращаем.
        if (keepOpen)
        {
            isMoving = false;
            yield break;
        }

        if (waitAtBottom > 0f)
            yield return new WaitForSeconds(waitAtBottom);

        yield return MoveWallTo(wallStartPosition);
        isMoving = false;
    }

    /// <summary>Вектор смещения стены/двери с учётом выбранного направления и расстояния.</summary>
    private Vector3 GetMoveOffset()
    {
        Vector3 direction;
        switch (moveDirection)
        {
            case WallMoveDirection.Up: direction = Vector3.up; break;
            case WallMoveDirection.Left: direction = Vector3.left; break;
            case WallMoveDirection.Right: direction = Vector3.right; break;
            case WallMoveDirection.Forward: direction = Vector3.forward; break;
            case WallMoveDirection.Backward: direction = Vector3.back; break;
            case WallMoveDirection.Custom: direction = customDirection; break;
            default: direction = Vector3.down; break;
        }

        direction = direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector3.down;

        // Направление относительно поворота стены (например, если стена повёрнута).
        if (directionIsLocal && wall != null)
            direction = wall.TransformDirection(direction);

        return direction * moveDistance;
    }

    // Подсказка в Scene view: куда уедет стена и где появится предмет.
    private void OnDrawGizmosSelected()
    {
        if (moveWallOnPress && wall != null)
        {
            Vector3 start = wall.position;
            Vector3 end = start + GetMoveOffset();

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(start, end);
            Gizmos.DrawWireSphere(end, 0.12f);
        }

        if (spawnItemOnPress)
        {
            Transform point = itemSpawnPoint != null ? itemSpawnPoint : respawnPoint;
            Vector3 position = point != null ? point.position : transform.position + itemSpawnOffset;

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(position, 0.15f);
            Gizmos.DrawLine(position, position + Vector3.up * 0.5f);
        }
    }

    private IEnumerator MoveWallTo(Vector3 targetPosition)
    {
        // Стена уже стоит на месте — двигать нечего.
        if ((wall.position - targetPosition).sqrMagnitude <= 0.0001f)
        {
            wall.position = targetPosition;
            yield break;
        }

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

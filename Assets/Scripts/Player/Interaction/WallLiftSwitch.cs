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

    private Vector3 wallStartPosition;
    private bool isMoving;
    private bool hasSpawnedItem;
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

        if (spawnItemOnPress && itemPrefab == null)
        {
            Debug.LogWarning(
                $"[{nameof(WallLiftSwitch)}] «Spawn Item On Press» включён, но Item Prefab не назначен.", this);
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

        isMoving = false;
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

        // При выключении объекта Animator уже неактивен — Play() выдал бы предупреждение.
        if (!buttonAnimator.isActiveAndEnabled)
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

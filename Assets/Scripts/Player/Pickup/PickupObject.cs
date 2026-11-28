using UnityEngine;

/// <summary>
/// Физический предмет: лёгкие предметы (до Max Carry Mass) носятся в руках,
/// тяжёлые — тащатся по земле с ограничением скорости и не могут быть брошены.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PickupObject : InteractableBase, IPhysicsHoldable
{
    private Rigidbody rb;

    [Header("Hold Settings")]
    [Tooltip("Жёсткость «пружины» удержания (Н/м). Чем больше — тем сильнее предмет тянется к руке.")]
    public float followForce = 600f;
    [Tooltip("Максимальная сила удержания (Н). Ограничивает подъём тяжёлых предметов.")]
    public float maxForce = 1500f;
    [Tooltip("Демпфирование скорости (1/с). Гасит раскачивание предмета.")]
    public float damping = 15f;
    [Tooltip("Скорость доворота предмета в руках за игроком (град/с).")]
    [SerializeField, Min(0f)] private float holdRotationSpeed = 720f;

    [Header("Weight / Drag (масса и перетаскивание)")]
    [Tooltip("До этой массы предмет можно поднимать и носить в руках. Тяжелее — только тащить по земле.")]
    [SerializeField, Min(0f)] private float maxCarryMass = 20f;
    [Tooltip("Максимальная скорость лёгкого предмета в руках (м/с). Обычно равна скорости бега игрока.")]
    [SerializeField, Min(0.01f)] private float maxCarrySpeed = 8f;
    [Tooltip("Минимальная скорость перетаскивания очень тяжёлого предмета (м/с).")]
    [SerializeField, Min(0f)] private float minDragSpeed = 0.15f;
    [Tooltip("Насколько резко падает скорость с ростом массы: 1 — линейно, больше — тяжелее чувствуется вес.")]
    [SerializeField, Min(0.1f)] private float dragSpeedFalloff = 1.5f;
    [Tooltip("Насколько быстро тяжёлый предмет догоняет точку перетаскивания (1/с).")]
    [SerializeField, Min(0.01f)] private float dragFollowSpeed = 3f;
    [Tooltip("Максимальный разгон/торможение тяжёлого предмета (м/с²).")]
    [SerializeField, Min(0.01f)] private float dragAcceleration = 10f;
    [Tooltip("Торможение вращения тяжёлого предмета (рад/с²).")]
    [SerializeField, Min(0f)] private float dragAngularDeceleration = 12f;
    [Tooltip("Минимальный множитель скорости игрока при перетаскивании очень тяжёлого предмета.")]
    [SerializeField, Range(0.01f, 1f)] private float minPlayerSpeedMultiplier = 0.05f;

    [Header("Collision Settings")]
    public float collisionCheckRadius = 0.2f;
    public LayerMask collisionMask = ~0;

    [Header("Impact Sound (падение/удар)")]
    [Tooltip("Звуки удара/падения. Каждый раз играет случайный клип.")]
    [SerializeField] private AudioClip[] impactClips;
    [Tooltip("Источник звука. Если не задан — создастся автоматически (3D).")]
    [SerializeField] private AudioSource audioSource;
    [Tooltip("Минимальная скорость удара (м/с), ниже которой звук не играет.")]
    [SerializeField] private float minImpactSpeed = 1f;
    [Tooltip("Скорость, при которой громкость удара достигает максимума.")]
    [SerializeField] private float maxImpactSpeed = 8f;
    [Tooltip("Базовая громкость удара.")]
    [SerializeField, Range(0f, 1f)] private float impactVolume = 0.8f;
    [Tooltip("Диапазон случайной высоты тона, чтобы звуки не повторялись.")]
    [SerializeField] private Vector2 impactPitchRange = new Vector2(0.9f, 1.1f);
    [Tooltip("Пауза между звуками, чтобы несколько коллайдеров не спамили.")]
    [SerializeField] private float impactCooldown = 0.1f;

    public bool IsHeld { get; private set; }

    /// <summary>Масса предмета в килограммах.</summary>
    public float Mass => rb != null ? rb.mass : 0f;

    /// <summary>Предмет слишком тяжёлый для переноски — его можно только тащить.</summary>
    public bool IsHeavy => rb != null && rb.mass > maxCarryMass;

    /// <summary>Насколько сильно предмет замедляет игрока при перетаскивании.</summary>
    public float PlayerSpeedMultiplier =>
        IsHeavy ? Mathf.Clamp(GetSpeedLimit() / maxCarrySpeed, minPlayerSpeedMultiplier, 1f) : 1f;

    private Vector3 targetPosition;
    private Quaternion targetRotation = Quaternion.identity;

    // Скорость точки удержания (нужна, чтобы предмет не отставал при ходьбе).
    private Vector3 lastTargetPosition;
    private Vector3 targetVelocity;
    private bool hasLastTarget;

    private float nextImpactTime;

    // Ограничение скорости броска, чтобы предмет не «выстреливал» сквозь стены.
    private const float MaxThrowSpeed = 30f;

    public override string InteractionPrompt => IsHeavy ? "Тащить" : "Взять";
    public override bool CanInteract(PlayerInteractor interactor) => isActiveAndEnabled && !IsHeld;

    public override void Interact(PlayerInteractor interactor)
        => interactor.PhysicsHold.BeginHold(this);

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        SetupAudio();
    }

    void OnValidate()
    {
        followForce = Mathf.Max(0.01f, followForce);
        maxForce = Mathf.Max(0.01f, maxForce);
        damping = Mathf.Max(0f, damping);

        maxCarryMass = Mathf.Max(0f, maxCarryMass);
        maxCarrySpeed = Mathf.Max(0.01f, maxCarrySpeed);
        minDragSpeed = Mathf.Max(0f, minDragSpeed);
        dragSpeedFalloff = Mathf.Max(0.1f, dragSpeedFalloff);
        dragFollowSpeed = Mathf.Max(0.01f, dragFollowSpeed);
        dragAcceleration = Mathf.Max(0.01f, dragAcceleration);
        dragAngularDeceleration = Mathf.Max(0f, dragAngularDeceleration);
        minPlayerSpeedMultiplier = Mathf.Clamp(minPlayerSpeedMultiplier, 0.01f, 1f);
    }

    void SetupAudio()
    {
        if (!audioSource) audioSource = GetComponent<AudioSource>();
        if (!audioSource)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 1f; // 3D-звук
        }

        audioSource.playOnAwake = false;
        audioSource.loop = false;
    }

    public void BeginHold(Vector3 holdPosition, Quaternion holdRotation)
    {
        if (IsHeld) return;
        IsHeld = true;
        targetPosition = holdPosition;
        targetRotation = holdRotation;
        hasLastTarget = false;
        targetVelocity = Vector3.zero;

        if (IsHeavy)
        {
            // Тяжёлое не поднимаем: гравитация остаётся, предмет тащится по земле.
            rb.useGravity = true;
            rb.linearDamping = 1f;
            rb.angularDamping = 2f;
        }
        else
        {
            // Лёгкое носим в руках — компенсируем вес.
            rb.useGravity = false;
            rb.linearDamping = 8f;
            // Небольшое угловое демпфирование: предмет не должен «сопротивляться»
            // повороту правой кнопкой мыши, но и не крутится сам по себе.
            rb.angularDamping = 2f;
        }
    }

    public void SetHoldTarget(Vector3 holdPosition, Quaternion holdRotation)
    {
        if (!IsHeld) return;
        targetPosition = holdPosition;
        targetRotation = holdRotation;
    }

    public void EndHold()
    {
        if (!IsHeld) return;
        IsHeld = false;

        rb.useGravity = true;
        rb.linearDamping = 0f;
        rb.angularDamping = 0.05f;
    }

    /// <summary>Бросок: отпускаем предмет и задаём ему скорость (м/с).</summary>
    public void Throw(Vector3 velocity)
    {
        if (!IsHeld) return;

        EndHold();

        // Страховка от «выстрела» предметом, если скорость пришла слишком большая.
        rb.linearVelocity = Vector3.ClampMagnitude(velocity, MaxThrowSpeed);
    }

    void OnCollisionEnter(Collision collision) => HandleImpact(collision);
    void OnCollisionStay(Collision collision) => HandleImpact(collision);

    // Ловит и первое падение (Enter), и повторный удар «заваливания на бок» (Stay),
    // когда объект уже касается земли и Enter больше не срабатывает.
    void HandleImpact(Collision collision)
    {
        if (IsHeld) return; // в руках не считаем ударом падения

        // Импульс на единицу массы ≈ изменение скорости (м/с).
        // Работает и при первом ударе, и при боковом «докате».
        float impulseSpeed = collision.impulse.magnitude / Mathf.Max(rb.mass, 0.0001f);
        float relativeSpeed = collision.relativeVelocity.magnitude;

        PlayImpactSound(Mathf.Max(impulseSpeed, relativeSpeed));
    }

    /// <summary>Проиграть звук удара с громкостью, зависящей от силы удара.</summary>
    public void PlayImpactSound(float impactSpeed)
    {
        if (Time.time < nextImpactTime) return;
        if (impactSpeed < minImpactSpeed) return;
        if (audioSource == null || !audioSource.isActiveAndEnabled || impactClips == null || impactClips.Length == 0) return;

        AudioClip clip = impactClips[Random.Range(0, impactClips.Length)];
        if (clip == null) return;

        nextImpactTime = Time.time + impactCooldown;

        float t = Mathf.InverseLerp(minImpactSpeed, maxImpactSpeed, impactSpeed);
        float volume = impactVolume * Mathf.Lerp(0.35f, 1f, t);

        audioSource.pitch = Random.Range(impactPitchRange.x, impactPitchRange.y);
        audioSource.PlayOneShot(clip, volume);
    }

    void FixedUpdate()
    {
        if (!IsHeld) return;

        if (IsHeavy)
            ApplyHeavyDrag(targetPosition);
        else
            ApplyCarry(targetPosition);
    }

    /// <summary>Лёгкий предмет: носим в руках. Сила пружины зависит от массы.</summary>
    private void ApplyCarry(Vector3 holdPosition)
    {
        // Скорость точки удержания. Без неё пружина «догоняет» уезжающую руку и
        // предмет отстаёт при ходьбе/беге (в билде это выглядит так, будто точка
        // перетаскивания уезжает от предмета).
        if (hasLastTarget && Time.fixedDeltaTime > 0.00001f)
            targetVelocity = (holdPosition - lastTargetPosition) / Time.fixedDeltaTime;
        lastTargetPosition = holdPosition;
        hasLastTarget = true;

        // Слой 3 — Player (см. TagManager): капсулу игрока не считаем препятствием,
        // иначе предмет замирает, когда точка удержания рядом с игроком.
        int mask = collisionMask.value & ~(1 << 3);
        Collider[] overlaps = Physics.OverlapSphere(holdPosition, collisionCheckRadius, mask, QueryTriggerInteraction.Ignore);
        bool blocked = false;
        foreach (Collider col in overlaps)
        {
            if (col != null && col.attachedRigidbody != rb)
            {
                blocked = true;
                break;
            }
        }

        // Точка занята геометрией — не замораживаем предмет (раньше он оставался
        // на месте и «отставал»), а ведём его к ближайшей свободной позиции.
        Vector3 finalTarget = holdPosition;
        if (blocked)
        {
            Vector3 toHold = holdPosition - rb.position;
            if (toHold.sqrMagnitude > 0.000001f)
            {
                float step = Mathf.Max(0.05f, toHold.magnitude - collisionCheckRadius * 2f);
                finalTarget = rb.position + toHold.normalized * step;
            }
        }

        Vector3 toTarget = finalTarget - rb.position;

        // Демпфирование считаем относительно скорости точки удержания: предмет
        // не сопротивляется движению игрока, а держится у руки.
        Vector3 relativeVelocity = rb.linearVelocity - targetVelocity;

        // followForce — жёсткость в Н/м, поэтому тяжёлый предмет разгоняется медленнее.
        // damping * mass превращает демпфирование в силу (Н), сохраняя прежнее ощущение для лёгких предметов.
        Vector3 force = toTarget * followForce - relativeVelocity * (damping * rb.mass);
        force = Vector3.ClampMagnitude(force, maxForce);
        rb.AddForce(force, ForceMode.Force);

        ApplyHoldRotation();

        // Страховка от «выстрела»: ограничиваем скорость относительно руки.
        Vector3 rel = rb.linearVelocity - targetVelocity;
        if (rel.magnitude > maxCarrySpeed)
            rb.linearVelocity = targetVelocity + rel.normalized * maxCarrySpeed;
    }

    /// <summary>Тяжёлый предмет: тащим по земле с ограничением скорости, поднять нельзя.</summary>
    private void ApplyHeavyDrag(Vector3 holdPosition)
    {
        // Цель — только по горизонтали: Y всегда равен текущей высоте предмета.
        Vector3 dragTarget = new Vector3(holdPosition.x, rb.position.y, holdPosition.z);
        Vector3 toTarget = dragTarget - rb.position;
        toTarget.y = 0f;

        float speedLimit = GetSpeedLimit();

        Vector3 desiredVelocity = Vector3.ClampMagnitude(toTarget * dragFollowSpeed, speedLimit);
        Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        Vector3 deltaVelocity = desiredVelocity - horizontalVelocity;

        // Плавный разгон/торможение с ограничением ускорения.
        Vector3 acceleration = Vector3.ClampMagnitude(deltaVelocity / Time.fixedDeltaTime, dragAcceleration);
        rb.AddForce(acceleration, ForceMode.Acceleration);

        // Ограничиваем горизонтальную скорость: тяжёлый предмет нельзя «кинуть».
        Vector3 velocity = rb.linearVelocity;
        Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
        if (horizontal.magnitude > speedLimit)
        {
            horizontal = horizontal.normalized * speedLimit;
            rb.linearVelocity = new Vector3(horizontal.x, velocity.y, horizontal.z);
        }

        // Тяжёлая плита не должна крутиться как картонка.
        rb.angularVelocity = Vector3.MoveTowards(
            rb.angularVelocity,
            Vector3.zero,
            dragAngularDeceleration * Time.fixedDeltaTime);
    }

    private void ApplyHoldRotation()
    {
        if (rb.isKinematic) return;

        // Тяжёлый предмет тащится по земле — доворачиваем только вокруг вертикали,
        // чтобы он не «вставал на ребро» при повороте игрока.
        Quaternion target = IsHeavy
            ? Quaternion.Euler(0f, targetRotation.eulerAngles.y, 0f)
            : targetRotation;

        float angle = Quaternion.Angle(rb.rotation, target);
        if (angle < 0.2f)
        {
            if (rb.angularVelocity.sqrMagnitude > 0.0004f)
                rb.angularVelocity *= 0.5f;
            return;
        }

        // Доворачиваем кинематически. Раньше здесь был только момент (AddTorque):
        // его гасили контакты с полом и стенами, и предмет оставался в прежней
        // мировой ориентации, хотя рука уже повернулась за камерой.
        float step = Mathf.Max(holdRotationSpeed * Time.fixedDeltaTime, 0.5f);
        rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, target, step));
        rb.angularVelocity = Vector3.zero;
    }

    /// <summary>Предел скорости удержания: чем тяжелее предмет, тем медленнее он двигается.</summary>
    public float GetSpeedLimit()
    {
        float mass = Mathf.Max(Mass, 0.0001f);
        if (mass <= maxCarryMass)
            return maxCarrySpeed;

        float scaled = maxCarrySpeed * Mathf.Pow(maxCarryMass / mass, dragSpeedFalloff);
        return Mathf.Max(minDragSpeed, scaled);
    }
}

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
        // Не толкаем предмет сквозь стены: если точка удержания занята — держим на месте.
        Collider[] overlaps = Physics.OverlapSphere(holdPosition, collisionCheckRadius, collisionMask);
        bool blocked = false;
        foreach (Collider col in overlaps)
        {
            if (col.attachedRigidbody != rb)
            {
                blocked = true;
                break;
            }
        }

        Vector3 finalTarget = blocked ? rb.position : holdPosition;
        Vector3 toTarget = finalTarget - rb.position;

        // followForce — жёсткость в Н/м, поэтому тяжёлый предмет разгоняется медленнее.
        // damping * mass превращает демпфирование в силу (Н), сохраняя прежнее ощущение для лёгких предметов.
        Vector3 force = toTarget * followForce - rb.linearVelocity * (damping * rb.mass);
        force = Vector3.ClampMagnitude(force, maxForce);
        rb.AddForce(force, ForceMode.Force);

        ApplyHoldRotation();

        // Страховка: нельзя «выстрелить» предметом, резко дёрнув мышью.
        Vector3 velocity = rb.linearVelocity;
        if (velocity.magnitude > maxCarrySpeed)
            rb.linearVelocity = velocity.normalized * maxCarrySpeed;
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

        // Крутим только если предмет реально отклонился от цели — иначе не трясём его.
        Quaternion deltaRot = targetRotation * Quaternion.Inverse(rb.rotation);
        deltaRot.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;

        // При нулевом угле ось может быть нулевой — тогда крутить нечего.
        if (axis.sqrMagnitude < 0.000001f) return;
        if (Mathf.Abs(angle) < 0.2f && rb.angularVelocity.sqrMagnitude < 0.0004f) return;

        // Чем сильнее доворот, тем быстрее предмет догоняет цель, чтобы
        // вращение правой кнопкой мыши ощущалось сразу, а не «через полсекунды».
        Vector3 torque = axis.normalized * angle * 50f - rb.angularVelocity * 14f;
        rb.AddTorque(Vector3.ClampMagnitude(torque, 300f), ForceMode.Acceleration);
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

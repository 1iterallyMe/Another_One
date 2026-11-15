using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PickupObject : InteractableBase, IPhysicsHoldable
{
    private Rigidbody rb;

    [Header("Hold Settings")]
    public float followForce = 600f;
    public float maxForce = 1500f;
    public float damping = 15f;

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

    private Vector3 targetPosition;
    private Quaternion targetRotation = Quaternion.identity;

    private float nextImpactTime;

    public override string InteractionPrompt => "Взять";
    public override bool CanInteract(PlayerInteractor interactor) => !IsHeld;

    public override void Interact(PlayerInteractor interactor)
        => interactor.PhysicsHold.BeginHold(this);

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        SetupAudio();
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

        rb.useGravity = false;
        rb.linearDamping = 8f;
        rb.angularDamping = 8f;
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
        if (audioSource == null || impactClips == null || impactClips.Length == 0) return;

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

        Collider[] overlaps = Physics.OverlapSphere(targetPosition, collisionCheckRadius, collisionMask);
        bool blocked = false;
        foreach (Collider col in overlaps)
        {
            if (col.attachedRigidbody != rb)
            {
                blocked = true;
                break;
            }
        }

        Vector3 finalTarget = blocked ? rb.position : targetPosition;
        Vector3 toTarget = finalTarget - rb.position;

        Vector3 force = toTarget * followForce - rb.linearVelocity * damping;
        force = Vector3.ClampMagnitude(force, maxForce);
        rb.AddForce(force, ForceMode.Acceleration);

        Quaternion deltaRot = targetRotation * Quaternion.Inverse(rb.rotation);
        deltaRot.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;

        Vector3 torque = axis * angle * 10f - rb.angularVelocity * 5f;
        rb.AddTorque(torque, ForceMode.Acceleration);
    }
}

using UnityEngine;

/// <summary>
/// �������� � ��������: ����������� ����� PickupObject, ��� ���������� � ��������� ����������
/// �������������� ��� ����� ������� ��� �����������, �������� ������� �� ������ � ��������, � ����� ������������.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(PickupObject))]
public class FuelCanister : MonoBehaviour
{
    [Header("Fuel Settings")]
    [SerializeField, Min(0.01f)] private float fuelCapacity = 100f;
    [SerializeField, Min(0f)] private float initialFuel = 100f;
    [Tooltip("����� ������� ������ ����� ������ ������� ����� �������� ������� �������� E.")]
    [SerializeField, Min(0f)] private float cancelLockDuration = 0.2f;
    [SerializeField] private KeyCode cancelKey = KeyCode.E;

    [Header("Nozzle & Pour Pose (������ ��� �����������)")]
    [Tooltip("������� ������� ������ � ��������� ����������� �������� (X � ������, Y � �����, Z � �����). ��� ����� ����������� � PourTargetPoint.")]
    [SerializeField] private Vector3 nozzleLocalOffset = Vector3.zero;
    [Tooltip("�������/������ �������� ��� ������� (� ��������) ������������ �������� PourTargetPoint. ��������, 135-150 �� X ��� Z �������������� �������� ��� ����� �����.")]
    [SerializeField] private Vector3 pourTiltEuler = new Vector3(135f, 0f, 0f);
    [Tooltip("���� �������� � ������� ��� ������� ��������� �� ������� ���� (� ������ Y PourTargetPoint), ����� ��������� �� �������� PourTargetPoint.")]
    [SerializeField] private bool alignPourYawToTarget = true;
    [SerializeField, Min(0.1f)] private float moveToPourSpeed = 8f;
    [SerializeField, Min(0.1f)] private float rotateToPourSpeed = 10f;

    [Header("Pour Visuals & Sound")]
    [Tooltip("������������ ������ ����� (ParticleSystem ��� ���), ���������� �� ����� �������.")]
    [SerializeField] private GameObject pourStreamObject;
    [SerializeField] private AudioSource pourAudioSource;
    [SerializeField] private AudioClip pourLoopClip;
    [SerializeField, Range(0f, 1f)] private float pourVolume = 0.85f;
    [SerializeField] private Vector2 pourPitchRange = new Vector2(0.95f, 1.15f);
    [SerializeField] private AudioClip fuelFullClip;
    [SerializeField, Range(0f, 1f)] private float fuelFullVolume = 0.9f;

    private Rigidbody rb;
    private PickupObject pickupObject;
    private FuelGenerator activeGenerator;
    private Transform pourTarget;

    private float currentFuel;
    private float pourElapsed;
    private bool previousKinematic;
    private bool previousUseGravity;

    public float CurrentFuel => currentFuel;
    public float FuelCapacity => fuelCapacity;
    public float FuelNormalized => fuelCapacity > 0f ? Mathf.Clamp01(currentFuel / fuelCapacity) : 0f;
    public bool HasFuel => currentFuel > 0.0001f;
    public bool IsPouring => activeGenerator != null;
    public Vector3 NozzleWorldPosition => transform.TransformPoint(nozzleLocalOffset);

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        pickupObject = GetComponent<PickupObject>();
        currentFuel = Mathf.Clamp(initialFuel, 0f, fuelCapacity);

        SetupAudio();
        SetStreamActive(false);
    }

    private void OnValidate()
    {
        fuelCapacity = Mathf.Max(0.01f, fuelCapacity);
        initialFuel = Mathf.Clamp(initialFuel, 0f, fuelCapacity);
    }

    private void SetupAudio()
    {
        if (pourAudioSource == null)
            pourAudioSource = GetComponent<AudioSource>();

        if (pourAudioSource == null)
            pourAudioSource = gameObject.AddComponent<AudioSource>();

        pourAudioSource.spatialBlend = 1f;
        pourAudioSource.playOnAwake = false;
    }

    private void Update()
    {
        if (!IsPouring)
            return;

        pourElapsed += Time.deltaTime;

        if (pourElapsed >= cancelLockDuration && Input.GetKeyDown(cancelKey))
        {
            activeGenerator.CancelPouring();
            return;
        }

        if (pourAudioSource != null && pourAudioSource.isPlaying && pourLoopClip != null)
        {
            float progress = 1f - FuelNormalized;
            pourAudioSource.pitch = Mathf.Lerp(pourPitchRange.x, pourPitchRange.y, progress);
        }
    }

    private void FixedUpdate()
    {
        if (!IsPouring || pourTarget == null)
            return;

        Quaternion desiredRot = GetDesiredPourRotation();
        Vector3 rotatedNozzleOffset = desiredRot * nozzleLocalOffset;
        Vector3 desiredRootPos = pourTarget.position - rotatedNozzleOffset;

        Vector3 nextPos = Vector3.MoveTowards(
            rb.position,
            desiredRootPos,
            moveToPourSpeed * Time.fixedDeltaTime);

        Quaternion nextRot = Quaternion.Slerp(
            rb.rotation,
            desiredRot,
            1f - Mathf.Exp(-rotateToPourSpeed * Time.fixedDeltaTime));

        rb.MovePosition(nextPos);
        rb.MoveRotation(nextRot);
    }

    private Quaternion GetDesiredPourRotation()
    {
        if (pourTarget == null)
            return Quaternion.Euler(pourTiltEuler);

        if (alignPourYawToTarget)
        {
            float targetYaw = pourTarget.eulerAngles.y;
            return Quaternion.Euler(0f, targetYaw, 0f) * Quaternion.Euler(pourTiltEuler);
        }

        return pourTarget.rotation * Quaternion.Euler(pourTiltEuler);
    }

    public bool BeginPouring(FuelGenerator generator, Transform targetPoint)
    {
        if (generator == null || !HasFuel || IsPouring)
            return false;

        activeGenerator = generator;
        pourTarget = targetPoint != null ? targetPoint : generator.transform;
        pourElapsed = 0f;

        ReleaseFromPlayerHold();

        previousKinematic = rb.isKinematic;
        previousUseGravity = rb.useGravity;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
        rb.useGravity = false;

        SetStreamActive(true);
        StartPourSound();
        return true;
    }

    public float ConsumeFuel(float amount)
    {
        if (!HasFuel || amount <= 0f)
            return 0f;

        float consumed = Mathf.Min(currentFuel, amount);
        currentFuel = Mathf.Max(0f, currentFuel - consumed);
        return consumed;
    }

    public void EndPouring(bool completedFull)
    {
        if (!IsPouring)
            return;

        activeGenerator = null;
        pourTarget = null;
        pourElapsed = 0f;

        SetStreamActive(false);
        StopPourSound();

        rb.isKinematic = previousKinematic;
        if (!rb.isKinematic)
        {
            // Поднимаем канистру над коллайдером генератора, иначе при
            // включении физики депенетрация может отбросить её далеко.
            rb.position = transform.position + Vector3.up * 0.35f;
            rb.useGravity = previousUseGravity || true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (completedFull && fuelFullClip != null && pourAudioSource != null)
        {
            pourAudioSource.pitch = 1f;
            pourAudioSource.PlayOneShot(fuelFullClip, fuelFullVolume);
        }
    }

    private void ReleaseFromPlayerHold()
    {
        PhysicsHoldController holdController = FindFirstObjectByType<PhysicsHoldController>();
        if (holdController != null && holdController.IsHolding && ReferenceEquals(holdController.Held, pickupObject))
        {
            holdController.Release(false);
        }

        if (pickupObject != null && pickupObject.IsHeld)
        {
            pickupObject.EndHold();
        }
    }

    private void SetStreamActive(bool active)
    {
        if (pourStreamObject != null)
            pourStreamObject.SetActive(active);
    }

    private void StartPourSound()
    {
        if (pourAudioSource == null || pourLoopClip == null)
            return;

        pourAudioSource.Stop();
        pourAudioSource.clip = pourLoopClip;
        pourAudioSource.loop = true;
        pourAudioSource.volume = pourVolume;
        pourAudioSource.pitch = pourPitchRange.x;
        pourAudioSource.Play();
    }

    private void StopPourSound()
    {
        if (pourAudioSource == null)
            return;

        if (pourAudioSource.loop && pourAudioSource.isPlaying)
            pourAudioSource.Stop();
    }

    private void OnDestroy()
    {
        SetStreamActive(false);
        StopPourSound();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(NozzleWorldPosition, 0.035f);
    }
}
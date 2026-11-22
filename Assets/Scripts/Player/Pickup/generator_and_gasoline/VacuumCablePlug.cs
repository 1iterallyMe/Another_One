using System.Collections;
using GogoGaga.OptimizedRopesAndCables;
using UnityEngine;

/// <summary>
/// Штекер провода «как у пылесоса» на базе OptimizedRopesAndCables (Rope).
/// Плавная размотка по мере удаления, авто-возврат при отпускании/превышении длины
/// и фиксация в розетке на столбе.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class VacuumCablePlug : PickupObject
{
    [Header("Rope & Generator")]
    [SerializeField] private Rope cableRope;
    [SerializeField] private FuelGenerator generator;
    [Tooltip("Точка на штекере, к которой привязывается Rope.EndPoint (если не задана — используется сам штекер).")]
    [SerializeField] private Transform plugEndAnchor;

    [Header("Cable Length & Reel-In")]
    [Tooltip("Максимальная длина шнура от генератора (м).")]
    [SerializeField, Min(0.5f)] private float maxCableLength = 7f;
    [Tooltip("Минимальная длина шнура, когда вилка рядом с генератором (м).")]
    [SerializeField, Min(0.1f)] private float minRopeLength = 0.5f;
    [Tooltip("Дополнительный запас длины для лёгкого провиса при размотке (м).")]
    [SerializeField, Min(0f)] private float slackMargin = 0.25f;
    [Tooltip("Запас сверх Max Cable Length, после которого вилка срывается на авто-возврат (м).")]
    [SerializeField, Min(0f)] private float snapBackMargin = 0.1f;
    [Tooltip("Скорость сматывания шнура обратно к генератору (м/с).")]
    [SerializeField, Min(0.1f)] private float retractSpeed = 12f;
    [Tooltip("Дистанция до StartPoint, при которой смотка считается завершённой (м).")]
    [SerializeField, Min(0.02f)] private float snapArriveDistance = 0.08f;

    [Header("Retract Audio")]
    [SerializeField] private AudioClip retractClip;
    [SerializeField, Range(0f, 1f)] private float retractVolume = 0.75f;

    private Rigidbody plugRb;
    private PhysicsHoldController holdController;
    private AudioSource cableAudio;
    private Coroutine physicsSyncRoutine;

    private bool isConnectedToSocket;
    private bool isRetracting;
    private Transform connectedSocketPoint;
    private bool prevKinematic;
    private bool prevUseGravity;

    public bool IsConnected => isConnectedToSocket;
    public FuelGenerator Generator => generator;
    public Rope CableRope => cableRope;

    public Transform StartPoint => cableRope != null ? cableRope.StartPoint : null;
    public Transform EndPoint => plugEndAnchor != null ? plugEndAnchor : (cableRope != null ? cableRope.EndPoint : transform);

    private void Start()
    {
        plugRb = GetComponent<Rigidbody>();
        holdController = FindFirstObjectByType<PhysicsHoldController>();
        SetupCableAudio();
        BindRopeEndPoint();
        UpdateRopeLengthSmooth(instant: true);
    }

    private void OnValidate()
    {
        maxCableLength = Mathf.Max(0.5f, maxCableLength);
        minRopeLength = Mathf.Clamp(minRopeLength, 0.1f, maxCableLength);
        slackMargin = Mathf.Max(0f, slackMargin);
        snapBackMargin = Mathf.Max(0f, snapBackMargin);
        retractSpeed = Mathf.Max(0.1f, retractSpeed);
        snapArriveDistance = Mathf.Max(0.02f, snapArriveDistance);
    }

    private void OnEnable()
    {
        if (holdController == null)
            holdController = FindFirstObjectByType<PhysicsHoldController>();

        if (holdController != null)
            holdController.HoldEnded += HandleHoldEnded;

        if (physicsSyncRoutine == null)
            physicsSyncRoutine = StartCoroutine(PhysicsSyncLoop());
    }

    protected override void OnDisable()
    {
        base.OnDisable();

        if (holdController != null)
            holdController.HoldEnded -= HandleHoldEnded;

        if (physicsSyncRoutine != null)
        {
            StopCoroutine(physicsSyncRoutine);
            physicsSyncRoutine = null;
        }
    }

    private void BindRopeEndPoint()
    {
        if (cableRope == null)
            return;

        Transform anchor = plugEndAnchor != null ? plugEndAnchor : transform;
        if (cableRope.EndPoint != anchor)
            cableRope.SetEndPoint(anchor, instantAssign: true);
    }

    private void SetupCableAudio()
    {
        cableAudio = GetComponent<AudioSource>();
        if (cableAudio == null)
            cableAudio = gameObject.AddComponent<AudioSource>();

        cableAudio.spatialBlend = 1f;
        cableAudio.playOnAwake = false;
    }

    public override bool CanInteract(PlayerInteractor interactor)
    {
        if (isConnectedToSocket)
            return false;

        return base.CanInteract(interactor);
    }

    public override void Interact(PlayerInteractor interactor)
    {
        if (isConnectedToSocket)
            return;

        isRetracting = false;
        StopRetractSound();
        base.Interact(interactor);
    }

    private void HandleHoldEnded(IPhysicsHoldable released)
    {
        if (!ReferenceEquals(released, this))
            return;

        if (!isConnectedToSocket)
            BeginRetract();
    }

    private void BeginRetract()
    {
        if (isConnectedToSocket || plugRb == null)
            return;

        isRetracting = true;
        plugRb.isKinematic = true;
        plugRb.useGravity = false;
        plugRb.linearVelocity = Vector3.zero;
        plugRb.angularVelocity = Vector3.zero;

        StartRetractSound();
    }

    private void Update()
    {
        if (plugRb == null)
            return;

        if (!isConnectedToSocket && StartPoint != null)
        {
            float distFromStart = Vector3.Distance(StartPoint.position, EndPoint.position);

            if (IsHeld && distFromStart > maxCableLength + snapBackMargin)
            {
                if (holdController != null && holdController.IsHolding && ReferenceEquals(holdController.Held, this))
                    holdController.Release(false);
                else
                    EndHold();

                BeginRetract();
            }
        }

        UpdateRopeLengthSmooth(instant: false);
    }

    private void UpdateRopeLengthSmooth(bool instant)
    {
        if (cableRope == null || StartPoint == null)
            return;

        float currentDist = Vector3.Distance(StartPoint.position, EndPoint.position);
        float desiredLength = Mathf.Clamp(currentDist + slackMargin, minRopeLength, maxCableLength + slackMargin);

        if (instant)
        {
            cableRope.ropeLength = desiredLength;
            cableRope.RecalculateRope();
        }
        else
        {
            float speed = isRetracting ? retractSpeed * 1.5f : retractSpeed;
            cableRope.ropeLength = Mathf.MoveTowards(cableRope.ropeLength, desiredLength, speed * Time.deltaTime);
        }
    }

    private IEnumerator PhysicsSyncLoop()
    {
        WaitForFixedUpdate waitFixed = new WaitForFixedUpdate();

        while (true)
        {
            yield return waitFixed;

            if (plugRb == null)
                continue;

            if (isConnectedToSocket && connectedSocketPoint != null)
            {
                plugRb.MovePosition(connectedSocketPoint.position);
                plugRb.MoveRotation(connectedSocketPoint.rotation);
                continue;
            }

            if (isRetracting)
            {
                UpdateRetractMotion();
                continue;
            }

            if (IsHeld && StartPoint != null)
            {
                Vector3 originPos = StartPoint.position;
                Vector3 toPlug = plugRb.position - originPos;
                if (toPlug.sqrMagnitude > maxCableLength * maxCableLength)
                {
                    Vector3 clampedTarget = originPos + toPlug.normalized * maxCableLength;
                    SetHoldTarget(clampedTarget, transform.rotation);
                }
            }
        }
    }

    private void UpdateRetractMotion()
    {
        if (StartPoint == null)
        {
            isRetracting = false;
            StopRetractSound();
            return;
        }

        Vector3 targetPos = StartPoint.position;
        Vector3 nextPos = Vector3.MoveTowards(plugRb.position, targetPos, retractSpeed * Time.fixedDeltaTime);
        plugRb.MovePosition(nextPos);

        if (Vector3.Distance(nextPos, targetPos) <= snapArriveDistance)
        {
            isRetracting = false;
            StopRetractSound();
            plugRb.isKinematic = false;
            plugRb.useGravity = true;
            plugRb.linearVelocity = Vector3.zero;
            plugRb.angularVelocity = Vector3.zero;
        }
    }

    /// <summary>Защёлкивает штекер в розетке на столбе и подаёт питание от генератора.</summary>
    public void ConnectToSocket(Transform socketPoint, FuelGenerator targetGenerator)
    {
        if (targetGenerator != null)
            generator = targetGenerator;

        if (plugRb == null)
            plugRb = GetComponent<Rigidbody>();

        isRetracting = false;
        StopRetractSound();

        if (IsHeld)
        {
            if (holdController != null && holdController.IsHolding && ReferenceEquals(holdController.Held, this))
                holdController.Release(false);
            else
                EndHold();
        }

        prevKinematic = plugRb.isKinematic;
        prevUseGravity = plugRb.useGravity;

        isConnectedToSocket = true;
        connectedSocketPoint = socketPoint;

        plugRb.linearVelocity = Vector3.zero;
        plugRb.angularVelocity = Vector3.zero;
        plugRb.isKinematic = true;
        plugRb.useGravity = false;

        if (socketPoint != null)
        {
            plugRb.position = socketPoint.position;
            plugRb.rotation = socketPoint.rotation;
        }

        if (cableRope != null)
            cableRope.RecalculateRope();

        if (generator != null)
            generator.SetCableConnected(true);
    }

    /// <summary>Вынимает штекер из розетки на столбе и отключает питание от генератора.</summary>
    public void DisconnectFromSocket()
    {
        if (!isConnectedToSocket)
            return;

        isConnectedToSocket = false;
        connectedSocketPoint = null;

        if (generator != null)
            generator.SetCableConnected(false);

        if (plugRb != null)
        {
            plugRb.isKinematic = prevKinematic;
            plugRb.useGravity = prevUseGravity || true;
        }
    }

    private void StartRetractSound()
    {
        if (cableAudio == null || retractClip == null)
            return;

        cableAudio.Stop();
        cableAudio.clip = retractClip;
        cableAudio.loop = true;
        cableAudio.volume = retractVolume;
        cableAudio.Play();
    }

    private void StopRetractSound()
    {
        if (cableAudio == null)
            return;

        if (cableAudio.loop && cableAudio.isPlaying)
            cableAudio.Stop();
    }

    private void OnDrawGizmosSelected()
    {
        if (StartPoint == null)
            return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(StartPoint.position, maxCableLength);
    }
}
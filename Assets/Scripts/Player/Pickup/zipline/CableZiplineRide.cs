using System.Reflection;
using UnityEngine;

/// <summary>
/// Зиплайн на тросе: выключается на старте (если не установлен), включается при установке собранного зиплайна,
/// позволяет повиснуть на E, тянет руку к точке хвата, едет по W (+Y) и S (-Y)
/// без смены направления на ходу, останавливается в точных точках и высаживает на E или F после задержки.
/// </summary>
[DisallowMultipleComponent]
public class CableZiplineRide : InteractableBase
{
    [Header("Activation & Power")]
    [Tooltip("Если объект включён в сцене при старте и ещё не установлен — выключить его автоматически в Awake.")]
    [SerializeField] private bool autoDisableAtStart = true;
    [Tooltip("Включено: питание считается доступным без подключения провода/генератора. Выключено: проверяется FuelGenerator.HasPower.")]
    [SerializeField] private bool powerWithoutGenerator = true;
    [SerializeField] private FuelGenerator generator;

    [Header("Mount & Hang Points")]
    [Tooltip("Точка крепления/установки на тросе (если не задана — используется позиция самого объекта).")]
    [SerializeField] private Transform mountPoint;
    [Tooltip("Точка, в которой висит игрок во время поездки.")]
    [SerializeField] private Transform hangPoint;
    [Tooltip("Точка на зиплайне, к которой тянется рука игрока при посадке.")]
    [SerializeField] private Transform gripPoint;

    [Header("Vertical Travel (строго по Y)")]
    [Tooltip("Ограничивать стартовую высоту Y диапазоном [Min Y .. Max Y] при активации.")]
    [SerializeField] private bool clampYOnActivate = false;
    [Tooltip("Нижняя координата Y остановки.")]
    [SerializeField] private float minY = 8.75f;
    [Tooltip("Верхняя координата Y остановки.")]
    [SerializeField] private float maxY = 14.0f;
    [Tooltip("Скорость движения зиплайна по оси Y (ед./сек).")]
    [SerializeField, Min(0.1f)] private float moveSpeed = 4.5f;
    [SerializeField] private KeyCode forwardPlusYKey = KeyCode.W;
    [SerializeField] private KeyCode backwardMinusYKey = KeyCode.S;

    [Header("Exit Points (точки выхода рядом со столбом)")]
    [Tooltip("Точка выхода при выходе в верхней половине пути / у maxY.")]
    [SerializeField] private Transform upperExitPoint;
    [Tooltip("Точка выхода при выходе в нижней половине пути / у minY / у начальной точки.")]
    [SerializeField] private Transform lowerExitPoint;
    [Tooltip("Задержка в секундах после посадки на зиплайн, до истечения которой нельзя выйти.")]
    [SerializeField, Min(0f)] private float exitCooldownAfterMount = 3.0f;
    [SerializeField] private KeyCode exitKeyPrimary = KeyCode.E;
    [SerializeField] private KeyCode exitKeySecondary = KeyCode.F;

    [Header("Sounds")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip[] mountClips;
    [SerializeField, Range(0f, 1f)] private float mountVolume = 0.85f;
    [SerializeField] private AudioClip[] moveStartClips;
    [SerializeField, Range(0f, 1f)] private float moveStartVolume = 0.9f;
    [SerializeField] private AudioClip moveLoopClip;
    [SerializeField, Range(0f, 1f)] private float moveLoopVolume = 0.75f;
    [SerializeField] private AudioClip[] moveStopClips;
    [SerializeField, Range(0f, 1f)] private float moveStopVolume = 0.9f;
    [SerializeField] private AudioClip[] dismountClips;
    [SerializeField, Range(0f, 1f)] private float dismountVolume = 0.8f;

    public float MinY => minY;
    public float MaxY => maxY;
    public bool IsMounted { get; private set; }
    public bool IsMoving { get; private set; }
    public int MoveDirectionY { get; private set; } // +1 вверх по Y, -1 вниз по Y, 0 стоит
    public bool HasPower => powerWithoutGenerator || (generator != null && generator.HasPower);

    public Vector3 MountWorldPosition => mountPoint != null ? mountPoint.position : transform.position;

    private PlayerInteractor riderInteractor;
    private Transform riderTransform;
    private Rigidbody riderRb;
    private CharacterController riderCharController;
    private bool prevRiderRbKinematic;
    private bool prevRiderRbUseGravity;
    private bool prevCharControllerEnabled;
    private Vector3 riderHangOffset;
    private float targetY;
    private float mountedAtTime;
    private bool isInstalled;

    private void Awake()
    {
        SetupAudio();

        if (minY > maxY)
        {
            float temp = minY;
            minY = maxY;
            maxY = temp;
        }

        if (autoDisableAtStart && !isInstalled)
        {
            gameObject.SetActive(false);
        }
    }

    private void SetupAudio()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.spatialBlend = 1f;
        audioSource.playOnAwake = false;
    }

    public void MarkAsInstalled()
    {
        isInstalled = true;
    }

    public void ActivateOnCable()
    {
        isInstalled = true;
        IsMoving = false;
        MoveDirectionY = 0;

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = true;
        }

        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = true;
        }

        if (clampYOnActivate)
        {
            float clampedY = Mathf.Clamp(transform.position.y, minY, maxY);
            Vector3 pos = transform.position;
            pos.y = clampedY;
            transform.position = pos;
        }
    }

    public override bool CanInteract(PlayerInteractor interactor)
    {
        if (!base.CanInteract(interactor) || interactor == null)
            return false;

        if (!isInstalled || IsMounted)
            return false;

        return HasPower;
    }

    public override void Interact(PlayerInteractor interactor)
    {
        if (interactor == null || !isInstalled || IsMounted || !HasPower)
            return;

        MountPlayer(interactor);
    }

    private void MountPlayer(PlayerInteractor interactor)
    {
        if (interactor.PhysicsHold != null && interactor.PhysicsHold.IsHolding)
        {
            interactor.PhysicsHold.Release(false);
        }

        riderInteractor = interactor;
        riderTransform = interactor.transform;
        riderRb = riderTransform.GetComponent<Rigidbody>();
        riderCharController = riderTransform.GetComponent<CharacterController>();

        if (riderCharController != null)
        {
            prevCharControllerEnabled = riderCharController.enabled;
            riderCharController.enabled = false;
        }

        if (riderRb != null)
        {
            prevRiderRbKinematic = riderRb.isKinematic;
            prevRiderRbUseGravity = riderRb.useGravity;
            riderRb.linearVelocity = Vector3.zero;
            riderRb.angularVelocity = Vector3.zero;
            riderRb.isKinematic = true;
            riderRb.useGravity = false;
        }

        Vector3 targetHangPos = hangPoint != null ? hangPoint.position : transform.position;
        riderHangOffset = riderTransform.position - targetHangPos;
        riderTransform.position = targetHangPos;

        IsMounted = true;
        IsMoving = false;
        MoveDirectionY = 0;
        mountedAtTime = Time.time;

        ReachHandToZipline();
        PlayRandomClip(mountClips, mountVolume);
    }

    private void ReachHandToZipline()
    {
        Vector3 reachPos = gripPoint != null
            ? gripPoint.position
            : (hangPoint != null ? hangPoint.position : transform.position);

        HandController hand = FindFirstObjectByType<HandController>();
        if (hand == null)
            return;

        System.Type t = hand.GetType();
        MethodInfo setTargetMethod = t.GetMethod("SetTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (setTargetMethod != null && setTargetMethod.GetParameters().Length == 1)
        {
            setTargetMethod.Invoke(hand, new object[] { reachPos });
            return;
        }

        PropertyInfo targetProp = t.GetProperty("TargetPosition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                             ?? t.GetProperty("Target", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (targetProp != null && targetProp.CanWrite && targetProp.PropertyType == typeof(Vector3))
        {
            targetProp.SetValue(hand, reachPos);
            return;
        }

        FieldInfo targetField = t.GetField("targetPosition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                           ?? t.GetField("target", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (targetField != null && targetField.FieldType == typeof(Vector3))
        {
            targetField.SetValue(hand, reachPos);
        }
    }

    private void Update()
    {
        if (!IsMounted)
            return;

        bool canExitNow = (Time.time - mountedAtTime) >= exitCooldownAfterMount;
        if (canExitNow && (Input.GetKeyDown(exitKeyPrimary) || Input.GetKeyDown(exitKeySecondary)))
        {
            DismountPlayer();
            return;
        }

        if (!IsMoving && HasPower)
        {
            if (Input.GetKeyDown(forwardPlusYKey))
            {
                StartMoveAlongY(+1, maxY);
            }
            else if (Input.GetKeyDown(backwardMinusYKey))
            {
                StartMoveAlongY(-1, minY);
            }
        }

        if (IsMoving)
        {
            UpdateMovementY();
        }

        SyncRiderToZipline();
    }

    private void StartMoveAlongY(int direction, float destinationY)
    {
        float currentY = transform.position.y;
        if (Mathf.Abs(destinationY - currentY) <= 0.001f)
            return;

        MoveDirectionY = direction;
        targetY = destinationY;
        IsMoving = true;

        PlayRandomClip(moveStartClips, moveStartVolume);
        StartMoveLoopSound();
    }

    private void UpdateMovementY()
    {
        Vector3 pos = transform.position;
        float nextY = Mathf.MoveTowards(pos.y, targetY, moveSpeed * Time.deltaTime);
        pos.y = nextY;
        transform.position = pos;

        if (Mathf.Abs(targetY - nextY) <= 0.001f)
        {
            pos.y = targetY;
            transform.position = pos;

            IsMoving = false;
            MoveDirectionY = 0;

            StopMoveLoopSound();
            PlayRandomClip(moveStopClips, moveStopVolume);
        }
    }

    private void SyncRiderToZipline()
    {
        if (riderTransform == null)
            return;

        Vector3 baseHang = hangPoint != null ? hangPoint.position : transform.position;
        riderTransform.position = baseHang;
    }

    private void DismountPlayer()
    {
        IsMoving = false;
        MoveDirectionY = 0;
        StopMoveLoopSound();

        if (riderTransform != null)
        {
            Transform chosenExit = ResolveExitPoint();
            if (chosenExit != null)
            {
                riderTransform.position = chosenExit.position;
            }
            else
            {
                riderTransform.position = transform.position + riderHangOffset;
            }
        }

        if (riderRb != null)
        {
            riderRb.isKinematic = prevRiderRbKinematic;
            riderRb.useGravity = prevRiderRbUseGravity;
            riderRb.linearVelocity = Vector3.zero;
            riderRb.angularVelocity = Vector3.zero;
        }

        if (riderCharController != null)
        {
            riderCharController.enabled = prevCharControllerEnabled;
        }

        PlayRandomClip(dismountClips, dismountVolume);

        IsMounted = false;
        riderInteractor = null;
        riderTransform = null;
        riderRb = null;
        riderCharController = null;
    }

    private Transform ResolveExitPoint()
    {
        float midY = (minY + maxY) * 0.5f;
        bool nearUpper = transform.position.y >= midY;

        if (nearUpper && upperExitPoint != null)
            return upperExitPoint;

        if (!nearUpper && lowerExitPoint != null)
            return lowerExitPoint;

        return upperExitPoint != null ? upperExitPoint : lowerExitPoint;
    }

    private void StartMoveLoopSound()
    {
        if (audioSource == null || moveLoopClip == null)
            return;

        audioSource.Stop();
        audioSource.clip = moveLoopClip;
        audioSource.loop = true;
        audioSource.volume = moveLoopVolume;
        audioSource.Play();
    }

    private void StopMoveLoopSound()
    {
        if (audioSource == null)
            return;

        if (audioSource.loop && audioSource.isPlaying)
            audioSource.Stop();
    }

    private void PlayRandomClip(AudioClip[] clips, float volume)
    {
        if (audioSource == null || clips == null || clips.Length == 0)
            return;

        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip != null)
            audioSource.PlayOneShot(clip, volume);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 p = transform.position;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(new Vector3(p.x, minY, p.z), new Vector3(p.x, maxY, p.z));
        Gizmos.DrawWireSphere(new Vector3(p.x, minY, p.z), 0.12f);
        Gizmos.DrawWireSphere(new Vector3(p.x, maxY, p.z), 0.12f);
    }
}
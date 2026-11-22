using UnityEngine;

/// <summary>
/// Съезд по тросу: ручка висит на тросе, игрок цепляется за неё по E,
/// едет вперёд/назад по W/S и отцепляется по E или F.
///
/// Компонент живёт на самой ручке зиплайна. После установки на трос
/// (см. <see cref="AssembledZipline"/>) он двигает ручку так, чтобы её
/// CableAnchor оставался ровно на линии троса.
/// </summary>
[DisallowMultipleComponent]
public class CableZiplineRide : InteractableBase
{
    [Header("Interaction")]
    [Tooltip("Подсказка, которая показывается игроку при наведении на установленную ручку.")]
    [SerializeField] private string ridePrompt = "Схватиться за зиплайн";

    [Header("Activation & Power")]
    [Tooltip("Выключать объект в Awake, пока он не установлен. Для ручки-предмета должно быть false.")]
    [SerializeField] private bool autoDisableAtStart = false;
    [Tooltip("Разрешить движение без генератора. Для тестового зиплайна включено.")]
    [SerializeField] private bool powerWithoutGenerator = true;
    [SerializeField] private FuelGenerator generator;

    [Header("Mount & Hang Points")]
    [Tooltip("Точка на самом тросе (центр роликов ручки). Именно она держится на канате.")]
    [SerializeField] private Transform cableAnchor;
    [Tooltip("Необязательная точка крепления на тросе. Если пусто — используется CableAnchor.")]
    [SerializeField] private Transform mountPoint;
    [Tooltip("Точка, в которую ставится игрок (его transform — это ноги).")]
    [SerializeField] private Transform hangPoint;
    [Tooltip("Точка, к которой тянется рука игрока (перекладина ручки).")]
    [SerializeField] private Transform gripPoint;

    [Header("Vertical Travel (Y)")]
    [SerializeField] private bool clampYOnActivate = false;
    [SerializeField] private float minY = 8.75f;
    [SerializeField] private float maxY = 14f;
    [SerializeField, Min(0.1f)] private float moveSpeed = 4.5f;
    [SerializeField] private KeyCode forwardPlusYKey = KeyCode.W;
    [SerializeField] private KeyCode backwardMinusYKey = KeyCode.S;

    [Header("Cable Route (forward/back)")]
    [Tooltip("Ехать вдоль троса, а не по вертикали.")]
    [SerializeField] private bool useCableRoute = true;
    [Tooltip("Маркер троса. Если пусто — ищется в сцене автоматически.")]
    [SerializeField] private ZiplineCableRoute cableRoute;
    [Tooltip("Запасные концы маршрута, если CableRoute не назначен.")]
    [SerializeField] private Transform routeStartPoint;
    [SerializeField] private Transform routeEndPoint;
    [SerializeField, Min(0.05f)] private float routeSpeed = 2.5f;

    [Header("Exit Points")]
    [SerializeField] private Transform upperExitPoint;
    [SerializeField] private Transform lowerExitPoint;
    [SerializeField] private bool placeExitOnGroundIfNoPoint = true;
    [SerializeField, Min(0f)] private float exitCooldownAfterMount = 1.0f;
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

    /// <summary>Активная поездка. PlayerInteractor блокирует обычное взаимодействие, пока это не null.</summary>
    public static CableZiplineRide ActiveMountedRide { get; private set; }

    public bool IsMounted { get; private set; }
    public bool IsMoving { get; private set; }
    public bool IsInstalled => isInstalled;
    public ZiplineCableRoute CableRoute => cableRoute;
    public int MoveDirectionY { get; private set; }
    public float RouteProgress => routeT;
    public bool HasPower => powerWithoutGenerator || (generator != null && generator.HasPower);

    public Vector3 MountWorldPosition
    {
        get
        {
            if (cableAnchor != null) return cableAnchor.position;
            if (mountPoint != null) return mountPoint.position;
            return transform.position;
        }
    }

    public override string InteractionPrompt => ridePrompt;

    private Transform riderTransform;
    private Rigidbody riderRb;
    private CharacterController riderCharController;
    private FPSController riderFps;
    private HandController riderHand;

    private bool prevRiderRbKinematic;
    private bool prevRiderRbUseGravity;
    private bool prevCharControllerEnabled;

    private float targetY;
    private float mountedAtTime;
    private float routeT;
    private bool isInstalled;
    private bool routeInitialized;

    private void Awake()
    {
        ResolveAnchors();
        SetupAudio();

        if (minY > maxY)
        {
            float swap = minY;
            minY = maxY;
            maxY = swap;
        }

        if (autoDisableAtStart && !isInstalled)
            gameObject.SetActive(false);
    }

    private void ResolveAnchors()
    {
        if (cableAnchor == null)
        {
            Transform found = transform.Find("Anchors/CableAnchor");
            if (found == null) found = transform.Find("CableAnchor");
            cableAnchor = found;
        }

        if (cableAnchor == null)
            cableAnchor = mountPoint;

        if (mountPoint == null)
            mountPoint = cableAnchor;

        if (hangPoint == null)
        {
            Transform found = transform.Find("Anchors/HangPoint");
            if (found == null) found = transform.Find("HangPoint");
            hangPoint = found;
        }

        if (gripPoint == null)
        {
            Transform found = transform.Find("Anchors/GripPoint");
            if (found == null) found = transform.Find("GripPoint");
            gripPoint = found;
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
        audioSource.loop = false;
    }

    private bool HasRoute
    {
        get
        {
            if (cableRoute != null && cableRoute.IsValid) return true;
            return routeStartPoint != null && routeEndPoint != null;
        }
    }

    /// <summary>Связывает ручку с тросом и ставит её в нужную точку маршрута.</summary>
    public void ConfigureRoute(ZiplineCableRoute route, float initialProgress)
    {
        if (route != null && route.IsValid)
        {
            cableRoute = route;
            routeStartPoint = route.StartPoint;
            routeEndPoint = route.EndPoint;
            useCableRoute = true;
        }

        if (!HasRoute)
            return;

        routeT = Mathf.Clamp01(initialProgress);
        AlignToRoute();
        PlaceAtRouteT(routeT);
    }

    /// <summary>Ставит ручку так, чтобы её CableAnchor оказался в точке маршрута t.</summary>
    public void PlaceAtRouteT(float t)
    {
        if (!HasRoute)
            return;

        routeT = Mathf.Clamp01(t);
        Vector3 cablePoint = GetRoutePoint(routeT);
        Vector3 anchorOffset = cableAnchor != null ? cableAnchor.position - transform.position : Vector3.zero;
        transform.position = cablePoint - anchorOffset;
        routeInitialized = true;
    }

    private Vector3 GetRoutePoint(float t)
    {
        if (cableRoute != null && cableRoute.IsValid)
            return cableRoute.GetPointAt(t);

        if (routeStartPoint != null && routeEndPoint != null)
            return Vector3.Lerp(routeStartPoint.position, routeEndPoint.position, t);

        return transform.position;
    }

    private void AlignToRoute()
    {
        Vector3 direction;

        if (cableRoute != null && cableRoute.IsValid)
            direction = cableRoute.TravelDirection;
        else if (routeStartPoint != null && routeEndPoint != null)
            direction = routeEndPoint.position - routeStartPoint.position;
        else
            return;

        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
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

        if (useCableRoute && HasRoute)
        {
            if (!routeInitialized)
                PlaceAtRouteT(routeT);
        }
        else if (clampYOnActivate)
        {
            Vector3 position = transform.position;
            position.y = Mathf.Clamp(position.y, minY, maxY);
            transform.position = position;
        }
    }

    public override bool CanInteract(PlayerInteractor interactor)
    {
        if (!base.CanInteract(interactor) || interactor == null)
            return false;

        return isInstalled && !IsMounted;
    }

    public override void Interact(PlayerInteractor interactor)
    {
        if (interactor == null || !isInstalled || IsMounted)
            return;

        MountPlayer(interactor);
    }


    private void MountPlayer(PlayerInteractor interactor)
    {
        if (interactor.PhysicsHold != null && interactor.PhysicsHold.IsHolding)
            interactor.PhysicsHold.Release(false);

        riderTransform = interactor.transform;
        riderRb = riderTransform.GetComponent<Rigidbody>();
        riderCharController = riderTransform.GetComponent<CharacterController>();
        riderFps = riderTransform.GetComponent<FPSController>();
        riderHand = riderTransform.GetComponentInChildren<HandController>();

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

        Vector3 targetHangPosition = hangPoint != null ? hangPoint.position : transform.position;
        riderTransform.position = targetHangPosition;

        IsMounted = true;
        IsMoving = false;
        MoveDirectionY = 0;
        mountedAtTime = Time.time;
        ActiveMountedRide = this;

        FaceRiderAlongRoute();
        ReachHandToZipline();
        PlayRandomClip(mountClips, mountVolume);
    }

    private void FaceRiderAlongRoute()
    {
        if (riderTransform == null || !HasRoute)
            return;

        Vector3 direction = cableRoute != null && cableRoute.IsValid
            ? cableRoute.TravelDirection
            : routeEndPoint.position - routeStartPoint.position;

        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        if (riderFps != null)
            riderFps.SetYaw(yaw);
        else
            riderTransform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    private void ReachHandToZipline()
    {
        if (riderHand == null)
            return;

        Vector3 reachPosition = gripPoint != null
            ? gripPoint.position
            : (hangPoint != null ? hangPoint.position : transform.position);

        riderHand.SetTarget(reachPosition);
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

        if (useCableRoute && HasRoute)
            UpdateRouteMovement();
        else
            UpdateVerticalMovement();

        SyncRiderToZipline();
        ReachHandToZipline();
    }

    private void UpdateVerticalMovement()
    {
        if (!IsMoving && HasPower)
        {
            if (Input.GetKeyDown(forwardPlusYKey))
                StartMoveAlongY(+1, maxY);
            else if (Input.GetKeyDown(backwardMinusYKey))
                StartMoveAlongY(-1, minY);
        }

        if (IsMoving)
            UpdateMovementY();
    }

    private void UpdateRouteMovement()
    {
        float input = 0f;
        if (HasPower)
        {
            if (Input.GetKey(forwardPlusYKey)) input += 1f;
            if (Input.GetKey(backwardMinusYKey)) input -= 1f;
        }

        if (Mathf.Abs(input) <= 0.01f)
        {
            StopMoving();
            return;
        }

        float length = Vector3.Distance(routeStartPoint.position, routeEndPoint.position);
        if (length <= 0.001f)
            return;

        float nextT = Mathf.Clamp01(routeT + input * (routeSpeed / length) * Time.deltaTime);
        if (Mathf.Approximately(nextT, routeT))
        {
            StopMoving();
            return;
        }

        if (!IsMoving)
        {
            IsMoving = true;
            PlayRandomClip(moveStartClips, moveStartVolume);
            StartMoveLoopSound();
        }

        MoveDirectionY = input > 0f ? 1 : -1;
        PlaceAtRouteT(nextT);
    }

    private void StopMoving()
    {
        if (!IsMoving)
            return;

        IsMoving = false;
        MoveDirectionY = 0;
        StopMoveLoopSound();
        PlayRandomClip(moveStopClips, moveStopVolume);
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
        Vector3 position = transform.position;
        float nextY = Mathf.MoveTowards(position.y, targetY, moveSpeed * Time.deltaTime);
        position.y = nextY;
        transform.position = position;

        if (Mathf.Abs(targetY - nextY) <= 0.001f)
        {
            position.y = targetY;
            transform.position = position;

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
            Transform exitPoint = ResolveExitPoint();
            if (exitPoint != null)
            {
                riderTransform.position = exitPoint.position;
            }
            else
            {
                riderTransform.position = hangPoint != null ? hangPoint.position : transform.position;
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
            riderCharController.enabled = prevCharControllerEnabled;

        if (riderHand != null)
            riderHand.ClearTarget();

        PlayRandomClip(dismountClips, dismountVolume);

        IsMounted = false;
        riderTransform = null;
        riderRb = null;
        riderCharController = null;
        riderFps = null;
        riderHand = null;

        if (ActiveMountedRide == this)
            ActiveMountedRide = null;
    }

    private Transform ResolveExitPoint()
    {
        if (useCableRoute && HasRoute)
        {
            bool nearEnd = routeT >= 0.5f;

            if (nearEnd && upperExitPoint != null) return upperExitPoint;
            if (!nearEnd && lowerExitPoint != null) return lowerExitPoint;
            if (upperExitPoint != null) return upperExitPoint;
            if (lowerExitPoint != null) return lowerExitPoint;

            if (placeExitOnGroundIfNoPoint)
            {
                Vector3 routeEnd = GetRoutePoint(nearEnd ? 1f : 0f);
                Vector3 groundPoint = FindGroundBelow(routeEnd);
                GameObject exitObject = new GameObject("ZiplineExitPoint");
                exitObject.transform.position = groundPoint;
                return exitObject.transform;
            }

            return null;
        }

        float midY = (minY + maxY) * 0.5f;
        bool nearUpper = transform.position.y >= midY;

        if (nearUpper && upperExitPoint != null) return upperExitPoint;
        if (!nearUpper && lowerExitPoint != null) return lowerExitPoint;

        return upperExitPoint != null ? upperExitPoint : lowerExitPoint;
    }


    /// <summary>Ищет ближайшую твёрдую поверхность под точкой троса.</summary>
    private Vector3 FindGroundBelow(Vector3 nearPoint)
    {
        Vector3 origin = nearPoint + Vector3.up * 0.25f;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 200f, ~0, QueryTriggerInteraction.Ignore);

        float bestY = float.NegativeInfinity;
        Vector3 bestPoint = nearPoint;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null) continue;
            if (hitCollider.transform.IsChildOf(transform)) continue;
            if (riderTransform != null && hitCollider.transform.IsChildOf(riderTransform)) continue;
            if (hits[i].point.y > nearPoint.y + 0.3f) continue;

            if (hits[i].point.y > bestY)
            {
                bestY = hits[i].point.y;
                bestPoint = hits[i].point;
            }
        }

        return bestY == float.NegativeInfinity ? nearPoint : bestPoint + Vector3.up * 0.05f;
    }

    /// <summary>Ручное управление маршрутом (для тестов/других систем).</summary>
    public void MoveRoute(float input, float deltaTime)
    {
        if (!HasPower || !HasRoute)
            return;

        float length = Vector3.Distance(routeStartPoint.position, routeEndPoint.position);
        if (length <= 0.001f)
            return;

        float nextT = Mathf.Clamp01(routeT + input * (routeSpeed / length) * deltaTime);
        PlaceAtRouteT(nextT);
        MoveDirectionY = input > 0f ? 1 : (input < 0f ? -1 : 0);
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
        if (cableAnchor != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(cableAnchor.position, 0.12f);
        }

        if (hangPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(hangPoint.position, 0.15f);
        }

        if (HasRoute)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(routeStartPoint.position, routeEndPoint.position);
        }
        else
        {
            Vector3 position = transform.position;
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(new Vector3(position.x, minY, position.z), new Vector3(position.x, maxY, position.z));
            Gizmos.DrawWireSphere(new Vector3(position.x, minY, position.z), 0.12f);
            Gizmos.DrawWireSphere(new Vector3(position.x, maxY, position.z), 0.12f);
        }
    }
}


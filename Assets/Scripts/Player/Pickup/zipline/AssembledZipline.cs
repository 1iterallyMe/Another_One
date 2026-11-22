using UnityEngine;

/// <summary>
/// Собранная ручка зиплайна. Когда игрок подносит её к тросу, ручка
/// «защёлкивается» на канате (верхняя часть с роликами ложится на трос),
/// становится кинематической и превращается в интерактивную ручку,
/// за которую можно схватиться (см. <see cref="CableZiplineRide"/>).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(PickupObject))]
public class AssembledZipline : MonoBehaviour
{
    [Header("Cable Attach")]
    [Tooltip("Маркер троса. Если пусто — ищется в сцене автоматически.")]
    [SerializeField] private ZiplineCableRoute cableRoute;
    [Tooltip("Точка ручки, которая должна оказаться на тросе (центр роликов).")]
    [SerializeField] private Transform cableAnchor;
    [Tooltip("Радиус, в котором ручка притягивается к тросу, м.")]
    [SerializeField, Min(0.1f)] private float attachRadius = 3f;
    [Tooltip("Притягивать к ближайшей точке троса, а не всегда к началу.")]
    [SerializeField] private bool snapToNearestPoint = true;

    [Header("Legacy Ride (optional)")]
    [Tooltip("Имя объекта с CableZiplineRide, если используется старая схема с отдельным ZiplineRig.")]
    [SerializeField] private string targetRideObjectName = "";

    [Header("Attach Sound")]
    [SerializeField] private AudioClip[] attachClips;
    [SerializeField, Range(0f, 1f)] private float attachVolume = 0.9f;
    [SerializeField] private Vector2 attachPitchRange = new Vector2(0.96f, 1.04f);

    private PickupObject pickupObject;
    private Rigidbody rb;
    private bool isAttached;

    private void Awake()
    {
        pickupObject = GetComponent<PickupObject>();
        rb = GetComponent<Rigidbody>();

        if (cableAnchor == null)
        {
            Transform found = transform.Find("Anchors/CableAnchor");
            if (found == null) found = transform.Find("CableAnchor");
            cableAnchor = found;
        }

        if (cableAnchor == null)
            cableAnchor = transform;

        if (cableRoute == null)
            cableRoute = FindFirstObjectByType<ZiplineCableRoute>();
    }

    private void Update()
    {
        if (isAttached)
            return;

        if (cableRoute == null)
        {
            cableRoute = FindFirstObjectByType<ZiplineCableRoute>();
            if (cableRoute == null)
                return;
        }

        if (!cableRoute.IsValid)
            return;

        Vector3 anchorPosition = cableAnchor.position;
        if (!IsNearCable(anchorPosition))
            return;

        Vector3 cablePoint;
        float routeProgress = snapToNearestPoint
            ? cableRoute.FindClosestProgress(anchorPosition, out cablePoint)
            : StartProgress(out cablePoint);

        AttachToCable(cablePoint, routeProgress);
    }

    private float StartProgress(out Vector3 point)
    {
        point = cableRoute.GetPointAt(0f);
        return 0f;
    }

    /// <summary>
    /// Быстрая проверка по прямой между концами троса, чтобы не семплить
    /// кривую каждый кадр, когда ручка лежит далеко.
    /// </summary>
    private bool IsNearCable(Vector3 worldPosition)
    {
        if (cableRoute == null || !cableRoute.IsValid)
            return false;

        Vector3 start = cableRoute.StartPoint.position;
        Vector3 end = cableRoute.EndPoint.position;
        Vector3 chord = end - start;
        float lengthSqr = chord.sqrMagnitude;

        if (lengthSqr <= 0.0001f)
            return (worldPosition - start).sqrMagnitude <= attachRadius * attachRadius;

        float t = Vector3.Dot(worldPosition - start, chord) / lengthSqr;
        t = Mathf.Clamp01(t);
        Vector3 chordPoint = start + chord * t;

        float roughRadius = attachRadius + 0.6f;
        return (chordPoint - worldPosition).sqrMagnitude <= roughRadius * roughRadius;
    }

    private void AttachToCable(Vector3 cablePoint, float routeProgress)
    {
        if (isAttached || cableRoute == null)
            return;

        isAttached = true;

        // Разворачиваем ручку так, чтобы ролики «смотрели» вдоль троса.
        Vector3 direction = cableRoute.TravelDirection;
        if (direction.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

        // Двигаем ручку так, чтобы CableAnchor оказался ровно на тросе.
        Vector3 anchorOffset = cableAnchor.position - transform.position;
        transform.position = cablePoint - anchorOffset;

        ReleaseFromHands();
        FreezePhysics();
        DisablePickupInteraction();

        CableZiplineRide ride = GetComponent<CableZiplineRide>();
        if (ride == null)
            ride = gameObject.AddComponent<CableZiplineRide>();

        ride.enabled = true;
        ride.ConfigureRoute(cableRoute, routeProgress);
        ride.MarkAsInstalled();
        ride.ActivateOnCable();

        RemoveOtherInstalledRides(ride);
        PlaySoundAt(cablePoint, attachClips, attachVolume, attachPitchRange);

        Debug.Log($"[AssembledZipline] Ручка установлена на трос в точке {cablePoint} (t={routeProgress:0.00}).", this);
    }

    /// <summary>
    /// На один трос — одна установленная ручка. Если игрок принёс и поставил
    /// новую ручку, старая (например, заготовленная в сцене) убирается,
    /// чтобы они не висели друг в друге.
    /// </summary>
    private void RemoveOtherInstalledRides(CableZiplineRide installedRide)
    {
        CableZiplineRide[] rides = FindObjectsByType<CableZiplineRide>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < rides.Length; i++)
        {
            CableZiplineRide other = rides[i];
            if (other == null || other == installedRide || !other.IsInstalled)
                continue;

            if (other.CableRoute != cableRoute)
                continue;

            Destroy(other.gameObject);
        }
    }

    private void ReleaseFromHands()
    {
        PhysicsHoldController holdController = FindFirstObjectByType<PhysicsHoldController>();
        if (holdController != null && holdController.IsHolding && ReferenceEquals(holdController.Held, pickupObject))
            holdController.Release(false);

        if (pickupObject != null && pickupObject.IsHeld)
            pickupObject.EndHold();
    }

    private void FreezePhysics()
    {
        if (rb == null)
            return;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.useGravity = false;
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.None;
    }

    private void DisablePickupInteraction()
    {
        if (pickupObject != null)
            pickupObject.enabled = false;

        Outline outline = GetComponent<Outline>();
        if (outline != null)
            outline.enabled = false;
    }

    // ---------------------------------------------------------------------
    // Старая схема: ручка активирует отдельный CableZiplineRide (ZiplineRig).
    // Оставлено для совместимости со сценами, где такой объект уже размещён.
    // ---------------------------------------------------------------------

    private void OnCollisionEnter(Collision collision)
    {
        TryAttachToLegacyRide(collision != null ? collision.collider : null);
    }

    private void OnTriggerEnter(Collider other)
    {
        TryAttachToLegacyRide(other);
    }

    private void TryAttachToLegacyRide(Collider otherCollider)
    {
        if (isAttached || otherCollider == null || cableRoute != null)
            return;

        CableZiplineRide targetRide = FindLegacyRide();
        if (targetRide == null || targetRide == GetComponent<CableZiplineRide>())
            return;

        if (otherCollider.GetComponentInParent<CableZiplineRide>() != targetRide)
            return;

        isAttached = true;
        ReleaseFromHands();
        PlaySoundAt(targetRide.MountWorldPosition, attachClips, attachVolume, attachPitchRange);

        targetRide.MarkAsInstalled();
        targetRide.gameObject.SetActive(true);
        targetRide.ActivateOnCable();

        Destroy(gameObject);
    }

    private CableZiplineRide FindLegacyRide()
    {
        CableZiplineRide[] rides = FindObjectsByType<CableZiplineRide>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (rides == null || rides.Length == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(targetRideObjectName))
        {
            for (int i = 0; i < rides.Length; i++)
            {
                if (rides[i] != null && rides[i].gameObject.name == targetRideObjectName)
                    return rides[i];
            }
        }

        if (rides.Length == 1 && rides[0] != GetComponent<CableZiplineRide>())
            return rides[0];

        return null;
    }

    private static void PlaySoundAt(Vector3 position, AudioClip[] clips, float volume, Vector2 pitchRange)
    {
        if (clips == null || clips.Length == 0)
            return;

        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip == null)
            return;

        GameObject audioObject = new GameObject("ZiplineAttachSound");
        audioObject.transform.position = position;

        AudioSource source = audioObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = volume;
        source.pitch = Random.Range(pitchRange.x, pitchRange.y);
        source.spatialBlend = 1f;
        source.minDistance = 1f;
        source.maxDistance = 25f;
        source.playOnAwake = false;
        source.loop = false;
        source.Play();

        Destroy(audioObject, clip.length / Mathf.Max(0.01f, Mathf.Abs(source.pitch)) + 0.25f);
    }

    private void OnDrawGizmosSelected()
    {
        if (cableAnchor == null)
            return;

        Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.5f);
        Gizmos.DrawWireSphere(cableAnchor.position, attachRadius);
    }
}


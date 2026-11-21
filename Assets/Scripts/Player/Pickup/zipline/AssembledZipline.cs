using UnityEngine;

/// <summary>
/// Собранный зиплайн (префаб): при поднесении к коллайдеру или тросу/точке установки
/// автоматически крепится, проигрывает звук, включает зиплайн на тросе и удаляет предмет из рук.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(PickupObject))]
public class AssembledZipline : MonoBehaviour
{
    [Header("Mount / Attach Trigger")]
    [Tooltip("Коллайдер точки крепления на тросе/столбе (опционально).")]
    [SerializeField] private Collider mountTargetCollider;
    [Tooltip("Радиус срабатывания при поднесении к точке крепления или линии троса (м).")]
    [SerializeField, Min(0.1f)] private float attachRadius = 2.0f;
    [Tooltip("Необязательно: точное имя объекта CableZiplineRide в сцене. Если пусто — берётся первый/ближайший CableZiplineRide.")]
    [SerializeField] private string targetRideObjectName = "";

    [Header("Attach Sound")]
    [SerializeField] private AudioClip[] attachClips;
    [SerializeField, Range(0f, 1f)] private float attachVolume = 0.9f;
    [SerializeField] private Vector2 attachPitchRange = new Vector2(0.96f, 1.04f);

    private PickupObject pickupObject;
    private Collider[] ownColliders;
    private bool isAttached;

    private void Awake()
    {
        pickupObject = GetComponent<PickupObject>();
        ownColliders = GetComponentsInChildren<Collider>(true);
    }

    private void Update()
    {
        if (isAttached)
            return;

        CableZiplineRide targetRide = FindTargetRide();
        if (targetRide == null)
            return;

        if (IsNearMountTarget(targetRide))
        {
            AttachToCable(targetRide);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryAttachFromCollider(collision != null ? collision.collider : null);
    }

    private void OnCollisionStay(Collision collision)
    {
        TryAttachFromCollider(collision != null ? collision.collider : null);
    }

    private void OnTriggerEnter(Collider other)
    {
        TryAttachFromCollider(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryAttachFromCollider(other);
    }

    private void TryAttachFromCollider(Collider otherCollider)
    {
        if (isAttached || otherCollider == null)
            return;

        CableZiplineRide targetRide = FindTargetRide();
        if (targetRide == null)
            return;

        if (mountTargetCollider != null &&
            (otherCollider == mountTargetCollider || otherCollider.transform.IsChildOf(mountTargetCollider.transform)))
        {
            AttachToCable(targetRide);
            return;
        }

        if (otherCollider.GetComponentInParent<CableZiplineRide>() == targetRide)
        {
            AttachToCable(targetRide);
        }
    }

    private CableZiplineRide FindTargetRide()
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

        if (rides.Length == 1)
            return rides[0];

        CableZiplineRide nearest = null;
        float bestDistSqr = float.MaxValue;

        for (int i = 0; i < rides.Length; i++)
        {
            CableZiplineRide ride = rides[i];
            if (ride == null) continue;

            float dSqr = (ride.MountWorldPosition - transform.position).sqrMagnitude;
            if (dSqr < bestDistSqr)
            {
                bestDistSqr = dSqr;
                nearest = ride;
            }
        }

        return nearest;
    }

    private Vector3 GetClosestSelfPoint(Vector3 referencePoint)
    {
        Vector3 best = transform.position;
        float bestSqr = (best - referencePoint).sqrMagnitude;

        if (ownColliders != null && ownColliders.Length > 0)
        {
            for (int i = 0; i < ownColliders.Length; i++)
            {
                Collider col = ownColliders[i];
                if (col == null || !col.enabled || !col.gameObject.activeInHierarchy) continue;

                if (col is MeshCollider mc && !mc.convex)
                    continue;

                Vector3 closest = col.ClosestPoint(referencePoint);
                float sqr = (closest - referencePoint).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = closest;
                }
            }
        }

        return best;
    }

    private bool IsNearMountTarget(CableZiplineRide targetRide)
    {
        float radiusSqr = attachRadius * attachRadius;

        if (mountTargetCollider != null && mountTargetCollider.enabled && mountTargetCollider.gameObject.activeInHierarchy)
        {
            Vector3 selfPt = GetClosestSelfPoint(mountTargetCollider.bounds.center);
            Vector3 targetPt = mountTargetCollider.ClosestPoint(selfPt);
            if ((targetPt - selfPt).sqrMagnitude <= radiusSqr)
                return true;
        }

        Vector3 mountPos = targetRide.MountWorldPosition;
        Vector3 fromSelf = GetClosestSelfPoint(mountPos);
        if ((mountPos - fromSelf).sqrMagnitude <= radiusSqr)
            return true;

        // Проверка по горизонтали (X, Z) к тросу/столбу на любой высоте Y троса.
        Vector3 ridePos = targetRide.transform.position;
        float clampedY = Mathf.Clamp(transform.position.y, targetRide.MinY, targetRide.MaxY);
        Vector3 closestOnLine = new Vector3(ridePos.x, clampedY, ridePos.z);
        Vector3 fromLineSelf = GetClosestSelfPoint(closestOnLine);
        if ((closestOnLine - fromLineSelf).sqrMagnitude <= radiusSqr)
            return true;

        // Запасная проверка по горизонтальной дистанции XZ до оси троса, если игрок тянется снизу.
        Vector2 selfXZ = new Vector2(fromLineSelf.x, fromLineSelf.z);
        Vector2 poleXZ = new Vector2(ridePos.x, ridePos.z);
        return (selfXZ - poleXZ).sqrMagnitude <= (attachRadius * 0.65f) * (attachRadius * 0.65f);
    }

    private void AttachToCable(CableZiplineRide targetRide)
    {
        if (isAttached || targetRide == null)
            return;

        isAttached = true;

        ReleaseFromHands();
        PlaySoundAt(targetRide.MountWorldPosition, attachClips, attachVolume, attachPitchRange);

        targetRide.MarkAsInstalled();
        targetRide.gameObject.SetActive(true);
        targetRide.ActivateOnCable();

        Debug.Log($"[AssembledZipline] Установлен на '{targetRide.gameObject.name}' в позиции {targetRide.transform.position}", targetRide);

        Destroy(gameObject);
    }

    private void ReleaseFromHands()
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

    private static void PlaySoundAt(Vector3 position, AudioClip[] clips, float volume, Vector2 pitchRange)
    {
        if (clips == null || clips.Length == 0)
            return;

        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip == null)
            return;

        GameObject audioObj = new GameObject("ZiplineAttachSound");
        audioObj.transform.position = position;

        AudioSource source = audioObj.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = volume;
        source.pitch = Random.Range(pitchRange.x, pitchRange.y);
        source.spatialBlend = 1f;
        source.minDistance = 1f;
        source.maxDistance = 25f;
        source.playOnAwake = false;
        source.loop = false;
        source.Play();

        Destroy(audioObj, clip.length / Mathf.Max(0.01f, Mathf.Abs(source.pitch)) + 0.25f);
    }
}
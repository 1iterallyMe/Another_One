using UnityEngine;

public enum ZiplinePartRole
{
    PartA,
    PartB
}

/// <summary>
/// Часть будущей ручки зиплайна. Когда две разные части соприкасаются
/// (или попадают в триггерные зоны друг друга), они исчезают и на их месте
/// появляется собранная ручка <see cref="assembledZiplinePrefab"/>.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(PickupObject))]
public class ZiplinePartCraft : MonoBehaviour
{
    [Header("Part Role")]
    [SerializeField] private ZiplinePartRole role = ZiplinePartRole.PartA;
    [Tooltip("Разрешить собирать эту часть с любой другой частью, а не только с противоположной ролью.")]
    [SerializeField] private bool allowAnyOtherPart = false;

    [Header("Assembled Result")]
    [Tooltip("Готовая ручка зиплайна (с Rigidbody, PickupObject и AssembledZipline).")]
    [SerializeField] private GameObject assembledZiplinePrefab;
    [Tooltip("Смещение точки появления готовой ручки относительно места контакта.")]
    [SerializeField] private Vector3 spawnOffset = Vector3.zero;
    [Tooltip("Если одна из частей была в руках — сразу взять готовую ручку в руки.")]
    [SerializeField] private bool equipInHandsWhenHeld = true;

    [Header("Safe Spawn")]
    [SerializeField, Min(0.05f)] private float safeCheckRadius = 0.35f;
    [SerializeField] private LayerMask obstacleMask = ~0;
    [SerializeField, Min(1)] private int maxPushIterations = 8;
    [SerializeField, Min(0.01f)] private float pushStep = 0.12f;

    [Header("Craft Sound & Effect")]
    [SerializeField] private AudioClip[] craftClips;
    [SerializeField, Range(0f, 1f)] private float craftVolume = 0.9f;
    [SerializeField] private Vector2 craftPitchRange = new Vector2(0.95f, 1.05f);
    [SerializeField] private GameObject craftEffectPrefab;
    [SerializeField] private float craftEffectLifetime = 2f;

    private PickupObject pickupObject;
    private bool isConsumed;

    public ZiplinePartRole Role => role;
    public bool IsConsumed => isConsumed;

    private void Awake()
    {
        pickupObject = GetComponent<PickupObject>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision == null || collision.collider == null)
            return;

        TryCraft(collision.collider, GetContactPoint(collision));
    }

    private void OnCollisionStay(Collision collision)
    {
        if (collision == null || collision.collider == null)
            return;

        TryCraft(collision.collider, GetContactPoint(collision));
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null)
            return;

        TryCraft(other, other.ClosestPoint(transform.position));
    }

    private void OnTriggerStay(Collider other)
    {
        if (other == null)
            return;

        TryCraft(other, other.ClosestPoint(transform.position));
    }

    private Vector3 GetContactPoint(Collision collision)
    {
        return collision.contactCount > 0
            ? collision.GetContact(0).point
            : (transform.position + collision.collider.bounds.center) * 0.5f;
    }

    private void TryCraft(Collider otherCollider, Vector3 contactPoint)
    {
        if (isConsumed || otherCollider == null)
            return;

        ZiplinePartCraft other = otherCollider.GetComponentInParent<ZiplinePartCraft>();
        if (other == null || other == this || other.isConsumed)
            return;

        if (!CanPairWith(other))
            return;

        // Из двух одновременных событий крафт запускает только часть с меньшим InstanceID.
        if (GetInstanceID() > other.GetInstanceID())
            return;

        CompleteCraft(other, contactPoint);
    }

    private bool CanPairWith(ZiplinePartCraft other)
    {
        if (allowAnyOtherPart || other.allowAnyOtherPart)
            return true;

        return role != other.role;
    }

    private void CompleteCraft(ZiplinePartCraft other, Vector3 contactPoint)
    {
        isConsumed = true;
        other.isConsumed = true;

        PhysicsHoldController holdController = FindFirstObjectByType<PhysicsHoldController>();
        bool wasHeldByController = IsHeldByController(holdController, this) || IsHeldByController(holdController, other);
        bool wasHeldDirectly = (pickupObject != null && pickupObject.IsHeld) ||
                               (other.pickupObject != null && other.pickupObject.IsHeld);
        bool shouldEquipInHands = equipInHandsWhenHeld && (wasHeldByController || wasHeldDirectly);

        Vector3 spawnPosition;
        Quaternion spawnRotation;

        if (shouldEquipInHands && holdController != null)
        {
            spawnPosition = holdController.TargetPosition;
            spawnRotation = holdController.TargetRotation;
        }
        else
        {
            spawnPosition = FindSafeSpawnPosition(contactPoint + spawnOffset, other);
            spawnRotation = transform.rotation;
        }

        ReleasePart(this, other, holdController);
        PlayCraftSound(spawnPosition);

        if (craftEffectPrefab != null)
        {
            GameObject effect = Instantiate(craftEffectPrefab, spawnPosition, spawnRotation);
            if (craftEffectLifetime > 0f)
                Destroy(effect, craftEffectLifetime);
        }

        GameObject assembledInstance = null;

        if (assembledZiplinePrefab != null)
        {
            assembledInstance = Instantiate(assembledZiplinePrefab, spawnPosition, spawnRotation);
            assembledInstance.name = assembledZiplinePrefab.name;
        }
        else
        {
            Debug.LogWarning($"[{nameof(ZiplinePartCraft)}] Не назначен Assembled Zipline Prefab у '{name}'.", this);
        }

        if (shouldEquipInHands && holdController != null && assembledInstance != null)
        {
            PickupObject assembledPickup = assembledInstance.GetComponent<PickupObject>();
            if (assembledPickup != null && !assembledPickup.IsHeavy)
                holdController.BeginHold(assembledPickup);
        }

        Destroy(other.gameObject);
        Destroy(gameObject);
    }

    private static bool IsHeldByController(PhysicsHoldController holdController, ZiplinePartCraft part)
    {
        if (holdController == null || !holdController.IsHolding || part == null || part.pickupObject == null)
            return false;

        return ReferenceEquals(holdController.Held, part.pickupObject);
    }

    private static void ReleasePart(ZiplinePartCraft first, ZiplinePartCraft second, PhysicsHoldController holdController)
    {
        if (holdController != null && holdController.IsHolding)
        {
            if (ReferenceEquals(holdController.Held, first.pickupObject) ||
                ReferenceEquals(holdController.Held, second.pickupObject))
            {
                holdController.Release(false);
            }
        }

        if (first != null && first.pickupObject != null && first.pickupObject.IsHeld)
            first.pickupObject.EndHold();

        if (second != null && second.pickupObject != null && second.pickupObject.IsHeld)
            second.pickupObject.EndHold();
    }


    private Vector3 FindSafeSpawnPosition(Vector3 desiredPosition, ZiplinePartCraft other)
    {
        Vector3 candidate = desiredPosition;
        Collider[] overlaps = new Collider[16];

        for (int iteration = 0; iteration < maxPushIterations; iteration++)
        {
            int count = Physics.OverlapSphereNonAlloc(
                candidate,
                safeCheckRadius,
                overlaps,
                obstacleMask,
                QueryTriggerInteraction.Ignore);

            Vector3 pushDirection = Vector3.zero;
            bool foundObstacle = false;

            for (int i = 0; i < count; i++)
            {
                Collider collider = overlaps[i];
                if (collider == null) continue;
                if (collider.transform.IsChildOf(transform)) continue;
                if (other != null && collider.transform.IsChildOf(other.transform)) continue;
                if (collider.GetComponentInParent<FPSController>() != null) continue;

                foundObstacle = true;
                Vector3 closestPoint = collider.ClosestPoint(candidate);
                Vector3 away = candidate - closestPoint;

                if (away.sqrMagnitude > 0.00001f)
                {
                    pushDirection += away.normalized;
                }
                else
                {
                    Vector3 centerAway = candidate - collider.bounds.center;
                    pushDirection += centerAway.sqrMagnitude > 0.00001f ? centerAway.normalized : Vector3.up;
                }
            }

            if (!foundObstacle)
                break;

            if (pushDirection.sqrMagnitude < 0.00001f)
                pushDirection = Vector3.up;
            else
                pushDirection.Normalize();

            candidate += pushDirection * pushStep;
        }

        return candidate;
    }

    private void PlayCraftSound(Vector3 position)
    {
        if (craftClips == null || craftClips.Length == 0)
            return;

        AudioClip clip = craftClips[Random.Range(0, craftClips.Length)];
        if (clip == null)
            return;

        GameObject tempAudioObject = new GameObject("ZiplineCraftSound");
        tempAudioObject.transform.position = position;

        AudioSource source = tempAudioObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = craftVolume;
        source.pitch = Random.Range(craftPitchRange.x, craftPitchRange.y);
        source.spatialBlend = 1f;
        source.minDistance = 1f;
        source.maxDistance = 25f;
        source.playOnAwake = false;
        source.loop = false;
        source.Play();

        Destroy(tempAudioObject, clip.length / Mathf.Max(0.01f, Mathf.Abs(source.pitch)) + 0.2f);
    }
}


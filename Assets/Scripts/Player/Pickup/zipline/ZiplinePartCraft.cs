using UnityEngine;

public enum ZiplinePartRole
{
    PartA,
    PartB
}

/// <summary>
/// —оедин€ет две детали зиплайна при столкновении коллайдеров (в том числе если одна в руках).
/// Ќа месте двух деталей по€вл€етс€ собранный зиплайн, проигрываетс€ звук,
/// а при удержании детали новый зиплайн сразу оказываетс€ в руках.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(PickupObject))]
public class ZiplinePartCraft : MonoBehaviour
{
    [Header("Part Role")]
    [SerializeField] private ZiplinePartRole role = ZiplinePartRole.PartA;
    [Tooltip("≈сли включено Ч может соединитьс€ с любой другой деталью ZiplinePartCraft, не только с противоположной ролью.")]
    [SerializeField] private bool allowAnyOtherPart = false;

    [Header("Assembled Result")]
    [Tooltip("ѕрефаб готового зиплайна (с Rigidbody, PickupObject и Outline).")]
    [SerializeField] private GameObject assembledZiplinePrefab;
    [Tooltip("—мещение точки по€влени€ собранного зиплайна относительно места столкновени€ (когда спавн не в руках).")]
    [SerializeField] private Vector3 spawnOffset = Vector3.zero;
    [Tooltip("—разу брать готовый зиплайн в руки, если одна из деталей в момент соединени€ была в руках.")]
    [SerializeField] private bool equipInHandsWhenHeld = true;

    [Header("Safe Spawn (защита от застревани€ в стенах)")]
    [Tooltip("–адиус проверки свободного места при по€влении зиплайна в мире.")]
    [SerializeField, Min(0.05f)] private float safeCheckRadius = 0.35f;
    [Tooltip("—лои стен/пола/преп€тствий, внутри которых не должен по€вл€тьс€ зиплайн.")]
    [SerializeField] private LayerMask obstacleMask = ~0;
    [Tooltip("ћаксимальное число попыток сдвинуть точку спавна в свободное место.")]
    [SerializeField, Min(1)] private int maxPushIterations = 8;
    [Tooltip("Ўаг сдвига от преп€тстви€ за одну итерацию (м).")]
    [SerializeField, Min(0.01f)] private float pushStep = 0.12f;

    [Header("Craft Sound & Effect")]
    [Tooltip("«вуки соединени€ деталей. ¬ыбираетс€ случайный клип.")]
    [SerializeField] private AudioClip[] craftClips;
    [SerializeField, Range(0f, 1f)] private float craftVolume = 0.9f;
    [SerializeField] private Vector2 craftPitchRange = new Vector2(0.95f, 1.05f);
    [Tooltip("Ќеоб€зательный эффект (искры/вспышка) при сборке.")]
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
        TryCraftWithCollision(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        TryCraftWithCollision(collision);
    }

    private void TryCraftWithCollision(Collision collision)
    {
        if (isConsumed || collision == null || collision.collider == null)
            return;

        ZiplinePartCraft other = collision.collider.GetComponentInParent<ZiplinePartCraft>();
        if (other == null || other == this || other.isConsumed)
            return;

        if (!CanPairWith(other))
            return;

        // ќба скрипта получают событие столкновени€ в один кадр:
        // оставл€ем выполнение только за объектом с меньшим InstanceID, чтобы не создать два зиплайна.
        if (GetInstanceID() > other.GetInstanceID())
            return;

        CompleteCraft(other, collision);
    }

    private bool CanPairWith(ZiplinePartCraft other)
    {
        if (allowAnyOtherPart || other.allowAnyOtherPart)
            return true;

        return role != other.role;
    }

    private void CompleteCraft(ZiplinePartCraft other, Collision collision)
    {
        isConsumed = true;
        other.isConsumed = true;

        PhysicsHoldController holdController = FindFirstObjectByType<PhysicsHoldController>();
        bool wasHeldByController = IsHeldByController(holdController, this) || IsHeldByController(holdController, other);
        bool wasHeldDirectly = (pickupObject != null && pickupObject.IsHeld) ||
                               (other.pickupObject != null && other.pickupObject.IsHeld);
        bool shouldEquipInHands = equipInHandsWhenHeld && (wasHeldByController || wasHeldDirectly);

        Vector3 contactPos = collision.contactCount > 0
            ? collision.GetContact(0).point
            : (transform.position + other.transform.position) * 0.5f;

        Vector3 spawnPos;
        Quaternion spawnRot;

        if (shouldEquipInHands && holdController != null)
        {
            spawnPos = holdController.TargetPosition;
            spawnRot = holdController.TargetRotation;
        }
        else
        {
            spawnPos = FindSafeSpawnPosition(contactPos + spawnOffset, other);
            spawnRot = transform.rotation;
        }

        ReleasePart(this, other, holdController);
        PlayCraftSound(spawnPos);

        if (craftEffectPrefab != null)
        {
            GameObject fx = Instantiate(craftEffectPrefab, spawnPos, spawnRot);
            if (craftEffectLifetime > 0f)
                Destroy(fx, craftEffectLifetime);
        }

        GameObject assembledInstance = null;
        if (assembledZiplinePrefab != null)
        {
            assembledInstance = Instantiate(assembledZiplinePrefab, spawnPos, spawnRot);
        }
        else
        {
            Debug.LogWarning($"[{nameof(ZiplinePartCraft)}] Ќе назначен Assembled Zipline Prefab на '{name}'.", this);
        }

        if (shouldEquipInHands && holdController != null && assembledInstance != null)
        {
            PickupObject assembledPickup = assembledInstance.GetComponent<PickupObject>();
            if (assembledPickup != null && !assembledPickup.IsHeavy)
            {
                holdController.BeginHold(assembledPickup);
            }
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

    private Vector3 FindSafeSpawnPosition(Vector3 desiredPos, ZiplinePartCraft other)
    {
        Vector3 candidate = desiredPos;
        Collider[] overlaps = new Collider[16];

        for (int iter = 0; iter < maxPushIterations; iter++)
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
                Collider col = overlaps[i];
                if (col == null) continue;
                if (col.transform.IsChildOf(transform)) continue;
                if (other != null && col.transform.IsChildOf(other.transform)) continue;
                if (col.GetComponentInParent<FPSController>() != null) continue;

                foundObstacle = true;
                Vector3 closestPoint = col.ClosestPoint(candidate);
                Vector3 away = candidate - closestPoint;

                if (away.sqrMagnitude > 0.00001f)
                {
                    pushDirection += away.normalized;
                }
                else
                {
                    Vector3 centerAway = candidate - col.bounds.center;
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

        GameObject tempAudioObj = new GameObject("ZiplineCraftSound");
        tempAudioObj.transform.position = position;

        AudioSource source = tempAudioObj.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = craftVolume;
        source.pitch = Random.Range(craftPitchRange.x, craftPitchRange.y);
        source.spatialBlend = 1f;
        source.minDistance = 1f;
        source.maxDistance = 25f;
        source.playOnAwake = false;
        source.loop = false;
        source.Play();

        Destroy(tempAudioObj, clip.length / Mathf.Max(0.01f, Mathf.Abs(source.pitch)) + 0.2f);
    }
}
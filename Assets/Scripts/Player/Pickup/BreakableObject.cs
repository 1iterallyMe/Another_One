using UnityEngine;

/// <summary>
/// Универсальный компонент разрушения физического предмета.
/// Вешается на тот же объект, что и <see cref="PickupObject"/>.
/// Пока предмет в руках (PickupObject.IsHeld) — он не разбивается.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(PickupObject))]
public class BreakableObject : MonoBehaviour
{
    [Header("Break Settings")]
    [Tooltip("Префаб разбитого объекта: пустой родитель + дочерние осколки с MeshCollider (Convex) и Rigidbody.")]
    [SerializeField] private GameObject brokenPrefab;
    [Tooltip("Порог силы удара (м/с), при котором объект разбивается. Должен быть выше minImpactSpeed у PickupObject.")]
    [SerializeField] private float breakSpeedThreshold = 8f;
    [Tooltip("Сила разлёта осколков от точки удара.")]
    [SerializeField] private float explosionForce = 150f;
    [Tooltip("Радиус взрывной волны.")]
    [SerializeField] private float explosionRadius = 2f;
    [Tooltip("Через сколько секунд удалять осколки для оптимизации.")]
    [SerializeField] private float shardLifetime = 5f;

    [Header("Break Sound (разбитие)")]
    [Tooltip("Звуки разбития. Каждый раз играет случайный клип.")]
    [SerializeField] private AudioClip[] breakClips;
    [Tooltip("Громкость звука разбития.")]
    [SerializeField, Range(0f, 1f)] private float breakVolume = 1f;

    public bool IsBroken { get; private set; }

    private Rigidbody rb;
    private PickupObject pickupObject;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        pickupObject = GetComponent<PickupObject>();
    }

    void OnCollisionEnter(Collision collision) => HandleCollision(collision);
    void OnCollisionStay(Collision collision) => HandleCollision(collision);

    void HandleCollision(Collision collision)
    {
        if (IsBroken) return;
        if (pickupObject != null && pickupObject.IsHeld) return; // в руках не разбиваем

        // Сила удара считается так же, как в PickupObject.HandleImpact,
        // чтобы порог разбития был сопоставим с громкостью звука удара.
        float impulseSpeed = collision.impulse.magnitude / Mathf.Max(rb.mass, 0.0001f);
        float relativeSpeed = collision.relativeVelocity.magnitude;
        float impactSpeed = Mathf.Max(impulseSpeed, relativeSpeed);

        if (impactSpeed >= breakSpeedThreshold) Break(collision);
    }

    /// <summary>Принудительно разбить объект (выстрел, внешний скрипт и т.п.), даже если он в руках.</summary>
    public void ForceBreak() => Break(null);

    void Break(Collision collision)
    {
        if (IsBroken) return;
        IsBroken = true;

        // Звук играем в мировой точке через PlayClipAtPoint: исходный объект будет уничтожен.
        PlayBreakSound(transform.position);

        Vector3 hitPoint = collision != null && collision.contactCount > 0
            ? collision.GetContact(0).point
            : transform.position;

        // Запоминаем инерцию целого объекта до его уничтожения.
        Vector3 inheritedVelocity = rb.linearVelocity;
        Vector3 inheritedAngularVelocity = rb.angularVelocity;

        if (brokenPrefab != null)
        {
            GameObject brokenInstance = Instantiate(brokenPrefab, transform.position, transform.rotation);

            foreach (Rigidbody pieceRb in brokenInstance.GetComponentsInChildren<Rigidbody>())
            {
                // Осколки продолжают движение целого объекта...
                pieceRb.linearVelocity = inheritedVelocity;
                pieceRb.angularVelocity = inheritedAngularVelocity;
                // ...и разлетаются от точки удара.
                pieceRb.AddExplosionForce(explosionForce, hitPoint, explosionRadius);
            }

            Destroy(brokenInstance, Mathf.Max(0f, shardLifetime));
        }
        else
        {
            Debug.LogWarning($"[{nameof(BreakableObject)}] На объекте '{name}' не назначен brokenPrefab — осколки не созданы.", this);
        }

        // Сбрасываем удержание и уничтожаем целый объект.
        // Новая ваза создаётся кнопкой (WallLiftSwitch) в указанной там точке.
        if (pickupObject != null) pickupObject.EndHold();

        Destroy(gameObject);
    }

    void PlayBreakSound(Vector3 position)
    {
        if (breakClips == null || breakClips.Length == 0) return;

        AudioClip clip = breakClips[Random.Range(0, breakClips.Length)];
        if (clip == null) return;

        AudioSource.PlayClipAtPoint(clip, position, breakVolume);
    }
}

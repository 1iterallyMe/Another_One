using GogoGaga.OptimizedRopesAndCables;
using UnityEngine;

/// <summary>
/// Провод-предмет на системе OptimizedRopesAndCables.
/// Игрок берёт его в руки, подключает один конец к розетке генератора,
/// затем второй — к розетке зиплайна. Когда подключены оба конца,
/// FuelGenerator считает кабель подключённым (HasPower).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(PickupObject))]
public class PowerCableItem : MonoBehaviour
{
    [Header("Rope & Ends")]
    [Tooltip("Компонент Rope, который рисует сам провод. Отключается: линию ведёт PhysicalCable.")]
    [SerializeField] private Rope rope;
    [Tooltip("Физическая симуляция провода с коллизиями пола/стен.")]
    [SerializeField] private PhysicalCable physicalCable;
    [Tooltip("Свободный конец провода, пока он не подключён к генератору.")]
    [SerializeField] private Transform looseStartAnchor;
    [Tooltip("Конец-вилка, который игрок держит и вставляет в розетку зиплайна.")]
    [SerializeField] private Transform plugAnchor;

    [Header("Power")]
    [SerializeField] private FuelGenerator generator;
    [Tooltip("Максимальная длина провода, м.")]
    [SerializeField, Min(0.5f)] private float maxLength = 12f;

    private Rigidbody rb;
    private PickupObject pickup;
    private Collider itemCollider;

    private PowerCableSocket generatorSocket;
    private PowerCableSocket ziplineSocket;

    private PowerCableSocket.PlugPose generatorPlugPose;
    private PowerCableSocket.PlugPose ziplinePlugPose;

    private bool generatorEndAttached;
    private bool ziplineEndAttached;

    public bool GeneratorEndAttached => generatorEndAttached;
    public bool ZiplineEndAttached => ziplineEndAttached;
    public bool IsFullyConnected => generatorEndAttached && ziplineEndAttached;
    public FuelGenerator Generator => generator;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        pickup = GetComponent<PickupObject>();
        itemCollider = GetComponent<Collider>();

        if (rope == null) rope = GetComponentInChildren<Rope>(true);
        if (physicalCable == null) physicalCable = GetComponentInChildren<PhysicalCable>(true);
        if (generator == null) generator = FindFirstObjectByType<FuelGenerator>();

        // Rope only draws a bezier without collisions. The cable is driven by
        // PhysicalCable instead, so the old spline update must be disabled.
        if (rope != null)
            rope.enabled = false;

        if (physicalCable != null)
        {
            // Before the ends are connected the cable hangs between the loose
            // end and the plug, both of which are part of this item.
            physicalCable.SetEndpoints(
                looseStartAnchor != null ? looseStartAnchor : transform,
                plugAnchor != null ? plugAnchor : transform);
            physicalCable.SetLength(maxLength);
        }
    }

    /// <summary>Подключить свободный конец к розетке (генератор или зиплайн).</summary>
    public bool AttachTo(PowerCableSocket socket)
    {
        if (socket == null) return false;

        if (socket.SocketType == PowerCableSocketType.Generator)
            return AttachGeneratorEnd(socket);

        return AttachZiplineEnd(socket);
    }

    private bool AttachGeneratorEnd(PowerCableSocket socket)
    {
        if (generatorEndAttached) return false;

        Transform end = looseStartAnchor != null ? looseStartAnchor : transform;

        generatorSocket = socket;
        generatorEndAttached = true;

        // Вставляем в розетку саму модель штекера (Plug_0) вместе с концом троса.
        generatorPlugPose = socket.AttachPlug(end);

        if (physicalCable != null)
            physicalCable.SetEndA(end);

        RefreshPowerState();
        return true;
    }

    private bool AttachZiplineEnd(PowerCableSocket socket)
    {
        if (ziplineEndAttached) return false;

        Transform end = plugAnchor != null ? plugAnchor : transform;

        ziplineSocket = socket;
        ziplineEndAttached = true;

        // Вставляем вилку (Plug_1) в розетку зиплайна.
        ziplinePlugPose = socket.AttachPlug(end);

        if (physicalCable != null)
            physicalCable.SetEndB(end);

        // Провод зафиксирован. Корень провода — невидимый держатель троса,
        // поэтому его НЕ телепортируем в SnapPoint: штекер уже стоит в розетке.
        ReleaseFromHands();

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        if (pickup != null) pickup.enabled = false;
        if (itemCollider != null) itemCollider.enabled = false;

        RefreshPowerState();
        return true;
    }

    /// <summary>
    /// Вынуть провод из обеих розеток и вернуть штекеры на место.
    /// Сейчас не вызывается автоматически, но нужен для отключения/сброса.
    /// </summary>
    public void DetachAll()
    {
        if (generatorSocket != null && generatorPlugPose.isValid)
            generatorSocket.DetachPlug(looseStartAnchor != null ? looseStartAnchor : transform, generatorPlugPose);

        if (ziplineSocket != null && ziplinePlugPose.isValid)
            ziplineSocket.DetachPlug(plugAnchor != null ? plugAnchor : transform, ziplinePlugPose);

        generatorPlugPose = default(PowerCableSocket.PlugPose);
        ziplinePlugPose = default(PowerCableSocket.PlugPose);
        generatorSocket = null;
        ziplineSocket = null;
        generatorEndAttached = false;
        ziplineEndAttached = false;

        if (pickup != null) pickup.enabled = true;
        if (itemCollider != null) itemCollider.enabled = true;

        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
        }

        if (physicalCable != null)
        {
            physicalCable.SetEndpoints(
                looseStartAnchor != null ? looseStartAnchor : transform,
                plugAnchor != null ? plugAnchor : transform);
            physicalCable.Rebuild();
        }

        RefreshPowerState();
    }

    private void RefreshPowerState()
    {
        if (generator == null) return;
        generator.SetCableConnected(IsFullyConnected);
    }

    /// <summary>
    /// Страховка от рассинхрона: флаг на генераторе мог быть сброшен другим
    /// кодом/порядком инициализации, хотя провод физически подключён с двух
    /// сторон. SetCableConnected сам выходит, если значение не изменилось,
    /// поэтому вызов каждый кадр ничего не стоит.
    /// </summary>
    private void Update()
    {
        if (generator == null) return;

        if (generator.IsCableConnected != IsFullyConnected)
            generator.SetCableConnected(IsFullyConnected);
    }

    private void OnDestroy()
    {
        if (generator != null) generator.SetCableConnected(false);
    }

    private void ReleaseFromHands()
    {
        PhysicsHoldController hold = FindFirstObjectByType<PhysicsHoldController>();
        if (hold != null && hold.IsHolding && ReferenceEquals(hold.Held, pickup))
            hold.Release(false);

        if (pickup != null && pickup.IsHeld) pickup.EndHold();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        if (looseStartAnchor != null) Gizmos.DrawWireSphere(looseStartAnchor.position, 0.05f);
        if (plugAnchor != null) Gizmos.DrawWireSphere(plugAnchor.position, 0.05f);
    }
}

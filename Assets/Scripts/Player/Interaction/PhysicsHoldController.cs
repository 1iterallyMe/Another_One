using System;
using UnityEngine;

[DisallowMultipleComponent]
public class PhysicsHoldController : MonoBehaviour
{
    [Header("Hold Point")]
    [Tooltip("Камера, от которой считается точка удержания.")]
    [SerializeField] private Camera playerCamera;
    [Tooltip("Смещение точки удержания относительно камеры (X — вправо, Y — вверх/вниз, Z — вперёд).")]
    public Vector3 holdOffset = new Vector3(0.4f, -0.2f, 2f);
    [Tooltip("Смещение точки удержания для тяжёлых предметов: они тащатся ниже, у земли.")]
    public Vector3 heavyHoldOffset = new Vector3(0.3f, -1.1f, 1.6f);
    [Tooltip("Если предмет отстал от точки удержания дальше этого расстояния — удержание срывается.")]
    [SerializeField, Min(0f)] private float breakDistance = 4.5f;
    public float throwForce = 4f;

    public IPhysicsHoldable Held { get; private set; }

    public bool IsHolding
    {
        get
        {
            if (ReferenceEquals(Held, null)) return false;
            if (Held is UnityEngine.Object held && held == null) return false;
            return true;
        }
    }

    /// <summary>Масса удерживаемого предмета (кг) или 0, если ничего не держим.</summary>
    public float HeldMass => IsHolding ? Held.Mass : 0f;

    /// <summary>Игрок сейчас тащит тяжёлый предмет по земле.</summary>
    public bool IsDraggingHeavy => IsHolding && Held.IsHeavy;

    /// <summary>Множитель скорости игрока при удержании (1 = не замедляет).</summary>
    public float PlayerSpeedMultiplier => IsHolding ? Held.PlayerSpeedMultiplier : 1f;

    void Awake()
    {
        if (!playerCamera) playerCamera = GetComponentInChildren<Camera>();
    }

    void FixedUpdate()
    {
        if (!IsHolding) return;

        // Не даём игроку «оторваться» от тяжёлого предмета и тащить его с дистанции.
        if (IsDraggingHeavy && breakDistance > 0f && TryGetHeldPosition(out Vector3 heldPosition) &&
            Vector3.Distance(heldPosition, TargetPosition) > breakDistance)
        {
            Release();
            return;
        }

        Held.SetHoldTarget(TargetPosition, TargetRotation);
    }

    private bool TryGetHeldPosition(out Vector3 position)
    {
        position = default;
        if (Held is not MonoBehaviour component || component == null) return false;

        position = component.transform.position;
        return true;
    }

    /// <summary>Мировая точка, куда летит предмет (для тяжёлых — ниже, у земли).</summary>
    public Vector3 TargetPosition
    {
        get
        {
            Transform origin = playerCamera != null ? playerCamera.transform : transform;

            if (IsHolding && Held.IsHeavy)
            {
                // Для перетаскивания берём горизонтальное направление тела,
                // чтобы наклон камеры вверх/вниз не менял точку тяги.
                Vector3 forward = transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
                forward.Normalize();

                Vector3 right = Vector3.Cross(Vector3.up, forward);
                return origin.position
                       + right * heavyHoldOffset.x
                       + Vector3.up * heavyHoldOffset.y
                       + forward * heavyHoldOffset.z;
            }

            return origin.TransformPoint(holdOffset);
        }
    }

    /// <summary>Целевой поворот предмета.</summary>
    public Quaternion TargetRotation =>
        playerCamera != null ? playerCamera.transform.rotation : transform.rotation;

    public event Action<IPhysicsHoldable> HoldStarted;
    public event Action<IPhysicsHoldable> HoldEnded;

    public void BeginHold(IPhysicsHoldable target)
    {
        if (target == null || target.IsHeld) return;
        if (IsHolding) Release();

        Held = target;
        Held.BeginHold(TargetPosition, TargetRotation);
        HoldStarted?.Invoke(Held);
    }

    public void Release(bool throwIt = false)
    {
        if (!IsHolding) return;
        var released = Held;
        Held = null;

        if (released is UnityEngine.Object o && o == null)
        {
            HoldEnded?.Invoke(null);
            return;
        }

        released.EndHold();
        HoldEnded?.Invoke(released);
    }
}

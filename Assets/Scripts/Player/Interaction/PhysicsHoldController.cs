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

    void Awake()
    {
        if (!playerCamera) playerCamera = GetComponentInChildren<Camera>();
    }

    void FixedUpdate()
    {
        if (IsHolding) Held.SetHoldTarget(TargetPosition, TargetRotation);
    }

    /// <summary>Мировая точка, куда летит предмет (с учётом holdOffset).</summary>
    public Vector3 TargetPosition =>
        playerCamera != null
            ? playerCamera.transform.TransformPoint(holdOffset)
            : transform.TransformPoint(holdOffset);

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

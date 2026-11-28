using System;
using System.Collections.Generic;
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
    [Tooltip("Скорость броска предмета вперёд по клавише F (м/с).")]
    public float throwForce = 9f;

    [Header("Rotate Held Item (ПКМ)")]
    [Tooltip("Градусы вращения на пиксель движения мыши. Уменьши значение, если предмет крутится слишком быстро.")]
    [SerializeField, Min(0f)] private float rotateSensitivity = 0.25f;
    [Tooltip("Плавность доворота (сек). 0 — предмет повторяет мышь мгновенно.")]
    [SerializeField, Min(0f)] private float rotateSmoothTime = 0.03f;
    [Tooltip("Инвертировать вертикальное вращение. По умолчанию движение мыши вниз наклоняет предмет к игроку.")]
    [SerializeField] private bool invertRotateY = false;
    [Tooltip("Колесо мыши во время вращения приближает/отдаляет предмет (удобно разглядывать листки).")]
    [SerializeField] private bool scrollAdjustsHoldDistance = true;
    [Tooltip("Насколько колесо мыши сдвигает предмет за один щелчок (м).")]
    [SerializeField, Min(0f)] private float scrollDistanceStep = 0.15f;
    [Tooltip("Насколько близко можно притянуть предмет к камере (м).")]
    [SerializeField, Min(0f)] private float maxPullCloser = 1.2f;
    [Tooltip("Насколько далеко можно отодвинуть предмет от камеры (м).")]
    [SerializeField, Min(0f)] private float maxPushAway = 1.5f;

    // Накопленный доворот в осях камеры (экранные оси): X — влево/вправо, Y — на себя/от себя.
    private Quaternion rotateOffset = Quaternion.identity;
    private Quaternion targetRotateOffset = Quaternion.identity;
    private float holdDistanceOffset;

    // Пары коллайдеров «игрок ↔ удерживаемый предмет», которым отключены
    // столкновения. Без этого предмет в 1.5 м перед камерой толкает капсулу
    // игрока, и вид трясётся при подходе к стене или к самому предмету.
    private readonly List<Collider> ignoredPlayerColliders = new List<Collider>(1);
    private readonly List<Collider> ignoredItemColliders = new List<Collider>(8);

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

    /// <summary>Игрок прямо сейчас вращает предмет в руке правой кнопкой мыши.</summary>
    public bool IsRotatingHeldItem =>
        IsHolding && !Held.IsHeavy && IsRotateButtonHeld();

    void Awake()
    {
        if (!playerCamera) playerCamera = GetComponentInChildren<Camera>();
    }

    void Update()
    {
        // Доворот и притягивание имеют смысл только пока предмет в руках.
        if (!IsHolding || Held.IsHeavy)
        {
            ResetRotationOffset();
            return;
        }

        if (IsRotateButtonHeld())
        {
            HandleRotateInput();
            HandlePullInput();
        }

        SmoothRotationOffset();
    }

    /// <summary>Крутим предмет в экранных осях: мышь влево/вправо и на себя/от себя.</summary>
    private void HandleRotateInput()
    {
        Vector2 delta = ReadMouseDelta();
        if (delta.sqrMagnitude <= 0f) return;

        // Мышь вниз (на себя) наклоняет верх предмета к игроку, мышь вправо — крутит его вправо.
        float yaw = delta.x * rotateSensitivity;
        float pitch = (invertRotateY ? 1f : -1f) * delta.y * rotateSensitivity;

        Quaternion deltaRotation =
            Quaternion.AngleAxis(pitch, Vector3.right) *
            Quaternion.AngleAxis(yaw, Vector3.up);

        // Домножаем слева: оси вращения всегда остаются экранными, гимбал-лок не возникает,
        // поэтому предмет можно свободно переворачивать «на себя» сколько угодно раз.
        targetRotateOffset = deltaRotation * targetRotateOffset;
    }

    /// <summary>Колесо мыши во время вращения притягивает/отодвигает предмет.</summary>
    private void HandlePullInput()
    {
        if (!scrollAdjustsHoldDistance || (maxPullCloser <= 0f && maxPushAway <= 0f)) return;

        float scroll = ReadMouseScroll();
        if (Mathf.Approximately(scroll, 0f)) return;

        // Колесо вверх — ближе к себе, вниз — дальше. Ограничиваем шаг,
        // чтобы тачпад с плавной прокруткой не «прыгал» через весь диапазон.
        scroll = Mathf.Clamp(scroll, -3f, 3f);
        holdDistanceOffset -= scroll * scrollDistanceStep;
        holdDistanceOffset = Mathf.Clamp(holdDistanceOffset, -maxPullCloser, maxPushAway);
    }

    private void SmoothRotationOffset()
    {
        if (rotateOffset == targetRotateOffset) return;

        if (rotateSmoothTime <= 0f)
        {
            rotateOffset = targetRotateOffset;
            return;
        }

        float t = 1f - Mathf.Exp(-Time.deltaTime / rotateSmoothTime);
        rotateOffset = Quaternion.Slerp(rotateOffset, targetRotateOffset, t);

        // Догоняем достаточно близко — фиксируем, чтобы не копить микроразницу.
        if (Quaternion.Angle(rotateOffset, targetRotateOffset) < 0.01f)
            rotateOffset = targetRotateOffset;
    }

    // Ввод мыши для удержания читаем через legacy Input: в билде при захваченном
    // курсоре Mouse.current.delta даёт рваные значения (предмет дёргается/улетает),
    // а обзор и движение уже работают через legacy — держим один источник.
    private static bool IsRotateButtonHeld() => Input.GetMouseButton(1);

    private static Vector2 ReadMouseDelta()
        => new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));

    private static float ReadMouseScroll() => Input.mouseScrollDelta.y;

    private void ResetRotationOffset()
    {
        rotateOffset = Quaternion.identity;
        targetRotateOffset = Quaternion.identity;
        holdDistanceOffset = 0f;
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

            return origin.TransformPoint(holdOffset + Vector3.forward * holdDistanceOffset);
        }
    }

    /// <summary>Целевой поворот предмета: поворот камеры плюс ручной доворот правой кнопкой мыши.</summary>
    public Quaternion TargetRotation
    {
        get
        {
            Quaternion baseRotation = playerCamera != null ? playerCamera.transform.rotation : transform.rotation;
            return baseRotation * rotateOffset;
        }
    }

    public event Action<IPhysicsHoldable> HoldStarted;
    public event Action<IPhysicsHoldable> HoldEnded;

    public void BeginHold(IPhysicsHoldable target)
    {
        if (target == null || target.IsHeld) return;
        if (IsHolding) Release();

        ResetRotationOffset();
        Held = target;
        Held.BeginHold(TargetPosition, TargetRotation);
        IgnorePlayerCollisions(target);
        HoldStarted?.Invoke(Held);
    }

    /// <summary>
    /// Отключает столкновения между капсулой игрока и предметом в руках.
    /// Иначе предмет отталкивает игрока (и наоборот), и камера дрожит.
    /// </summary>
    private void IgnorePlayerCollisions(IPhysicsHoldable target)
    {
        RestorePlayerCollisions();

        if (target is not MonoBehaviour component || component == null) return;
        if (!TryGetPlayerCollider(out Collider playerCollider)) return;

        ignoredPlayerColliders.Add(playerCollider);

        Collider[] itemColliders = component.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < itemColliders.Length; i++)
        {
            Collider itemCollider = itemColliders[i];
            if (itemCollider == null || itemCollider == playerCollider) continue;

            Physics.IgnoreCollision(playerCollider, itemCollider, true);
            ignoredItemColliders.Add(itemCollider);
        }
    }

    /// <summary>Возвращает столкновения игрока и предмета в исходное состояние.</summary>
    private void RestorePlayerCollisions()
    {
        for (int i = 0; i < ignoredItemColliders.Count; i++)
        {
            Collider itemCollider = ignoredItemColliders[i];
            if (itemCollider == null) continue;

            for (int j = 0; j < ignoredPlayerColliders.Count; j++)
            {
                Collider playerCollider = ignoredPlayerColliders[j];
                if (playerCollider == null) continue;
                Physics.IgnoreCollision(playerCollider, itemCollider, false);
            }
        }

        ignoredItemColliders.Clear();
        ignoredPlayerColliders.Clear();
    }

    private bool TryGetPlayerCollider(out Collider playerCollider)
    {
        playerCollider = GetComponent<CapsuleCollider>();
        if (playerCollider == null) playerCollider = GetComponent<Collider>();
        if (playerCollider == null) playerCollider = GetComponentInChildren<Collider>();

        // Только собственные коллайдеры игрока, а не коллайдеры модели в руках.
        return playerCollider != null && playerCollider.transform.IsChildOf(transform);
    }

    void OnDisable()
    {
        // Зиплайн и другие механики выключают контроллер. Без этого предмет
        // оставался «в руке» навсегда, а следующий BeginHold телепортировал его
        // в мировые координаты (Release → EndHold без цели).
        if (IsHolding) Release();
        RestorePlayerCollisions();
    }

    void OnDestroy()
    {
        RestorePlayerCollisions();
    }

    /// <summary>Направление броска — туда, куда смотрит игрок.</summary>
    public Vector3 ThrowDirection
    {
        get
        {
            Transform origin = playerCamera != null ? playerCamera.transform : transform;
            return origin.forward;
        }
    }

    /// <summary>Бросить удерживаемый предмет вперёд (клавиша F).</summary>
    public void Throw()
    {
        if (!IsHolding) return;

        // Тяжёлый предмет бросить нельзя — его просто отпускаем.
        if (Held.IsHeavy)
        {
            Release();
            return;
        }

        Vector3 velocity = ThrowDirection * throwForce;

        var released = Held;
        Held = null;
        RestorePlayerCollisions();
        ResetRotationOffset();

        if (released is UnityEngine.Object o && o == null)
        {
            HoldEnded?.Invoke(null);
            return;
        }

        released.Throw(velocity);
        HoldEnded?.Invoke(released);
    }

    public void Release(bool throwIt = false)
    {
        // Совместимость со старыми вызовами: Release(true) = бросок.
        if (throwIt)
        {
            Throw();
            return;
        }

        if (!IsHolding) return;
        var released = Held;
        Held = null;
        RestorePlayerCollisions();
        ResetRotationOffset();

        if (released is UnityEngine.Object o && o == null)
        {
            HoldEnded?.Invoke(null);
            return;
        }

        released.EndHold();
        HoldEnded?.Invoke(released);
    }
}

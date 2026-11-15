using UnityEngine;

[DisallowMultipleComponent]
public class HandController : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Camera playerCamera;
    [Tooltip("Кость плеча (Right Arm) — вокруг неё вращается рука. Если пусто — handObject.")]
    public Transform shoulderPivot;
    [Tooltip("Видимая кисть / рука (Right Arm). Если пусто — берётся родитель lightPoint.")]
    [SerializeField] private Transform handObject;
    [Tooltip("Точка крепления предметов инвентаря (дочерний объект руки).")]
    [SerializeField] private Transform lightPoint;
    [SerializeField] private GrabManager inventory;
    [SerializeField] private PhysicsHoldController physicsHold;

    [Header("Physics Reach")]
    [Tooltip("Скорость доворота руки к предмету.")]
    [SerializeField] private float followSmoothTime = 0.08f;
    [Tooltip("Максимальный угол отклонения руки от анимационной позы.")]
    [SerializeField] private float maxReachAngle = 100f;
    [SerializeField] private float rotationLerpSpeed = 12f;

    [Header("Animation Return")]
    [SerializeField] private float returnDuration = 0.15f;

    [Header("Inventory Attach")]
    [SerializeField] private Vector3 inventoryEulerOffset = Vector3.zero;
    [SerializeField] private float attachSmoothTime = 0.06f;

    private enum HandMode { Empty, Inventory, Physics }

    private HandMode mode = HandMode.Empty;
    private Transform physicsTarget;

    private Vector3 smoothedPos;
    private Quaternion smoothedRot;
    private Vector3 attachVelocity;

    private Quaternion lastPhysicsRot;
    private float returnTimer;
    private bool initialized;

    void Awake()
    {
        if (!playerCamera) playerCamera = GetComponentInChildren<Camera>();
        if (!inventory) inventory = GetComponent<GrabManager>();
        if (!physicsHold) physicsHold = GetComponent<PhysicsHoldController>();

        if (lightPoint == null && inventory != null) lightPoint = inventory.lightPoint;
        if (handObject == null && lightPoint != null) handObject = lightPoint.parent;

        if (shoulderPivot == null) shoulderPivot = handObject;
    }

    void OnEnable()
    {
        if (physicsHold != null)
        {
            physicsHold.HoldStarted += HandleHoldStarted;
            physicsHold.HoldEnded += HandleHoldEnded;
        }
        if (inventory != null)
            inventory.EquippedChanged += HandleEquippedChanged;
    }

    void OnDisable()
    {
        if (physicsHold != null)
        {
            physicsHold.HoldStarted -= HandleHoldStarted;
            physicsHold.HoldEnded -= HandleHoldEnded;
        }
        if (inventory != null)
            inventory.EquippedChanged -= HandleEquippedChanged;
    }

    void Start()
    {
        if (physicsHold != null && physicsHold.Held != null) HandleHoldStarted(physicsHold.Held);
        else mode = (inventory != null && inventory.HasEquipped) ? HandMode.Inventory : HandMode.Empty;

        if (handObject != null)
        {
            smoothedPos = handObject.position;
            smoothedRot = handObject.rotation;
            initialized = true;
        }
    }

    void HandleHoldStarted(IPhysicsHoldable held)
    {
        var component = held as MonoBehaviour;
        physicsTarget = component != null ? component.transform : null;
        if (physicsTarget == null) return;

        mode = HandMode.Physics;
        returnTimer = 0f;

        if (shoulderPivot == null) shoulderPivot = handObject;
        if (shoulderPivot != null)
        {
            smoothedRot = shoulderPivot.rotation;
            initialized = true;
        }
    }

    void HandleHoldEnded(IPhysicsHoldable _)
    {
        physicsTarget = null;
        mode = (inventory != null && inventory.HasEquipped) ? HandMode.Inventory : HandMode.Empty;

        if (shoulderPivot != null)
        {
            lastPhysicsRot = shoulderPivot.rotation;
            returnTimer = returnDuration;
        }
    }

    void HandleEquippedChanged(MonoBehaviour item)
    {
        if (mode == HandMode.Physics) return;
        mode = (inventory != null && inventory.HasEquipped) ? HandMode.Inventory : HandMode.Empty;
    }

    void LateUpdate()
    {
        if (shoulderPivot == null) shoulderPivot = handObject;
        if (handObject == null && shoulderPivot == null) return;

        // INVENTORY: предмет уже висит на lightPoint.
        // Если handObject — отдельная кисть (не родитель lightPoint), притягиваем её к lightPoint.
        if (mode == HandMode.Inventory && lightPoint != null && handObject != null && !lightPoint.IsChildOf(handObject))
        {
            if (!initialized) { smoothedPos = handObject.position; smoothedRot = handObject.rotation; initialized = true; }

            Vector3 targetPos = lightPoint.position;
            Quaternion targetRot = lightPoint.rotation * Quaternion.Euler(inventoryEulerOffset);

            smoothedPos = Vector3.SmoothDamp(smoothedPos, targetPos, ref attachVelocity, attachSmoothTime);
            smoothedRot = Quaternion.Slerp(smoothedRot, targetRot, 1f - Mathf.Exp(-rotationLerpSpeed * Time.deltaTime));
            handObject.SetPositionAndRotation(smoothedPos, smoothedRot);
            return;
        }

        if (shoulderPivot == null) return;

        // PHYSICS: вращаем плечо (Right Arm) как шарнир — рука указывает на предмет,
        // не отрываясь от плеча. lightPoint (и предметы) остаются на конце руки.
        if (mode == HandMode.Physics && physicsTarget != null)
        {
            // Цель — точка удержания из PhysicsHoldController (с учётом holdOffset).
            Vector3 aimPos = physicsHold != null ? physicsHold.TargetPosition : physicsTarget.position;

            Vector3 shoulderPos = shoulderPivot.position;
            Vector3 toAim = aimPos - shoulderPos;
            if (toAim.sqrMagnitude < 0.000001f) return;
            Vector3 aimDir = toAim.normalized;

            // Направление самой руки (плечо -> кисть) в локальных осях плеча.
            Vector3 armLocalDir = Vector3.down;
            if (lightPoint != null)
            {
                armLocalDir = shoulderPivot.InverseTransformPoint(lightPoint.position);
                if (armLocalDir.sqrMagnitude < 0.000001f) armLocalDir = Vector3.down;
                armLocalDir.Normalize();
            }

            Quaternion baseRot = shoulderPivot.rotation; // анимационная поза (Animator обновил в Update)

            // LookRotation: «вперёд» смотрит на цель; доворот учитывает реальную ось руки.
            Vector3 up = playerCamera != null ? playerCamera.transform.up : Vector3.up;
            if (Mathf.Abs(Vector3.Dot(aimDir, up)) > 0.999f) up = Vector3.forward;
            Quaternion lookRot = Quaternion.LookRotation(aimDir, up);
            Quaternion armAxisDelta = Quaternion.FromToRotation(Vector3.forward, armLocalDir);
            Quaternion desiredRot = lookRot * Quaternion.Inverse(armAxisDelta);

            // Ограничиваем угол: рука не выворачивается.
            Quaternion clampedRot = Quaternion.RotateTowards(baseRot, desiredRot, maxReachAngle);

            if (!initialized) { smoothedRot = baseRot; initialized = true; }

            float t = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.0001f, followSmoothTime));
            smoothedRot = Quaternion.Slerp(smoothedRot, clampedRot, t);
            shoulderPivot.rotation = smoothedRot;

            lastPhysicsRot = smoothedRot;
            returnTimer = returnDuration;
            return;
        }

        // RETURN: плавно возвращаем плечо к текущей анимации.
        if (returnTimer > 0f)
        {
            returnTimer -= Time.deltaTime;
            float t = 1f - Mathf.Clamp01(returnTimer / Mathf.Max(0.0001f, returnDuration));

            Quaternion animRot = shoulderPivot.rotation; // Animator уже обновил позу в Update
            shoulderPivot.rotation = Quaternion.Slerp(lastPhysicsRot, animRot, t);
        }
        // Иначе — не трогаем Transform: рукой управляет Animator.
    }
}
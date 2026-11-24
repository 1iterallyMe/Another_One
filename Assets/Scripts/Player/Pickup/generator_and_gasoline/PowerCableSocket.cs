using UnityEngine;

public enum PowerCableSocketType
{
    Generator,
    Zipline
}

/// <summary>
/// Розетка для провода-предмета. Игрок держит PowerCableItem, смотрит на розетку
/// и нажимает E: сначала на генераторе, затем на зиплайне.
/// После двух подключений FuelGenerator.HasPower становится доступным.
/// </summary>
[DisallowMultipleComponent]
public class PowerCableSocket : InteractableBase
{
    [Header("Socket")]
    [SerializeField] private PowerCableSocketType socketType = PowerCableSocketType.Generator;
    [SerializeField] private FuelGenerator generator;
    [Tooltip("Точка, в которую встаёт конец провода.")]
    [SerializeField] private Transform snapPoint;

    [Header("Plug Pose (local to Snap Point)")]
    [Tooltip("Позиция модели штекера внутри розетки, в локальных координатах Snap Point. " +
             "Эталон из сцены: Socket_0 → Plug, localPosition (0, 0, -0.1094).")]
    [SerializeField] private Vector3 plugLocalPosition = new Vector3(0f, 0f, -0.1094f);
    [Tooltip("Поворот модели штекера внутри розетки, градусы Эйлера.")]
    [SerializeField] private Vector3 plugLocalEuler = Vector3.zero;
    [Tooltip("Отключать коллайдеры штекера, пока он вставлен: трос не сталкивается с собственной вилкой.")]
    [SerializeField] private bool disablePlugCollidersWhenPlugged = true;

    [Header("Interaction")]
    [SerializeField] private string connectPrompt = "Подключить провод";
    [SerializeField] private string takenPrompt = "Возьмите провод в руки";

    public PowerCableSocketType SocketType => socketType;
    public Transform SnapPoint => snapPoint != null ? snapPoint : transform;
    public FuelGenerator Generator => generator;

    public override string InteractionPrompt => connectPrompt;

    /// <summary>
    /// Состояние конца провода до вставки в розетку. Нужно, чтобы можно было
    /// вернуть штекер обратно в предмет (отключение/сброс).
    /// </summary>
    public struct PlugPose
    {
        public Transform parent;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Collider[] disabledColliders;
        public bool isValid;
    }

    /// <summary>
    /// Ставит конец провода (трансформ, в котором лежит модель штекера) в розетку:
    /// пере-родительствует его в Snap Point и применяет позу вставки.
    /// </summary>
    public PlugPose AttachPlug(Transform endAnchor)
    {
        PlugPose pose = default(PowerCableSocket.PlugPose);
        if (endAnchor == null)
            return pose;

        pose.parent = endAnchor.parent;
        pose.localPosition = endAnchor.localPosition;
        pose.localRotation = endAnchor.localRotation;
        pose.isValid = true;

        Transform snap = SnapPoint;
        endAnchor.SetParent(snap, false);
        endAnchor.localPosition = plugLocalPosition;
        endAnchor.localRotation = Quaternion.Euler(plugLocalEuler);

        if (disablePlugCollidersWhenPlugged)
            pose.disabledColliders = DisableColliders(endAnchor);

        return pose;
    }

    /// <summary>Возвращает конец провода на прежнее место (обратная операция к AttachPlug).</summary>
    public void DetachPlug(Transform endAnchor, PlugPose pose)
    {
        if (endAnchor == null)
            return;

        if (pose.disabledColliders != null)
        {
            for (int i = 0; i < pose.disabledColliders.Length; i++)
            {
                if (pose.disabledColliders[i] != null)
                    pose.disabledColliders[i].enabled = true;
            }
        }

        if (!pose.isValid)
            return;

        endAnchor.SetParent(pose.parent, false);
        endAnchor.localPosition = pose.localPosition;
        endAnchor.localRotation = pose.localRotation;
    }

    private static Collider[] DisableColliders(Transform root)
    {
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        if (colliders == null || colliders.Length == 0)
            return null;

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }

        return colliders;
    }

    public override bool CanInteract(PlayerInteractor interactor)
    {
        if (!base.CanInteract(interactor) || interactor == null) return false;

        PowerCableItem cable = GetHeldCable(interactor);
        if (cable == null) return false;

        if (socketType == PowerCableSocketType.Generator)
            return !cable.GeneratorEndAttached;

        // К зиплайну подключаем только после генератора.
        return cable.GeneratorEndAttached && !cable.ZiplineEndAttached;
    }

    public override void Interact(PlayerInteractor interactor)
    {
        PowerCableItem cable = GetHeldCable(interactor);
        if (cable == null)
        {
            Debug.Log("[PowerCableSocket] " + takenPrompt, this);
            return;
        }

        bool attached = cable.AttachTo(this);
        if (!attached)
        {
            Debug.Log("[PowerCableSocket] Не удалось подключить провод к " + socketType, this);
            return;
        }

        Debug.Log("[PowerCableSocket] Провод подключён к " + socketType + " (" + gameObject.name + ")", this);
    }

    private PowerCableItem GetHeldCable(PlayerInteractor interactor)
    {
        if (interactor == null || interactor.PhysicsHold == null || !interactor.PhysicsHold.IsHolding)
            return null;

        MonoBehaviour held = interactor.PhysicsHold.Held as MonoBehaviour;
        return held != null ? held.GetComponentInParent<PowerCableItem>() : null;
    }

    private void OnDrawGizmosSelected()
    {
        Transform snap = SnapPoint;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(snap.position, 0.03f);
        Gizmos.DrawRay(snap.position, snap.forward * 0.12f);

        // Поза, в которой окажется модель штекера после подключения.
        Gizmos.color = Color.cyan;
        Vector3 plugPosition = snap.TransformPoint(plugLocalPosition);
        Quaternion plugRotation = snap.rotation * Quaternion.Euler(plugLocalEuler);
        Gizmos.matrix = Matrix4x4.TRS(plugPosition, plugRotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(0.094f, 0.094f, 0.133f));
        Gizmos.matrix = Matrix4x4.identity;
    }
}

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

    [Header("Interaction")]
    [SerializeField] private string connectPrompt = "Подключить провод";
    [SerializeField] private string takenPrompt = "Возьмите провод в руки";

    public PowerCableSocketType SocketType => socketType;
    public Transform SnapPoint => snapPoint != null ? snapPoint : transform;
    public FuelGenerator Generator => generator;

    public override string InteractionPrompt => connectPrompt;

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
}

using UnityEngine;

[DisallowMultipleComponent]
public class PlayerInteractor : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private GrabManager inventory;
    [SerializeField] private PhysicsHoldController physicsHold;

    [Header("Raycast")]
    [SerializeField] private float interactDistance = 3f;
    [SerializeField] private float sphereRadius = 0.15f;
    [SerializeField] private LayerMask interactMask = 1 << 6;

    [Header("Input")]
    [SerializeField] private KeyCode interactKey = KeyCode.E;
    [SerializeField] private KeyCode useKey = KeyCode.F;

    public PhysicsHoldController PhysicsHold => physicsHold;
    public IInteractable Focused { get; private set; }
    public event System.Action<IInteractable> FocusChanged;

    private readonly RaycastHit[] hits = new RaycastHit[16];

    void Awake()
    {
        if (!playerCamera) playerCamera = GetComponentInChildren<Camera>();
        if (!inventory) inventory = GetComponent<GrabManager>();
        if (!physicsHold) physicsHold = GetComponent<PhysicsHoldController>();
    }

    void Update()
    {
        // Ленивая страховка: если камера не назначена/потеряна — не роняем кадр.
        if (!playerCamera) playerCamera = GetComponentInChildren<Camera>();

        if (playerCamera == null || Cursor.lockState != CursorLockMode.Locked ||
            (inventory != null && inventory.IsInventoryHidden))
        {
            SetFocus(null);
            return;
        }

        UpdateFocus();

        if (Input.GetKeyDown(interactKey)) Interact();
        if (Input.GetKeyDown(useKey)) UseOrThrow();
    }

    /// <summary>F: бросаем предмет из рук, иначе используем предмет инвентаря.</summary>
    void UseOrThrow()
    {
        if (physicsHold != null && physicsHold.IsHolding)
        {
            physicsHold.Throw();
            return;
        }

        if (inventory != null) inventory.UseEquipped(this);
    }

    void UpdateFocus()
    {
        IInteractable found = null;
        float bestDistance = float.MaxValue;

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        int count = Physics.SphereCastNonAlloc(ray, sphereRadius, hits, interactDistance, interactMask, QueryTriggerInteraction.Collide);

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.distance >= bestDistance) continue;
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (IsHeldCollider(hit.collider)) continue;

            IInteractable candidate = hit.collider.GetComponentInParent<IInteractable>();
            if (candidate == null || !candidate.CanInteract(this)) continue;

            found = candidate;
            bestDistance = hit.distance;
        }

        SetFocus(found);
    }

    bool IsHeldCollider(Collider col)
    {
        if (physicsHold == null || physicsHold.Held == null) return false;
        var held = physicsHold.Held as MonoBehaviour;
        return held != null && col.transform.IsChildOf(held.transform);
    }

    void SetFocus(IInteractable next)
    {
        if (ReferenceEquals(Focused, next)) return;
        Focused?.SetFocused(false);
        Focused = next;
        Focused?.SetFocused(true);
        FocusChanged?.Invoke(Focused);
    }

    void Interact()
    {
        if (Focused == null)
        {
            if (physicsHold != null && physicsHold.IsHolding) physicsHold.Release();
            return;
        }

        if (Focused is IPhysicsHoldable && inventory != null && inventory.HasEquipped)
            inventory.UnequipAll();

        if (Focused is IInventoryItem && physicsHold != null && physicsHold.IsHolding)
            physicsHold.Release();

        Focused.Interact(this);

        if (Focused is Object unityObj && unityObj == null) SetFocus(null);
    }
}

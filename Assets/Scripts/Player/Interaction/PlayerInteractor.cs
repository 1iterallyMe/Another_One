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

        // Пока игрок едет на зиплайне, обычное взаимодействие отключено:
        // иначе E может «зацепить» кнопку/предмет мимо цели.
        if (ZiplineController.ActiveRide != null)
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

        // Луч строго из центра экрана — как прицел. Небольшой радиус сферы
        // прощает неточное наведение на мелкие объекты (кнопки, вентили).
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        int count = Physics.SphereCastNonAlloc(ray, sphereRadius, hits, interactDistance, interactMask, QueryTriggerInteraction.Collide);

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.distance >= bestDistance) continue;
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (IsHeldCollider(hit.collider)) continue;

            IInteractable candidate = ResolveInteractable(hit.collider);
            if (candidate == null) continue;

            found = candidate;
            bestDistance = hit.distance;
        }

        SetFocus(found);
    }

    /// <summary>
    /// Выбирает доступный IInteractable на объекте.
    ///
    /// На одном GameObject может быть несколько реализаций интерфейса: например, у
    /// генератора это GeneratorController (заправка) и PickupObject (поднять).
    /// Раньше брался только первый найденный компонент, и если его CanInteract
    /// возвращал false (нет канистры в руках), объект целиком считался
    /// неинтерактивным — поэтому генератор нельзя было поднять.
    ///
    /// Метод не аллоцирует (два GetComponentInParent), поэтому безопасен для Update.
    /// </summary>
    IInteractable ResolveInteractable(Collider col)
    {
        IInteractable primary = col.GetComponentInParent<IInteractable>();
        if (primary != null && primary.CanInteract(this))
            return primary;

        // Запасной вариант: «поднимаемый» предмет на том же объекте имеет приоритет,
        // если основной интерактор сейчас недоступен.
        IPhysicsHoldable holdable = col.GetComponentInParent<IPhysicsHoldable>();
        if (holdable != null && holdable.CanInteract(this))
            return holdable;

        return null;
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

        // --- Механики выживания -------------------------------------------
        // Явная маршрутизация по типу объекта, на который попал луч:
        // генератор — заправляем из канистры, зиплайн — начинаем поездку.
        if (Focused is GeneratorController generator)
        {
            // E по генератору использует канистру, которую игрок держит в руках.
            generator.Interact(this);
            return;
        }

        if (Focused is ZiplineController zipline)
        {
            zipline.TryStartRide(this);
            return;
        }
        // ------------------------------------------------------------------

        if (Focused is IPhysicsHoldable && inventory != null && inventory.HasEquipped)
            inventory.UnequipAll();

        if (Focused is IInventoryItem && physicsHold != null && physicsHold.IsHolding)
            physicsHold.Release();

        Focused.Interact(this);

        if (Focused is Object unityObj && unityObj == null) SetFocus(null);
    }
}

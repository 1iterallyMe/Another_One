using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerInteractor : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private GrabManager inventory;
    [SerializeField] private PhysicsHoldController physicsHold;

    /// <summary>Максимальная дистанция взаимодействия (доступна наружу для мышиного клика).</summary>
    public float InteractDistance => interactDistance;
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
    private readonly List<IInteractable> interactableBuffer = new List<IInteractable>(8);

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

        if (playerCamera == null ||
            (inventory != null && inventory.IsInventoryHidden))
        {
            SetFocus(null);
            return;
        }

        // Пока игрок едет на зиплайне, обычное взаимодействие отключено:
        // иначе действие может «зацепить» кнопку/предмет мимо цели.
        if (ZiplineController.ActiveRide != null || CableZiplineRide.ActiveMountedRide != null)
        {
            SetFocus(null);
            return;
        }

        UpdateFocus();

        // Клавиатура — дополнительный способ управления.
        // Основной способ (мышь) обрабатывается в MouseGameplayController:
        // он сам выбирает цель по лучу и вызывает InteractWith().
        if (Input.GetKeyDown(interactKey)) Interact();
        if (Input.GetKeyDown(useKey)) UseOrThrow();
    }

    /// <summary>
    /// Найти интерактивный объект по лучу (для мышиного клика).
    /// Возвращает null, если под лучом ничего подходящего нет.
    /// </summary>
    public IInteractable FindTarget(Ray ray, float maxDistance)
    {
        if (playerCamera == null) return null;

        float distance = maxDistance > 0f ? maxDistance : interactDistance;
        int count = Physics.SphereCastNonAlloc(ray, sphereRadius, hits, distance, interactMask, QueryTriggerInteraction.Collide);

        IInteractable best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.distance >= bestDistance) continue;
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (IsHeldCollider(hit.collider)) continue;

            IInteractable candidate = ResolveInteractable(hit.collider);
            if (candidate == null) continue;

            best = candidate;
            bestDistance = hit.distance;
        }

        return best;
    }

    /// <summary>Взаимодействовать с конкретным объектом (мышиный клик).</summary>
    public void InteractWith(IInteractable target)
    {
        if (target == null) return;
        SetFocus(target);
        Interact();
    }

    /// <summary>F: бросаем предмет из рук, иначе используем предмет инвентаря.</summary>
/// <summary>F: бросаем предмет из рук, иначе используем предмет инвентаря.</summary>
public void UseOrThrow()
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
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        SetFocus(FindTarget(ray, interactDistance));
    }

    /// <summary>
    /// Обновляет фокус по произвольному лучу (мышиное управление: луч из курсора).
    /// </summary>
    public void UpdateFocusFromRay(Ray ray)
    {
        SetFocus(FindTarget(ray, interactDistance));
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
        // На одном объекте может быть несколько реализаций IInteractable
        // (PickupObject + CableZiplineRide у ручки зиплайна). Раньше брался
        // только первый компонент, поэтому установленную ручку нельзя было
        // «схватить» — PickupObject побеждал и пытался её поднять.
        interactableBuffer.Clear();
        col.GetComponentsInParent<IInteractable>(true, interactableBuffer);

        for (int i = 0; i < interactableBuffer.Count; i++)
        {
            IInteractable candidate = interactableBuffer[i];
            if (candidate != null && candidate.CanInteract(this))
                return candidate;
        }

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

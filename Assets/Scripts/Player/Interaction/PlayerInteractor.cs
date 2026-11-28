using System.Collections.Generic;
using UnityEngine;

// Порядок выполнения: 10000 — гарантированно после FPSController (по умолчанию 0).
// Луч взаимодействия обязан считаться ПОСЛЕ того, как камера получила финальные
// позицию и поворот в LateUpdate, иначе он бьёт из прошлого кадра.
[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
public class PlayerInteractor : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private GrabManager inventory;
    [SerializeField] private PhysicsHoldController physicsHold;

    [Header("Raycast")]
    [SerializeField] private float interactDistance = 3.5f;
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

    // Взаимодействие считаем в LateUpdate: к этому моменту FPSController уже
    // выставил камеру (позиция головы + pitch/yaw), поэтому луч из центра экрана
    // совпадает с тем, что игрок видит. Раньше это был Update, порядок с
    // FPSController.Update не определён, а позиция камеры вообще менялась позже —
    // луч уходил на несколько градусов в сторону: предмет ровно в центре («банан»,
    // «мяч», розетка) не подсвечивался, а чуть сбоку — ловился.
    void LateUpdate()
    {
        // Ленивая страховка: если камера не назначена/потеряна — не роняем кадр.
        if (!playerCamera) playerCamera = GetComponentInChildren<Camera>();

        // Курсор может быть отпущен самой ОС (macOS сбрасывает захват, когда окно
        // теряет фокус и возвращает его). Раньше здесь стояла проверка
        // Cursor.lockState == Locked — в билде это навсегда отключало E.
        // Ориентируемся на фокус приложения: он же определяет, получаем ли мы ввод.
        if (playerCamera == null || !Application.isFocused ||
            (inventory != null && inventory.IsInventoryHidden))
        {
            SetFocus(null);
            return;
        }

        // Пока игрок едет на зиплайне, обычное взаимодействие отключено:
        // иначе E может «зацепить» кнопку/предмет мимо цели.
        if (ZiplineController.ActiveRide != null || CableZiplineRide.ActiveMountedRide != null)
        {
            SetFocus(null);
            return;
        }

        UpdateFocus();

        if (KeyPressedThisFrame(interactKey)) Interact();
        if (KeyPressedThisFrame(useKey)) UseOrThrow();
    }

    /// <summary>
    /// Нажатие читаем из обоих бэкендов: при активном Input System legacy
    /// Input.GetKeyDown в билде иногда пропускает нажатие на длинном кадре,
    /// из-за чего «E не сработало» при подборе.
    /// </summary>
    private static bool KeyPressedThisFrame(KeyCode keyCode)
    {
        if (Input.GetKeyDown(keyCode)) return true;

        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null) return false;

        UnityEngine.InputSystem.Key key = keyCode switch
        {
            KeyCode.E => UnityEngine.InputSystem.Key.E,
            KeyCode.F => UnityEngine.InputSystem.Key.F,
            KeyCode.Space => UnityEngine.InputSystem.Key.Space,
            KeyCode.LeftShift => UnityEngine.InputSystem.Key.LeftShift,
            KeyCode.Escape => UnityEngine.InputSystem.Key.Escape,
            _ => UnityEngine.InputSystem.Key.None
        };

        return key != UnityEngine.InputSystem.Key.None && keyboard[key].wasPressedThisFrame;
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
        // Двери двигаются transform.position, предметы телепортируются в хранилище.
        // При выключенном Auto Sync Transforms физика узнаёт об этом только на
        // следующем шаге — коллайдер «отстаёт» и цель то видна, то нет.
        Physics.SyncTransforms();

        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        float bestDistance = float.MaxValue;

        // 1) Точный луч из центра экрана — он совпадает с прицелом.
        int count = Physics.RaycastNonAlloc(ray, hits, interactDistance, interactMask, QueryTriggerInteraction.Collide);
        IInteractable found = PickBest(count, ref bestDistance);

        // 2) Только если луч не нашёл интерактивный объект — «прощающая» сфера:
        // мелкие кнопки и тонкие коллайдеры (листок кода, вентиль).
        // Раньше сфера стояла первой и ближайший коллайдер слоя 6 (край стола,
        // триггер розетки, другой проп) перебивал объект под прицелом.
        if (found == null)
        {
            count = Physics.SphereCastNonAlloc(ray, sphereRadius, hits, interactDistance, interactMask, QueryTriggerInteraction.Collide);
            found = PickBest(count, ref bestDistance);
        }

        SetFocus(found);
    }

    /// <summary>Выбирает ближайший интерактивный объект из результатов каста.</summary>
    IInteractable PickBest(int count, ref float bestDistance)
    {
        IInteractable found = null;

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

        return found;
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

        // Приоритет — объект, с которым действие сейчас доступно. Если такого нет
        // (например, розетка без провода в руках), всё равно возвращаем первый
        // найденный: игрок должен видеть подсветку и подсказку, а не «мёртвую» стену.
        IInteractable fallback = null;
        for (int i = 0; i < interactableBuffer.Count; i++)
        {
            IInteractable candidate = interactableBuffer[i];
            if (candidate == null) continue;

            if (fallback == null) fallback = candidate;
            if (candidate.CanInteract(this)) return candidate;
        }

        return fallback;
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

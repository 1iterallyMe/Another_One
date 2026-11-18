using UnityEngine;

/// <summary>
/// Единый контракт взаимодействия для всего, с чем игрок контактирует
/// через PlayerInteractor (кнопки, предметы, тяжёлые объекты).
/// </summary>
public interface IInteractable
{
    string InteractionPrompt { get; }
    bool CanInteract(PlayerInteractor interactor);
    void Interact(PlayerInteractor interactor);
    void SetFocused(bool focused);
}

/// <summary>Предмет, который уходит в инвентарь (бывший IGrabbable).</summary>
public interface IInventoryItem : IInteractable
{
    void OnEquip(GrabManager inventory);
    void OnUnequip(GrabManager inventory);
    void BindUI(GameObject icon);
    void OnCountChanged(int count);
}

/// <summary>Тяжёлый предмет, удерживаемый физикой (бывший PlayerPickup + PickupObject).</summary>
public interface IPhysicsHoldable : IInteractable
{
    bool IsHeld { get; }
    /// <summary>Масса предмета (кг).</summary>
    float Mass { get; }
    /// <summary>true, если предмет слишком тяжёлый для переноски и должен тащиться.</summary>
    bool IsHeavy { get; }
    /// <summary>Множитель скорости игрока при удержании этого предмета (1 = без замедления).</summary>
    float PlayerSpeedMultiplier { get; }
    void BeginHold(Vector3 targetPosition, Quaternion targetRotation);
    void SetHoldTarget(Vector3 targetPosition, Quaternion targetRotation);
    void EndHold();
}

/// <summary>Опционально: действие на клавишу F.</summary>
public interface IUsableItem
{
    void Use(PlayerInteractor interactor);
}

/// <summary>
/// База для интерактивных предметов: убирает бойлерплейт
/// (prompt, CanInteract, SetFocused) и включает подсветку Outline при наведении.
/// </summary>
public abstract class InteractableBase : MonoBehaviour, IInteractable
{
    [Header("Interaction")]
    [SerializeField] protected string prompt = "Взаимодействовать";

    [Header("Highlight (Outline)")]
    [Tooltip("Готовый компонент Outline (Quick Outline). Если пусто — добавится автоматически при первом наведении.")]
    [SerializeField] private Outline outline;
    [Tooltip("Автоматически добавлять Outline, если он не назначен.")]
    [SerializeField] private bool createOutlineIfMissing = true;
    [SerializeField] private Color outlineColor = new Color(1f, 0.85f, 0.2f, 1f);
    [SerializeField, Range(0f, 10f)] private float outlineWidth = 3f;
    [SerializeField] private Outline.Mode outlineMode = Outline.Mode.OutlineVisible;

    private bool outlineResolved;
    private bool isFocused;

    public bool IsFocused => isFocused;

    public virtual string InteractionPrompt => prompt;
    public virtual bool CanInteract(PlayerInteractor interactor) => isActiveAndEnabled;
    public abstract void Interact(PlayerInteractor interactor);

    public virtual void SetFocused(bool focused)
    {
        isFocused = focused;

        if (focused) ShowOutline();
        else HideOutline();
    }

    protected virtual void ShowOutline()
    {
        ResolveOutline();
        if (outline == null) return;

        outline.OutlineMode = outlineMode;
        outline.OutlineColor = outlineColor;
        outline.OutlineWidth = outlineWidth;
        outline.enabled = true;
    }

    protected virtual void HideOutline()
    {
        if (outline != null) outline.enabled = false;
    }

    private void ResolveOutline()
    {
        if (outlineResolved) return;
        outlineResolved = true;

        if (outline == null) outline = GetComponent<Outline>();

        if (outline == null && createOutlineIfMissing)
        {
            outline = gameObject.AddComponent<Outline>();

            // Настраиваем сразу, чтобы outline сразу был нужного цвета/толщины.
            outline.OutlineMode = outlineMode;
            outline.OutlineColor = outlineColor;
            outline.OutlineWidth = outlineWidth;
        }
    }

    protected virtual void OnDisable()
    {
        if (isFocused) HideOutline();
        isFocused = false;
    }
}

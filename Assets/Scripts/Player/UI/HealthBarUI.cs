using UnityEngine;
using UnityEngine.UI;

public class HealthBarUI : MonoBehaviour
{
    public Image fillImage;
    private PlayerHealth playerHealth;

    void Awake()
    {
        // Полоса здоровья не должна быть вложена в выключенную рамку инвентаря
        // (Selection_Frame): иначе она не видна и не подписывается на изменения HP.
        // Поэтому переносим её (и подложку) прямо на канвас.
        DetachFromInactiveParent();
        ResolveAndSubscribe();
    }

    /// <summary>Переносит полосу HP на канвас, если родитель выключен.</summary>
    void DetachFromInactiveParent()
    {
        if (fillImage == null) return;

        Transform bar = fillImage.transform;
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        // Ищем ближайшего выключенного предка — рамку инвентаря.
        Transform inactiveAncestor = null;
        for (Transform p = bar.parent; p != null; p = p.parent)
        {
            if (!p.gameObject.activeSelf) { inactiveAncestor = p; break; }
        }

        if (inactiveAncestor == null) return;

        // Подложку (Back_Ground) тоже переносим — иначе останется без фона.
        for (int i = 0; i < inactiveAncestor.childCount; i++)
        {
            Transform child = inactiveAncestor.GetChild(i);
            if (child == bar) continue;
            if (child.name == "Back_Ground")
                child.SetParent(canvas.transform, false);
        }

        bar.SetParent(canvas.transform, false);
        bar.gameObject.SetActive(true);
    }

    void Start()
    {
        ResolveAndSubscribe();
    }

    void OnEnable()
    {
        // Компонент может лежать на объекте, который включают позже —
        // подписка в Start() тогда не выполнится (BUG-032).
        ResolveAndSubscribe();
    }

    void ResolveAndSubscribe()
    {
        if (playerHealth == null)
            playerHealth = Object.FindFirstObjectByType<PlayerHealth>();

        if (playerHealth == null) return;

        playerHealth.OnHealthChanged -= UpdateBar;   // без дублей при повторном OnEnable
        playerHealth.OnHealthChanged += UpdateBar;
        UpdateBar(playerHealth.currentHealth, playerHealth.maxHealth);
    }

    void OnDisable()
    {
        if (playerHealth != null)
            playerHealth.OnHealthChanged -= UpdateBar;
    }

    void UpdateBar(float current, float max)
    {
        if (fillImage != null && max > 0f)
            fillImage.fillAmount = current / max;
    }

    void OnDestroy()
    {
        if (playerHealth != null)
            playerHealth.OnHealthChanged -= UpdateBar;
    }
}

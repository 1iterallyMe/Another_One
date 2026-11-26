using UnityEngine;
using UnityEngine.UI;

public class HealthBarUI : MonoBehaviour
{
    public Image fillImage;
    private PlayerHealth playerHealth;

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

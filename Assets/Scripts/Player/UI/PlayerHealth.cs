using System;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerHealth : MonoBehaviour
{
    [Header("Health")]
    [Tooltip("Максимальное HP.")]
    public float maxHealth = 100f;

    [Tooltip("Текущее HP. Видно и меняется в Инспекторе в Play Mode.")]
    public float currentHealth;

    [Header("Death")]
    [Tooltip("Экран смерти. Если не задан — найдётся автоматически.")]
    [SerializeField] private DeathScreen deathScreen;

    [Tooltip("Текст, который покажет DeathScreen (пусто — текст по умолчанию).")]
    [SerializeField] private string deathMessage = "";

    public event Action<float, float> OnHealthChanged;

    public bool isDead { get; private set; }

    private FPSController fpsController;
    private PlayerInteractor interactor;

    void Awake()
    {
        // Стартовое состояние: жив и полон HP (защита от «смерти при загрузке»).
        currentHealth = maxHealth;
        isDead = false;

        fpsController = GetComponent<FPSController>();
        interactor = GetComponent<PlayerInteractor>();
        if (!deathScreen) deathScreen = FindFirstObjectByType<DeathScreen>(FindObjectsInactive.Include);
    }

    void Start()
    {
        currentHealth = maxHealth;
        isDead = false;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    public void TakeDamage(float amount)
    {
        if (isDead || amount <= 0f) return;

        currentHealth -= amount;
        currentHealth = Mathf.Max(0f, currentHealth);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (currentHealth <= 0f)
        {
            isDead = true;
            OnDeath();
        }
    }

    public void Heal(float amount)
    {
        if (isDead || amount <= 0f) return;
        if (IsFullHealth()) return;

        currentHealth += amount;
        currentHealth = Mathf.Min(currentHealth, maxHealth);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    public bool IsFullHealth() => currentHealth >= maxHealth;

    void OnDeath()
    {
        if (fpsController != null) fpsController.enabled = false;
        if (interactor != null) interactor.enabled = false;

        if (deathScreen != null) deathScreen.Show(deathMessage);
    }
}

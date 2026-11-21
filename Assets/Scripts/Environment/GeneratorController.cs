using System;
using UnityEngine;

/// <summary>
/// Генератор с системой заправки.
///
/// Логика:
///  * Пока <see cref="IsPowered"/> == true, генератор тратит топливо со скоростью
///    <see cref="FuelConsumptionRate"/> единиц в секунду.
///  * Как только топливо закончилось — генератор автоматически выключается.
///  * Игрок может «заправить» генератор из канистры (клавиша E, взаимодействие через
///    <see cref="PlayerInteractor"/>). После заправки, если топливо > 0, генератор
///    включается сам.
///
/// Компонент реализует <see cref="IInteractable"/>, поэтому живёт в той же системе
/// взаимодействия, что и остальные объекты уровня (кнопки, предметы).
/// </summary>
[DisallowMultipleComponent]
public class GeneratorController : MonoBehaviour, IInteractable
{
    [Header("Топливо")]
    [Tooltip("Максимальный объём бака.")]
    [SerializeField, Min(0f)] private float maxFuel = 100f;

    [Tooltip("Текущее топливо. По умолчанию бак пуст — игрок должен заправить генератор.")]
    [SerializeField, Min(0f)] private float currentFuel = 0f;

    [Tooltip("Включён ли генератор. Управляется автоматически: включается при заправке и гаснет без топлива.")]
    [SerializeField] private bool isPowered = false;

    [Tooltip("Расход топлива в секунду во время работы.")]
    [SerializeField, Min(0f)] private float fuelConsumptionRate = 1.5f;

    [Header("Заправка из канистры")]
    [Tooltip("Сколько топлива добавляет одно нажатие E.")]
    [SerializeField, Min(0f)] private float canisterFuelAmount = 25f;

    [Tooltip("Минимальная пауза между заправками, чтобы одно нажатие не срабатывало несколько раз.")]
    [SerializeField, Min(0f)] private float refuelCooldown = 0.2f;

    [Header("Обратная связь")]
    [Tooltip("Лампа/подсветка, которая горит только при работающем генераторе.")]
    [SerializeField] private Light powerLight;

    [Tooltip("Рендерер индикатора на корпусе (меняет цвет в зависимости от состояния).")]
    [SerializeField] private Renderer statusLightRenderer;

    [SerializeField] private Color poweredColor = new Color(0.35f, 1f, 0.45f, 1f);
    [SerializeField] private Color unpoweredColor = new Color(0.35f, 0.05f, 0.05f, 1f);

    [Tooltip("Необязательный гул генератора.")]
    [SerializeField] private AudioSource humSource;

    [Header("Подсказки взаимодействия")]
    [SerializeField] private string refuelPrompt = "Заправить генератор";
    [SerializeField] private string fullPrompt = "Бак полон";

    private float nextRefuelTime;
    private MaterialPropertyBlock propertyBlock;

    // ---------------------------------------------------------------------
    // Публичное состояние (только чтение). Поля сериализованы и видны в инспекторе.
    // ---------------------------------------------------------------------

    public float MaxFuel => maxFuel;
    public float CurrentFuel => currentFuel;
    public bool IsPowered => isPowered;
    public float FuelConsumptionRate => fuelConsumptionRate;

    /// <summary>Топливо в диапазоне 0..1 — удобно для UI/индикаторов.</summary>
    public float FuelNormalized => maxFuel > 0.0001f ? Mathf.Clamp01(currentFuel / maxFuel) : 0f;

    public bool IsFuelFull => currentFuel >= maxFuel - 0.001f;

    /// <summary>Событие при включении/выключении. Аргумент — новое значение isPowered.</summary>
    public event Action<bool> PoweredChanged;

    // ---------------------------------------------------------------------
    // Жизненный цикл
    // ---------------------------------------------------------------------

    private void Awake()
    {
        maxFuel = Mathf.Max(0f, maxFuel);
        fuelConsumptionRate = Mathf.Max(0f, fuelConsumptionRate);
        currentFuel = Mathf.Clamp(currentFuel, 0f, maxFuel);

        // Если в инспекторе случайно оставили isPowered при пустом баке — не даём
        // «вечному двигателю»: работать без топлива нельзя.
        if (isPowered && currentFuel <= 0f) isPowered = false;

        UpdateFeedback();
    }

    private void Update()
    {
        if (!isPowered) return;

        // Работающий генератор постепенно расходует топливо.
        currentFuel -= fuelConsumptionRate * Time.deltaTime;

        if (currentFuel <= 0f)
        {
            currentFuel = 0f;
            SetPowered(false);
            Debug.Log("[GeneratorController] Топливо закончилось — генератор выключился.", this);
        }
    }

    private void OnValidate()
    {
        maxFuel = Mathf.Max(0f, maxFuel);
        currentFuel = Mathf.Clamp(currentFuel, 0f, maxFuel);
        fuelConsumptionRate = Mathf.Max(0f, fuelConsumptionRate);
        canisterFuelAmount = Mathf.Max(0f, canisterFuelAmount);
        refuelCooldown = Mathf.Max(0f, refuelCooldown);

        if (isPowered && currentFuel <= 0f) isPowered = false;
    }

    // ---------------------------------------------------------------------
    // Публичный API
    // ---------------------------------------------------------------------

    /// <summary>
    /// Добавить топливо (имитация канистры) и автоматически запустить генератор.
    /// </summary>
    /// <param name="amount">Сколько добавить. Если &lt;= 0 — используется canisterFuelAmount.</param>
    /// <returns>true, если топливо действительно было добавлено.</returns>
    public bool TryRefuel(float amount = -1f)
    {
        if (Time.time < nextRefuelTime) return false;
        if (IsFuelFull) return false;

        float add = amount > 0f ? amount : canisterFuelAmount;
        if (add <= 0f) return false;

        currentFuel = Mathf.Clamp(currentFuel + add, 0f, maxFuel);
        nextRefuelTime = Time.time + refuelCooldown;

        // По условию задачи: если после заправки топливо > 0 — генератор включается.
        if (currentFuel > 0f) SetPowered(true);

        UpdateFeedback();
        Debug.Log($"[GeneratorController] Заправка: +{add:0.#} → {currentFuel:0.#}/{maxFuel:0.#}, powered={isPowered}", this);
        return true;
    }

    /// <summary>Включить/выключить генератор. Без топлива включиться нельзя.</summary>
    public void SetPowered(bool powered)
    {
        bool next = powered && currentFuel > 0f;
        if (isPowered != next)
        {
            isPowered = next;
            PoweredChanged?.Invoke(isPowered);
            Debug.Log($"[GeneratorController] isPowered = {isPowered}", this);
        }

        UpdateFeedback();
    }

    // ---------------------------------------------------------------------
    // IInteractable — интеграция с существующей системой взаимодействия
    // ---------------------------------------------------------------------

    public string InteractionPrompt =>
        IsFuelFull
            ? fullPrompt
            : $"{refuelPrompt} ({Mathf.CeilToInt(currentFuel)}/{Mathf.CeilToInt(maxFuel)})";

    public bool CanInteract(PlayerInteractor interactor) => isActiveAndEnabled;

    public void Interact(PlayerInteractor interactor) => TryRefuel();

    /// <summary>
    /// Подсветка при наведении не требуется: у корпуса есть собственная лампа состояния,
    /// которая загорается при включении генератора.
    /// </summary>
    public void SetFocused(bool focused) { }

    // ---------------------------------------------------------------------
    // Визуал
    // ---------------------------------------------------------------------

    private void UpdateFeedback()
    {
        if (powerLight != null) powerLight.enabled = isPowered;

        if (humSource != null)
        {
            if (isPowered && !humSource.isPlaying) humSource.Play();
            else if (!isPowered && humSource.isPlaying) humSource.Stop();
        }

        if (statusLightRenderer == null) return;

        if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();

        Color c = isPowered ? poweredColor : unpoweredColor;
        statusLightRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor("_BaseColor", c);
        propertyBlock.SetColor("_Color", c);
        // В выключенном состоянии оставляем лёгкое свечение, чтобы индикатор читался в темноте.
        propertyBlock.SetColor("_EmissionColor", c * (isPowered ? 3f : 0.35f));
        statusLightRenderer.SetPropertyBlock(propertyBlock);
    }

    /// <summary>Принудительно обновить визуальное состояние (например, после правок в рантайме).</summary>
    public void RefreshFeedback() => UpdateFeedback();
}

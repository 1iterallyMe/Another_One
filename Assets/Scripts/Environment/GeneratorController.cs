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

    [Tooltip("Расход топлива в секунду во время работы. При баке 100 и 0.075 полного бака хватает примерно на 22 минуты (было 1.5 — ~67 секунд).")]
    [SerializeField, Min(0f)] private float fuelConsumptionRate = 0.075f;

    [Header("Заправка из канистры")]
    [Tooltip("Сколько топлива добавляет одно нажатие E.")]
    [SerializeField, Min(0f)] private float canisterFuelAmount = 25f;

    [Tooltip("Минимальная пауза между заправками, чтобы одно нажатие не срабатывало несколько раз.")]
    [SerializeField, Min(0f)] private float refuelCooldown = 0.2f;

    [Header("Интеграция с системой Александра")]
    [Tooltip("FuelGenerator из системы Александра. Если назначен, топливо и состояние двигателя берутся из него.")]
    [SerializeField] private FuelGenerator linkedGenerator;

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

    // Индикаторы обновляются при изменении состояния, а не только при вызове
    // TryRefuel/SetPowered: двигатель может запуститься напрямую через FuelGenerator
    // (заливка канистрой), и тогда GeneratorController об этом не узнаёт.
    private bool lastPoweredState;
    private bool feedbackInitialized;

    // ---------------------------------------------------------------------
    // Публичное состояние (только чтение). Поля сериализованы и видны в инспекторе.
    // ---------------------------------------------------------------------

    public float MaxFuel => linkedGenerator != null ? linkedGenerator.FuelCapacity : maxFuel;
    public float CurrentFuel => linkedGenerator != null ? linkedGenerator.CurrentFuel : currentFuel;
    public bool IsPowered => linkedGenerator != null ? linkedGenerator.IsRunning : isPowered;
    public float FuelConsumptionRate => fuelConsumptionRate;

    /// <summary>Двигатель работает И кабель питания подключён (провод включён).</summary>
    public bool HasPower => linkedGenerator != null ? linkedGenerator.HasPower : isPowered;

    /// <summary>FuelGenerator Александра, если он используется как источник состояния.</summary>
    public FuelGenerator LinkedGenerator => linkedGenerator;

    /// <summary>Топливо в диапазоне 0..1 — удобно для UI/индикаторов.</summary>
    public float FuelNormalized => MaxFuel > 0.0001f ? Mathf.Clamp01(CurrentFuel / MaxFuel) : 0f;

    public bool IsFuelFull => CurrentFuel >= MaxFuel - 0.001f;

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

        // Если на объекте есть FuelGenerator Александра — работаем через него.
        if (linkedGenerator == null) linkedGenerator = GetComponent<FuelGenerator>();

        // Если в инспекторе случайно оставили isPowered при пустом баке — не даём
        // «вечному двигателю»: работать без топлива нельзя.
        if (isPowered && currentFuel <= 0f) isPowered = false;

        UpdateFeedback();
    }

    private void Update()
    {
        // FuelGenerator сам запускает двигатель после заливки канистрой, поэтому
        // состояние индикаторов (StatusLight + PowerLight) отслеживаем по факту
        // изменения IsPowered, а не только через SetPowered/TryRefuel.
        bool poweredNow = IsPowered;
        if (!feedbackInitialized || poweredNow != lastPoweredState)
        {
            feedbackInitialized = true;
            lastPoweredState = poweredNow;
            UpdateFeedback();
        }

        if (!poweredNow) return;

        // Если подключён FuelGenerator Александра — расходуем его топливо,
        // чтобы все его потребители (кабель, зиплайн) видели одно состояние.
        if (linkedGenerator != null)
        {
            linkedGenerator.ConsumeFuel(fuelConsumptionRate * Time.deltaTime);

            if (!linkedGenerator.IsRunning)
            {
                SetPowered(false);
                Debug.Log("[GeneratorController] Топливо закончилось — генератор выключился.", this);
            }

            return;
        }

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

        nextRefuelTime = Time.time + refuelCooldown;

        // Если на объекте есть FuelGenerator Александра — льём топливо в него.
        // Это автоматически запускает его двигатель (AddFuel -> StartEngine).
        if (linkedGenerator != null)
        {
            linkedGenerator.AddFuel(add);
            SetPowered(true);
            UpdateFeedback();

            Debug.Log($"[GeneratorController] Заправка FuelGenerator: +{add:0.#} → {linkedGenerator.CurrentFuel:0.#}/{linkedGenerator.FuelCapacity:0.#}, running={linkedGenerator.IsRunning}", this);
            return true;
        }

        currentFuel = Mathf.Clamp(currentFuel + add, 0f, maxFuel);

        // По условию задачи: если после заправки топливо > 0 — генератор включается.
        if (currentFuel > 0f) SetPowered(true);

        UpdateFeedback();
        Debug.Log($"[GeneratorController] Заправка: +{add:0.#} → {currentFuel:0.#}/{maxFuel:0.#}, powered={isPowered}", this);
        return true;
    }

    /// <summary>Включить/выключить генератор. Без топлива включиться нельзя.</summary>
    public void SetPowered(bool powered)
    {
        if (linkedGenerator != null)
        {
            bool wasRunning = linkedGenerator.IsRunning;

            if (powered)
            {
                if (linkedGenerator.CurrentFuel > 0f) linkedGenerator.StartEngine();
            }
            else
            {
                linkedGenerator.StopEngine();
            }

            isPowered = linkedGenerator.IsRunning;
            if (wasRunning != isPowered) PoweredChanged?.Invoke(isPowered);

            Debug.Log($"[GeneratorController] linked FuelGenerator running={isPowered}", this);
            UpdateFeedback();
            return;
        }

        bool next = powered && currentFuel > 0f;
        if (isPowered != next)
        {
            isPowered = next;
            PoweredChanged?.Invoke(isPowered);
            Debug.Log($"[GeneratorController] isPowered = {isPowered}", this);
        }

        UpdateFeedback();
    }

    /// <summary>
    /// Подключение "кабеля питания". WireConnector вызывает это при появлении/исчезновении
    /// провода; для FuelGenerator Александра это переключает HasPower.
    /// </summary>
    public void SetCableConnected(bool connected)
    {
        if (linkedGenerator != null) linkedGenerator.SetCableConnected(connected);
    }

    // ---------------------------------------------------------------------
    // IInteractable — интеграция с существующей системой взаимодействия
    // ---------------------------------------------------------------------

    public string InteractionPrompt =>
        IsFuelFull
            ? fullPrompt
            : $"{refuelPrompt} ({Mathf.CeilToInt(CurrentFuel)}/{Mathf.CeilToInt(MaxFuel)})";

    /// <summary>Генератор доступен для E только когда игрок держит канистру с бензином.</summary>
    public bool CanInteract(PlayerInteractor interactor)
        => isActiveAndEnabled
           && linkedGenerator != null
           && !linkedGenerator.IsFueled
           && FindHeldCanister(interactor) != null;

    public void Interact(PlayerInteractor interactor) => TryRefuelFromCanister(interactor);

    /// <summary>E: поставить канистру из рук на горловину и начать заливку.</summary>
    private bool TryRefuelFromCanister(PlayerInteractor interactor)
    {
        if (linkedGenerator == null) return TryRefuel();

        FuelCanister canister = FindHeldCanister(interactor);
        if (canister == null)
        {
            Debug.Log("[GeneratorController] Нужна канистра с бензином в руках.", this);
            return false;
        }

        bool started = linkedGenerator.BeginPouringHeldCanister(canister);
        if (started)
            Debug.Log("[GeneratorController] Канистра встала на горловину, идёт заливка.", this);

        return started;
    }

    private FuelCanister FindHeldCanister(PlayerInteractor interactor)
    {
        if (interactor == null || interactor.PhysicsHold == null || !interactor.PhysicsHold.IsHolding)
            return null;

        MonoBehaviour held = interactor.PhysicsHold.Held as MonoBehaviour;
        return held != null ? held.GetComponentInParent<FuelCanister>() : null;
    }

    /// <summary>
    /// Подсветка при наведении не требуется: у корпуса есть собственная лампа состояния,
    /// которая загорается при включении генератора.
    /// </summary>
    public void SetFocused(bool focused) => OutlineHighlight.Set(this, focused);

    // ---------------------------------------------------------------------
    // Визуал
    // ---------------------------------------------------------------------

    private void UpdateFeedback()
    {
        bool powered = IsPowered;

        // Синхронизируем кэш, чтобы внешние вызовы (TryRefuel, SetPowered,
        // RefreshFeedback) не приводили к повторному обновлению на следующем кадре.
        lastPoweredState = powered;
        feedbackInitialized = true;

        if (powerLight != null) powerLight.enabled = powered;

        if (humSource != null)
        {
            if (powered && !humSource.isPlaying) humSource.Play();
            else if (!powered && humSource.isPlaying) humSource.Stop();
        }

        if (statusLightRenderer == null) return;

        if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();

        Color c = powered ? poweredColor : unpoweredColor;
        statusLightRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor("_BaseColor", c);
        propertyBlock.SetColor("_Color", c);
        // В выключенном состоянии оставляем лёгкое свечение, чтобы индикатор читался в темноте.
        // Множитель берём от вычисленного powered: поле isPowered не обновляется,
        // когда двигатель запускается напрямую через FuelGenerator (заливка канистрой).
        propertyBlock.SetColor("_EmissionColor", c * (powered ? 3f : 0.35f));
        statusLightRenderer.SetPropertyBlock(propertyBlock);
    }

    /// <summary>Принудительно обновить визуальное состояние (например, после правок в рантайме).</summary>
    public void RefreshFeedback() => UpdateFeedback();
}

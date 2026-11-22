using System;
using UnityEngine;

/// <summary>
/// Генератор: принимает канистру у горловины, заливает топливо, автоматически запускает двигатель
/// после заправки и хранит состояние питания для будущего провода и зиплайна.
/// </summary>
[DisallowMultipleComponent]
public class FuelGenerator : MonoBehaviour
{
    [Header("Fuel & Pouring")]
    [Tooltip("Точка у горловины, куда плавно встаёт и наклоняется канистра во время заливки.")]
    [SerializeField] private Transform pourTargetPoint;
    [Tooltip("Радиус зоны заливки вокруг Pour Target Point (если не используется отдельный триггер).")]
    [SerializeField, Min(0.05f)] private float fillTriggerRadius = 0.65f;
    [Tooltip("Скорость заливки топлива (ед./сек). При ёмкости канистры 100 и скорости 40 заливка займёт 2.5 сек.")]
    [SerializeField, Min(0.1f)] private float fillRatePerSecond = 40f;
    [Tooltip("Если включено — заливка начнётся только когда канистру поднесли к горловине, даже если игрок отпустит её рядом.")]
    [SerializeField] private bool allowUnheldCanisterInZone = true;

    [Header("Engine Audio")]
    [SerializeField] private AudioSource engineAudioSource;
    [SerializeField] private AudioClip engineStartClip;
    [SerializeField, Range(0f, 1f)] private float engineStartVolume = 0.9f;
    [SerializeField] private AudioClip engineLoopClip;
    [SerializeField, Range(0f, 1f)] private float engineLoopVolume = 0.75f;

    [Header("Optional Engine Vibration")]
    [Tooltip("Объект корпуса/деталей генератора, который слегка вибрирует при работающем двигателе.")]
    [SerializeField] private Transform vibrationTarget;
    [SerializeField, Min(0f)] private float vibrationAmplitude = 0.004f;
    [SerializeField, Min(0f)] private float vibrationFrequency = 38f;

    public float CurrentFuel { get; private set; }
    public float FuelCapacity { get; private set; } = 100f;
    public bool IsFueled { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsCableConnected { get; private set; }

    /// <summary>Итоговое питание в сети: двигатель работает и кабель подключён к потребителю/столбу.</summary>
    public bool HasPower => IsRunning && IsCableConnected;

    public event Action<FuelGenerator> FuelCompleted;
    public event Action<FuelGenerator> EngineStarted;
    public event Action<bool> CableConnectionChanged;

    private FuelCanister currentCanister;
    private Vector3 vibrationBaseLocalPos;
    private bool hasVibrationBase;

    private void Awake()
    {
        SetupEngineAudio();
        CacheVibrationBase();
    }

    private void OnValidate()
    {
        fillTriggerRadius = Mathf.Max(0.05f, fillTriggerRadius);
        fillRatePerSecond = Mathf.Max(0.1f, fillRatePerSecond);
        vibrationAmplitude = Mathf.Max(0f, vibrationAmplitude);
        vibrationFrequency = Mathf.Max(0f, vibrationFrequency);
    }

    private void SetupEngineAudio()
    {
        if (engineAudioSource == null)
            engineAudioSource = GetComponent<AudioSource>();

        if (engineAudioSource == null)
            engineAudioSource = gameObject.AddComponent<AudioSource>();

        engineAudioSource.spatialBlend = 1f;
        engineAudioSource.playOnAwake = false;
    }

    private void CacheVibrationBase()
    {
        if (vibrationTarget == null)
            return;

        vibrationBaseLocalPos = vibrationTarget.localPosition;
        hasVibrationBase = true;
    }

    private void Update()
    {
        if (!IsFueled)
            UpdateFuelPouring();

        UpdateVibration();
    }

    private void UpdateFuelPouring()
    {
        Vector3 zoneCenter = pourTargetPoint != null ? pourTargetPoint.position : transform.position;

        if (currentCanister == null)
        {
            FuelCanister candidate = FindCanisterInZone(zoneCenter);
            if (candidate != null)
            {
                FuelCapacity = candidate.FuelCapacity;
                CurrentFuel = FuelCapacity * (1f - candidate.FuelNormalized);

                if (candidate.BeginPouring(this, pourTargetPoint))
                    currentCanister = candidate;
            }
            return;
        }

        if (!currentCanister.HasFuel)
        {
            FinishFueling();
            return;
        }

        float consumed = currentCanister.ConsumeFuel(fillRatePerSecond * Time.deltaTime);
        CurrentFuel = Mathf.Min(FuelCapacity, CurrentFuel + consumed);

        if (!currentCanister.HasFuel)
            FinishFueling();
    }

    private FuelCanister FindCanisterInZone(Vector3 zoneCenter)
    {
        Collider[] hits = Physics.OverlapSphere(zoneCenter, fillTriggerRadius, ~0, QueryTriggerInteraction.Ignore);
        FuelCanister best = null;
        float bestDistSqr = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i];
            if (col == null) continue;

            FuelCanister canister = col.GetComponentInParent<FuelCanister>();
            if (canister == null || !canister.HasFuel || canister.IsPouring) continue;

            PickupObject pickup = canister.GetComponent<PickupObject>();
            bool held = pickup != null && pickup.IsHeld;
            if (!held && !allowUnheldCanisterInZone) continue;

            float distSqr = (canister.transform.position - zoneCenter).sqrMagnitude;
            if (distSqr < bestDistSqr)
            {
                bestDistSqr = distSqr;
                best = canister;
            }
        }

        return best;
    }

    private void FinishFueling()
    {
        FuelCanister finishedCanister = currentCanister;
        currentCanister = null;

        if (finishedCanister != null)
            finishedCanister.EndPouring(true);

        CurrentFuel = FuelCapacity;
        IsFueled = true;
        FuelCompleted?.Invoke(this);

        StartEngine();
    }

    public void CancelPouring()
    {
        if (currentCanister == null)
            return;

        FuelCanister active = currentCanister;
        currentCanister = null;
        active.EndPouring(false);
    }

    public void StartEngine()
    {
        if (!IsFueled || IsRunning)
            return;

        IsRunning = true;

        if (engineAudioSource != null)
        {
            if (engineStartClip != null)
                engineAudioSource.PlayOneShot(engineStartClip, engineStartVolume);

            if (engineLoopClip != null)
            {
                engineAudioSource.clip = engineLoopClip;
                engineAudioSource.loop = true;
                engineAudioSource.volume = engineLoopVolume;
                engineAudioSource.Play();
            }
        }

        EngineStarted?.Invoke(this);
    }

    public void SetCableConnected(bool connected)
    {
        if (IsCableConnected == connected)
            return;

        IsCableConnected = connected;
        CableConnectionChanged?.Invoke(IsCableConnected);
    }

    private void UpdateVibration()
    {
        if (vibrationTarget == null)
            return;

        if (!hasVibrationBase)
            CacheVibrationBase();

        if (!IsRunning || vibrationAmplitude <= 0f)
        {
            vibrationTarget.localPosition = vibrationBaseLocalPos;
            return;
        }

        float t = Time.time * vibrationFrequency;
        Vector3 offset = new Vector3(
            Mathf.Sin(t * 1.1f) * vibrationAmplitude,
            Mathf.Cos(t * 1.7f) * vibrationAmplitude * 0.7f,
            Mathf.Sin(t * 1.3f) * vibrationAmplitude
        );
        vibrationTarget.localPosition = vibrationBaseLocalPos + offset;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 zoneCenter = pourTargetPoint != null ? pourTargetPoint.position : transform.position;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(zoneCenter, fillTriggerRadius);

        if (pourTargetPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(pourTargetPoint.position, 0.05f);
            Gizmos.DrawRay(pourTargetPoint.position, -pourTargetPoint.up * 0.25f);
        }
    }
}
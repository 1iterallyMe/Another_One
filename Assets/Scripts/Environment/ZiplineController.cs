using System.Collections;
using UnityEngine;

/// <summary>
/// Зиплайн: перемещение игрока от <see cref="startPoint"/> к <see cref="endPoint"/>.
///
/// Правила:
///  * Пользоваться зиплайном можно только при включённом генераторе.
///  * Игрок должен стоять рядом со стартовой точкой и смотреть на зиплайн.
///  * После нажатия E игрок «цепляется», плавно едет по тросу и автоматически
///    отцепляется в конце маршрута.
///
/// Компонент реализует <see cref="IInteractable"/>, поэтому PlayerInteractor
/// находит его тем же лучом, что и остальные интерактивные объекты.
/// </summary>
[DisallowMultipleComponent]
public class ZiplineController : MonoBehaviour, IInteractable
{
    [Header("Маршрут")]
    [Tooltip("Начало зиплайна — позиция, в которую «телепортируется» игрок при старте.")]
    [SerializeField] private Transform startPoint;

    [Tooltip("Конец зиплайна — позиция, в которой игрок отцепляется.")]
    [SerializeField] private Transform endPoint;

    [Header("Питание")]
    [Tooltip("Генератор, который питает зиплайн. Без isPowered == true поездка невозможна.")]
    [SerializeField] private GeneratorController generator;

    [Header("Поездка")]
    [Tooltip("Максимальная скорость движения по тросу, м/с.")]
    [SerializeField, Min(0.1f)] private float rideSpeed = 6f;

    [Tooltip("Ускорение разгона/торможения, м/с².")]
    [SerializeField, Min(0f)] private float rideAcceleration = 9f;

    [Tooltip("Насколько близко к startPoint должен стоять игрок, чтобы зацепиться.")]
    [SerializeField, Min(0f)] private float boardingRadius = 2.4f;

    [Tooltip("Пауза перед отцеплением в конце маршрута, сек.")]
    [SerializeField, Min(0f)] private float arrivalDelay = 0.15f;

    [Tooltip("Клавиша досрочного отцепления во время поездки.")]
    [SerializeField] private KeyCode releaseKey = KeyCode.E;

    [Tooltip("Сколько секунд после старта игнорировать E, чтобы то же нажатие не отцепило игрока.")]
    [SerializeField, Min(0f)] private float releaseInputLock = 0.4f;

    [Header("Визуал")]
    [Tooltip("LineRenderer, который рисует сам трос (необязательно).")]
    [SerializeField] private LineRenderer cableRenderer;

    [Tooltip("Визуальная тележка/ручка, которая едет вместе с игроком (необязательно).")]
    [SerializeField] private Transform trolley;

    [Tooltip("Смещение тележки относительно позиции игрока.")]
    [SerializeField] private Vector3 trolleyOffset = new Vector3(0f, 0.45f, 0f);

    [Header("Подсказки взаимодействия")]
    [SerializeField] private string ridePrompt = "Пролететь на зиплайне";
    [SerializeField] private string unpoweredPrompt = "Зиплайн обесточен";

    private bool isRiding;
    private bool detachRequested;
    private float rideStartTime;
    private Transform rider;

    /// <summary>Активная поездка (для блокировки обычного взаимодействия в PlayerInteractor).</summary>
    public static ZiplineController ActiveRide { get; private set; }

    public bool IsRiding => isRiding;
    public GeneratorController Generator => generator;
    public Transform StartPoint => startPoint;
    public Transform EndPoint => endPoint;

    private void Awake()
    {
        UpdateCable();
    }

    private void Start()
    {
        UpdateCable();
        MoveTrolleyToStart();
    }

    private void OnValidate()
    {
        if (rideSpeed < 0.1f) rideSpeed = 0.1f;
        if (rideAcceleration < 0f) rideAcceleration = 0f;
        if (boardingRadius < 0f) boardingRadius = 0f;
        if (arrivalDelay < 0f) arrivalDelay = 0f;
        if (releaseInputLock < 0f) releaseInputLock = 0f;
    }

    private void Update()
    {
        UpdateCable();

        if (isRiding)
        {
            // Досрочное отцепление по E (после короткой блокировки ввода на старте).
            if (Time.time - rideStartTime > releaseInputLock && Input.GetKeyDown(releaseKey))
                detachRequested = true;

            if (trolley != null && ActiveRide == this)
                trolley.position = GetRideAnchorPosition() + trolleyOffset;
        }
    }

    // ---------------------------------------------------------------------
    // Публичный API
    // ---------------------------------------------------------------------

    /// <summary>
    /// Попытаться начать поездку. Вызывается из PlayerInteractor по нажатию E.
    /// </summary>
    public bool TryStartRide(PlayerInteractor interactor)
    {
        if (isRiding || interactor == null) return false;
        if (startPoint == null || endPoint == null)
        {
            Debug.LogWarning("[ZiplineController] Не назначены startPoint/endPoint.", this);
            return false;
        }

        if (generator == null || !generator.IsPowered)
        {
            Debug.Log("[ZiplineController] Генератор выключен — зиплайн не работает.", this);
            return false;
        }

        // Игрок должен стоять у начала маршрута, а не «притягиваться» через всю комнату.
        float distanceToStart = Vector3.Distance(interactor.transform.position, startPoint.position);
        if (distanceToStart > boardingRadius)
        {
            Debug.Log($"[ZiplineController] Игрок слишком далеко от старта ({distanceToStart:0.0} м > {boardingRadius:0.0} м).", this);
            return false;
        }

        var rb = interactor.GetComponent<Rigidbody>();
        var fps = interactor.GetComponent<FPSController>();

        StartCoroutine(RideRoutine(interactor.transform, rb, fps));
        return true;
    }

    /// <summary>Мгновенно прервать поездку (например, из другого скрипта).</summary>
    public void RequestDetach()
    {
        if (isRiding) detachRequested = true;
    }

    // ---------------------------------------------------------------------
    // Поездка
    // ---------------------------------------------------------------------

    private IEnumerator RideRoutine(Transform riderTransform, Rigidbody rb, FPSController fps)
    {
        rider = riderTransform;
        isRiding = true;
        detachRequested = false;
        rideStartTime = Time.time;
        ActiveRide = this;

        Vector3 start = startPoint.position;
        Vector3 end = endPoint.position;
        Vector3 direction = (end - start).sqrMagnitude > 0.0001f
            ? (end - start).normalized
            : riderTransform.forward;

        // --- Подготовка игрока -------------------------------------------------
        // Запоминаем состояние физики, чтобы вернуть его после поездки.
        bool wasKinematic = false;
        bool wasGravity = true;

        if (rb != null)
        {
            wasKinematic = rb.isKinematic;
            wasGravity = rb.useGravity;

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // FPSController в FixedUpdate двигает игрока под действием гравитации и ввода.
        // На время поездки он выключается, иначе будет «бороться» с зиплайном.
        bool fpsWasEnabled = fps != null && fps.enabled;
        if (fps != null) fps.enabled = false;

        // Ставим игрока в начало троса и разворачиваем вдоль движения.
        riderTransform.position = start;
        FaceDirection(riderTransform, fps, direction, instant: true);
        if (trolley != null) trolley.position = start + trolleyOffset;

        Debug.Log($"[ZiplineController] Поездка началась: {start} → {end}", this);

        // --- Движение ---------------------------------------------------------
        float speed = 0f;
        while (!detachRequested)
        {
            Vector3 current = riderTransform.position;
            float remaining = Vector3.Distance(current, end);

            if (remaining <= 0.02f) break;

            // Плавный разгон и мягкое торможение у финиша.
            speed = Mathf.MoveTowards(speed, rideSpeed, rideAcceleration * Time.deltaTime);
            float brakingSpeed = Mathf.Sqrt(2f * rideAcceleration * remaining);
            float stepSpeed = Mathf.Min(speed, Mathf.Max(brakingSpeed, 0.35f));

            riderTransform.position = Vector3.MoveTowards(current, end, stepSpeed * Time.deltaTime);

            // Поворачиваем игрока/камеру по направлению троса (плавно).
            FaceDirection(riderTransform, fps, direction, instant: false);

            yield return null;
        }

        // Доводим до конца, если не отцепились досрочно.
        if (!detachRequested) riderTransform.position = end;

        if (arrivalDelay > 0f) yield return new WaitForSeconds(arrivalDelay);

        // --- Отцепление -------------------------------------------------------
        if (rb != null)
        {
            rb.isKinematic = wasKinematic;
            rb.useGravity = wasGravity;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.WakeUp();
        }

        if (fps != null) fps.enabled = fpsWasEnabled;

        rider = null;
        isRiding = false;
        detachRequested = false;
        if (ActiveRide == this) ActiveRide = null;

        MoveTrolleyToStart();
        Debug.Log("[ZiplineController] Игрок отцепился.", this);
    }

    /// <summary>Повернуть игрока (и синхронизировать yaw FPSController) вдоль троса.</summary>
    private void FaceDirection(Transform riderTransform, FPSController fps, Vector3 direction, bool instant)
    {
        float targetYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        float currentYaw = riderTransform.eulerAngles.y;

        float yaw = instant
            ? targetYaw
            : Mathf.MoveTowardsAngle(currentYaw, targetYaw, 180f * Time.deltaTime);

        if (fps != null)
        {
            // FPSController хранит собственный yaw — синхронизируем, чтобы после
            // поездки камера не «прыгнула» в старое направление.
            fps.SetYaw(yaw);
        }
        else
        {
            riderTransform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }
    }

    // ---------------------------------------------------------------------
    // Визуал троса
    // ---------------------------------------------------------------------

    private void UpdateCable()
    {
        if (cableRenderer == null || startPoint == null || endPoint == null) return;

        cableRenderer.useWorldSpace = true;
        cableRenderer.positionCount = 2;
        cableRenderer.SetPosition(0, startPoint.position);
        cableRenderer.SetPosition(1, endPoint.position);
    }

    private void MoveTrolleyToStart()
    {
        if (trolley != null && startPoint != null)
            trolley.position = startPoint.position + trolleyOffset;
    }

    private Vector3 GetRideAnchorPosition()
    {
        if (rider != null) return rider.position;
        return startPoint != null ? startPoint.position : transform.position;
    }

    // ---------------------------------------------------------------------
    // IInteractable
    // ---------------------------------------------------------------------

    public string InteractionPrompt =>
        generator != null && generator.IsPowered ? ridePrompt : unpoweredPrompt;

    public bool CanInteract(PlayerInteractor interactor)
    {
        if (!isActiveAndEnabled || isRiding || interactor == null) return false;
        if (generator == null || !generator.IsPowered) return false;
        if (startPoint == null) return false;

        // Фокус появляется только рядом со стартом — иначе игрок «видел» бы зиплайн
        // из любой точки комнаты.
        return Vector3.Distance(interactor.transform.position, startPoint.position) <= boardingRadius;
    }

    public void Interact(PlayerInteractor interactor) => TryStartRide(interactor);

    public void SetFocused(bool focused) { }
}

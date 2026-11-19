using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Монитор/терминал для ввода кода из листков.
/// Игрок наводится на монитор и нажимает E — включается режим ввода:
/// цифры 0–9 с клавиатуры, Backspace — удалить, Enter — подтвердить, Esc — выйти.
/// Введённый код сверяется с SecretCodeManager.
/// Для экранных кнопок есть публичные методы PressDigit/Backspace/Submit.
/// </summary>
[DisallowMultipleComponent]
public class CodeMonitor : InteractableBase
{
    [Header("Экран")]
    [Tooltip("Текст на экране монитора. Если пусто — найдётся первый TMP-текст в детях.")]
    [SerializeField] private TMP_Text display;
    [Tooltip("Что показывать, пока код не начат.")]
    [SerializeField] private string emptyDisplay = "_ _ _ _";
    [Tooltip("Скрывать введённые цифры звёздочками.")]
    [SerializeField] private bool maskDigits = false;

    [Header("Сообщения")]
    [SerializeField] private string successMessage = "ДОСТУП РАЗРЕШЁН";
    [SerializeField] private string wrongMessage = "НЕВЕРНЫЙ КОД";
    [Tooltip("Цвет текста при успешном коде.")]
    [SerializeField] private Color successColor = new Color(0.2f, 1f, 0.3f);
    [Tooltip("Цвет текста при ошибке.")]
    [SerializeField] private Color errorColor = new Color(1f, 0.25f, 0.2f);
    [Tooltip("Сколько секунд показывать сообщение об ошибке.")]
    [SerializeField, Min(0f)] private float messageDuration = 1.2f;
    [Tooltip("Пауза перед показом результата (сек), чтобы игрок успел увидеть введённые цифры или звёздочки.")]
    [SerializeField, Min(0f)] private float messageDelay = 0.5f;

    [Header("Ввод")]
    [Tooltip("Принимать цифры с клавиатуры, когда режим ввода активен.")]
    [SerializeField] private bool acceptKeyboardInput = true;
    [Tooltip("Проверять код сразу после ввода последней цифры.")]
    [SerializeField] private bool autoSubmitWhenFull = true;
    [Tooltip("Выйти из режима ввода, если игрок отошёл дальше этого расстояния (м). 0 — не проверять.")]
    [SerializeField, Min(0f)] private float exitDistance = 4f;

    [Header("Дверь по коду")]
    [Tooltip("Двери, которые открываются при верном коде (объекты дверей в сцене).")]
    [SerializeField] private Transform[] doorsToOpen;

    [Header("Перемещение двери")]
    [Tooltip("Направление, в котором уезжает дверь при открытии.")]
    [SerializeField] private WallMoveDirection doorMoveDirection = WallMoveDirection.Down;
    [Tooltip("Своё направление. Используется только при Direction = Своё направление.")]
    [SerializeField] private Vector3 doorCustomDirection = Vector3.down;
    [Tooltip("Считать направление относительно поворота двери, а не мировых осей.")]
    [SerializeField] private bool doorDirectionIsLocal = false;
    [Tooltip("На сколько метров уезжает дверь.")]
    [SerializeField, Min(0f)] private float doorMoveDistance = 3f;
    [Tooltip("Скорость движения двери (м/с).")]
    [SerializeField, Min(0.01f)] private float doorMoveSpeed = 2f;
    [Tooltip("Оставить дверь открытой. Если выключено — дверь вернётся назад.")]
    [SerializeField] private bool keepDoorOpen = true;
    [Tooltip("Пауза в открытом положении перед возвратом (если Keep Door Open выключено), сек.")]
    [SerializeField, Min(0f)] private float doorWaitBeforeReturn = 2f;

    [Header("Звук двери")]
    [Tooltip("Клипы движения двери (Move_Wall_0…3). Если пусто — используется тот же сгенерированный звук, что у стен.")]
    [SerializeField] private AudioClip[] doorMoveClips;
    [Tooltip("Источник звука двери. Если пусто — создастся автоматически (3D) на двери.")]
    [SerializeField] private AudioSource doorMoveAudioSource;
    [SerializeField, Range(0f, 1f)] private float doorMoveVolume = 0.8f;
    [Tooltip("Диапазон случайной высоты тона, чтобы звук не повторялся.")]
    [SerializeField] private Vector2 doorMovePitchRange = new Vector2(0.92f, 1.08f);
    [Tooltip("Плавное появление звука в начале движения, сек.")]
    [SerializeField, Min(0f)] private float doorMoveFadeIn = 0.12f;
    [Tooltip("Плавное затухание звука в конце движения, сек.")]
    [SerializeField, Min(0f)] private float doorMoveFadeOut = 0.18f;

    [Header("После успеха")]
    [Tooltip("Больше не реагировать на игрока после верного кода.")]
    [SerializeField] private bool lockAfterSuccess = true;

    [Header("Тест")]
    [Tooltip("Сразу включить режим ввода при старте — удобно проверить монитор без настройки взаимодействия.")]
    [SerializeField] private bool startInInputMode = false;

    private readonly StringBuilder input = new StringBuilder();

    private const string DoorSoundChildName = "DoorMoveSound";

    private bool isEntering;
    private bool solved;
    private bool isVerifying;
    private Transform player;
    private Coroutine messageRoutine;
    private Coroutine submitRoutine;
    private Coroutine doorRoutine;
    private Coroutine doorSoundRoutine;
    private Vector3[] doorStartPositions;
    private Color displayBaseColor = Color.white;

    /// <summary>Сейчас открыт режим ввода.</summary>
    public bool IsEntering => isEntering;

    /// <summary>Код уже введён верно.</summary>
    public bool IsSolved => solved;

    /// <summary>Строка, которую игрок ввёл в данный момент.</summary>
    public string EnteredCode => input.ToString();

    public override string InteractionPrompt
    {
        get
        {
            if (solved && lockAfterSuccess) return "Открыто";
            return isEntering ? "Выйти" : "Ввести код";
        }
    }

    public override bool CanInteract(PlayerInteractor interactor) =>
        isActiveAndEnabled && !(solved && lockAfterSuccess);

    private void Awake()
    {
        if (display == null)
            display = GetComponentInChildren<TMP_Text>(true);

        if (display != null)
            displayBaseColor = display.color;
        else
            Debug.LogWarning("[CodeMonitor] Не найден TMP-текст для экрана. Назначьте Display в инспекторе.", this);

        CaptureDoorPositions();

        // Если своих клипов нет — заранее готовим сгенерированный звук движения,
        // чтобы он не создавался с задержкой в момент открытия двери.
        if (doorMoveClips == null || doorMoveClips.Length == 0)
            _ = MoveSound.GeneratedMoveClip;

        RefreshDisplay();
    }

    private void Start()
    {
        if (startInInputMode) BeginInput();
    }

    protected override void OnDisable()
    {
        base.OnDisable();

        // Монитор выключили во время паузы перед результатом — отменяем проверку.
        if (submitRoutine != null)
        {
            StopCoroutine(submitRoutine);
            submitRoutine = null;
        }

        isVerifying = false;
    }

    private void Update()
    {
        if (!isEntering) return;

        // Если игрок отошёл — закрываем ввод, чтобы клавиатура не «залипала».
        if (player != null && exitDistance > 0f &&
            Vector3.Distance(player.position, transform.position) > exitDistance)
        {
            EndInput();
            return;
        }

        if (!acceptKeyboardInput) return;

        if (Input.GetKeyDown(KeyCode.Backspace)) Backspace();
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Submit();
        if (Input.GetKeyDown(KeyCode.Escape)) EndInput();

        for (int digit = 0; digit <= 9; digit++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha0 + digit) ||
                Input.GetKeyDown(KeyCode.Keypad0 + digit))
            {
                PressDigit(digit);
            }
        }
    }

    public override void Interact(PlayerInteractor interactor)
    {
        if (solved && lockAfterSuccess) return;

        if (isEntering)
            EndInput();
        else
            BeginInput(interactor);
    }

    /// <summary>Включает режим ввода и очищает предыдущий ввод.</summary>
    public void BeginInput(PlayerInteractor interactor = null)
    {
        if (solved && lockAfterSuccess) return;

        isEntering = true;

        if (interactor != null)
            player = interactor.transform;

        input.Clear();
        RefreshDisplay();
    }

    /// <summary>Выключает режим ввода и сбрасывает ввод.</summary>
    public void EndInput()
    {
        isEntering = false;

        // Если игрок вышел из режима ввода до показа результата — отменяем проверку.
        if (submitRoutine != null)
        {
            StopCoroutine(submitRoutine);
            submitRoutine = null;
        }

        isVerifying = false;

        input.Clear();
        RefreshDisplay();
    }

    /// <summary>Добавляет цифру к введённому коду.</summary>
    public void PressDigit(int digit)
    {
        if (solved && lockAfterSuccess) return;
        if (isVerifying) return;
        if (digit < 0 || digit > 9) return;

        int codeLength = SecretCodeManager.Instance.Length;
        if (input.Length >= codeLength) return;

        input.Append(digit);
        RefreshDisplay();

        if (autoSubmitWhenFull && input.Length >= codeLength)
            Submit();
    }

    /// <summary>Перегрузка для кнопок UI: в событие можно передать цифру строкой.</summary>
    public void PressDigit(string digit)
    {
        if (int.TryParse(digit, out int value))
            PressDigit(value);
    }

    /// <summary>Удаляет последнюю введённую цифру.</summary>
    public void Backspace()
    {
        if (solved && lockAfterSuccess) return;
        if (isVerifying) return;
        if (input.Length == 0) return;

        input.Length--;
        RefreshDisplay();
    }

    /// <summary>Полностью очищает ввод.</summary>
    public void Clear()
    {
        if (isVerifying) return;

        input.Clear();
        RefreshDisplay();
    }

    /// <summary>Проверяет введённый код. Вызывается по Enter или кнопкой «ОК».</summary>
    public void Submit()
    {
        if (solved && lockAfterSuccess) return;
        if (input.Length == 0) return;
        if (isVerifying) return;

        string entered = input.ToString();
        bool correct = SecretCodeManager.Instance.Verify(entered);

        if (messageDelay > 0f)
        {
            // Оставляем введённые цифры (или звёздочки) на экране на messageDelay секунд,
            // и только потом показываем результат — так видно, что именно было введено.
            isVerifying = true;
            submitRoutine = StartCoroutine(FinishSubmitRoutine(correct));
        }
        else
        {
            FinishSubmit(correct);
        }
    }

    private IEnumerator FinishSubmitRoutine(bool correct)
    {
        yield return new WaitForSeconds(messageDelay);

        submitRoutine = null;
        isVerifying = false;

        FinishSubmit(correct);
    }

    private void FinishSubmit(bool correct)
    {
        if (correct)
        {
            solved = true;
            isEntering = false;
            input.Clear();

            ShowMessage(successMessage, successColor);
            OpenDoors();
        }
        else
        {
            input.Clear();

            ShowMessage(wrongMessage, errorColor);
        }
    }

    /// <summary>Открывает двери, привязанные к монитору, по настройкам перемещения ниже.</summary>
    public void OpenDoors()
    {
        if (doorsToOpen == null || doorsToOpen.Length == 0) return;
        if (doorMoveDistance <= 0f) return;
        if (doorRoutine != null) return;

        if (doorStartPositions == null || doorStartPositions.Length != doorsToOpen.Length)
            CaptureDoorPositions();

        doorRoutine = StartCoroutine(MoveDoorsRoutine());
    }

    /// <summary>Запоминает исходные позиции дверей, чтобы открывать их относительно старта.</summary>
    private void CaptureDoorPositions()
    {
        doorStartPositions = new Vector3[doorsToOpen != null ? doorsToOpen.Length : 0];

        for (int i = 0; i < doorStartPositions.Length; i++)
        {
            if (doorsToOpen[i] != null)
                doorStartPositions[i] = doorsToOpen[i].position;
        }
    }

    /// <summary>Вектор, на который уезжает дверь при открытии.</summary>
    private Vector3 GetDoorOffset(Transform door)
    {
        Vector3 direction;

        switch (doorMoveDirection)
        {
            case WallMoveDirection.Up: direction = Vector3.up; break;
            case WallMoveDirection.Left: direction = Vector3.left; break;
            case WallMoveDirection.Right: direction = Vector3.right; break;
            case WallMoveDirection.Forward: direction = Vector3.forward; break;
            case WallMoveDirection.Backward: direction = Vector3.back; break;
            case WallMoveDirection.Custom: direction = doorCustomDirection; break;
            default: direction = Vector3.down; break;
        }

        direction = direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector3.down;

        if (doorDirectionIsLocal && door != null)
            direction = door.TransformDirection(direction);

        return direction * doorMoveDistance;
    }

    private IEnumerator MoveDoorsRoutine()
    {
        yield return MoveDoors(true);

        if (!keepDoorOpen)
        {
            if (doorWaitBeforeReturn > 0f)
                yield return new WaitForSeconds(doorWaitBeforeReturn);

            yield return MoveDoors(false);
        }

        doorRoutine = null;
    }

    private IEnumerator MoveDoors(bool open)
    {
        // Двери уже стоят на месте — не двигаем и не включаем звук.
        if (!AnyDoorNeedsMove(open))
            yield break;

        StartDoorSound();

        bool anyMoving = true;

        while (anyMoving)
        {
            anyMoving = false;

            for (int i = 0; i < doorsToOpen.Length; i++)
            {
                Transform door = doorsToOpen[i];
                if (door == null || doorStartPositions == null || i >= doorStartPositions.Length)
                    continue;

                Vector3 target = open
                    ? doorStartPositions[i] + GetDoorOffset(door)
                    : doorStartPositions[i];

                if ((door.position - target).sqrMagnitude <= 0.0001f)
                    continue;

                door.position = Vector3.MoveTowards(
                    door.position,
                    target,
                    doorMoveSpeed * Time.deltaTime);

                anyMoving = true;
            }

            if (anyMoving)
                yield return null;
        }

        StopDoorSound();
    }

    /// <summary>Есть ли дверь, которую нужно двигать в этом направлении.</summary>
    private bool AnyDoorNeedsMove(bool open)
    {
        if (doorsToOpen == null || doorStartPositions == null)
            return false;

        for (int i = 0; i < doorsToOpen.Length; i++)
        {
            Transform door = doorsToOpen[i];
            if (door == null || i >= doorStartPositions.Length)
                continue;

            Vector3 target = open
                ? doorStartPositions[i] + GetDoorOffset(door)
                : doorStartPositions[i];

            if ((door.position - target).sqrMagnitude > 0.0001f)
                return true;
        }

        return false;
    }

    // ================= ЗВУК ДВИЖЕНИЯ ДВЕРИ =================

    /// <summary>
    /// Источник звука движения. По возможности создаётся на самой двери (звук идёт от неё),
    /// иначе — на мониторе.
    /// </summary>
    private void EnsureDoorAudio()
    {
        if (doorMoveAudioSource != null && doorMoveAudioSource.gameObject.activeInHierarchy)
            return;

        Transform host = transform;

        if (doorsToOpen != null)
        {
            foreach (Transform door in doorsToOpen)
            {
                if (door == null || !door.gameObject.activeInHierarchy) continue;

                host = door;
                break;
            }
        }

        Transform existing = host.Find(DoorSoundChildName);
        AudioSource source = existing != null ? existing.GetComponent<AudioSource>() : null;

        if (source == null)
        {
            GameObject soundObject = new GameObject(DoorSoundChildName);
            soundObject.transform.SetParent(host, false);
            source = soundObject.AddComponent<AudioSource>();
            source.spatialBlend = 1f; // 3D-звук
            source.minDistance = 2f;
            source.maxDistance = 30f;
            source.dopplerLevel = 0f; // дверь едет — не «воем» от эффекта Доплера
        }

        source.enabled = true;
        source.playOnAwake = false;
        source.loop = true;
        source.volume = 0f;

        doorMoveAudioSource = source;
    }

    private void StartDoorSound()
    {
        EnsureDoorAudio();
        if (doorMoveAudioSource == null) return;

        AudioClip clip = MoveSound.GetRandomClip(doorMoveClips);
        if (clip == null) return;

        if (doorSoundRoutine != null)
        {
            StopCoroutine(doorSoundRoutine);
            doorSoundRoutine = null;
        }

        doorMoveAudioSource.Stop();
        doorMoveAudioSource.clip = clip;
        doorMoveAudioSource.loop = true;
        doorMoveAudioSource.pitch = Random.Range(doorMovePitchRange.x, doorMovePitchRange.y);
        doorMoveAudioSource.volume = 0f;
        doorMoveAudioSource.Play();

        doorSoundRoutine = StartCoroutine(FadeDoorSound(doorMoveVolume, doorMoveFadeIn, false));
    }

    private void StopDoorSound()
    {
        if (doorMoveAudioSource == null || !doorMoveAudioSource.isPlaying)
            return;

        if (doorSoundRoutine != null)
        {
            StopCoroutine(doorSoundRoutine);
            doorSoundRoutine = null;
        }

        if (doorMoveFadeOut <= 0f)
        {
            doorMoveAudioSource.Stop();
            doorMoveAudioSource.volume = 0f;
            return;
        }

        doorSoundRoutine = StartCoroutine(FadeDoorSound(0f, doorMoveFadeOut, true));
    }

    private IEnumerator FadeDoorSound(float targetVolume, float duration, bool stopAfter)
    {
        float startVolume = doorMoveAudioSource != null ? doorMoveAudioSource.volume : 0f;
        float time = 0f;

        while (time < duration && doorMoveAudioSource != null)
        {
            time += Time.deltaTime;
            doorMoveAudioSource.volume = Mathf.Lerp(startVolume, targetVolume, time / duration);
            yield return null;
        }

        if (doorMoveAudioSource != null)
        {
            doorMoveAudioSource.volume = targetVolume;

            if (stopAfter)
                doorMoveAudioSource.Stop();
        }

        doorSoundRoutine = null;
    }

    private void RefreshDisplay()
    {
        if (messageRoutine != null)
        {
            StopCoroutine(messageRoutine);
            messageRoutine = null;
        }

        if (display == null) return;

        if (input.Length == 0)
            display.text = emptyDisplay;
        else
            display.text = maskDigits ? new string('*', input.Length) : input.ToString();

        display.color = displayBaseColor;
    }

    private void ShowMessage(string message, Color color)
    {
        if (messageRoutine != null)
            StopCoroutine(messageRoutine);

        messageRoutine = StartCoroutine(ShowMessageRoutine(message, color));
    }

    private IEnumerator ShowMessageRoutine(string message, Color color)
    {
        if (display != null)
        {
            display.text = message;
            display.color = color;
        }

        if (messageDuration > 0f)
            yield return new WaitForSeconds(messageDuration);

        messageRoutine = null;

        // После успеха сообщение остаётся на экране, после ошибки — возвращаем ввод.
        if (!solved) RefreshDisplay();
    }
}


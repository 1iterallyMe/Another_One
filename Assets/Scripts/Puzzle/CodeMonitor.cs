using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

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

    [Header("Ввод")]
    [Tooltip("Принимать цифры с клавиатуры, когда режим ввода активен.")]
    [SerializeField] private bool acceptKeyboardInput = true;
    [Tooltip("Проверять код сразу после ввода последней цифры.")]
    [SerializeField] private bool autoSubmitWhenFull = true;
    [Tooltip("Выйти из режима ввода, если игрок отошёл дальше этого расстояния (м). 0 — не проверять.")]
    [SerializeField, Min(0f)] private float exitDistance = 4f;

    [Header("События")]
    [Tooltip("Код верный: можно открыть дверь, включить свет и т.п.")]
    [SerializeField] private UnityEvent onCodeAccepted;
    [Tooltip("Код неверный.")]
    [SerializeField] private UnityEvent onCodeRejected;
    [Tooltip("Любая отправка кода: передаётся введённая строка.")]
    [SerializeField] private UnityEvent<string> onCodeSubmitted;

    [Header("После успеха")]
    [Tooltip("Больше не реагировать на игрока после верного кода.")]
    [SerializeField] private bool lockAfterSuccess = true;

    [Header("Тест")]
    [Tooltip("Сразу включить режим ввода при старте — удобно проверить монитор без настройки взаимодействия.")]
    [SerializeField] private bool startInInputMode = false;

    private readonly StringBuilder input = new StringBuilder();

    private bool isEntering;
    private bool solved;
    private Transform player;
    private Coroutine messageRoutine;
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

        RefreshDisplay();
    }

    private void Start()
    {
        if (startInInputMode) BeginInput();
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
        input.Clear();
        RefreshDisplay();
    }

    /// <summary>Добавляет цифру к введённому коду.</summary>
    public void PressDigit(int digit)
    {
        if (solved && lockAfterSuccess) return;
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
        if (input.Length == 0) return;

        input.Length--;
        RefreshDisplay();
    }

    /// <summary>Полностью очищает ввод.</summary>
    public void Clear()
    {
        input.Clear();
        RefreshDisplay();
    }

    /// <summary>Проверяет введённый код. Вызывается по Enter или кнопкой «ОК».</summary>
    public void Submit()
    {
        if (solved && lockAfterSuccess) return;
        if (input.Length == 0) return;

        string entered = input.ToString();
        onCodeSubmitted?.Invoke(entered);

        if (SecretCodeManager.Instance.Verify(entered))
        {
            solved = true;
            isEntering = false;
            input.Clear();

            ShowMessage(successMessage, successColor);
            onCodeAccepted?.Invoke();
        }
        else
        {
            input.Clear();

            ShowMessage(wrongMessage, errorColor);
            onCodeRejected?.Invoke();
        }
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


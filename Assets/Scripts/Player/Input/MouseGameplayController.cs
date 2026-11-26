using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Мышиное управление: игра проходится целиком мышью.
///
/// Режим работы (курсор всегда виден):
///  * перетаскивание ЛКМ по пустому месту — осмотр (обзор камеры);
///  * короткий клик ЛКМ по объекту — взаимодействие с ним;
///  * короткий клик ЛКМ по полу — переход в эту точку (click-to-move);
///  * ПКМ — поворот предмета в руках (как раньше);
///  * экранные кнопки (MouseHud) — прыжок, присед, бег, взять, бросить;
///  * экранная клавиатура (MouseHud) — ввод кода от двери.
///
/// Клавиатура (WASD, Space, E, F) продолжает работать как ДОПОЛНИТЕЛЬНЫЙ способ.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public class MouseGameplayController : MonoBehaviour
{
    [Header("Ссылки")]
    [SerializeField] private FPSController controller;
    [SerializeField] private PlayerInteractor interactor;
    [SerializeField] private Camera viewCamera;

    [Header("Клик по точке (перемещение)")]
    [Tooltip("Слои, по которым ищется точка назначения. Слой игрока (3) исключён.")]
    [SerializeField] private LayerMask groundMask = ~(1 << 3);
    [SerializeField] private float stopDistance = 0.4f;
    [SerializeField] private float maxMoveDistance = 30f;

    [Header("Обзор перетаскиванием")]
    [Tooltip("Сколько пикселей нужно протянуть, чтобы жест стал обзором, а не кликом.")]
    [SerializeField] private float dragThresholdPixels = 8f;
    [Tooltip("Максимальная длительность клика, после которого жест считается протяжкой.")]
    [SerializeField] private float clickMaxSeconds = 0.5f;
    [Tooltip("Сколько градусов поворота камеры даёт один пиксель протяжки.")]
    [SerializeField] private float dragLookDegreesPerPixel = 0.2f;

    private Vector3 destination;
    private bool hasDestination;
    private bool pointerDown;
    private bool dragging;
    private bool pressOverUi;
    private Vector2 pressPosition;
    private float pressTime;
    private Vector2 appliedDrag;

    /// <summary>Сейчас игрок тянет мышью — идёт осмотр.</summary>
    public bool IsDragging => dragging;

    /// <summary>Задана ли точка назначения (click-to-move).</summary>
    public bool HasDestination => hasDestination;

    void Awake()
    {
        if (!controller) controller = GetComponent<FPSController>();
        if (!interactor) interactor = GetComponent<PlayerInteractor>();
        if (!viewCamera) viewCamera = GetComponentInChildren<Camera>();
    }

    void Start()
    {
        // Курсор всегда виден: весь интерфейс и весь игровой процесс — мышью.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Обзор ведёт этот контроллер (протяжка мышью), а не legacy-Input.
        if (controller != null) controller.UseExternalLook = true;

        HideCrosshair();
    }

    /// <summary>
    /// Прицел в центре экрана не нужен при мышином управлении — прячем его,
    /// чтобы не вводить игрока в заблуждение.
    /// </summary>
    void HideCrosshair()
    {
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            foreach (Transform child in canvas.transform)
            {
                if (child.name != "Crosshair") continue;
                child.gameObject.SetActive(false);
                return;
            }
        }
    }

    void Update()
    {
        HandlePointer();
        HandleMoveToDestination();
        UpdateFocusFromCursor();
    }

    /// <summary>
    /// Фокус взаимодействия считается по лучу ИЗ КУРСОРА: при мышином
    /// управлении прицел в центре экрана не нужен — игрок наводится на объект
    /// курсором, как в обычных играх от первого лица.
    /// </summary>
    void UpdateFocusFromCursor()
    {
        if (interactor == null || viewCamera == null) return;

        // Пока предмет крутят правой кнопкой — не меняем цель.
        if (dragging) return;

        interactor.UpdateFocusFromRay(viewCamera.ScreenPointToRay(Input.mousePosition));
    }

    // ================= МЫШЬ =================

    void HandlePointer()
    {
        if (Input.GetMouseButtonDown(0))
        {
            pointerDown = true;
            dragging = false;
            pressPosition = (Vector2)Input.mousePosition;
            pressTime = Time.unscaledTime;
            appliedDrag = Vector2.zero;
            pressOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        if (pointerDown && Input.GetMouseButton(0))
        {
            Vector2 delta = (Vector2)Input.mousePosition - pressPosition;

            if (!dragging && delta.magnitude >= dragThresholdPixels)
                dragging = true;

            if (dragging)
            {
                Vector2 step = delta - appliedDrag;
                appliedDrag = delta;

                if (controller != null)
                {
                    controller.ApplyLookDegrees(
                        step.x * dragLookDegreesPerPixel,
                        step.y * dragLookDegreesPerPixel);
                }
            }
        }

        if (pointerDown && !Input.GetMouseButton(0))
        {
            pointerDown = false;
            bool wasDrag = dragging;
            dragging = false;

            // Короткое нажатие без протяжки и не по интерфейсу — это клик по миру.
            if (!wasDrag && !pressOverUi && Time.unscaledTime - pressTime <= clickMaxSeconds)
                HandleWorldClick(pressPosition);
        }
    }

    /// <summary>Клик мышью: сначала интерактивный объект, иначе — точка на полу.</summary>
    void HandleWorldClick(Vector2 screenPosition)
    {
        if (viewCamera == null) return;

        Ray ray = viewCamera.ScreenPointToRay(screenPosition);

        if (interactor != null)
        {
            IInteractable target = interactor.FindTarget(ray, interactor.InteractDistance);
            if (target != null)
            {
                interactor.InteractWith(target);
                return;
            }
        }

        if (!Physics.Raycast(ray, out RaycastHit hit, 200f, groundMask, QueryTriggerInteraction.Ignore))
            return;

        if (hit.collider != null && hit.collider.transform.IsChildOf(transform))
            return;

        Vector3 point = hit.point;
        point.y = transform.position.y;
        SetDestination(point);
    }

    void SetDestination(Vector3 point)
    {
        Vector3 delta = point - transform.position;
        delta.y = 0f;

        if (delta.magnitude > maxMoveDistance)
            point = transform.position + delta.normalized * maxMoveDistance;

        destination = point;
        hasDestination = true;
    }

    void HandleMoveToDestination()
    {
        if (!hasDestination || controller == null) return;

        Vector3 delta = destination - transform.position;
        delta.y = 0f;

        if (delta.magnitude <= stopDistance)
        {
            controller.StopExternalMovement();
            hasDestination = false;
            return;
        }

        controller.SetExternalMoveDirection(delta);
    }

    // ================= ДЕЙСТВИЯ ДЛЯ ЭКРАННЫХ КНОПОК =================

    /// <summary>Кнопка «ВЗЯТЬ» — взаимодействие с текущей целью.</summary>
    public void RequestInteract()
    {
        if (interactor == null || interactor.Focused == null) return;
        interactor.InteractWith(interactor.Focused);
    }

    /// <summary>Кнопка «БРОСИТЬ» — бросить предмет или применить его.</summary>
    public void RequestUseOrThrow()
    {
        if (interactor != null) interactor.UseOrThrow();
    }

    /// <summary>Кнопка «ПРЫЖОК».</summary>
    public void RequestJump()
    {
        if (controller != null) controller.RequestJump();
    }

    /// <summary>Кнопка «ПРИСЕСТЬ» — переключатель.</summary>
    public void ToggleCrouch()
    {
        if (controller != null) controller.SetExternalCrouch(!controller.ExternalCrouchEnabled);
    }

    /// <summary>Кнопка «БЕГ» — переключатель.</summary>
    public void ToggleSprint()
    {
        if (controller != null) controller.SetExternalSprint(!controller.ExternalSprintEnabled);
    }
}

using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class FPSController : MonoBehaviour
{
    [Header("Camera & Look")]
    public Camera playerCamera;
    public Transform cameraJoint;
    public float mouseSensitivity = 2f;
    public float maxLookAngle = 80f;
    public bool invertY = false;
    public bool lockCursor = true;
    public KeyCode unlockCursorKey = KeyCode.Escape;

    [Header("Body & Head (Naklon)")]
    [Tooltip("Кость Torso, которая будет наклоняться")]
    public Transform torsoPivot;
    [Tooltip("Точка внутри головы (HeadTarget), за которой будет летать камера")]
    public Transform headTarget;
    [Tooltip("Физическая кость головы, которая будет крутиться вверх/вниз")]
    public Transform physicalHead;

    [Tooltip("Максимальный угол поворота физической головы (чтобы шея не ломалась)")]
    public float maxHeadPitch = 50f;

    [Header("Body Pitch Settings")]
    [Tooltip("Кривая наклона тела. По оси X: 0 - смотрим прямо, 1 - смотрим в самый пол. По оси Y: сила наклона тела (0-1).")]
    public AnimationCurve torsoBlendCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.4f, 0.1f),
        new Keyframe(1f, 1f)
    );

    [Tooltip("Общая сила наклона торса. 1 = наклоняется ровно как камера, 0.5 = слабее")]
    [Range(0f, 1.5f)] public float torsoPitchWeight = 0.8f;

    public float torsoMaxDown = 70f;
    public float torsoMaxUp = 60f;
    public Vector3 torsoAxis = Vector3.right;

    [Tooltip("Плавность следования камеры за головой")]
    public float cameraFollowSpeed = 20f;

    [Header("Holding Weight")]
    [Tooltip("Контроллер удержания предметов. Если пусто — найдётся на этом же объекте.")]
    [SerializeField] private PhysicsHoldController physicsHold;
    [Tooltip("Инвентарь игрока, чтобы не вращать камеру во время поворота предмета.")]
    [SerializeField] private GrabManager grabManager;

    [Header("Movement")]
    public float walkSpeed = 5f;
    public float sprintSpeed = 8f;
    public float crouchSpeed = 2.5f;
    public float acceleration = 10f;
    public float deceleration = 10f;
    public float maxSlopeAngle = 50f;
    public KeyCode sprintKey = KeyCode.LeftShift;

    [Header("Jump & Airborne")]
    public float jumpForce = 5f;
    public float jumpBufferTime = 0.15f;
    public float coyoteTime = 0.15f;
    [Tooltip("Клавиша прыжка.")]
    public KeyCode jumpKey = KeyCode.Space;
    [Tooltip("Если включено — удержание клавиши прыжка заставляет прыгать постоянно.")]
    public bool holdToJump = true;

    [Header("Jump Sound")]
    [Tooltip("Несколько звуков прыжка — каждый раз играет случайный. Также используются батутом, если у батута нет своих звуков.")]
    [SerializeField] private AudioClip[] jumpClips;
    [Tooltip("Источник звука прыжка. Если пусто — создастся автоматически.")]
    [SerializeField] private AudioSource jumpAudioSource;
    [SerializeField, Range(0f, 1f)] private float jumpVolume = 0.8f;
    [Tooltip("Диапазон случайной высоты тона, чтобы звуки не повторялись.")]
    [SerializeField] private Vector2 jumpPitchRange = new Vector2(0.95f, 1.05f);
    [SerializeField, Min(0f)] private float jumpSoundCooldown = 0.05f;

    [Header("Crouch Physics")]
    public KeyCode crouchKey = KeyCode.LeftControl;
    public bool holdToCrouch = true;
    [Range(0.1f, 1f)] public float crouchHeightMultiplier = 0.6f;
    public float crouchLerpSpeed = 12f;

    [Header("Grounding")]
    public LayerMask groundMask = ~0;
    public float groundCheckRadiusMultiplier = 0.9f;
    public float groundCheckDistance = 0.15f;
    public float snapForce = 15f;

    [Header("Animation")]
    public Animator animator;
    [Tooltip("Необязательный триггер анимации прыжка/отскока. Оставь пустым, если в Animator нет такого параметра.")]
    public string jumpTrigger = "";

    // Публичное состояние для PlayerFootsteps и других систем
    public bool IsGrounded => isGrounded;
    public bool IsCrouched => isCrouched;

    /// <summary>Игрок нажал/держит прыжок (в пределах буфера).</summary>
    public bool WantsToJump => jumpBufferCounter > 0f;

    /// <summary>Под ногами сейчас батут.</summary>
    public bool IsOnTrampoline =>
        currentGroundCollider != null &&
        currentGroundCollider.GetComponentInParent<Trampoline>() != null;
    public float HorizontalSpeed
    {
        get
        {
            if (rb == null) return 0f;
            Vector3 v = rb.linearVelocity;
            v.y = 0f;
            return v.magnitude;
        }
    }

    private Rigidbody rb;
    private CapsuleCollider col;

    private float yaw;
    private float pitch;
    private Vector3 moveInput;
    private bool isSprinting;
    private bool isCrouched;
    private bool isGrounded;
    private float currentSlopeAngle;
    private Vector3 groundNormal;
    private float jumpBufferCounter;
    private float coyoteTimeCounter;
    private float lastJumpTime;
    private float suppressGroundUntil;
    private Collider currentGroundCollider;

    private float nextJumpSoundTime;
    private int lastJumpClipIndex = -1;

    private const float TrampolineFallSpeed = 1f;
    private float originalHeight;
    private Vector3 originalCenter;

    private float smoothedTorsoPitch;
    private Quaternion lastTorsoResult;
    private Quaternion lastTorsoOffset = Quaternion.identity;
    private bool hasLastTorso;

    private float smoothedHeadPitch;
    private Quaternion lastHeadResult;
    private Quaternion lastHeadOffset = Quaternion.identity;
    private bool hasLastHead;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<CapsuleCollider>();

        rb.freezeRotation = true;
        rb.useGravity = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;

        if (!playerCamera) playerCamera = GetComponentInChildren<Camera>();
        if (!cameraJoint) cameraJoint = playerCamera != null ? playerCamera.transform : transform;
        if (!physicsHold) physicsHold = GetComponent<PhysicsHoldController>();
        if (!grabManager) grabManager = GetComponent<GrabManager>();
        if (jumpAudioSource != null)
        {
            jumpAudioSource.playOnAwake = false;
            jumpAudioSource.loop = false;
        }

        originalHeight = col.height;
        originalCenter = col.center;

        // Своя капсула (слой Player) не должна попадать в проверку земли.
        groundMask &= ~(1 << 3);

        // Стартовый поворот берём из сцены: иначе первый же кадр Look()
        // обнулял yaw и игрок оказывался развёрнут не туда, куда поставлен.
        yaw = transform.eulerAngles.y;
    }

    void Start()
    {
        if (cameraJoint == null) cameraJoint = transform;

        ApplyCursorState();
    }

    void OnEnable()
    {
        Application.focusChanged += OnApplicationFocusChanged;
    }

    void OnDisable()
    {
        Application.focusChanged -= OnApplicationFocusChanged;
    }

    // macOS снимает захват курсора, когда окно теряет фокус, и не возвращает его сам.
    // Без этого игрок возвращается в окно и не может ни взаимодействовать, ни крутить камеру.
    void OnApplicationFocusChanged(bool focused)
    {
        if (focused)
            ApplyCursorState();
    }

    /// <summary>Привести курсор к состоянию lockCursor (захвачен/отпущен).</summary>
    void ApplyCursorState()
    {
        Cursor.lockState = lockCursor ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !lockCursor;
    }

    void Update()
    {
        if (Input.GetKeyDown(unlockCursorKey))
        {
            lockCursor = !lockCursor;
            ApplyCursorState();
        }

        // Клик по окну после потери фокуса возвращает захват курсора,
        // иначе игра остаётся с «мёртвым» курсором до повторного Esc.
        if (lockCursor && Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0))
            ApplyCursorState();

        // Пока игрок вращает предмет правой кнопкой мыши — камера остаётся на месте.
        bool rotatingHeldItem = (grabManager != null && grabManager.IsRotatingEquippedItem) ||
                                (physicsHold != null && physicsHold.IsRotatingHeldItem);

        if (!rotatingHeldItem)
            Look();

        GatherInput();

        if (animator != null)
        {
            Vector2 horizontalVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.z);
            animator.SetFloat("Speed", horizontalVelocity.magnitude);
            animator.SetBool("IsCrouching", isCrouched);
        }
    }

    void LateUpdate()
    {
        HandlePhysicalHead();
        HandleTorsoPitch();
        HandleCameraFollow();
    }

    void FixedUpdate()
    {
        // После батута временно не считаем игрока на земле, иначе Move() погасит вертикальную скорость.
        if (Time.time < suppressGroundUntil)
        {
            isGrounded = false;
            groundNormal = Vector3.up;
            coyoteTimeCounter = 0f;
        }
        else
        {
            CheckGround();
        }

        // Падение на батут не гасим и не превращаем в обычный прыжок —
        // пусть столкновение обработает сам батут.
        if (IsOnTrampoline && rb.linearVelocity.y < -TrampolineFallSpeed)
        {
            isGrounded = false;
            coyoteTimeCounter = 0f;
        }

        HandleCrouchPhysics();
        HandleJump();
        Move();
        SnapToGround();
    }

    void OnValidate()
    {
        mouseSensitivity = Mathf.Max(0.01f, mouseSensitivity);
        maxLookAngle = Mathf.Clamp(maxLookAngle, 1f, 90f);
        walkSpeed = Mathf.Max(0f, walkSpeed);
        sprintSpeed = Mathf.Max(0f, sprintSpeed);
        crouchSpeed = Mathf.Max(0f, crouchSpeed);
        jumpForce = Mathf.Max(0f, jumpForce);
        crouchHeightMultiplier = Mathf.Clamp(crouchHeightMultiplier, 0.1f, 1f);
        crouchLerpSpeed = Mathf.Max(0.1f, crouchLerpSpeed);
        maxSlopeAngle = Mathf.Clamp(maxSlopeAngle, 10f, 85f);
    }

    /// <summary>
    /// Принудительно задать горизонтальный угол взгляда.
    /// Используется зиплайном: во время поездки контроллер выключен, но yaw нужно
    /// синхронизировать, чтобы после отцепления камера не «прыгнула» обратно.
    /// </summary>
    public void SetYaw(float newYaw)
    {
        yaw = newYaw;
        transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
    }

    void Look()
    {
        float mx = Input.GetAxis("Mouse X") * mouseSensitivity;
        float my = Input.GetAxis("Mouse Y") * mouseSensitivity;

        yaw += mx;
        transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

        pitch += invertY ? my : -my;
        pitch = Mathf.Clamp(pitch, -maxLookAngle, maxLookAngle);

        if (cameraJoint != null) cameraJoint.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    void HandlePhysicalHead()
    {
        if (physicalHead == null) return;

        float targetHeadPitch = Mathf.Clamp(pitch, -maxHeadPitch, maxHeadPitch);
        smoothedHeadPitch = Mathf.Lerp(smoothedHeadPitch, targetHeadPitch, 1f - Mathf.Exp(-25f * Time.deltaTime));

        Quaternion current = physicalHead.localRotation;
        Quaternion baseRot = hasLastHead && Quaternion.Angle(current, lastHeadResult) < 0.01f
            ? current * Quaternion.Inverse(lastHeadOffset)
            : current;

        Quaternion offset = Quaternion.AngleAxis(smoothedHeadPitch, torsoAxis.normalized);
        Quaternion result = baseRot * offset;

        physicalHead.localRotation = result;
        lastHeadResult = result;
        lastHeadOffset = offset;
        hasLastHead = true;
    }

    void HandleTorsoPitch()
    {
        if (torsoPivot == null) return;

        float normalizedPitch = Mathf.Abs(pitch) / maxLookAngle;
        float blendMultiplier = torsoBlendCurve.Evaluate(normalizedPitch);
        float targetPitch = pitch * torsoPitchWeight * blendMultiplier;

        targetPitch = Mathf.Clamp(targetPitch, -torsoMaxUp, torsoMaxDown);
        smoothedTorsoPitch = Mathf.Lerp(smoothedTorsoPitch, targetPitch, 1f - Mathf.Exp(-18f * Time.deltaTime));

        Quaternion current = torsoPivot.localRotation;
        Quaternion baseRot = hasLastTorso && Quaternion.Angle(current, lastTorsoResult) < 0.01f
            ? current * Quaternion.Inverse(lastTorsoOffset)
            : current;

        Quaternion offset = Quaternion.AngleAxis(smoothedTorsoPitch, torsoAxis.normalized);
        Quaternion result = baseRot * offset;

        torsoPivot.localRotation = result;
        lastTorsoResult = result;
        lastTorsoOffset = offset;
        hasLastTorso = true;
    }

    void HandleCameraFollow()
    {
        if (headTarget == null || cameraJoint == null) return;

        // Сглаживание, не зависящее от длины кадра: раньше при dt≈0.1 коэффициент
        // Time.deltaTime * 20 превышал 1, Lerp обрезался — камера прыгала к голове.
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        float t = 1f - Mathf.Exp(-cameraFollowSpeed * dt);

        cameraJoint.position = Vector3.Lerp(cameraJoint.position, headTarget.position, t);
        cameraJoint.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    void GatherInput()
    {
        moveInput = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")).normalized;
        isSprinting = Input.GetKey(sprintKey) && !isCrouched && moveInput.z > 0.1f;

        bool jumpInput = holdToJump ? Input.GetKey(jumpKey) : Input.GetKeyDown(jumpKey);
        if (jumpInput) jumpBufferCounter = jumpBufferTime;
        else jumpBufferCounter -= Time.deltaTime;

        if (holdToCrouch)
        {
            bool wantToCrouch = Input.GetKey(crouchKey);
            if (!wantToCrouch && isCrouched && CanStandUp()) isCrouched = false;
            else if (wantToCrouch) isCrouched = true;
        }
        else
        {
            if (Input.GetKeyDown(crouchKey))
            {
                if (isCrouched && CanStandUp()) isCrouched = false;
                else if (!isCrouched) isCrouched = true;
            }
        }
    }

    void CheckGround()
    {
        if (Time.time - lastJumpTime < 0.25f)
        {
            isGrounded = false;
            groundNormal = Vector3.up;
            coyoteTimeCounter = 0f;
            currentGroundCollider = null;
            return;
        }

        float radius = col.radius * groundCheckRadiusMultiplier;
        Vector3 origin = transform.position + col.center + Vector3.down * (col.height * 0.5f - radius);

        float extraCastDistance = groundCheckDistance + 0.05f;
        bool hitGround = Physics.SphereCast(origin, radius, Vector3.down, out RaycastHit hit, extraCastDistance, groundMask, QueryTriggerInteraction.Ignore);

        if (hitGround)
        {
            currentSlopeAngle = Vector3.Angle(hit.normal, Vector3.up);
            if (currentSlopeAngle <= maxSlopeAngle)
            {
                isGrounded = true;
                groundNormal = hit.normal;
                coyoteTimeCounter = coyoteTime;
                currentGroundCollider = hit.collider;
                return;
            }
        }

        isGrounded = false;
        groundNormal = Vector3.up;
        currentGroundCollider = null;
        coyoteTimeCounter -= Time.fixedDeltaTime;
    }

    /// <summary>Подбросить игрока с батута вдоль заданной нормали.</summary>
    public void TrampolineBounce(Vector3 direction, float speed)
    {
        Vector3 normal = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.up;

        Vector3 velocity = rb.linearVelocity;

        // Гасим скорость падения вдоль нормали и задаём подбрасывание.
        float along = Vector3.Dot(velocity, normal);
        if (along < 0f) velocity -= normal * along;
        velocity += normal * speed;
        rb.linearVelocity = velocity;

        isGrounded = false;
        groundNormal = Vector3.up;
        jumpBufferCounter = 0f;
        coyoteTimeCounter = 0f;
        lastJumpTime = Time.time;

        // Несколько кадров не даём Move()/SnapToGround() погасить вертикальную скорость.
        suppressGroundUntil = Time.time + 0.25f;

        PlayJumpAnimation();
    }

    private void PlayJumpAnimation()
    {
        if (animator != null && !string.IsNullOrEmpty(jumpTrigger))
            animator.SetTrigger(jumpTrigger);
    }

    /// <summary>Проиграть случайный звук прыжка.</summary>
    public void PlayJumpSound()
    {
        if (jumpClips == null || jumpClips.Length == 0) return;
        if (Time.time < nextJumpSoundTime) return;

        AudioClip clip = PickRandomClip(jumpClips, ref lastJumpClipIndex);
        if (clip == null) return;

        if (jumpAudioSource == null)
        {
            jumpAudioSource = gameObject.AddComponent<AudioSource>();
            jumpAudioSource.playOnAwake = false;
            jumpAudioSource.spatialBlend = 1f;
        }

        nextJumpSoundTime = Time.time + jumpSoundCooldown;
        jumpAudioSource.pitch = Random.Range(
            Mathf.Min(jumpPitchRange.x, jumpPitchRange.y),
            Mathf.Max(jumpPitchRange.x, jumpPitchRange.y));
        jumpAudioSource.PlayOneShot(clip, jumpVolume);
    }

    private static AudioClip PickRandomClip(AudioClip[] clips, ref int lastIndex)
    {
        if (clips == null || clips.Length == 0) return null;

        int count = clips.Length;
        int start = Random.Range(0, count);

        for (int i = 0; i < count; i++)
        {
            int idx = (start + i) % count;
            if (clips[idx] == null) continue;
            if (count > 1 && idx == lastIndex) continue;
            lastIndex = idx;
            return clips[idx];
        }

        // Если все клипы пустые или доступен только последний — вернём любой валидный.
        for (int i = 0; i < count; i++)
        {
            if (clips[i] != null)
            {
                lastIndex = i;
                return clips[i];
            }
        }

        return null;
    }

    void HandleJump()
    {
        if (isCrouched) return;

        // С тяжёлым предметом прыгать нельзя — иначе его можно «подбросить».
        if (physicsHold != null && physicsHold.IsDraggingHeavy)
        {
            jumpBufferCounter = 0f;
            return;
        }

        // Если падаем на батут, отскок обрабатывает сам батут (с учётом нажатого прыжка).
        if (IsOnTrampoline && rb.linearVelocity.y < -TrampolineFallSpeed)
            return;

        if (jumpBufferCounter > 0f && coyoteTimeCounter > 0f)
        {
            jumpBufferCounter = 0f;
            coyoteTimeCounter = 0f;
            lastJumpTime = Time.time;

            isGrounded = false;

            Vector3 currentVel = rb.linearVelocity;
            currentVel.y = 0f;
            currentVel.y = jumpForce;
            rb.linearVelocity = currentVel;

            PlayJumpAnimation();
            PlayJumpSound();
        }
    }

    void Move()
    {
        Vector3 currentVel = rb.linearVelocity;

        // Перетаскивание тяжёлого предмета замедляет игрока и запрещает бег.
        bool draggingHeavy = physicsHold != null && physicsHold.IsDraggingHeavy;
        float speedMultiplier = physicsHold != null ? physicsHold.PlayerSpeedMultiplier : 1f;
        float baseSpeed = isCrouched
            ? crouchSpeed
            : (isSprinting && !draggingHeavy ? sprintSpeed : walkSpeed);
        float speed = baseSpeed * speedMultiplier;

        Vector3 moveDirection = Quaternion.Euler(0f, yaw, 0f) * moveInput;

        if (isGrounded)
        {
            moveDirection = Vector3.ProjectOnPlane(moveDirection, groundNormal).normalized;
            Vector3 targetVelocity = moveDirection * speed;
            Vector3 velocityChange = targetVelocity - currentVel;
            rb.AddForce(velocityChange, ForceMode.VelocityChange);
        }
        else
        {
            Vector3 targetVelocity = moveDirection * speed;
            if (moveInput.sqrMagnitude > 0.01f)
            {
                Vector3 velocityDiff = targetVelocity - currentVel;
                velocityDiff.y = 0;
                rb.AddForce(velocityDiff * (acceleration * 1.5f), ForceMode.Acceleration);
            }
            else
            {
                Vector3 horizontalVel = new Vector3(currentVel.x, 0f, currentVel.z);
                Vector3 dampingForce = -horizontalVel * deceleration;
                rb.AddForce(dampingForce, ForceMode.Acceleration);
            }
        }
    }

    void SnapToGround()
    {
        if (!isGrounded || Time.time - lastJumpTime < 0.25f) return;

        float radius = col.radius * groundCheckRadiusMultiplier;
        Vector3 origin = transform.position + col.center + Vector3.down * (col.height * 0.5f - radius);

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundCheckDistance + 0.2f, groundMask, QueryTriggerInteraction.Ignore))
        {
            // Тянем вниз только когда под ногами реальный зазор (ступенька/склон),
            // а не каждый шаг стоя на месте — иначе капсула «вдавливается» в пол.
            float gap = hit.distance - radius;
            if (gap > 0.02f && Vector3.Angle(hit.normal, Vector3.up) <= maxSlopeAngle)
            {
                rb.AddForce(-groundNormal * snapForce, ForceMode.Acceleration);
            }
        }
    }

    void HandleCrouchPhysics()
    {
        float targetHeight = isCrouched ? originalHeight * crouchHeightMultiplier : originalHeight;
        float newHeight = Mathf.Lerp(col.height, targetHeight, crouchLerpSpeed * Time.fixedDeltaTime);

        float heightDiffFromOriginal = originalHeight - newHeight;
        col.height = newHeight;
        col.center = new Vector3(originalCenter.x, originalCenter.y - heightDiffFromOriginal * 0.5f, originalCenter.z);
    }

    bool CanStandUp()
    {
        float checkDistance = originalHeight - col.height + 0.05f;
        Vector3 topOrigin = transform.position + col.center;
        float checkRadius = col.radius * 0.9f;
        return !Physics.SphereCast(topOrigin, checkRadius, Vector3.up, out _, checkDistance, groundMask, QueryTriggerInteraction.Ignore);
    }
}
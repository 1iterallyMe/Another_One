using UnityEngine;

/// <summary>
/// Батут: подбрасывает игрока или физические объекты вдоль своей локальной оси Y.
/// Работает и с обычным коллайдером (OnCollisionEnter), и с триггер-зоной (OnTriggerEnter).
/// </summary>
[RequireComponent(typeof(Collider))]
public class Trampoline : MonoBehaviour
{
    [Header("Bounce")]
    [Tooltip("Базовая скорость активного отскока, когда игрок прыгает (м/с).")]
    [SerializeField, Min(0f)] private float bounceSpeed = 10f;
    [Tooltip("Минимальная скорость падения для срабатывания (м/с). Минимум 1, чтобы просто стоящий игрок не подпрыгивал.")]
    [SerializeField, Min(0f)] private float minFallSpeed = 1f;
    [Tooltip("Пауза между срабатываниями, чтобы не было двойных срабатываний от нескольких коллайдеров.")]
    [SerializeField, Min(0f)] private float cooldown = 0.2f;
    [Tooltip("Подбрасывать игрока.")]
    [SerializeField] private bool affectPlayer = true;
    [Tooltip("Подбрасывать другие объекты с Rigidbody.")]
    [SerializeField] private bool affectOtherRigidbodies = true;
    [Tooltip("Сохранять горизонтальную скорость объектов при подбрасывании.")]
    [SerializeField] private bool keepHorizontalVelocity = true;

    [Header("Fall Softening (падение без прыжка)")]
    [Tooltip("Какая часть высоты падения возвращается без прыжка. 0.5 = упал с 6 м — отскочил на 3 м.")]
    [SerializeField, Range(0f, 1f)] private float passiveHeightRetention = 0.5f;

    [Header("Jump Combo (распрыжка)")]
    [Tooltip("На сколько м/с увеличивается отскок за каждый следующий активный прыжок.")]
    [SerializeField, Min(0f)] private float comboBounceIncrease = 1.2f;
    [Tooltip("Максимальная скорость отскока при распрыжке.")]
    [SerializeField, Min(0.01f)] private float maxBounceSpeed = 14f;
    [Tooltip("Через сколько секунд без активного отскока серия сбрасывается.")]
    [SerializeField, Min(0f)] private float comboResetTime = 5f;

    [Header("Contact Check")]
    [Tooltip("Срабатывать только если объект находится над батутом, а не сбоку.")]
    [SerializeField] private bool requireObjectAbove = true;
    [Tooltip("Минимальная высота объекта над батутом вдоль его оси Y.")]
    [SerializeField] private float minAboveHeight = 0f;

    [Header("FX")]
    [Tooltip("Несколько звуков отскока — каждый раз играет случайный. Если пусто — используется звук прыжка игрока.")]
    [SerializeField] private AudioClip[] bounceSounds;
    [Tooltip("Одиночный звук отскока. Используется, если массив Bounce Sounds пуст.")]
    [SerializeField] private AudioClip bounceSound;
    [SerializeField, Range(0f, 1f)] private float bounceVolume = 0.8f;
    [Tooltip("Диапазон случайной высоты тона, чтобы звуки не повторялись.")]
    [SerializeField] private Vector2 bouncePitchRange = new Vector2(0.95f, 1.05f);
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private Animator animator;
    [Tooltip("Имя триггера анимации батута. Оставь пустым, если анимация не нужна.")]
    [SerializeField] private string bounceTrigger = "";

    private float nextBounceTime;
    private Collider ownCollider;
    private int comboCount;
    private float lastActiveBounceTime = -999f;
    private int lastBounceClipIndex = -1;

    void Awake()
    {
        minFallSpeed = Mathf.Max(minFallSpeed, 1f);

        // Если компонент был сохранён до появления поля — восстановим разумный потолок.
        if (maxBounceSpeed <= 0f)
            maxBounceSpeed = bounceSpeed + comboBounceIncrease * 4f;

        ownCollider = GetComponent<Collider>();

        // Если Animator не назначен вручную — ищем на себе или на дочернем визуале.
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (audioSource == null && HasBounceClips())
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 1f;
        }

        if (audioSource != null)
        {
            audioSource.playOnAwake = false;
            audioSource.loop = false;
        }
    }

    void OnValidate()
    {
        minFallSpeed = Mathf.Max(minFallSpeed, 1f);
        passiveHeightRetention = Mathf.Clamp01(passiveHeightRetention);
        comboBounceIncrease = Mathf.Max(0f, comboBounceIncrease);
        maxBounceSpeed = Mathf.Max(bounceSpeed, maxBounceSpeed, 0.01f);
        comboResetTime = Mathf.Max(0f, comboResetTime);

        bounceVolume = Mathf.Clamp01(bounceVolume);
        if (bouncePitchRange.x > bouncePitchRange.y)
            bouncePitchRange = new Vector2(bouncePitchRange.y, bouncePitchRange.x);
    }

    void OnCollisionEnter(Collision collision)
    {
        if (collision.collider == null)
            return;

        Vector3 normal = GetNormal();

        // relativeVelocity — скорость до решения столкновения;
        // linearVelocity — запасной вариант, если relativeVelocity почему-то нулевая.
        float impactSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, normal));
        Rigidbody body = collision.collider.attachedRigidbody;
        if (body != null)
            impactSpeed = Mathf.Max(impactSpeed, -Vector3.Dot(body.linearVelocity, normal));

        TryBounce(collision.collider, impactSpeed);
    }

    // Если игрок просто стоит/идёт по батуту — серия распрыжки сбрасывается.
    void OnCollisionStay(Collision collision)
    {
        Rigidbody body = collision.collider != null ? collision.collider.attachedRigidbody : null;
        if (body == null)
            return;

        float verticalSpeed = Mathf.Abs(Vector3.Dot(body.linearVelocity, GetNormal()));
        if (verticalSpeed < 0.2f)
        {
            comboCount = 0;
            lastActiveBounceTime = -999f;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        TryBounce(other);
    }

    private Vector3 GetNormal()
    {
        return transform.up.sqrMagnitude > 0.0001f
            ? transform.up.normalized
            : Vector3.up;
    }

    private void TryBounce(Collider other, float impactSpeedOverride = -1f)
    {
        if (other == null || Time.time < nextBounceTime)
            return;

        Rigidbody body = other.attachedRigidbody;
        if (body == null || body.isKinematic)
            return;

        Vector3 normal = GetNormal();

        if (requireObjectAbove)
        {
            Vector3 reference = ownCollider != null ? ownCollider.bounds.center : transform.position;
            float height = Vector3.Dot(other.bounds.center - reference, normal);
            if (height < minAboveHeight)
                return;
        }

        // Скорость падения вдоль нормали (положительная — вниз).
        // Для обычного коллайдера берём relativeVelocity: после столкновения
        // linearVelocity могла уже обнулиться солвером.
        float fallSpeed = impactSpeedOverride >= 0f
            ? impactSpeedOverride
            : -Vector3.Dot(body.linearVelocity, normal);

        if (fallSpeed < minFallSpeed)
        {
            // Игрок просто стоит или идёт по батуту — ничего не делаем.
            comboCount = 0;
            lastActiveBounceTime = -999f;
            return;
        }

        FPSController player = body.GetComponent<FPSController>();
        if (player == null) player = body.GetComponentInParent<FPSController>();

        float restitution = Mathf.Sqrt(Mathf.Clamp01(passiveHeightRetention));
        float passiveSpeed = Mathf.Min(fallSpeed * restitution, maxBounceSpeed);

        if (player != null)
        {
            if (!affectPlayer)
                return;

            if (player.WantsToJump)
            {
                // Активная распрыжка: каждый следующий отскок выше.
                if (Time.time - lastActiveBounceTime > comboResetTime)
                    comboCount = 0;
                else
                    comboCount++;

                float activeSpeed = Mathf.Max(bounceSpeed, passiveSpeed) + comboCount * comboBounceIncrease;
                activeSpeed = Mathf.Min(activeSpeed, maxBounceSpeed);
                lastActiveBounceTime = Time.time;

                player.TrampolineBounce(normal, activeSpeed);
            }
            else
            {
                // Падение без прыжка: мягкий отскок (например, с 6 м на 3 м).
                comboCount = 0;
                lastActiveBounceTime = -999f;
                player.TrampolineBounce(normal, passiveSpeed);
            }
        }
        else
        {
            if (!affectOtherRigidbodies)
                return;

            Vector3 velocity = body.linearVelocity;

            // Убираем скорость вниз по нормали, чтобы не гасить отскок.
            float along = Vector3.Dot(velocity, normal);
            if (along < 0f)
                velocity -= normal * along;

            if (!keepHorizontalVelocity)
                velocity = Vector3.Project(velocity, normal);

            velocity += normal * passiveSpeed;
            body.linearVelocity = velocity;
        }

        nextBounceTime = Time.time + cooldown;

        bool playedBounceSound = PlayFeedback();
        if (!playedBounceSound && player != null)
            player.PlayJumpSound();
    }

    private bool PlayFeedback()
    {
        AudioClip clip = PickRandomBounceClip();
        bool playedSound = false;

        if (audioSource != null && clip != null)
        {
            audioSource.pitch = Random.Range(
                Mathf.Min(bouncePitchRange.x, bouncePitchRange.y),
                Mathf.Max(bouncePitchRange.x, bouncePitchRange.y));
            audioSource.PlayOneShot(clip, bounceVolume);
            playedSound = true;
        }

        if (animator != null && !string.IsNullOrEmpty(bounceTrigger))
            animator.SetTrigger(bounceTrigger);

        return playedSound;
    }

    private bool HasBounceClips()
    {
        if (bounceSound != null) return true;
        if (bounceSounds == null) return false;

        for (int i = 0; i < bounceSounds.Length; i++)
            if (bounceSounds[i] != null) return true;

        return false;
    }

    private AudioClip PickRandomBounceClip()
    {
        if (bounceSounds != null && bounceSounds.Length > 0)
        {
            AudioClip clip = PickRandomClip(bounceSounds, ref lastBounceClipIndex);
            if (clip != null) return clip;
        }

        return bounceSound;
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

    void OnDrawGizmosSelected()
    {
        Vector3 origin = transform.position;
        Vector3 up = transform.up.normalized * 2f;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(origin, origin + up);
        Gizmos.DrawWireSphere(origin + up, 0.15f);
    }
}

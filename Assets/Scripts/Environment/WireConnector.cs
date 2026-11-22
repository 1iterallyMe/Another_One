using UnityEngine;

/// <summary>
/// Визуальный провод от генератора к стартовой точке зиплайна.
///
/// Рисуется стандартным <see cref="LineRenderer"/> (без сторонних ассетов).
/// Провод виден (LineRenderer.enabled == true) только когда генератор включён,
/// то есть когда <see cref="GeneratorController.IsPowered"/> == true.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(LineRenderer))]
public class WireConnector : MonoBehaviour
{
    [Header("Ссылки")]
    [Tooltip("Генератор, от которого идёт провод. Если пусто — ищется на этом же объекте/родителе.")]
    [SerializeField] private GeneratorController generator;

    [Tooltip("Точка крепления провода на генераторе. Если пусто — берётся позиция генератора + вверх.")]
    [SerializeField] private Transform generatorAnchor;

    [Tooltip("Точка крепления провода на зиплайне (обычно startPoint зиплайна).")]
    [SerializeField] private Transform ziplineAnchor;

    [Header("Форма провода")]
    [Tooltip("Количество сегментов линии. Больше — плавнее провисание.")]
    [SerializeField, Range(2, 64)] private int segments = 16;

    [Tooltip("Насколько провод провисает в середине, метры.")]
    [SerializeField, Min(0f)] private float sag = 0.35f;

    private LineRenderer line;

    private void Awake()
    {
        line = GetComponent<LineRenderer>();

        if (generator == null) generator = GetComponentInParent<GeneratorController>();
        if (generatorAnchor == null) generatorAnchor = transform;
    }

    private void OnEnable()
    {
        // При включении объекта сразу приводим провод в корректное состояние.
        RefreshWire();
    }

    private void Update()
    {
        // Позиции генератора/зиплайна могут меняться (например, движущиеся платформы),
        // поэтому линию обновляем каждый кадр — это дешево для 16 сегментов.
        RefreshWire();
    }

    private void OnValidate()
    {
        if (segments < 2) segments = 2;
        if (sag < 0f) sag = 0f;
    }

    /// <summary>
    /// Перестроить линию и синхронизировать её видимость с состоянием генератора.
    /// </summary>
    public void RefreshWire()
    {
        if (line == null) return;

        bool powered = generator != null && generator.IsPowered;

        // Ключевое требование: провод активен только при включённом генераторе.
        line.enabled = powered;

        // Провод — это ещё и "кабель питания" в системе Александра:
        // пока он виден, FuelGenerator считает кабель подключённым (HasPower).
        if (generator != null) generator.SetCableConnected(powered);

        if (!powered) return;

        line.useWorldSpace = true;
        line.positionCount = segments;

        Vector3 from = generatorAnchor != null
            ? generatorAnchor.position
            : (generator != null ? generator.transform.position + Vector3.up * 0.9f : transform.position);

        Vector3 to = ziplineAnchor != null
            ? ziplineAnchor.position
            : from + transform.forward;

        for (int i = 0; i < segments; i++)
        {
            float t = segments <= 1 ? 0f : i / (float)(segments - 1);

            // Линейная интерполяция между точками + провисание по синусу,
            // чтобы провод выглядел как настоящий кабель, а не как палка.
            Vector3 point = Vector3.Lerp(from, to, t);
            point.y -= Mathf.Sin(t * Mathf.PI) * sag;

            line.SetPosition(i, point);
        }
    }
}

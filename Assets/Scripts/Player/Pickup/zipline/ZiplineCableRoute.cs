using GogoGaga.OptimizedRopesAndCables;
using UnityEngine;

/// <summary>
/// Маркер троса зиплайна. Хранит ссылку на <see cref="Rope"/> и его концы,
/// чтобы ручка зиплайна могла прицепиться к тросу в ближайшей точке
/// и затем ехать вдоль него.
/// </summary>
[DisallowMultipleComponent]
public class ZiplineCableRoute : MonoBehaviour
{
    [Header("Cable")]
    [Tooltip("Визуальный трос (Rope). Если пусто — компонент ищет Rope на этом объекте.")]
    [SerializeField] private Rope cable;

    [Tooltip("Начало маршрута. Если пусто — берётся StartPoint у Rope.")]
    [SerializeField] private Transform startPoint;

    [Tooltip("Конец маршрута. Если пусто — берётся EndPoint у Rope.")]
    [SerializeField] private Transform endPoint;

    [Header("Closest Point")]
    [Tooltip("Сколько выборок делать при поиске ближайшей точки на кривой троса.")]
    [SerializeField, Min(8)] private int closenessSamples = 48;

    public Rope Cable
    {
        get
        {
            if (cable == null) cable = GetComponent<Rope>();
            return cable;
        }
    }

    public Transform StartPoint => startPoint != null ? startPoint : (Cable != null ? Cable.StartPoint : null);
    public Transform EndPoint => endPoint != null ? endPoint : (Cable != null ? Cable.EndPoint : null);
    public bool IsValid => StartPoint != null && EndPoint != null;

    /// <summary>Горизонтальное направление движения по тросу (от начала к концу).</summary>
    public Vector3 TravelDirection
    {
        get
        {
            if (!IsValid) return Vector3.forward;

            Vector3 direction = EndPoint.position - StartPoint.position;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        }
    }

    /// <summary>
    /// Точка на тросе для параметра 0..1. В Play Mode используется настоящая
    /// кривая Rope, в редакторе — прямая между концами (чтобы гизмо не «прыгали»).
    /// </summary>
    public Vector3 GetPointAt(float t)
    {
        t = Mathf.Clamp01(t);

        if (Application.isPlaying && Cable != null && IsValid)
            return Cable.GetPointAt(t);

        if (!IsValid)
            return transform.position;

        return Vector3.Lerp(StartPoint.position, EndPoint.position, t);
    }

    /// <summary>
    /// Ищет ближайшую к мировой точке позицию на тросе.
    /// Возвращает прогресс 0..1 и саму точку.
    /// </summary>
    public float FindClosestProgress(Vector3 worldPosition, out Vector3 closestPoint)
    {
        closestPoint = worldPosition;

        if (!IsValid)
            return 0f;

        int steps = Mathf.Max(8, closenessSamples);
        float bestSqr = float.MaxValue;
        float bestT = 0f;

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector3 point = GetPointAt(t);
            float sqr = (point - worldPosition).sqrMagnitude;

            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                bestT = t;
                closestPoint = point;
            }
        }

        // Уточняем результат вокруг найденной выборки.
        float step = 1f / steps;
        for (int i = 0; i < 4; i++)
        {
            float tBefore = Mathf.Clamp01(bestT - step);
            float tAfter = Mathf.Clamp01(bestT + step);

            Vector3 pointBefore = GetPointAt(tBefore);
            Vector3 pointAfter = GetPointAt(tAfter);

            float sqrBefore = (pointBefore - worldPosition).sqrMagnitude;
            float sqrAfter = (pointAfter - worldPosition).sqrMagnitude;

            if (sqrBefore < bestSqr)
            {
                bestSqr = sqrBefore;
                bestT = tBefore;
                closestPoint = pointBefore;
            }

            if (sqrAfter < bestSqr)
            {
                bestSqr = sqrAfter;
                bestT = tAfter;
                closestPoint = pointAfter;
            }

            step *= 0.5f;
        }

        return bestT;
    }

    private void OnDrawGizmosSelected()
    {
        if (!IsValid) return;

        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);

        const int gizmoSteps = 24;
        Vector3 previous = StartPoint.position;
        for (int i = 1; i <= gizmoSteps; i++)
        {
            Vector3 current = Vector3.Lerp(StartPoint.position, EndPoint.position, i / (float)gizmoSteps);
            Gizmos.DrawLine(previous, current);
            previous = current;
        }
    }
}

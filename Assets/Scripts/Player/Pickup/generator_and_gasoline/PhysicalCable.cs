using UnityEngine;

/// <summary>
/// Collision-aware cable simulation for the power cable.
/// The cable is a chain of verlet nodes between two anchors. Every node is
/// sphere-cast against the environment, so the cable drapes over floors,
/// walls and props instead of passing through them. The line is rendered by
/// the LineRenderer that already exists on the cable prefab.
/// </summary>
[DisallowMultipleComponent]
public class PhysicalCable : MonoBehaviour
{
    [Header("Anchors")]
    [Tooltip("Start anchor. When the cable is loose this is the loose end inside the item.")]
    [SerializeField] private Transform endA;
    [Tooltip("End anchor. In the power cable this is the plug, or the zipline socket after connection.")]
    [SerializeField] private Transform endB;

    [Header("Shape")]
    [SerializeField, Min(3)] private int segments = 28;
    [Tooltip("Cable length in meters. It must be at least the distance between the anchors.")]
    [SerializeField, Min(0.5f)] private float totalLength = 12f;
    [Tooltip("Collision radius of one cable node, meters.")]
    [SerializeField, Min(0.005f)] private float radius = 0.025f;

    [Header("Simulation")]
    [SerializeField] private float gravity = -9.81f;
    [SerializeField, Range(0f, 0.95f)] private float damping = 0.12f;
    [SerializeField, Range(1, 24)] private int solverIterations = 10;
    [Tooltip("Layers the cable collides with. By default this is everything except Player (3) and Interactable (6).")]
    [SerializeField] private LayerMask collisionMask = ~((1 << 3) | (1 << 6));
    [SerializeField, Range(0f, 1f)] private float surfaceFriction = 0.55f;
    [SerializeField] private bool collideWithEnvironment = true;

    [Header("Rendering")]
    [SerializeField] private LineRenderer line;
    [SerializeField, Min(0.005f)] private float lineWidth = 0.04f;
    [Tooltip("Optional override. Leave empty to keep the material that is already on the LineRenderer.")]
    [SerializeField] private Material lineMaterial;

    private Vector3[] nodes;
    private Vector3[] previousNodes;
    private bool ready;

    public bool IsReady => ready;
    public float TotalLength => totalLength;
    public Transform EndA => endA;
    public Transform EndB => endB;

    private void Awake()
    {
        if (line == null)
            line = GetComponent<LineRenderer>();

        ApplyLineSettings();
    }

    private void OnEnable()
    {
        ready = false;
    }

    private void Start()
    {
        Rebuild();
    }

    private void OnValidate()
    {
        segments = Mathf.Max(3, segments);
        totalLength = Mathf.Max(0.5f, totalLength);
        radius = Mathf.Max(0.005f, radius);
        solverIterations = Mathf.Clamp(solverIterations, 1, 24);
    }

    private void LateUpdate()
    {
        if (!ready)
        {
            Initialize();
            if (!ready)
                return;
        }

        float dt = Mathf.Min(Time.deltaTime, 0.0333f);
        if (dt <= 0f)
            return;

        Vector3 a = GetAnchor(endA);
        Vector3 b = GetAnchor(endB);
        Simulate(a, b, dt);
        UpdateLine();
    }

    public void SetEndA(Transform anchor)
    {
        endA = anchor;
        Rebuild();
    }

    public void SetEndB(Transform anchor)
    {
        endB = anchor;
        Rebuild();
    }

    public void SetEndpoints(Transform a, Transform b)
    {
        endA = a;
        endB = b;
        Rebuild();
    }

    public void SetLength(float meters)
    {
        totalLength = Mathf.Max(0.5f, meters);
        Rebuild();
    }

    /// <summary>Re-initializes the curve between the current anchors.</summary>
    public void Rebuild()
    {
        ready = false;
        Initialize();
    }

    private void Initialize()
    {
        if (line == null)
            line = GetComponent<LineRenderer>();

        ApplyLineSettings();

        if (segments < 3)
            segments = 3;

        nodes = new Vector3[segments];
        previousNodes = new Vector3[segments];

        Vector3 a = GetAnchor(endA);
        Vector3 b = GetAnchor(endB);
        float span = Vector3.Distance(a, b);
        float halfLength = totalLength * 0.5f;
        float halfSpan = span * 0.5f;
        float sag = Mathf.Sqrt(Mathf.Max(0f, halfLength * halfLength - halfSpan * halfSpan));
        float rayStartY = Mathf.Max(a.y, b.y) + 0.5f;

        for (int i = 0; i < segments; i++)
        {
            float t = i / (float)(segments - 1);
            Vector3 p = Vector3.Lerp(a, b, t);
            p.y -= 4f * sag * t * (1f - t);

            // A long cable between close anchors would start below the floor.
            // Lift every node to the first surface above it so the simulation
            // never begins inside the level geometry.
            p = ClampAboveSurface(p, rayStartY);

            nodes[i] = p;
            previousNodes[i] = p;
        }

        nodes[0] = a;
        nodes[segments - 1] = b;
        previousNodes[0] = a;
        previousNodes[segments - 1] = b;
        ready = true;
        UpdateLine();
    }

    private void Simulate(Vector3 a, Vector3 b, float dt)
    {
        int last = nodes.Length - 1;
        nodes[0] = a;
        previousNodes[0] = a;
        nodes[last] = b;
        previousNodes[last] = b;

        float dt2 = dt * dt;
        for (int i = 1; i < last; i++)
        {
            Vector3 velocity = (nodes[i] - previousNodes[i]) * (1f - damping);
            previousNodes[i] = nodes[i];
            nodes[i] += velocity + Vector3.up * (gravity * dt2);
        }

        float segmentLength = totalLength / (segments - 1);
        int iterations = Mathf.Max(1, solverIterations);

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            for (int i = 0; i < last; i++)
            {
                Vector3 delta = nodes[i + 1] - nodes[i];
                float length = delta.magnitude;
                if (length < 0.00001f)
                    continue;

                float error = (length - segmentLength) / length;
                Vector3 correction = Vector3.ClampMagnitude(delta * error, segmentLength);

                if (i == 0)
                {
                    nodes[i + 1] -= correction;
                }
                else if (i + 1 == last)
                {
                    nodes[i] += correction;
                }
                else
                {
                    nodes[i] += correction * 0.5f;
                    nodes[i + 1] -= correction * 0.5f;
                }
            }

            if (collideWithEnvironment)
            {
                float rayStartY = Mathf.Max(a.y, b.y) + 0.5f;
                for (int i = 1; i < last; i++)
                {
                    ResolveCollision(i);
                    nodes[i] = ClampAboveSurface(nodes[i], rayStartY);
                }
            }

            nodes[0] = a;
            nodes[last] = b;
        }
    }

    private void ResolveCollision(int index)
    {
        Vector3 from = previousNodes[index];
        Vector3 to = nodes[index];
        Vector3 motion = to - from;
        float distance = motion.magnitude;

        if (distance > 0.00001f)
        {
            if (Physics.SphereCast(
                    from,
                    radius,
                    motion / distance,
                    out RaycastHit hit,
                    distance,
                    collisionMask,
                    QueryTriggerInteraction.Ignore))
            {
                nodes[index] = hit.point + hit.normal * radius;
                previousNodes[index] = Vector3.Lerp(previousNodes[index], nodes[index], 1f - surfaceFriction);
                return;
            }
        }

        if (!Physics.CheckSphere(to, radius, collisionMask, QueryTriggerInteraction.Ignore))
            return;

        Collider[] overlaps = Physics.OverlapSphere(to, radius, collisionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider col = overlaps[i];
            if (col == null)
                continue;

            Vector3 closest = col.ClosestPoint(to);
            Vector3 away = to - closest;
            float sqr = away.sqrMagnitude;
            if (sqr < 0.000001f)
            {
                away = Vector3.up;
                sqr = 1f;
            }

            if (sqr < radius * radius)
            {
                nodes[index] = closest + away.normalized * radius;
                previousNodes[index] = Vector3.Lerp(previousNodes[index], nodes[index], 1f - surfaceFriction);
                return;
            }
        }
    }

    private void UpdateLine()
    {
        if (line == null || nodes == null)
            return;

        line.positionCount = nodes.Length;
        line.SetPositions(nodes);
    }

    private void ApplyLineSettings()
    {
        if (line == null)
            return;

        line.useWorldSpace = true;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.numCapVertices = 4;
        line.numCornerVertices = 2;
        line.widthMultiplier = lineWidth;

        if (lineMaterial != null)
            line.sharedMaterial = lineMaterial;
    }

    private Vector3 ClampAboveSurface(Vector3 point, float rayStartY)
    {
        Vector3 origin = new Vector3(point.x, rayStartY, point.z);
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 100f, collisionMask, QueryTriggerInteraction.Ignore))
        {
            float minimumY = hit.point.y + radius;
            if (point.y < minimumY)
                point.y = minimumY;
        }

        return point;
    }

    private Vector3 GetAnchor(Transform anchor)
    {
        return anchor != null ? anchor.position : transform.position;
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || nodes == null)
            return;

        Gizmos.color = Color.cyan;
        for (int i = 0; i < nodes.Length - 1; i++)
            Gizmos.DrawLine(nodes[i], nodes[i + 1]);
    }
}

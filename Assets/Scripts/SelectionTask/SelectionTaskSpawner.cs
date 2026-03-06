using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class SelectionTaskSpawner : MonoBehaviour
{
    public enum TargetDistanceRegion
    {
        Near = 0,
        Mid = 1,
        Far = 2
    }

    [Header("Task Space (based on paper setups)")]
    [Tooltip("Local-space center offset of the cuboid task space.")]
    public Vector3 taskSpaceOffset = new Vector3(0f, 0f, 0.8f);

    [Tooltip("Cuboid size for placing spheres. Paper example commonly uses 1m x 1m x 1m.")]
    public Vector3 taskSpaceSize = Vector3.one;

    [Header("Sphere Variables (independent variables)")]
    [Tooltip("Sphere diameter in meters. Example from papers: 0.2m (radius 0.1m).")]
    [Min(0.01f)]
    public float sphereDiameter = 0.2f;

    [Tooltip("Minimum center-to-center distance in meters. Lower value means higher density.")]
    [Min(0.01f)]
    public float minimumCenterDistance = 0.3f;

    [Header("Generation Control")]
    [Tooltip("Upper bound to avoid infinite generation in sparse/large spaces.")]
    [Min(1)]
    public int maxSphereCount = 128;

    [Tooltip("Attempts per active sample in Bridson-style Poisson sampling.")]
    [Min(1)]
    public int attemptsPerSample = 30;

    [Tooltip("Generate automatically when scene starts.")]
    public bool generateOnStart = true;

    [Tooltip("Delete previously generated spheres before regeneration.")]
    public bool clearBeforeGenerate = true;

    [Header("Randomness")]
    [Tooltip("Use a fixed seed for repeatable trials.")]
    public bool useFixedSeed = true;

    public int seed = 2026;

    [Header("Target Distance Control (Fitts Law)")]
    [Tooltip("用于计算距离分区的 HMD 初始世界坐标。仅使用水平距离 (x,z)，忽略 y。")]
    public Vector3 hmdInitialWorldPosition = new Vector3(0f, 1f, 0f);

    [Tooltip("按水平距离分位数（等频）划分近/中/远后，目标出现的区域。")]
    public TargetDistanceRegion targetDistanceRegion = TargetDistanceRegion.Mid;

    [Header("Visual")]
    public Color targetColor = new Color(1f, 0.5f, 0f, 1f);
    public Color distractorColor = Color.white;

    [Tooltip("Optional target material override.")]
    public Material targetMaterial;

    [Tooltip("Optional distractor material override.")]
    public Material distractorMaterial;

    [Header("Grab Integration")]
    [Tooltip("Automatically add and configure Grabbable on generated spheres.")]
    public bool makeSpheresGrabbable = true;

    [Tooltip("Add Rigidbody for stable grab follow and release behavior.")]
    public bool addRigidbodyForGrab = true;

    [Tooltip("Initial Rigidbody isKinematic value for generated spheres.")]
    public bool rigidbodyKinematicByDefault = true;

    [Tooltip("Initial Rigidbody useGravity value for generated spheres.")]
    public bool rigidbodyUseGravity = false;

    [Tooltip("Forwarded to Grabbable.stopMotionOnRelease.")]
    public bool stopMotionOnRelease = true;

    [Tooltip("Forwarded to Grabbable.forceKinematicOnRelease.")]
    public bool forceKinematicOnRelease = true;

    [Tooltip("Assign generated spheres to this layer when it exists.")]
    public bool assignGrabbableLayer = true;

    public string grabbableLayerName = "Grabbable";

    [Header("Runtime Output")]
    [SerializeField] private int generatedTargetCount = 0;
    [SerializeField] private int generatedDistractorCount = 0;

    public event Action OnTargetDeliveredToArea;

    [Header("Task Completion")]
    [Tooltip("目标球与该名称的对象接触后，判定任务完成并清空所有生成球。")]
    public string targetAreaObjectName = "TargetArea";

    [Tooltip("是否启用“目标进入 TargetArea 即结束任务”的规则。")]
    public bool clearAllWhenTargetTouchesArea = true;

    private const string ContainerName = "SelectionTask_Spheres";
    private Transform _container;
    private bool _taskCompleted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureSpawnerInSampleScene()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.name != "Sample")
        {
            return;
        }

        if (FindObjectOfType<SelectionTaskSpawner>() != null)
        {
            return;
        }

        GameObject host = new GameObject("SelectionTaskSpawner_Auto");
        host.AddComponent<SelectionTaskSpawner>();
    }

    private void Start()
    {
        if (generateOnStart)
        {
            Generate();
        }
    }

    [ContextMenu("Generate Selection Task Spheres")]
    public void Generate()
    {
        EnsureContainer();

        if (clearBeforeGenerate)
        {
            ClearGenerated();
        }

        List<Vector3> points = GeneratePoissonPointsInBox(
            taskSpaceSize,
            minimumCenterDistance,
            maxSphereCount,
            attemptsPerSample,
            useFixedSeed ? seed : Environment.TickCount);

        generatedTargetCount = 0;
        generatedDistractorCount = 0;
        _taskCompleted = false;

        if (points.Count == 0)
        {
            Debug.LogWarning("[SelectionTaskSpawner] No point generated. Try lower minimumCenterDistance or bigger task space.");
            return;
        }

        System.Random rng = useFixedSeed ? new System.Random(seed + 1) : new System.Random();
        int targetIndex = SelectTargetIndexByDistanceRegion(points, rng);

        for (int i = 0; i < points.Count; i++)
        {
            bool isTarget = i == targetIndex;
            SpawnSphere(points[i], isTarget, i);
        }
    }

    private int SelectTargetIndexByDistanceRegion(List<Vector3> localPointsInCenteredBox, System.Random rng)
    {
        if (localPointsInCenteredBox == null || localPointsInCenteredBox.Count == 0)
        {
            return -1;
        }

        List<DistanceEntry> ordered = new List<DistanceEntry>(localPointsInCenteredBox.Count);
        for (int i = 0; i < localPointsInCenteredBox.Count; i++)
        {
            Vector3 worldPos = transform.TransformPoint(taskSpaceOffset + localPointsInCenteredBox[i]);
            Vector2 d = new Vector2(worldPos.x - hmdInitialWorldPosition.x, worldPos.z - hmdInitialWorldPosition.z);
            ordered.Add(new DistanceEntry(i, d.magnitude));
        }

        ordered.Sort((a, b) => a.distance.CompareTo(b.distance));

        SplitThreeQuantileRanges(ordered.Count, out int nearStart, out int nearEnd, out int midStart, out int midEnd, out int farStart, out int farEnd);
        int selectedRangeStart;
        int selectedRangeEnd;

        if (targetDistanceRegion == TargetDistanceRegion.Near)
        {
            selectedRangeStart = nearStart;
            selectedRangeEnd = nearEnd;
        }
        else if (targetDistanceRegion == TargetDistanceRegion.Mid)
        {
            selectedRangeStart = midStart;
            selectedRangeEnd = midEnd;
        }
        else
        {
            selectedRangeStart = farStart;
            selectedRangeEnd = farEnd;
        }

        if (selectedRangeEnd <= selectedRangeStart)
        {
            int fallbackSorted = Mathf.Clamp(ordered.Count / 2, 0, ordered.Count - 1);
            Debug.LogWarning("[SelectionTaskSpawner] Selected distance region is empty for current sphere count. Falling back to median-distance target.");
            return ordered[fallbackSorted].index;
        }

        int sortedPick = rng.Next(selectedRangeStart, selectedRangeEnd);
        return ordered[sortedPick].index;
    }

    private static void SplitThreeQuantileRanges(
        int n,
        out int nearStart,
        out int nearEnd,
        out int midStart,
        out int midEnd,
        out int farStart,
        out int farEnd)
    {
        int baseSize = n / 3;
        int remainder = n % 3;

        int nearCount = baseSize + (remainder > 0 ? 1 : 0);
        int midCount = baseSize + (remainder > 1 ? 1 : 0);
        int farCount = n - nearCount - midCount;

        nearStart = 0;
        nearEnd = nearStart + nearCount; // [nearStart, nearEnd)
        midStart = nearEnd;
        midEnd = midStart + midCount;    // [midStart, midEnd)
        farStart = midEnd;
        farEnd = farStart + farCount;    // [farStart, farEnd)
    }

    private readonly struct DistanceEntry
    {
        public readonly int index;
        public readonly float distance;

        public DistanceEntry(int index, float distance)
        {
            this.index = index;
            this.distance = distance;
        }
    }

    [ContextMenu("Clear Generated Spheres")]
    public void ClearGenerated()
    {
        EnsureContainer();

        for (int i = _container.childCount - 1; i >= 0; i--)
        {
            Transform child = _container.GetChild(i);
            if (Application.isPlaying)
            {
                Destroy(child.gameObject);
            }
            else
            {
                DestroyImmediate(child.gameObject);
            }
        }

        generatedTargetCount = 0;
        generatedDistractorCount = 0;
    }

    public void NotifyTargetTouchedArea(SelectionTaskSphere sphere, Collider other)
    {
        if (!clearAllWhenTargetTouchesArea) return;
        if (_taskCompleted) return;
        if (sphere == null || !sphere.isTarget) return;
        if (other == null) return;
        if (!IsTargetAreaCollider(other)) return;

        _taskCompleted = true;
        OnTargetDeliveredToArea?.Invoke();
        ClearGenerated();
    }

    private void SpawnSphere(Vector3 localPointInCenteredBox, bool isTarget, int index)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = isTarget ? "TargetSphere" : $"DistractorSphere_{index:000}";
        sphere.transform.SetParent(_container, false);
        sphere.transform.localPosition = taskSpaceOffset + localPointInCenteredBox;
        sphere.transform.localRotation = Quaternion.identity;
        sphere.transform.localScale = Vector3.one * sphereDiameter;

        var marker = sphere.AddComponent<SelectionTaskSphere>();
        marker.isTarget = isTarget;
        marker.Initialize(this);

        Renderer renderer = sphere.GetComponent<Renderer>();
        if (renderer != null)
        {
            if (isTarget && targetMaterial != null)
            {
                renderer.sharedMaterial = targetMaterial;
            }
            else if (!isTarget && distractorMaterial != null)
            {
                renderer.sharedMaterial = distractorMaterial;
            }
            else
            {
                renderer.sharedMaterial = new Material(Shader.Find("Standard"));
            }

            renderer.sharedMaterial.color = isTarget ? targetColor : distractorColor;
        }

        if (isTarget) generatedTargetCount++;
        else generatedDistractorCount++;

        ConfigureGrabComponents(sphere);
    }

    private bool IsTargetAreaCollider(Collider other)
    {
        if (other == null) return false;
        if (string.IsNullOrWhiteSpace(targetAreaObjectName)) return false;

        string targetName = targetAreaObjectName.Trim();
        if (string.Equals(other.gameObject.name, targetName, StringComparison.Ordinal))
            return true;

        Transform t = other.transform;
        while (t != null)
        {
            if (string.Equals(t.name, targetName, StringComparison.Ordinal))
                return true;
            t = t.parent;
        }

        return false;
    }

    private void ConfigureGrabComponents(GameObject sphere)
    {
        if (!makeSpheresGrabbable || sphere == null)
        {
            return;
        }

        if (assignGrabbableLayer)
        {
            int layer = LayerMask.NameToLayer(grabbableLayerName);
            if (layer >= 0)
            {
                sphere.layer = layer;
            }
        }

        Rigidbody rb = sphere.GetComponent<Rigidbody>();
        if (addRigidbodyForGrab && rb == null)
        {
            rb = sphere.AddComponent<Rigidbody>();
        }

        if (rb != null)
        {
            rb.isKinematic = rigidbodyKinematicByDefault;
            rb.useGravity = rigidbodyUseGravity;
        }

        Grabbable grabbable = sphere.GetComponent<Grabbable>();
        if (grabbable == null)
        {
            grabbable = sphere.AddComponent<Grabbable>();
        }

        grabbable.rb = rb;
        grabbable.colliders = sphere.GetComponentsInChildren<Collider>();
        grabbable.stopMotionOnRelease = stopMotionOnRelease;
        grabbable.forceKinematicOnRelease = forceKinematicOnRelease;
    }

    private void EnsureContainer()
    {
        if (_container != null)
        {
            return;
        }

        Transform existing = transform.Find(ContainerName);
        if (existing != null)
        {
            _container = existing;
            return;
        }

        GameObject go = new GameObject(ContainerName);
        go.transform.SetParent(transform, false);
        _container = go.transform;
    }

    private static List<Vector3> GeneratePoissonPointsInBox(
        Vector3 size,
        float minDistance,
        int maxCount,
        int attemptsPerPoint,
        int randomSeed)
    {
        List<Vector3> points = new List<Vector3>();
        if (minDistance <= 0f || size.x <= 0f || size.y <= 0f || size.z <= 0f || maxCount <= 0)
        {
            return points;
        }

        float cellSize = minDistance / Mathf.Sqrt(3f);
        int gridX = Mathf.CeilToInt(size.x / cellSize);
        int gridY = Mathf.CeilToInt(size.y / cellSize);
        int gridZ = Mathf.CeilToInt(size.z / cellSize);
        Vector3[,,] grid = new Vector3[gridX, gridY, gridZ];
        bool[,,] occupied = new bool[gridX, gridY, gridZ];

        System.Random rng = new System.Random(randomSeed);
        List<Vector3> active = new List<Vector3>();

        // Use task-space center as the deterministic seed point.
        Vector3 first = Vector3.zero;
        AddPoint(first, points, active, grid, occupied, cellSize, gridX, gridY, gridZ);

        while (active.Count > 0 && points.Count < maxCount)
        {
            int activeIndex = rng.Next(active.Count);
            Vector3 center = active[activeIndex];
            bool found = false;

            for (int i = 0; i < attemptsPerPoint; i++)
            {
                Vector3 candidate = RandomPointInSphericalShell(center, minDistance, 2f * minDistance, rng);
                if (!IsInsideCenteredBox(candidate, size))
                {
                    continue;
                }

                if (IsFarEnough(candidate, minDistance, grid, occupied, cellSize, gridX, gridY, gridZ))
                {
                    AddPoint(candidate, points, active, grid, occupied, cellSize, gridX, gridY, gridZ);
                    found = true;
                    if (points.Count >= maxCount)
                    {
                        break;
                    }
                }
            }

            if (!found)
            {
                active.RemoveAt(activeIndex);
            }
        }

        return points;
    }

    private static void AddPoint(
        Vector3 p,
        List<Vector3> points,
        List<Vector3> active,
        Vector3[,,] grid,
        bool[,,] occupied,
        float cellSize,
        int gridX,
        int gridY,
        int gridZ)
    {
        points.Add(p);
        active.Add(p);

        Vector3Int idx = GridIndexFromPoint(p, cellSize, gridX, gridY, gridZ);
        grid[idx.x, idx.y, idx.z] = p;
        occupied[idx.x, idx.y, idx.z] = true;
    }

    private static bool IsFarEnough(
        Vector3 candidate,
        float minDistance,
        Vector3[,,] grid,
        bool[,,] occupied,
        float cellSize,
        int gridX,
        int gridY,
        int gridZ)
    {
        float sqrMin = minDistance * minDistance;
        Vector3Int idx = GridIndexFromPoint(candidate, cellSize, gridX, gridY, gridZ);

        int minX = Mathf.Max(0, idx.x - 2);
        int maxX = Mathf.Min(gridX - 1, idx.x + 2);
        int minY = Mathf.Max(0, idx.y - 2);
        int maxY = Mathf.Min(gridY - 1, idx.y + 2);
        int minZ = Mathf.Max(0, idx.z - 2);
        int maxZ = Mathf.Min(gridZ - 1, idx.z + 2);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    if (!occupied[x, y, z])
                    {
                        continue;
                    }

                    Vector3 existing = grid[x, y, z];
                    if ((existing - candidate).sqrMagnitude < sqrMin)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static Vector3Int GridIndexFromPoint(Vector3 point, float cellSize, int gx, int gy, int gz)
    {
        Vector3 halfGridExtent = new Vector3(gx * cellSize * 0.5f, gy * cellSize * 0.5f, gz * cellSize * 0.5f);
        Vector3 shifted = point + halfGridExtent;
        int x = Mathf.Clamp(Mathf.FloorToInt(shifted.x / cellSize), 0, gx - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(shifted.y / cellSize), 0, gy - 1);
        int z = Mathf.Clamp(Mathf.FloorToInt(shifted.z / cellSize), 0, gz - 1);
        return new Vector3Int(x, y, z);
    }

    private static Vector3 RandomPointInBox(Vector3 size, System.Random rng)
    {
        return new Vector3(
            RandomRange(rng, -size.x * 0.5f, size.x * 0.5f),
            RandomRange(rng, -size.y * 0.5f, size.y * 0.5f),
            RandomRange(rng, -size.z * 0.5f, size.z * 0.5f));
    }

    private static Vector3 RandomPointInSphericalShell(Vector3 center, float minR, float maxR, System.Random rng)
    {
        double u = rng.NextDouble();
        double v = rng.NextDouble();
        double w = rng.NextDouble();

        float theta = (float)(2.0 * Math.PI * u);
        float phi = Mathf.Acos((float)(2.0 * v - 1.0));
        float radius = Mathf.Lerp(minR, maxR, (float)w);

        float sinPhi = Mathf.Sin(phi);
        Vector3 dir = new Vector3(
            radius * sinPhi * Mathf.Cos(theta),
            radius * sinPhi * Mathf.Sin(theta),
            radius * Mathf.Cos(phi));

        return center + dir;
    }

    private static float RandomRange(System.Random rng, float min, float max)
    {
        return min + (float)rng.NextDouble() * (max - min);
    }

    private static bool IsInsideCenteredBox(Vector3 p, Vector3 size)
    {
        Vector3 half = size * 0.5f;
        return Mathf.Abs(p.x) <= half.x && Mathf.Abs(p.y) <= half.y && Mathf.Abs(p.z) <= half.z;
    }

    private void OnValidate()
    {
        taskSpaceSize.x = Mathf.Max(0.01f, taskSpaceSize.x);
        taskSpaceSize.y = Mathf.Max(0.01f, taskSpaceSize.y);
        taskSpaceSize.z = Mathf.Max(0.01f, taskSpaceSize.z);
        sphereDiameter = Mathf.Max(0.01f, sphereDiameter);
        minimumCenterDistance = Mathf.Max(0.01f, minimumCenterDistance);
        maxSphereCount = Mathf.Max(1, maxSphereCount);
        attemptsPerSample = Mathf.Max(1, attemptsPerSample);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
        Matrix4x4 prev = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(taskSpaceOffset, taskSpaceSize);
        Gizmos.matrix = prev;
    }
}

public class SelectionTaskSphere : MonoBehaviour
{
    public bool isTarget = false;
    private SelectionTaskSpawner _owner;

    public void Initialize(SelectionTaskSpawner owner)
    {
        _owner = owner;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!isTarget || _owner == null) return;
        _owner.NotifyTargetTouchedArea(this, other);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!isTarget || _owner == null) return;
        if (collision == null) return;
        _owner.NotifyTargetTouchedArea(this, collision.collider);
    }
}

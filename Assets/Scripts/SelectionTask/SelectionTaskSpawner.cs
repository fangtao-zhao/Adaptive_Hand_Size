using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class SelectionTaskSpawner : MonoBehaviour
{
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

    private const string ContainerName = "SelectionTask_Spheres";
    private Transform _container;

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

        if (points.Count == 0)
        {
            Debug.LogWarning("[SelectionTaskSpawner] No point generated. Try lower minimumCenterDistance or bigger task space.");
            return;
        }

        System.Random rng = useFixedSeed ? new System.Random(seed + 1) : new System.Random();
        int targetIndex = rng.Next(points.Count);

        for (int i = 0; i < points.Count; i++)
        {
            bool isTarget = i == targetIndex;
            SpawnSphere(points[i], isTarget, i);
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

        Vector3 first = RandomPointInBox(size, rng);
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
}

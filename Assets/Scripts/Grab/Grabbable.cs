using UnityEngine;

[DisallowMultipleComponent]
public class Grabbable : MonoBehaviour
{
    [Header("Optional")]
    public Rigidbody rb;

    [Tooltip("如果你想限制抓取判定用哪个Collider，就填这个；不填则自动找所有Collider。")]
    public Collider[] colliders;

    private void Reset()
    {
        rb = GetComponent<Rigidbody>();
        if (colliders == null || colliders.Length == 0)
            colliders = GetComponentsInChildren<Collider>();
    }

    private void Awake()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (colliders == null || colliders.Length == 0)
            colliders = GetComponentsInChildren<Collider>();
    }

    public Vector3 ClosestPoint(Vector3 toPoint)
    {
        // 用所有collider的 ClosestPoint，取最近的
        if (colliders == null || colliders.Length == 0)
            return transform.position;

        float bestDist = float.PositiveInfinity;
        Vector3 best = transform.position;

        foreach (var c in colliders)
        {
            if (c == null) continue;
            Vector3 p = c.ClosestPoint(toPoint);
            float d = (p - toPoint).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = p;
            }
        }
        return best;
    }
}
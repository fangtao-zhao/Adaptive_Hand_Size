using UnityEngine;

public class MyGrabManager : MonoBehaviour
{
    [Header("Tip Triggers (assign in Inspector)")]
    public FingerTipTrigger thumbTip;
    public FingerTipTrigger indexTip;

    [Header("Tip Transforms (assign in Inspector)")]
    public Transform thumbTipTransform;
    public Transform indexTipTransform;

    [Header("Grab Settings")]
    [Tooltip("抓取检测半径（米）。比如 0.03~0.06 之间试。")]
    public float detectRadius = 0.045f;

    [Tooltip("只检测这些层上的可抓取物体（建议你把可抓取物体都放到Grabbable层）。")]
    public LayerMask grabbableLayer;

    [Tooltip("抓取时物体会被 parent 到这个点（建议是手上的一个空节点）。不填则用 thumbTipTransform。")]
    public Transform grabParent;

    [Header("Debug")]
    public bool drawGizmos = true;

    // runtime
    private bool _isPinching = false;

    private Grabbable _grabbed;
    private Transform _grabbedOriginalParent;
    private bool _grabbedOriginalKinematic;

    private void OnEnable()
    {
        if (thumbTip != null)
        {
            thumbTip.OnPinchEnter += HandlePinchEnter;
            thumbTip.OnPinchExit += HandlePinchExit;
        }
        if (indexTip != null)
        {
            indexTip.OnPinchEnter += HandlePinchEnter;
            indexTip.OnPinchExit += HandlePinchExit;
        }
    }

    private void OnDisable()
    {
        if (thumbTip != null)
        {
            thumbTip.OnPinchEnter -= HandlePinchEnter;
            thumbTip.OnPinchExit -= HandlePinchExit;
        }
        if (indexTip != null)
        {
            indexTip.OnPinchEnter -= HandlePinchEnter;
            indexTip.OnPinchExit -= HandlePinchExit;
        }
    }

    private void HandlePinchEnter()
    {
        if (_isPinching) return;
        _isPinching = true;

        TryGrabNearest();
    }

    private void HandlePinchExit()
    {
        if (!_isPinching) return;
        _isPinching = false;

        Release();
    }

    private Vector3 GetPinchPoint()
    {
        if (thumbTipTransform == null || indexTipTransform == null)
            return transform.position;

        return (thumbTipTransform.position + indexTipTransform.position) * 0.5f;
    }

    private void TryGrabNearest()
    {
        if (_grabbed != null) return;

        Vector3 pinchPoint = GetPinchPoint();

        Collider[] hits = Physics.OverlapSphere(pinchPoint, detectRadius, grabbableLayer, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0) return;

        Grabbable best = null;
        float bestDist = float.PositiveInfinity;

        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            if (col == null) continue;

            // 找到最近的 Grabbable（允许 collider 在子物体上）
            var g = col.GetComponentInParent<Grabbable>();
            if (g == null) continue;

            Vector3 closest = g.ClosestPoint(pinchPoint);
            float d = (closest - pinchPoint).sqrMagnitude;

            if (d < bestDist)
            {
                bestDist = d;
                best = g;
            }
        }

        if (best == null) return;

        Grab(best);
    }

    private void Grab(Grabbable g)
    {
        _grabbed = g;
        _grabbedOriginalParent = g.transform.parent;

        Transform parent = grabParent != null ? grabParent : thumbTipTransform;
        if (parent == null) parent = transform;

        // 直接绑定：不吸取、不插值
        // 为了避免物理抖动，抓取时把rb设为kinematic（如果有）
        if (g.rb != null)
        {
            _grabbedOriginalKinematic = g.rb.isKinematic;
            g.rb.isKinematic = true;
            g.rb.velocity = Vector3.zero;
            g.rb.angularVelocity = Vector3.zero;
        }

        g.transform.SetParent(parent, true);
    }

    private void Release()
    {
        if (_grabbed == null) return;

        // 释放：恢复 parent
        _grabbed.transform.SetParent(_grabbedOriginalParent, true);

        // 恢复 Rigidbody
        if (_grabbed.rb != null)
        {
            _grabbed.rb.isKinematic = _grabbedOriginalKinematic;
        }

        _grabbed = null;
        _grabbedOriginalParent = null;
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;
        if (thumbTipTransform == null || indexTipTransform == null) return;

        Vector3 p = (thumbTipTransform.position + indexTipTransform.position) * 0.5f;
        Gizmos.DrawWireSphere(p, detectRadius);
    }
}
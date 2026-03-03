using System;
using UnityEngine;

[DisallowMultipleComponent]
public class HandSizeController : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("要缩放的目标（通常是手的根节点）。不填则缩放当前物体。")]
    public Transform target;

    [Header("Scale (relative to base)")]
    [Tooltip("把 baseLocalScale 乘以 scaleFactor 得到最终 localScale。")]
    [Min(0.001f)]
    public float scaleFactor = 1.0f;

    [Tooltip("缩放因子最小值（用于防止缩到 0 或负数）。")]
    [Min(0.001f)]
    public float minScaleFactor = 0.2f;

    [Tooltip("缩放因子最大值。")]
    [Min(0.001f)]
    public float maxScaleFactor = 2.0f;

    [Tooltip("是否在启用/运行时记录一次 baseLocalScale 作为基准。若关闭，则 baseLocalScale 需要手动调用 RecalibrateBaseScale。")]
    public bool captureBaseScaleOnEnable = true;

    public event Action<float> OnScaleFactorChanged;

    private Vector3 _baseLocalScale = Vector3.one;
    private bool _hasBaseScale;
    private float _lastAppliedScaleFactor = float.NaN;

    public Vector3 BaseLocalScale => _baseLocalScale;
    public float CurrentScaleFactor => scaleFactor;
    public Vector3 CurrentLocalScale => GetTarget().localScale;

    private Transform GetTarget()
    {
        return target != null ? target : transform;
    }

    private void OnEnable()
    {
        if (captureBaseScaleOnEnable)
        {
            RecalibrateBaseScale();
        }

        ApplyScaleIfNeeded(force: true);
    }

    private void Update()
    {
        if (!Application.isPlaying) return;
        ApplyScaleIfNeeded(force: false);
    }

    public void RecalibrateBaseScale()
    {
        _baseLocalScale = GetTarget().localScale;
        _hasBaseScale = true;
        _lastAppliedScaleFactor = float.NaN;
    }

    public void SetScaleFactor(float newScaleFactor)
    {
        scaleFactor = Mathf.Clamp(newScaleFactor, minScaleFactor, maxScaleFactor);
        ApplyScaleIfNeeded(force: true);
    }

    public void SetAbsoluteLocalScale(Vector3 absoluteLocalScale)
    {
        var t = GetTarget();
        t.localScale = absoluteLocalScale;
        _baseLocalScale = absoluteLocalScale;
        _hasBaseScale = true;
        scaleFactor = 1.0f;
        _lastAppliedScaleFactor = 1.0f;
        OnScaleFactorChanged?.Invoke(scaleFactor);
    }

    public void ResetScale()
    {
        SetScaleFactor(1.0f);
    }

    private void ApplyScaleIfNeeded(bool force)
    {
        if (!_hasBaseScale)
        {
            _baseLocalScale = GetTarget().localScale;
            _hasBaseScale = true;
        }

        float clamped = Mathf.Clamp(scaleFactor, minScaleFactor, maxScaleFactor);
        if (!Mathf.Approximately(clamped, scaleFactor))
        {
            scaleFactor = clamped;
        }

        if (!force && Mathf.Approximately(_lastAppliedScaleFactor, scaleFactor))
            return;

        var t = GetTarget();
        t.localScale = _baseLocalScale * scaleFactor;
        _lastAppliedScaleFactor = scaleFactor;
        OnScaleFactorChanged?.Invoke(scaleFactor);
    }

    private void OnValidate()
    {
        minScaleFactor = Mathf.Max(0.001f, minScaleFactor);
        maxScaleFactor = Mathf.Max(minScaleFactor, maxScaleFactor);
        if (scaleFactor <= 0f) scaleFactor = 1.0f;
        scaleFactor = Mathf.Clamp(scaleFactor, minScaleFactor, maxScaleFactor);

        if (!Application.isPlaying)
        {
            if (!captureBaseScaleOnEnable)
            {
                return;
            }

            if (target == null) return;
            _baseLocalScale = target.localScale;
            _hasBaseScale = true;
            _lastAppliedScaleFactor = float.NaN;
        }
    }
}


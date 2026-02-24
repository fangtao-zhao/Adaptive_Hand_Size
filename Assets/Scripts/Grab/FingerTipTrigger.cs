using System;
using UnityEngine;

[DisallowMultipleComponent]
public class FingerTipTrigger : MonoBehaviour
{
    public enum TipType { Thumb, Index }

    public TipType tipType;

    [Tooltip("只把另一根指尖的 trigger 认作 pinch 对象（建议设置为 FingerTipTrigger 所在 layer）。")]
    public LayerMask otherTipLayer;

    public event Action OnPinchEnter;
    public event Action OnPinchExit;

    private int _overlapCount = 0;

    private void Reset()
    {
        // 你可以在Inspector里手动设，这里不给默认LayerMask，避免误伤
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsOtherTip(other)) return;

        _overlapCount++;
        if (_overlapCount == 1)
            OnPinchEnter?.Invoke();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsOtherTip(other)) return;

        _overlapCount = Mathf.Max(0, _overlapCount - 1);
        if (_overlapCount == 0)
            OnPinchExit?.Invoke();
    }

    private bool IsOtherTip(Collider other)
    {
        // 1) layer过滤
        if (((1 << other.gameObject.layer) & otherTipLayer.value) == 0)
            return false;

        // 2) 必须有 FingerTipTrigger 且类型相反（拇指<->食指）
        var otherTip = other.GetComponent<FingerTipTrigger>();
        if (otherTip == null) return false;

        return otherTip.tipType != this.tipType;
    }
}
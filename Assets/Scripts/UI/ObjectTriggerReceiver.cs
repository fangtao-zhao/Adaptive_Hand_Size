using UnityEngine;
using UnityEngine.Events;

public class ObjectTriggerReceiver : MonoBehaviour
{
    [Header("Trigger Filter")]
    [Tooltip("Only colliders with this tag can trigger the event.")]
    public string triggerTag = "FingerTrigger";

    [Tooltip("If enabled, this receiver only responds once.")]
    public bool triggerOnce = true;

    [Header("Events")]
    [Tooltip("Invoked when a valid collider enters.")]
    public UnityEvent onFingerTouch;

    [Header("Post Trigger")]
    [Tooltip("When triggered, hide the target UI Canvas.")]
    public bool hideCanvasOnTouch = true;

    [Tooltip("Canvas to hide after touch. If empty, tries to find parent Canvas.")]
    public Canvas canvasToHide;

    public event System.Action FingerTouched;

    private bool _hasTriggered;

    private void OnTriggerEnter(Collider other)
    {
        if (_hasTriggered && triggerOnce)
        {
            return;
        }

        if (other != null && other.CompareTag(triggerTag))
        {
            OnFingerTouch();
        }
    }

    private void OnFingerTouch()
    {
        _hasTriggered = true;
        HideCanvasIfNeeded();
        onFingerTouch?.Invoke();
        FingerTouched?.Invoke();
    }

    private void HideCanvasIfNeeded()
    {
        if (!hideCanvasOnTouch)
        {
            return;
        }

        if (canvasToHide == null)
        {
            canvasToHide = GetComponentInParent<Canvas>();
        }

        if (canvasToHide != null)
        {
            canvasToHide.gameObject.SetActive(false);
        }
    }

    [ContextMenu("Reset Trigger State")]
    public void ResetTriggerState()
    {
        _hasTriggered = false;
    }
}
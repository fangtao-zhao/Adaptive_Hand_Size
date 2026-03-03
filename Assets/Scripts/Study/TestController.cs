using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class TestController : MonoBehaviour
{
    [System.Serializable]
    public struct TestCondition
    {
        public float handScaleFactor;
        public float detectRadius;
        public float sphereDiameter;
        public float minimumCenterDistance;
        public SelectionTaskSpawner.TargetDistanceRegion targetDistanceRegion;

        public override string ToString()
        {
            return $"HandScaleFactor={handScaleFactor:0.###}, DetectRadius={detectRadius:0.###}, Diameter={sphereDiameter:0.###}, DensityMinDist={minimumCenterDistance:0.###}, DistanceRegion={targetDistanceRegion}";
        }
    }

    [Header("References")]
    public SelectionTaskSpawner selectionTaskSpawner;
    public HandSizeController handSizeController;
    public MyGrabManager grabManager;
    public StudyController studyController;

    [Header("Test Condition (direct control)")]
    public TestCondition currentCondition = new TestCondition
    {
        handScaleFactor = 1.0f,
        detectRadius = 0.045f,
        sphereDiameter = 0.03f,
        minimumCenterDistance = 0.045f,
        targetDistanceRegion = SelectionTaskSpawner.TargetDistanceRegion.Mid
    };

    [Header("Run Control")]
    [Tooltip("完成一个 trial 后，等待多少秒再按当前条件重新生成。")]
    [Min(0f)]
    public float regenDelayAfterTrialSeconds = 0.15f;

    [Tooltip("启用 TestController 时自动禁用 StudyController，避免两套流程同时运行。")]
    public bool disableStudyControllerWhenActive = true;

    [Header("Runtime Output")]
    [SerializeField] private bool isRunning = false;
    [SerializeField] private int completedTrialCount = 0;

    private Coroutine _regenCoroutine;

    private void OnEnable()
    {
        ResolveReferencesIfNeeded();
        ToggleStudyController(enable: false);
        SubscribeSpawnerEvent();
        StartTest();
    }

    private void OnDisable()
    {
        UnsubscribeSpawnerEvent();
        if (_regenCoroutine != null)
        {
            StopCoroutine(_regenCoroutine);
            _regenCoroutine = null;
        }

        // TestController 关闭时恢复 StudyController 可用状态（是否启动由其自身控制）。
        ToggleStudyController(enable: true);
        isRunning = false;
    }

    private void StartTest()
    {
        ResolveReferencesIfNeeded();
        if (selectionTaskSpawner == null)
        {
            Debug.LogError("[TestController] SelectionTaskSpawner not found.");
            return;
        }

        ApplyCondition();
        if (selectionTaskSpawner.generateOnStart)
        {
            selectionTaskSpawner.generateOnStart = false;
        }

        completedTrialCount = 0;
        isRunning = true;
        selectionTaskSpawner.Generate();
        Debug.Log($"[TestController] Test started with condition: {currentCondition}");
    }

    private void HandleTargetDelivered()
    {
        if (!isRunning || selectionTaskSpawner == null) return;

        completedTrialCount++;
        if (_regenCoroutine != null)
        {
            StopCoroutine(_regenCoroutine);
            _regenCoroutine = null;
        }

        _regenCoroutine = StartCoroutine(RegenerateAfterDelay(regenDelayAfterTrialSeconds));
    }

    private IEnumerator RegenerateAfterDelay(float delay)
    {
        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        if (!isRunning || selectionTaskSpawner == null) yield break;

        ApplyCondition();
        selectionTaskSpawner.Generate();
        _regenCoroutine = null;
    }

    private void ApplyCondition()
    {
        if (handSizeController != null)
        {
            handSizeController.SetScaleFactor(currentCondition.handScaleFactor);
        }

        if (grabManager != null)
        {
            grabManager.detectRadius = currentCondition.detectRadius;
        }

        if (selectionTaskSpawner != null)
        {
            selectionTaskSpawner.sphereDiameter = currentCondition.sphereDiameter;
            selectionTaskSpawner.minimumCenterDistance = currentCondition.minimumCenterDistance;
            selectionTaskSpawner.targetDistanceRegion = currentCondition.targetDistanceRegion;
        }
    }

    private void ResolveReferencesIfNeeded()
    {
        if (selectionTaskSpawner == null) selectionTaskSpawner = FindObjectOfType<SelectionTaskSpawner>();
        if (handSizeController == null) handSizeController = FindObjectOfType<HandSizeController>();
        if (grabManager == null) grabManager = FindObjectOfType<MyGrabManager>();
        if (studyController == null) studyController = FindObjectOfType<StudyController>();
    }

    private void ToggleStudyController(bool enable)
    {
        if (!disableStudyControllerWhenActive) return;
        if (studyController == null) return;
        studyController.enabled = enable;
    }

    private void SubscribeSpawnerEvent()
    {
        if (selectionTaskSpawner == null) return;
        selectionTaskSpawner.OnTargetDeliveredToArea -= HandleTargetDelivered;
        selectionTaskSpawner.OnTargetDeliveredToArea += HandleTargetDelivered;
    }

    private void UnsubscribeSpawnerEvent()
    {
        if (selectionTaskSpawner == null) return;
        selectionTaskSpawner.OnTargetDeliveredToArea -= HandleTargetDelivered;
    }

    private void OnValidate()
    {
        regenDelayAfterTrialSeconds = Mathf.Max(0f, regenDelayAfterTrialSeconds);
        currentCondition.handScaleFactor = Mathf.Max(0.001f, currentCondition.handScaleFactor);
        currentCondition.detectRadius = Mathf.Max(0.001f, currentCondition.detectRadius);
        currentCondition.sphereDiameter = Mathf.Max(0.01f, currentCondition.sphereDiameter);
        currentCondition.minimumCenterDistance = Mathf.Max(0.01f, currentCondition.minimumCenterDistance);
    }
}

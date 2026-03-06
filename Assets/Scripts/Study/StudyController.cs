using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public class StudyController : MonoBehaviour
{
    public struct TrialLifecycleEventData
    {
        public int participantId;
        public int blockOrderPosition;
        public int totalBlockCount;
        public int trialNumber;
        public int totalTrialCount;
        public FullTrialCondition condition;

        public TrialLifecycleEventData(
            int participantId,
            int blockOrderPosition,
            int totalBlockCount,
            int trialNumber,
            int totalTrialCount,
            FullTrialCondition condition)
        {
            this.participantId = participantId;
            this.blockOrderPosition = blockOrderPosition;
            this.totalBlockCount = totalBlockCount;
            this.trialNumber = trialNumber;
            this.totalTrialCount = totalTrialCount;
            this.condition = condition;
        }
    }

    public event Action<TrialLifecycleEventData> OnTrialStarted;
    public event Action<TrialLifecycleEventData> OnTrialCompleted;

    [Serializable]
    public struct BlockCondition
    {
        public float handScaleFactor;
        public float detectRadius;

        public BlockCondition(float handScaleFactor, float detectRadius)
        {
            this.handScaleFactor = handScaleFactor;
            this.detectRadius = detectRadius;
        }

        public override string ToString()
        {
            return $"HandScaleFactor={handScaleFactor:0.###}, DetectRadius={detectRadius:0.###}";
        }
    }

    [Serializable]
    public struct TaskTrialCondition
    {
        public float sphereDiameter;
        public float minimumCenterDistance;
        public SelectionTaskSpawner.TargetDistanceRegion targetDistanceRegion;

        public TaskTrialCondition(float diameter, float minCenterDistance, SelectionTaskSpawner.TargetDistanceRegion region)
        {
            sphereDiameter = diameter;
            minimumCenterDistance = minCenterDistance;
            targetDistanceRegion = region;
        }

        public override string ToString()
        {
            return $"Diameter={sphereDiameter:0.###}, DensityMinDist={minimumCenterDistance:0.###}, DistanceRegion={targetDistanceRegion}";
        }
    }

    [Serializable]
    public struct FullTrialCondition
    {
        public float handScaleFactor;
        public float detectRadius;
        public float sphereDiameter;
        public float minimumCenterDistance;
        public SelectionTaskSpawner.TargetDistanceRegion targetDistanceRegion;

        public FullTrialCondition(BlockCondition block, TaskTrialCondition task)
        {
            handScaleFactor = block.handScaleFactor;
            detectRadius = block.detectRadius;
            sphereDiameter = task.sphereDiameter;
            minimumCenterDistance = task.minimumCenterDistance;
            targetDistanceRegion = task.targetDistanceRegion;
        }

        public override string ToString()
        {
            return $"HandScaleFactor={handScaleFactor:0.###}, DetectRadius={detectRadius:0.###}, Diameter={sphereDiameter:0.###}, DensityMinDist={minimumCenterDistance:0.###}, DistanceRegion={targetDistanceRegion}";
        }
    }

    [Header("References")]
    [Tooltip("Study1 场景中的 SelectionTaskSpawner。可留空，运行时自动查找。")]
    public SelectionTaskSpawner selectionTaskSpawner;
    
    [Tooltip("Study1 场景中的 HandSizeController。可留空，运行时自动查找。")]
    public HandSizeController handSizeController;
    
    [Tooltip("用于抓取判定半径的 MyGrabManager。可留空，运行时自动查找。")]
    public MyGrabManager grabManager;

    [Header("Participant")]
    [Tooltip("被试ID（整数）。用于通过 Latin square 映射 block 顺序与 trial 顺序。")]
    public int participantId = 1;

    [Header("Block Variables (HandScale Levels x DetectRadius Levels = Blocks)")]
    [Tooltip("HandSizeController.ScaleFactor 取值。支持任意数量（至少1个）。")]
    public float[] handScaleFactorLevels = new float[] { 0.75f, 0.875f, 1.0f, 1.125f, 1.25f };
    
    [Tooltip("MyGrabManager.detectRadius 取值。支持任意数量（至少1个）。")]
    public float[] detectRadiusLevels = new float[] { 0.03f, 0.035f, 0.045f, 0.055f, 0.065f };
    
    [FormerlySerializedAs("currentBlockToRun")]
    [Tooltip("当前要运行第几个 block（1-based，按 participantId 对应的 Latin square 顺序解释）。")]
    [Min(1)]
    public int currentBlockOrderPosition = 1;

    [Header("Task Variables Inside Each Block (Sphere Levels x MinDistanceMultiplier Levels = Trials)")]
    [Tooltip("小球大小取值（Sphere Diameter）。支持任意数量（至少1个）。")]
    public float[] sphereDiameterLevels = new float[] { 0.01f, 0.02f, 0.03f };

    [Tooltip("最小中心距相对当前球直径的倍数（Minimum Center Distance Multiplier）。支持任意数量（至少1个）。例如 1.0 表示 1.0d。")]
    public float[] minimumCenterDistanceMultiplierLevels = new float[] { 1.0f, 1.25f, 1.5f };

    [Tooltip("目标距离区域（Target Distance Region）。用于每个 trial 随机选择。")]
    public SelectionTaskSpawner.TargetDistanceRegion[] targetDistanceLevels = new SelectionTaskSpawner.TargetDistanceRegion[]
    {
        SelectionTaskSpawner.TargetDistanceRegion.Near,
        SelectionTaskSpawner.TargetDistanceRegion.Mid,
        SelectionTaskSpawner.TargetDistanceRegion.Far
    };

    [Tooltip("每个 trial 条件的重复次数。1=不重复，2=每种条件再随机一遍，依此类推。")]
    [Min(1)]
    public int trialRepeatCount = 1;

    [Header("Run Control")]
    [Tooltip("运行后自动开始实验流程。")]
    public bool autoStartOnPlay = true;

    [Tooltip("开始某个 block 后，是否等待 Start 触发器点击再开始第一个 trial。")]
    public bool waitForStartTriggerToBeginBlock = true;

    [Tooltip("用于开始 block 的触发器（例如场景中的 Start UI，对象上挂载 ObjectTriggerReceiver）。可留空，运行时自动查找。")]
    public ObjectTriggerReceiver blockStartTriggerReceiver;

    [Tooltip("开始实验时，自动关闭 spawner 自带的 generateOnStart，避免重复生成。")]
    public bool disableSpawnerGenerateOnStart = true;

    [Tooltip("每个 trial 完成后到下一 trial 开始前的间隔（秒）。")]
    [Min(0f)]
    public float interTrialDelaySeconds = 0.25f;

    [Header("Runtime Output")]
    [SerializeField] private int totalBlockCount = 0;
    [SerializeField] private int resolvedBlockOrderPosition = 0;
    [SerializeField] private BlockCondition currentBlockCondition;
    [SerializeField] private int totalTrialCount = 0;
    [SerializeField] private int completedTrialCount = 0;
    [SerializeField] private int currentTrialNumber = 0;
    [SerializeField] private FullTrialCondition currentCondition;
    [SerializeField] private bool isStudyRunning = false;
    [SerializeField] private bool isWaitingForBlockStartTrigger = false;

    private readonly List<BlockCondition> _orderedBlocks = new List<BlockCondition>(25);
    private readonly List<TaskTrialCondition> _orderedTrials = new List<TaskTrialCondition>(16);
    private Coroutine _advanceCoroutine;

    private void Awake()
    {
        ResolveReferencesIfNeeded();
        if (disableSpawnerGenerateOnStart && selectionTaskSpawner != null)
        {
            selectionTaskSpawner.generateOnStart = false;
        }
        PrepareBlockSequence();
        PrepareTrialSequence();
    }

    private void OnEnable()
    {
        SubscribeSpawnerEvent();
    }

    private void Start()
    {
        if (autoStartOnPlay)
        {
            StartStudy();
        }
    }

    private void OnDisable()
    {
        UnsubscribeSpawnerEvent();
    }

    [ContextMenu("Start Study")]
    public void StartStudy()
    {
        ResolveReferencesIfNeeded();
        if (selectionTaskSpawner == null)
        {
            Debug.LogError("[StudyController] SelectionTaskSpawner not found.");
            return;
        }

        PrepareBlockSequence();
        PrepareTrialSequence();
        if (_orderedBlocks.Count == 0 || _orderedTrials.Count == 0)
        {
            Debug.LogError("[StudyController] Sequence is empty. Check factor levels.");
            return;
        }

        int blockIndexOneBased = currentBlockOrderPosition;
        if (blockIndexOneBased < 1 || blockIndexOneBased > _orderedBlocks.Count)
        {
            Debug.LogError($"[StudyController] currentBlockOrderPosition must be within 1~{_orderedBlocks.Count}.");
            return;
        }

        if (disableSpawnerGenerateOnStart)
        {
            selectionTaskSpawner.generateOnStart = false;
        }

        resolvedBlockOrderPosition = blockIndexOneBased;
        currentBlockCondition = _orderedBlocks[blockIndexOneBased - 1];
        ApplyBlockCondition(currentBlockCondition);

        completedTrialCount = 0;
        currentTrialNumber = 0;
        isStudyRunning = false;

        if (waitForStartTriggerToBeginBlock)
        {
            if (!TryEnterWaitForBlockStartTrigger())
            {
                return;
            }
            return;
        }

        BeginCurrentBlockTrials();
    }

    [ContextMenu("Stop Study")]
    public void StopStudy()
    {
        isStudyRunning = false;
        ExitWaitForBlockStartTrigger();
        if (_advanceCoroutine != null)
        {
            StopCoroutine(_advanceCoroutine);
            _advanceCoroutine = null;
        }
    }

    [ContextMenu("Start Next Trial")]
    public void StartNextTrialManually()
    {
        if (_orderedTrials.Count == 0)
        {
            PrepareTrialSequence();
        }

        int nextIndex = Mathf.Clamp(completedTrialCount, 0, Mathf.Max(0, _orderedTrials.Count - 1));
        StartTrialAt(nextIndex);
    }

    private void HandleTargetDelivered()
    {
        if (!isStudyRunning) return;

        TrialLifecycleEventData completedData = new TrialLifecycleEventData(
            participantId,
            resolvedBlockOrderPosition,
            totalBlockCount,
            currentTrialNumber,
            totalTrialCount,
            currentCondition);
        OnTrialCompleted?.Invoke(completedData);

        completedTrialCount = Mathf.Min(completedTrialCount + 1, totalTrialCount);
        if (completedTrialCount >= totalTrialCount)
        {
            isStudyRunning = false;
            currentTrialNumber = totalTrialCount;
            Debug.Log($"[StudyController] Block finished. Participant {participantId}, block {resolvedBlockOrderPosition}/{totalBlockCount}, trials: {totalTrialCount}. You can now rest.");
            return;
        }

        if (_advanceCoroutine != null)
        {
            StopCoroutine(_advanceCoroutine);
            _advanceCoroutine = null;
        }

        _advanceCoroutine = StartCoroutine(AdvanceToNextTrialAfterDelay(interTrialDelaySeconds));
    }

    private IEnumerator AdvanceToNextTrialAfterDelay(float delay)
    {
        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        int nextIndex = completedTrialCount;
        StartTrialAt(nextIndex);
        _advanceCoroutine = null;
    }

    private void StartTrialAt(int trialIndex)
    {
        if (selectionTaskSpawner == null) return;
        if (trialIndex < 0 || trialIndex >= _orderedTrials.Count)
        {
            Debug.LogWarning($"[StudyController] Trial index out of range: {trialIndex}");
            return;
        }

        TaskTrialCondition condition = _orderedTrials[trialIndex];
        condition.targetDistanceRegion = GetRandomTargetDistanceRegion();
        currentCondition = new FullTrialCondition(currentBlockCondition, condition);
        currentTrialNumber = trialIndex + 1;

        TrialLifecycleEventData startedData = new TrialLifecycleEventData(
            participantId,
            resolvedBlockOrderPosition,
            totalBlockCount,
            currentTrialNumber,
            totalTrialCount,
            currentCondition);
        OnTrialStarted?.Invoke(startedData);

        ApplyConditionToSpawner(condition);
        selectionTaskSpawner.Generate();

        Debug.Log($"[StudyController] Block {resolvedBlockOrderPosition}/{totalBlockCount} | Trial {currentTrialNumber}/{totalTrialCount} started | Participant {participantId} | BlockCondition: {currentBlockCondition} | TaskCondition: {condition}");
    }

    private void ApplyConditionToSpawner(TaskTrialCondition c)
    {
        selectionTaskSpawner.sphereDiameter = c.sphereDiameter;
        selectionTaskSpawner.minimumCenterDistance = c.minimumCenterDistance;
        selectionTaskSpawner.targetDistanceRegion = c.targetDistanceRegion;
    }

    private void ApplyBlockCondition(BlockCondition c)
    {
        if (handSizeController != null)
        {
            handSizeController.SetScaleFactor(c.handScaleFactor);
        }
        else
        {
            Debug.LogWarning("[StudyController] HandSizeController not found, hand scale block factor is not applied.");
        }

        if (grabManager != null)
        {
            grabManager.detectRadius = c.detectRadius;
        }
        else
        {
            Debug.LogWarning("[StudyController] MyGrabManager not found, detectRadius block factor is not applied.");
        }
    }

    private void PrepareBlockSequence()
    {
        _orderedBlocks.Clear();

        List<BlockCondition> baseOrder = BuildBaseBlockConditionList();
        if (baseOrder.Count == 0)
        {
            totalBlockCount = 0;
            return;
        }

        int n = baseOrder.Count;
        int shift = Mod(participantId - 1, n);

        // Cyclic Latin square: row p is a circular shift of base block sequence.
        for (int i = 0; i < n; i++)
        {
            int idx = (i + shift) % n;
            _orderedBlocks.Add(baseOrder[idx]);
        }

        totalBlockCount = _orderedBlocks.Count;
    }

    private void PrepareTrialSequence()
    {
        _orderedTrials.Clear();

        List<TaskTrialCondition> baseOrder = BuildBaseTaskTrialConditionList();
        if (baseOrder.Count == 0)
        {
            totalTrialCount = 0;
            return;
        }

        int n = baseOrder.Count;
        int shift = Mod(participantId - 1, n);

        // Cyclic Latin square: row p is a circular shift of base sequence.
        for (int i = 0; i < n; i++)
        {
            int idx = (i + shift) % n;
            _orderedTrials.Add(baseOrder[idx]);
        }

        int repeat = Mathf.Max(1, trialRepeatCount);
        for (int r = 1; r < repeat; r++)
        {
            _orderedTrials.AddRange(_orderedTrials.GetRange(0, n));
        }

        totalTrialCount = _orderedTrials.Count;
    }

    private List<BlockCondition> BuildBaseBlockConditionList()
    {
        int handScaleLevelCount = handScaleFactorLevels != null ? handScaleFactorLevels.Length : 0;
        int detectRadiusLevelCount = detectRadiusLevels != null ? detectRadiusLevels.Length : 0;
        int estimatedCount = Mathf.Max(1, handScaleLevelCount * detectRadiusLevelCount);
        List<BlockCondition> list = new List<BlockCondition>(estimatedCount);

        if (!HasAtLeastOneLevel(handScaleFactorLevels) || !HasAtLeastOneLevel(detectRadiusLevels))
        {
            Debug.LogError("[StudyController] Block variables must each include at least 1 level.");
            return list;
        }

        for (int i = 0; i < handScaleFactorLevels.Length; i++)
        {
            for (int j = 0; j < detectRadiusLevels.Length; j++)
            {
                list.Add(new BlockCondition(handScaleFactorLevels[i], detectRadiusLevels[j]));
            }
        }

        return list;
    }

    private List<TaskTrialCondition> BuildBaseTaskTrialConditionList()
    {
        int diameterLevelCount = sphereDiameterLevels != null ? sphereDiameterLevels.Length : 0;
        int minDistMultiplierLevelCount = minimumCenterDistanceMultiplierLevels != null ? minimumCenterDistanceMultiplierLevels.Length : 0;
        int estimatedCount = Mathf.Max(1, diameterLevelCount * minDistMultiplierLevelCount);
        List<TaskTrialCondition> list = new List<TaskTrialCondition>(estimatedCount);

        if (!HasAtLeastOneLevel(sphereDiameterLevels) ||
            !HasAtLeastOneLevel(minimumCenterDistanceMultiplierLevels) ||
            !HasAtLeastOneLevel(targetDistanceLevels))
        {
            Debug.LogError("[StudyController] Task variables must include at least 1 diameter level, at least 1 minimum-center-distance multiplier, and at least 1 target distance option.");
            return list;
        }

        for (int i = 0; i < sphereDiameterLevels.Length; i++)
        {
            float diameter = sphereDiameterLevels[i];
            for (int j = 0; j < minimumCenterDistanceMultiplierLevels.Length; j++)
            {
                float minCenterDistance = diameter * minimumCenterDistanceMultiplierLevels[j];
                list.Add(new TaskTrialCondition(
                    diameter,
                    minCenterDistance,
                    targetDistanceLevels[0]));
            }
        }

        return list;
    }

    private void ResolveReferencesIfNeeded()
    {
        if (selectionTaskSpawner == null)
        {
            selectionTaskSpawner = FindObjectOfType<SelectionTaskSpawner>();
        }
        if (handSizeController == null)
        {
            handSizeController = FindObjectOfType<HandSizeController>();
        }
        if (grabManager == null)
        {
            grabManager = FindObjectOfType<MyGrabManager>();
        }
        if (blockStartTriggerReceiver == null)
        {
            blockStartTriggerReceiver = FindObjectOfType<ObjectTriggerReceiver>();
        }
    }

    private void SubscribeSpawnerEvent()
    {
        ResolveReferencesIfNeeded();
        if (selectionTaskSpawner != null)
        {
            selectionTaskSpawner.OnTargetDeliveredToArea -= HandleTargetDelivered;
            selectionTaskSpawner.OnTargetDeliveredToArea += HandleTargetDelivered;
        }
    }

    private void UnsubscribeSpawnerEvent()
    {
        if (selectionTaskSpawner != null)
        {
            selectionTaskSpawner.OnTargetDeliveredToArea -= HandleTargetDelivered;
        }
        UnsubscribeBlockStartTriggerEvent();
    }

    private bool TryEnterWaitForBlockStartTrigger()
    {
        ResolveReferencesIfNeeded();
        if (blockStartTriggerReceiver == null)
        {
            Debug.LogError("[StudyController] waitForStartTriggerToBeginBlock is enabled, but ObjectTriggerReceiver is not assigned/found.");
            return false;
        }

        ExitWaitForBlockStartTrigger();
        blockStartTriggerReceiver.ResetTriggerState();
        blockStartTriggerReceiver.FingerTouched += HandleBlockStartTriggerTouched;
        isWaitingForBlockStartTrigger = true;
        Debug.Log($"[StudyController] Block {resolvedBlockOrderPosition}/{totalBlockCount} is ready. Waiting for Start trigger touch.");
        return true;
    }

    private void ExitWaitForBlockStartTrigger()
    {
        isWaitingForBlockStartTrigger = false;
        UnsubscribeBlockStartTriggerEvent();
    }

    private void UnsubscribeBlockStartTriggerEvent()
    {
        if (blockStartTriggerReceiver != null)
        {
            blockStartTriggerReceiver.FingerTouched -= HandleBlockStartTriggerTouched;
        }
    }

    private void HandleBlockStartTriggerTouched()
    {
        if (!isWaitingForBlockStartTrigger)
        {
            return;
        }

        ExitWaitForBlockStartTrigger();
        BeginCurrentBlockTrials();
    }

    private void BeginCurrentBlockTrials()
    {
        isStudyRunning = true;
        StartTrialAt(0);
    }

    private static bool HasAtLeastOneLevel<T>(T[] levels)
    {
        return levels != null && levels.Length > 0;
    }

    private SelectionTaskSpawner.TargetDistanceRegion GetRandomTargetDistanceRegion()
    {
        if (!HasAtLeastOneLevel(targetDistanceLevels))
        {
            return SelectionTaskSpawner.TargetDistanceRegion.Near;
        }
        int index = UnityEngine.Random.Range(0, targetDistanceLevels.Length);
        return targetDistanceLevels[index];
    }

    private static int Mod(int value, int mod)
    {
        int r = value % mod;
        return r < 0 ? r + mod : r;
    }

    private void OnValidate()
    {
        if (participantId <= 0) participantId = 1;
        if (currentBlockOrderPosition < 1) currentBlockOrderPosition = 1;
        interTrialDelaySeconds = Mathf.Max(0f, interTrialDelaySeconds);
        if (trialRepeatCount < 1) trialRepeatCount = 1;
    }
}

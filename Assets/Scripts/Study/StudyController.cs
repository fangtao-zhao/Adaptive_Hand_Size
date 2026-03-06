using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class StudyController : MonoBehaviour
{
    public enum BlockSelection
    {
        Block1 = 1,
        Block2 = 2,
        Block3 = 3,
        Block4 = 4,
        Block5 = 5,
        Block6 = 6,
        Block7 = 7,
        Block8 = 8,
        Block9 = 9,
        Block10 = 10,
        Block11 = 11,
        Block12 = 12,
        Block13 = 13,
        Block14 = 14,
        Block15 = 15,
        Block16 = 16,
        Block17 = 17,
        Block18 = 18,
        Block19 = 19,
        Block20 = 20,
        Block21 = 21,
        Block22 = 22,
        Block23 = 23,
        Block24 = 24,
        Block25 = 25
    }

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

    [Header("Block Variables (5 x 5 = 25 blocks)")]
    [Tooltip("HandSizeController.ScaleFactor 的5个取值。")]
    public float[] handScaleFactorLevels = new float[] { 0.75f, 0.875f, 1.0f, 1.125f, 1.25f };
    
    [Tooltip("MyGrabManager.detectRadius 的5个取值。")]
    public float[] detectRadiusLevels = new float[] { 0.03f, 0.035f, 0.045f, 0.055f, 0.065f };
    
    [Tooltip("当前要运行哪个 block（按 participantId 对应的 Latin square 顺序解释）。")]
    public BlockSelection currentBlockToRun = BlockSelection.Block1;

    [Header("Task Variables Inside Each Block (Sphere Levels x MinDistanceMultiplier Levels = Trials)")]
    [Tooltip("小球大小取值（Sphere Diameter）。支持任意数量（至少1个）。")]
    public float[] sphereDiameterLevels = new float[] { 0.02f, 0.03f, 0.04f };

    [Tooltip("最小中心距相对当前球直径的倍数（Minimum Center Distance Multiplier）。支持任意数量（至少1个）。例如 1.0 表示 1.0d。")]
    public float[] minimumCenterDistanceMultiplierLevels = new float[] { 1.0f, 1.5f, 2.0f };

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

    private readonly List<BlockCondition> _orderedBlocks = new List<BlockCondition>(25);
    private readonly List<TaskTrialCondition> _orderedTrials = new List<TaskTrialCondition>(16);
    private Coroutine _advanceCoroutine;

    private void Awake()
    {
        ResolveReferencesIfNeeded();
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

        int blockIndexOneBased = (int)currentBlockToRun;
        if (blockIndexOneBased < 1 || blockIndexOneBased > _orderedBlocks.Count)
        {
            Debug.LogError($"[StudyController] currentBlockToRun must be within 1~{_orderedBlocks.Count}.");
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
        isStudyRunning = true;

        StartTrialAt(0);
    }

    [ContextMenu("Stop Study")]
    public void StopStudy()
    {
        isStudyRunning = false;
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
        List<BlockCondition> list = new List<BlockCondition>(25);
        if (!HasExactlyLevels(handScaleFactorLevels, 5) || !HasExactlyLevels(detectRadiusLevels, 5))
        {
            Debug.LogError("[StudyController] Block variables must each have exactly 5 levels.");
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
    }

    private static bool HasExactlyLevels<T>(T[] levels, int count)
    {
        return levels != null && levels.Length == count;
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
        interTrialDelaySeconds = Mathf.Max(0f, interTrialDelaySeconds);
        if (trialRepeatCount < 1) trialRepeatCount = 1;
    }
}

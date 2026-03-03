using UnityEngine;
using UnityEngine.Serialization;

public class MyGrabManager : MonoBehaviour
{
    [Header("Tip Triggers (assign in Inspector)")]
    public FingerTipTrigger thumbTip;
    public FingerTipTrigger indexTip;

    [Header("Tip Transforms (assign in Inspector)")]
    public Transform thumbTipTransform;
    public Transform indexTipTransform;

    [Header("Grab Settings")]
    [Tooltip("抓取检测半径（米）。")]
    public float detectRadius = 0.045f;

    [Tooltip("只检测这些层上的可抓取物体（建议你把可抓取物体都放到Grabbable层）。")]
    public LayerMask grabbableLayer;

    [Tooltip("抓取锚点。物体会跟随这个点（建议是手上的一个空节点）。不填则用拇指指尖。")]
    [FormerlySerializedAs("grabParent")]
    public Transform grabAnchor;

    [Tooltip("开启后，抓取成功时先把物体吸附到抓取锚点位置，再进入跟随。")]
    public bool suctionOnGrab = false;

    [Header("Pinch Detection (Hysteresis + Debounce)")]
    [Tooltip("是否使用指尖 trigger 重叠作为 pinch 信号（更贴近你原本的做法，但会抖）。建议勾选后也配合 Release Debounce。")]
    public bool useTipOverlapSignal = true;

    [Tooltip("是否使用指尖距离作为 pinch 信号（更稳定，移动中不容易误脱手）。")]
    public bool useTipDistanceSignal = true;

    [Tooltip("指尖距离进入抓取阈值（米）。例如 0.02~0.03。")]
    public float pinchBeginDistance = 0.028f;

    [Tooltip("指尖距离退出抓取阈值（米，必须大于 Begin 才有滞回）。例如 0.035~0.05。")]
    public float pinchEndDistance = 0.040f;

    [Tooltip("释放去抖（秒）。pinch 信号“松开”持续这么久才真正释放，避免移动时一两帧抖动导致脱手。")]
    public float releaseDebounceSeconds = 0.10f;

    [Tooltip("距离信号平滑系数（0~1）。越大越平滑但更“粘”。")]
    [Range(0f, 1f)]
    public float distanceSmoothing = 0.5f;

    [Header("Follow / Throw")]
    [Tooltip("抓取期间是否将刚体设为 Kinematic 并用 MovePosition/MoveRotation 跟随（推荐，稳定）。")]
    public bool kinematicFollow = true;
    
    [Tooltip("开启后使用“硬跟随”：每帧直接对齐抓取点，尽量接近子物体效果（拖尾最小）。")]
    public bool directFollowLikeChild = false;

    [Tooltip("是否让被抓取物体跟随手部旋转。关闭后仅跟随位置。")]
    public bool enableRotationFollow = true;

    [Header("Follow Smoothing (anti-jitter)")]
    [Tooltip("位置跟随平滑强度（Hz）。值越大跟手性越强，越小越稳。0 表示不平滑。")]
    [Min(0f)]
    public float followPositionSmoothingHz = 20f;

    [Tooltip("旋转跟随平滑强度（Hz）。值越大跟手性越强，越小越稳。0 表示不平滑。")]
    [Min(0f)]
    public float followRotationSmoothingHz = 24f;

    [Tooltip("位置抖动死区（米）。小于该位移时忽略，减少手部追踪微抖。")]
    [Min(0f)]
    public float followPositionDeadzone = 0.0015f;

    [Tooltip("旋转抖动死区（度）。小于该角度变化时忽略，减少微小抖动。")]
    [Min(0f)]
    public float followRotationDeadzoneDeg = 0.6f;

    [Tooltip("位移超过该值后，自动提高跟随速度以减少拖尾（米）。")]
    [Min(0.001f)]
    public float fastFollowDistance = 0.02f;

    [Tooltip("角度差超过该值后，自动提高跟随速度以减少拖尾（度）。")]
    [Min(0.1f)]
    public float fastFollowAngleDeg = 8f;

    [Tooltip("允许的最大位置落后距离（米）。超过后会硬限制回拉，防止明显延迟。")]
    [Min(0f)]
    public float maxFollowPositionLag = 0.015f;

    [Tooltip("允许的最大旋转落后角度（度）。超过后会硬限制回拉，防止明显延迟。")]
    [Min(0f)]
    public float maxFollowRotationLagDeg = 10f;

    [Tooltip("是否在松手时给刚体速度（投掷感）。")]
    public bool applyReleaseVelocity = true;

    [Tooltip("释放速度倍增（可用于微调投掷手感）。")]
    public float releaseVelocityScale = 1.0f;

    [Tooltip("角速度倍增（可用于微调旋转投掷手感）。")]
    public float releaseAngularVelocityScale = 1.0f;

    [Tooltip("限制松手时的线速度上限（米/秒）。避免因追踪抖动导致“乱飞”。")]
    public float maxReleaseLinearSpeed = 3.0f;

    [Tooltip("限制松手时的角速度上限（弧度/秒）。避免因追踪抖动导致“乱转乱飞”。")]
    public float maxReleaseAngularSpeed = 25.0f;

    [Header("Release Collision Safety")]
    [Tooltip("松手后短暂忽略与手部碰撞，避免球在手里/指尖附近解除 kinematic 时被弹飞。")]
    public bool ignoreHandCollisionsOnRelease = true;

    [Tooltip("松手后忽略碰撞的时长（秒）。")]
    public float ignoreHandCollisionsSeconds = 0.10f;

    [Header("Debug")]
    public bool drawGizmos = true;

    // runtime
    private bool _isPinching;
    private bool _pinchStartedNearTarget;
    private float _releaseTimer;
    private float _smoothedTipDistance;
    private float _grabRetryTimer;

    private Grabbable _grabbed;
    private bool _grabbedOriginalKinematic;
    private RigidbodyInterpolation _grabbedOriginalInterpolation;
    private Pose _anchorToObjectOffset;
    private Pose _lastTargetPose;
    private bool _hasLastTargetPose;
    private Pose _filteredTargetPose;
    private bool _hasFilteredTargetPose;
    private Vector3 _estimatedLinearVelocity;
    private Vector3 _estimatedAngularVelocity;

    private Collider[] _handCollidersCache;
    private Collider[] _grabbedCollidersCache;
    private float _reenableCollisionsAt;

    private Vector3 GetPinchPoint()
    {
        if (thumbTipTransform == null || indexTipTransform == null)
            return transform.position;

        return (thumbTipTransform.position + indexTipTransform.position) * 0.5f;
    }

    private Pose GetAnchorPose()
    {
        if (grabAnchor != null)
            return new Pose(grabAnchor.position, grabAnchor.rotation);

        // grabAnchor 为空时，默认使用两指尖中点 + 指尖平均旋转作为虚拟锚点，
        // 这样抓取物体不仅跟随位置，也能稳定跟随手部旋转。
        if (thumbTipTransform != null && indexTipTransform != null)
        {
            Vector3 pos = (thumbTipTransform.position + indexTipTransform.position) * 0.5f;
            Quaternion rot = Quaternion.Slerp(thumbTipTransform.rotation, indexTipTransform.rotation, 0.5f);
            return new Pose(pos, rot);
        }

        if (thumbTipTransform != null)
            return new Pose(thumbTipTransform.position, thumbTipTransform.rotation);

        if (indexTipTransform != null)
            return new Pose(indexTipTransform.position, indexTipTransform.rotation);

        return new Pose(transform.position, transform.rotation);
    }

    private static Pose Inverse(Pose p)
    {
        Quaternion invRot = Quaternion.Inverse(p.rotation);
        return new Pose(invRot * (-p.position), invRot);
    }

    private static Pose Multiply(Pose a, Pose b)
    {
        return new Pose(a.position + a.rotation * b.position, a.rotation * b.rotation);
    }

    private void Update()
    {
        UpdatePinchState(Time.deltaTime);
        RetryGrabWhilePinching(Time.deltaTime);
        UpdateGrabTarget(Time.deltaTime);
        
        if (directFollowLikeChild)
        {
            FollowGrabbedObjectImmediate(Time.deltaTime);
        }
    }

    private void FixedUpdate()
    {
        if (directFollowLikeChild) return;
        FollowGrabbedObject();
    }

    private void UpdatePinchState(float dt)
    {
        bool hasTips = thumbTipTransform != null && indexTipTransform != null;
        if (!hasTips)
        {
            if (_isPinching)
            {
                _isPinching = false;
                Release(instant: true);
            }
            return;
        }

        float tipDistance = Vector3.Distance(thumbTipTransform.position, indexTipTransform.position);
        if (_smoothedTipDistance <= 0f) _smoothedTipDistance = tipDistance;
        _smoothedTipDistance = Mathf.Lerp(_smoothedTipDistance, tipDistance, 1f - Mathf.Clamp01(distanceSmoothing));

        bool overlapSignal = false;
        if (useTipOverlapSignal && thumbTip != null && indexTip != null)
        {
            // 只要任一边仍报告重叠就算 overlapSignal 仍存在（避免事件顺序导致的“单边先退出就释放”）
            overlapSignal = thumbTip.IsOverlappingOtherTip || indexTip.IsOverlappingOtherTip;
        }

        bool distanceWantsPinch;
        if (!_isPinching)
        {
            distanceWantsPinch = _smoothedTipDistance <= pinchBeginDistance;
        }
        else
        {
            distanceWantsPinch = _smoothedTipDistance <= pinchEndDistance;
        }

        bool pinchSignal;
        if (useTipOverlapSignal && useTipDistanceSignal) pinchSignal = overlapSignal || distanceWantsPinch;
        else if (useTipDistanceSignal) pinchSignal = distanceWantsPinch;
        else pinchSignal = overlapSignal;

        if (!_isPinching)
        {
            if (pinchSignal)
            {
                _isPinching = true;
                _releaseTimer = 0f;
                _grabRetryTimer = 0f;
                _pinchStartedNearTarget = HasNearbyGrabbable(GetPinchPoint());
                TryGrabNearest();
            }
            return;
        }

        // 已在 pinch：只有在“松开信号”持续一段时间才释放
        if (pinchSignal)
        {
            _releaseTimer = 0f;
            return;
        }

        _releaseTimer += dt;
        if (_releaseTimer >= releaseDebounceSeconds)
        {
            _isPinching = false;
            _pinchStartedNearTarget = false;
            _releaseTimer = 0f;
            _grabRetryTimer = 0f;
            Release(instant: false);
        }
    }

    private void RetryGrabWhilePinching(float dt)
    {
        if (!_isPinching) return;
        if (_grabbed != null) return;
        if (!_pinchStartedNearTarget) return;

        // 仅当 pinch 开始时就处在可抓取目标附近，才允许保持 pinch 后重试抓取
        _grabRetryTimer += dt;
        if (_grabRetryTimer < 0.05f) return;
        _grabRetryTimer = 0f;

        TryGrabNearest();
    }

    private bool HasNearbyGrabbable(Vector3 pinchPoint)
    {
        Collider[] hits = Physics.OverlapSphere(pinchPoint, detectRadius, grabbableLayer, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0) return false;

        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            if (col == null) continue;

            var g = col.GetComponentInParent<Grabbable>();
            if (g != null) return true;
        }

        return false;
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

        Pose anchorPose = GetAnchorPose();
        Pose objPose;

        if (g.rb != null)
        {
            _grabbedOriginalKinematic = g.rb.isKinematic;
            _grabbedOriginalInterpolation = g.rb.interpolation;
            if (kinematicFollow)
            {
                // 先在非 kinematic 状态下清零速度，避免 Unity 警告。
                if (!g.rb.isKinematic)
                {
                    g.rb.velocity = Vector3.zero;
                    g.rb.angularVelocity = Vector3.zero;
                }

                g.rb.isKinematic = true;
                g.rb.interpolation = directFollowLikeChild ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
            }
        }

        bool shouldUseSuction = suctionOnGrab && g != null && g.name != "UICanvas";
        if (shouldUseSuction)
        {
            Quaternion keepRotation = g.transform.rotation;
            if (g.rb != null)
            {
                g.rb.position = anchorPose.position;
                g.rb.rotation = keepRotation;
            }
            else
            {
                g.transform.SetPositionAndRotation(anchorPose.position, keepRotation);
            }

            // 关键：吸附模式下直接使用“吸附后的目标位姿”计算偏移，避免一帧内 Transform/Rigidbody 同步时序造成回弹。
            objPose = new Pose(anchorPose.position, keepRotation);
        }
        else
        {
            objPose = new Pose(g.transform.position, g.transform.rotation);
        }
        _anchorToObjectOffset = Multiply(Inverse(anchorPose), objPose);

        // 不再 SetParent：避免 transform parenting 造成的物理手感问题
        _hasLastTargetPose = false;
        _hasFilteredTargetPose = false;
        _estimatedLinearVelocity = Vector3.zero;
        _estimatedAngularVelocity = Vector3.zero;

        if (ignoreHandCollisionsOnRelease)
        {
            CacheCollidersForCollisionIgnores();
            SetHandCollisionIgnores(ignore: true);
        }
    }

    private void Release(bool instant)
    {
        if (_grabbed == null) return;

        if (_grabbed.rb != null)
        {
            Vector3 linearVel = Vector3.zero;
            Vector3 angularVel = Vector3.zero;

            if (applyReleaseVelocity && !instant)
            {
                // 使用 FixedUpdate 里根据目标位姿估算的速度（更贴近 MovePosition 产生的实际速度）
                linearVel = _estimatedLinearVelocity;
                angularVel = _estimatedAngularVelocity;
                linearVel *= releaseVelocityScale;
                angularVel *= releaseAngularVelocityScale;

                if (maxReleaseLinearSpeed > 0f)
                    linearVel = Vector3.ClampMagnitude(linearVel, maxReleaseLinearSpeed);
                if (maxReleaseAngularSpeed > 0f)
                    angularVel = Vector3.ClampMagnitude(angularVel, maxReleaseAngularSpeed);
            }

            bool forceKinematic = _grabbed.forceKinematicOnRelease;
            bool stopMotion = _grabbed.stopMotionOnRelease;

            _grabbed.rb.isKinematic = forceKinematic ? true : _grabbedOriginalKinematic;
            _grabbed.rb.interpolation = _grabbedOriginalInterpolation;

            if (stopMotion)
            {
                // kinematic 刚体不支持设置角速度/线速度，避免警告。
                if (!_grabbed.rb.isKinematic)
                {
                    _grabbed.rb.velocity = Vector3.zero;
                    _grabbed.rb.angularVelocity = Vector3.zero;
                }
                _grabbed.rb.Sleep();
            }
            else if (!_grabbed.rb.isKinematic)
            {
                _grabbed.rb.velocity = linearVel;
                _grabbed.rb.angularVelocity = angularVel;
            }
        }

        if (ignoreHandCollisionsOnRelease)
        {
            // 延迟一小段时间再恢复碰撞，避免松手瞬间产生很大冲量
            _reenableCollisionsAt = Time.time + Mathf.Max(0f, ignoreHandCollisionsSeconds);
        }

        _grabbed = null;
    }

    private void UpdateGrabTarget(float dt)
    {
        if (ignoreHandCollisionsOnRelease && _handCollidersCache != null && _grabbedCollidersCache != null)
        {
            if (_reenableCollisionsAt > 0f && Time.time >= _reenableCollisionsAt)
            {
                SetHandCollisionIgnores(ignore: false);
                _reenableCollisionsAt = 0f;
            }
        }
    }

    private void FollowGrabbedObject()
    {
        if (_grabbed == null) return;

        Pose desiredPose = GetDesiredGrabPose();
        Pose targetPose = GetSmoothedFollowPose(desiredPose, Time.fixedDeltaTime);

        // 在 FixedUpdate 用目标位姿差分估算速度（更稳定，避免 Time.time/追踪抖动带来的尖峰）
        if (_hasLastTargetPose)
        {
            float dt = Time.fixedDeltaTime;
            if (dt > Mathf.Epsilon)
            {
                _estimatedLinearVelocity = (targetPose.position - _lastTargetPose.position) / dt;

                Quaternion dq = targetPose.rotation * Quaternion.Inverse(_lastTargetPose.rotation);
                dq.ToAngleAxis(out float angleDeg, out Vector3 axis);
                if (!float.IsNaN(axis.x) && axis != Vector3.zero)
                {
                    float angleRad = angleDeg * Mathf.Deg2Rad;
                    if (angleRad > Mathf.PI) angleRad -= 2f * Mathf.PI;
                    _estimatedAngularVelocity = axis.normalized * (angleRad / dt);
                }
                else
                {
                    _estimatedAngularVelocity = Vector3.zero;
                }
            }
        }
        _lastTargetPose = targetPose;
        _hasLastTargetPose = true;

        if (_grabbed.rb != null)
        {
            _grabbed.rb.MovePosition(targetPose.position);
            _grabbed.rb.MoveRotation(targetPose.rotation);
        }
        else
        {
            _grabbed.transform.SetPositionAndRotation(targetPose.position, targetPose.rotation);
        }
    }
    
    private Pose GetDesiredGrabPose()
    {
        Pose desired = Multiply(GetAnchorPose(), _anchorToObjectOffset);
        if (!enableRotationFollow && _grabbed != null)
        {
            Quaternion keepRot = _grabbed.rb != null ? _grabbed.rb.rotation : _grabbed.transform.rotation;
            desired.rotation = keepRot;
        }
        return desired;
    }
    
    private void FollowGrabbedObjectImmediate(float dt)
    {
        if (_grabbed == null) return;
        if (dt <= Mathf.Epsilon) dt = Mathf.Max(Time.deltaTime, 1f / 90f);

        Pose targetPose = GetDesiredGrabPose();

        if (_hasLastTargetPose)
        {
            _estimatedLinearVelocity = (targetPose.position - _lastTargetPose.position) / dt;

            Quaternion dq = targetPose.rotation * Quaternion.Inverse(_lastTargetPose.rotation);
            dq.ToAngleAxis(out float angleDeg, out Vector3 axis);
            if (!float.IsNaN(axis.x) && axis != Vector3.zero)
            {
                float angleRad = angleDeg * Mathf.Deg2Rad;
                if (angleRad > Mathf.PI) angleRad -= 2f * Mathf.PI;
                _estimatedAngularVelocity = axis.normalized * (angleRad / dt);
            }
            else
            {
                _estimatedAngularVelocity = Vector3.zero;
            }
        }

        _lastTargetPose = targetPose;
        _hasLastTargetPose = true;

        if (_grabbed.rb != null)
        {
            _grabbed.rb.position = targetPose.position;
            _grabbed.rb.rotation = targetPose.rotation;
        }
        else
        {
            _grabbed.transform.SetPositionAndRotation(targetPose.position, targetPose.rotation);
        }
    }

    private Pose GetSmoothedFollowPose(Pose desiredPose, float dt)
    {
        if (!_hasFilteredTargetPose)
        {
            _filteredTargetPose = desiredPose;
            _hasFilteredTargetPose = true;
            return desiredPose;
        }

        Vector3 currentPos = _filteredTargetPose.position;
        Quaternion currentRot = _filteredTargetPose.rotation;

        Vector3 posDelta = desiredPose.position - currentPos;
        float posDeltaMag = posDelta.magnitude;
        float posAlphaBase = ComputeExpSmoothingAlpha(followPositionSmoothingHz, dt);
        float posBoostT = Mathf.InverseLerp(followPositionDeadzone, fastFollowDistance, posDeltaMag);
        float posAlpha = Mathf.Lerp(posAlphaBase, 1f, posBoostT);
        if (posDeltaMag > followPositionDeadzone)
        {
            currentPos = posAlpha >= 1f ? desiredPose.position : Vector3.Lerp(currentPos, desiredPose.position, posAlpha);
        }
        if (maxFollowPositionLag > 0f)
        {
            Vector3 lag = desiredPose.position - currentPos;
            float lagMag = lag.magnitude;
            if (lagMag > maxFollowPositionLag && lagMag > Mathf.Epsilon)
            {
                currentPos = desiredPose.position - (lag / lagMag) * maxFollowPositionLag;
            }
        }

        float angleDelta = Quaternion.Angle(currentRot, desiredPose.rotation);
        float rotAlphaBase = ComputeExpSmoothingAlpha(followRotationSmoothingHz, dt);
        float rotBoostT = Mathf.InverseLerp(followRotationDeadzoneDeg, fastFollowAngleDeg, angleDelta);
        float rotAlpha = Mathf.Lerp(rotAlphaBase, 1f, rotBoostT);
        if (angleDelta > followRotationDeadzoneDeg)
        {
            currentRot = rotAlpha >= 1f ? desiredPose.rotation : Quaternion.Slerp(currentRot, desiredPose.rotation, rotAlpha);
        }
        if (maxFollowRotationLagDeg > 0f)
        {
            float lagAngle = Quaternion.Angle(currentRot, desiredPose.rotation);
            if (lagAngle > maxFollowRotationLagDeg)
            {
                currentRot = Quaternion.RotateTowards(desiredPose.rotation, currentRot, maxFollowRotationLagDeg);
            }
        }

        _filteredTargetPose = new Pose(currentPos, currentRot);
        return _filteredTargetPose;
    }

    private static float ComputeExpSmoothingAlpha(float smoothingHz, float dt)
    {
        if (smoothingHz <= 0f || dt <= 0f)
        {
            return 1f;
        }

        float lambda = 2f * Mathf.PI * smoothingHz;
        return 1f - Mathf.Exp(-lambda * dt);
    }

    private void OnValidate()
    {
        detectRadius = Mathf.Max(0.001f, detectRadius);
        pinchBeginDistance = Mathf.Max(0.001f, pinchBeginDistance);
        pinchEndDistance = Mathf.Max(pinchBeginDistance + 0.001f, pinchEndDistance);
        releaseDebounceSeconds = Mathf.Max(0f, releaseDebounceSeconds);
        followPositionSmoothingHz = Mathf.Max(0f, followPositionSmoothingHz);
        followRotationSmoothingHz = Mathf.Max(0f, followRotationSmoothingHz);
        followPositionDeadzone = Mathf.Max(0f, followPositionDeadzone);
        followRotationDeadzoneDeg = Mathf.Max(0f, followRotationDeadzoneDeg);
        fastFollowDistance = Mathf.Max(followPositionDeadzone + 0.0005f, fastFollowDistance);
        fastFollowAngleDeg = Mathf.Max(followRotationDeadzoneDeg + 0.1f, fastFollowAngleDeg);
        maxFollowPositionLag = Mathf.Max(0f, maxFollowPositionLag);
        maxFollowRotationLagDeg = Mathf.Max(0f, maxFollowRotationLagDeg);
        maxReleaseLinearSpeed = Mathf.Max(0f, maxReleaseLinearSpeed);
        maxReleaseAngularSpeed = Mathf.Max(0f, maxReleaseAngularSpeed);
        ignoreHandCollisionsSeconds = Mathf.Max(0f, ignoreHandCollisionsSeconds);
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;
        if (thumbTipTransform == null || indexTipTransform == null) return;

        Vector3 p = (thumbTipTransform.position + indexTipTransform.position) * 0.5f;
        Gizmos.DrawWireSphere(p, detectRadius);
    }

    private void CacheCollidersForCollisionIgnores()
    {
        if (_handCollidersCache == null || _handCollidersCache.Length == 0)
        {
            // 取当前 GrabManager 下所有非 Trigger 的 Collider 作为“手部碰撞体”
            // （指尖 trigger 自身不产生碰撞冲量，但手模型/手掌 Collider 可能会弹飞物体）
            _handCollidersCache = GetComponentsInChildren<Collider>(includeInactive: false);
        }

        if (_grabbed != null)
        {
            _grabbedCollidersCache = _grabbed.colliders != null && _grabbed.colliders.Length > 0
                ? _grabbed.colliders
                : _grabbed.GetComponentsInChildren<Collider>(includeInactive: false);
        }
    }

    private void SetHandCollisionIgnores(bool ignore)
    {
        if (_handCollidersCache == null || _grabbedCollidersCache == null) return;

        for (int i = 0; i < _handCollidersCache.Length; i++)
        {
            Collider hc = _handCollidersCache[i];
            if (hc == null) continue;
            // trigger 不会产生冲量，忽略它们意义不大
            if (hc.isTrigger) continue;

            for (int j = 0; j < _grabbedCollidersCache.Length; j++)
            {
                Collider oc = _grabbedCollidersCache[j];
                if (oc == null) continue;
                if (oc.isTrigger) continue;
                Physics.IgnoreCollision(hc, oc, ignore);
            }
        }
    }
}

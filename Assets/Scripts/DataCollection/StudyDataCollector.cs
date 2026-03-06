using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public class StudyDataCollector : MonoBehaviour
{
    [Header("References")]
    public StudyController studyController;
    public MyGrabManager grabManager;

    [Header("Output")]
    [Tooltip("Automatically create output files when play starts.")]
    public bool createSessionOnEnable = true;

    [Tooltip("CSV output folder under Assets/ExperimentData/Study1.")]
    public string outputSubFolder = "ExperimentData/Study1";

    [Tooltip("Fallback file name prefix when StudyController is unavailable.")]
    public string filePrefix = "study";

    [Tooltip("Write Debug.Log for key recording events.")]
    public bool verboseLog = false;

    private struct GrabAttemptRecord
    {
        public int attemptIndex;
        public float attemptTimeFromTrialStart;
        public float attemptTimeAbsolute;
        public MyGrabManager.GrabAttemptOutcome outcome;
    }

    private sealed class ActiveTrialContext
    {
        public StudyController.TrialLifecycleEventData trialData;
        public float trialStartTime;
        public readonly List<GrabAttemptRecord> attempts = new List<GrabAttemptRecord>();
    }

    private string _sessionId;
    private string _trialSummaryPath;
    private string _grabAttemptPath;
    private StreamWriter _trialSummaryWriter;
    private StreamWriter _grabAttemptWriter;
    private ActiveTrialContext _activeTrial;

    private void OnEnable()
    {
        ResolveReferencesIfNeeded();
        SubscribeEvents();
        if (createSessionOnEnable && Application.isPlaying)
        {
            EnsureSessionReady();
        }
    }

    private void OnDisable()
    {
        UnsubscribeEvents();
        CloseWriters();
    }

    private void ResolveReferencesIfNeeded()
    {
        if (studyController == null)
        {
            studyController = FindObjectOfType<StudyController>();
        }
        if (grabManager == null)
        {
            grabManager = FindObjectOfType<MyGrabManager>();
        }
    }

    private void SubscribeEvents()
    {
        if (studyController != null)
        {
            studyController.OnTrialStarted -= HandleTrialStarted;
            studyController.OnTrialStarted += HandleTrialStarted;
            studyController.OnTrialCompleted -= HandleTrialCompleted;
            studyController.OnTrialCompleted += HandleTrialCompleted;
        }

        if (grabManager != null)
        {
            grabManager.OnGrabAttemptRecorded -= HandleGrabAttemptRecorded;
            grabManager.OnGrabAttemptRecorded += HandleGrabAttemptRecorded;
        }
    }

    private void UnsubscribeEvents()
    {
        if (studyController != null)
        {
            studyController.OnTrialStarted -= HandleTrialStarted;
            studyController.OnTrialCompleted -= HandleTrialCompleted;
        }

        if (grabManager != null)
        {
            grabManager.OnGrabAttemptRecorded -= HandleGrabAttemptRecorded;
        }
    }

    private void HandleTrialStarted(StudyController.TrialLifecycleEventData data)
    {
        EnsureSessionReady();

        if (_activeTrial != null)
        {
            Debug.LogWarning("[StudyDataCollector] Previous trial context still active. Replacing with latest trial start.");
        }

        _activeTrial = new ActiveTrialContext
        {
            trialData = data,
            trialStartTime = Time.time
        };

        if (verboseLog)
        {
            Debug.Log($"[StudyDataCollector] Trial started: block {data.blockOrderPosition}, trial {data.trialNumber}.");
        }
    }

    private void HandleGrabAttemptRecorded(MyGrabManager.GrabAttemptResult result)
    {
        if (_activeTrial == null)
        {
            return;
        }

        GrabAttemptRecord record = new GrabAttemptRecord
        {
            attemptIndex = _activeTrial.attempts.Count + 1,
            attemptTimeFromTrialStart = Mathf.Max(0f, result.recordedTime - _activeTrial.trialStartTime),
            attemptTimeAbsolute = result.recordedTime,
            outcome = result.outcome
        };

        _activeTrial.attempts.Add(record);
        WriteGrabAttemptRow(_activeTrial, record);
    }

    private void HandleTrialCompleted(StudyController.TrialLifecycleEventData data)
    {
        if (_activeTrial == null)
        {
            return;
        }

        float trialEndTime = Time.time;
        float duration = Mathf.Max(0f, trialEndTime - _activeTrial.trialStartTime);

        int targetGrabCount = 0;
        int distractorGrabCount = 0;
        int noneGrabCount = 0;
        for (int i = 0; i < _activeTrial.attempts.Count; i++)
        {
            MyGrabManager.GrabAttemptOutcome outcome = _activeTrial.attempts[i].outcome;
            if (outcome == MyGrabManager.GrabAttemptOutcome.Target) targetGrabCount++;
            else if (outcome == MyGrabManager.GrabAttemptOutcome.Distractor) distractorGrabCount++;
            else noneGrabCount++;
        }

        float diameter = _activeTrial.trialData.condition.sphereDiameter;
        float minCenterDistance = _activeTrial.trialData.condition.minimumCenterDistance;
        float minCenterDistanceMultiplier = diameter > Mathf.Epsilon ? minCenterDistance / diameter : 0f;

        WriteTrialSummaryRow(
            _activeTrial.trialData,
            _activeTrial.trialStartTime,
            trialEndTime,
            duration,
            minCenterDistanceMultiplier,
            _activeTrial.attempts.Count,
            targetGrabCount,
            distractorGrabCount,
            noneGrabCount);

        if (verboseLog)
        {
            Debug.Log($"[StudyDataCollector] Trial completed: block {_activeTrial.trialData.blockOrderPosition}, trial {_activeTrial.trialData.trialNumber}, duration={duration:0.###}s.");
        }

        _activeTrial = null;
    }

    private void EnsureSessionReady()
    {
        if (_trialSummaryWriter != null && _grabAttemptWriter != null)
        {
            return;
        }

        string root = Application.dataPath; // Assets folder
        string folder = Path.Combine(root, outputSubFolder);
        Directory.CreateDirectory(folder);

        if (string.IsNullOrWhiteSpace(_sessionId))
        {
            _sessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        }

        string safePrefix = BuildFilePrefixFromParticipantId();
        _trialSummaryPath = Path.Combine(folder, $"{safePrefix}_{_sessionId}_trial_summary.csv");
        _grabAttemptPath = Path.Combine(folder, $"{safePrefix}_{_sessionId}_grab_attempts.csv");

        _trialSummaryWriter = CreateCsvWriter(_trialSummaryPath);
        _grabAttemptWriter = CreateCsvWriter(_grabAttemptPath);

        _trialSummaryWriter.WriteLine("session_id,participant_id,block_order_position,total_block_count,trial_number,total_trial_count,hand_scale_factor,detect_radius,sphere_diameter,minimum_center_distance_multiplier,minimum_center_distance,target_distance_region,trial_start_time_s,trial_end_time_s,trial_duration_s,grab_attempt_count,target_grab_count,distractor_grab_count,empty_grab_count");
        _grabAttemptWriter.WriteLine("session_id,participant_id,block_order_position,trial_number,attempt_index,attempt_time_from_trial_start_s,attempt_time_absolute_s,attempt_outcome");
        _trialSummaryWriter.Flush();
        _grabAttemptWriter.Flush();

        Debug.Log($"[StudyDataCollector] Data files created:\n- {_trialSummaryPath}\n- {_grabAttemptPath}");
    }

    private static StreamWriter CreateCsvWriter(string path)
    {
        return new StreamWriter(path, append: false, encoding: new UTF8Encoding(false));
    }

    private string BuildFilePrefixFromParticipantId()
    {
        if (studyController != null && studyController.participantId > 0)
        {
            return $"participant_{studyController.participantId}";
        }

        return string.IsNullOrWhiteSpace(filePrefix) ? "study" : filePrefix.Trim();
    }

    private void CloseWriters()
    {
        if (_trialSummaryWriter != null)
        {
            _trialSummaryWriter.Flush();
            _trialSummaryWriter.Dispose();
            _trialSummaryWriter = null;
        }

        if (_grabAttemptWriter != null)
        {
            _grabAttemptWriter.Flush();
            _grabAttemptWriter.Dispose();
            _grabAttemptWriter = null;
        }
    }

    private void WriteGrabAttemptRow(ActiveTrialContext ctx, GrabAttemptRecord record)
    {
        if (_grabAttemptWriter == null || ctx == null)
        {
            return;
        }

        StudyController.TrialLifecycleEventData d = ctx.trialData;
        _grabAttemptWriter.WriteLine(string.Join(",",
            Csv(_sessionId),
            d.participantId.ToString(CultureInfo.InvariantCulture),
            d.blockOrderPosition.ToString(CultureInfo.InvariantCulture),
            d.trialNumber.ToString(CultureInfo.InvariantCulture),
            record.attemptIndex.ToString(CultureInfo.InvariantCulture),
            F(record.attemptTimeFromTrialStart),
            F(record.attemptTimeAbsolute),
            Csv(record.outcome.ToString())));
        _grabAttemptWriter.Flush();
    }

    private void WriteTrialSummaryRow(
        StudyController.TrialLifecycleEventData d,
        float trialStartTime,
        float trialEndTime,
        float duration,
        float minCenterDistanceMultiplier,
        int grabAttemptCount,
        int targetGrabCount,
        int distractorGrabCount,
        int noneGrabCount)
    {
        if (_trialSummaryWriter == null)
        {
            return;
        }

        _trialSummaryWriter.WriteLine(string.Join(",",
            Csv(_sessionId),
            d.participantId.ToString(CultureInfo.InvariantCulture),
            d.blockOrderPosition.ToString(CultureInfo.InvariantCulture),
            d.totalBlockCount.ToString(CultureInfo.InvariantCulture),
            d.trialNumber.ToString(CultureInfo.InvariantCulture),
            d.totalTrialCount.ToString(CultureInfo.InvariantCulture),
            F(d.condition.handScaleFactor),
            F(d.condition.detectRadius),
            F(d.condition.sphereDiameter),
            F(minCenterDistanceMultiplier),
            F(d.condition.minimumCenterDistance),
            Csv(d.condition.targetDistanceRegion.ToString()),
            F(trialStartTime),
            F(trialEndTime),
            F(duration),
            grabAttemptCount.ToString(CultureInfo.InvariantCulture),
            targetGrabCount.ToString(CultureInfo.InvariantCulture),
            distractorGrabCount.ToString(CultureInfo.InvariantCulture),
            noneGrabCount.ToString(CultureInfo.InvariantCulture)));
        _trialSummaryWriter.Flush();
    }

    private static string F(float v)
    {
        return v.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static string Csv(string value)
    {
        if (value == null) return "\"\"";
        string escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }
}

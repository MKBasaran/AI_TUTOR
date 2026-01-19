using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace ServerCore.Tutor;

/// <summary>
/// Coordinates per-session tutor state: trial ingestion, stuck detection, hint availability, and hint generation.
/// </summary>
public sealed class TutorSessionManager
{
    private const string DefaultLegId = "leg-0";

    private readonly IStuckDetector stuckDetector;
    private readonly HintGenerator hintGenerator;
    private readonly ITrialLogger trialLogger;
    private readonly IStuckReportLogger stuckReportLogger;
    private readonly TutorOptions options;

    private readonly ConcurrentDictionary<string, TutorSessionState> sessions = new(StringComparer.OrdinalIgnoreCase);

    public TutorSessionManager(
        IStuckDetector stuckDetector,
        HintGenerator hintGenerator,
        ITrialLogger trialLogger,
        IStuckReportLogger stuckReportLogger,
        TutorOptions options)
    {
        this.stuckDetector = stuckDetector;
        this.hintGenerator = hintGenerator;
        this.trialLogger = trialLogger;
        this.stuckReportLogger = stuckReportLogger;
        this.options = options;
    }

    public TutorTrialResponse SubmitTrial(TrialSubmission submission)
    {
        ValidateTrialSubmission(submission);

        var session = sessions.GetOrAdd(submission.SessionId, id => new TutorSessionState(id, options.HintBudget));

        TutorTrialResponse response;
        TutorTrialLogEntry logEntry;

        lock (session.SyncRoot)
        {
            var legId = NormalizeLegId(submission.LegId);
            var leg = session.GetLeg(legId);

            var record = CreateTrialRecord(submission, legId);
            AppendTrial(leg, record);

            if (leg.HintLocked && HasMeaningfulParamChangeSinceHint(leg.LastHintParams, record.Params))
            {
                leg.HintLocked = false;
                leg.LastHintParams = null;
            }

            var stuck = EvaluateStuckState(leg);
            leg.LastStuck = stuck;

            var hintMode = GetHintMode();
            var hintBudgetRemaining = GetHintBudgetRemaining(session, leg);
            var hintAvailable = GetHintAvailable(session, leg, stuck);
            var (voteCount, voteThreshold, voteTotal, voted) = GetVoteStatus(session, legId);

            var diagnostics = options.IncludeDiagnostics
                ? new TutorDiagnostics(leg.History.Count, leg.StuckStreak, leg.CurrentTier)
                : null;

            response = new TutorTrialResponse(
                stuck,
                leg.LastHint,
                hintBudgetRemaining,
                hintAvailable,
                diagnostics,
                hintMode,
                voteCount,
                voteThreshold,
                voteTotal,
                voted);

            logEntry = new TutorTrialLogEntry(
                record.Timestamp,
                submission.SessionId,
                record.RunId,
                legId,
                new Dictionary<string, double>(record.Params),
                record.SessionScore,
                record.LimbSpeed,
                record.Safety,
                record.GoalType,
                stuck,
                leg.LastHint,
                hintBudgetRemaining,
                hintMode);
        }

        trialLogger.Log(logEntry);
        return response;
    }

    public TutorStatusResponse GetStatus(string sessionId, string? legId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("session_id is required.");

        legId = NormalizeLegId(legId);

        if (!sessions.TryGetValue(sessionId, out var session))
        {
            var hintMode = GetHintMode();
            var (voteCount, voteThreshold, voteTotal, voted) = IsGlobalHintMode()
                ? (0, ClampVoteThreshold(), ClampVoteTotal(), false)
                : (0, 0, 0, false);

            return new TutorStatusResponse(false, null, options.HintBudget, false, hintMode, voteCount, voteThreshold, voteTotal, voted);
        }

        lock (session.SyncRoot)
        {
            var leg = session.GetLeg(legId);
            var hintAvailable = GetHintAvailable(session, leg, leg.LastStuck);
            var hintBudgetRemaining = GetHintBudgetRemaining(session, leg);
            var (voteCount, voteThreshold, voteTotal, voted) = GetVoteStatus(session, legId);

            return new TutorStatusResponse(
                leg.LastStuck,
                leg.LastHint,
                hintBudgetRemaining,
                hintAvailable,
                GetHintMode(),
                voteCount,
                voteThreshold,
                voteTotal,
                voted);
        }
    }

    public TutorHintResponse RequestHint(string sessionId, string legId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("session_id is required.");

        legId = NormalizeLegId(legId);
        var session = sessions.GetOrAdd(sessionId, id => new TutorSessionState(id, options.HintBudget));

        lock (session.SyncRoot)
        {
            if (IsGlobalHintMode())
                return RequestGlobalHint(session, legId);

            return RequestPerLegHint(session, legId);
        }
    }

    public TutorStuckReportResponse ReportStuck(TutorStuckReportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
            throw new ArgumentException("session_id is required.");

        var session = sessions.GetOrAdd(request.SessionId, id => new TutorSessionState(id, options.HintBudget));

        TutorStuckReportEntry entry;
        TutorStuckReportResponse response;

        lock (session.SyncRoot)
        {
            var legId = NormalizeLegId(request.LegId);
            var leg = session.GetLeg(legId);

            var systemStuck = leg.LastStuck;
            var accepted = !systemStuck;

            var lastRecord = leg.History.Count > 0 ? leg.History[^1] : null;
            var parameters = lastRecord is null
                ? new Dictionary<string, double>()
                : new Dictionary<string, double>(lastRecord.Params);

            entry = new TutorStuckReportEntry(
                request.Timestamp ?? DateTimeOffset.UtcNow,
                request.SessionId,
                legId,
                lastRecord?.RunId,
                parameters,
                lastRecord?.SessionScore ?? 0,
                systemStuck,
                accepted,
                GetHintMode());

            response = new TutorStuckReportResponse(accepted, systemStuck);
        }

        stuckReportLogger.Log(entry);
        return response;
    }

    private TutorHintResponse RequestPerLegHint(TutorSessionState session, string legId)
    {
        var leg = session.GetLeg(legId);
        var hintMode = GetHintMode();

        if (!leg.LastStuck)
            return new TutorHintResponse(false, leg.LastHint, leg.HintBudget, false, hintMode, 0, 0, 0, false);

        if (leg.HintLocked)
            return new TutorHintResponse(true, leg.LastHint, leg.HintBudget, false, hintMode, 0, 0, 0, false);

        if (leg.HintBudget <= 0)
        {
            leg.LastHint = hintGenerator.CreateHint(
                leg.CurrentTier,
                new Dictionary<string, ParameterDirection>(),
                new SafetyFlags(),
                hintLimitReached: true,
                goalType: GoalTypes.Speed);

            return new TutorHintResponse(true, leg.LastHint, leg.HintBudget, false, hintMode, 0, 0, 0, false);
        }

        var (lastSafety, goalType) = GetLastSafetyAndGoal(leg);
        var directions = hintGenerator.SuggestDirections(leg.History, legId);

        leg.LastHint = hintGenerator.CreateHint(leg.CurrentTier, directions, lastSafety, hintLimitReached: false, goalType);
        leg.HintBudget -= 1;
        LockHintUntilChange(leg);

        return new TutorHintResponse(true, leg.LastHint, leg.HintBudget, false, hintMode, 0, 0, 0, false);
    }

    private TutorHintResponse RequestGlobalHint(TutorSessionState session, string legId)
    {
        var leg = session.GetLeg(legId);
        var hintMode = GetHintMode();

        var hintBudgetRemaining = session.GlobalHintBudget;
        var (voteCount, voteThreshold, voteTotal, voted) = GetVoteStatus(session, legId);

        if (!leg.LastStuck)
            return new TutorHintResponse(false, leg.LastHint, hintBudgetRemaining, false, hintMode, voteCount, voteThreshold, voteTotal, voted);

        if (leg.HintLocked)
            return new TutorHintResponse(true, leg.LastHint, hintBudgetRemaining, false, hintMode, voteCount, voteThreshold, voteTotal, voted);

        if (!session.HintVotes.Contains(legId))
        {
            session.HintVotes.Add(legId);
            voteCount = session.HintVotes.Count;
            voted = true;
        }

        if (voteCount < voteThreshold)
            return new TutorHintResponse(true, leg.LastHint, hintBudgetRemaining, false, hintMode, voteCount, voteThreshold, voteTotal, voted);

        if (session.GlobalHintBudget <= 0)
        {
            foreach (var targetLeg in session.Legs.Values)
            {
                targetLeg.LastHint = hintGenerator.CreateHint(
                    targetLeg.CurrentTier,
                    new Dictionary<string, ParameterDirection>(),
                    new SafetyFlags(),
                    hintLimitReached: true,
                    goalType: GoalTypes.Speed);

                targetLeg.HintLocked = true;
                targetLeg.LastHintParams = targetLeg.History.Count > 0 ? new Dictionary<string, double>(targetLeg.History[^1].Params) : null;
            }

            session.HintVotes.Clear();
            return new TutorHintResponse(true, leg.LastHint, 0, false, hintMode, 0, voteThreshold, voteTotal, false);
        }

        foreach (var targetLeg in session.Legs.Values)
        {
            var (lastSafety, goalType) = GetLastSafetyAndGoal(targetLeg);
            var directions = hintGenerator.SuggestDirections(targetLeg.History, targetLeg.LegId);

            targetLeg.LastHint = hintGenerator.CreateHint(targetLeg.CurrentTier, directions, lastSafety, hintLimitReached: false, goalType);
            targetLeg.HintLocked = true;
            targetLeg.LastHintParams = targetLeg.History.Count > 0 ? new Dictionary<string, double>(targetLeg.History[^1].Params) : null;
        }

        session.GlobalHintBudget -= 1;
        hintBudgetRemaining = session.GlobalHintBudget;
        session.HintVotes.Clear();

        return new TutorHintResponse(true, leg.LastHint, hintBudgetRemaining, false, hintMode, 0, voteThreshold, voteTotal, false);
    }

    private static (SafetyFlags Safety, string GoalType) GetLastSafetyAndGoal(LegTutorState leg)
    {
        if (leg.History.Count == 0)
            return (new SafetyFlags(), GoalTypes.Speed);

        var last = leg.History[^1];
        return (last.Safety, last.GoalType);
    }

    private void LockHintUntilChange(LegTutorState leg)
    {
        leg.HintLocked = true;
        leg.LastHintParams = leg.History.Count > 0 ? new Dictionary<string, double>(leg.History[^1].Params) : null;
    }

    private TutorTrialRecord CreateTrialRecord(TrialSubmission submission, string legId)
    {
        var goalType = GoalTypes.Normalize(submission.GoalType);
        var safety = submission.Safety ?? new SafetyFlags();
        var limbSpeed = submission.LimbSpeed ?? 0;

        var sessionScore = submission.SpeedMps ?? OptimalParameters.ScoreFor(legId, submission.Params);
        if (sessionScore < 0)
            sessionScore = 0;

        return new TutorTrialRecord(
            submission.Timestamp,
            submission.RunId,
            legId,
            submission.Params,
            limbSpeed,
            sessionScore,
            safety,
            goalType);
    }

    private void AppendTrial(LegTutorState leg, TutorTrialRecord record)
    {
        leg.History.Add(record);
        if (leg.History.Count > options.HistoryLimit)
            leg.History.RemoveAt(0);
    }

    private bool EvaluateStuckState(LegTutorState leg)
    {
        if (!HasMeaningfulParamChange(leg.History))
        {
            leg.StuckStreak = 0;
            leg.CurrentTier = HintTier.Reflection;
            return false;
        }

        var stuck = stuckDetector.IsStuck(leg.History);
        if (stuck && IsProgressing(leg.History))
            stuck = false;

        UpdateTierAndStreak(leg, stuck);
        return stuck;
    }

    private void UpdateTierAndStreak(LegTutorState leg, bool stuck)
    {
        if (!stuck)
        {
            leg.StuckStreak = 0;
            leg.CurrentTier = HintTier.Reflection;
            return;
        }

        leg.StuckStreak += 1;

        if (leg.StuckStreak >= options.EscalationTrials * 2)
            leg.CurrentTier = HintTier.Pattern;
        else if (leg.StuckStreak >= options.EscalationTrials)
            leg.CurrentTier = HintTier.Micro;
        else
            leg.CurrentTier = HintTier.Reflection;
    }

    private bool IsProgressing(IReadOnlyList<TutorTrialRecord> history)
    {
        if (history.Count < 2)
            return false;

        var last = history[^1].SessionScore;
        var bestBefore = history.Take(history.Count - 1).Max(r => r.SessionScore);
        return last >= bestBefore + options.ProgressThreshold;
    }

    private bool HasMeaningfulParamChange(IReadOnlyList<TutorTrialRecord> history)
    {
        if (history.Count < 2)
            return false;

        var reference = history[0].Params;
        foreach (var parameter in OptimalParameters.ParameterRanges)
        {
            var key = parameter.Key;
            if (!reference.TryGetValue(key, out var referenceValue))
                continue;

            var epsilon = Math.Max(parameter.Value.Max - parameter.Value.Min, 1) * options.ParamDeltaEpsilon;

            foreach (var record in history)
            {
                if (!record.Params.TryGetValue(key, out var current))
                    continue;

                double delta = string.Equals(key, OptimalParameters.RelationKey, StringComparison.OrdinalIgnoreCase)
                    ? Math.Abs(OptimalParameters.ShortestAngleDelta(current, referenceValue))
                    : Math.Abs(current - referenceValue);

                if (delta > epsilon)
                    return true;
            }
        }

        return false;
    }

    private bool HasMeaningfulParamChangeSinceHint(
        IReadOnlyDictionary<string, double>? reference,
        IReadOnlyDictionary<string, double> current)
    {
        if (reference is null || reference.Count == 0)
            return true;

        foreach (var parameter in OptimalParameters.ParameterRanges)
        {
            var key = parameter.Key;
            if (!reference.TryGetValue(key, out var referenceValue))
                continue;
            if (!current.TryGetValue(key, out var currentValue))
                continue;

            var epsilon = Math.Max(parameter.Value.Max - parameter.Value.Min, 1) * options.ParamDeltaEpsilon;

            double delta = string.Equals(key, OptimalParameters.RelationKey, StringComparison.OrdinalIgnoreCase)
                ? Math.Abs(OptimalParameters.ShortestAngleDelta(currentValue, referenceValue))
                : Math.Abs(currentValue - referenceValue);

            if (delta > epsilon)
                return true;
        }

        return false;
    }

    private bool IsGlobalHintMode()
    {
        var mode = options.HintMode?.Trim() ?? string.Empty;
        return mode.Equals("global_majority", StringComparison.OrdinalIgnoreCase)
               || mode.Equals("global", StringComparison.OrdinalIgnoreCase);
    }

    private string GetHintMode()
        => IsGlobalHintMode() ? "global_majority" : "per_leg";

    private int GetHintBudgetRemaining(TutorSessionState session, LegTutorState leg)
        => IsGlobalHintMode() ? session.GlobalHintBudget : leg.HintBudget;

    private bool GetHintAvailable(TutorSessionState session, LegTutorState leg, bool stuck)
    {
        if (!stuck || leg.HintLocked)
            return false;

        if (IsGlobalHintMode())
            return session.GlobalHintBudget > 0 && !session.HintVotes.Contains(leg.LegId);

        return leg.HintBudget > 0;
    }

    private (int VoteCount, int VoteThreshold, int VoteTotal, bool Voted) GetVoteStatus(TutorSessionState session, string legId)
    {
        if (!IsGlobalHintMode())
            return (0, 0, 0, false);

        var voteTotal = ClampVoteTotal();
        var voteThreshold = ClampVoteThreshold(voteTotal);
        var voteCount = session.HintVotes.Count;
        var voted = session.HintVotes.Contains(legId);

        return (voteCount, voteThreshold, voteTotal, voted);
    }

    private int ClampVoteTotal()
    {
        var voteTotal = Math.Max(options.HintVoteTotal, options.HintVoteThreshold);
        return voteTotal > 0 ? voteTotal : 4;
    }

    private int ClampVoteThreshold(int? voteTotal = null)
    {
        var total = voteTotal ?? ClampVoteTotal();
        return Math.Clamp(options.HintVoteThreshold, 1, total);
    }

    private static string NormalizeLegId(string? legId)
    {
        if (string.IsNullOrWhiteSpace(legId))
            return DefaultLegId;

        return legId.Trim().ToLowerInvariant();
    }

    private static void ValidateTrialSubmission(TrialSubmission submission)
    {
        if (string.IsNullOrWhiteSpace(submission.SessionId))
            throw new ArgumentException("session_id is required.");
        if (string.IsNullOrWhiteSpace(submission.RunId))
            throw new ArgumentException("run_id is required.");
        if (submission.Params is null)
            throw new ArgumentException("params is required.");
    }
}


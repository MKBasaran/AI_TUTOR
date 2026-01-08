using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace ServerCore.Tutor;

public sealed class TutorSessionManager
{
    private readonly IStuckDetector stuckDetector;
    private readonly HintGenerator hintGenerator;
    private readonly ITrialLogger trialLogger;
    private readonly TutorOptions options;
    private readonly ConcurrentDictionary<string, TutorSessionState> sessions = new(StringComparer.OrdinalIgnoreCase);

    public TutorSessionManager(
        IStuckDetector stuckDetector,
        HintGenerator hintGenerator,
        ITrialLogger trialLogger,
        TutorOptions options)
    {
        this.stuckDetector = stuckDetector;
        this.hintGenerator = hintGenerator;
        this.trialLogger = trialLogger;
        this.options = options;
    }

    public TutorTrialResponse SubmitTrial(TrialSubmission submission)
    {
        if (string.IsNullOrWhiteSpace(submission.SessionId))
            throw new ArgumentException("session_id is required.");
        if (string.IsNullOrWhiteSpace(submission.RunId))
            throw new ArgumentException("run_id is required.");

        var session = sessions.GetOrAdd(submission.SessionId, id => new TutorSessionState(id, options.HintBudget));
        var legId = NormalizeLegId(submission.LegId);
        var leg = session.GetLeg(legId);

        var goalType = GoalTypes.Normalize(submission.GoalType);
        var safety = submission.Safety ?? new SafetyFlags();
        var limbSpeed = submission.LimbSpeed ?? 0;

        var sessionScore = submission.SpeedMps ?? OptimalParameters.ScoreFor(legId, submission.Params);
        if (sessionScore < 0)
            sessionScore = 0;

        var record = new TutorTrialRecord(
            submission.Timestamp,
            submission.RunId,
            legId,
            submission.Params,
            limbSpeed,
            sessionScore,
            safety,
            goalType);

        leg.History.Add(record);
        if (leg.History.Count > options.HistoryLimit)
            leg.History.RemoveAt(0);

        var hasChanges = HasMeaningfulParamChange(leg.History);

        bool stuck;
        if (!hasChanges)
        {
            stuck = false;
            leg.StuckStreak = 0;
            leg.CurrentTier = HintTier.Reflection;
            leg.HintLocked = false;
        }
        else
        {
            stuck = stuckDetector.IsStuck(leg.History);
            if (stuck && IsProgressing(leg.History))
                stuck = false;

            if (stuck)
            {
                leg.StuckStreak += 1;
                leg.HintLocked = false;
            }
            else
            {
                leg.StuckStreak = 0;
                leg.CurrentTier = HintTier.Reflection;
                leg.HintLocked = false;
            }

            if (stuck)
            {
                if (leg.StuckStreak >= options.EscalationTrials * 2)
                    leg.CurrentTier = HintTier.Pattern;
                else if (leg.StuckStreak >= options.EscalationTrials)
                    leg.CurrentTier = HintTier.Micro;
                else
                    leg.CurrentTier = HintTier.Reflection;
            }
        }

        leg.LastStuck = stuck;

        var hintAvailable = stuck && leg.HintBudget > 0 && !leg.HintLocked;

        var diagnostics = options.IncludeDiagnostics
            ? new TutorDiagnostics(leg.History.Count, leg.StuckStreak, leg.CurrentTier)
            : null;

        var response = new TutorTrialResponse(
            stuck,
            leg.LastHint,
            leg.HintBudget,
            hintAvailable,
            diagnostics);

        trialLogger.Log(new TutorTrialLogEntry(
            submission.Timestamp,
            submission.SessionId,
            submission.RunId,
            legId,
            submission.Params,
            sessionScore,
            limbSpeed,
            safety,
            goalType,
            stuck,
            leg.LastHint,
            leg.HintBudget));

        return response;
    }

    public TutorStatusResponse GetStatus(string sessionId, string? legId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("session_id is required.");

        legId = NormalizeLegId(legId);

        if (!sessions.TryGetValue(sessionId, out var session))
        {
            return new TutorStatusResponse(false, null, options.HintBudget, false);
        }

        var leg = session.GetLeg(legId);
        var hintAvailable = leg.LastStuck && leg.HintBudget > 0 && !leg.HintLocked;
        return new TutorStatusResponse(leg.LastStuck, leg.LastHint, leg.HintBudget, hintAvailable);
    }

    public TutorHintResponse RequestHint(string sessionId, string legId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("session_id is required.");

        legId = NormalizeLegId(legId);
        var session = sessions.GetOrAdd(sessionId, id => new TutorSessionState(id, options.HintBudget));
        var leg = session.GetLeg(legId);

        if (!leg.LastStuck)
            return new TutorHintResponse(false, leg.LastHint, leg.HintBudget, false);

        if (leg.HintLocked)
            return new TutorHintResponse(true, leg.LastHint, leg.HintBudget, false);

        if (leg.HintBudget <= 0)
        {
            var limitHint = hintGenerator.CreateHint(leg.CurrentTier, new Dictionary<string, ParameterDirection>(),
                new SafetyFlags(), true, GoalTypes.Speed);
            leg.LastHint = limitHint;
            return new TutorHintResponse(true, leg.LastHint, leg.HintBudget, false);
        }

        var directions = hintGenerator.SuggestDirections(leg.History, legId);
        var lastSafety = leg.History.Count > 0 ? leg.History[^1].Safety : new SafetyFlags();
        var goalType = leg.History.Count > 0 ? leg.History[^1].GoalType : GoalTypes.Speed;

        var hint = hintGenerator.CreateHint(leg.CurrentTier, directions, lastSafety, false, goalType);
        leg.LastHint = hint;
        leg.HintBudget -= 1;
        leg.HintLocked = true;

        return new TutorHintResponse(true, hint, leg.HintBudget, false);
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

                double delta = string.Equals(key, "relation", StringComparison.OrdinalIgnoreCase)
                    ? Math.Abs(OptimalParameters.ShortestAngleDelta(current, referenceValue))
                    : Math.Abs(current - referenceValue);

                if (delta > epsilon)
                    return true;
            }
        }

        return false;
    }

    private static string NormalizeLegId(string? legId)
    {
        if (string.IsNullOrWhiteSpace(legId))
            return "leg-0";

        return legId.Trim().ToLowerInvariant();
    }
}
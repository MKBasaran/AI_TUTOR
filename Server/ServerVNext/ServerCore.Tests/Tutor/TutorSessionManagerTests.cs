using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ServerCore.Tutor;

namespace ServerCore.Tests.Tutor;

[TestClass]
public sealed class TutorSessionManagerTests
{
    [TestMethod]
    public void EscalatesAndResetsHintTiers()
    {
        var stuckDetector = new SequenceStuckDetector(true, true, true, true, false);
        var options = new TutorOptions
        {
            EscalationTrials = 2,
            HintBudget = 4,
            ProgressThreshold = 0.1,
            ParamDeltaEpsilon = 0
        };

        var manager = new TutorSessionManager(
            stuckDetector,
            new HintGenerator(options),
            new NoopTrialLogger(),
            new NoopStuckReportLogger(),
            options);

        const string sessionId = "session-a";
        const string legId = "leg-1";

        manager.SubmitTrial(MakeSubmission(sessionId, legId, "run-1", baseline: 90));
        manager.SubmitTrial(MakeSubmission(sessionId, legId, "run-2", baseline: 91));
        manager.SubmitTrial(MakeSubmission(sessionId, legId, "run-3", baseline: 92));

        var microHint = manager.RequestHint(sessionId, legId);
        Assert.AreEqual(HintTier.Micro, microHint.Hint?.Tier);

        manager.SubmitTrial(MakeSubmission(sessionId, legId, "run-4", baseline: 93));
        manager.SubmitTrial(MakeSubmission(sessionId, legId, "run-5", baseline: 94));

        var patternHint = manager.RequestHint(sessionId, legId);
        Assert.AreEqual(HintTier.Pattern, patternHint.Hint?.Tier);

        manager.SubmitTrial(MakeSubmission(sessionId, legId, "run-6", baseline: 95));
        var status = manager.GetStatus(sessionId, legId);
        Assert.IsFalse(status.Stuck);
    }

    [TestMethod]
    public void HintLockPreventsBackToBackRequests()
    {
        var stuckDetector = new SequenceStuckDetector(true, true);
        var options = new TutorOptions
        {
            EscalationTrials = 2,
            HintBudget = 2,
            ProgressThreshold = 0.1,
            ParamDeltaEpsilon = 0
        };

        var manager = new TutorSessionManager(
            stuckDetector,
            new HintGenerator(options),
            new NoopTrialLogger(),
            new NoopStuckReportLogger(),
            options);

        const string sessionId = "session-b";
        const string legId = "leg-0";

        manager.SubmitTrial(MakeSubmission(sessionId, legId, "run-1", baseline: 90));
        manager.SubmitTrial(MakeSubmission(sessionId, legId, "run-2", baseline: 91));

        var first = manager.RequestHint(sessionId, legId);
        var second = manager.RequestHint(sessionId, legId);

        Assert.AreEqual(first.HintBudgetRemaining, second.HintBudgetRemaining);
        Assert.AreEqual(first.Hint?.Text, second.Hint?.Text);
    }

    [TestMethod]
    public void ReflectionHintsHideDirections()
    {
        var stuckDetector = new SequenceStuckDetector(true);
        var options = new TutorOptions
        {
            EscalationTrials = 10,
            HintBudget = 1,
            ProgressThreshold = 0.1,
            ParamDeltaEpsilon = 0
        };

        var manager = new TutorSessionManager(
            stuckDetector,
            new HintGenerator(options),
            new NoopTrialLogger(),
            new NoopStuckReportLogger(),
            options);

        const string sessionId = "session-c";
        const string legId = "leg-2";

        manager.SubmitTrial(MakeSubmission(sessionId, legId, "run-1", baseline: 90));
        manager.SubmitTrial(MakeSubmission(sessionId, legId, "run-2", baseline: 91));

        var hint = manager.RequestHint(sessionId, legId);

        Assert.AreEqual(HintTier.Reflection, hint.Hint?.Tier);
        Assert.AreEqual(0, hint.Hint?.ParameterDirections.Count);
    }

    [TestMethod]
    public void HintTextContainsNoNumbers()
    {
        var options = new TutorOptions { ParamDeltaEpsilon = 0 };
        var generator = new HintGenerator(options);

        var history = new List<TutorTrialRecord>
        {
            new(DateTimeOffset.UtcNow, "run-1", "leg-0",
                new Dictionary<string, double> { ["speed"] = 1, ["range"] = 0, ["baseline_position"] = 90, ["relation"] = 0 },
                LimbSpeed: 0,
                SessionScore: 0.5,
                Safety: new SafetyFlags(),
                GoalType: GoalTypes.Speed)
        };

        var directions = generator.SuggestDirections(history, "leg-0");
        var hint = generator.CreateHint(HintTier.Micro, directions, new SafetyFlags(), hintLimitReached: false, GoalTypes.Speed);

        Assert.IsFalse(Regex.IsMatch(hint.Text, @"\d"));
    }

    private static TrialSubmission MakeSubmission(string sessionId, string legId, string runId, double baseline)
    {
        return new TrialSubmission(
            sessionId,
            runId,
            DateTimeOffset.UtcNow,
            new Dictionary<string, double>
            {
                ["speed"] = 0,
                ["range"] = 0,
                ["baseline_position"] = baseline,
                ["relation"] = 0
            },
            SpeedMps: null,
            LimbSpeed: 0,
            LegId: legId,
            Safety: null,
            GoalType: GoalTypes.Speed);
    }

    private sealed class NoopTrialLogger : ITrialLogger
    {
        public void Log(TutorTrialLogEntry entry)
        {
        }
    }

    private sealed class NoopStuckReportLogger : IStuckReportLogger
    {
        public void Log(TutorStuckReportEntry entry)
        {
        }
    }

    private sealed class SequenceStuckDetector : IStuckDetector
    {
        private readonly Queue<bool> sequence;
        private bool last;

        public SequenceStuckDetector(params bool[] values)
        {
            sequence = new Queue<bool>(values);
            if (values.Length > 0)
                last = values[^1];
        }

        public bool IsStuck(IReadOnlyList<TutorTrialRecord> history)
        {
            if (sequence.Count == 0)
                return last;

            last = sequence.Dequeue();
            return last;
        }
    }
}




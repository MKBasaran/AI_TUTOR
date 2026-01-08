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
            ProgressThreshold = 0
        };

        var manager = new TutorSessionManager(stuckDetector, new HintGenerator(options), new NoopTrialLogger(), options);
        var sessionId = "session-a";
        var legId = "leg-1";

        manager.SubmitTrial(MakeSubmission(sessionId, legId, "run-1"));
        manager.SubmitTrial(MakeSubmission(sessionId, legId, "run-2"));

        var microHint = manager.RequestHint(sessionId, legId);
        Assert.AreEqual(HintTier.Micro, microHint.Hint?.Tier);

        manager.SubmitTrial(MakeSubmission(sessionId, legId, "run-3"));
        manager.SubmitTrial(MakeSubmission(sessionId, legId, "run-4"));

        var patternHint = manager.RequestHint(sessionId, legId);
        Assert.AreEqual(HintTier.Pattern, patternHint.Hint?.Tier);

        manager.SubmitTrial(MakeSubmission(sessionId, legId, "run-5"));
        var status = manager.GetStatus(sessionId, legId);
        Assert.IsFalse(status.Stuck);
    }

    [TestMethod]
    public void HintLockPreventsBackToBackRequests()
    {
        var stuckDetector = new SequenceStuckDetector(true, true, true);
        var options = new TutorOptions
        {
            EscalationTrials = 2,
            HintBudget = 2,
            ProgressThreshold = 0
        };

        var manager = new TutorSessionManager(stuckDetector, new HintGenerator(options), new NoopTrialLogger(), options);
        var sessionId = "session-b";
        var legId = "leg-0";

        manager.SubmitTrial(MakeSubmission(sessionId, legId, "run-1"));
        var first = manager.RequestHint(sessionId, legId);
        var second = manager.RequestHint(sessionId, legId);

        Assert.AreEqual(first.HintBudgetRemaining, second.HintBudgetRemaining);
        Assert.AreEqual(first.Hint?.Text, second.Hint?.Text);
    }

    [TestMethod]
    public void ReflectionHintsHideDirections()
    {
        var stuckDetector = new SequenceStuckDetector(true, true, true);
        var options = new TutorOptions
        {
            EscalationTrials = 10,
            HintBudget = 1,
            ProgressThreshold = 0
        };

        var manager = new TutorSessionManager(stuckDetector, new HintGenerator(options), new NoopTrialLogger(), options);
        var sessionId = "session-c";
        var legId = "leg-2";

        manager.SubmitTrial(MakeSubmission(sessionId, legId, "run-1"));
        var hint = manager.RequestHint(sessionId, legId);

        Assert.AreEqual(HintTier.Reflection, hint.Hint?.Tier);
        Assert.AreEqual(0, hint.Hint?.ParameterDirections.Count);
    }

    [TestMethod]
    public void HintTextContainsNoNumbers()
    {
        var options = new TutorOptions();
        var generator = new HintGenerator(options);
        var history = new List<TutorTrialRecord>
        {
            new(DateTimeOffset.UtcNow, "run-1", "leg-0",
                new Dictionary<string, double> { ["speed"] = 1 }, 0, 0.5, new SafetyFlags(), GoalTypes.Speed)
        };

        var directions = generator.SuggestDirections(history, "leg-0");
        var hint = generator.CreateHint(HintTier.Micro, directions, new SafetyFlags(), false, GoalTypes.Speed);

        Assert.IsFalse(Regex.IsMatch(hint.Text, @"\d"));
    }

    private static TrialSubmission MakeSubmission(string sessionId, string legId, string runId)
    {
        return new TrialSubmission(
            sessionId,
            runId,
            DateTimeOffset.UtcNow,
            new Dictionary<string, double>
            {
                ["speed"] = 0,
                ["range"] = 0,
                ["baseline_position"] = 90,
                ["relation"] = 0
            },
            null,
            0,
            legId,
            null,
            GoalTypes.Speed);
    }

    private sealed class NoopTrialLogger : ITrialLogger
    {
        public void Log(TutorTrialLogEntry entry)
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
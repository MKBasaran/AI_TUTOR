using System;
using System.Collections.Generic;

namespace ServerCore.Tutor;

public sealed class TutorSessionState
{
    public TutorSessionState(string sessionId, int hintBudget)
    {
        SessionId = sessionId;
        DefaultHintBudget = hintBudget;
    }

    public string SessionId { get; }
    public int DefaultHintBudget { get; }
    public Dictionary<string, LegTutorState> Legs { get; } = new(StringComparer.OrdinalIgnoreCase);

    public LegTutorState GetLeg(string legId)
    {
        if (!Legs.TryGetValue(legId, out var leg))
        {
            leg = new LegTutorState(legId, DefaultHintBudget);
            Legs[legId] = leg;
        }

        return leg;
    }
}

public sealed class LegTutorState
{
    public LegTutorState(string legId, int hintBudget)
    {
        LegId = legId;
        HintBudget = hintBudget;
    }

    public string LegId { get; }
    public List<TutorTrialRecord> History { get; } = new();
    public HintTier CurrentTier { get; set; } = HintTier.Reflection;
    public int StuckStreak { get; set; }
    public int HintBudget { get; set; }
    public bool HintLocked { get; set; }
    public HintPayload? LastHint { get; set; }
    public bool LastStuck { get; set; }
}
using System;
using System.Collections.Generic;

namespace ServerCore.Tutor;

/// <summary>
/// In-memory tutor state for a single session, including per-leg state and global hint votes/budget.
/// </summary>
public sealed class TutorSessionState
{
    public TutorSessionState(string sessionId, int hintBudget)
    {
        SessionId = sessionId;
        DefaultHintBudget = hintBudget;
        GlobalHintBudget = hintBudget;
    }

    internal object SyncRoot { get; } = new();

    public string SessionId { get; }
    public int DefaultHintBudget { get; }
    public int GlobalHintBudget { get; set; }

    /// <summary>
    /// Set of leg identifiers that have voted for a hint in global-majority mode.
    /// </summary>
    public HashSet<string> HintVotes { get; } = new(StringComparer.OrdinalIgnoreCase);

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

/// <summary>
/// In-memory tutor state for a single leg.
/// </summary>
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

    /// <summary>
    /// When true, the leg must make a meaningful parameter change before requesting another hint.
    /// </summary>
    public bool HintLocked { get; set; }

    public Dictionary<string, double>? LastHintParams { get; set; }
    public HintPayload? LastHint { get; set; }
    public bool LastStuck { get; set; }
}

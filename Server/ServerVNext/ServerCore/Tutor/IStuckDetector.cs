using System.Collections.Generic;

namespace ServerCore.Tutor;

/// <summary>
/// Determines whether a sequence of recent trials represents a "stuck" state.
/// </summary>
public interface IStuckDetector
{
    /// <summary>
    /// Returns true when the provided history indicates the session is stuck.
    /// </summary>
    bool IsStuck(IReadOnlyList<TutorTrialRecord> history);
}

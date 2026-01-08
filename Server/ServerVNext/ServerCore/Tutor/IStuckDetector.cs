using System.Collections.Generic;

namespace ServerCore.Tutor;

public interface IStuckDetector
{
    bool IsStuck(IReadOnlyList<TutorTrialRecord> history);
}
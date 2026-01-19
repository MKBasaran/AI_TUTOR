namespace ServerCore.Tutor;

/// <summary>
/// Persists tutor trial events for later analysis.
/// </summary>
public interface ITrialLogger
{
    /// <summary>Logs a single trial event.</summary>
    void Log(TutorTrialLogEntry entry);
}

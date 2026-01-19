namespace ServerCore.Tutor;

/// <summary>
/// Persists student-reported stuck signals for later analysis.
/// </summary>
public interface IStuckReportLogger
{
    /// <summary>Logs a single stuck report entry.</summary>
    void Log(TutorStuckReportEntry entry);
}

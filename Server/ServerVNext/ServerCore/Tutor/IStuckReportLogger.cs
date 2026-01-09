namespace ServerCore.Tutor;

public interface IStuckReportLogger
{
    void Log(TutorStuckReportEntry entry);
}

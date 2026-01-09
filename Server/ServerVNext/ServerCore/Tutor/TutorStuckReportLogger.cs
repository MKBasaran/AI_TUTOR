using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using ServerCore.Logging;

namespace ServerCore.Tutor;

public sealed class TutorStuckReportLogger : IStuckReportLogger, IDisposable
{
    private readonly StreamWriter writer;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public TutorStuckReportLogger()
    {
        Directory.CreateDirectory(StandardLogs.DEFAULT_LOG_DIRECTORY.FullName);
        var logPath = Path.Combine(StandardLogs.DEFAULT_LOG_DIRECTORY.FullName, "tutor_stuck_reports.jsonl");
        writer = new StreamWriter(logPath, append: true)
        {
            AutoFlush = true
        };
    }

    public void Log(TutorStuckReportEntry entry)
    {
        writeLock.Wait();
        try
        {
            var json = JsonSerializer.Serialize(entry, jsonOptions);
            writer.WriteLine(json);
        }
        finally
        {
            writeLock.Release();
        }
    }

    public void Dispose()
    {
        writer.Dispose();
        writeLock.Dispose();
    }
}

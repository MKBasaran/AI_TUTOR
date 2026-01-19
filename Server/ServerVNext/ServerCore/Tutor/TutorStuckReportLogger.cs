using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServerCore.Tutor;

/// <summary>
/// JSONL logger for student-reported stuck signals.
/// </summary>
public sealed class TutorStuckReportLogger : IStuckReportLogger, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ConcurrentDictionary<string, StreamWriter> writers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object writeLock = new();

    public void Log(TutorStuckReportEntry entry)
    {
        var logPath = TutorLogPaths.ResolveSessionLogPath(entry.SessionId, entry.Timestamp, "tutor_stuck_reports.jsonl");
        var writer = writers.GetOrAdd(logPath, TutorLogPaths.CreateJsonlWriter);

        lock (writeLock)
        {
            var json = JsonSerializer.Serialize(entry, JsonOptions);
            writer.WriteLine(json);
        }
    }

    public void Dispose()
    {
        foreach (var writer in writers.Values)
            writer.Dispose();
    }
}

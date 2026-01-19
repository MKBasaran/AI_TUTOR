using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServerCore.Tutor;

/// <summary>
/// JSONL logger for tutor trial events.
/// </summary>
public sealed class TutorTrialLogger : ITrialLogger, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ConcurrentDictionary<string, StreamWriter> writers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object writeLock = new();

    public void Log(TutorTrialLogEntry entry)
    {
        var logPath = TutorLogPaths.ResolveSessionLogPath(entry.SessionId, entry.Timestamp, "tutor_trials.jsonl");
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

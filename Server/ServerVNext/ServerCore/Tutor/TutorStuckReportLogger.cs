using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using ServerCore.Logging;

namespace ServerCore.Tutor;

public sealed class TutorStuckReportLogger : IStuckReportLogger, IDisposable
{
    private readonly ConcurrentDictionary<string, StreamWriter> writers = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public void Log(TutorStuckReportEntry entry)
    {
        var logPath = ResolveLogPath(entry.SessionId, entry.Timestamp, "tutor_stuck_reports.jsonl");
        var writer = writers.GetOrAdd(logPath, CreateWriter);

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
        foreach (var writer in writers.Values)
            writer.Dispose();

        writeLock.Dispose();
    }

    private static StreamWriter CreateWriter(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        return new StreamWriter(path, append: true)
        {
            AutoFlush = true
        };
    }

    private static string ResolveLogPath(string sessionId, DateTimeOffset timestamp, string fileName)
    {
        var safeSession = SanitizeSegment(sessionId);
        var dateSegment = timestamp.ToString("yyyyMMdd");
        return Path.Combine(StandardLogs.DEFAULT_LOG_DIRECTORY.FullName, "Sessions", dateSegment, safeSession, fileName);
    }

    private static string SanitizeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "session";

        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
                chars[i] = '_';
        }

        return new string(chars);
    }
}

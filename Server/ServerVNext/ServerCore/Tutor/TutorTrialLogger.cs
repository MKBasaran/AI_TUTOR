using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using ServerCore.Logging;

namespace ServerCore.Tutor;

public sealed class TutorTrialLogger : ITrialLogger, IDisposable
{
    private readonly StreamWriter writer;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public TutorTrialLogger()
    {
        Directory.CreateDirectory(StandardLogs.DEFAULT_LOG_DIRECTORY.FullName);
        var logPath = Path.Combine(StandardLogs.DEFAULT_LOG_DIRECTORY.FullName, "tutor_trials.jsonl");
        writer = new StreamWriter(logPath, append: true)
        {
            AutoFlush = true
        };
    }

    public void Log(TutorTrialLogEntry entry)
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
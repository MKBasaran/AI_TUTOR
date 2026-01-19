using System;
using System.IO;
using ServerCore.Logging;

namespace ServerCore.Tutor;

internal static class TutorLogPaths
{
    public static string ResolveSessionLogPath(string sessionId, DateTimeOffset timestamp, string fileName)
    {
        var safeSession = SanitizePathSegment(sessionId);
        var dateSegment = timestamp.ToString("yyyyMMdd");
        return Path.Combine(StandardLogs.DEFAULT_LOG_DIRECTORY.FullName, "Sessions", dateSegment, safeSession, fileName);
    }

    public static StreamWriter CreateJsonlWriter(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        return new StreamWriter(path, append: true)
        {
            AutoFlush = true
        };
    }

    private static string SanitizePathSegment(string value)
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

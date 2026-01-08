using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


namespace ServerCore.Logging;

/// <summary>
/// <inheritdoc/>
/// <br/>
/// This logger is backed by a file.
/// </summary>
/// <remarks>
/// The stream used is synchronised, and therefore threadsafe.
/// </remarks>
public sealed class FileLogger : ILogger
{
    private static readonly Regex invalid_char_regex =
        new($"[{new string(Path.GetInvalidFileNameChars())}]", RegexOptions.Compiled);

    private readonly TextWriter fileStream;
    private readonly DateTime loggerStartTime;

    private readonly bool useRelativeTime;

    /// <summary>
    /// <inheritdoc cref="FileLogger"/>
    /// </summary>
    /// <param name="logFilePath">The path to the file</param>
    /// <param name="useRelativeTime">If <c>true</c>, the timestamps used during logging will be relative to the time the logger is created.</param>
    public FileLogger(FileInfo logFilePath, bool useRelativeTime = false)
    {
        if (logFilePath.Directory is { Exists: false })
            logFilePath.Directory.Create();

        fileStream = TextWriter.Synchronized(new StreamWriter(File.OpenWrite(logFilePath.ToString())));
        loggerStartTime = DateTime.Now;
        this.useRelativeTime = useRelativeTime;
    }

    private static string sanitiseFileName(string fileName)
        => invalid_char_regex.Replace(fileName, "_");

    /// <inheritdoc/>
    public void Log<T>(T message, LogLevel logLevel = LogLevel.Info)
    {
        string logLinePrefix = logLevel switch
        {
            LogLevel.Write => $"{timeStamp()}",
            _ => $"{timeStamp()} [{logLevel:G}]"
        };

        fileStream?.WriteLine($"{logLinePrefix} {message}");
        fileStream?.Flush();
    }

    /// <inheritdoc/>
    public async Task LogAsync<T>(T message, LogLevel logLevel = LogLevel.Info)
    {
        string logLinePrefix = logLevel switch
        {
            LogLevel.Write => $"{timeStamp()}",
            _ => $"{timeStamp()} [{logLevel:G}]"
        };


        await fileStream.WriteLineAsync($"{logLinePrefix} {message}");
        await fileStream.FlushAsync();
    }

    private string timeStamp()
        => useRelativeTime
            ? (DateTime.Now - loggerStartTime).ToString("c", CultureInfo.InvariantCulture)
            : DateTime.Now.ToString("u", CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public void Dispose()
    {
        fileStream?.Close();
        fileStream?.Dispose();
    }
}

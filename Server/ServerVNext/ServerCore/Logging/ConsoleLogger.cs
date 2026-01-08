using System;
using System.Globalization;
using System.Threading.Tasks;

namespace ServerCore.Logging;

/// <summary>
/// <inheritdoc/>
/// <br/>
/// This logger is backed by the standard output stream (typically a console window).
/// </summary>
public sealed class ConsoleLogger : ILogger
{
    /// <summary>
    /// The identifier on this logger, typically describing the purpose/description of the thing being logged.
    /// </summary>
    public string Identifier { get; }

    /// <summary>
    /// <inheritdoc cref="ConsoleLogger"/>
    /// </summary>
    /// <param name="identifier">
    ///  The identifier on this logger, typically describing the purpose/description of the thing being logged.
    /// </param>
    public ConsoleLogger(string identifier)
    {
        Identifier = identifier;
    }

    /// <inheritdoc/>
    public void Log<T>(T message, LogLevel logLevel = LogLevel.Info)
    {
        string timestamp = DateTime.Now.ToString("u", CultureInfo.InvariantCulture);

        string logLinePrefix = logLevel switch
        {
            LogLevel.Write => timestamp,
            _ => $"{timestamp} [{logLevel:G}]"
        };


        Console.WriteLine($"{logLinePrefix} {{{Identifier}}} {message}");
    }

    /// <inheritdoc/>
    public async Task LogAsync<T>(T message, LogLevel logLevel = LogLevel.Info)
    {
        string timestamp = DateTime.Now.ToString("u", CultureInfo.InvariantCulture);

        string logLinePrefix = logLevel switch
        {
            LogLevel.Write => timestamp,
            _ => $"{timestamp} [{logLevel:G}]"
        };

        await Console.Out.WriteLineAsync($"{logLinePrefix} {{{Identifier}}} {message}");
        await Console.Out.FlushAsync();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// This doesn't dispose the underlying output stream, since that is shared between all <see cref="ConsoleLogger"/>s
    /// </remarks>
    public void Dispose() { }
}

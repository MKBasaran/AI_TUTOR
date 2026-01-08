using System.Threading.Tasks;

namespace ServerCore.Logging;

/// <summary>
/// <inheritdoc cref="CompositeLogger{T}"/>
/// </summary>
public sealed class CompositeLogger : CompositeLogger<ILogger>;

/// <summary>
/// <inheritdoc/>
/// <br/>
/// This logger is a wrapper that can hold multiple <see cref="ILogger"/> implementations. Used to transparently log to multiple destinations.
/// </summary>
/// <typeparam name="TLogger">The specialised logger type</typeparam>
public class CompositeLogger<TLogger> : ILogger where TLogger : ILogger
{
    /// <summary>
    /// The loggers that are part of this composite
    /// </summary>
    public required TLogger[] Loggers { get; init; }

    /// <inheritdoc/>
    public void Log<T>(T message, LogLevel logLevel = LogLevel.Info)
    {
        foreach (var logger in Loggers)
            logger.Log(message, logLevel);
    }

    /// <inheritdoc/>
    public async Task LogAsync<T>(T message, LogLevel logLevel = LogLevel.Info)
    {
        foreach (var logger in Loggers)
            await logger.LogAsync(message, logLevel);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var logger in Loggers)
            logger.Dispose();
    }
}

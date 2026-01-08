using System;
using System.Threading.Tasks;

namespace ServerCore.Logging;

/// <summary>
/// Represents an object that provides logging related functionality.
/// </summary>
public interface ILogger : IDisposable
{
    /// <summary>
    /// Log a message to the logging destination synchronously.
    /// </summary>
    /// <param name="message">The object to be logged</param>
    /// <param name="logLevel">The important/severity of this message</param>
    /// <typeparam name="T">A type that exposes <see cref="object.ToString"/> functionality.</typeparam>
    /// <seealso cref="LogLevel"/>
    void Log<T>(T message, LogLevel logLevel = LogLevel.Info);

    /// <summary>
    /// Log a message to the logging destination asynchronously.
    /// </summary>
    /// <param name="message">The object to be logged</param>
    /// <param name="logLevel">The important/severity of this message</param>
    /// <typeparam name="T">A type that exposes <see cref="object.ToString"/> functionality.</typeparam>
    /// <seealso cref="LogLevel"/>
    Task LogAsync<T>(T message, LogLevel logLevel = LogLevel.Info);
}

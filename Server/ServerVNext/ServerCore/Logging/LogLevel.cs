namespace ServerCore.Logging;

/// <summary>
/// Represents the importance and severity of a log entry.
/// </summary>
public enum LogLevel
{
    /// <summary>
    /// Just a regular message. Similar to <see cref="System.Console.WriteLine()"/>
    /// </summary>
    Write,

    /// <summary>
    /// An informational message.
    /// </summary>
    Info,

    /// <summary>
    /// A warning of improper/unexpected execution.
    /// </summary>
    Warning,

    /// <summary>
    /// A problem has occurred, but is recoverable.
    /// </summary>
    Error,

    /// <summary>
    /// A problem has occured that prevents further functioning of the program.
    /// </summary>
    Fatal,

    /// <summary>
    /// Additional debug information, useful for diagnostics.
    /// </summary>
    Debug,

    /// <summary>
    /// Even more information, used to analyse the flow of the program.
    /// </summary>
    Trace,
}

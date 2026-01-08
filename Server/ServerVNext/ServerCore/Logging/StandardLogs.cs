using System;
using System.IO;

namespace ServerCore.Logging;

/// <summary>
/// Provides standardised logging related facilities.
/// </summary>
public static class StandardLogs
{
    /// <summary>
    /// The standard logging directory for this program execution, best used as the root of all information dumps.
    /// </summary>
    public static readonly DirectoryInfo DEFAULT_LOG_DIRECTORY;

    /// <summary>
    /// Standard runtime logs. This should be used for general logging that can be associated with the program execution.
    /// </summary>
    public static readonly ILogger RUNTIME_LOGGER;

    static StandardLogs()
    {
        var dateTime = DateTime.Now;

        DEFAULT_LOG_DIRECTORY =
            new DirectoryInfo($"{AppContext.BaseDirectory}/Logs/{dateTime:yyyyMMdd_HHmmss}");


        RUNTIME_LOGGER = new CompositeLogger
        {
            Loggers =
            [
                new FileLogger(new FileInfo($"{DEFAULT_LOG_DIRECTORY}/runtime.log")),
                new ConsoleLogger("runtime")
            ]
        };
    }
}

# Logging utilities

ServerCore makes extensive use of logging to better provide diagnostics of its behaviour. EDMO study sessions also rely on extensive logging facilities.

In order to facilitate these uses, while staying minimal and lightweight. Logging utilities are provided out-of-the-box in the ServerCore library.

## ILogger
This is the fundamental interface that defines a _write-only_ logging stream.

It is simple, declaring both synchronous and asynchronous log methods. Each of these methods also accepts a `LogLevel` parameter indicating the type of log line it is.

Logging classes are expected to implement this interface, and consumers of logging classes should accept _any_ types implementing `ILogger`.

## Console logger
A pass-through logger that doesn't persist log lines to disk, simply outputting to the console. 

Each console logger is given an identifier describing the context.

The log lines are timestamped, and context for the line is provided via the `Identifier` property passed during construction.

Sample output:
```
2025-08-21 11:53:26Z [INFO] {Runtime} Initialising Dotnet plugin loader.
```

## File logger
A logger that stores log lines to disk.

The file path to be written is provided upon construction of the logger. The file logger can write times relative to file creation time as well, via a boolean parameter in the constructor.

The log lines are timestamped, with the log level specified. 

The logger instance is implicitly threadsafe via [`TextWriter.Synchronized`](https://learn.microsoft.com/en-us/dotnet/api/system.io.textwriter.synchronized?view=net-9.0)


Sample output (runtime.log):
```
2025-08-21 11:53:26Z [INFO] Initialising Dotnet plugin loader.
```

## Composite logger
A logger that holds one or more loggers. This is used to log to multiple sinks simultaneously.

This logger simply passes logging messages to every child logger.
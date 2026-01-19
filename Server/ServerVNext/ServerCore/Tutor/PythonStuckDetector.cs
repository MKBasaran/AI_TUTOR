using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Python.Runtime;
using ServerCore.Logging;

namespace ServerCore.Tutor;

/// <summary>
/// Bridges the Python <c>stuck_detector.py</c> module into the server via pythonnet.
/// </summary>
public sealed class PythonStuckDetector : IStuckDetector, IDisposable
{
    private readonly string stuckDetectorPath;
    private readonly int window;

    private readonly object initLock = new();
    private PyObject? detector;
    private bool initialised;

    /// <summary>
    /// Creates a stuck detector backed by the Python implementation at <paramref name="stuckDetectorPath"/>.
    /// </summary>
    public PythonStuckDetector(string stuckDetectorPath, int window)
    {
        this.stuckDetectorPath = stuckDetectorPath;
        this.window = window;
    }

    /// <inheritdoc />
    public bool IsStuck(IReadOnlyList<TutorTrialRecord> history)
    {
        if (history.Count < window)
            return false;

        EnsureInitialised();

        using var gil = Py.GIL();
        using var pyList = new PyList();

        foreach (var record in history)
        {
            using var run = new PyDict();
            run.SetItem("speed_mps", record.SessionScore.ToPython());

            using var paramDict = new PyDict();
            foreach (var param in record.Params)
                paramDict.SetItem(param.Key, param.Value.ToPython());

            run.SetItem("params", paramDict);

            // The Python validator expects explicit booleans for safety flags.
            run.SetItem("overcurrent", record.Safety.Overcurrent.ToPython());
            run.SetItem("overtemp", record.Safety.Overtemp.ToPython());
            run.SetItem("timeout", record.Safety.Timeout.ToPython());

            pyList.Append(run);
        }

        try
        {
            dynamic result = detector!.InvokeMethod("detect_stuck", pyList);
            return (bool)result;
        }
        catch (PythonException ex)
        {
            StandardLogs.RUNTIME_LOGGER.Log($"Python stuck detector error: {ex}", LogLevel.Error);
            return false;
        }
    }

    private void EnsureInitialised()
    {
        if (initialised)
            return;

        lock (initLock)
        {
            if (initialised)
                return;

            if (!File.Exists(stuckDetectorPath))
                throw new FileNotFoundException("Python stuck detector file not found.", stuckDetectorPath);

            EnsurePythonRuntime();

            var code = File.ReadAllText(stuckDetectorPath);
            using var gil = Py.GIL();
            using var scope = Py.CreateScope();
            var compiled = PythonEngine.Compile(code);
            scope.Execute(compiled);

            dynamic detectorType = scope.Get("StuckDetector");
            detector = detectorType.Invoke(window.ToPython());

            initialised = true;
        }
    }

    private static void EnsurePythonRuntime()
    {
        if (PythonEngine.IsInitialized)
            return;

        var dll = Environment.GetEnvironmentVariable("PYTHONNET_PYDLL");
        if (string.IsNullOrWhiteSpace(dll))
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                dll = "python312.dll";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                dll = "libpython3.12.so";
            else
                dll = "libpython3.12.dylib";
        }

        Runtime.PythonDLL = dll;

        try
        {
            PythonEngine.Initialize();
            PythonEngine.BeginAllowThreads();
        }
        catch (Exception ex)
        {
            StandardLogs.RUNTIME_LOGGER.Log($"Python runtime initialization failed: {ex}", LogLevel.Error);
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        detector?.Dispose();
        detector = null;
    }
}

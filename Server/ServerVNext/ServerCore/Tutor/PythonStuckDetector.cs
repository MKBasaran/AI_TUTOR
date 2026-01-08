using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Python.Runtime;
using ServerCore.Logging;

namespace ServerCore.Tutor;

public sealed class PythonStuckDetector : IStuckDetector, IDisposable
{
    private readonly string stuckDetectorPath;
    private readonly int window;
    private readonly object initLock = new();
    private PyObject? detector;
    private bool initialised;

    public PythonStuckDetector(string stuckDetectorPath, int window)
    {
        this.stuckDetectorPath = stuckDetectorPath;
        this.window = window;
    }

    public bool IsStuck(IReadOnlyList<TutorTrialRecord> history)
    {
        if (history.Count < window)
            return false;

        EnsureInitialised();

        using var gil = Py.GIL();
        using var pyList = new PyList();

        foreach (var record in history)
        {
            using var pyDict = new PyDict();
            pyDict.SetItem("speed_mps", record.SessionScore.ToPython());

            using var paramDict = new PyDict();
            foreach (var param in record.Params)
                paramDict.SetItem(param.Key, param.Value.ToPython());

            pyDict.SetItem("params", paramDict);

            if (record.Safety.Overcurrent)
                pyDict.SetItem("overcurrent", true.ToPython());
            if (record.Safety.Overtemp)
                pyDict.SetItem("overtemp", true.ToPython());
            if (record.Safety.Timeout)
                pyDict.SetItem("timeout", true.ToPython());

            pyList.Append(pyDict);
        }

        try
        {
            dynamic result = detector!.InvokeMethod("detect_stuck", pyList);
            return (bool)result;
        }
        catch (PythonException ex)
        {
            StandardLogs.RUNTIME_LOGGER.Log(ex.Message, LogLevel.Error);
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

            if (!PythonEngine.IsInitialized)
            {
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
                    StandardLogs.RUNTIME_LOGGER.Log(ex.Message, LogLevel.Error);
                    throw;
                }
            }

            var code = File.ReadAllText(stuckDetectorPath);
            using var gil = Py.GIL();
            var scope = Py.CreateScope();
            var compiled = PythonEngine.Compile(code);
            scope.Execute(compiled);
            dynamic detectorType = scope.Get("StuckDetector");
            detector = detectorType.Invoke(window.ToPython());

            initialised = true;
        }
    }

    public void Dispose()
    {
        detector?.Dispose();
        detector = null;
    }
}
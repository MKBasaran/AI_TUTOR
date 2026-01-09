using System;
using System.IO;

namespace ServerCore.Tutor;

public sealed class TutorOptions
{
    public string StuckDetectorPath { get; set; } = "..\\..\\..\\..\\..\\stuck_detector.py";
    public int StuckWindow { get; set; } = 5;
    public int HintBudget { get; set; } = 6;
    public int HistoryLimit { get; set; } = 25;
    public int EscalationTrials { get; set; } = 3;
    public double ProgressThreshold { get; set; } = 0.02;
    public double ParamDeltaEpsilon { get; set; } = 0.05;
    public bool IncludeDiagnostics { get; set; } = false;
    public string HintMode { get; set; } = "per_leg";
    public int HintVoteThreshold { get; set; } = 3;
    public int HintVoteTotal { get; set; } = 4;

    public string ResolveStuckDetectorPath(string contentRoot)
    {
        var path = StuckDetectorPath;
        if (string.IsNullOrWhiteSpace(path))
            path = "stuck_detector.py";

        if (Path.IsPathRooted(path) && File.Exists(path))
            return StuckDetectorPath = path;

        string[] roots =
        [
            AppContext.BaseDirectory,
            contentRoot,
            Directory.GetParent(contentRoot)?.FullName ?? contentRoot,
            Directory.GetParent(Directory.GetParent(contentRoot)?.FullName ?? contentRoot)?.FullName ?? contentRoot,
            Directory.GetParent(Directory.GetParent(Directory.GetParent(contentRoot)?.FullName ?? contentRoot)?.FullName ?? contentRoot)?.FullName ?? contentRoot
        ];

        foreach (var root in roots)
        {
            var candidate = Path.GetFullPath(Path.Combine(root, path));
            if (File.Exists(candidate))
            {
                StuckDetectorPath = candidate;
                return candidate;
            }

            var fallback = Path.Combine(root, "stuck_detector.py");
            if (File.Exists(fallback))
            {
                StuckDetectorPath = Path.GetFullPath(fallback);
                return StuckDetectorPath;
            }
        }

        StuckDetectorPath = Path.GetFullPath(Path.Combine(contentRoot, path));
        return StuckDetectorPath;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ServerCore.Tutor;

/// <summary>
/// Configuration for tutor behaviour, thresholds, and integration paths.
/// </summary>
public sealed class TutorOptions
{
    /// <summary>
    /// Path to the Python stuck detector file (<c>stuck_detector.py</c>). Can be relative to the content root.
    /// </summary>
    public string StuckDetectorPath { get; set; } = "..\\..\\..\\..\\..\\stuck_detector.py";

    /// <summary>Number of recent trials required before stuck detection can trigger.</summary>
    public int StuckWindow { get; set; } = 5;

    /// <summary>Hints available per leg (per-leg mode) or per session (global-majority mode).</summary>
    public int HintBudget { get; set; } = 6;

    /// <summary>Maximum number of trial records stored in memory per leg.</summary>
    public int HistoryLimit { get; set; } = 25;

    /// <summary>Number of consecutive stuck trials required to escalate hint tier.</summary>
    public int EscalationTrials { get; set; } = 3;

    /// <summary>Score improvement required to consider a run "progressing".</summary>
    public double ProgressThreshold { get; set; } = 0.02;

    /// <summary>Relative threshold for treating parameter changes as meaningful (as fraction of range).</summary>
    public double ParamDeltaEpsilon { get; set; } = 0.05;

    /// <summary>Includes diagnostics fields in responses when enabled.</summary>
    public bool IncludeDiagnostics { get; set; } = false;

    /// <summary>Hint mode: <c>per_leg</c> or <c>global_majority</c>.</summary>
    public string HintMode { get; set; } = "per_leg";

    /// <summary>Vote threshold for global-majority hint mode.</summary>
    public int HintVoteThreshold { get; set; } = 3;

    /// <summary>Total number of voters shown in the UI for global-majority hint mode.</summary>
    public int HintVoteTotal { get; set; } = 4;

    /// <summary>
    /// Resolves <see cref="StuckDetectorPath"/> to an absolute path using a small set of likely roots.
    /// </summary>
    public string ResolveStuckDetectorPath(string contentRoot)
    {
        var configured = StuckDetectorPath;
        if (string.IsNullOrWhiteSpace(configured))
            configured = "stuck_detector.py";

        if (Path.IsPathRooted(configured) && File.Exists(configured))
            return StuckDetectorPath = configured;

        var roots = new List<string>
        {
            AppContext.BaseDirectory,
            contentRoot
        };

        var current = new DirectoryInfo(contentRoot);
        for (var i = 0; i < 4; i++)
        {
            current = current.Parent ?? current;
            roots.Add(current.FullName);
        }

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var candidate = Path.GetFullPath(Path.Combine(root, configured));
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

        StuckDetectorPath = Path.GetFullPath(Path.Combine(contentRoot, configured));
        return StuckDetectorPath;
    }
}


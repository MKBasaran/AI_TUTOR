using System;
using System.Collections.Generic;
using System.Linq;

namespace ServerCore.Tutor;

public static class OptimalParameters
{
    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Targets =
        new Dictionary<string, IReadOnlyDictionary<string, double>>(StringComparer.OrdinalIgnoreCase)
        {
            ["leg-0"] = new Dictionary<string, double>
            {
                ["range"] = 0,
                ["baseline_position"] = 115,
                ["relation"] = 0
            },
            ["leg-1"] = new Dictionary<string, double>
            {
                ["range"] = 43.9,
                ["baseline_position"] = 91,
                ["relation"] = 104
            },
            ["leg-2"] = new Dictionary<string, double>
            {
                ["range"] = 0,
                ["baseline_position"] = 115,
                ["relation"] = 0
            },
            ["leg-3"] = new Dictionary<string, double>
            {
                ["range"] = 60.4,
                ["baseline_position"] = 110,
                ["relation"] = 0
            }
        };

    public static readonly IReadOnlyDictionary<string, (double Min, double Max)> ParameterRanges =
        new Dictionary<string, (double Min, double Max)>
        {
            ["range"] = (0, 90),
            ["baseline_position"] = (0, 180),
            ["relation"] = (0, 360)
        };

    public static IReadOnlyDictionary<string, double> GetTargets(string legId)
    {
        if (Targets.TryGetValue(legId, out var target))
            return target;

        return Targets["leg-0"];
    }

    public static double ScoreFor(string legId, IReadOnlyDictionary<string, double> parameters)
    {
        var target = GetTargets(legId);
        var keys = target.Keys;
        var deltas = new List<double>();

        foreach (var key in keys)
        {
            if (!parameters.TryGetValue(key, out var current))
                continue;

            var targetValue = target[key];
            var range = ParameterRanges.TryGetValue(key, out var bounds)
                ? Math.Max(bounds.Max - bounds.Min, 1)
                : 1;

            double delta;
            if (string.Equals(key, "relation", StringComparison.OrdinalIgnoreCase))
                delta = Math.Abs(ShortestAngleDelta(current, targetValue));
            else
                delta = Math.Abs(current - targetValue);

            deltas.Add(delta / range);
        }

        if (deltas.Count == 0)
            return 0;

        var average = deltas.Average();
        var score = 1 - average;
        return Math.Clamp(score, 0, 1);
    }

    public static double ShortestAngleDelta(double current, double target)
    {
        double delta = (target - current) % 360;
        if (delta > 180)
            delta -= 360;
        if (delta < -180)
            delta += 360;
        return delta;
    }
}

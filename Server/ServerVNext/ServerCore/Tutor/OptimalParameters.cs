using System;
using System.Collections.Generic;

namespace ServerCore.Tutor;

/// <summary>
/// Holds the current best-known parameter targets per leg and helper scoring utilities.
/// </summary>
public static class OptimalParameters
{
    public const string SpeedKey = "speed";
    public const string RangeKey = "range";
    public const string BaselinePositionKey = "baseline_position";
    public const string RelationKey = "relation";

    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Targets =
        new Dictionary<string, IReadOnlyDictionary<string, double>>(StringComparer.OrdinalIgnoreCase)
        {
            ["leg-0"] = new Dictionary<string, double>
            {
                [RangeKey] = 0,
                [BaselinePositionKey] = 115,
                [RelationKey] = 0
            },
            ["leg-1"] = new Dictionary<string, double>
            {
                [RangeKey] = 43.9,
                [BaselinePositionKey] = 91,
                [RelationKey] = 104
            },
            ["leg-2"] = new Dictionary<string, double>
            {
                [RangeKey] = 0,
                [BaselinePositionKey] = 115,
                [RelationKey] = 0
            },
            ["leg-3"] = new Dictionary<string, double>
            {
                [RangeKey] = 60.4,
                [BaselinePositionKey] = 110,
                [RelationKey] = 0
            }
        };

    public static readonly IReadOnlyDictionary<string, (double Min, double Max)> ParameterRanges =
        new Dictionary<string, (double Min, double Max)>
        {
            [RangeKey] = (0, 90),
            [BaselinePositionKey] = (0, 180),
            [RelationKey] = (0, 360)
        };

    /// <summary>
    /// Returns the target parameter values for a given leg (falls back to leg-0).
    /// </summary>
    public static IReadOnlyDictionary<string, double> GetTargets(string legId)
        => Targets.TryGetValue(legId, out var target) ? target : Targets["leg-0"];

    /// <summary>
    /// Scores how close <paramref name="parameters"/> is to the target for <paramref name="legId"/>.
    /// Returns a value in [0, 1], where higher is better.
    /// </summary>
    public static double ScoreFor(string legId, IReadOnlyDictionary<string, double> parameters)
    {
        var target = GetTargets(legId);

        double normalizedDeltaSum = 0;
        var deltaCount = 0;

        foreach (var key in target.Keys)
        {
            if (!parameters.TryGetValue(key, out var current))
                continue;

            var targetValue = target[key];
            var range = ParameterRanges.TryGetValue(key, out var bounds)
                ? Math.Max(bounds.Max - bounds.Min, 1)
                : 1;

            double delta;
            if (string.Equals(key, RelationKey, StringComparison.OrdinalIgnoreCase))
                delta = Math.Abs(ShortestAngleDelta(current, targetValue));
            else
                delta = Math.Abs(current - targetValue);

            normalizedDeltaSum += delta / range;
            deltaCount += 1;
        }

        if (deltaCount == 0)
            return 0;

        var average = normalizedDeltaSum / deltaCount;
        var score = 1 - average;
        return Math.Clamp(score, 0, 1);
    }

    /// <summary>
    /// Computes the shortest signed angular distance (degrees) from <paramref name="current"/> to <paramref name="target"/>.
    /// </summary>
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

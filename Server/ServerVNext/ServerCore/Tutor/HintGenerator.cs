using System;
using System.Collections.Generic;
using System.Linq;

namespace ServerCore.Tutor;

public sealed class HintGenerator
{
    private readonly TutorOptions options;
    private readonly Random random = new();

    private static readonly string[] ReflectionHints =
    [
        "What changed between recent trials? Try a single adjustment and compare stability with speed.",
        "Pause and review the trend. Are changes helping stability or slowing the run?",
        "Try one small change at a time and watch the motion carefully.",
        "If progress slows, simplify the changes and test again."
    ];

    private static readonly string[] MicroHints =
    [
        "Make a small change in one direction and see if the motion becomes smoother.",
        "Try nudging a single parameter while keeping the others steady.",
        "If the last change reduced stability, try the opposite direction."
    ];

    private static readonly string[] PatternHints =
    [
        "Reduce aggressiveness until the motion is smooth, then increase gradually while keeping changes consistent.",
        "Aim for smooth, repeatable motion first, then build up speed in small steps.",
        "Keep changes consistent across trials and adjust gradually until the motion stabilizes."
    ];

    public HintGenerator(TutorOptions options)
    {
        this.options = options;
    }

    public Dictionary<string, ParameterDirection> SuggestDirections(IReadOnlyList<TutorTrialRecord> history, string legId)
    {
        var directions = new Dictionary<string, ParameterDirection>(StringComparer.OrdinalIgnoreCase);
        if (history.Count == 0)
            return directions;

        var last = history[^1];
        var targets = OptimalParameters.GetTargets(legId);

        foreach (var target in targets)
        {
            if (!last.Params.TryGetValue(target.Key, out var current))
                continue;

            var range = OptimalParameters.ParameterRanges.TryGetValue(target.Key, out var bounds)
                ? Math.Max(bounds.Max - bounds.Min, 1)
                : 1;
            var epsilon = range * options.ParamDeltaEpsilon;

            double delta;
            if (string.Equals(target.Key, "relation", StringComparison.OrdinalIgnoreCase))
                delta = OptimalParameters.ShortestAngleDelta(current, target.Value);
            else
                delta = target.Value - current;

            if (Math.Abs(delta) <= epsilon)
                directions[target.Key] = ParameterDirection.Keep;
            else if (delta > 0)
                directions[target.Key] = ParameterDirection.Increase;
            else
                directions[target.Key] = ParameterDirection.Decrease;
        }

        return directions;
    }

    public HintPayload CreateHint(
        HintTier tier,
        IReadOnlyDictionary<string, ParameterDirection> directions,
        SafetyFlags safety,
        bool hintLimitReached,
        string goalType)
    {
        if (hintLimitReached)
        {
            return new HintPayload(
                HintTier.Reflection,
                "Hint limit reached. Focus on one small adjustment and observe the trend.",
                new Dictionary<string, ParameterDirection>());
        }

        if (safety.AnyIssue)
        {
            return new HintPayload(
                tier,
                "Safety warning detected. Reduce aggressiveness and keep changes small until the motion stabilizes.",
                new Dictionary<string, ParameterDirection>());
        }

        string text = tier switch
        {
            HintTier.Micro => BuildMicroHint(directions),
            HintTier.Pattern => Pick(PatternHints),
            _ => Pick(ReflectionHints)
        };

        var payloadDirections = tier == HintTier.Reflection
            ? new Dictionary<string, ParameterDirection>()
            : new Dictionary<string, ParameterDirection>(directions, StringComparer.OrdinalIgnoreCase);

        return new HintPayload(tier, text, payloadDirections);
    }

    private string BuildMicroHint(IReadOnlyDictionary<string, ParameterDirection> directions)
    {
        var candidate = directions.FirstOrDefault(kvp => kvp.Value != ParameterDirection.Keep);
        if (!string.IsNullOrWhiteSpace(candidate.Key))
        {
            var directionText = candidate.Value switch
            {
                ParameterDirection.Increase => "up",
                ParameterDirection.Decrease => "down",
                _ => "a little"
            };

            var label = ParameterLabel(candidate.Key);
            return $"Try nudging {label} {directionText} and watch for smoother motion.";
        }

        return Pick(MicroHints);
    }

    private static string ParameterLabel(string key)
    {
        return key switch
        {
            "speed" => "Speed",
            "range" => "Range",
            "baseline_position" => "Baseline position",
            "relation" => "Relation",
            _ => key
        };
    }

    private string Pick(IReadOnlyList<string> options)
        => options[random.Next(options.Count)];
}
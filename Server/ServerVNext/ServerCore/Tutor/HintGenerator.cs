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
        "The trend shows whether a change helped or hurt the motion. Change one thing and compare the next run.",
        "Multiple changes hide cause and effect. Keep everything else the same and adjust just one setting.",
        "Stable motion comes from consistent, small experiments. Try a tiny change, then watch the next run closely.",
        "If progress stalls, changes are often too big or too many. Reset to a simple setting and test a single tweak."
    ];

    private static readonly string[] MicroHints =
    [
        "Small steps reduce wobble and make cause and effect clearer. Make one small change and keep the rest steady.",
        "Gentle adjustments often smooth oscillation before adding speed. Nudge one setting slightly and watch the motion.",
        "Overshooting can make the motion worse on the next run. Try the opposite direction of the last change and compare."
    ];

    private static readonly string[] PatternHints =
    [
        "Consistent steps reveal which direction truly improves motion. Reduce aggressiveness until smooth, then increase gradually.",
        "Smooth, repeatable movement creates a reliable baseline. Keep changes consistent and build up slowly.",
        "Large jumps add noise and hide improvement. Take a few small steps in the same direction and reassess."
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
                "The hint limit means you need to rely on the trend now. Make one small adjustment and observe the next run.",
                new Dictionary<string, ParameterDirection>());
        }

        if (safety.AnyIssue)
        {
            return new HintPayload(
                tier,
                "Safety warnings mean the motion is too aggressive for the hardware. Reduce aggressiveness and keep changes small until it stabilizes.",
                new Dictionary<string, ParameterDirection>());
        }

        string text = tier switch
        {
            HintTier.Micro => BuildMicroHint(directions),
            HintTier.Pattern => Pick(PatternHints),
            _ => Pick(ReflectionHints)
        };

        var payloadDirections = tier == HintTier.Pattern
            ? new Dictionary<string, ParameterDirection>(directions, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, ParameterDirection>();

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
            return $"Small steps make the effect easier to see and reduce instability. Nudge {label} {directionText} and watch the motion.";
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

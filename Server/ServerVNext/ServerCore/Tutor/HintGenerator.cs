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
        "Look at the pattern: did your last change help or hurt? Change one thing, then test again.",
        "If you change many things at once, it’s hard to know what worked. Keep everything the same and change one setting.",
        "Good progress comes from small, careful tests. Make a tiny change and watch what happens next.",
        "If you feel stuck, your changes might be too big. Go back to a simple setup and try one small tweak."
    ];

    private static readonly string[] MicroHints =
    [
        "Small changes help you see cause and effect. Change one setting a little and keep the rest the same.",
        "Try a gentle adjustment first. Move one setting slightly, then watch the robot’s movement.",
        "If the last change made it worse, try the other direction next time and compare."
    ];

    private static readonly string[] PatternHints =
    [
        "Make steady, repeatable tests. Lower the ‘strength’ of changes until it’s smooth, then increase slowly.",
        "First, aim for smooth and consistent movement. That gives you a good starting point.",
        "Big jumps can hide what’s really happening. Take a few small steps in one direction, then check results."
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
                "You’ve used all your hints for now. Use the trend: change one small thing and test again.",
                new Dictionary<string, ParameterDirection>());
        }

        if (safety.AnyIssue)
        {
            return new HintPayload(
                tier,
                "Safety warning: the robot is moving too strongly. Turn things down and make only small changes until it’s stable.",
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
            return $"Try a small change. Move {label} {directionText}, then watch what changes in the movement.";
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

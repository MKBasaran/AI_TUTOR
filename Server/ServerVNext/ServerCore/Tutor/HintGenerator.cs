using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ServerCore.Tutor;

/// <summary>
/// Generates scaffolded, non-revealing hints and optional parameter direction indicators.
/// </summary>
public sealed class HintGenerator
{
    private readonly TutorOptions options;

    private static readonly string[] ReflectionHints =
    [
        "Pick one person to change one slider a tiny bit. Everyone else keeps their sliders the same. This way you can tell what caused the change.",
        "Look at the robot together: is it smooth, shaky, or turning? Agree on one small change to try next. You learn faster when you all watch for the same thing.",
        "Try to make the movement look smooth and repeatable first. Then change just one slider a tiny bit and test again. Stable motion is easier to improve.",
        "Take a quick team pause and choose who changes what next. Make one small change and run another test. Clear roles stop you from working against each other."
    ];

    private static readonly string[] MicroHints =
    [
        "Tell your team which slider you will change. Make a small change and run a short test while the others keep theirs steady. That shows what your change did.",
        "If the robot looks wobbly or shaky, try a smaller change than last time. Ask your teammates if it looks smoother. Smoother motion usually works better.",
        "If the last change made it worse, try the other direction with a small step. Let your team watch for any improvement. Comparing directions makes learning faster."
    ];

    private static readonly string[] PatternHints =
    [
        "Aim for motion that looks smooth, steady, and repeats the same way each time. If it jerks or wobbles, make changes smaller and test again. Stable motion is easier for the whole team to improve.",
        "If the robot keeps turning, different legs may be pushing in different ways. Talk with your teammates and try to make the robot push in one clear direction. Coordinated legs stop fighting each other.",
        "Take turns changing sliders and keep the changes small. When the motion looks stable, improve it step by step. Careful teamwork finds what really works."
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
            if (string.Equals(target.Key, OptimalParameters.RelationKey, StringComparison.OrdinalIgnoreCase))
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
        _ = goalType;

        if (hintLimitReached)
        {
            return new HintPayload(
                HintTier.Reflection,
                "No more hints right now. Take turns making one small change and watch what happens. Careful tests can still move you forward.",
                new Dictionary<string, ParameterDirection>());
        }

        if (safety.AnyIssue)
        {
            return new HintPayload(
                tier,
                "The robot may be struggling or unsafe. Turn the sliders down and only make small changes. Safe, smooth motion is easier to control.",
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
                ParameterDirection.Increase => "a bit higher",
                ParameterDirection.Decrease => "a bit lower",
                _ => "a little"
            };

            var label = ParameterLabel(candidate.Key);

            return $"Tell your team you are changing {label}. Move it {directionText} and run a short test while the others keep theirs steady. That shows what your change did.";
        }

        return Pick(MicroHints);
    }

    private static string ParameterLabel(string key)
        => key switch
        {
            var k when string.Equals(k, OptimalParameters.SpeedKey, StringComparison.OrdinalIgnoreCase) => "Speed",
            var k when string.Equals(k, OptimalParameters.RangeKey, StringComparison.OrdinalIgnoreCase) => "Range",
            var k when string.Equals(k, OptimalParameters.BaselinePositionKey, StringComparison.OrdinalIgnoreCase) => "Baseline position",
            var k when string.Equals(k, OptimalParameters.RelationKey, StringComparison.OrdinalIgnoreCase) => "Relation",
            _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(key.Replace('_', ' '))
        };

    private static string Pick(IReadOnlyList<string> candidates)
        => candidates[Random.Shared.Next(candidates.Count)];
}

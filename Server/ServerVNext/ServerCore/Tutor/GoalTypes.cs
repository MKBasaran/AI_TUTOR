using System;
using System.Collections.Generic;
using System.Linq;

namespace ServerCore.Tutor;

public static class GoalTypes
{
    public const string Speed = "speed";
    public const string TurnLeft = "turn_left";
    public const string TurnRight = "turn_right";
    public const string Stability = "stability";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Speed,
        TurnLeft,
        TurnRight,
        Stability
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Speed;

        var trimmed = value.Trim().ToLowerInvariant();
        return All.Contains(trimmed) ? trimmed : Speed;
    }
}
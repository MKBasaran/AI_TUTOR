using System;
using System.Collections.Generic;

namespace ServerCore.Tutor;

/// <summary>
/// Supported tutor goal identifiers (kept as strings for JSON compatibility).
/// </summary>
public static class GoalTypes
{
    /// <summary>Default goal: maximize speed.</summary>
    public const string Speed = "speed";

    /// <summary>Goal: turn left.</summary>
    public const string TurnLeft = "turn_left";

    /// <summary>Goal: turn right.</summary>
    public const string TurnRight = "turn_right";

    /// <summary>Goal: prioritize stability/smoothness.</summary>
    public const string Stability = "stability";

    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        Speed,
        TurnLeft,
        TurnRight,
        Stability
    };

    /// <summary>All supported goal identifiers.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Speed, TurnLeft, TurnRight, Stability };

    /// <summary>
    /// Normalizes an incoming goal identifier to a supported value.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Speed;

        var trimmed = value.Trim();
        return Supported.Contains(trimmed) ? trimmed.ToLowerInvariant() : Speed;
    }
}

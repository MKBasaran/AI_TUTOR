using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ServerCore.Tutor;

public enum HintTier
{
    Reflection,
    Micro,
    Pattern
}

public enum ParameterDirection
{
    Increase,
    Decrease,
    Keep
}

public record SafetyFlags(
    [property: JsonPropertyName("overcurrent")] bool Overcurrent = false,
    [property: JsonPropertyName("overtemp")] bool Overtemp = false,
    [property: JsonPropertyName("timeout")] bool Timeout = false)
{
    [JsonIgnore]
    public bool AnyIssue => Overcurrent || Overtemp || Timeout;
}

public record TrialSubmission(
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("params")] Dictionary<string, double> Params,
    [property: JsonPropertyName("speed_mps")] double? SpeedMps = null,
    [property: JsonPropertyName("limb_speed")] double? LimbSpeed = null,
    [property: JsonPropertyName("leg_id")] string? LegId = null,
    [property: JsonPropertyName("safety")] SafetyFlags? Safety = null,
    [property: JsonPropertyName("goal_type")] string? GoalType = null);

public record HintPayload(
    [property: JsonPropertyName("tier")] HintTier Tier,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("parameter_directions")] Dictionary<string, ParameterDirection> ParameterDirections);

public record TutorDiagnostics(
    [property: JsonPropertyName("history_count")] int HistoryCount,
    [property: JsonPropertyName("stuck_streak")] int StuckStreak,
    [property: JsonPropertyName("current_tier")] HintTier CurrentTier);

public record TutorTrialResponse(
    [property: JsonPropertyName("stuck")] bool Stuck,
    [property: JsonPropertyName("hint")] HintPayload? Hint,
    [property: JsonPropertyName("hint_budget_remaining")] int HintBudgetRemaining,
    [property: JsonPropertyName("hint_available")] bool HintAvailable,
    [property: JsonPropertyName("diagnostics")] TutorDiagnostics? Diagnostics,
    [property: JsonPropertyName("hint_mode")] string HintMode,
    [property: JsonPropertyName("hint_vote_count")] int HintVoteCount,
    [property: JsonPropertyName("hint_vote_threshold")] int HintVoteThreshold,
    [property: JsonPropertyName("hint_vote_total")] int HintVoteTotal,
    [property: JsonPropertyName("hint_voted")] bool HintVoted);

public record TutorStatusResponse(
    [property: JsonPropertyName("stuck")] bool Stuck,
    [property: JsonPropertyName("last_hint")] HintPayload? LastHint,
    [property: JsonPropertyName("hint_budget_remaining")] int HintBudgetRemaining,
    [property: JsonPropertyName("hint_available")] bool HintAvailable,
    [property: JsonPropertyName("hint_mode")] string HintMode,
    [property: JsonPropertyName("hint_vote_count")] int HintVoteCount,
    [property: JsonPropertyName("hint_vote_threshold")] int HintVoteThreshold,
    [property: JsonPropertyName("hint_vote_total")] int HintVoteTotal,
    [property: JsonPropertyName("hint_voted")] bool HintVoted);

public record TutorHintResponse(
    [property: JsonPropertyName("stuck")] bool Stuck,
    [property: JsonPropertyName("hint")] HintPayload? Hint,
    [property: JsonPropertyName("hint_budget_remaining")] int HintBudgetRemaining,
    [property: JsonPropertyName("hint_available")] bool HintAvailable,
    [property: JsonPropertyName("hint_mode")] string HintMode,
    [property: JsonPropertyName("hint_vote_count")] int HintVoteCount,
    [property: JsonPropertyName("hint_vote_threshold")] int HintVoteThreshold,
    [property: JsonPropertyName("hint_vote_total")] int HintVoteTotal,
    [property: JsonPropertyName("hint_voted")] bool HintVoted);

public record TutorHintRequest(
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("leg_id")] string LegId);

public record TutorStuckReportRequest(
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("leg_id")] string LegId,
    [property: JsonPropertyName("timestamp")] DateTimeOffset? Timestamp = null);

public record TutorStuckReportResponse(
    [property: JsonPropertyName("accepted")] bool Accepted,
    [property: JsonPropertyName("system_stuck")] bool SystemStuck);

public record TutorTrialRecord(
    DateTimeOffset Timestamp,
    string RunId,
    string LegId,
    Dictionary<string, double> Params,
    double LimbSpeed,
    double SessionScore,
    SafetyFlags Safety,
    string GoalType);

public record TutorTrialLogEntry(
    DateTimeOffset Timestamp,
    string SessionId,
    string RunId,
    string LegId,
    Dictionary<string, double> Params,
    double SessionScore,
    double LimbSpeed,
    SafetyFlags Safety,
    string GoalType,
    bool Stuck,
    HintPayload? Hint,
    int HintBudgetRemaining,
    string HintMode);

public record TutorStuckReportEntry(
    DateTimeOffset Timestamp,
    string SessionId,
    string LegId,
    string? RunId,
    Dictionary<string, double>? Params,
    double SessionScore,
    bool SystemStuck,
    bool Accepted,
    string HintMode);

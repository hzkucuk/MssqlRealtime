namespace MssqlRealtime.Core.Alerts;

public enum Severity
{
    Ok = 0,
    Warning = 1,
    Critical = 2
}

/// <summary>
/// What a module reports for one evaluated rule on one target, every cycle.
/// The module decides whether a rule is breached; the engine decides whether that is news.
/// </summary>
public sealed record AlertCandidate
{
    public required string RuleId { get; init; }

    /// <summary>Rule label shown in UI, e.g. "İşlemci".</summary>
    public required string RuleTitle { get; init; }

    public required bool IsBreached { get; init; }
    public required Severity Severity { get; init; }

    /// <summary>User-facing sentence for the notification body (Turkish).</summary>
    public required string Message { get; init; }

    public double? Value { get; init; }
    public double? Threshold { get; init; }
    public string? Unit { get; init; }

    /// <summary>Consecutive breached cycles before firing. 1 fires immediately.</summary>
    public int RequiredConsecutiveBreaches { get; init; } = 1;

    public int RenotifyMinutes { get; init; } = 15;

    public static AlertCandidate Ok(string ruleId, string ruleTitle) => new()
    {
        RuleId = ruleId,
        RuleTitle = ruleTitle,
        IsBreached = false,
        Severity = Severity.Ok,
        Message = string.Empty
    };
}

/// <summary>Identifies what is being monitored, without the engine knowing what it is.</summary>
public sealed record AlertTarget
{
    public required string ModuleId { get; init; }
    public required string TargetId { get; init; }
    public required string TargetName { get; init; }

    /// <summary>Grouping label — the customer, in the MSSQL module.</summary>
    public string? GroupName { get; init; }
}

/// <summary>A rule that is currently firing.</summary>
public sealed record AlertState
{
    public required AlertTarget Target { get; init; }
    public required string RuleId { get; init; }
    public required string RuleTitle { get; init; }
    public required Severity Severity { get; init; }
    public required string Message { get; init; }
    public double? Value { get; init; }
    public double? Threshold { get; init; }
    public string? Unit { get; init; }
    public required DateTimeOffset SinceUtc { get; init; }
    public DateTimeOffset? LastNotifiedUtc { get; init; }

    /// <summary>Deduplication key for the client.</summary>
    public string Key => $"{Target.ModuleId}:{Target.TargetId}:{RuleId}";
}

/// <summary>
/// Pushed only when something changed: a rule started firing, stopped firing, or is still
/// firing after the renotify window. A snapshot on its own never notifies the phone.
/// </summary>
public sealed record AlertNotification
{
    public required AlertState Alert { get; init; }
    public required bool IsCleared { get; init; }
    public required DateTimeOffset RaisedAtUtc { get; init; }

    public string Title => IsCleared
        ? $"✅ {Alert.Target.TargetName}"
        : Alert.Severity == Severity.Critical
            ? $"🔴 {Alert.Target.TargetName}"
            : $"🟠 {Alert.Target.TargetName}";

    public string Body => IsCleared
        ? $"{Alert.RuleTitle} normale döndü."
        : Alert.Message;
}

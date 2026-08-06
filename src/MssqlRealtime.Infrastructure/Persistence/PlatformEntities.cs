using MssqlRealtime.Core.Alerts;

namespace MssqlRealtime.Infrastructure.Persistence;

/// <summary>
/// One configured value of one notification channel. Secrets are stored encrypted and are
/// never sent back to a client — only the fact that a value exists.
/// </summary>
public sealed class NotificationChannelSetting
{
    public int Id { get; set; }
    public string ChannelId { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;

    /// <summary>Plain text, or Data Protection ciphertext when <see cref="IsSecret"/>.</summary>
    public string Value { get; set; } = string.Empty;

    public bool IsSecret { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Whether a channel is switched on, and from which severity upwards it fires.</summary>
public sealed class NotificationChannelState
{
    public string ChannelId { get; set; } = string.Empty;
    public bool Enabled { get; set; }

    /// <summary>Only alerts at this severity or above are delivered through the channel.</summary>
    public Severity MinimumSeverity { get; set; } = Severity.Warning;

    /// <summary>Whether "back to normal" messages are sent as well.</summary>
    public bool SendRecoveries { get; set; } = true;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A notification that has not been delivered yet.
/// <para>
/// Telegram being briefly unreachable must not mean the alert is lost — that is precisely the
/// moment the tool has to work. Failed deliveries land here and are retried in the background
/// until they succeed or the give-up window passes.
/// </para>
/// </summary>
public sealed class NotificationOutboxEntry
{
    public long Id { get; set; }
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>Serialized AlertNotification.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>Shown in the UI without deserializing the payload.</summary>
    public string Summary { get; set; } = string.Empty;

    public int Attempts { get; set; }
    public DateTime FirstFailedUtc { get; set; }
    public DateTime? LastAttemptUtc { get; set; }

    /// <summary>Backoff: nothing is retried before this moment.</summary>
    public DateTime NextAttemptUtc { get; set; }

    public string? LastError { get; set; }

    /// <summary>Set when the give-up window passed; kept for visibility, not retried again.</summary>
    public DateTime? AbandonedUtc { get; set; }
}

/// <summary>
/// Persisted alert history. Survives restarts, which is the difference between "the service
/// restarted at 04:00 and we lost the night" and an answer to "what happened last night".
/// </summary>
public sealed class AlertRecord
{
    public long Id { get; set; }
    public string ModuleId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;
    public string? GroupName { get; set; }
    public string RuleId { get; set; } = string.Empty;
    public string RuleTitle { get; set; } = string.Empty;
    public Severity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public double? Value { get; set; }
    public double? Threshold { get; set; }
    public string? Unit { get; set; }

    /// <summary>
    /// Who was consuming the server when this fired. Nullable on purpose: records written
    /// before this existed have no answer, and inventing one would be worse than saying
    /// nothing.
    /// </summary>
    public string? Context { get; set; }

    // Stored as UTC DateTime, not DateTimeOffset: measured 2026-08-05, SQLite cannot ORDER BY
    // a DateTimeOffset column and the history query is ordered by time by definition.
    public DateTime RaisedAtUtc { get; set; }
    public DateTime? ClearedAtUtc { get; set; }
    public DateTime? LastNotifiedUtc { get; set; }
}

/// <summary>
/// One measurement of a target, kept so the reports screen can answer "what did last month
/// look like?". Module-agnostic on purpose: the platform stores numbers and a module decides
/// what they mean, exactly like alerts.
/// </summary>
/// <remarks>
/// Rows are thinned rather than kept forever. A sample a minute is 525.600 rows per server
/// per year, which is both slow to chart and pointless at that age — nobody asks what the CPU
/// did at 03:47 last March, they ask what March looked like. So samples age into hourly and
/// then daily averages, and everything older than two years is deleted.
/// </remarks>
public sealed class MetricSample
{
    public long Id { get; set; }
    public string ModuleId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;

    /// <summary>Start of the bucket this row covers (UTC).</summary>
    public DateTime TakenAtUtc { get; set; }

    public MetricResolution Resolution { get; set; }

    public double? CpuPercent { get; set; }
    public double? SqlCpuPercent { get; set; }
    public double? MemoryPercent { get; set; }
    public int? SqlMemoryMb { get; set; }
    public int? SessionCount { get; set; }
    public int? RequestCount { get; set; }
    public int? BlockedCount { get; set; }
    public int? LongestQuerySeconds { get; set; }

    /// <summary>How many minute samples this row was folded from; 1 while it is still raw.</summary>
    public int SampleCount { get; set; } = 1;
}

public enum MetricResolution
{
    Minute = 0,
    Hour = 1,
    Day = 2
}

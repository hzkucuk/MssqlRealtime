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

    // Stored as UTC DateTime, not DateTimeOffset: measured 2026-08-05, SQLite cannot ORDER BY
    // a DateTimeOffset column and the history query is ordered by time by definition.
    public DateTime RaisedAtUtc { get; set; }
    public DateTime? ClearedAtUtc { get; set; }
    public DateTime? LastNotifiedUtc { get; set; }
}

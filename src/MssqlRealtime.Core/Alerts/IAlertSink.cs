namespace MssqlRealtime.Core.Alerts;

/// <summary>
/// Where a raised alert goes. One call, several destinations: the in-app stream, the persisted
/// history, and every configured notification channel.
/// <para>
/// Modules publish here instead of talking to SignalR directly, so a tool written later gets
/// e-mail and Telegram delivery without knowing either exists.
/// </para>
/// </summary>
public interface IAlertSink
{
    Task PublishAsync(AlertNotification notification, CancellationToken ct = default);
}

/// <summary>Persisted alert history, so a restart does not erase what happened last night.</summary>
public interface IAlertStore
{
    /// <summary>Records a raise, an escalation or a clear.</summary>
    Task RecordAsync(AlertNotification notification, CancellationToken ct = default);

    /// <summary>Alerts that were still firing when the service last stopped.</summary>
    Task<IReadOnlyList<AlertState>> GetActiveAsync(CancellationToken ct = default);

    /// <summary>Newest first. This is what a phone shows after being offline for a while.</summary>
    Task<IReadOnlyList<AlertHistoryEntry>> GetHistoryAsync(int limit = 100, CancellationToken ct = default);

    /// <summary>Drops history older than the retention window.</summary>
    Task<int> PruneAsync(TimeSpan retention, CancellationToken ct = default);
}

public sealed record AlertHistoryEntry
{
    public required long Id { get; init; }
    public required string ModuleId { get; init; }
    public required string TargetId { get; init; }
    public required string TargetName { get; init; }
    public string? GroupName { get; init; }
    public required string RuleId { get; init; }
    public required string RuleTitle { get; init; }
    public required Severity Severity { get; init; }
    public required string Message { get; init; }
    public double? Value { get; init; }
    public double? Threshold { get; init; }
    public string? Unit { get; init; }
    public required DateTimeOffset RaisedAtUtc { get; init; }
    public DateTimeOffset? ClearedAtUtc { get; init; }
    public bool IsActive => ClearedAtUtc is null;
}

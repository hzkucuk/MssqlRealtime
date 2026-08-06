namespace MssqlRealtime.Core.Abstractions;

/// <summary>One cycle's numbers for a target. Every field is optional: a probe that could
/// not measure something must leave it null rather than report a zero, or the report will
/// show a quiet server where there was actually no measurement.</summary>
public sealed record MetricPoint
{
    public double? CpuPercent { get; init; }
    public double? SqlCpuPercent { get; init; }
    public double? MemoryPercent { get; init; }
    public int? SqlMemoryMb { get; init; }
    public int? SessionCount { get; init; }
    public int? RequestCount { get; init; }
    public int? BlockedCount { get; init; }
    public int? LongestQuerySeconds { get; init; }
}

/// <summary>
/// Where modules hand their measurements for history. Fire-and-forget on purpose: a poller
/// must never wait on the reports database, and a lost minute is worth less than a delayed
/// alert.
/// </summary>
public interface IMetricSink
{
    void Report(string moduleId, string targetId, MetricPoint point);
}

public enum MetricRange
{
    Day,
    Week,
    Month,
    Year
}

public sealed record MetricSeriesPoint
{
    public required DateTimeOffset AtUtc { get; init; }
    public double? CpuPercent { get; init; }
    public double? SqlCpuPercent { get; init; }
    public double? MemoryPercent { get; init; }
    public int? SqlMemoryMb { get; init; }
    public int? SessionCount { get; init; }
    public int? RequestCount { get; init; }
    public int? BlockedCount { get; init; }
    public int? LongestQuerySeconds { get; init; }
}

public interface IMetricStore
{
    Task<IReadOnlyList<MetricSeriesPoint>> ReadAsync(
        string moduleId, string targetId, MetricRange range, CancellationToken ct = default);
}

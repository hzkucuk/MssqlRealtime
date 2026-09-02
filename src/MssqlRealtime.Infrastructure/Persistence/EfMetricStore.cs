using Microsoft.EntityFrameworkCore;
using MssqlRealtime.Core.Abstractions;

namespace MssqlRealtime.Infrastructure.Persistence;

/// <summary>Reads the history back at whatever resolution the asked-for window deserves.</summary>
public sealed class EfMetricStore(AppDbContext db) : IMetricStore
{
    public async Task<IReadOnlyList<MetricSeriesPoint>> ReadAsync(
        string moduleId, string targetId, MetricRange range, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // The window decides the resolution: a day of minutes is 1440 points, which a phone
        // can draw; a year of minutes would be half a million, which it cannot and which no
        // eye could read anyway.
        var (from, resolutions) = range switch
        {
            MetricRange.Day => (now.AddDays(-1), new[] { MetricResolution.Minute, MetricResolution.Hour }),
            MetricRange.Week => (now.AddDays(-7), new[] { MetricResolution.Minute, MetricResolution.Hour }),
            MetricRange.Month => (now.AddDays(-30), new[] { MetricResolution.Hour, MetricResolution.Day }),
            _ => (now.AddDays(-365), new[] { MetricResolution.Day, MetricResolution.Hour })
        };

        var rows = await db.Set<MetricSample>()
            .AsNoTracking()
            .Where(x => x.ModuleId == moduleId
                && x.TargetId == targetId
                && x.TakenAtUtc >= from
                && resolutions.Contains(x.Resolution))
            .OrderBy(x => x.TakenAtUtc)
            .ToListAsync(ct);

        return rows.Select(x => new MetricSeriesPoint
        {
            AtUtc = new DateTimeOffset(x.TakenAtUtc, TimeSpan.Zero),
            CpuPercent = x.CpuPercent,
            SqlCpuPercent = x.SqlCpuPercent,
            MemoryPercent = x.MemoryPercent,
            SqlMemoryMb = x.SqlMemoryMb,
            SessionCount = x.SessionCount,
            RequestCount = x.RequestCount,
            BlockedCount = x.BlockedCount,
            LongestQuerySeconds = x.LongestQuerySeconds,
            LongestQueryBy = x.LongestQueryBy,
            LongestQueryText = x.LongestQueryText
        }).ToList();
    }
}

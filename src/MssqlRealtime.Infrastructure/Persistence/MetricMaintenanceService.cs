using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MssqlRealtime.Infrastructure.Persistence;

/// <summary>
/// Ages the history down and finally deletes it.
/// </summary>
/// <remarks>
/// A minute sample per server is 525.600 rows a year — slow to chart and pointless at that
/// age, because nobody asks what the CPU did at 03:47 last March; they ask what March looked
/// like. So minutes older than a week become hours, hours older than three months become
/// days, and anything older than two years is deleted. Two years is the agreed retention:
/// long enough to compare this year's peak season with last year's, short enough to stay
/// small.
/// </remarks>
public sealed class MetricMaintenanceService(IServiceScopeFactory scopes, ILogger<MetricMaintenanceService> logger)
    : BackgroundService
{
    private static readonly TimeSpan KeepMinutes = TimeSpan.FromDays(7);
    private static readonly TimeSpan KeepHours = TimeSpan.FromDays(90);
    private static readonly TimeSpan KeepEverything = TimeSpan.FromDays(730);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Once at startup so an upgraded panel tidies itself, then hourly.
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));

        do
        {
            try
            {
                await RunAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Ölçüm geçmişi bakımı yapılamadı.");
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        await FoldAsync(db, MetricResolution.Minute, MetricResolution.Hour, now - KeepMinutes, TimeSpan.FromHours(1), ct);
        await FoldAsync(db, MetricResolution.Hour, MetricResolution.Day, now - KeepHours, TimeSpan.FromDays(1), ct);

        var cutoff = now - KeepEverything;
        var deleted = await db.Set<MetricSample>().Where(x => x.TakenAtUtc < cutoff).ExecuteDeleteAsync(ct);

        if (deleted > 0)
        {
            logger.LogInformation("{Count} eski ölçüm kaydı silindi (2 yıldan eski).", deleted);
        }
    }

    /// <summary>Folds every sample older than <paramref name="before"/> into wider buckets.</summary>
    private static async Task FoldAsync(
        AppDbContext db,
        MetricResolution from,
        MetricResolution to,
        DateTime before,
        TimeSpan bucket,
        CancellationToken ct)
    {
        var rows = await db.Set<MetricSample>()
            .Where(x => x.Resolution == from && x.TakenAtUtc < before)
            .ToListAsync(ct);

        if (rows.Count == 0) return;

        var folded = rows
            .GroupBy(x => new
            {
                x.ModuleId,
                x.TargetId,
                Bucket = new DateTime(x.TakenAtUtc.Ticks / bucket.Ticks * bucket.Ticks, DateTimeKind.Utc)
            })
            .Select(g =>
            {
                // The hour keeps the worst minute's query, not a query of its own: folding
                // averages the numbers but there is no average of a statement.
                var worst = Worst(g);

                return new MetricSample
                {
                    ModuleId = g.Key.ModuleId,
                    TargetId = g.Key.TargetId,
                    TakenAtUtc = g.Key.Bucket,
                    Resolution = to,
                    SampleCount = g.Sum(x => x.SampleCount),
                    // Weighted by how many raw samples each row stands for, so an hour missing
                    // half its minutes does not count as much as a full one.
                    CpuPercent = Weighted(g, x => x.CpuPercent),
                    SqlCpuPercent = Weighted(g, x => x.SqlCpuPercent),
                    MemoryPercent = Weighted(g, x => x.MemoryPercent),
                    SqlMemoryMb = WeightedInt(g, x => x.SqlMemoryMb),
                    SessionCount = WeightedInt(g, x => x.SessionCount),
                    RequestCount = WeightedInt(g, x => x.RequestCount),
                    BlockedCount = WeightedInt(g, x => x.BlockedCount),
                    LongestQuerySeconds = worst?.LongestQuerySeconds,
                    LongestQueryBy = worst?.LongestQueryBy,
                    LongestQueryText = worst?.LongestQueryText
                };
            })
            .ToList();

        db.Set<MetricSample>().RemoveRange(rows);
        db.Set<MetricSample>().AddRange(folded);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The row holding the slowest query of the bucket, if any row measured one.</summary>
    private static MetricSample? Worst(IEnumerable<MetricSample> rows) =>
        rows.Where(x => x.LongestQuerySeconds is not null).MaxBy(x => x.LongestQuerySeconds);

    private static double? Weighted(IEnumerable<MetricSample> rows, Func<MetricSample, double?> select)
    {
        var pairs = rows.Where(r => select(r) is not null).Select(r => (Value: select(r)!.Value, r.SampleCount)).ToList();
        if (pairs.Count == 0) return null;

        var weight = pairs.Sum(p => p.SampleCount);
        return weight == 0 ? null : Math.Round(pairs.Sum(p => p.Value * p.SampleCount) / weight, 2);
    }

    private static int? WeightedInt(IEnumerable<MetricSample> rows, Func<MetricSample, int?> select)
    {
        var value = Weighted(rows, r => select(r));
        return value is null ? null : (int)Math.Round(value.Value);
    }
}

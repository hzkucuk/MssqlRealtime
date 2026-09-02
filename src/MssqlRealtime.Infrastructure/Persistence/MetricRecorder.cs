using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MssqlRealtime.Core.Abstractions;

namespace MssqlRealtime.Infrastructure.Persistence;

/// <summary>
/// Collects what the pollers measure and writes one row per target per minute.
/// </summary>
/// <remarks>
/// Pollers run every few seconds; storing each cycle would multiply the table by five for no
/// extra insight, since nothing on this screen is read at second resolution. The minute is
/// averaged in memory and written once, so a poller never waits on a disk write and a busy
/// server costs the same as an idle one.
/// </remarks>
public sealed class MetricRecorder(IServiceScopeFactory scopes, ILogger<MetricRecorder> logger)
    : BackgroundService, IMetricSink
{
    private sealed class Bucket
    {
        public readonly List<MetricPoint> Points = [];
    }

    private readonly ConcurrentDictionary<(string Module, string Target), Bucket> _buckets = new();

    public void Report(string moduleId, string targetId, MetricPoint point)
    {
        var bucket = _buckets.GetOrAdd((moduleId, targetId), static _ => new Bucket());
        lock (bucket)
        {
            bucket.Points.Add(point);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        while (await SafeWaitAsync(timer, stoppingToken))
        {
            try
            {
                await FlushAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // History is worth less than the live panel: never let a failed write take the
                // process down, but say so, because silence would hide an empty reports screen.
                logger.LogWarning(ex, "Ölçüm geçmişi yazılamadı.");
            }
        }
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

    private async Task FlushAsync(CancellationToken ct)
    {
        var minute = new DateTime(
            DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute * TimeSpan.TicksPerMinute,
            DateTimeKind.Utc);

        var rows = new List<MetricSample>();

        foreach (var ((moduleId, targetId), bucket) in _buckets)
        {
            List<MetricPoint> points;
            lock (bucket)
            {
                if (bucket.Points.Count == 0) continue;
                points = [.. bucket.Points];
                bucket.Points.Clear();
            }

            var worst = points
                .Where(p => p.LongestQuerySeconds is not null)
                .MaxBy(p => p.LongestQuerySeconds);

            rows.Add(new MetricSample
            {
                ModuleId = moduleId,
                TargetId = targetId,
                TakenAtUtc = minute,
                Resolution = MetricResolution.Minute,
                SampleCount = points.Count,
                // Averages, not the last reading: a spike that lasted two of the five cycles
                // is part of that minute, and taking the last one would erase it at random.
                CpuPercent = Average(points, p => p.CpuPercent),
                SqlCpuPercent = Average(points, p => p.SqlCpuPercent),
                MemoryPercent = Average(points, p => p.MemoryPercent),
                SqlMemoryMb = AverageInt(points, p => p.SqlMemoryMb),
                SessionCount = AverageInt(points, p => p.SessionCount),
                RequestCount = AverageInt(points, p => p.RequestCount),
                BlockedCount = AverageInt(points, p => p.BlockedCount),
                // The worst query of the minute, not the average of them: this one is asked
                // as "how bad did it get?". Its owner and statement travel with it, so the
                // report can answer "who?" as well as "how bad?".
                LongestQuerySeconds = worst?.LongestQuerySeconds,
                LongestQueryBy = worst?.LongestQueryBy,
                LongestQueryText = worst?.LongestQueryText
            });
        }

        if (rows.Count == 0) return;

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Set<MetricSample>().AddRange(rows);
        await db.SaveChangesAsync(ct);
    }

    private static double? Average(List<MetricPoint> points, Func<MetricPoint, double?> select)
    {
        var values = points.Select(select).Where(v => v is not null).Select(v => v!.Value).ToList();
        return values.Count == 0 ? null : Math.Round(values.Average(), 2);
    }

    private static int? AverageInt(List<MetricPoint> points, Func<MetricPoint, int?> select)
    {
        var values = points.Select(select).Where(v => v is not null).Select(v => v!.Value).ToList();
        return values.Count == 0 ? null : (int)Math.Round(values.Average());
    }
}

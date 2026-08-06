using Microsoft.EntityFrameworkCore;
using MssqlRealtime.Core.Alerts;

namespace MssqlRealtime.Infrastructure.Persistence;

public sealed class EfAlertStore(AppDbContext db) : IAlertStore
{
    public async Task RecordAsync(AlertNotification notification, CancellationToken ct = default)
    {
        var alert = notification.Alert;

        // An alert is one row from raise to clear; escalations and renotifications update it
        // rather than adding noise to the history.
        var open = await db.AlertRecords
            .Where(r => r.ModuleId == alert.Target.ModuleId
                        && r.TargetId == alert.Target.TargetId
                        && r.RuleId == alert.RuleId
                        && r.ClearedAtUtc == null)
            .OrderByDescending(r => r.RaisedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (notification.IsCleared)
        {
            if (open is null)
            {
                return;
            }

            open.ClearedAtUtc = notification.RaisedAtUtc.UtcDateTime;
            await db.SaveChangesAsync(ct);
            return;
        }

        if (open is null)
        {
            db.AlertRecords.Add(new AlertRecord
            {
                ModuleId = alert.Target.ModuleId,
                TargetId = alert.Target.TargetId,
                TargetName = alert.Target.TargetName,
                GroupName = alert.Target.GroupName,
                RuleId = alert.RuleId,
                RuleTitle = alert.RuleTitle,
                Severity = alert.Severity,
                Message = Truncate(alert.Message, 1000),
                Value = alert.Value,
                Threshold = alert.Threshold,
                Unit = alert.Unit,
                Context = alert.Context is null ? null : Truncate(alert.Context, 400),
                RaisedAtUtc = alert.SinceUtc.UtcDateTime,
                LastNotifiedUtc = alert.LastNotifiedUtc?.UtcDateTime
            });
        }
        else
        {
            open.Severity = alert.Severity;
            open.Message = Truncate(alert.Message, 1000);
            open.Value = alert.Value;
            // The first capture is the one that matters: it names who caused the alert, not
            // who happens to be busiest now that it has been firing for an hour.
            if (open.Context is null && alert.Context is not null)
            {
                open.Context = Truncate(alert.Context, 400);
            }
            open.TargetName = alert.Target.TargetName;
            open.LastNotifiedUtc = alert.LastNotifiedUtc?.UtcDateTime;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AlertState>> GetActiveAsync(CancellationToken ct = default)
    {
        var records = await db.AlertRecords
            .AsNoTracking()
            .Where(r => r.ClearedAtUtc == null)
            .ToListAsync(ct);

        return records.Select(r => new AlertState
        {
            Target = new AlertTarget
            {
                ModuleId = r.ModuleId,
                TargetId = r.TargetId,
                TargetName = r.TargetName,
                GroupName = r.GroupName
            },
            RuleId = r.RuleId,
            RuleTitle = r.RuleTitle,
            Severity = r.Severity,
            Message = r.Message,
            Value = r.Value,
            Threshold = r.Threshold,
            Unit = r.Unit,
            Context = r.Context,
            SinceUtc = new DateTimeOffset(r.RaisedAtUtc, TimeSpan.Zero),
            LastNotifiedUtc = r.LastNotifiedUtc is null ? null : new DateTimeOffset(r.LastNotifiedUtc.Value, TimeSpan.Zero)
        }).ToList();
    }

    public async Task<IReadOnlyList<AlertHistoryEntry>> GetHistoryAsync(int limit = 100, CancellationToken ct = default)
    {
        var records = await db.AlertRecords
            .AsNoTracking()
            .OrderByDescending(r => r.RaisedAtUtc)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(ct);

        return records.Select(r => new AlertHistoryEntry
        {
            Id = r.Id,
            ModuleId = r.ModuleId,
            TargetId = r.TargetId,
            TargetName = r.TargetName,
            GroupName = r.GroupName,
            RuleId = r.RuleId,
            RuleTitle = r.RuleTitle,
            Severity = r.Severity,
            Message = r.Message,
            Value = r.Value,
            Threshold = r.Threshold,
            Unit = r.Unit,
            Context = r.Context,
            RaisedAtUtc = new DateTimeOffset(r.RaisedAtUtc, TimeSpan.Zero),
            ClearedAtUtc = r.ClearedAtUtc is null ? null : new DateTimeOffset(r.ClearedAtUtc.Value, TimeSpan.Zero)
        }).ToList();
    }

    public async Task<int> PruneAsync(TimeSpan retention, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - retention;

        // Only closed alerts are pruned: something still firing stays, however old it is.
        return await db.AlertRecords
            .Where(r => r.ClearedAtUtc != null && r.ClearedAtUtc < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}

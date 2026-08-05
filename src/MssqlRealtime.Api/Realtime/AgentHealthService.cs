using Microsoft.EntityFrameworkCore;
using MssqlRealtime.Core.Agents;
using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Infrastructure.Persistence;
using MssqlRealtime.Modules.Mssql.Models;

namespace MssqlRealtime.Api.Realtime;

/// <summary>
/// Watches the agents themselves.
/// <para>
/// Without this, an agent going offline is the most dangerous failure the product has: its
/// servers simply stop reporting, no alert fires, and the silence reads exactly like "nothing
/// is wrong". A monitoring tool that goes quiet must say so.
/// </para>
/// </summary>
public sealed class AgentHealthService(
    IServiceScopeFactory scopeFactory,
    IAgentRegistry registry,
    IAlertEngine alerts,
    IAlertSink alertSink,
    IConfiguration configuration,
    ILogger<AgentHealthService> logger) : BackgroundService
{
    /// <summary>Module id used for platform-level alerts that belong to no tool.</summary>
    public const string PlatformModuleId = "platform";

    public const string SilentRuleId = "agent-silent";

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    private TimeSpan OfflineAfter => TimeSpan.FromMinutes(
        Math.Clamp(configuration.GetValue("Agents:OfflineAfterMinutes", 3), 1, 1440));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give agents a chance to reconnect after a hub restart before declaring them silent.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        logger.LogInformation("Agent health service started (offline after {Minutes} min)", OfflineAfter.TotalMinutes);

        using var timer = new PeriodicTimer(CheckInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAsync(stoppingToken);

                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Agent health check failed");
            }
        }
    }

    private async Task CheckAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var agents = await db.Agents.AsNoTracking().Where(a => a.Enabled).ToListAsync(ct);
        if (agents.Count == 0)
        {
            return;
        }

        var assignedCounts = await db.Set<ServerProfile>()
            .AsNoTracking()
            .Where(p => p.AgentId != null && p.Enabled)
            .GroupBy(p => p.AgentId!.Value)
            .Select(g => new { AgentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AgentId, x => x.Count, ct);

        var now = DateTimeOffset.UtcNow;

        foreach (var agent in agents)
        {
            var input = new AgentHealthInput
            {
                AgentId = agent.Id,
                Name = agent.Name,
                MachineName = agent.MachineName,
                IsConnected = registry.IsConnected(agent.Id),
                FirstConnectedUtc = agent.FirstConnectedUtc is null
                    ? null
                    : new DateTimeOffset(agent.FirstConnectedUtc.Value, TimeSpan.Zero),
                LastSeenUtc = agent.LastSeenUtc is null
                    ? null
                    : new DateTimeOffset(agent.LastSeenUtc.Value, TimeSpan.Zero),
                AssignedTargets = assignedCounts.GetValueOrDefault(agent.Id)
            };

            var candidate = AgentHealthEvaluator.Evaluate(input, OfflineAfter, now);

            if (candidate is null)
            {
                // Not evaluated (pending install, or nothing assigned): drop any stale state
                // so a later assignment starts from a clean slate.
                alerts.Forget(PlatformModuleId, agent.Id.ToString("N"));
                continue;
            }

            var target = new AlertTarget
            {
                ModuleId = PlatformModuleId,
                TargetId = agent.Id.ToString("N"),
                TargetName = agent.Name,
                GroupName = agent.MachineName
            };

            var outcome = alerts.Evaluate(target, [candidate], now);

            foreach (var notification in outcome.ToNotify)
            {
                logger.LogWarning(
                    "Agent alert {State}: {Message}",
                    notification.IsCleared ? "cleared" : "raised",
                    notification.Body);

                await alertSink.PublishAsync(notification, ct);
            }
        }
    }
}

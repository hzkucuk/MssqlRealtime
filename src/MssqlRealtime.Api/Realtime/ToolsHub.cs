using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using MssqlRealtime.Core.Abstractions;
using MssqlRealtime.Core.Alerts;

namespace MssqlRealtime.Api.Realtime;

/// <summary>
/// The single real-time endpoint for every tool. Clients subscribe to a module, and
/// optionally to one target inside it, so a phone showing one customer's server does not
/// pay for the traffic of twenty others.
/// </summary>
[Authorize]
public sealed class ToolsHub : Hub
{
    public const string Path = "/hubs/tools";

    /// <summary>Everyone signed in is in this group; alerts go here.</summary>
    public const string AlertsGroup = "alerts";

    public static string ModuleGroup(string moduleId) => $"module:{moduleId}";
    public static string TargetGroup(string moduleId, string targetId) => $"target:{moduleId}:{targetId}";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, AlertsGroup);
        await base.OnConnectedAsync();
    }

    /// <summary>Start receiving a module's broadcasts (e.g. every server's summary).</summary>
    public Task SubscribeModule(string moduleId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, ModuleGroup(moduleId));

    public Task UnsubscribeModule(string moduleId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, ModuleGroup(moduleId));

    /// <summary>Start receiving one target's detail stream (e.g. a single server's sessions).</summary>
    public Task SubscribeTarget(string moduleId, string targetId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, TargetGroup(moduleId, targetId));

    public Task UnsubscribeTarget(string moduleId, string targetId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, TargetGroup(moduleId, targetId));
}

/// <summary>SignalR implementation of the transport modules publish through.</summary>
public sealed class SignalRPublisher(IHubContext<ToolsHub> hub) : IRealtimePublisher
{
    public Task PublishAsync<T>(string moduleId, string? targetId, string eventName, T payload, CancellationToken ct = default)
    {
        var envelope = new
        {
            moduleId,
            targetId,
            @event = eventName,
            payload,
            sentAt = DateTimeOffset.UtcNow
        };

        // Module subscribers get everything; target subscribers get only their own target.
        // A client subscribed to both is in two groups, so SignalR delivers once per group —
        // clients de-duplicate on (moduleId, targetId, sentAt).
        var moduleTask = hub.Clients.Group(ToolsHub.ModuleGroup(moduleId))
            .SendAsync("moduleEvent", envelope, ct);

        if (targetId is null)
        {
            return moduleTask;
        }

        var targetTask = hub.Clients.Group(ToolsHub.TargetGroup(moduleId, targetId))
            .SendAsync("moduleEvent", envelope, ct);

        return Task.WhenAll(moduleTask, targetTask);
    }

    public Task PublishAlertAsync(AlertNotification notification, CancellationToken ct = default) =>
        hub.Clients.Group(ToolsHub.AlertsGroup).SendAsync("alert", notification, ct);
}

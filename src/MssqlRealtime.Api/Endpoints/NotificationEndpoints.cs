using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Core.Notifications;
using MssqlRealtime.Infrastructure.Notifications;

namespace MssqlRealtime.Api.Endpoints;

/// <summary>
/// Notification channels and alert history, configurable entirely from the phone.
/// </summary>
public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").RequireAuthorization();

        // Channel list with their field definitions, so the settings form is generated from
        // the server: a channel added later needs no client release.
        group.MapGet("/channels", async (
            IEnumerable<INotificationChannel> channels,
            INotificationSettingsStore store,
            CancellationToken ct) =>
        {
            var configurations = await store.GetAllAsync(ct);

            var result = channels.Select(channel =>
            {
                var configuration = configurations.FirstOrDefault(c =>
                    string.Equals(c.ChannelId, channel.Id, StringComparison.OrdinalIgnoreCase));

                return new
                {
                    id = channel.Id,
                    title = channel.Title,
                    enabled = configuration?.Enabled ?? false,
                    minimumSeverity = (int)(configuration?.MinimumSeverity ?? Severity.Warning),
                    sendRecoveries = configuration?.SendRecoveries ?? true,
                    fields = channel.Fields.Select(f => new
                    {
                        key = f.Key,
                        label = f.Label,
                        isSecret = f.IsSecret,
                        isRequired = f.IsRequired,
                        placeholder = f.Placeholder,
                        help = f.Help,
                        // Secrets are never returned; the client only learns one is stored.
                        value = f.IsSecret ? null : configuration?.Settings.Get(f.Key),
                        hasValue = configuration?.Settings.Get(f.Key) is not null
                    })
                };
            });

            return Results.Ok(result);
        });

        group.MapPut("/channels/{channelId}", async (
            string channelId,
            ChannelUpdateRequest request,
            IEnumerable<INotificationChannel> channels,
            INotificationSettingsStore store,
            CancellationToken ct) =>
        {
            var channel = channels.FirstOrDefault(c =>
                string.Equals(c.Id, channelId, StringComparison.OrdinalIgnoreCase));

            if (channel is null)
            {
                return Results.NotFound(new { error = "Bilinmeyen bildirim kanalı." });
            }

            await store.SaveAsync(
                channel.Id,
                request.Enabled,
                (Severity)Math.Clamp(request.MinimumSeverity, 0, 2),
                request.SendRecoveries,
                request.Values ?? new Dictionary<string, string?>(),
                channel.Fields,
                ct);

            return Results.Ok(new { ok = true });
        });

        // Find out the token is wrong now, not during an incident at 03:00.
        group.MapPost("/channels/{channelId}/test", async (
            string channelId,
            INotificationDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendTestAsync(channelId, ct);

            return result.IsSuccess
                ? Results.Ok(new { ok = true })
                : Results.BadRequest(new { ok = false, error = result.Error, code = result.Code });
        });

        // What happened while the app was closed.
        app.MapGet("/api/alerts", async (int? limit, IAlertStore store, CancellationToken ct) =>
                Results.Ok(await store.GetHistoryAsync(limit ?? 100, ct)))
            .RequireAuthorization();

        app.MapGet("/api/alerts/active", (IAlertEngine engine) => Results.Ok(engine.GetActive()))
            .RequireAuthorization();

        return app;
    }
}

public sealed record ChannelUpdateRequest
{
    public bool Enabled { get; init; }
    public int MinimumSeverity { get; init; } = (int)Severity.Warning;
    public bool SendRecoveries { get; init; } = true;

    /// <summary>
    /// Only the keys present are written. Omitting a secret keeps the stored one; sending an
    /// empty string clears it.
    /// </summary>
    public Dictionary<string, string?>? Values { get; init; }
}

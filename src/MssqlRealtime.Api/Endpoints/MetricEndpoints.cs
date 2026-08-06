using MssqlRealtime.Core.Abstractions;

namespace MssqlRealtime.Api.Endpoints;

/// <summary>
/// The reports screen reads history here. Platform-level rather than per-module: the numbers
/// are stored the same way whatever measured them, and a second tool gets its charts for free.
/// </summary>
public static class MetricEndpoints
{
    public static void MapMetricEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/metrics/{moduleId}/{targetId}", async (
            string moduleId,
            string targetId,
            string? aralik,
            IMetricStore store,
            CancellationToken ct) =>
        {
            var range = aralik?.ToLowerInvariant() switch
            {
                "hafta" => MetricRange.Week,
                "ay" => MetricRange.Month,
                "yil" => MetricRange.Year,
                _ => MetricRange.Day
            };

            var points = await store.ReadAsync(moduleId, targetId, range, ct);
            return Results.Ok(new { range = range.ToString().ToLowerInvariant(), points });
        }).RequireAuthorization();
    }
}

using MssqlRealtime.Api.Setup;

namespace MssqlRealtime.Api.Endpoints;

/// <summary>
/// Panelin kendi güncellemesi. Elle tetiklenir — zamanlanmış bir iş <b>yok</b>: bozuk bir
/// sürüm izlemeyi sessizce körleştirebilir, ne zaman güncelleneceğine operatör karar verir.
/// </summary>
public static class UpdateEndpoints
{
    public static void MapUpdateEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/update", async (UpdateService service, CancellationToken ct) =>
        {
            var info = await service.CheckAsync(ct);
            return Results.Ok(new
            {
                current = info.Current,
                latest = info.Latest,
                available = info.Available,
                supported = info.Supported,
                canRollback = info.CanRollback,
                size = info.Setup?.Size ?? 0,
                notes = info.Notes,
                error = info.Error
            });
        }).RequireAuthorization();

        routes.MapPost("/api/update", async (UpdateService service, CancellationToken ct) =>
        {
            var r = await service.ApplyAsync(ct);
            return r.IsSuccess
                ? Results.Ok(new { started = true, version = r.Value })
                : Results.BadRequest(new { error = r.Error, code = r.Code });
        }).RequireAuthorization();
    }
}

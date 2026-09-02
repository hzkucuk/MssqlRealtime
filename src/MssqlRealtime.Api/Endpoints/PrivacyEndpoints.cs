using MssqlRealtime.Core.Privacy;

namespace MssqlRealtime.Api.Endpoints;

/// <summary>
/// The one privacy decision the panel owner makes: how much of a query may be written to disk.
/// Platform-level, like the reports it governs — every module that captures statement text
/// obeys the same setting.
/// </summary>
public static class PrivacyEndpoints
{
    public static void MapPrivacyEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/gizlilik").RequireAuthorization();

        group.MapGet("/", (IStatementPrivacy privacy) =>
            Results.Ok(new { sorguMetni = Name(privacy.Storage) }));

        group.MapPut("/", async (
            PrivacyUpdateRequest request,
            IStatementPrivacy privacy,
            CancellationToken ct) =>
        {
            if (Parse(request.SorguMetni) is not { } storage)
            {
                return Results.BadRequest(new
                {
                    error = "Geçersiz değer. Beklenen: maskeli, tam ya da kapali."
                });
            }

            await privacy.SaveAsync(storage, ct);
            return Results.Ok(new { sorguMetni = Name(storage) });
        });
    }

    private static string Name(StatementStorage storage) => storage switch
    {
        StatementStorage.Full => "tam",
        StatementStorage.None => "kapali",
        _ => "maskeli"
    };

    private static StatementStorage? Parse(string? value) => value switch
    {
        "maskeli" => StatementStorage.Masked,
        "tam" => StatementStorage.Full,
        "kapali" => StatementStorage.None,
        // Unlike the store's reader, an API caller sending nonsense is told so rather than
        // quietly given the safe default: a client that thinks it turned masking off should
        // not believe it succeeded.
        _ => null
    };
}

public sealed record PrivacyUpdateRequest(string? SorguMetni);

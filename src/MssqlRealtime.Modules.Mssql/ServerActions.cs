using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using MssqlRealtime.Core.Common;
using MssqlRealtime.Modules.Mssql.Models;
using MssqlRealtime.Modules.Mssql.Polling;

namespace MssqlRealtime.Modules.Mssql;

public sealed class ServerActions(
    IServerProfileStore store,
    IConnectionStringFactory connectionStrings,
    ServerPoller poller,
    ILogger<ServerActions> logger) : IServerActions
{
    /// <summary>
    /// Verifies a profile before it is saved: can we connect, and do we have the permission
    /// the probes actually need? Reporting "connected" and then showing empty screens is worse
    /// than failing here.
    /// </summary>
    public async Task<Result<ServerSnapshot>> TestConnectionAsync(ServerProfile profile, CancellationToken ct = default)
    {
        var connectionString = connectionStrings.Build(profile, "MssqlRealtime-Test");
        if (connectionString.IsFailure)
        {
            return Result<ServerSnapshot>.Failure(connectionString.Error!, connectionString.Code);
        }

        try
        {
            await using var connection = new SqlConnection(connectionString.Value);
            await connection.OpenAsync(ct);

            var hasViewServerState = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    "SELECT CONVERT(int, HAS_PERMS_BY_NAME(NULL, NULL, 'VIEW SERVER STATE'));",
                    commandTimeout: profile.CommandTimeoutSeconds,
                    cancellationToken: ct));

            if (hasViewServerState != 1)
            {
                return Result<ServerSnapshot>.Failure(
                    "Bağlantı kuruldu ama bu kullanıcıda VIEW SERVER STATE izni yok. "
                    + $"Sunucuda çalıştırın: GRANT VIEW SERVER STATE TO [{profile.Username ?? "kullanıcı"}]",
                    "missing_permission");
            }
        }
        catch (SqlException ex)
        {
            return Result<ServerSnapshot>.Failure(ServerPoller.DescribeSqlError(ex), $"sql_{ex.Number}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Connection test failed for {Host}", profile.Host);
            return Result<ServerSnapshot>.Failure(ex.Message, "unexpected");
        }

        // Connection and permission are good — return a real snapshot so the user sees data
        // immediately rather than a green tick they have to trust.
        var snapshot = await poller.PollAsync(profile, pollNumber: 0, ct);
        return snapshot.Status == ServerStatus.Online
            ? Result<ServerSnapshot>.Success(snapshot)
            : Result<ServerSnapshot>.Failure(snapshot.ErrorMessage ?? "Bağlantı doğrulanamadı.", "probe_failed");
    }

    public async Task<Result> KillSessionAsync(Guid serverId, int sessionId, CancellationToken ct = default)
    {
        // System sessions are not ours to kill, and KILL takes no parameters — so the id is
        // validated as an integer in range before it is ever put into the command text.
        if (sessionId <= 50)
        {
            return Result.Failure("Sistem oturumları (session_id ≤ 50) sonlandırılamaz.", "system_session");
        }

        var profile = await store.GetAsync(serverId, ct);
        if (profile is null)
        {
            return Result.Failure("Sunucu profili bulunamadı.", "not_found");
        }

        var connectionString = connectionStrings.Build(profile, "MssqlRealtime-Action");
        if (connectionString.IsFailure)
        {
            return Result.Failure(connectionString.Error!, connectionString.Code);
        }

        try
        {
            await using var connection = new SqlConnection(connectionString.Value);
            await connection.OpenAsync(ct);

            var command = "KILL " + sessionId.ToString(CultureInfo.InvariantCulture);
            await connection.ExecuteAsync(new CommandDefinition(
                command, commandTimeout: profile.CommandTimeoutSeconds, cancellationToken: ct));

            // Audit trail: a destructive action against a customer's production server.
            logger.LogWarning(
                "KILL {SessionId} executed on {Server} ({Host})",
                sessionId, profile.Name, profile.Host);

            return Result.Success();
        }
        catch (SqlException ex)
        {
            return Result.Failure(ServerPoller.DescribeSqlError(ex), $"sql_{ex.Number}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "KILL {SessionId} failed on {Server}", sessionId, profile.Name);
            return Result.Failure(ex.Message, "unexpected");
        }
    }
}

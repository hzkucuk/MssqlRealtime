using Microsoft.Data.SqlClient;
using MssqlRealtime.Core.Abstractions;
using MssqlRealtime.Core.Common;
using MssqlRealtime.Modules.Mssql.Models;

namespace MssqlRealtime.Modules.Mssql;

/// <summary>
/// The only place a credential is turned into a connection string. Everything the poller
/// opens is read-only by intent and tagged with an application name so the DBA on the other
/// side can see who is connecting in their own sys.dm_exec_sessions.
/// </summary>
public sealed class ConnectionStringFactory(ISecretProtector protector) : IConnectionStringFactory
{
    public const string DefaultApplicationName = "MssqlRealtime";

    public Result<string> Build(ServerProfile profile, string? applicationName = null)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = profile.Port == 1433 ? profile.Host : $"{profile.Host},{profile.Port}",
            InitialCatalog = string.IsNullOrWhiteSpace(profile.InitialCatalog) ? "master" : profile.InitialCatalog,
            Encrypt = profile.EncryptConnection,
            TrustServerCertificate = profile.TrustServerCertificate,
            ConnectTimeout = profile.ConnectTimeoutSeconds,
            ApplicationName = applicationName ?? DefaultApplicationName,
            // A dead monitoring connection must not be handed out again.
            Pooling = true,
            MaxPoolSize = 5,
            MultipleActiveResultSets = false
        };

        if (profile.UsesIntegratedAuth)
        {
            builder.IntegratedSecurity = true;
            return Result<string>.Success(builder.ConnectionString);
        }

        if (string.IsNullOrWhiteSpace(profile.Username))
        {
            return Result<string>.Failure("SQL girişi seçildi ama kullanıcı adı boş.", "username_required");
        }

        if (string.IsNullOrEmpty(profile.ProtectedPassword))
        {
            return Result<string>.Failure("SQL girişi seçildi ama parola kayıtlı değil.", "password_required");
        }

        var password = protector.Unprotect(profile.ProtectedPassword);
        if (password.IsFailure)
        {
            return Result<string>.Failure(password.Error!, password.Code);
        }

        builder.UserID = profile.Username;
        builder.Password = password.Value;
        return Result<string>.Success(builder.ConnectionString);
    }
}

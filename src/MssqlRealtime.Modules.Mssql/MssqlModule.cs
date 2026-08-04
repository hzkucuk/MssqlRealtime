using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MssqlRealtime.Core.Abstractions;
using MssqlRealtime.Core.Modularity;
using MssqlRealtime.Modules.Mssql.Models;
using MssqlRealtime.Modules.Mssql.Polling;
using MssqlRealtime.Modules.Mssql.Probes;

namespace MssqlRealtime.Modules.Mssql;

/// <summary>
/// Live MSSQL monitoring: who is connected, what is running, what is blocking, and how the
/// machine itself is doing — for any number of customer servers at once.
/// </summary>
public sealed class MssqlModule : IToolModule
{
    public const string ModuleId = "mssql";

    public string Id => ModuleId;
    public string Title => "MSSQL İzleme";
    public string Icon => "🗄️";
    public int Order => 10;
    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IServerProfileStore, EfServerProfileStore>();
        services.AddScoped<IServerActions, ServerActions>();

        services.AddSingleton<IConnectionStringFactory, ConnectionStringFactory>();
        services.AddSingleton<ISnapshotCache, SnapshotCache>();
        services.AddSingleton<ServerPoller>();

        // Probes are singletons: some of them (wait stats) keep a per-server baseline
        // between polls, which is the whole reason their numbers mean anything.
        services.AddSingleton<ISqlProbe, InstanceProbe>();
        services.AddSingleton<ISqlProbe, ResourcesProbe>();
        services.AddSingleton<ISqlProbe, SessionsProbe>();
        services.AddSingleton<ISqlProbe, RequestsProbe>();
        services.AddSingleton<ISqlProbe, BlockingProbe>();
        services.AddSingleton<ISqlProbe, WaitStatsProbe>();
        services.AddSingleton<ISqlProbe, DatabasesProbe>();
        services.AddSingleton<ISqlProbe, ServicesProbe>();

        services.AddHostedService<MssqlPollingService>();
    }

    public void ConfigureDbModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ServerProfile>(e =>
        {
            e.ToTable("MssqlServerProfiles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Host).HasMaxLength(255).IsRequired();
            e.Property(x => x.InitialCatalog).HasMaxLength(128).IsRequired();
            e.Property(x => x.Username).HasMaxLength(128);
            e.Property(x => x.ProtectedPassword).HasMaxLength(4000);
            e.Property(x => x.AuthMode).HasConversion<int>();
            e.HasIndex(x => x.CustomerName);
        });
    }

    public ToolDescriptor Describe() => new()
    {
        Id = Id,
        Title = Title,
        Icon = Icon,
        Order = Order,
        Version = Version,
        Description = "Oturumlar, bloke kayıtlar, işlemci ve bellek — çoklu sunucu, canlı.",
        Capabilities = ["targets", "alerts", "actions", "realtime"]
    };

    public void MapEndpoints(IEndpointRouteBuilder routes)
    {
        // --- Live state -------------------------------------------------------------
        // Served from cache so a phone that just connected paints instantly instead of
        // waiting a whole poll interval for the first push.
        routes.MapGet("/snapshots", (ISnapshotCache cache) => Results.Ok(cache.GetAll()));

        routes.MapGet("/snapshots/{id:guid}", (Guid id, ISnapshotCache cache) =>
            cache.Get(id) is { } snapshot
                ? Results.Ok(snapshot)
                : Results.NotFound(new { error = "Bu sunucu için henüz veri yok." }));

        // --- Server profiles --------------------------------------------------------
        routes.MapGet("/servers", async (IServerProfileStore store, CancellationToken ct) =>
        {
            var profiles = await store.GetAllAsync(ct);
            return Results.Ok(profiles.Select(ServerProfileDto.From));
        });

        routes.MapGet("/servers/{id:guid}", async (Guid id, IServerProfileStore store, CancellationToken ct) =>
        {
            var profile = await store.GetAsync(id, ct);
            return profile is null
                ? Results.NotFound(new { error = "Sunucu profili bulunamadı." })
                : Results.Ok(ServerProfileDto.From(profile));
        });

        routes.MapPost("/servers", async (
            ServerProfileRequest request,
            IServerProfileStore store,
            ISecretProtector protector,
            CancellationToken ct) =>
        {
            var errors = request.Validate();
            if (errors.Count > 0)
            {
                return Results.BadRequest(new { errors });
            }

            var profile = new ServerProfile();
            Apply(request, profile, protector);

            await store.AddAsync(profile, ct);
            return Results.Created($"/api/modules/{ModuleId}/servers/{profile.Id}", ServerProfileDto.From(profile));
        });

        routes.MapPut("/servers/{id:guid}", async (
            Guid id,
            ServerProfileRequest request,
            IServerProfileStore store,
            ISecretProtector protector,
            CancellationToken ct) =>
        {
            var errors = request.Validate();
            if (errors.Count > 0)
            {
                return Results.BadRequest(new { errors });
            }

            var profile = await store.GetAsync(id, ct);
            if (profile is null)
            {
                return Results.NotFound(new { error = "Sunucu profili bulunamadı." });
            }

            Apply(request, profile, protector);

            var result = await store.UpdateAsync(profile, ct);
            return result.IsSuccess
                ? Results.Ok(ServerProfileDto.From(profile))
                : Results.BadRequest(new { error = result.Error });
        });

        routes.MapDelete("/servers/{id:guid}", async (Guid id, IServerProfileStore store, CancellationToken ct) =>
        {
            var result = await store.DeleteAsync(id, ct);
            return result.IsSuccess
                ? Results.NoContent()
                : Results.NotFound(new { error = result.Error });
        });

        // --- Actions ----------------------------------------------------------------
        routes.MapPost("/servers/test", async (
            ServerProfileRequest request,
            IServerProfileStore store,
            ISecretProtector protector,
            IServerActions actions,
            CancellationToken ct) =>
        {
            var errors = request.Validate();
            if (errors.Count > 0)
            {
                return Results.BadRequest(new { errors });
            }

            // Testing an existing server without retyping its password: start from the stored profile.
            var profile = new ServerProfile();
            if (request.Password is null)
            {
                var existing = await store.GetAllAsync(ct);
                var match = existing.FirstOrDefault(p =>
                    string.Equals(p.Host, request.Host, StringComparison.OrdinalIgnoreCase)
                    && p.Port == request.Port
                    && string.Equals(p.Username, request.Username, StringComparison.OrdinalIgnoreCase));

                if (match is not null)
                {
                    profile.ProtectedPassword = match.ProtectedPassword;
                }
            }

            Apply(request, profile, protector);

            var result = await actions.TestConnectionAsync(profile, ct);
            return result.IsSuccess
                ? Results.Ok(new { ok = true, snapshot = result.Value })
                : Results.BadRequest(new { ok = false, error = result.Error, code = result.Code });
        });

        routes.MapPost("/servers/{id:guid}/kill", async (
            Guid id,
            KillSessionRequest request,
            IServerActions actions,
            CancellationToken ct) =>
        {
            var result = await actions.KillSessionAsync(id, request.SessionId, ct);
            return result.IsSuccess
                ? Results.Ok(new { ok = true })
                : Results.BadRequest(new { ok = false, error = result.Error, code = result.Code });
        });
    }

    private static void Apply(ServerProfileRequest request, ServerProfile profile, ISecretProtector protector)
    {
        profile.Name = request.Name.Trim();
        profile.CustomerName = request.CustomerName.Trim();
        profile.Host = request.Host.Trim();
        profile.Port = request.Port;
        profile.InitialCatalog = string.IsNullOrWhiteSpace(request.InitialCatalog) ? "master" : request.InitialCatalog.Trim();
        profile.AuthMode = request.AuthMode;
        profile.Username = request.Username?.Trim();
        profile.EncryptConnection = request.EncryptConnection;
        profile.TrustServerCertificate = request.TrustServerCertificate;
        profile.ConnectTimeoutSeconds = request.ConnectTimeoutSeconds;
        profile.CommandTimeoutSeconds = request.CommandTimeoutSeconds;
        profile.Enabled = request.Enabled;
        profile.PollIntervalSeconds = request.PollIntervalSeconds;

        // An omitted password means "keep the stored one"; an empty string means "clear it".
        if (request.Password is not null)
        {
            profile.ProtectedPassword = request.Password.Length == 0
                ? null
                : protector.Protect(request.Password);
        }

        request.Thresholds?.ApplyTo(profile);
    }
}

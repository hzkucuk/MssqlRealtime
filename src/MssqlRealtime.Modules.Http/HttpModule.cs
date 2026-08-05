using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MssqlRealtime.Core.Modularity;
using MssqlRealtime.Modules.Http.Models;

namespace MssqlRealtime.Modules.Http;

/// <summary>
/// Uptime monitoring for HTTP endpoints: is the customer's site answering, how fast, and when
/// does its TLS certificate expire.
/// <para>
/// This module exists partly to prove the platform's claim: it was added without touching the
/// alert engine, the notification channels, the hub or the host — one registration line and a
/// front-end folder. Everything else came for free.
/// </para>
/// </summary>
public sealed class HttpModule : IToolModule
{
    public const string ModuleId = "http";

    public string Id => ModuleId;
    public string Title => "Site / API İzleme";
    public string Icon => "🌐";
    public int Order => 20;
    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<HttpChecker>();
        services.AddSingleton<HttpResultCache>();
        services.AddHostedService<HttpMonitorService>();

        services.AddHttpClient(HttpChecker.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => HttpChecker.CreateHandler(ignoreCertificateErrors: false));

        services.AddHttpClient(HttpChecker.InsecureClientName)
            .ConfigurePrimaryHttpMessageHandler(() => HttpChecker.CreateHandler(ignoreCertificateErrors: true));
    }

    public void ConfigureDbModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HttpTarget>(e =>
        {
            e.ToTable("HttpTargets");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.GroupName).HasMaxLength(200);
            e.Property(x => x.Url).HasMaxLength(2000).IsRequired();
            e.Property(x => x.Method).HasMaxLength(10);
            e.Property(x => x.ExpectedBodyContains).HasMaxLength(400);
            e.HasIndex(x => x.GroupName);
        });
    }

    public ToolDescriptor Describe() => new()
    {
        Id = Id,
        Title = Title,
        Icon = Icon,
        Order = Order,
        Version = Version,
        Description = "Site ve API ayakta mı, ne kadar hızlı, sertifikası ne zaman bitiyor.",
        Capabilities = ["targets", "alerts", "realtime"]
    };

    public void MapEndpoints(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/checks", (HttpResultCache cache) => Results.Ok(cache.GetAll()));

        routes.MapGet("/checks/{id:guid}", (Guid id, HttpResultCache cache) =>
            cache.Get(id) is { } result
                ? Results.Ok(result)
                : Results.NotFound(new { error = "Bu hedef için henüz ölçüm yok." }));

        routes.MapGet("/targets", async (DbContext db, CancellationToken ct) =>
            Results.Ok(await db.Set<HttpTarget>().AsNoTracking()
                .OrderBy(t => t.GroupName).ThenBy(t => t.Name)
                .ToListAsync(ct)));

        routes.MapPost("/targets", async (HttpTargetRequest request, DbContext db, CancellationToken ct) =>
        {
            var errors = request.Validate();
            if (errors.Count > 0)
            {
                return Results.BadRequest(new { errors });
            }

            var target = new HttpTarget();
            request.ApplyTo(target);

            db.Set<HttpTarget>().Add(target);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/modules/{ModuleId}/targets/{target.Id}", target);
        });

        routes.MapPut("/targets/{id:guid}", async (Guid id, HttpTargetRequest request, DbContext db, CancellationToken ct) =>
        {
            var errors = request.Validate();
            if (errors.Count > 0)
            {
                return Results.BadRequest(new { errors });
            }

            var target = await db.Set<HttpTarget>().FirstOrDefaultAsync(t => t.Id == id, ct);
            if (target is null)
            {
                return Results.NotFound(new { error = "Hedef bulunamadı." });
            }

            request.ApplyTo(target);
            target.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            return Results.Ok(target);
        });

        routes.MapDelete("/targets/{id:guid}", async (Guid id, DbContext db, CancellationToken ct) =>
        {
            var deleted = await db.Set<HttpTarget>().Where(t => t.Id == id).ExecuteDeleteAsync(ct);
            return deleted == 0
                ? Results.NotFound(new { error = "Hedef bulunamadı." })
                : Results.NoContent();
        });

        // Check an address before saving it, the same way the MSSQL module tests a connection.
        routes.MapPost("/targets/test", async (HttpTargetRequest request, HttpChecker checker, CancellationToken ct) =>
        {
            var errors = request.Validate();
            if (errors.Count > 0)
            {
                return Results.BadRequest(new { errors });
            }

            var target = new HttpTarget();
            request.ApplyTo(target);

            var result = await checker.CheckAsync(target, ct);
            var (days, subject) = await checker.InspectCertificateAsync(target, ct);

            return Results.Ok(new
            {
                ok = result.Status != HttpCheckStatus.Down,
                result = result with { CertificateDaysRemaining = days, CertificateSubject = subject }
            });
        });
    }
}

public sealed record HttpTargetRequest
{
    public string Name { get; init; } = string.Empty;
    public string GroupName { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Method { get; init; } = "GET";
    public int ExpectedStatusCode { get; init; }
    public string? ExpectedBodyContains { get; init; }
    public bool Enabled { get; init; } = true;
    public int CheckIntervalSeconds { get; init; } = 60;
    public int TimeoutSeconds { get; init; } = 10;
    public bool IgnoreCertificateErrors { get; init; }
    public bool AlertOnDown { get; init; } = true;
    public int? SlowResponseMs { get; init; } = 3000;
    public int? CertificateExpiryWarningDays { get; init; } = 14;
    public int AlertConsecutiveBreaches { get; init; } = 2;
    public int AlertRenotifyMinutes { get; init; } = 15;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Name)) errors.Add("Ad zorunlu.");

        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("Adres http:// veya https:// ile başlamalı.");
        }

        if (CheckIntervalSeconds is < 5 or > 86400) errors.Add("Kontrol aralığı 5-86400 sn olmalı.");
        if (TimeoutSeconds is < 1 or > 120) errors.Add("Zaman aşımı 1-120 sn olmalı.");
        if (ExpectedStatusCode != 0 && ExpectedStatusCode is < 100 or > 599) errors.Add("Durum kodu 100-599 aralığında olmalı.");

        return errors;
    }

    public void ApplyTo(HttpTarget target)
    {
        target.Name = Name.Trim();
        target.GroupName = GroupName.Trim();
        target.Url = Url.Trim();
        target.Method = string.IsNullOrWhiteSpace(Method) ? "GET" : Method.Trim().ToUpperInvariant();
        target.ExpectedStatusCode = ExpectedStatusCode;
        target.ExpectedBodyContains = string.IsNullOrWhiteSpace(ExpectedBodyContains) ? null : ExpectedBodyContains.Trim();
        target.Enabled = Enabled;
        target.CheckIntervalSeconds = CheckIntervalSeconds;
        target.TimeoutSeconds = TimeoutSeconds;
        target.IgnoreCertificateErrors = IgnoreCertificateErrors;
        target.AlertOnDown = AlertOnDown;
        target.SlowResponseMs = SlowResponseMs;
        target.CertificateExpiryWarningDays = CertificateExpiryWarningDays;
        target.AlertConsecutiveBreaches = Math.Clamp(AlertConsecutiveBreaches, 1, 60);
        target.AlertRenotifyMinutes = Math.Clamp(AlertRenotifyMinutes, 1, 1440);
    }
}

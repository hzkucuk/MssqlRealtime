using Microsoft.EntityFrameworkCore;
using MssqlRealtime.Core.Agents;
using MssqlRealtime.Infrastructure.Persistence;
using MssqlRealtime.Modules.Mssql.Models;

namespace MssqlRealtime.Api.Endpoints;

/// <summary>Agent management: issue a key, see who is connected, revoke.</summary>
public static class AgentEndpoints
{
    /// <summary>An agent quiet for longer than this is treated as offline in the UI.</summary>
    private static readonly TimeSpan OfflineAfter = TimeSpan.FromMinutes(2);

    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/agents").RequireAuthorization();

        group.MapGet("/", async (AppDbContext db, IAgentRegistry registry, CancellationToken ct) =>
        {
            var agents = await db.Agents.AsNoTracking().OrderBy(a => a.Name).ToListAsync(ct);

            var assignedCounts = await db.Set<ServerProfile>()
                .AsNoTracking()
                .Where(p => p.AgentId != null)
                .GroupBy(p => p.AgentId!.Value)
                .Select(g => new { AgentId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.AgentId, x => x.Count, ct);

            return Results.Ok(agents.Select(a => new AgentInfo
            {
                Id = a.Id,
                Name = a.Name,
                MachineName = a.MachineName,
                Version = a.Version,
                OperatingSystem = a.OperatingSystem,
                // Live connection state, with a grace window so a brief reconnect does not
                // flash the whole list red.
                IsConnected = registry.IsConnected(a.Id)
                              || (a.LastSeenUtc is { } seen && DateTime.UtcNow - seen < OfflineAfter),
                LastSeenUtc = a.LastSeenUtc is null ? null : new DateTimeOffset(a.LastSeenUtc.Value, TimeSpan.Zero),
                RegisteredAtUtc = new DateTimeOffset(a.CreatedAtUtc, TimeSpan.Zero),
                AssignedTargets = assignedCounts.GetValueOrDefault(a.Id)
            }));
        });

        // The key is returned exactly once, here. It is stored hashed, so it cannot be shown
        // again — an operator who loses it issues a new agent or rotates the key.
        group.MapPost("/", async (CreateAgentRequest request, AppDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { error = "Agent adı zorunlu." });
            }

            var key = AgentRecord.GenerateKey();
            var agent = new AgentRecord
            {
                Name = request.Name.Trim(),
                KeyHash = AgentRecord.Hash(key)
            };

            db.Agents.Add(agent);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                id = agent.Id,
                name = agent.Name,
                enrollmentKey = key,
                warning = "Bu anahtar bir daha gösterilmeyecek. Agent yapılandırmasına şimdi kaydedin."
            });
        });

        group.MapPost("/{id:guid}/rotate-key", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var agent = await db.Agents.FirstOrDefaultAsync(a => a.Id == id, ct);
            if (agent is null)
            {
                return Results.NotFound(new { error = "Agent bulunamadı." });
            }

            var key = AgentRecord.GenerateKey();
            agent.KeyHash = AgentRecord.Hash(key);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                enrollmentKey = key,
                warning = "Eski anahtar geçersiz oldu; agent yeni anahtarla yeniden yapılandırılmalı."
            });
        });

        group.MapPut("/{id:guid}", async (Guid id, UpdateAgentRequest request, AppDbContext db, CancellationToken ct) =>
        {
            var agent = await db.Agents.FirstOrDefaultAsync(a => a.Id == id, ct);
            if (agent is null)
            {
                return Results.NotFound(new { error = "Agent bulunamadı." });
            }

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                agent.Name = request.Name.Trim();
            }

            agent.Enabled = request.Enabled;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { ok = true });
        });

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            // Servers pointing at a deleted agent would silently stop being monitored, so they
            // are handed back to the hub instead.
            var reassigned = await db.Set<ServerProfile>()
                .Where(p => p.AgentId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.AgentId, (Guid?)null), ct);

            var deleted = await db.Agents.Where(a => a.Id == id).ExecuteDeleteAsync(ct);

            return deleted == 0
                ? Results.NotFound(new { error = "Agent bulunamadı." })
                : Results.Ok(new { ok = true, reassignedServers = reassigned });
        });

        return app;
    }
}

public sealed record CreateAgentRequest(string Name);
public sealed record UpdateAgentRequest(string? Name, bool Enabled);

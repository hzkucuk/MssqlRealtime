using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MssqlRealtime.Core.Abstractions;
using MssqlRealtime.Core.Agents;
using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Core.Common;
using MssqlRealtime.Modules.Mssql.Alerts;
using MssqlRealtime.Modules.Mssql.Models;

namespace MssqlRealtime.Modules.Mssql.Agents;

/// <summary>
/// Turns server profiles assigned to an agent into that agent's work list.
/// <para>
/// The password is decrypted here and sent over the (TLS) hub connection, because the agent
/// has no key ring of its own — that is deliberate: nothing sensitive is written to disk on
/// the customer's machine, so an agent host that is later decommissioned leaks nothing.
/// </para>
/// </summary>
public sealed class MssqlAgentConfigurationProvider(
    DbContext db,
    ISecretProtector protector,
    ILogger<MssqlAgentConfigurationProvider> logger) : IAgentConfigurationProvider
{
    public async Task<AgentConfiguration> GetConfigurationAsync(Guid agentId, CancellationToken ct = default)
    {
        var profiles = await db.Set<ServerProfile>()
            .AsNoTracking()
            .Where(p => p.AgentId == agentId && p.Enabled)
            .ToListAsync(ct);

        var targets = new List<AgentSqlTarget>(profiles.Count);

        foreach (var profile in profiles)
        {
            string? password = null;

            if (!profile.UsesIntegratedAuth && !string.IsNullOrEmpty(profile.ProtectedPassword))
            {
                var unprotected = protector.Unprotect(profile.ProtectedPassword);
                if (unprotected.IsFailure)
                {
                    logger.LogError(
                        "Cannot send {Server} to agent {AgentId}: {Error}",
                        profile.Name, agentId, unprotected.Error);
                    continue;
                }

                password = unprotected.Value;
            }

            targets.Add(new AgentSqlTarget
            {
                ServerId = profile.Id,
                Name = profile.Name,
                CustomerName = profile.CustomerName,
                Host = profile.Host,
                Port = profile.Port,
                InitialCatalog = profile.InitialCatalog,
                IntegratedSecurity = profile.UsesIntegratedAuth,
                Username = profile.Username,
                Password = password,
                EncryptConnection = profile.EncryptConnection,
                TrustServerCertificate = profile.TrustServerCertificate,
                ConnectTimeoutSeconds = profile.ConnectTimeoutSeconds,
                CommandTimeoutSeconds = profile.CommandTimeoutSeconds,
                PollIntervalSeconds = profile.PollIntervalSeconds
            });
        }

        // Revision lets an agent skip a configuration it already has, without comparing
        // passwords field by field.
        var revision = string.Join(
            '|',
            targets.OrderBy(t => t.ServerId).Select(t => $"{t.ServerId:N}:{t.PollIntervalSeconds}"));

        return new AgentConfiguration
        {
            SqlTargets = targets,
            Revision = revision.Length == 0 ? "empty" : Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(revision)))[..16]
        };
    }
}

/// <summary>
/// Receives snapshots measured by an agent and runs them through exactly the same path as a
/// locally polled one: thresholds, alert engine, cache, broadcast.
/// <para>
/// The agent measures; the hub decides. That split is what keeps alerting consistent whether
/// a server is polled directly or through an agent.
/// </para>
/// </summary>
public sealed class MssqlAgentSnapshotSink(
    ISnapshotCache cache,
    IAlertEngine alerts,
    IAlertSink alertSink,
    IRealtimePublisher publisher,
    ILogger<MssqlAgentSnapshotSink> logger) : IAgentSnapshotSink
{
    public const string Kind = "mssql.snapshot";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public string PayloadKind => Kind;

    public async Task<Result> IngestAsync(Guid agentId, string payloadJson, CancellationToken ct = default)
    {
        AgentSnapshotEnvelope? envelope;

        try
        {
            envelope = JsonSerializer.Deserialize<AgentSnapshotEnvelope>(payloadJson, SerializerOptions);
        }
        catch (JsonException ex)
        {
            return Result.Failure($"Agent verisi çözümlenemedi: {ex.Message}", "bad_payload");
        }

        if (envelope?.Snapshot is not { } snapshot)
        {
            return Result.Failure("Agent verisi boş.", "empty_payload");
        }

        var target = new AlertTarget
        {
            ModuleId = MssqlModule.ModuleId,
            TargetId = snapshot.ServerId.ToString("N"),
            TargetName = snapshot.ServerName,
            GroupName = snapshot.CustomerName
        };

        // The agent sends thresholds it measured against, but the hub owns the rules: a
        // compromised or outdated agent must not be able to suppress an alert.
        var outcome = alerts.Evaluate(target, envelope.Candidates ?? [], DateTimeOffset.UtcNow);

        var enriched = snapshot with
        {
            ActiveAlerts = outcome.Active,
            Summary = snapshot.Summary with
            {
                Severity = outcome.Active.Count == 0
                    ? snapshot.Summary.Severity
                    : outcome.Active.Max(a => a.Severity)
            }
        };

        cache.Set(enriched);
        await publisher.PublishAsync(MssqlModule.ModuleId, target.TargetId, "snapshot", enriched, ct);

        foreach (var notification in outcome.ToNotify)
        {
            logger.LogInformation(
                "Alert {State} {Rule} on {Server} (via agent {AgentId}): {Message}",
                notification.IsCleared ? "cleared" : "raised",
                notification.Alert.RuleId, snapshot.ServerName, agentId, notification.Body);

            await alertSink.PublishAsync(notification, ct);
        }

        return Result.Success();
    }
}

/// <summary>What an agent sends per poll: the measurement plus the rule evaluations.</summary>
public sealed record AgentSnapshotEnvelope
{
    public ServerSnapshot? Snapshot { get; init; }
    public IReadOnlyList<AlertCandidate>? Candidates { get; init; }
}

using System.Collections.Concurrent;

namespace MssqlRealtime.Core.Alerts;

/// <summary>
/// Turns a stream of per-cycle rule evaluations into the few events worth waking a phone for.
/// Module-agnostic on purpose: every tool added later inherits debouncing, hysteresis and
/// renotify suppression without writing any of it.
/// </summary>
public interface IAlertEngine
{
    /// <summary>
    /// Feeds one cycle's evaluations for a target. Returns what is still firing and what must
    /// be pushed right now.
    /// </summary>
    AlertOutcome Evaluate(AlertTarget target, IReadOnlyList<AlertCandidate> candidates, DateTimeOffset nowUtc);

    /// <summary>Drops all state for a target (deleted, or disabled).</summary>
    void Forget(string moduleId, string targetId);

    IReadOnlyList<AlertState> GetActive();

    /// <summary>
    /// Reloads alerts that were firing before a restart, including when they were last
    /// notified — otherwise every restart re-announces problems the user already knows about.
    /// </summary>
    void Restore(IEnumerable<AlertState> active);
}

public sealed record AlertOutcome(
    IReadOnlyList<AlertState> Active,
    IReadOnlyList<AlertNotification> ToNotify);

public sealed class AlertEngine : IAlertEngine
{
    private sealed class RuleState
    {
        public int ConsecutiveBreaches;
        public AlertState? Firing;
    }

    private readonly ConcurrentDictionary<string, RuleState> _rules = new(StringComparer.Ordinal);

    public AlertOutcome Evaluate(AlertTarget target, IReadOnlyList<AlertCandidate> candidates, DateTimeOffset nowUtc)
    {
        var active = new List<AlertState>();
        var notify = new List<AlertNotification>();

        foreach (var candidate in candidates)
        {
            var key = $"{target.ModuleId}:{target.TargetId}:{candidate.RuleId}";
            var state = _rules.GetOrAdd(key, static _ => new RuleState());

            lock (state)
            {
                if (!candidate.IsBreached)
                {
                    state.ConsecutiveBreaches = 0;

                    // Recovery is news too — but only if we ever told the user about it.
                    if (state.Firing is not null)
                    {
                        var cleared = state.Firing;
                        state.Firing = null;

                        if (cleared.LastNotifiedUtc is not null)
                        {
                            notify.Add(new AlertNotification
                            {
                                Alert = cleared,
                                IsCleared = true,
                                RaisedAtUtc = nowUtc
                            });
                        }
                    }

                    continue;
                }

                state.ConsecutiveBreaches++;

                // Still inside the grace window: a single spike must not reach the phone.
                if (state.Firing is null && state.ConsecutiveBreaches < Math.Max(1, candidate.RequiredConsecutiveBreaches))
                {
                    continue;
                }

                var since = state.Firing?.SinceUtc ?? nowUtc;
                var lastNotified = state.Firing?.LastNotifiedUtc;
                var previousSeverity = state.Firing?.Severity;

                var current = new AlertState
                {
                    Target = target,
                    RuleId = candidate.RuleId,
                    RuleTitle = candidate.RuleTitle,
                    Severity = candidate.Severity,
                    Message = candidate.Message,
                    Value = candidate.Value,
                    Threshold = candidate.Threshold,
                    Unit = candidate.Unit,
                    // Recovery keeps the context that was captured when it fired: knowing who
                    // caused it is the point, and by the time it clears they are gone.
                    Context = candidate.Context ?? state.Firing?.Context,
                    SinceUtc = since,
                    LastNotifiedUtc = lastNotified
                };

                var isNew = previousSeverity is null;
                var gotWorse = previousSeverity is not null && candidate.Severity > previousSeverity;
                var renotifyDue = lastNotified is not null
                    && nowUtc - lastNotified.Value >= TimeSpan.FromMinutes(Math.Max(1, candidate.RenotifyMinutes));

                if (isNew || gotWorse || renotifyDue)
                {
                    current = current with { LastNotifiedUtc = nowUtc };
                    notify.Add(new AlertNotification
                    {
                        Alert = current,
                        IsCleared = false,
                        RaisedAtUtc = nowUtc
                    });
                }

                state.Firing = current;
                active.Add(current);
            }
        }

        return new AlertOutcome(active, notify);
    }

    public void Forget(string moduleId, string targetId)
    {
        var prefix = $"{moduleId}:{targetId}:";
        foreach (var key in _rules.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)))
        {
            _rules.TryRemove(key, out _);
        }
    }

    public void Restore(IEnumerable<AlertState> active)
    {
        foreach (var alert in active)
        {
            var key = $"{alert.Target.ModuleId}:{alert.Target.TargetId}:{alert.RuleId}";

            _rules[key] = new RuleState
            {
                Firing = alert,
                // Treat it as already past the grace window: the breach was confirmed before
                // the restart, so a single fresh reading is enough to keep it firing.
                ConsecutiveBreaches = int.MaxValue / 2
            };
        }
    }

    public IReadOnlyList<AlertState> GetActive() =>
        _rules.Values
            .Select(r =>
            {
                lock (r)
                {
                    return r.Firing;
                }
            })
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
}

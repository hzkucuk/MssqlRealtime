using System.Reflection;
using System.Text.RegularExpressions;
using MssqlRealtime.Modules.Mssql.Probes;

namespace MssqlRealtime.Tests;

/// <summary>
/// Measured 2026-08-09: on a busy SQL Server 2019 Standard instance the Sessions tab
/// rendered nothing while its header still counted 254 sessions. Cause:
/// sys.dm_exec_connections holds one row per *connection* and MARS
/// (MultipleActiveResultSets=True) gives a session several of them, so a plain JOIN
/// fanned one session into N rows. The payload then carried duplicate SessionId values
/// and the keyed {#each} in the UI threw each_key_duplicate, aborting the whole tab —
/// in production builds too, the check is not DEV-only. Express instances were fine
/// because few of their clients enable MARS.
///
/// These tests pin the shape of the queries. A snapshot row must be uniquely
/// identifiable, and that property has to survive future edits to the SQL.
/// </summary>
public class ProbeRowUniquenessTests
{
    private static string SqlOf<T>()
    {
        var field = typeof(T).GetField("Sql", BindingFlags.NonPublic | BindingFlags.Static)
                    ?? throw new InvalidOperationException($"{typeof(T).Name} has no Sql constant");
        return (string)(field.GetRawConstantValue() ?? throw new InvalidOperationException("Sql is null"));
    }

    /// <summary>A JOIN onto dm_exec_connections is one-to-many and must never be used.</summary>
    [Theory]
    [InlineData(typeof(SessionsProbe))]
    [InlineData(typeof(BlockingProbe))]
    public void NoProbeJoinsConnectionsDirectly(Type probe)
    {
        var sql = (string)typeof(ProbeRowUniquenessTests)
            .GetMethod(nameof(SqlOf), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(probe).Invoke(null, null)!;

        var badJoin = Regex.IsMatch(sql, @"JOIN\s+sys\.dm_exec_connections", RegexOptions.IgnoreCase);
        Assert.False(badJoin,
            $"{probe.Name}: sys.dm_exec_connections must be read through OUTER APPLY (SELECT TOP 1 …); " +
            "a JOIN duplicates rows for MARS sessions and breaks the keyed list in the UI.");

        Assert.Matches(new Regex(@"OUTER\s+APPLY\s*\(\s*SELECT\s+TOP\s+1", RegexOptions.IgnoreCase), sql);
    }

    /// <summary>
    /// A session may legitimately run several requests at once under MARS, so the requests
    /// query is right to return them all — but each row then needs request_id to stay
    /// identifiable. Without it the "Çalışan" tab hits the same duplicate-key crash.
    /// </summary>
    [Fact]
    public void RequestsCarryRequestIdSoRowsStayUnique()
    {
        var sql = SqlOf<RequestsProbe>();
        Assert.Matches(new Regex(@"r\.request_id\s+AS\s+RequestId", RegexOptions.IgnoreCase), sql);
    }

    /// <summary>
    /// Same story on the blocking side: sys.dm_exec_requests has one row per *request*, so a
    /// MARS session with two blocked requests produces two edges with the same blocked
    /// session id. The "Bloke" tab keys on the pair, so the query has to carry request_id.
    /// </summary>
    [Fact]
    public void BlockingEdgesCarryBlockedRequestIdSoRowsStayUnique()
    {
        var sql = SqlOf<BlockingProbe>();
        Assert.Matches(new Regex(@"r\.request_id\s+AS\s+BlockedRequestId", RegexOptions.IgnoreCase), sql);
    }

    /// <summary>The sessions query must stay one row per session.</summary>
    [Fact]
    public void SessionsQueryReadsConnectionsThroughTopOne()
    {
        var sql = SqlOf<SessionsProbe>();
        Assert.Contains("dm_exec_connections", sql);
        Assert.Matches(
            new Regex(@"OUTER\s+APPLY\s*\(\s*SELECT\s+TOP\s+1\s+c2\.client_net_address", RegexOptions.IgnoreCase),
            sql);
    }
}

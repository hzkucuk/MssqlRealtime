using System.Text.RegularExpressions;
using MssqlRealtime.Core.Privacy;

namespace MssqlRealtime.Modules.Mssql.Models;

/// <summary>
/// The slowest request of one collection cycle, described well enough to still mean something
/// a month later.
/// </summary>
/// <remarks>
/// The history table used to keep the number alone, and a number cannot be acted on: by the
/// time somebody opens the report the session is gone, so "who ran it" and "what was it" have
/// to be captured at the moment of measurement or not at all. Both are cut here rather than at
/// the database, so the stored row can never be longer than the column allows.
/// </remarks>
public static partial class LongestQuery
{
    /// <summary>Matches the <c>LongestQueryBy</c> column width.</summary>
    public const int ByMaxLength = 200;

    /// <summary>
    /// Matches the <c>LongestQueryText</c> column width. Longer than the 240 an alert carries:
    /// this text is read on a screen with room, not inside a Telegram message.
    /// </summary>
    public const int TextMaxLength = 500;

    /// <summary>Seconds of the slowest request, plus who ran it and what it was.</summary>
    public readonly record struct Result(int Seconds, string? By, string? Text);

    /// <summary>
    /// Picks the slowest request of the cycle. An empty list is a measurement of zero, not a
    /// missing measurement — the server answered and nothing was running — but there is nobody
    /// to name for it, so identity and statement stay null.
    /// </summary>
    public static Result From(IReadOnlyList<RequestInfo> requests, StatementStorage storage)
    {
        var longest = requests.Count == 0 ? null : requests.MaxBy(r => r.ElapsedSeconds);

        if (longest is null || longest.ElapsedSeconds <= 0)
        {
            return new Result(0, null, null);
        }

        return new Result(longest.ElapsedSeconds, By(longest), Statement(longest.SqlText, storage));
    }

    /// <summary>SPID, application, login, machine, database — in that order, whatever exists.</summary>
    private static string By(RequestInfo request)
    {
        var parts = new List<string> { $"SPID {request.SessionId}" };

        if (!string.IsNullOrWhiteSpace(request.ProgramName)) parts.Add(request.ProgramName!);
        if (!string.IsNullOrWhiteSpace(request.LoginName)) parts.Add(request.LoginName!);
        if (!string.IsNullOrWhiteSpace(request.HostName)) parts.Add(request.HostName!);
        if (!string.IsNullOrWhiteSpace(request.DatabaseName)) parts.Add(request.DatabaseName!);

        return Cut(string.Join(" · ", parts), ByMaxLength);
    }

    /// <summary>
    /// The statement folded onto one line. Stored folded, not on display: the report table
    /// shows it in a row, and 40 lines of indentation stored per minute is 40 lines of
    /// indentation read back for every chart.
    /// <para>
    /// Masking happens before folding and cutting, in that order: masked text is shorter, so
    /// more of the query survives the 500 characters — and a literal must never be what gets
    /// cut off last.
    /// </para>
    /// </summary>
    private static string? Statement(string? sql, StatementStorage storage)
    {
        if (storage == StatementStorage.None || string.IsNullOrWhiteSpace(sql))
        {
            return null;
        }

        var text = storage == StatementStorage.Masked ? StatementMasking.Mask(sql) : sql;

        return Cut(WhitespaceRun().Replace(text!.Trim(), " "), TextMaxLength);
    }

    private static string Cut(string text, int max)
    {
        if (text.Length <= max)
        {
            return text;
        }

        // One character short, because the ellipsis takes a place too — the result has to fit
        // the column, not merely be close to it.
        var end = max - 1;

        // Never split a surrogate pair: half of one is not a character.
        if (char.IsHighSurrogate(text[end - 1])) end--;

        return string.Concat(text.AsSpan(0, end), "…");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}

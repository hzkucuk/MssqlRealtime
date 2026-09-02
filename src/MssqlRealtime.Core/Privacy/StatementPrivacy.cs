namespace MssqlRealtime.Core.Privacy;

/// <summary>
/// How much of a captured statement may be written to disk.
/// </summary>
/// <remarks>
/// The panel stores query text in two places that outlive the session it came from: the
/// reports history (kept up to two years) and the alert record. A statement can carry a
/// customer's identity number in a literal, so what is kept on disk is a decision the panel
/// owner has to be able to make — not one hard-coded here. Stored as a string, so renumbering
/// this enum can never silently change a customer's setting.
/// </remarks>
public enum StatementStorage
{
    /// <summary>Literals replaced by <c>?</c>: the shape of the query is kept, the values are not.</summary>
    Masked = 0,

    /// <summary>Stored exactly as the server reported it, literals included.</summary>
    Full = 1,

    /// <summary>Not stored at all — only the number and who ran it.</summary>
    None = 2
}

/// <summary>
/// The panel-wide answer to "may this statement be written down?".
/// </summary>
/// <remarks>
/// Read on every poll cycle by every module that captures query text, so the value is served
/// from memory and re-read only when it changes. Nothing here decides what a screen shows:
/// this is about what survives on disk.
/// </remarks>
public interface IStatementPrivacy
{
    StatementStorage Storage { get; }

    /// <summary>Re-reads the setting. Called once at startup and after it is saved.</summary>
    Task<StatementStorage> RefreshAsync(CancellationToken ct = default);

    Task SaveAsync(StatementStorage storage, CancellationToken ct = default);
}

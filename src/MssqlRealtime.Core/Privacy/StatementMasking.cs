using System.Text.RegularExpressions;

namespace MssqlRealtime.Core.Privacy;

/// <summary>
/// Replaces the values in a statement with <c>?</c> and leaves its shape alone.
/// </summary>
/// <remarks>
/// <para>
/// <c>WHERE TCKimlik = '12345678901'</c> becomes <c>WHERE TCKimlik = ?</c>: what a report
/// needs — which query, against which table, run by whom — survives, and the personal datum
/// does not. This is the same idea as a query fingerprint; it is not encryption and it is not
/// reversible, which is the point.
/// </para>
/// <para>
/// What it does not do: comments are left alone, and a name written into one
/// (<c>-- Ahmet'in raporu</c>) still reaches the column. Masking is a reduction of risk, not a
/// guarantee, and saying so is more useful than implying otherwise.
/// </para>
/// </remarks>
public static partial class StatementMasking
{
    public const string Placeholder = "?";

    public static string? Mask(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        // Strings first: a number inside a literal is already gone by the time the numeric
        // rule runs, so '2026-09-02' cannot come out as ?-?-?.
        var masked = QuotedLiteral().Replace(text, Placeholder);
        masked = BinaryLiteral().Replace(masked, Placeholder);

        return NumericLiteral().Replace(masked, Placeholder);
    }

    /// <summary><c>'metin'</c> and <c>N'metin'</c>, including the doubled-quote escape.</summary>
    [GeneratedRegex(@"[Nn]?'(?:[^']|'')*'")]
    private static partial Regex QuotedLiteral();

    [GeneratedRegex(@"\b0[xX][0-9A-Fa-f]+\b")]
    private static partial Regex BinaryLiteral();

    /// <summary>
    /// Numbers standing on their own. A digit inside an identifier is not a value —
    /// <c>Adres2</c> and <c>dbo.T1</c> stay readable, <c>TOP 100</c> becomes <c>TOP ?</c>.
    /// Numeric identity numbers are exactly why unquoted digits are masked too.
    /// </summary>
    [GeneratedRegex(@"(?<![\w.@#$])\d+(?:\.\d+)?(?!\w)")]
    private static partial Regex NumericLiteral();
}

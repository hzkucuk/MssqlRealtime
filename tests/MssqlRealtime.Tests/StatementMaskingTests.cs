using MssqlRealtime.Core.Privacy;
using MssqlRealtime.Modules.Mssql.Models;

namespace MssqlRealtime.Tests;

public class StatementMaskingTests
{
    [Theory]
    // The case the switch exists for: an identity number in a literal.
    [InlineData("SELECT * FROM Musteri WHERE TCKimlik = '12345678901'",
                "SELECT * FROM Musteri WHERE TCKimlik = ?")]
    // Unquoted numbers are values too — an identity number stored in a bigint column.
    [InlineData("SELECT * FROM Musteri WHERE TCKimlik = 12345678901",
                "SELECT * FROM Musteri WHERE TCKimlik = ?")]
    [InlineData("EXEC sp_Rapor @Ad = N'Ahmet', @Yil = 2026", "EXEC sp_Rapor @Ad = ?, @Yil = ?")]
    // A doubled quote is an escaped apostrophe inside one literal, not two literals.
    [InlineData("WHERE Ad = 'O''Brien' AND Soyad = 'Küçük'", "WHERE Ad = ? AND Soyad = ?")]
    [InlineData("WHERE Tutar BETWEEN 10.5 AND 99.9", "WHERE Tutar BETWEEN ? AND ?")]
    [InlineData("WHERE Hash = 0xDEADBEEF", "WHERE Hash = ?")]
    public void ValuesGoTheShapeStays(string sql, string expected)
    {
        Assert.Equal(expected, StatementMasking.Mask(sql));
    }

    [Theory]
    // Digits inside a name are not values: masking them would leave a report nobody can read.
    [InlineData("SELECT Adres2 FROM dbo.T1 JOIN Musteri2 ON 1 = 1",
                "SELECT Adres2 FROM dbo.T1 JOIN Musteri2 ON ? = ?")]
    [InlineData("SELECT * FROM sys.dm_exec_requests", "SELECT * FROM sys.dm_exec_requests")]
    public void IdentifiersCarryingDigitsSurvive(string sql, string expected)
    {
        Assert.Equal(expected, StatementMasking.Mask(sql));
    }

    [Fact]
    public void EmptyTextIsLeftAlone()
    {
        Assert.Null(StatementMasking.Mask(null));
        Assert.Equal("   ", StatementMasking.Mask("   "));
    }

    [Fact]
    public void MaskedStorageKeepsTheQueryShapeInTheMeasurement()
    {
        var result = LongestQuery.From(
            [
                new RequestInfo
                {
                    SessionId = 312,
                    ElapsedSeconds = 38,
                    ProgramName = "RaporServisi",
                    LoginName = "app_user",
                    SqlText = "SELECT * FROM Musteri WHERE TCKimlik = '12345678901'"
                }
            ],
            StatementStorage.Masked);

        Assert.Equal("SELECT * FROM Musteri WHERE TCKimlik = ?", result.Text);

        // Who ran it is never masked: that is the question the report exists to answer.
        Assert.Equal("SPID 312 · RaporServisi · app_user", result.By);
        Assert.Equal(38, result.Seconds);
    }

    [Fact]
    public void NoneStorageKeepsTheNumberAndTheOwnerButNotTheText()
    {
        var result = LongestQuery.From(
            [
                new RequestInfo
                {
                    SessionId = 312,
                    ElapsedSeconds = 38,
                    LoginName = "app_user",
                    SqlText = "SELECT * FROM Musteri WHERE TCKimlik = '12345678901'"
                }
            ],
            StatementStorage.None);

        Assert.Null(result.Text);
        Assert.Equal("SPID 312 · app_user", result.By);
        Assert.Equal(38, result.Seconds);
    }

    [Fact]
    public void MaskingRunsBeforeTheCutSoALiteralCannotSurviveByBeingLate()
    {
        // The literal sits past the 500th character of the raw text: if the cut ran first the
        // masker would never see it, and the tail of a customer's data would be stored.
        var sql = "SELECT " + new string('a', 600) + " WHERE TCKimlik = '12345678901'";

        var result = LongestQuery.From(
            [new RequestInfo { SessionId = 7, ElapsedSeconds = 4, SqlText = sql }],
            StatementStorage.Masked);

        Assert.DoesNotContain("12345678901", result.Text);
    }
}

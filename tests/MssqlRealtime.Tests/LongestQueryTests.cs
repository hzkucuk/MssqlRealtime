using MssqlRealtime.Modules.Mssql.Models;
using MssqlRealtime.Core.Privacy;

namespace MssqlRealtime.Tests;

public class LongestQueryTests
{
    [Fact]
    public void NamesTheSlowestRequestNotTheFirstOne()
    {
        var result = LongestQuery.From(
        [
            new RequestInfo
            {
                SessionId = 71,
                ElapsedSeconds = 4,
                ProgramName = "SSMS",
                LoginName = "sa",
                HostName = "DBA-PC",
                DatabaseName = "master",
                SqlText = "SELECT 1"
            },
            new RequestInfo
            {
                SessionId = 312,
                ElapsedSeconds = 38,
                ProgramName = ".Net SqlClient Data Provider",
                LoginName = "app_user",
                HostName = "WEB01",
                DatabaseName = "Satis",
                SqlText = "SELECT * FROM Hareket"
            }
        ], StatementStorage.Full);

        Assert.Equal(38, result.Seconds);
        Assert.Equal("SPID 312 · .Net SqlClient Data Provider · app_user · WEB01 · Satis", result.By);
        Assert.Equal("SELECT * FROM Hareket", result.Text);
    }

    [Fact]
    public void MissingSessionFieldsAreLeftOutRatherThanShownAsDashes()
    {
        var result = LongestQuery.From(
            [new RequestInfo { SessionId = 90, ElapsedSeconds = 2 }], StatementStorage.Full);

        Assert.Equal("SPID 90", result.By);
        Assert.Null(result.Text);
    }

    [Fact]
    public void NothingRunningIsZeroSecondsWithNobodyToName()
    {
        var empty = LongestQuery.From([], StatementStorage.Full);

        Assert.Equal(0, empty.Seconds);
        Assert.Null(empty.By);
        Assert.Null(empty.Text);

        // A request that has not yet reached a full second is the same case: the number is a
        // true zero, but naming a session for it would put noise in every row of the report.
        var subSecond = LongestQuery.From(
            [new RequestInfo { SessionId = 91, ElapsedSeconds = 0, SqlText = "SELECT 1" }],
            StatementStorage.Full);

        Assert.Equal(0, subSecond.Seconds);
        Assert.Null(subSecond.By);
        Assert.Null(subSecond.Text);
    }

    [Fact]
    public void StatementIsFoldedOntoOneLine()
    {
        var result = LongestQuery.From(
        [
            new RequestInfo
            {
                SessionId = 55,
                ElapsedSeconds = 9,
                SqlText = "SELECT\r\n    Ad,\tSoyad\nFROM   dbo.Musteri\n"
            }
        ], StatementStorage.Full);

        Assert.Equal("SELECT Ad, Soyad FROM dbo.Musteri", result.Text);
    }

    [Fact]
    public void BothFieldsFitTheColumnsTheyAreStoredIn()
    {
        var result = LongestQuery.From(
        [
            new RequestInfo
            {
                SessionId = 4000,
                ElapsedSeconds = 60,
                ProgramName = new string('u', 400),
                SqlText = new string('x', 4000)
            }
        ], StatementStorage.Full);

        // Not "about 200/500": the columns are declared at exactly these widths, and a row
        // one character over is a failed insert at three in the morning.
        Assert.Equal(LongestQuery.ByMaxLength, result.By!.Length);
        Assert.Equal(LongestQuery.TextMaxLength, result.Text!.Length);
        Assert.EndsWith("…", result.By);
        Assert.EndsWith("…", result.Text);
    }

    [Fact]
    public void CuttingNeverSplitsASurrogatePair()
    {
        // An emoji straddling the cut point used to leave half a code point in the column.
        var text = new string('x', LongestQuery.TextMaxLength - 2) + "🔴" + new string('y', 20);

        var result = LongestQuery.From(
            [new RequestInfo { SessionId = 12, ElapsedSeconds = 3, SqlText = text }],
            StatementStorage.Full);

        Assert.DoesNotContain(result.Text!, char.IsSurrogate);
        Assert.True(result.Text!.Length <= LongestQuery.TextMaxLength);
    }
}

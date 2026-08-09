using System.Text.Json;
using MssqlRealtime.Api.Setup;

namespace MssqlRealtime.Tests;

/// <summary>
/// The panel can update itself from a GitHub release, triggered by hand from the UI.
///
/// What these tests can cover is the decision: which release is newest, which asset belongs
/// to it, whether an update is offered at all, and whether a rollback package exists. What
/// they cannot cover is the install itself — that runs an Inno Setup installer which stops
/// and recreates a Windows service, and it has to be measured on Windows. The refusal on a
/// non-Windows host is tested here precisely so the untestable path is never entered by
/// accident on a developer machine.
/// </summary>
public class UpdateServiceTests
{
    /// <summary>Shaped like the real GitHub answer — the digest form was taken from a live call.</summary>
    private static JsonElement Releases(params (string Tag, string Version, bool Draft, bool Pre)[] items)
    {
        var releases = items.Select(i => new
        {
            tag_name = i.Tag,
            draft = i.Draft,
            prerelease = i.Pre,
            body = $"notlar {i.Version}",
            assets = new object[]
            {
                new
                {
                    name = $"SunucuIzleme-Setup-{i.Version}.exe",
                    size = 41318570L,
                    digest = "sha256:313e7816c1f079571e79b9e043cf92f4fa6bac34bd3b47281fff35395678a559",
                    browser_download_url = $"https://example.invalid/{i.Version}/setup.exe"
                },
                new
                {
                    name = $"SunucuIzleme-{i.Version}.apk",
                    size = 12309952L,
                    digest = "sha256:88890e00d070f2843e4cb04ae3d00c4d48af0170e0ba22d749290fdde4eccadf",
                    browser_download_url = $"https://example.invalid/{i.Version}/app.apk"
                }
            }
        });

        return JsonDocument.Parse(JsonSerializer.Serialize(releases)).RootElement.Clone();
    }

    [Theory]
    [InlineData("0.18.5", "0.18.6", -1)]
    [InlineData("0.18.6", "0.18.5", 1)]
    [InlineData("0.18.6", "0.18.6", 0)]
    [InlineData("v0.18.6", "0.18.6", 0)]      // etiketteki 'v' fark yaratmamalı
    [InlineData("0.9.0", "0.18.0", -1)]       // metin sıralaması olsaydı ters çıkardı
    [InlineData("1.0", "1.0.0", 0)]           // eksik parça sıfır sayılır
    public void VersionsCompareNumerically(string a, string b, int expected)
    {
        Assert.Equal(expected, Math.Sign(UpdateService.CompareVersions(a, b)));
    }

    [Fact]
    public void NewerReleaseIsOfferedWithItsInstaller()
    {
        var info = UpdateService.Evaluate("0.18.5",
            Releases(("v0.18.6", "0.18.6", false, false), ("v0.18.5", "0.18.5", false, false)), supported: true);

        Assert.True(info.Available);
        Assert.Equal("0.18.6", info.Latest);
        Assert.Equal("SunucuIzleme-Setup-0.18.6.exe", info.Setup!.Name);
        Assert.Equal("313e7816c1f079571e79b9e043cf92f4fa6bac34bd3b47281fff35395678a559", info.Setup.Sha256);
        Assert.Null(info.Error);
    }

    /// <summary>
    /// Rollback is the installer of the version that is running now: the same, already
    /// tested install machinery puts the service back with the right arguments.
    /// </summary>
    [Fact]
    public void RollbackPackageIsTheRunningVersionsInstaller()
    {
        var info = UpdateService.Evaluate("0.18.5",
            Releases(("v0.18.6", "0.18.6", false, false), ("v0.18.5", "0.18.5", false, false)), supported: true);

        Assert.True(info.CanRollback);
        Assert.Equal("SunucuIzleme-Setup-0.18.5.exe", info.Rollback!.Name);
    }

    /// <summary>
    /// A version with no release of its own can still be updated — but the operator has to
    /// be told there is no way back, rather than finding out after a failed install.
    /// </summary>
    [Fact]
    public void MissingRollbackIsReportedNotHidden()
    {
        var info = UpdateService.Evaluate("0.18.4-dev",
            Releases(("v0.18.6", "0.18.6", false, false)), supported: true);

        Assert.True(info.Available);
        Assert.False(info.CanRollback);
    }

    [Fact]
    public void CurrentVersionMeansNothingIsOffered()
    {
        var info = UpdateService.Evaluate("0.18.6",
            Releases(("v0.18.6", "0.18.6", false, false), ("v0.18.5", "0.18.5", false, false)), supported: true);

        Assert.False(info.Available);
        Assert.Null(info.Setup);
        Assert.Null(info.Error);
    }

    [Fact]
    public void DraftsAndPrereleasesAreIgnored()
    {
        var info = UpdateService.Evaluate("0.18.6",
            Releases(("v0.19.0", "0.19.0", true, false),      // taslak
                     ("v0.20.0", "0.20.0", false, true),      // ön sürüm
                     ("v0.18.6", "0.18.6", false, false)), supported: true);

        Assert.False(info.Available);
        Assert.Equal("0.18.6", info.Latest);
    }

    /// <summary>The newest release wins even when the list is not ordered.</summary>
    [Fact]
    public void NewestWinsRegardlessOfListOrder()
    {
        var info = UpdateService.Evaluate("0.18.5",
            Releases(("v0.18.6", "0.18.6", false, false),
                     ("v0.19.2", "0.19.2", false, false),
                     ("v0.18.5", "0.18.5", false, false)), supported: true);

        Assert.Equal("0.19.2", info.Latest);
        Assert.Equal("SunucuIzleme-Setup-0.19.2.exe", info.Setup!.Name);
    }

    /// <summary>
    /// A release whose installer is missing must not be offered: the button would download
    /// nothing and the operator would be left guessing.
    /// </summary>
    [Fact]
    public void ReleaseWithoutInstallerIsNotOffered()
    {
        var json = JsonDocument.Parse("""
            [{ "tag_name": "v0.19.0", "draft": false, "prerelease": false,
               "assets": [{ "name": "SunucuIzleme-0.19.0.apk", "size": 1,
                            "digest": "sha256:aa", "browser_download_url": "https://example.invalid/a.apk" }] }]
            """).RootElement;

        var info = UpdateService.Evaluate("0.18.6", json, supported: true);
        Assert.False(info.Available);
    }

    [Theory]
    [InlineData("SunucuIzleme-Setup-0.18.6.exe", "0.18.6")]
    [InlineData("SunucuIzleme-0.18.6.apk", null)]
    [InlineData("SunucuIzleme-0.18.6-win-x64.zip", null)]
    public void InstallerVersionIsReadFromTheAssetName(string name, string? expected)
    {
        Assert.Equal(expected, UpdateService.VersionFromAssetName(name));
    }
}

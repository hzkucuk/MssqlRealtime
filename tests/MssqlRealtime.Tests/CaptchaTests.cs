using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using MssqlRealtime.Api.Security;

namespace MssqlRealtime.Tests;

/// <summary>
/// The captcha is deliberately home-grown: the panel runs inside customer networks where
/// outbound internet is not guaranteed, so a third-party widget that fails to load would lock
/// the operator out during the incident they installed the tool for.
/// </summary>
public class CaptchaTests
{
    private static CaptchaService Create() =>
        new(DataProtectionProvider.Create(nameof(CaptchaTests)));

    /// <summary>Reads the challenge text back out of the rendered SVG.</summary>
    private static string ReadCode(string svg) =>
        string.Concat(Regex.Matches(svg, "<text[^>]*>([^<])</text>").Select(m => m.Groups[1].Value));

    [Fact]
    public void CorrectAnswerIsAccepted()
    {
        var captcha = Create();
        var challenge = captcha.Create();

        Assert.True(captcha.Validate(challenge.Token, ReadCode(challenge.Svg)));
    }

    [Fact]
    public void WrongAnswerIsRejected()
    {
        var captcha = Create();
        var challenge = captcha.Create();

        Assert.False(captcha.Validate(challenge.Token, "ZZZZZ"));
    }

    [Fact]
    public void AnswerIsCaseInsensitive()
    {
        var captcha = Create();
        var challenge = captcha.Create();

        // The image is uppercase, but nobody should fail for typing lowercase on a phone.
        Assert.True(captcha.Validate(challenge.Token, ReadCode(challenge.Svg).ToLowerInvariant()));
    }

    [Fact]
    public void SurroundingWhitespaceIsIgnored()
    {
        var captcha = Create();
        var challenge = captcha.Create();

        Assert.True(captcha.Validate(challenge.Token, $"  {ReadCode(challenge.Svg)} "));
    }

    [Fact]
    public void TamperedTokenIsRejected()
    {
        var captcha = Create();
        var challenge = captcha.Create();
        var tampered = challenge.Token[..^4] + "AAAA";

        Assert.False(captcha.Validate(tampered, ReadCode(challenge.Svg)));
    }

    [Fact]
    public void TokenFromAnotherKeyRingIsRejected()
    {
        // A different instance — or the same one after its key ring was replaced.
        var issuer = Create();
        var verifier = new CaptchaService(DataProtectionProvider.Create("different-application"));
        var challenge = issuer.Create();

        Assert.False(verifier.Validate(challenge.Token, ReadCode(challenge.Svg)));
    }

    [Theory]
    [InlineData(null, "ABCDE")]
    [InlineData("", "ABCDE")]
    [InlineData("token", null)]
    [InlineData("token", "")]
    [InlineData("token", "   ")]
    public void MissingPartsAreRejected(string? token, string? answer)
    {
        Assert.False(Create().Validate(token, answer));
    }

    [Fact]
    public void EachChallengeIsDifferent()
    {
        var captcha = Create();

        var codes = Enumerable.Range(0, 20).Select(_ => ReadCode(captcha.Create().Svg)).ToList();

        Assert.All(codes, c => Assert.Equal(5, c.Length));
        // Not a strict guarantee, but 20 identical draws from 32^5 would mean the generator
        // is not random at all.
        Assert.True(codes.Distinct().Count() > 15);
    }

    [Fact]
    public void CodeAvoidsAmbiguousCharacters()
    {
        var captcha = Create();

        var codes = string.Concat(Enumerable.Range(0, 50).Select(_ => ReadCode(captcha.Create().Svg)));

        // 0/O and 1/I/l are unfair rather than secure, especially on a phone screen.
        Assert.DoesNotContain('0', codes);
        Assert.DoesNotContain('O', codes);
        Assert.DoesNotContain('1', codes);
        Assert.DoesNotContain('I', codes);
    }

    [Fact]
    public void SvgIsSelfContainedAndLabelled()
    {
        var challenge = Create().Create();

        Assert.StartsWith("<svg", challenge.Svg);
        Assert.Contains("role='img'", challenge.Svg);

        // No external references: the whole point is that it renders with no internet.
        // The SVG namespace URI is not a fetch, so it is excluded before checking.
        var withoutNamespace = challenge.Svg.Replace("http://www.w3.org/2000/svg", string.Empty);
        Assert.DoesNotContain("http://", withoutNamespace);
        Assert.DoesNotContain("https://", withoutNamespace);
        Assert.DoesNotContain("<image", challenge.Svg);
    }
}

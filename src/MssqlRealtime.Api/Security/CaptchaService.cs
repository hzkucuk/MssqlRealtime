using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace MssqlRealtime.Api.Security;

/// <summary>
/// A captcha that works with no internet access.
/// <para>
/// reCAPTCHA, hCaptcha and Turnstile are all out of the question here: the panel runs inside a
/// customer's network where outbound internet is not guaranteed, and a captcha widget that
/// cannot load does not degrade — it locks the operator out of their own monitoring during
/// the exact incident they installed it for.
/// </para>
/// <para>
/// Stateless by design: the answer travels inside an encrypted, time-limited token, so a
/// restart or a second instance does not invalidate an in-flight login.
/// </para>
/// </summary>
public sealed class CaptchaService(IDataProtectionProvider protectionProvider)
{
    private const string Purpose = "MssqlRealtime.Captcha.v1";
    private const int Length = 5;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    /// <summary>Ambiguous glyphs are excluded — 0/O and 1/I/l are unfair, not secure.</summary>
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly IDataProtector _protector = protectionProvider.CreateProtector(Purpose);

    public CaptchaChallenge Create()
    {
        var text = new string(Enumerable.Range(0, Length)
            .Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)])
            .ToArray());

        var payload = JsonSerializer.Serialize(new CaptchaPayload(text, DateTimeOffset.UtcNow.Add(Lifetime)));

        return new CaptchaChallenge(_protector.Protect(payload), RenderSvg(text));
    }

    public bool Validate(string? token, string? answer)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(answer))
        {
            return false;
        }

        CaptchaPayload? payload;

        try
        {
            payload = JsonSerializer.Deserialize<CaptchaPayload>(_protector.Unprotect(token));
        }
        catch
        {
            // Tampered, or issued by an instance with a different key ring.
            return false;
        }

        if (payload is null || payload.ExpiresAt < DateTimeOffset.UtcNow)
        {
            return false;
        }

        // Case-insensitive: the image is uppercase, but nobody should fail for typing lowercase.
        return string.Equals(payload.Text, answer.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Draws the challenge as SVG. Deliberately mild distortion: this exists to stop scripted
    /// password guessing, not a determined human, and an unreadable captcha on a phone at 3am
    /// costs more than it protects.
    /// </summary>
    private static string RenderSvg(string text)
    {
        const int width = 180;
        const int height = 60;

        var svg = new StringBuilder();
        svg.Append($"<svg xmlns='http://www.w3.org/2000/svg' width='{width}' height='{height}' viewBox='0 0 {width} {height}' role='img' aria-label='Güvenlik kodu'>");
        svg.Append("<rect width='100%' height='100%' fill='#1f242c' rx='8'/>");

        // A few crossing lines: enough to defeat naive OCR without hurting a human eye.
        for (var i = 0; i < 4; i++)
        {
            var x1 = RandomNumberGenerator.GetInt32(width);
            var y1 = RandomNumberGenerator.GetInt32(height);
            var x2 = RandomNumberGenerator.GetInt32(width);
            var y2 = RandomNumberGenerator.GetInt32(height);
            var opacity = 0.15 + RandomNumberGenerator.GetInt32(20) / 100.0;

            svg.Append($"<line x1='{x1}' y1='{y1}' x2='{x2}' y2='{y2}' stroke='#4a9eff' stroke-width='1' opacity='{opacity:0.00}'/>");
        }

        for (var i = 0; i < 25; i++)
        {
            svg.Append($"<circle cx='{RandomNumberGenerator.GetInt32(width)}' cy='{RandomNumberGenerator.GetInt32(height)}' r='1' fill='#949dab' opacity='0.35'/>");
        }

        var step = (width - 30) / (double)text.Length;

        for (var i = 0; i < text.Length; i++)
        {
            var x = 20 + i * step;
            var y = 38 + RandomNumberGenerator.GetInt32(10) - 5;
            var rotation = RandomNumberGenerator.GetInt32(31) - 15;
            var size = 26 + RandomNumberGenerator.GetInt32(6);

            svg.Append($"<text x='{x:0.#}' y='{y}' font-family='monospace' font-size='{size}' font-weight='bold' fill='#e6e9ee' transform='rotate({rotation} {x:0.#} {y})'>{text[i]}</text>");
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    private sealed record CaptchaPayload(string Text, DateTimeOffset ExpiresAt);
}

public sealed record CaptchaChallenge(string Token, string Svg);

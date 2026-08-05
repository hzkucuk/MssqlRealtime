using System.Collections.Concurrent;

namespace MssqlRealtime.Api.Security;

/// <summary>
/// Requires a captcha on sign-in once an address has failed a few times.
/// <para>
/// Not on every attempt: the operator signs in from a phone, often at 03:00, often while
/// something is already broken. Making them read a distorted code every single time buys
/// nothing against a bot that has not started guessing yet, and costs the one person the
/// product exists for.
/// </para>
/// <para>
/// Together with Identity's account lockout this leaves a scripted attack with no cheap path:
/// the first attempts are throttled, the next ones need a captcha, and the account locks
/// regardless.
/// </para>
/// </summary>
public sealed class CaptchaMiddleware(
    RequestDelegate next,
    CaptchaService captcha,
    ILogger<CaptchaMiddleware> logger)
{
    /// <summary>Failures from one address before a captcha is demanded.</summary>
    public const int FreeAttempts = 2;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private static readonly ConcurrentDictionary<string, Failures> Tracker = new();

    public async Task InvokeAsync(HttpContext context)
    {
        var isLogin = context.Request.Path.StartsWithSegments("/api/auth/login", StringComparison.OrdinalIgnoreCase)
                      && HttpMethods.IsPost(context.Request.Method);

        if (!isLogin)
        {
            await next(context);
            return;
        }

        var key = Key(context);

        if (RequiresCaptcha(key))
        {
            var token = context.Request.Headers["X-Captcha-Token"].ToString();
            var answer = context.Request.Headers["X-Captcha-Answer"].ToString();

            if (!captcha.Validate(token, answer))
            {
                logger.LogWarning("Sign-in rejected: captcha missing or wrong ({Address})", key);

                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.Headers["X-Captcha-Required"] = "true";
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Güvenlik kodu hatalı veya süresi dolmuş.",
                    captchaRequired = true
                });
                return;
            }
        }

        await next(context);

        // Identity answers 401 for a bad password; anything successful clears the counter.
        if (context.Response.StatusCode == StatusCodes.Status401Unauthorized)
        {
            RecordFailure(key);
        }
        else if (context.Response.StatusCode is >= 200 and < 300)
        {
            Tracker.TryRemove(key, out _);
        }
    }

    /// <summary>Whether this caller must solve a captcha before the next attempt.</summary>
    public static bool RequiresCaptcha(string key)
    {
        if (!Tracker.TryGetValue(key, out var failures))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow - failures.First > Window)
        {
            Tracker.TryRemove(key, out _);
            return false;
        }

        return failures.Count >= FreeAttempts;
    }

    public static string Key(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static void RecordFailure(string key) =>
        Tracker.AddOrUpdate(
            key,
            _ => new Failures(1, DateTimeOffset.UtcNow),
            (_, existing) => DateTimeOffset.UtcNow - existing.First > Window
                ? new Failures(1, DateTimeOffset.UtcNow)
                : existing with { Count = existing.Count + 1 });

    private sealed record Failures(int Count, DateTimeOffset First);
}

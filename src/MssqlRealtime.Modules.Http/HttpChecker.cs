using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using MssqlRealtime.Modules.Http.Models;

namespace MssqlRealtime.Modules.Http;

/// <summary>Performs one HTTP check and reports what actually happened.</summary>
public sealed class HttpChecker(IHttpClientFactory httpClientFactory, ILogger<HttpChecker> logger)
{
    public const string ClientName = "http-monitor";
    public const string InsecureClientName = "http-monitor-insecure";

    /// <summary>
    /// Identifies the monitor to the sites it polls. Being identifiable is deliberate: an
    /// admin looking at their own access log should be able to tell what this traffic is.
    /// </summary>
    private const string UserAgent = "SunucuIzleme/1.0 (+monitoring)";

    public async Task<HttpCheckResult> CheckAsync(HttpTarget target, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var checkedAt = DateTimeOffset.UtcNow;

        try
        {
            var client = httpClientFactory.CreateClient(
                target.IgnoreCertificateErrors ? InsecureClientName : ClientName);

            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(target.TimeoutSeconds, 1, 120));

            using var request = new HttpRequestMessage(
                new HttpMethod(string.IsNullOrWhiteSpace(target.Method) ? "GET" : target.Method.ToUpperInvariant()),
                target.Url);

            // Measured 2026-08-05: api.github.com answers 403 to a request with no User-Agent,
            // and many WAFs do the same. Without this the monitor invents outages.
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "*/*");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);

            var elapsedMs = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var statusCode = (int)response.StatusCode;

            var statusOk = target.ExpectedStatusCode == 0
                ? response.IsSuccessStatusCode
                : statusCode == target.ExpectedStatusCode;

            string? bodyError = null;

            // A page that returns 200 while showing "Database connection failed" is down as far
            // as the customer is concerned; the body check is what catches it.
            if (statusOk && !string.IsNullOrWhiteSpace(target.ExpectedBodyContains))
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                if (!body.Contains(target.ExpectedBodyContains, StringComparison.OrdinalIgnoreCase))
                {
                    statusOk = false;
                    bodyError = $"Yanıt gövdesinde beklenen metin yok: \"{target.ExpectedBodyContains}\"";
                }
            }

            return new HttpCheckResult
            {
                TargetId = target.Id,
                TargetName = target.Name,
                GroupName = target.GroupName,
                Url = target.Url,
                CheckedAt = checkedAt,
                Status = statusOk ? HttpCheckStatus.Up : HttpCheckStatus.Down,
                StatusCode = statusCode,
                ResponseTimeMs = elapsedMs,
                ContentLength = response.Content.Headers.ContentLength,
                Error = statusOk
                    ? null
                    : bodyError ?? $"Beklenmeyen durum kodu {statusCode} ({response.ReasonPhrase})."
            };
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failure(target, checkedAt, started, $"Zaman aşımı ({target.TimeoutSeconds} sn).");
        }
        catch (HttpRequestException ex)
        {
            // The inner exception carries the useful sentence — DNS failure, refused connection,
            // rejected certificate. The outer one is always "An error occurred while sending".
            return Failure(target, checkedAt, started, ex.InnerException?.Message ?? ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "HTTP check failed unexpectedly for {Url}", target.Url);
            return Failure(target, checkedAt, started, ex.Message);
        }
    }

    /// <summary>
    /// Reads the TLS certificate with its own short-lived connection.
    /// <para>
    /// Deliberately separate from the HTTP check: a pooled HttpClient hands out connections
    /// that were established earlier, so a certificate observed through it cannot reliably be
    /// attributed to this request. A dedicated handshake is unambiguous — and since this only
    /// runs every Nth check, the extra connection costs nothing worth optimising.
    /// </para>
    /// </summary>
    public async Task<(int? DaysRemaining, string? Subject)> InspectCertificateAsync(
        HttpTarget target,
        CancellationToken ct)
    {
        if (!Uri.TryCreate(target.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return (null, null);
        }

        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(uri.Host, uri.Port, ct);

            await using var ssl = new SslStream(
                tcp.GetStream(),
                leaveInnerStreamOpen: false,
                // Expiry is what we are here to read, so an expired certificate must not throw
                // before we can report how long ago it expired.
                userCertificateValidationCallback: (_, _, _, _) => true);

            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = uri.Host
            }, ct);

            if (ssl.RemoteCertificate is not { } raw)
            {
                return (null, null);
            }

            using var certificate = X509CertificateLoader.LoadCertificate(raw.Export(X509ContentType.Cert));
            var days = (int)Math.Floor((certificate.NotAfter.ToUniversalTime() - DateTime.UtcNow).TotalDays);

            return (days, certificate.Subject);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not read TLS certificate for {Url}", target.Url);
            return (null, null);
        }
    }

    private static HttpCheckResult Failure(HttpTarget target, DateTimeOffset checkedAt, long started, string error) => new()
    {
        TargetId = target.Id,
        TargetName = target.Name,
        GroupName = target.GroupName,
        Url = target.Url,
        CheckedAt = checkedAt,
        Status = HttpCheckStatus.Down,
        ResponseTimeMs = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
        Error = error
    };

    /// <summary>Handler used by both named clients.</summary>
    public static SocketsHttpHandler CreateHandler(bool ignoreCertificateErrors)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            // Long-lived pooled connections would hide DNS changes and certificate renewals.
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        };

        if (ignoreCertificateErrors)
        {
            handler.SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            };
        }

        return handler;
    }
}

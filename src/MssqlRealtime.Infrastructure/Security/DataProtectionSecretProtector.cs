using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using MssqlRealtime.Core.Abstractions;
using MssqlRealtime.Core.Common;


namespace MssqlRealtime.Infrastructure.Security;

/// <summary>
/// Encrypts SQL passwords at rest with ASP.NET Core Data Protection. The key ring lives on
/// disk (see DI registration) so the same ciphertext survives restarts and container rebuilds,
/// and works identically on Linux, Windows and macOS.
/// </summary>
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private const string Purpose = "MssqlRealtime.ServerProfile.Password.v1";

    private readonly IDataProtector _protector;
    private readonly ILogger<DataProtectionSecretProtector> _logger;

    public DataProtectionSecretProtector(
        IDataProtectionProvider provider,
        ILogger<DataProtectionSecretProtector> logger)
    {
        _protector = provider.CreateProtector(Purpose);
        _logger = logger;
    }

    public string Protect(string plainText) => _protector.Protect(plainText);

    public Result<string> Unprotect(string cipherText)
    {
        try
        {
            return Result<string>.Success(_protector.Unprotect(cipherText));
        }
        catch (Exception ex)
        {
            // Almost always a lost/rotated key ring. Never log the ciphertext itself.
            _logger.LogError(ex, "Stored credential could not be decrypted; the data protection key ring may have changed");
            return Result<string>.Failure(
                "Kayıtlı parola çözülemedi. Şifreleme anahtarı değişmiş olabilir — sunucu parolasını yeniden girin.",
                "secret_unprotect_failed");
        }
    }
}

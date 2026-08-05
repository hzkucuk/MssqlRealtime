using System.Security.Cryptography;
using System.Text;

namespace MssqlRealtime.Infrastructure.Persistence;

/// <summary>
/// A registered agent. The enrollment key is stored hashed, like a password — an operator who
/// loses it issues a new one rather than reading the old one back.
/// </summary>
public sealed class AgentRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Name { get; set; } = string.Empty;

    /// <summary>SHA-256 of the enrollment key. The key itself is shown once, at creation.</summary>
    public string KeyHash { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string? MachineName { get; set; }
    public string? Version { get; set; }
    public string? OperatingSystem { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastSeenUtc { get; set; }
    public DateTime? FirstConnectedUtc { get; set; }

    /// <summary>
    /// Hashes an enrollment key.
    /// <para>
    /// SHA-256 without a work factor is deliberate and safe here: unlike a password, the key
    /// is 32 bytes of cryptographic randomness we generated ourselves, so there is no
    /// dictionary to run against it — and an agent authenticates on every reconnect, where a
    /// slow KDF would be a self-inflicted denial of service.
    /// </para>
    /// </summary>
    public static string Hash(string enrollmentKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(enrollmentKey)));

    /// <summary>Generates a key with enough entropy that the hashing choice above holds.</summary>
    public static string GenerateKey()
    {
        // URL-safe and double-click friendly: this gets pasted into a config file over RDP.
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}

using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using MssqlRealtime.Infrastructure.Persistence;

namespace MssqlRealtime.Api.Setup;

/// <summary>
/// Creates the single operator account on first run. There is no registration endpoint, so
/// this is the only way an account comes into existence.
/// </summary>
public static class AdminSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        var users = services.GetRequiredService<UserManager<AppUser>>();

        var email = configuration["Admin:Email"] ?? "admin@local";
        var existing = await users.FindByEmailAsync(email);
        if (existing is not null)
        {
            // Upgrades come through here: the account already exists, so the installer's copy
            // of the password has no job left and should not be lying around.
            ClearInstallerPassword(configuration, logger);
            return;
        }

        // A configured password is used as-is; otherwise we generate one and print it once.
        // Shipping a hard-coded default password is how monitoring tools end up on Shodan.
        var configured = configuration["Admin:Password"];
        var password = string.IsNullOrWhiteSpace(configured) ? GeneratePassword() : configured;

        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = "Yönetici"
        };

        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            logger.LogError(
                "Admin account could not be created: {Errors}",
                string.Join(", ", result.Errors.Select(e => e.Description)));
            return;
        }

        ClearInstallerPassword(configuration, logger);

        if (string.IsNullOrWhiteSpace(configured))
        {
            logger.LogWarning(
                "İlk kurulum: yönetici hesabı oluşturuldu.\n"
                + "  Kullanıcı : {Email}\n"
                + "  Parola    : {Password}\n"
                + "Bu parola bir daha gösterilmeyecek — kaydedin ve giriş yaptıktan sonra değiştirin.",
                email, password);
        }
        else
        {
            logger.LogInformation("İlk kurulum: yönetici hesabı ({Email}) yapılandırmadaki parolayla oluşturuldu.", email);
        }
    }

    /// <summary>
    /// Removes the first-run password the installer left in the machine environment.
    /// </summary>
    /// <remarks>
    /// It has to travel that way — a Windows service reads its configuration from the machine
    /// environment, and nothing else survives between the installer and the first start. But
    /// measured 2026-08-06 on Windows 11: that registry value is readable by BUILTIN\Users,
    /// so it must not outlive the account it creates. Nothing reads it after this point; the
    /// password lives in the database as a hash from here on.
    /// </remarks>
    private static void ClearInstallerPassword(IConfiguration configuration, ILogger logger)
    {
        var passwordFile = configuration["Admin:PasswordFile"];

        if (!string.IsNullOrWhiteSpace(passwordFile) && File.Exists(passwordFile))
        {
            try
            {
                File.Delete(passwordFile);
                logger.LogInformation("Kurulum parola dosyası silindi.");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Kurulum parola dosyası silinemedi: {Path}", passwordFile);
            }
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Installs from 0.12.1 and earlier put it in the machine environment; clean that up too.
        try
        {
            var current = Environment.GetEnvironmentVariable("Admin__Password", EnvironmentVariableTarget.Machine);
            if (string.IsNullOrEmpty(current))
            {
                return;
            }

            Environment.SetEnvironmentVariable("Admin__Password", null, EnvironmentVariableTarget.Machine);
            logger.LogInformation("Kurulum parolası makine ortam değişkeninden silindi.");
        }
        catch (Exception ex)
        {
            // A service account without registry rights lands here. Say so loudly: the value
            // staying behind is exactly the finding this method exists to close.
            logger.LogWarning(
                ex,
                "Admin__Password ortam değişkeni silinemedi. Elle silin: "
                + "[Environment]::SetEnvironmentVariable('Admin__Password', $null, 'Machine')");
        }
    }

    private static string GeneratePassword()
    {
        // Ambiguous characters are left out: this gets read off a terminal and typed on a phone.
        const string alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var chars = new char[20];

        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        return new string(chars);
    }
}

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

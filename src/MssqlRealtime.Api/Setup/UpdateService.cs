using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MssqlRealtime.Core.Common;

namespace MssqlRealtime.Api.Setup;

/// <summary>Bir GitHub sürümündeki tek bir dosya.</summary>
public sealed record UpdateAsset(string Name, long Size, string Sha256, string Url);

/// <summary>Güncelleme durumu — arayüzün "Güncelle" düğmesini çizerken bildiği her şey.</summary>
public sealed record UpdateInfo
{
    public required string Current { get; init; }
    public string? Latest { get; init; }
    public bool Available { get; init; }
    public UpdateAsset? Setup { get; init; }

    /// <summary>
    /// Çalışan sürümün kurulum dosyası. Geri dönüş bununla yapılır: aynı, denenmiş kurulum
    /// makinesi çalışır, servisi doğru argümanlarla yeniden kurar. Bulunamazsa güncelleme
    /// yine yapılabilir ama <b>otomatik geri dönüş olmaz</b> ve bu kullanıcıya söylenir.
    /// </summary>
    public UpdateAsset? Rollback { get; init; }

    public bool CanRollback => Rollback is not null;
    public string? Notes { get; init; }

    /// <summary>Sürüm listesi okunamadıysa dolu. Boş liste ile hata asla karıştırılmaz.</summary>
    public string? Error { get; init; }

    /// <summary>Windows dışında güncelleme uygulanamaz; arayüz düğmeyi hiç göstermez.</summary>
    public bool Supported { get; init; }
}

/// <summary>
/// Panelin kendi kendini güncellemesi — <b>elle tetiklenir</b>, zamanlanmış değil.
///
/// Neden ayrı bir süreç: kurulum servisi <c>sc stop</c> + <c>sc delete</c> ile kaldırıp
/// yeniden kuruyor (setup/SunucuIzleme.iss). Yani kurulumu başlatan sürecin kendisi
/// ölüyor. Sağlık kontrolünü ve geri dönüşü yapacak olan, servisten bağımsız yaşayan bir
/// yardımcıdır.
///
/// Neden bir izleme ürününde bu iş ekstra dikkat ister: bozuk bir güncelleme servisi
/// düşürürse müşteri izlemesiz kalır ve <b>bunu kimse fark etmez</b>. Bu yüzden yükseltici
/// "kurdum" demez; yeni sürüm <c>/api/health</c> ile cevap verene kadar bekler, vermezse
/// eski kuruluma geri döner.
/// </summary>
public sealed class UpdateService(IHttpClientFactory http, ILogger<UpdateService> log, IConfiguration cfg)
{
    public const string HttpClientName = "guncelleme";

    private const string Repo = "hzkucuk/MssqlRealtime";
    private const string SetupPrefix = "SunucuIzleme-Setup-";

    /// <summary>Kurulum bittikten sonra sağlık ucuna en fazla bu kadar beklenir.</summary>
    private const int HealthWaitSeconds = 180;

    public static string CurrentVersion =>
        typeof(UpdateService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] ?? "0.0.0";

    /// <summary>
    /// Sürümleri sayısal parçalarıyla karşılaştırır. Bu ürün hiç ön sürüm etiketi yayınlamadı;
    /// olmayan bir şeyi tahmin etmektense yok saymak doğrusu.
    /// </summary>
    public static int CompareVersions(string a, string b)
    {
        static int[] Parse(string v)
        {
            var core = (v ?? string.Empty).TrimStart('v', 'V').Split('-')[0].Split('+')[0];
            return core.Split('.')
                .Select(p => int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0)
                .ToArray();
        }

        var x = Parse(a);
        var y = Parse(b);
        for (var i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            var l = i < x.Length ? x[i] : 0;
            var r = i < y.Length ? y[i] : 0;
            if (l != r) return l.CompareTo(r);
        }

        return 0;
    }

    /// <summary>Kurulum dosyasının adından sürümü okur: SunucuIzleme-Setup-0.18.6.exe → 0.18.6</summary>
    internal static string? VersionFromAssetName(string name) =>
        name.StartsWith(SetupPrefix, StringComparison.OrdinalIgnoreCase)
        && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name[SetupPrefix.Length..^4]
            : null;

    /// <summary>
    /// GitHub'ın sürüm listesini güncelleme durumuna çevirir.
    ///
    /// Ayrı ve saf tutuluyor: ağ olmadan test edilebilsin. Sürüm listesi okunamadığında
    /// çağıran taraf <see cref="UpdateInfo.Error"/> doldurur — "güncelleme yok" ile
    /// "bakamadım" aynı şey değildir.
    /// </summary>
    internal static UpdateInfo Evaluate(string current, JsonElement releases, bool supported)
    {
        UpdateAsset? Pick(JsonElement release, string version)
        {
            if (!release.TryGetProperty("assets", out var assets)) return null;
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                if (!string.Equals(VersionFromAssetName(name), version, StringComparison.OrdinalIgnoreCase)) continue;

                var digest = a.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
                return new UpdateAsset(
                    name,
                    a.TryGetProperty("size", out var s) ? s.GetInt64() : 0,
                    digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : "",
                    a.GetProperty("browser_download_url").GetString() ?? "");
            }

            return null;
        }

        string? latest = null;
        UpdateAsset? setup = null, rollback = null;
        string? notes = null;

        foreach (var r in releases.EnumerateArray())
        {
            if (r.TryGetProperty("draft", out var dr) && dr.GetBoolean()) continue;
            if (r.TryGetProperty("prerelease", out var pr) && pr.GetBoolean()) continue;

            var tag = (r.TryGetProperty("tag_name", out var t) ? t.GetString() : null)?.TrimStart('v', 'V');
            if (string.IsNullOrWhiteSpace(tag)) continue;

            if (latest is null || CompareVersions(tag, latest) > 0)
            {
                latest = tag;
                setup = Pick(r, tag);
                notes = r.TryGetProperty("body", out var b) ? b.GetString() : null;
            }

            // Çalışan sürümün kurulum dosyası geri dönüş paketidir.
            if (CompareVersions(tag, current) == 0) rollback = Pick(r, tag);
        }

        var available = latest is not null && setup is not null && CompareVersions(latest, current) > 0;
        return new UpdateInfo
        {
            Current = current,
            Latest = latest,
            Available = available,
            Setup = available ? setup : null,
            Rollback = available ? rollback : null,
            Notes = available ? notes : null,
            Supported = supported
        };
    }

    public async Task<UpdateInfo> CheckAsync(CancellationToken ct)
    {
        var current = CurrentVersion;
        var supported = OperatingSystem.IsWindows();

        try
        {
            var client = http.CreateClient(HttpClientName);
            using var response = await client.GetAsync(
                $"https://api.github.com/repos/{Repo}/releases?per_page=20", ct);

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateInfo
                {
                    Current = current, Supported = supported,
                    Error = $"sürüm listesi alınamadı (HTTP {(int)response.StatusCode})"
                };
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return Evaluate(current, doc.RootElement, supported);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Sessizce "güncelleme yok" demek yanlış olur: bakamadığımızı söylemeliyiz.
            log.LogWarning(e, "Güncelleme kontrolü başarısız");
            return new UpdateInfo { Current = current, Supported = supported, Error = "sürüm listesine ulaşılamadı" };
        }
    }

    /// <summary>
    /// Güncellemeyi başlatır: indir → sha256 doğrula → ayrık yükselticiyi çalıştır.
    /// Dönerken kurulum <b>henüz yapılmamıştır</b>; yükseltici arkada çalışır ve bu süreç
    /// birazdan öldürülecektir.
    /// </summary>
    public async Task<Result<string>> ApplyAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            return Result<string>.Failure("Güncelleme yalnızca Windows kurulumunda yapılabilir.", "desteklenmiyor");

        var info = await CheckAsync(ct);
        if (info.Error is not null) return Result<string>.Failure(info.Error, "kontrol");
        if (!info.Available || info.Setup is null)
            return Result<string>.Failure("Zaten en güncel sürüm çalışıyor.", "guncel");

        var dir = Path.Combine(DataDirectory, "guncelleme");
        Directory.CreateDirectory(dir);

        var yeni = Path.Combine(dir, info.Setup.Name);
        var indir = await DownloadVerifiedAsync(info.Setup, yeni, ct);
        if (indir.IsFailure) return Result<string>.Failure(indir.Error!, indir.Code);

        string? eski = null;
        if (info.Rollback is not null)
        {
            var yol = Path.Combine(dir, info.Rollback.Name);
            var geri = await DownloadVerifiedAsync(info.Rollback, yol, ct);
            // Geri dönüş paketi indirilemezse güncelleme yine yapılır; yükseltici bunu
            // bilir ve başarısızlıkta yalnız log'a yazar. Kullanıcıya da söylenir.
            if (geri.IsSuccess) eski = yol;
            else log.LogWarning("Geri dönüş paketi indirilemedi: {Hata}", geri.Error);
        }

        var betik = WriteUpgraderScript(dir, yeni, eski, info.Latest!, info.Current);

        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{betik}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = dir
            });
            log.LogWarning("Güncelleme başlatıldı: {Eski} -> {Yeni} (yükseltici pid {Pid})",
                info.Current, info.Latest, p?.Id);
        }
        catch (Exception e)
        {
            log.LogError(e, "Yükseltici başlatılamadı");
            return Result<string>.Failure("Yükseltici başlatılamadı.", "baslatilamadi");
        }

        return Result<string>.Success(info.Latest!);
    }

    private async Task<Result> DownloadVerifiedAsync(UpdateAsset asset, string hedef, CancellationToken ct)
    {
        try
        {
            var client = http.CreateClient(HttpClientName);
            using (var response = await client.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                if (!response.IsSuccessStatusCode)
                    return Result.Failure($"{asset.Name} indirilemedi (HTTP {(int)response.StatusCode})", "indirme");

                await using var gelen = await response.Content.ReadAsStreamAsync(ct);
                await using var dosya = File.Create(hedef);
                await gelen.CopyToAsync(dosya, ct);
            }

            // Sağlama GitHub'ın kendi verdiği özet; imzalı paket olmadığı için tek koruma bu.
            if (string.IsNullOrWhiteSpace(asset.Sha256))
                return Result.Failure($"{asset.Name} için sha256 yok — doğrulanamayan dosya kurulmaz.", "ozet-yok");

            await using (var okunan = File.OpenRead(hedef))
            {
                var hesap = Convert.ToHexString(await SHA256.HashDataAsync(okunan, ct)).ToLowerInvariant();
                if (!string.Equals(hesap, asset.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(hedef);
                    return Result.Failure($"{asset.Name} sha256 tutmadı — dosya silindi.", "ozet");
                }
            }

            return Result.Success();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError(e, "İndirme başarısız: {Ad}", asset.Name);
            return Result.Failure($"{asset.Name} indirilemedi.", "indirme");
        }
    }

    private string DataDirectory =>
        cfg["Storage:DataDirectory"] is { Length: > 0 } d
            ? d
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "SunucuIzleme");

    /// <summary>
    /// Kurulumu yapan, sağlığı bekleyen ve gerekirse geri dönen yardımcı.
    ///
    /// PowerShell seçildi çünkü Windows'ta zaten var ve servis öldükten sonra da yaşamaya
    /// devam eder. Betik yazılıp çalıştırılır; her adımı ProgramData altındaki güncelleme
    /// günlüğüne yazar — güncelleme sırasında panel kapalı olacağı için ekranda gösterilecek
    /// bir yer yok, sonradan bakılabilecek bir iz gerekiyor.
    /// </summary>
    private string WriteUpgraderScript(string dir, string yeniKurulum, string? eskiKurulum, string yeni, string eski)
    {
        var log = Path.Combine(DataDirectory, "logs",
            $"guncelleme-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);

        var saglik = HealthUrl();
        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Continue'");
        sb.AppendLine($"$log = '{log.Replace("'", "''")}'");
        sb.AppendLine("function Yaz($m) { \"$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') | $m\" | Tee-Object -FilePath $log -Append }");
        sb.AppendLine($"Yaz 'guncelleme basliyor: {eski} -> {yeni}'");
        // Cevabin istemciye ulasmasi ve servisin isini bitirmesi icin kisa bir soluk.
        sb.AppendLine("Start-Sleep -Seconds 3");
        sb.AppendLine();
        sb.AppendLine("function SaglikBekle($saniye) {");
        sb.AppendLine("  $bitis = (Get-Date).AddSeconds($saniye)");
        sb.AppendLine("  while ((Get-Date) -lt $bitis) {");
        sb.AppendLine("    try {");
        sb.AppendLine($"      $c = Invoke-RestMethod -Uri '{saglik}' -TimeoutSec 5");
        sb.AppendLine("      if ($c.status -eq 'ok') { return $c.version }");
        sb.AppendLine("    } catch { }");
        sb.AppendLine("    Start-Sleep -Seconds 3");
        sb.AppendLine("  }");
        sb.AppendLine("  return $null");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine($"Yaz 'kurulum calistiriliyor: {Path.GetFileName(yeniKurulum)}'");
        sb.AppendLine($"$k = Start-Process -FilePath '{yeniKurulum.Replace("'", "''")}' -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait -PassThru");
        sb.AppendLine("Yaz \"kurulum bitti, cikis kodu: $($k.ExitCode)\"");
        sb.AppendLine();
        sb.AppendLine($"$s = SaglikBekle {HealthWaitSeconds}");
        sb.AppendLine("if ($s) {");
        sb.AppendLine("  Yaz \"saglik ucu cevap verdi, calisan surum: $s\"");
        sb.AppendLine($"  if ($s -ne '{yeni}') {{ Yaz 'UYARI: calisan surum beklenenden farkli' }}");
        sb.AppendLine("  Yaz 'guncelleme tamam'");
        sb.AppendLine("} else {");
        sb.AppendLine($"  Yaz 'HATA: {HealthWaitSeconds} saniyede saglik ucu cevap vermedi'");

        if (eskiKurulum is not null)
        {
            sb.AppendLine($"  Yaz 'geri donuluyor: {eski}'");
            sb.AppendLine($"  $g = Start-Process -FilePath '{eskiKurulum.Replace("'", "''")}' -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait -PassThru");
            sb.AppendLine("  Yaz \"geri donus kurulumu bitti, cikis kodu: $($g.ExitCode)\"");
            sb.AppendLine($"  $s2 = SaglikBekle {HealthWaitSeconds}");
            sb.AppendLine("  if ($s2) { Yaz \"geri donus BASARILI, calisan surum: $s2\" }");
            sb.AppendLine("  else { Yaz 'GERI DONUS DE BASARISIZ - elle mudahale gerekiyor' }");
        }
        else
        {
            sb.AppendLine("  Yaz 'geri donus paketi yok - elle mudahale gerekiyor'");
        }

        sb.AppendLine("}");

        var yol = Path.Combine(dir, "yukselt.ps1");
        // UTF-8 BOM: PowerShell 5.1 BOM'suz UTF-8'i ANSI sanar ve Turkce karakterler bozulur.
        File.WriteAllText(yol, sb.ToString(), new UTF8Encoding(true));
        return yol;
    }

    /// <summary>
    /// Yükselticinin soracağı sağlık adresi. Servis kendi dinlediği adresi bilir; loopback
    /// her zaman çalışır ve güvenlik duvarına takılmaz.
    /// </summary>
    private string HealthUrl()
    {
        var urls = cfg["ASPNETCORE_URLS"] ?? "";
        var port = 5199;
        foreach (var u in urls.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Uri.TryCreate(u.Replace("0.0.0.0", "127.0.0.1").Replace("*", "127.0.0.1"), UriKind.Absolute, out var uri)
                && uri.Port > 0)
            {
                port = uri.Port;
                break;
            }
        }

        return $"http://127.0.0.1:{port}/api/health";
    }
}

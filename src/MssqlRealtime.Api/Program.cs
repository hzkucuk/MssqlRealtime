using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using MssqlRealtime.Api.Endpoints;
using MssqlRealtime.Api.Security;
using MssqlRealtime.Api.Realtime;
using MssqlRealtime.Api.Setup;
using MssqlRealtime.Core.Abstractions;
using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Core.Modularity;
using MssqlRealtime.Core.Notifications;
using MssqlRealtime.Core.Privacy;
using MssqlRealtime.Infrastructure.Notifications;
using MssqlRealtime.Infrastructure.Persistence;
using MssqlRealtime.Infrastructure.Security;
using MssqlRealtime.Modules.Http;
using MssqlRealtime.Modules.Mssql;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Runs as a long-lived background service, not something someone starts by hand: the poller
// keeps measuring and raising alerts whether or not a phone is connected. The product ships
// as a Windows service (`sc create`, see setup/), and the call below is what makes the
// service lifetime work there. It is a no-op on every other platform, so it stays
// unconditional — running from a terminal on macOS for development still works.
builder.Host.UseWindowsService();

// Behind nginx the app only sees the proxy: without this the scheme looks like http even on
// an HTTPS site, generated links come out wrong and the client IP in the logs is 127.0.0.1.
// Which machines are allowed to say "the real client is somebody else". Measured 2026-08-06:
// with this list empty and the headers trusted from anyone, twelve sign-in attempts carrying
// a different forged X-Forwarded-For each time never produced a 429 — the per-IP rate limiter
// opened a fresh partition for every made-up address. The header is only believed from the
// addresses named here.
var trustedProxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>()
    ?? [];

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();

    // The usual deployment: the reverse proxy sits on this machine and reaches Kestrel over
    // loopback. Nothing else can forge a loopback source address without already being here.
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);

    foreach (var candidate in trustedProxies)
    {
        if (IPAddress.TryParse(candidate, out var address))
        {
            options.KnownProxies.Add(address);
        }
    }
});

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// --- Storage ------------------------------------------------------------------------------
// Everything the product owns lives under one data directory, so a backup is a folder copy
// and a container only needs one volume.
// 🔴 Measured 2026-08-06 (Windows 11): a Windows service does not reliably see machine
// environment variables written after boot — services.exe caches the block — so a freshly
// installed service can start without Storage:DataDirectory ever reaching it. The old
// fallback then pointed at C:\Program Files\SunucuIzleme\data, which is not writable, and the
// service died with an unhandled exception before Serilog had a file to write to: no log, no
// clue, "servis başlamıyor". The installer now passes this on the command line, which a
// service always gets, and the fallback below never points inside the program folder.
var dataDirectory = builder.Configuration["Storage:DataDirectory"];

if (string.IsNullOrWhiteSpace(dataDirectory))
{
    dataDirectory = OperatingSystem.IsWindows()
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SunucuIzleme")
        : Path.Combine(builder.Environment.ContentRootPath, "data");
}

// 🔴 Olculdu 2026-08-10 (Windows): gunlukler C:\Windows\System32\data\logs altina
// yaziliyordu. appsettings.json'daki yol GORELI ("data/logs/app-.log") ve bir Windows
// servisinin calisma dizini System32'dir; LocalSystem oraya yazabildigi icin hata da
// vermiyordu -- yalnizca kimsenin bakmadigi bir yere yaziyordu. Bir arizada "loglara bak"
// adimi bos klasor gosteriyordu. Yol artik veri klasorunun altinda ve MUTLAK.
// Dizin sirasina bagli kalmamak icin dosya havuzunun yolu anahtar adiyla bulunur.
foreach (var anahtar in builder.Configuration.AsEnumerable()
             .Where(k => k.Key.EndsWith(":Args:path", StringComparison.Ordinal))
             .Select(k => k.Key)
             .ToList())
{
    builder.Configuration[anahtar] = Path.Combine(dataDirectory, "logs", "app-.log");
}

try
{
    Directory.CreateDirectory(dataDirectory);
}
catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
{
    // Nothing can be logged to a file when the file's own directory is the problem, so make
    // the console and the event log carry a sentence someone can act on.
    throw new InvalidOperationException(
        $"Veri klasörü açılamadı: {dataDirectory}. Servis bu klasöre yazamıyor. "
        + "Kurulumu yeniden çalıştırın ya da servisi "
        + "--Storage:DataDirectory=\"C:\\ProgramData\\SunucuIzleme\" argümanıyla kurun.",
        ex);
}

// The first password cannot ride in the machine environment either — same invisibility, and
// the registry copy was readable by BUILTIN\Users on top of it. It arrives as a file inside
// the locked-down data directory, and the seeder deletes it the moment the account exists.
var firstRunPasswordFile = Path.Combine(dataDirectory, "ilk-parola");

if (string.IsNullOrWhiteSpace(builder.Configuration["Admin:Password"]) && File.Exists(firstRunPasswordFile))
{
    builder.Configuration["Admin:Password"] = File.ReadAllText(firstRunPasswordFile).Trim();
}

builder.Configuration["Admin:PasswordFile"] = firstRunPasswordFile;

var databasePath = Path.Combine(dataDirectory, "mssqlrealtime.db");
// Migrations live in the host, not in Infrastructure: the schema is the union of the
// platform's tables and every registered module's, and only the host knows which modules
// are in this build.
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(
    $"Data Source={databasePath}",
    sqlite => sqlite.MigrationsAssembly("MssqlRealtime.Api")));

// Modules resolve the base DbContext so they never reference the host's concrete type.
builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>());

// The key ring must survive restarts: without it every stored SQL password becomes unreadable.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")))
    .SetApplicationName("MssqlRealtime");

// --- Platform services --------------------------------------------------------------------
builder.Services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();

// Panel-wide privacy setting, read on every poll cycle: a singleton serving it from memory.
builder.Services.AddSingleton<IStatementPrivacy, StatementPrivacyService>();
builder.Services.AddSingleton<IAlertEngine, AlertEngine>();
builder.Services.AddSingleton<IRealtimePublisher, SignalRPublisher>();
builder.Services.AddSingleton<IModuleRegistry, ModuleRegistry>();

// --- Alerting and notifications -------------------------------------------------------------
// A raised alert goes three ways: connected apps, persisted history, and the notification
// channels below — the last one being how it reaches a phone with the app closed.
builder.Services.AddSingleton<AlertBroadcaster>();
builder.Services.AddSingleton<IAlertSink>(sp => sp.GetRequiredService<AlertBroadcaster>());
builder.Services.AddHostedService<AlertDeliveryService>();
builder.Services.AddHostedService<AlertMaintenanceService>();
builder.Services.AddHostedService<NotificationRetryService>();


builder.Services.AddScoped<IAlertStore, EfAlertStore>();

// History for the reports screen: the recorder buffers what pollers measure and writes a
// row a minute; maintenance folds those into hours and days and drops anything past two
// years. Both are hosted services, so a panel nobody opens still builds its history.
builder.Services.AddSingleton<MetricRecorder>();
builder.Services.AddSingleton<IMetricSink>(sp => sp.GetRequiredService<MetricRecorder>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<MetricRecorder>());
builder.Services.AddHostedService<MetricMaintenanceService>();
builder.Services.AddScoped<IMetricStore, EfMetricStore>();
builder.Services.AddScoped<INotificationSettingsStore, NotificationSettingsStore>();
builder.Services.AddScoped<INotificationOutbox, NotificationOutbox>();
builder.Services.AddSingleton<INotificationDispatcher, NotificationDispatcher>();

// Registering a channel is all it takes for it to appear in the settings screen.
builder.Services.AddSingleton<INotificationChannel, TelegramChannel>();
builder.Services.AddSingleton<INotificationChannel, EmailChannel>();
builder.Services.AddSingleton<INotificationChannel, WebhookChannel>();

// Short timeout: a hanging webhook must not block the delivery queue behind it.
// GitHub sürüm listesi ve kurulum dosyası. User-Agent zorunlu: GitHub API başlıksız
// isteği 403 ile reddeder. Zaman aşımı uzun, çünkü kurulum dosyası ~40 MB.
builder.Services.AddHttpClient(UpdateService.HttpClientName, c =>
{
    c.Timeout = TimeSpan.FromMinutes(10);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("SunucuIzleme-Guncelleme");
    c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
});
builder.Services.AddSingleton<UpdateService>();

builder.Services.AddHttpClient(TelegramChannel.ChannelId, c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient(WebhookChannel.ChannelId, c => c.Timeout = TimeSpan.FromSeconds(15));

// --- Tool modules -------------------------------------------------------------------------
// Adding a tool is one line here plus its own project. Nothing else in the host changes.
builder.Services.AddToolModule<MssqlModule>(builder.Configuration);
builder.Services.AddToolModule<HttpModule>(builder.Configuration);

// --- Identity -----------------------------------------------------------------------------
// A single operator account. Registration is disabled below; the account is seeded at startup.
// AddIdentityApiEndpoints wires the bearer + cookie schemes and their defaults together —
// doing it by hand leaves no default challenge scheme and every guarded route returns 500.
builder.Services.AddAuthorization();
builder.Services.AddSingleton<CaptchaService>();

// Rate limiting on the auth endpoints. Identity already locks the account after five bad
// passwords; this stops the attempts from arriving fast enough to be worth making, and it
// protects the endpoint itself rather than just the account behind it.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

builder.Services.AddIdentityApiEndpoints<AppUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 10;
        options.Password.RequireNonAlphanumeric = false;
        options.SignIn.RequireConfirmedEmail = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppDbContext>();

// SignalR cannot set an Authorization header on a WebSocket handshake, so browser and Tauri
// clients pass the token as a query parameter. Accept it only for the hub path.
builder.Services.Configure<BearerTokenOptions>(IdentityConstants.BearerScheme, options =>
{
    options.Events.OnMessageReceived = context =>
    {
        var token = context.Request.Query["access_token"];
        if (!string.IsNullOrEmpty(token)
            && context.Request.Path.StartsWithSegments(ToolsHub.Path, StringComparison.OrdinalIgnoreCase))
        {
            context.Token = token;
        }

        return Task.CompletedTask;
    };
});

builder.Services.AddSignalR().AddJsonProtocol(options =>
    options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

// Tauri apps ship their own origin; the browser build is served from this host itself.
const string CorsPolicy = "app";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:1420", "http://localhost:5173", "tauri://localhost", "http://tauri.localhost"];

builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

// --- Schema and seed ----------------------------------------------------------------------
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // Migrate, not EnsureCreated: measured 2026-08-05, EnsureCreated silently leaves an
    // existing database untouched, so a release that adds a table breaks on upgrade only.
    try
    {
        await db.Database.MigrateAsync();
    }
    catch (Exception ex)
    {
        // Measured 2026-08-06: when the data directory's permissions are wrong this throws
        // "SQLite Error 14: unable to open database file" and the service dies with nothing
        // written anywhere — the log file lives in the same unreachable directory. Naming the
        // path and the fix turns a silent death into a five-second diagnosis in Event Viewer.
        throw new InvalidOperationException(
            $"Veritabanı açılamadı: {databasePath}. Servis bu dosyaya yazamıyor. "
            + "Yönetici PowerShell'de izinleri varsayılana döndürün: "
            + $"icacls \"{dataDirectory}\" /reset /T /C /Q",
            ex);
    }
    await AdminSeeder.SeedAsync(scope.ServiceProvider, app.Configuration, app.Logger);
}

// Before the first poll: a poller that starts with the built-in default would write full
// statement text for one cycle on a panel that had switched it off.
var statementPrivacy = await app.Services.GetRequiredService<IStatementPrivacy>().RefreshAsync();
app.Logger.LogInformation("Sorgu metni saklama: {Storage}", statementPrivacy);

// Must run before anything that reads the scheme or the client address.
app.UseForwardedHeaders();

// A proxy on another machine has to be named, or every client behind it shares one rate-limit
// bucket and the logs show the proxy instead of the caller. Silence here would look like it
// worked, so say it once at startup where the operator can see it.
if (trustedProxies.Length == 0
    && (app.Configuration["ASPNETCORE_URLS"] ?? string.Empty).Contains("0.0.0.0", StringComparison.Ordinal))
{
    app.Logger.LogWarning(
        "Panel dış arayüze bağlı ama tanımlı ters vekil sunucu yok. Vekil başka bir makinedeyse "
        + "ForwardedHeaders__KnownProxies__0 ile IP'sini verin; verilmezse istemci adresi vekilin "
        + "adresi olarak görünür ve hız sınırı hepsini tek kovaya koyar.");
}

// Set on every response, including static files and errors. No CSP beyond frame-ancestors:
// the browser client connects to whichever customer hub you sign in to, so connect-src cannot
// be enumerated ahead of time — a list that guesses would break panel switching silently.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "no-referrer";
    headers["X-Frame-Options"] = "DENY";
    headers["Content-Security-Policy"] = "frame-ancestors 'none'";

    await next();
});

app.UseSerilogRequestLogging();
app.UseCors(CorsPolicy);

// Single-operator product: no self-service registration.
app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/api/auth/register", StringComparison.OrdinalIgnoreCase),
    branch => branch.Run(context =>
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }));

app.UseRateLimiter();

// Runs before authentication: a wrong captcha should never reach the password check.
app.UseMiddleware<CaptchaMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

// --- Endpoints ----------------------------------------------------------------------------
app.MapGroup("/api/auth").MapIdentityApi<AppUser>().RequireRateLimiting("auth");

// Issued on demand; the client asks for one when a sign-in comes back saying it is required.
app.MapGet("/api/auth/captcha", (CaptchaService captcha) =>
{
    var challenge = captcha.Create();
    return Results.Ok(new { token = challenge.Token, svg = challenge.Svg });
}).RequireRateLimiting("auth");

// Lets the sign-in screen show the captcha up front instead of after a rejected attempt.
app.MapGet("/api/auth/captcha/required", (HttpContext context) =>
    Results.Ok(new { required = CaptchaMiddleware.RequiresCaptcha(CaptchaMiddleware.Key(context)) }))
    .RequireRateLimiting("auth");

// The version ships here rather than behind authorisation: "which build is on this box"
// is the first question of every support call, and it has to be answerable before sign-in.
// It is read from the assembly, so Directory.Build.props stays the only place it is set.
var serverVersion = typeof(Program).Assembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    .Split('+')[0]  // strip the source-revision suffix the SDK appends
    ?? "bilinmiyor";

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    version = serverVersion,
    utc = DateTimeOffset.UtcNow
}));

// The client asks what tools this build has and renders itself from the answer.
app.MapGet("/api/modules", (IModuleRegistry registry) => Results.Ok(registry.Describe()))
    .RequireAuthorization();

app.MapNotificationEndpoints();
app.MapMetricEndpoints();
app.MapPrivacyEndpoints();
app.MapUpdateEndpoints();
app.MapToolModules();

app.MapHub<ToolsHub>(ToolsHub.Path);


// The same SvelteKit build that ships inside Tauri is served here for desktop browsers.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Exposed so integration tests can boot the real host.</summary>
public partial class Program;

using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MssqlRealtime.Api.Realtime;
using MssqlRealtime.Api.Setup;
using MssqlRealtime.Core.Abstractions;
using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Core.Modularity;
using MssqlRealtime.Infrastructure.Persistence;
using MssqlRealtime.Infrastructure.Security;
using MssqlRealtime.Modules.Mssql;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Runs as a long-lived background service, not something someone starts by hand: the poller
// keeps measuring and raising alerts whether or not a phone is connected.
//   Linux : systemd    (deploy/systemd/mssqlrealtime.service)
//   Docker: restart policy (docker-compose.yml)
//   Windows: `sc create` — the call below is what makes the service lifetime work there.
// It is a no-op on every other platform, so it is safe to leave in unconditionally.
builder.Host.UseWindowsService();

// Behind nginx the app only sees the proxy: without this the scheme looks like http even on
// an HTTPS site, generated links come out wrong and the client IP in the logs is 127.0.0.1.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // The proxy is not in the known-network list by default — and when nginx runs in its own
    // container it arrives from an arbitrary bridge address. Clearing these accepts the
    // headers from it. Safe only because Kestrel is never exposed directly (see deploy/).
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// --- Storage ------------------------------------------------------------------------------
// Everything the product owns lives under one data directory, so a backup is a folder copy
// and a container only needs one volume.
var dataDirectory = builder.Configuration["Storage:DataDirectory"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "data");
Directory.CreateDirectory(dataDirectory);

var databasePath = Path.Combine(dataDirectory, "mssqlrealtime.db");
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));

// Modules resolve the base DbContext so they never reference the host's concrete type.
builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>());

// The key ring must survive restarts: without it every stored SQL password becomes unreadable.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")))
    .SetApplicationName("MssqlRealtime");

// --- Platform services --------------------------------------------------------------------
builder.Services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
builder.Services.AddSingleton<IAlertEngine, AlertEngine>();
builder.Services.AddSingleton<IRealtimePublisher, SignalRPublisher>();
builder.Services.AddSingleton<IModuleRegistry, ModuleRegistry>();

// --- Tool modules -------------------------------------------------------------------------
// Adding a tool is one line here plus its own project. Nothing else in the host changes.
builder.Services.AddToolModule<MssqlModule>(builder.Configuration);

// --- Identity -----------------------------------------------------------------------------
// A single operator account. Registration is disabled below; the account is seeded at startup.
// AddIdentityApiEndpoints wires the bearer + cookie schemes and their defaults together —
// doing it by hand leaves no default challenge scheme and every guarded route returns 500.
builder.Services.AddAuthorization();

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
    await db.Database.EnsureCreatedAsync();
    await AdminSeeder.SeedAsync(scope.ServiceProvider, app.Configuration, app.Logger);
}

// Must run before anything that reads the scheme or the client address.
app.UseForwardedHeaders();

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

app.UseAuthentication();
app.UseAuthorization();

// --- Endpoints ----------------------------------------------------------------------------
app.MapGroup("/api/auth").MapIdentityApi<AppUser>();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    utc = DateTimeOffset.UtcNow
}));

// The client asks what tools this build has and renders itself from the answer.
app.MapGet("/api/modules", (IModuleRegistry registry) => Results.Ok(registry.Describe()))
    .RequireAuthorization();

app.MapToolModules();

app.MapHub<ToolsHub>(ToolsHub.Path);

// The same SvelteKit build that ships inside Tauri is served here for desktop browsers.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Exposed so integration tests can boot the real host.</summary>
public partial class Program;

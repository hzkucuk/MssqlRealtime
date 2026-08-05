using MssqlRealtime.Agent;
using MssqlRealtime.Modules.Mssql.Probes;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// Runs as a Windows service on the customer's server; a no-op elsewhere.
builder.Services.AddWindowsService(options => options.ServiceName = "MssqlRealtimeAgent");

builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

var agentOptions = new AgentOptions();
builder.Configuration.GetSection("Agent").Bind(agentOptions);
builder.Services.AddSingleton(agentOptions);

// The same probe implementations the hub uses — that is what makes an agent-monitored server
// produce identical numbers to a directly polled one.
builder.Services.AddSingleton<ISqlProbe, InstanceProbe>();
builder.Services.AddSingleton<ISqlProbe, ResourcesProbe>();
builder.Services.AddSingleton<ISqlProbe, SessionsProbe>();
builder.Services.AddSingleton<ISqlProbe, RequestsProbe>();
builder.Services.AddSingleton<ISqlProbe, BlockingProbe>();
builder.Services.AddSingleton<ISqlProbe, WaitStatsProbe>();
builder.Services.AddSingleton<ISqlProbe, DatabasesProbe>();
builder.Services.AddSingleton<ISqlProbe, ServicesProbe>();

builder.Services.AddSingleton<AgentSqlPoller>();
builder.Services.AddHostedService<AgentWorker>();

var host = builder.Build();
await host.RunAsync();

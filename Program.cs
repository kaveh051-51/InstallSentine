using InstallSentinel.Configuration;
using InstallSentinel.Services;
using InstallSentinel.Services.Interfaces;
using InstallSentinel.Services.Logging;
using InstallSentinel.UI;
using InstallSentinel.UI.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;

var builder = Host.CreateApplicationBuilder(args);

// Configuration
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
builder.Services.Configure<AppConfig>(builder.Configuration);

// Agent Logger (AI-friendly structured logging)
builder.Services.AddSingleton<AgentLogger>();

// Services
builder.Services.AddHttpClient<IVirusTotalService, VirusTotalService>();
builder.Services.AddSingleton<IPrivilegeService, PrivilegeService>();
builder.Services.AddSingleton<IProcessLauncher, ProcessLauncherService>();
builder.Services.AddSingleton<INoiseFilterService, NoiseFilterService>();
builder.Services.AddSingleton<IEtwMonitorEngine, EtwMonitorEngine>();
builder.Services.AddSingleton<IRollbackGenerator, RollbackGenerator>();

// UI Components
builder.Services.AddSingleton<LiveEventTable>();
builder.Services.AddSingleton<SummaryTreeRenderer>();
builder.Services.AddSingleton<TerminalApp>();

// Logging
builder.Services.AddLogging(logging =>
{
    logging.ClearProviders();
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Warning);
});

var host = builder.Build();

// Initialize Agent Logger
var agentLogger = host.Services.GetRequiredService<AgentLogger>();
agentLogger.Info("SYSTEM", "InstallSentinel starting...");

// Ensure running as admin
var privilegeService = host.Services.GetRequiredService<IPrivilegeService>();
if (!privilegeService.IsRunningAsAdmin())
{
    agentLogger.Warn("SYSTEM", "Not running as admin - limited functionality");
    AnsiConsole.MarkupLine("[red]Error: This application must be run as Administrator.[/]");
    AnsiConsole.MarkupLine("[yellow]Right-click the executable and select 'Run as administrator'.[/]");
    return 1;
}

// Run the application
var app = host.Services.GetRequiredService<TerminalApp>();
var exitCode = await app.RunAsync(args);

agentLogger.Info("SYSTEM", $"Application exited with code {exitCode}");
agentLogger.Dispose();

return exitCode;
namespace InstallSentinel.UI;

using InstallSentinel.Configuration;
using InstallSentinel.Models;
using InstallSentinel.Models.Enums;
using InstallSentinel.Services.Interfaces;
using InstallSentinel.UI.Components;
using InstallSentinel.Common.Helpers;
using InstallSentinel.Services.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;
using System.Diagnostics;
using System.Threading.Channels;

public sealed class TerminalApp(
    IServiceProvider services,
    ILogger<TerminalApp> logger,
    IPrivilegeService privilegeService,
    IVirusTotalService virusTotalService,
    IProcessLauncher processLauncher,
    IEtwMonitorEngine etwMonitor,
    INoiseFilterService noiseFilter,
    IRollbackGenerator rollbackGenerator,
    LiveEventTable liveTable,
    SummaryTreeRenderer summaryRenderer,
    IOptions<AppConfig> config,
    AgentLogger agentLogger)
{
    private readonly IServiceProvider _services = services;
    private readonly ILogger<TerminalApp> _logger = logger;
    private readonly IPrivilegeService _privilegeService = privilegeService;
    private readonly IVirusTotalService _virusTotalService = virusTotalService;
    private readonly IProcessLauncher _processLauncher = processLauncher;
    private readonly IEtwMonitorEngine _etwMonitor = etwMonitor;
    private readonly INoiseFilterService _noiseFilter = noiseFilter;
    private readonly IRollbackGenerator _rollbackGenerator = rollbackGenerator;
    private readonly LiveEventTable _liveTable = liveTable;
    private readonly SummaryTreeRenderer _summaryRenderer = summaryRenderer;
    private readonly AppConfig _config = config.Value;
    private readonly AgentLogger _agentLogger = agentLogger;

    private ProcessNode? _processTree;
    private readonly List<SystemEvent> _allEvents = [];
    private DateTime _startTime;
    private string _installerPath = "";
    private string _installerSha256 = "";
    private CancellationTokenSource? _cts;

    public async Task<int> RunAsync(string[] args)
    {
        try
        {
            _agentLogger.Ui("UI", "Application started");
            _agentLogger.Ui("UI", $"Admin: {_privilegeService.IsRunningAsAdmin()}");
            AnsiConsole.Clear();
            ShowBanner();

            // Screen 1: Target Selection & Pre-Scan
            var targetPath = await SelectTargetAsync();
            if (string.IsNullOrEmpty(targetPath))
                return 0;

            _installerPath = targetPath;
            _installerSha256 = await HashUtils.ComputeSha256Async(targetPath);
            _agentLogger.Info("APP", $"Target selected: {targetPath}");
            _agentLogger.Info("APP", $"SHA256: {_installerSha256}");

            var vtReport = await ScanWithVirusTotalAsync(_installerPath, _installerSha256);

            _agentLogger.Info("APP", $"VirusTotal: {vtReport?.ThreatStatus} ({vtReport?.Positives}/{vtReport?.Total})");
            if (vtReport?.ThreatStatus == ThreatStatus.Malicious)
            {
                AnsiConsole.MarkupLine("[bold red]⚠ VIRUS TOTAL: MALICIOUS DETECTED![/]");
                if (!AnsiConsole.Confirm("Continue anyway?", false))
                    return 1;
            }

            _agentLogger.Ui("UI", "Application started");
            _agentLogger.Ui("UI", $"Admin: {_privilegeService.IsRunningAsAdmin()}");
            AnsiConsole.Clear();
            ShowBanner();
            AnsiConsole.MarkupLine($"[green]✓ Target:[/] {_installerPath}");
            AnsiConsole.MarkupLine($"[green]✓ SHA256:[/] {_installerSha256[..16]}...");

            // Screen 2: Live Monitoring
            _startTime = DateTime.UtcNow;
            _cts = new CancellationTokenSource();
            _agentLogger.Ui("UI", "Starting live monitoring...");

            await RunLiveMonitoringAsync(_cts.Token);

            // Screen 3: Summary & Rollback
            await ShowSummaryAndRollbackAsync();

            return 0;
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("\n[yellow]Operation cancelled by user.[/]");
            return 130;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Application error");
            AnsiConsole.MarkupLine($"\n[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private void ShowBanner()
    {
        var banner = @"
  ___           _        _ll   ____             _   _inel 
 |_ _|_ __  ___| |_ __ _| | | / ___|  ___ _ __ | |_(_)_ __   ___| |
  | || '_ \/ __| __/ _` | | | \___ \ / _ \ '_ \| __| | '_ \ / _ \ |
  | || | | \__ \ || (_| | | |  ___) |  __/ | | | |_| | | | |  __/ |
 |___|_| |_|___/\__\__,_|_|_| |____/ \___|_| |_|\__|_|_| |_|\___|_|
";
        AnsiConsole.Write(new FigletText(banner).Color(Color.Cyan1).Centered());
        AnsiConsole.WriteLine();

        var rule = new Rule("[bold cyan]InstallSentinel v1.0[/]")
            .RuleStyle("purple")
            .Centered();
        AnsiConsole.Write(rule);

        if (_privilegeService.IsRunningAsAdmin())
        {
            AnsiConsole.MarkupLine("[black on green] ADMIN ELEVATED [/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[black on red] NOT ADMIN - Limited functionality [/]");
        }
        AnsiConsole.WriteLine();
    }

    private static Task<string?> SelectTargetAsync()
    {
        AnsiConsole.MarkupLine("[bold cyan]Step 1: Select Installer[/]");
        AnsiConsole.WriteLine();

        var path = AnsiConsole.Prompt(
            new TextPrompt<string>("Enter path to installer (.exe/.msi):")
                .PromptStyle("cyan")
                .ValidationErrorMessage("[red]Invalid path[/]")
                .Validate(path =>
                {
                    if (string.IsNullOrWhiteSpace(path))
                        return ValidationResult.Error("Path cannot be empty");

                    path = path.Trim('"', '\'');
                    if (!File.Exists(path))
                        return ValidationResult.Error("File not found");

                    var ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext is not ".exe" and not ".msi")
                        return ValidationResult.Error("Must be .exe or .msi file");

                    return ValidationResult.Success();
                }));

        return Task.FromResult<string?>(path.Trim('"', '\''));
    }

    private async Task<VirusTotalReport?> ScanWithVirusTotalAsync(string filePath, string sha256)
    {
        if (!_config.VirusTotal.Enabled || string.IsNullOrEmpty(_config.VirusTotal.ApiKey))
        {
            AnsiConsole.MarkupLine("[yellow]VirusTotal scanning disabled (no API key configured)[/]");
            return null;
        }

        AnsiConsole.WriteLine();
        var result = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan"))
            .StartAsync($"[cyan]Checking VirusTotal Hash (SHA256: {sha256[..16]}...)[/]",
                async ctx =>
                {
                    try
                    {
                        return await _virusTotalService.ScanHashAsync(sha256, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "VirusTotal scan failed");
                        return null;
                    }
                });

        if (result != null)
        {
            var statusColor = result.ThreatStatus switch
            {
                ThreatStatus.Malicious => "red",
                ThreatStatus.Suspicious => "yellow",
                ThreatStatus.Benign => "green",
                _ => "grey"
            };

            AnsiConsole.MarkupLine($"[bold green]✓ VirusTotal Scan:[/] [{statusColor}]{result.ThreatStatus}[/] ({result.Positives}/{result.Total})");
            AnsiConsole.MarkupLine($"[dim]Permalink: {result.Permalink}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]⚠ VirusTotal scan unavailable[/]");
        }

        return result;
    }

    private async Task RunLiveMonitoringAsync(CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine("[bold cyan]Step 2: Live Monitoring[/]");
        AnsiConsole.WriteLine();

        _processLauncher.ProcessSpawned += OnProcessSpawned;
        _etwMonitor.EventReceived += OnEventReceived;
        _etwMonitor.ErrorOccurred += OnEtwError;

        // Pre-launch: create ETW session BEFORE starting the installer
        // so we don't miss early events (registry/file writes that happen immediately)
        var eventChannel = Channel.CreateUnbounded<SystemEvent>();
        var monitorConfig = new MonitorConfiguration
        {
            RootProcessId = 0, // Will be set after launch
            ProcessTreePids = new HashSet<int>(),
            SessionName = _config.Etw.SessionName,
            BufferSizeMb = _config.Etw.BufferSizeMb,
            MinBuffers = _config.Etw.MinBuffers,
            MaxBuffers = _config.Etw.MaxBuffers,
            FlushTimer = _config.Etw.FlushTimer,
            KernelProviders = _config.Etw.KernelProviders
        };

        try
        {
            await _etwMonitor.StartAsync(monitorConfig, eventChannel.Writer, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ETW monitor error");
            _agentLogger.Error("ETW", "ETW monitor error", ex);
        }

        // Now start the installer
        AnsiConsole.MarkupLine($"[cyan]Launching installer...[/]");
        var launchResult = await _processLauncher.LaunchAndTrackAsync(_installerPath, null, null, cancellationToken);

        if (!launchResult.Success)
        {
            AnsiConsole.MarkupLine($"[red]Failed to launch: {launchResult.ErrorMessage}[/]");
            return;
        }

        var rootPid = launchResult.RootProcessId;
        _liveTable.SetContext(rootPid, launchResult.RootProcessName);

        // Add root PID and any tracked children to the ETW engine
        _etwMonitor.AddTrackedPid(rootPid, launchResult.RootProcessName, 0);
        foreach (var pid in _processLauncher.GetTrackedPids())
        {
            if (pid != rootPid)
                _etwMonitor.AddTrackedPid(pid, "child", rootPid);
        }

        // Live display loop
        await AnsiConsole.Live(_liveTable.GetTable())
            .AutoClear(false)
            .Overflow(VerticalOverflow.Ellipsis)
            .StartAsync(async ctx =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    ctx.UpdateTarget(_liveTable.GetTable());
                    ctx.Refresh();

                    // Check if root process exited
                    if (await _processLauncher.WaitForProcessTreeAsync(rootPid, TimeSpan.FromSeconds(1), cancellationToken))
                    {
                        break;
                    }

                    await Task.Delay(_config.Ui.TableRefreshRateMs, cancellationToken);
                }
            });

        // Stop monitoring
        await _etwMonitor.StopAsync(cancellationToken);
        _cts?.Cancel();

        // Collect all events from channel
        await CollectEventsAsync(cancellationToken);
    }

    private void OnProcessSpawned(ProcessNode node)
    {
        // Propagate child PID to ETW engine so it trackés registry/file events from this process
        _etwMonitor.AddTrackedPid(node.ProcessId, node.ProcessName, node.ParentProcessId);
        _agentLogger.Info("UI", $"ETW now tracking child PID: {node.ProcessId} ({node.ProcessName})");
    }

    private void OnEventReceived(object? sender, SystemEvent evt)
    {
        _allEvents.Add(evt);
        _liveTable.AddEvent(evt);
    }

    private void OnEtwError(object? sender, Exception ex)
    {
        _logger.LogError(ex, "ETW error");
        _agentLogger.Error("ETW", "ETW error", ex);
    }

    private static async Task CollectEventsAsync(CancellationToken cancellationToken)
    {
        // Events are already collected via EventReceived
        await Task.CompletedTask;
    }

    private async Task ShowSummaryAndRollbackAsync()
    {
        AnsiConsole.Clear();
        ShowBanner();

        AnsiConsole.MarkupLine("[bold cyan]Step 3: Analysis Complete[/]");
        AnsiConsole.WriteLine();

        // Build process tree
        _processTree = await BuildProcessTreeAsync();

        // Render process tree
        var tree = SummaryTreeRenderer.RenderProcessTree(_processTree!);
        AnsiConsole.Write(tree);
        AnsiConsole.WriteLine();

        // Render summary
        var report = CreateMonitoringReport();
        var summaryPanel = SummaryTreeRenderer.RenderSummary(report);
        AnsiConsole.Write(summaryPanel);
        AnsiConsole.WriteLine();

        // Generate rollback script
        AnsiConsole.MarkupLine("[cyan]Generating rollback script...[/]");
        var rollbackPath = await _rollbackGenerator.GenerateRollbackScriptAsync(report);
        report.RollbackScriptPath = rollbackPath;
        report.RollbackScriptGenerated = true;

        var rollbackPanel = SummaryTreeRenderer.RenderRollbackInfo(report);
        AnsiConsole.Write(rollbackPanel);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[green]Done! Press any key to exit...[/]");
        Console.ReadKey(true);
    }

    private async Task<ProcessNode> BuildProcessTreeAsync()
    {
        // Get process tree from launcher
        var tree = await _processLauncher.GetProcessTreeAsync(_processTree?.ProcessId ?? 0, CancellationToken.None);
        return tree;
    }

    private MonitoringReport CreateMonitoringReport()
    {
        return new MonitoringReport
        {
            InstallerPath = _installerPath,
            InstallerSha256 = _installerSha256,
            StartTime = _startTime,
            EndTime = DateTime.UtcNow,
            ProcessTree = _processTree!,
            AllEvents = _allEvents,
            VirusTotalReport = null, // Would be passed from scan
            Metadata = new Dictionary<string, object>
            {
                ["session"] = _config.Etw.SessionName,
                ["filter_stats"] = _noiseFilter.GetStatistics()
            }
        };
    }
}
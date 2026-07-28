namespace InstallSentinel.UI;

using InstallSentinel.Configuration;
using InstallSentinel.Models;
using InstallSentinel.Models.Enums;
using InstallSentinel.Services.Interfaces;
using InstallSentinel.UI.Components;
using InstallSentinel.Common.Helpers;
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
    IOptions<AppConfig> config)
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
            AnsiConsole.Clear();
            ShowBanner();

            // Screen 1: Target Selection & Pre-Scan
            var targetPath = await SelectTargetAsync();
            if (string.IsNullOrEmpty(targetPath))
                return 0;

            _installerPath = targetPath;
            _installerSha256 = await HashUtils.ComputeSha256Async(targetPath);

            var vtReport = await ScanWithVirusTotalAsync(_installerPath, _installerSha256);

            if (vtReport?.ThreatStatus == ThreatStatus.Malicious)
            {
                AnsiConsole.MarkupLine("[bold red]⚠ VIRUS TOTAL: MALICIOUS DETECTED![/]");
                if (!AnsiConsole.Confirm("Continue anyway?", false))
                    return 1;
            }

            AnsiConsole.Clear();
            ShowBanner();
            AnsiConsole.MarkupLine($"[green]✓ Target:[/] {_installerPath}");
            AnsiConsole.MarkupLine($"[green]✓ SHA256:[/] {_installerSha256[..16]}...");

            // Screen 2: Live Monitoring
            _startTime = DateTime.UtcNow;
            _cts = new CancellationTokenSource();

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

        // Start the installer
        AnsiConsole.MarkupLine($"[cyan]Launching installer...[/]");
        var launchResult = await _processLauncher.LaunchAndTrackAsync(_installerPath, null, null, cancellationToken);

        if (!launchResult.Success)
        {
            AnsiConsole.MarkupLine($"[red]Failed to launch: {launchResult.ErrorMessage}[/]");
            return;
        }

        var rootPid = launchResult.RootProcessId;
        _liveTable.SetContext(rootPid, launchResult.RootProcessName);

        // Setup ETW monitoring
        var monitorConfig = new MonitorConfiguration
        {
            RootProcessId = rootPid,
            ProcessTreePids = _processLauncher.GetTrackedPids(),
            SessionName = _config.Etw.SessionName,
            BufferSizeMb = _config.Etw.BufferSizeMb,
            MinBuffers = _config.Etw.MinBuffers,
            MaxBuffers = _config.Etw.MaxBuffers,
            FlushTimer = _config.Etw.FlushTimer,
            KernelProviders = _config.Etw.KernelProviders
        };

        var eventChannel = Channel.CreateUnbounded<SystemEvent>();

        _ = Task.Run(async () =>
        {
            try
            {
                await _etwMonitor.StartAsync(monitorConfig, eventChannel.Writer, cancellationToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ETW monitor error");
            }
        }, cancellationToken);

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
        // Track process tree
    }

    private void OnEventReceived(object? sender, SystemEvent evt)
    {
        _allEvents.Add(evt);
        _liveTable.AddEvent(evt);
    }

    private void OnEtwError(object? sender, Exception ex)
    {
        _logger.LogError(ex, "ETW error");
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
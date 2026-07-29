namespace InstallSentinel.Services.Interfaces;

using InstallSentinel.Models;
using System.Threading.Channels;

public interface IEtwMonitorEngine : IAsyncDisposable
{
    event EventHandler<SystemEvent>? EventReceived;
    event EventHandler<Exception>? ErrorOccurred;

    Task StartAsync(
        MonitorConfiguration configuration,
        ChannelWriter<SystemEvent> eventChannel,
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
    bool IsRunning { get; }
    string SessionName { get; }
    EtwStatistics GetStatistics();

    /// <summary>
    /// Dynamically add a PID to the tracked set (called when ProcessLauncherService detects a new child).
    /// </summary>
    void AddTrackedPid(int pid, string processName, int parentPid);
}

public record EtwStatistics
{
    public required long EventsReceived { get; init; }
    public required long EventsFiltered { get; init; }
    public required long EventsPublished { get; init; }
    public required long EventsDropped { get; init; }
    public required TimeSpan Uptime { get; init; }
    public required int BufferedEvents { get; init; }
}
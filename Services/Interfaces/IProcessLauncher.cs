namespace InstallSentinel.Services.Interfaces;

using InstallSentinel.Models;
using System.Diagnostics;

public interface IProcessLauncher
{
    Task<ProcessLaunchResult> LaunchAndTrackAsync(
        string filePath,
        string? arguments = null,
        string? workingDirectory = null,
        Action<int, string>? onProcessStarted = null,
        CancellationToken cancellationToken = default);

    Task<ProcessNode> GetProcessTreeAsync(int rootPid, CancellationToken cancellationToken = default);
    Task<bool> WaitForProcessTreeAsync(int rootPid, TimeSpan timeout, CancellationToken cancellationToken = default);
    IReadOnlySet<int> GetTrackedPids();
    event Action<ProcessNode>? ProcessSpawned;
    event Action<int>? ProcessExited;
}

public record ProcessLaunchResult
{
    public required bool Success { get; init; }
    public required int RootProcessId { get; init; }
    public required string RootProcessName { get; init; }
    public Process? Process { get; init; }
    public string? ProcessPath { get; init; }
    public DateTime StartTime { get; init; }
    public string? ErrorMessage { get; init; }
    public ProcessNode? ProcessTree { get; init; }
}
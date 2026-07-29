namespace InstallSentinel.Services;
using InstallSentinel.Services.Logging;
using InstallSentinel.Services;
using InstallSentinel.Models;
using InstallSentinel.Models.Enums;
using InstallSentinel.Services.Interfaces;
using InstallSentinel.Configuration;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Management;

public sealed class ProcessLauncherService(IOptions<AppConfig> config, AgentLogger agentLogger) : IProcessLauncher
{
    private readonly HashSet<int> _trackedPids = [];
    private readonly ReaderWriterLockSlim _pidLock = new();
    private readonly AppConfig _config = config.Value;
    private readonly AgentLogger _agentLogger = agentLogger;
    private Process? _rootProcess;
    private ManagementEventWatcher? _processStartWatcher;
    private ManagementEventWatcher? _processStopWatcher;

    public event Action<ProcessNode>? ProcessSpawned;
    public event Action<int>? ProcessExited;

    public async Task<ProcessLaunchResult> LaunchAndTrackAsync(
        string filePath,
        string? arguments = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return new ProcessLaunchResult
            {
                Success = false,
                RootProcessId = 0,
                RootProcessName = Path.GetFileName(filePath),
                ProcessPath = filePath,
                StartTime = DateTime.UtcNow,
                ErrorMessage = $"File not found: {filePath}"
            };
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = filePath,
            Arguments = arguments ?? string.Empty,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(filePath) ?? string.Empty,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false
        };

        // For .exe files that need elevation, the manifest should handle it.
        // UseShellExecute=true breaks PID tracking and WaitForExit.

        try
        {
            _rootProcess = Process.Start(startInfo);
            _agentLogger.Info("LAUNCHER", $"Process started: {Path.GetFileName(filePath)} PID={_rootProcess?.Id ?? 0}");
            if (_rootProcess == null)
            {
                return new ProcessLaunchResult
                {
                    Success = false,
                    RootProcessId = 0,
                    RootProcessName = Path.GetFileName(filePath),
                    ProcessPath = filePath,
                    StartTime = DateTime.UtcNow,
                    ErrorMessage = "Failed to start process"
                };
            }

            // Don't call WaitForInputIdle — it throws with UseShellExecute=false
            // and doesn't work for console apps. Just give the process a moment to start.
            await Task.Delay(200, cancellationToken);

            _pidLock.EnterWriteLock();
            try
            {
                _trackedPids.Add(_rootProcess.Id);
            }
            finally
            {
                _pidLock.ExitWriteLock();
            }

            SetupProcessWatchers(_rootProcess.Id);

            var rootNode = new ProcessNode
            {
                ProcessId = _rootProcess.Id,
                ProcessName = _rootProcess.ProcessName,
                ProcessPath = filePath,
                ParentProcessId = GetParentProcessId(_rootProcess.Id),
                StartTime = _rootProcess.StartTime,
                Relation = ProcessTreeRelation.Root,
                Depth = 0,
                IsInstallerRoot = true
            };

            ProcessSpawned?.Invoke(rootNode);

            return new ProcessLaunchResult
            {
                Success = true,
                RootProcessId = _rootProcess.Id,
                RootProcessName = _rootProcess.ProcessName,
                ProcessPath = filePath,
                StartTime = _rootProcess.StartTime,
                ProcessTree = rootNode
            };
        }
        catch (Exception ex)
        {
            return new ProcessLaunchResult
            {
                Success = false,
                RootProcessId = 0,
                RootProcessName = Path.GetFileName(filePath),
                ProcessPath = filePath,
                StartTime = DateTime.UtcNow,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<ProcessNode> GetProcessTreeAsync(int rootPid, CancellationToken cancellationToken = default)
    {
        var tree = await BuildProcessTreeAsync(rootPid, cancellationToken);
        return tree;
    }

    public IReadOnlySet<int> GetTrackedPids()
    {
        _pidLock.EnterReadLock();
        try
        {
            return new HashSet<int>(_trackedPids);
        }
        finally
        {
            _pidLock.ExitReadLock();
        }
    }

    public async Task<bool> WaitForProcessTreeAsync(int rootPid, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            _pidLock.EnterReadLock();
            var hasTracked = _trackedPids.Count > 0;
            _pidLock.ExitReadLock();

            if (!hasTracked)
                return true;

            try
            {
                using var process = Process.GetProcessById(rootPid);
                if (process.HasExited)
                {
                    await Task.Delay(100, cancellationToken);
                    _pidLock.EnterReadLock();
                    var remaining = _trackedPids.Count;
                    _pidLock.ExitReadLock();
                    return remaining == 0;
                }
            }
            catch (ArgumentException)
            {
                // Process doesn't exist anymore
                return true;
            }

            await Task.Delay(100, cancellationToken);
        }
        return false;
    }

    private void SetupProcessWatchers(int rootPid)
    {
        try
        {
            var startQuery = new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace");
            _processStartWatcher = new ManagementEventWatcher(startQuery);
            _processStartWatcher.EventArrived += (sender, e) =>
            {
                var pid = (int)(uint)e.NewEvent["ProcessID"];
                var parentPid = (int)(uint)e.NewEvent["ParentProcessID"];
                var name = (string)e.NewEvent["ProcessName"];

                if (IsTracked(parentPid) || pid == rootPid)
                {
                    TrackProcess(pid, parentPid, name);
                }
            };
            _processStartWatcher.Start();

            var stopQuery = new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace");
            _processStopWatcher = new ManagementEventWatcher(stopQuery);
            _processStopWatcher.EventArrived += (sender, e) =>
            {
                var pid = (int)(uint)e.NewEvent["ProcessID"];
                if (IsTracked(pid))
                {
                    _pidLock.EnterWriteLock();
                    try
                    {
                        _trackedPids.Remove(pid);
                    }
                    finally
                    {
                        _pidLock.ExitWriteLock();
                    }
                    ProcessExited?.Invoke(pid);
                }
            };
            _processStopWatcher.Start();
        }
        catch
        {
            // WMI not available, continue without child process tracking
        }
    }

    private bool IsTracked(int pid)
    {
        _pidLock.EnterReadLock();
        try
        {
            return _trackedPids.Contains(pid);
        }
        finally
        {
            _pidLock.ExitReadLock();
        }
    }

    private void TrackProcess(int pid, int parentPid, string name)
    {
        _pidLock.EnterWriteLock();
        try
        {
            if (!_trackedPids.Add(pid))
                return;
        }
        finally
        {
            _pidLock.ExitWriteLock();
        }

        var node = new ProcessNode
        {
            ProcessId = pid,
            ProcessName = name,
            ProcessPath = GetProcessPath(pid) ?? string.Empty,
            ParentProcessId = parentPid,
            StartTime = DateTime.UtcNow,
            Relation = ProcessTreeRelation.Child,
            Depth = GetDepth(parentPid) + 1
        };

        ProcessSpawned?.Invoke(node);
    }

    private static int GetParentProcessId(int pid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {pid}");
            foreach (ManagementObject obj in searcher.Get().Cast<ManagementObject>())
            {
                return (int)(uint)obj["ParentProcessId"];
            }
        }
        catch { }
        return 0;
    }

    private static string? GetProcessPath(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static int GetDepth(int pid)
    {
        if (pid == 0) return 0;
        // Simple depth estimation
        return 1;
    }

    private Task<ProcessNode> BuildProcessTreeAsync(int rootPid, CancellationToken cancellationToken)
    {
        var root = new ProcessNode
        {
            ProcessId = rootPid,
            ProcessName = GetProcessName(rootPid) ?? "Unknown",
            ProcessPath = GetProcessPath(rootPid) ?? string.Empty,
            ParentProcessId = 0,
            StartTime = DateTime.UtcNow,
            Relation = ProcessTreeRelation.Root,
            Depth = 0,
            IsInstallerRoot = true
        };

        var pids = GetTrackedPids();
        var nodes = new Dictionary<int, ProcessNode> { [rootPid] = root };

        foreach (var pid in pids)
        {
            if (pid == rootPid) continue;

            var name = GetProcessName(pid);
            var path = GetProcessPath(pid);
            var parentPid = GetParentProcessId(pid);

            var node = new ProcessNode
            {
                ProcessId = pid,
                ProcessName = name ?? "Unknown",
                ProcessPath = path ?? string.Empty,
                ParentProcessId = parentPid,
                StartTime = DateTime.UtcNow,
                Relation = ProcessTreeRelation.Child,
                Depth = 1
            };

            nodes[pid] = node;

            if (nodes.TryGetValue(parentPid, out var parent))
            {
                parent.Children.Add(node);
            }
        }

        return Task.FromResult(root);
    }

    private static string? GetProcessName(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _processStartWatcher?.Stop();
        _processStartWatcher?.Dispose();
        _processStopWatcher?.Stop();
        _processStopWatcher?.Dispose();
        _rootProcess?.Dispose();
        _pidLock.Dispose();
    }
}
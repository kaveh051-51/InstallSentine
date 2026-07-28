namespace InstallSentinel.Services;

using InstallSentinel.Models;
using InstallSentinel.Models.Enums;
using InstallSentinel.Services.Interfaces;
using InstallSentinel.Common;
using InstallSentinel.Common.Helpers;
using InstallSentinel.Configuration;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

public sealed class EtwMonitorEngine(INoiseFilterService noiseFilter, IOptions<AppConfig> config) : IEtwMonitorEngine, IDisposable, IAsyncDisposable
{
    private readonly EtwSettings _settings = config.Value.Etw;
    private readonly INoiseFilterService _noiseFilter = noiseFilter;
    private TraceEventSession? _kernelSession;
    private CancellationTokenSource? _cts;
    private Task? _processingTask;
    private readonly ConcurrentDictionary<int, string> _pidToProcessName = new();
    private readonly ConcurrentDictionary<int, string> _pidToProcessPath = new();
    private readonly ConcurrentDictionary<int, int> _pidToParentPid = new();
    private readonly HashSet<int> _trackedPids = [];
    private readonly ReaderWriterLockSlim _pidLock = new();
    private long _eventSequence = 0;
    private ChannelWriter<SystemEvent>? _eventWriter;
    private long _eventsReceived = 0;
    private long _eventsFiltered = 0;
    private long _eventsPublished = 0;
    private long _eventsDropped = 0;
    private DateTime _startTime;

    public event EventHandler<SystemEvent>? EventReceived;
    public event EventHandler<Exception>? ErrorOccurred;

    public bool IsRunning => _kernelSession != null;
    public string SessionName { get; private set; } = string.Empty;

    public async Task StartAsync(
        MonitorConfiguration configuration,
        ChannelWriter<SystemEvent> eventChannel,
        CancellationToken cancellationToken = default)
    {
        if (IsRunning)
            throw new InvalidOperationException("ETW monitor is already running");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _eventWriter = eventChannel;

        _pidLock.EnterWriteLock();
        try
        {
            _trackedPids.Clear();
            foreach (var pid in configuration.ProcessTreePids)
                _trackedPids.Add(pid);
        }
        finally
        {
            _pidLock.ExitWriteLock();
        }

        RefreshProcessCache();
        _startTime = DateTime.UtcNow;
        SessionName = configuration.SessionName + "_" + Guid.NewGuid().ToString("N")[..8];

        _kernelSession = new TraceEventSession(SessionName, TraceEventSessionOptions.Create)
        {
            StopOnDispose = true,
            BufferSizeMB = configuration.BufferSizeMb
        };

        EnableKernelProviders(configuration.KernelProviders);

        // File IO events
        _kernelSession.Source.Kernel.FileIOCreate += OnFileCreate;
        _kernelSession.Source.Kernel.FileIODelete += OnFileDelete;
        _kernelSession.Source.Kernel.FileIORename += OnFileRename;
        _kernelSession.Source.Kernel.FileIORead += OnFileRead;
        _kernelSession.Source.Kernel.FileIOWrite += OnFileWrite;
        _kernelSession.Source.Kernel.FileIOSetInfo += OnFileSetInfo;

        // Registry events
        _kernelSession.Source.Kernel.RegistryCreate += OnRegCreate;
        _kernelSession.Source.Kernel.RegistryDelete += OnRegDelete;
        _kernelSession.Source.Kernel.RegistrySetValue += OnRegSetValue;
        _kernelSession.Source.Kernel.RegistryDeleteValue += OnRegDeleteValue;

        // Process/Thread events
        _kernelSession.Source.Kernel.ProcessStart += OnProcessStart;
        _kernelSession.Source.Kernel.ProcessStop += OnProcessStop;
        _kernelSession.Source.Kernel.ThreadStart += OnThreadStart;
        _kernelSession.Source.Kernel.ThreadStop += OnThreadStop;

        // Image Load events
        _kernelSession.Source.Kernel.ImageLoad += OnImageLoad;

        _processingTask = Task.Run(ProcessEventsAsync, _cts.Token);

        // Start processing in background
        _ = Task.Run(() =>
        {
            try
            {
                _kernelSession.Source.Process();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
            }
        }, _cts.Token);

        // Allow session to start
        await Task.Delay(500, cancellationToken);
    }

    private void EnableKernelProviders(string[] providers)
    {
        var keywords = KernelTraceEventParser.Keywords.None;
        foreach (var provider in providers)
        {
            keywords |= provider switch
            {
                "Microsoft-Windows-Kernel-File" => KernelTraceEventParser.Keywords.FileIO,
                "Microsoft-Windows-Kernel-Registry" => KernelTraceEventParser.Keywords.Registry,
                "Microsoft-Windows-Kernel-Process" => KernelTraceEventParser.Keywords.Process,
                "Microsoft-Windows-Kernel-Thread" => KernelTraceEventParser.Keywords.Thread,
                "Microsoft-Windows-Kernel-ImageLoad" => KernelTraceEventParser.Keywords.ImageLoad,
                "Microsoft-Windows-Kernel-Network" => KernelTraceEventParser.Keywords.NetworkTCPIP,
                _ => KernelTraceEventParser.Keywords.None
            };
        }
        _kernelSession!.EnableKernelProvider(keywords);
    }

    private void OnFileCreate(FileIOCreateTraceData data)
    {
        if (!IsTrackedPid(data.ProcessID)) return;

        var evt = CreateSystemEvent(
            EventCategory.FileSystem,
            ActionType.Create,
            data.FileName,
            data.ProcessID,
            data.ThreadID,
            $"File created: {data.FileName}");

        if (evt != null) TryEnqueueEvent(evt);
    }

    private void OnFileDelete(FileIOInfoTraceData data)
    {
        if (!IsTrackedPid(data.ProcessID)) return;

        var evt = CreateSystemEvent(
            EventCategory.FileSystem,
            ActionType.Delete,
            data.FileName,
            data.ProcessID,
            data.ThreadID,
            $"File deleted: {data.FileName}");

        if (evt != null) TryEnqueueEvent(evt);
    }

    private void OnFileRename(FileIOInfoTraceData data)
    {
        if (!IsTrackedPid(data.ProcessID)) return;

        var evt = CreateSystemEvent(
            EventCategory.FileSystem,
            ActionType.Rename,
            data.FileName,
            data.ProcessID,
            data.ThreadID,
            $"File renamed: {data.FileName}",
            data.FileName);

        if (evt != null) TryEnqueueEvent(evt);
    }

    private void OnFileRead(FileIOReadWriteTraceData data)
    {
        // Optionally track reads - usually noise
    }

    private void OnFileWrite(FileIOReadWriteTraceData data)
    {
        if (!IsTrackedPid(data.ProcessID)) return;

        var evt = CreateSystemEvent(
            EventCategory.FileSystem,
            ActionType.Write,
            data.FileName,
            data.ProcessID,
            data.ThreadID,
            $"File written: {data.FileName}, bytes: {data.IoSize}");

        if (evt != null) TryEnqueueEvent(evt);
    }

    private void OnFileSetInfo(FileIOInfoTraceData data)
    {
        if (!IsTrackedPid(data.ProcessID)) return;

        var evt = CreateSystemEvent(
            EventCategory.FileSystem,
            ActionType.Modify,
            data.FileName,
            data.ProcessID,
            data.ThreadID,
            $"File metadata changed: {data.FileName}");

        if (evt != null) TryEnqueueEvent(evt);
    }

    private void OnRegCreate(RegistryTraceData data)
    {
        if (!IsTrackedPid(data.ProcessID)) return;

        var evt = CreateSystemEvent(
            EventCategory.Registry,
            ActionType.CreateKey,
            data.KeyName,
            data.ProcessID,
            data.ThreadID,
            $"Registry key created: {data.KeyName}");

        if (evt != null) TryEnqueueEvent(evt);
    }

    private void OnRegDelete(RegistryTraceData data)
    {
        if (!IsTrackedPid(data.ProcessID)) return;

        var evt = CreateSystemEvent(
            EventCategory.Registry,
            ActionType.DeleteKey,
            data.KeyName,
            data.ProcessID,
            data.ThreadID,
            $"Registry key deleted: {data.KeyName}");

        if (evt != null) TryEnqueueEvent(evt);
    }

    private void OnRegSetValue(RegistryTraceData data)
    {
        if (!IsTrackedPid(data.ProcessID)) return;

        var evt = CreateSystemEvent(
            EventCategory.Registry,
            ActionType.SetValue,
            data.KeyName,
            data.ProcessID,
            data.ThreadID,
            $"Registry value set: {data.KeyName}\\{data.ValueName}");

        if (evt != null)
        {
            evt.Metadata!["ValueName"] = data.ValueName;
            TryEnqueueEvent(evt);
        }
    }

    private void OnRegDeleteValue(RegistryTraceData data)
    {
        if (!IsTrackedPid(data.ProcessID)) return;

        var evt = CreateSystemEvent(
            EventCategory.Registry,
            ActionType.DeleteValue,
            data.KeyName,
            data.ProcessID,
            data.ThreadID,
            $"Registry value deleted: {data.KeyName}\\{data.ValueName}");

        if (evt != null)
        {
            evt.Metadata!["ValueName"] = data.ValueName;
            TryEnqueueEvent(evt);
        }
    }

    private void OnProcessStart(ProcessTraceData data)
    {
        var pid = (int)data.ProcessID;
        var ppid = (int)data.ParentID;

        if (IsTrackedPid(ppid) || pid == _trackedPids.FirstOrDefault())
        {
            _pidLock.EnterWriteLock();
            try
            {
                _trackedPids.Add(pid);
            }
            finally
            {
                _pidLock.ExitWriteLock();
            }

            _pidToProcessName[pid] = data.ProcessName;
            _pidToProcessPath[pid] = string.Empty;
            _pidToParentPid[pid] = ppid;

            var evt = CreateSystemEvent(
                EventCategory.Process,
                ActionType.Start,
                data.ProcessName,
                pid,
                0,
                $"Process started: {data.ProcessName} (PID: {pid}, PPID: {ppid})");

            if (evt != null) TryEnqueueEvent(evt);
        }
    }

    private void OnProcessStop(ProcessTraceData data)
    {
        var pid = (int)data.ProcessID;

        _pidLock.EnterWriteLock();
        try
        {
            _trackedPids.Remove(pid);
        }
        finally
        {
            _pidLock.ExitWriteLock();
        }

        var evt = CreateSystemEvent(
            EventCategory.Process,
            ActionType.Exit,
            data.ProcessName,
            pid,
            0,
            $"Process exited: {data.ProcessName} (PID: {pid})");

        if (evt != null) TryEnqueueEvent(evt);
    }

    private void OnThreadStart(ThreadTraceData data)
    {
        // Optional thread tracking
    }

    private void OnThreadStop(ThreadTraceData data)
    {
        // Optional thread tracking
    }

    private void OnImageLoad(ImageLoadTraceData data)
    {
        if (!IsTrackedPid((int)data.ProcessID)) return;

        var evt = CreateSystemEvent(
            EventCategory.ImageLoad,
            ActionType.Load,
            data.FileName,
            (int)data.ProcessID,
            0,
            $"Image loaded: {data.FileName}");

        if (evt != null) TryEnqueueEvent(evt);
    }

    private SystemEvent? CreateSystemEvent(
        EventCategory category,
        ActionType action,
        string targetPath,
        int processId,
        int threadId,
        string details,
        string? oldPath = null)
    {
        var normalizedPath = PathSanitizer.NormalizePath(targetPath);
        var processName = _pidToProcessName.TryGetValue(processId, out var pn) ? pn : "Unknown";
        var processPath = _pidToProcessPath.TryGetValue(processId, out var pp) ? pp : string.Empty;
        var parentPid = _pidToParentPid.TryGetValue(processId, out var ppid) ? ppid : 0;
        var parentName = parentPid > 0 && _pidToProcessName.TryGetValue(parentPid, out var pName) ? pName : "Unknown";

        var evt = new SystemEvent
        {
            Category = category,
            Action = action,
            TargetPath = normalizedPath,
            ProcessId = processId,
            ThreadId = threadId,
            ProcessName = processName,
            ProcessPath = processPath,
            ParentProcessId = parentPid,
            ParentProcessName = parentName,
            Timestamp = DateTime.UtcNow,
            EventSequence = (ulong)Interlocked.Increment(ref _eventSequence),
            Details = details,
            OldPath = oldPath != null ? PathSanitizer.NormalizePath(oldPath) : null,
            Metadata = new Dictionary<string, object>
            {
                ["OriginalPath"] = targetPath,
                ["NormalizedPath"] = normalizedPath
            }
        };

        if (_noiseFilter.ShouldFilter(evt))
        {
            Interlocked.Increment(ref _eventsFiltered);
            return null;
        }

        Interlocked.Increment(ref _eventsReceived);
        return evt;
    }

    private bool IsTrackedPid(int pid)
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

    private void TryEnqueueEvent(SystemEvent evt)
    {
        if (_eventWriter != null && !_eventWriter.TryWrite(evt))
        {
            Interlocked.Increment(ref _eventsDropped);
        }
        else
        {
            Interlocked.Increment(ref _eventsPublished);
            EventReceived?.Invoke(this, evt);
        }
    }

    private async Task ProcessEventsAsync()
    {
        // Events are processed via callbacks, this keeps the task alive
        try
        {
            await Task.Delay(Timeout.Infinite, _cts!.Token);
        }
        catch (OperationCanceledException) { }
    }

    private void RefreshProcessCache()
    {
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                _pidToProcessName[process.Id] = process.ProcessName;
                try
                {
                    _pidToProcessPath[process.Id] = process.MainModule?.FileName ?? string.Empty;
                }
                catch { }
            }
        }
        catch { }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _cts?.Cancel();

        if (_kernelSession != null)
        {
            _kernelSession.Source.Kernel.FileIOCreate -= OnFileCreate;
            _kernelSession.Source.Kernel.FileIODelete -= OnFileDelete;
            _kernelSession.Source.Kernel.FileIORename -= OnFileRename;
            _kernelSession.Source.Kernel.FileIORead -= OnFileRead;
            _kernelSession.Source.Kernel.FileIOWrite -= OnFileWrite;
            _kernelSession.Source.Kernel.FileIOSetInfo -= OnFileSetInfo;
            _kernelSession.Source.Kernel.RegistryCreate -= OnRegCreate;
            _kernelSession.Source.Kernel.RegistryDelete -= OnRegDelete;
            _kernelSession.Source.Kernel.RegistrySetValue -= OnRegSetValue;
            _kernelSession.Source.Kernel.RegistryDeleteValue -= OnRegDeleteValue;
            _kernelSession.Source.Kernel.ProcessStart -= OnProcessStart;
            _kernelSession.Source.Kernel.ProcessStop -= OnProcessStop;
            _kernelSession.Source.Kernel.ThreadStart -= OnThreadStart;
            _kernelSession.Source.Kernel.ThreadStop -= OnThreadStop;
            _kernelSession.Source.Kernel.ImageLoad -= OnImageLoad;

            _kernelSession.Dispose();
            _kernelSession = null;
        }

        _eventWriter?.TryComplete();

        if (_processingTask != null)
        {
            try
            {
                await _processingTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) { }
        }
    }

    public EtwStatistics GetStatistics()
    {
        return new EtwStatistics
        {
            EventsReceived = _eventsReceived,
            EventsFiltered = _eventsFiltered,
            EventsPublished = _eventsPublished,
            EventsDropped = _eventsDropped,
            Uptime = DateTime.UtcNow - _startTime,
            BufferedEvents = 0
        };
    }

    public void Dispose()
    {
        StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        _cts?.Dispose();
        _pidLock.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _cts?.Dispose();
        _pidLock.Dispose();
    }
}
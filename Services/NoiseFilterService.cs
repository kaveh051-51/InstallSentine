namespace InstallSentinel.Services;
using InstallSentinel.Services;
using InstallSentinel.Models;
using InstallSentinel.Models.Enums;
using InstallSentinel.Services.Interfaces;
using InstallSentinel.Common;
using InstallSentinel.Common.Helpers;
using InstallSentinel.Configuration;
using Microsoft.Extensions.Options;
using InstallSentinel.Services.Logging;

public sealed class NoiseFilterService : INoiseFilterService
{
    private readonly HashSet<string> _pathExclusions = [];
    private readonly HashSet<int> _pidExclusions = [];
    private readonly HashSet<string> _processNameExclusions = [];
    private readonly HashSet<string> _extensionExclusions = [];
    private readonly ReaderWriterLockSlim _lock = new();
    private long _totalEvents;
    private long _filteredEvents;
    private long _passedEvents;
    private readonly Dictionary<string, long> _filteredByReason = [];
    private readonly AgentLogger _agentLogger;

    public NoiseFilterService(IOptions<AppConfig> config, AgentLogger agentLogger)
    {
        _agentLogger = agentLogger;
        var settings = config.Value.NoiseFilter;

        foreach (var path in settings.ExcludedPaths)
            _pathExclusions.Add(ExpandPath(path));

        foreach (var pid in settings.ExcludedPids)
            _pidExclusions.Add(pid);

        foreach (var name in settings.ExcludedProcessNames)
            _processNameExclusions.Add(name.ToLowerInvariant());

        foreach (var ext in settings.ExcludedExtensions)
            _extensionExclusions.Add(ext.ToLowerInvariant());

        // Always exclude system PIDs
        foreach (var pid in Constants.Process.SystemPids)
            _pidExclusions.Add(pid);

        foreach (var name in Constants.Process.SystemProcessNames)
            _processNameExclusions.Add(name.ToLowerInvariant());

        foreach (var ext in Constants.FileSystem.ExcludedExtensions)
            _extensionExclusions.Add(ext.ToLowerInvariant());
    }

    public bool ShouldFilter(SystemEvent evt)
    {
        Interlocked.Increment(ref _totalEvents);

        var reason = GetFilterReason(evt);
        if (reason != null)
        {
            Interlocked.Increment(ref _filteredEvents);
            _agentLogger.Filter("NOISE", $"Filter reason={reason} PID={evt.ProcessId} {evt.ProcessName} {evt.TargetPath}");
            _lock.EnterWriteLock();
            try
            {
                _filteredByReason[reason] = _filteredByReason.GetValueOrDefault(reason) + 1;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
            return true;
        }

        Interlocked.Increment(ref _passedEvents);
        return false;
    }

    private string? GetFilterReason(SystemEvent evt)
    {
        // Check PID exclusions
        _lock.EnterReadLock();
        try
        {
            if (_pidExclusions.Contains(evt.ProcessId))
                return "ExcludedPID";
        }
        finally
        {
            _lock.ExitReadLock();
        }

        // Check process name exclusions
        _lock.EnterReadLock();
        try
        {
            if (_processNameExclusions.Contains(evt.ProcessName.ToLowerInvariant()))
                return "ExcludedProcessName";
        }
        finally
        {
            _lock.ExitReadLock();
        }

        // Check path exclusions
        var normalizedPath = PathSanitizer.NormalizePath(evt.TargetPath);
        _lock.EnterReadLock();
        try
        {
            foreach (var exclusion in _pathExclusions)
            {
                if (IsPathMatch(normalizedPath, exclusion))
                    return "ExcludedPath";
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }

        // Check extension exclusions
        var extension = Path.GetExtension(normalizedPath).ToLowerInvariant();
        _lock.EnterReadLock();
        try
        {
            if (_extensionExclusions.Contains(extension))
                return "ExcludedExtension";
        }
        finally
        {
            _lock.ExitReadLock();
        }

        // Filter registry noise
        if (evt.Category == EventCategory.Registry)
        {
            if (IsRegistryNoise(normalizedPath))
                return "RegistryNoise";
        }

        // Filter temp file noise
        if (evt.Category == EventCategory.FileSystem)
        {
            if (IsTempFileNoise(normalizedPath))
                return "TempFileNoise";
        }

        return null;
    }

    private static bool IsPathMatch(string path, string pattern)
    {
        if (pattern.EndsWith("*"))
        {
            var prefix = pattern[..^1];
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        return path.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRegistryNoise(string path)
    {
        foreach (var excluded in Constants.Registry.ExcludedKeys)
        {
            if (path.Contains(excluded, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsTempFileNoise(string path)
    {
        var tempPaths = new[]
        {
            @"\Temp\", @"\Temporary Internet Files\", @"\Cache\",
            @"\crashdumps\", @"\WER\", @"\Windows\Prefetch\",
            @"\Windows\Logs\", @"\Windows\Temp\"
        };

        return tempPaths.Any(p => path.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsExcludedPath(string path)
    {
        var normalized = PathSanitizer.NormalizePath(path);
        _lock.EnterReadLock();
        try
        {
            return _pathExclusions.Any(e => IsPathMatch(normalized, e));
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public bool IsExcludedProcess(int pid, string processName)
    {
        _lock.EnterReadLock();
        try
        {
            if (_pidExclusions.Contains(pid))
                return true;

            return _processNameExclusions.Contains(processName.ToLowerInvariant());
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public bool IsExcludedExtension(string extension)
    {
        _lock.EnterReadLock();
        try
        {
            return _extensionExclusions.Contains(extension.ToLowerInvariant());
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void AddPathExclusion(string pattern)
    {
        _lock.EnterWriteLock();
        try
        {
            _pathExclusions.Add(ExpandPath(pattern));
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void AddProcessExclusion(int pid)
    {
        _lock.EnterWriteLock();
        try
        {
            _pidExclusions.Add(pid);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void AddProcessNameExclusion(string processName)
    {
        _lock.EnterWriteLock();
        try
        {
            _processNameExclusions.Add(processName.ToLowerInvariant());
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void AddExtensionExclusion(string extension)
    {
        _lock.EnterWriteLock();
        try
        {
            _extensionExclusions.Add(extension.ToLowerInvariant());
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public IReadOnlySet<string> GetPathExclusions()
    {
        _lock.EnterReadLock();
        try
        {
            return new HashSet<string>(_pathExclusions);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public IReadOnlySet<int> GetPidExclusions()
    {
        _lock.EnterReadLock();
        try
        {
            return new HashSet<int>(_pidExclusions);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public IReadOnlySet<string> GetProcessNameExclusions()
    {
        _lock.EnterReadLock();
        try
        {
            return new HashSet<string>(_processNameExclusions);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public IReadOnlySet<string> GetExtensionExclusions()
    {
        _lock.EnterReadLock();
        try
        {
            return new HashSet<string>(_extensionExclusions);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public NoiseFilterStatistics GetStatistics()
    {
        _lock.EnterReadLock();
        try
        {
            return new NoiseFilterStatistics
            {
                TotalEvents = _totalEvents,
                FilteredEvents = _filteredEvents,
                PassedEvents = _passedEvents,
                FilteredByReason = new Dictionary<string, long>(_filteredByReason)
            };
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    private static string ExpandPath(string path)
    {
        return Environment.ExpandEnvironmentVariables(path);
    }
}
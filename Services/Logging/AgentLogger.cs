using System.Collections.Concurrent;
using System.Text;

namespace InstallSentinel.Services.Logging;

/// <summary>
/// AI-friendly structured logger with [AGENT] tags.
/// Output format: [timestamp] [AGENT] [LEVEL] [SOURCE] message
/// Log files are saved to logs/ directory for AI consumption.
/// </summary>
public sealed class AgentLogger : IDisposable
{
    private readonly string _logDirectory;
    private readonly string _logFilePath;
    private readonly StreamWriter _writer;
    private readonly object _lock = new();
    private bool _disposed;

    // Backing fields for thread-safe counters
    private int _totalLogs;
    private int _errorCount;
    private int _warningCount;
    private int _eventCount;

    // Public statistics
    public int TotalLogs { get; private set; }
    public int ErrorCount { get; private set; }
    public int WarningCount { get; private set; }
    public int EventCount { get; private set; }

    public AgentLogger(string? logDirectory = null)
    {
        _logDirectory = logDirectory ?? Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(_logDirectory);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _logFilePath = Path.Combine(_logDirectory, $"agent_{timestamp}.log");

        _writer = new StreamWriter(_logFilePath, append: true, encoding: Encoding.UTF8)
        {
            AutoFlush = true
        };

        // Write session header
        WriteLog("SYSTEM", "INFO", $"=== InstallSentinel Agent Log Session Started ===");
        WriteLog("SYSTEM", "INFO", $"Log file: {_logFilePath}");
        WriteLog("SYSTEM", "INFO", $"OS: {Environment.OSVersion}");
        WriteLog("SYSTEM", "INFO", $".NET: {Environment.Version}");
        WriteLog("SYSTEM", "INFO", $"User: {Environment.UserName}");
        WriteLog("SYSTEM", "INFO", $"Admin: {IsAdmin()}");
        WriteLog("SYSTEM", "INFO", $"=== End Header ===");
    }

    /// <summary>
    /// Log an informational message.
    /// </summary>
    public void Info(string source, string message)
    {
        WriteLog(source, "INFO", message);
    }

    /// <summary>
    /// Log a warning.
    /// </summary>
    public void Warn(string source, string message)
    {
        WriteLog(source, "WARN", message);
        var count = Interlocked.Increment(ref _warningCount);
        WarningCount = count;
    }

    /// <summary>
    /// Log an error with optional exception.
    /// </summary>
    public void Error(string source, string message, Exception? ex = null)
    {
        var msg = ex != null ? $"{message}: {ex.Message}" : message;
        WriteLog(source, "ERROR", msg);

        if (ex != null)
        {
            WriteLog(source, "ERROR", $"Stack: {ex.StackTrace}");
        }

        var count = Interlocked.Increment(ref _errorCount);
        ErrorCount = count;
    }

    /// <summary>
    /// Log a system event (file/registry/process).
    /// </summary>
    public void Event(string source, string eventType, string details)
    {
        WriteLog(source, "EVENT", $"[{eventType}] {details}");
        var count = Interlocked.Increment(ref _eventCount);
        EventCount = count;
    }

    /// <summary>
    /// Log a filtering decision (noise filter).
    /// </summary>
    public void Filter(string source, string message)
    {
        WriteLog(source, "FILTER", message);
    }

    /// <summary>
    /// Log a rollback action.
    /// </summary>
    public void Rollback(string source, string message)
    {
        WriteLog(source, "ROLLBACK", message);
    }

    /// <summary>
    /// Log ETW-specific events.
    /// </summary>
    public void Etw(string source, string message)
    {
        WriteLog(source, "ETW", message);
    }

    /// <summary>
    /// Log UI state changes.
    /// </summary>
    public void Ui(string source, string message)
    {
        WriteLog(source, "UI", message);
    }

    /// <summary>
    /// Get the full log file path.
    /// </summary>
    public string GetLogFilePath() => _logFilePath;

    /// <summary>
    /// Get log statistics summary for AI consumption.
    /// </summary>
    public string GetStatsSummary()
    {
        return $"[AGENT] [STATS] Total={TotalLogs}, Errors={ErrorCount}, Warnings={WarningCount}, Events={EventCount}";
    }

    private void WriteLog(string source, string level, string message)
    {
        if (_disposed) return;

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var line = $"[{timestamp}] [AGENT] [{level,-5}] [{source,-16}] {message}";

        lock (_lock)
        {
            try
            {
                _writer.WriteLine(line);
                var count = Interlocked.Increment(ref _totalLogs);
                TotalLogs = count;
            }
            catch
            {
                // Silently ignore write failures
            }
        }
    }

    private static bool IsAdmin()
    {
        try
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        WriteLog("SYSTEM", "INFO", $"=== Session Ended === {GetStatsSummary()}");

        lock (_lock)
        {
            _writer?.Flush();
            _writer?.Dispose();
        }
    }
}

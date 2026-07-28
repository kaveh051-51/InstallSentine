namespace InstallSentinel.Models;

using InstallSentinel.Models.Enums;
using InstallSentinel.Common.Helpers;

public sealed record SystemEvent
{
    public required EventCategory Category { get; init; }
    public required ActionType Action { get; init; }
    public required string TargetPath { get; init; }
    public required int ProcessId { get; init; }
    public required int ThreadId { get; init; }
    public required string ProcessName { get; init; }
    public required string ProcessPath { get; init; }
    public required int ParentProcessId { get; init; }
    public required string ParentProcessName { get; init; }
    public required DateTime Timestamp { get; init; }
    public required ulong EventSequence { get; init; }
    public string? Details { get; init; }
    public string? OldPath { get; init; } // For rename operations
    public ThreatStatus ThreatStatus { get; init; } = ThreatStatus.NotScanned;
    public Dictionary<string, object>? Metadata { get; init; }

    public string ShortPath => PathSanitizer.GetShortPath(TargetPath);
    public string DisplayAction => $"{Category}:{Action}";
}

public sealed record ProcessNode
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required string ProcessPath { get; init; }
    public required int ParentProcessId { get; init; }
    public required DateTime StartTime { get; init; }
    public DateTime? ExitTime { get; init; }
    public required ProcessTreeRelation Relation { get; init; }
    public int Depth { get; init; }
    public List<ProcessNode> Children { get; init; } = [];
    public List<SystemEvent> Events { get; init; } = [];
    public ThreatStatus ThreatStatus { get; set; } = ThreatStatus.NotScanned;
    public bool IsInstallerRoot { get; init; }
    public bool IsSystemProcess { get; init; }

    public int TotalEvents => Events.Count + Children.Sum(c => c.TotalEvents);
    public int FileSystemEvents => Events.Count(e => e.Category == EventCategory.FileSystem) + Children.Sum(c => c.FileSystemEvents);
    public int RegistryEvents => Events.Count(e => e.Category == EventCategory.Registry) + Children.Sum(c => c.RegistryEvents);
    public int ProcessEvents => Events.Count(e => e.Category == EventCategory.Process) + Children.Sum(c => c.ProcessEvents);
}

public sealed record VirusTotalReport
{
    public required string Sha256 { get; init; }
    public required string Resource { get; init; }
    public int Positives { get; init; }
    public int Total { get; init; }
    public required string ScanDate { get; init; }
    public required string Permalink { get; init; }
    public ThreatStatus ThreatStatus => Positives switch
    {
        0 => ThreatStatus.Benign,
        <= 3 => ThreatStatus.Suspicious,
        _ => ThreatStatus.Malicious
    };
    public Dictionary<string, object>? DetailedResults { get; init; }
    public string Summary => $"{Positives}/{Total} engines detected";
}

public sealed record MonitoringReport
{
    public required string InstallerPath { get; init; }
    public required string InstallerSha256 { get; init; }
    public required DateTime StartTime { get; init; }
    public required DateTime EndTime { get; init; }
    public TimeSpan Duration => EndTime - StartTime;
    public required ProcessNode ProcessTree { get; init; }
    public required List<SystemEvent> AllEvents { get; init; }
    public required VirusTotalReport? VirusTotalReport { get; init; }
    public int TotalFileSystemChanges => AllEvents.Count(e => e.Category == EventCategory.FileSystem);
    public int TotalRegistryChanges => AllEvents.Count(e => e.Category == EventCategory.Registry);
    public int TotalProcessEvents => AllEvents.Count(e => e.Category == EventCategory.Process);
    public int SuspiciousEvents => AllEvents.Count(e => e.ThreatStatus == ThreatStatus.Suspicious);
    public int MaliciousEvents => AllEvents.Count(e => e.ThreatStatus == ThreatStatus.Malicious);
    public string RollbackScriptPath { get; set; } = string.Empty;
    public bool RollbackScriptGenerated { get; set; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public sealed record RollbackAction
{
    public required RollbackActionType ActionType { get; init; }
    public required string TargetPath { get; init; }
    public string? BackupPath { get; init; }
    public string? RegistryValueName { get; init; }
    public object? RegistryValueData { get; init; }
    public Microsoft.Win32.RegistryValueKind? RegistryValueKind { get; init; }
    public required string Description { get; init; }
    public int ProcessId { get; init; }
    public DateTime Timestamp { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}
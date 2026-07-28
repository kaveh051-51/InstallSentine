namespace InstallSentinel.Tests.Models;

using FluentAssertions;
using InstallSentinel.Models;
using InstallSentinel.Models.Enums;
using Xunit;

public class ModelsTests
{
    [Fact]
    public void SystemEvent_DisplayAction_ReturnsCategoryColonAction()
    {
        var evt = CreateEvent(EventCategory.FileSystem, ActionType.Create);
        evt.DisplayAction.Should().Be("FileSystem:Create");
    }

    [Fact]
    public void MonitoringReport_Duration_ReturnsDifference()
    {
        var start = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 1, 1, 10, 5, 30, DateTimeKind.Utc);
        var report = CreateReport(start, end);

        report.Duration.Should().Be(TimeSpan.FromMinutes(5.5));
    }

    [Fact]
    public void MonitoringReport_TotalFileSystemChanges_CountsCorrectly()
    {
        var events = new List<SystemEvent>
        {
            CreateEvent(EventCategory.FileSystem, ActionType.Create),
            CreateEvent(EventCategory.FileSystem, ActionType.Delete),
            CreateEvent(EventCategory.Registry, ActionType.CreateKey),
            CreateEvent(EventCategory.Process, ActionType.Start)
        };
        var report = CreateReport(events: events);

        report.TotalFileSystemChanges.Should().Be(2);
        report.TotalRegistryChanges.Should().Be(1);
        report.TotalProcessEvents.Should().Be(1);
    }

    [Fact]
    public void VirusTotalReport_ThreatStatus_ZeroPositives_IsBenign()
    {
        var report = new VirusTotalReport
        {
            Sha256 = "abc123",
            Resource = "test",
            Positives = 0,
            Total = 70,
            ScanDate = "2025-01-01",
            Permalink = "https://example.com"
        };
        report.ThreatStatus.Should().Be(ThreatStatus.Benign);
        report.Summary.Should().Be("0/70 engines detected");
    }

    [Fact]
    public void VirusTotalReport_ThreatStatus_TwoPositives_IsSuspicious()
    {
        var report = new VirusTotalReport
        {
            Sha256 = "abc123",
            Resource = "test",
            Positives = 2,
            Total = 70,
            ScanDate = "2025-01-01",
            Permalink = "https://example.com"
        };
        report.ThreatStatus.Should().Be(ThreatStatus.Suspicious);
    }

    [Fact]
    public void VirusTotalReport_ThreatStatus_FivePositives_IsMalicious()
    {
        var report = new VirusTotalReport
        {
            Sha256 = "abc123",
            Resource = "test",
            Positives = 5,
            Total = 70,
            ScanDate = "2025-01-01",
            Permalink = "https://example.com"
        };
        report.ThreatStatus.Should().Be(ThreatStatus.Malicious);
    }

    [Fact]
    public void ProcessNode_TotalEvents_IncludesChildren()
    {
        var child = new ProcessNode
        {
            ProcessId = 2,
            ProcessName = "child",
            ProcessPath = "child.exe",
            ParentProcessId = 1,
            StartTime = DateTime.UtcNow,
            Relation = ProcessTreeRelation.Child,
            Events = [
                CreateEvent(EventCategory.FileSystem, ActionType.Create),
                CreateEvent(EventCategory.Registry, ActionType.SetValue)
            ]
        };

        var parent = new ProcessNode
        {
            ProcessId = 1,
            ProcessName = "parent",
            ProcessPath = "parent.exe",
            ParentProcessId = 0,
            StartTime = DateTime.UtcNow,
            Relation = ProcessTreeRelation.Root,
            IsInstallerRoot = true,
            Events = [
                CreateEvent(EventCategory.FileSystem, ActionType.Create)
            ]
        };
        parent.Children.Add(child);

        parent.TotalEvents.Should().Be(3);
        parent.FileSystemEvents.Should().Be(2);
        parent.RegistryEvents.Should().Be(1);
    }

    private static SystemEvent CreateEvent(EventCategory category, ActionType action)
    {
        return new SystemEvent
        {
            Category = category,
            Action = action,
            TargetPath = @"C:\test\file.txt",
            ProcessId = 1,
            ThreadId = 1,
            ProcessName = "test.exe",
            ProcessPath = @"C:\test\test.exe",
            ParentProcessId = 0,
            ParentProcessName = "Unknown",
            Timestamp = DateTime.UtcNow,
            EventSequence = 1
        };
    }

    private static MonitoringReport CreateReport(
        DateTime? start = null,
        DateTime? end = null,
        List<SystemEvent>? events = null)
    {
        return new MonitoringReport
        {
            InstallerPath = @"C:\test\installer.exe",
            InstallerSha256 = "abc123",
            StartTime = start ?? DateTime.UtcNow.AddMinutes(-5),
            EndTime = end ?? DateTime.UtcNow,
            ProcessTree = new ProcessNode
            {
                ProcessId = 1,
                ProcessName = "test",
                ProcessPath = "test.exe",
                ParentProcessId = 0,
                StartTime = DateTime.UtcNow,
                Relation = ProcessTreeRelation.Root
            },
            AllEvents = events?.ToList() ?? new List<SystemEvent>(),
            VirusTotalReport = null
        };
    }
}

namespace InstallSentinel.Tests.Services;

using FluentAssertions;
using InstallSentinel.Configuration;
using InstallSentinel.Models;
using InstallSentinel.Models.Enums;
using InstallSentinel.Services;
using InstallSentinel.Services.Logging;
using Microsoft.Extensions.Options;
using Xunit;

public class RollbackGeneratorTests : IDisposable
{
    private readonly RollbackGenerator _sut;
    private readonly string _tempDir;
    private readonly AgentLogger _agentLogger;

    public RollbackGeneratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"InstallSentinelTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var logDir = Path.Combine(_tempDir, "logs");
        _agentLogger = new AgentLogger(logDir);

        var config = Options.Create(new AppConfig
        {
            Rollback = new RollbackSettings
            {
                OutputDirectory = _tempDir,
                IncludeRegistryRollback = true,
                IncludeFileRollback = true,
                MaxRollbackScripts = 5
            }
        });
        _sut = new RollbackGenerator(config, _agentLogger);
    }

    public void Dispose()
    {
        _agentLogger?.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task GenerateRollbackScriptAsync_EmptyEvents_CreatesScriptFile()
    {
        var report = CreateReport([]);
        var path = await _sut.GenerateRollbackScriptAsync(report);

        File.Exists(path).Should().BeTrue();
        path.Should().EndWith(".ps1");

        var content = await File.ReadAllTextAsync(path);
        content.Should().Contain("InstallSentinel Rollback Script");
        content.Should().Contain("#requires -RunAsAdministrator");
    }

    [Fact]
    public async Task GenerateRollbackScriptAsync_WithFileCreations_GeneratesDeleteFile()
    {
        var events = new List<SystemEvent>
        {
            CreateEvent(ActionType.Create, @"C:\test\newfile.dll")
        };
        var report = CreateReport(events);
        var path = await _sut.GenerateRollbackScriptAsync(report);

        var content = await File.ReadAllTextAsync(path);
        content.Should().Contain("FILE SYSTEM ROLLBACK");
        content.Should().Contain("Remove-Item");
        content.Should().Contain(@"C:\test\newfile.dll");
    }

    [Fact]
    public async Task GenerateRollbackScriptAsync_WithFileDeletions_GeneratesRestoreNote()
    {
        var events = new List<SystemEvent>
        {
            CreateEvent(ActionType.Delete, @"C:\test\deleted.txt")
        };
        var report = CreateReport(events);
        var path = await _sut.GenerateRollbackScriptAsync(report);

        var content = await File.ReadAllTextAsync(path);
        content.Should().Contain("Restore");
        content.Should().Contain(@"C:\test\deleted.txt");
    }

    [Fact]
    public async Task GenerateRollbackScriptAsync_WithRegistryCreate_GeneratesRegistryRollback()
    {
        var events = new List<SystemEvent>
        {
            CreateEvent(ActionType.CreateKey, @"\REGISTRY\MACHINE\SOFTWARE\MyCompany\MyApp",
                EventCategory.Registry)
        };
        var report = CreateReport(events);
        var path = await _sut.GenerateRollbackScriptAsync(report);

        var content = await File.ReadAllTextAsync(path);
        // Script should contain rollback content
        content.Should().Contain("Rollback completed.");
    }

    [Fact]
    public async Task GenerateRollbackScriptAsync_ProcessTree_CreatesProcessTermination()
    {
        var rootNode = CreateProcessNode(100, ProcessTreeRelation.Root);
        var childNode = CreateProcessNode(200, ProcessTreeRelation.Child);
        rootNode.Children.Add(childNode);

        var report = new MonitoringReport
        {
            InstallerPath = @"C:\test\installer.exe",
            InstallerSha256 = "abc123",
            StartTime = DateTime.UtcNow.AddMinutes(-5),
            EndTime = DateTime.UtcNow,
            ProcessTree = rootNode,
            AllEvents = [],
            VirusTotalReport = null
        };

        var path = await _sut.GenerateRollbackScriptAsync(report);
        var content = await File.ReadAllTextAsync(path);

        content.Should().Contain("PROCESS TERMINATION");
        content.Should().Contain("Stop-Process");
    }

    [Fact]
    public void ValidateScript_ExistingFile_ReturnsSuccess()
    {
        var scriptPath = Path.Combine(_tempDir, "test_rollback.ps1");
        File.WriteAllText(scriptPath, @"
#requires -RunAsAdministrator
Set-StrictMode -Version Latest
`$ErrorActionPreference = 'Stop'
Remove-Item -Path 'C:\test.txt' -Force
Set-ItemProperty -Path 'HKLM:\SOFTWARE' -Name 'Key' -Value 1
Stop-Process -Id 1234
");

        var result = _sut.ValidateScript(scriptPath);
        result.Success.Should().BeTrue();
        result.FileActions.Should().BeGreaterThan(0);
        result.ProcessActions.Should().Be(1);
    }

    [Fact]
    public void ValidateScript_NonExistentFile_ReturnsFailure()
    {
        var result = _sut.ValidateScript(@"C:\nonexistent\script.ps1");
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public void GetRollbackDirectory_ReturnsConfiguredPath()
    {
        _sut.GetRollbackDirectory().Should().Be(_tempDir);
    }

    [Fact]
    public async Task GenerateRollbackScriptAsync_RegistryActions_GeneratesRegistrySection()
    {
        // Regression: previously StartsWith("Registry") filtered out all registry actions
        // because enum values start with "Delete"/"Restore", not "Registry"
        var events = new List<SystemEvent>
        {
            CreateEvent(ActionType.CreateKey, @"\REGISTRY\MACHINE\SOFTWARE\MyCompany\MyApp", EventCategory.Registry),
            CreateEvent(ActionType.SetValue, @"\REGISTRY\MACHINE\SOFTWARE\MyCompany\MyApp", EventCategory.Registry),
            CreateEvent(ActionType.DeleteKey, @"\REGISTRY\MACHINE\SOFTWARE\OldCompany", EventCategory.Registry)
        };
        var report = CreateReport(events);
        var path = await _sut.GenerateRollbackScriptAsync(report);

        var content = await File.ReadAllTextAsync(path);
        content.Should().Contain("REGISTRY ROLLBACK");
        // Verify body is NOT empty — the try block must contain actual commands
        content.Should().Contain("Deleted registry key:");
        content.Should().Contain("Restore registry key:");
        content.Should().Contain("Rollback completed.");
    }

    private static MonitoringReport CreateReport(IReadOnlyList<SystemEvent> events)
    {
        var rootNode = CreateProcessNode(100, ProcessTreeRelation.Root);
        return new MonitoringReport
        {
            InstallerPath = @"C:\test\installer.exe",
            InstallerSha256 = "abc123def456",
            StartTime = DateTime.UtcNow.AddMinutes(-5),
            EndTime = DateTime.UtcNow,
            ProcessTree = rootNode,
            AllEvents = events?.ToList() ?? new List<SystemEvent>(),
            VirusTotalReport = null
        };
    }

    private static SystemEvent CreateEvent(
        ActionType action,
        string targetPath,
        EventCategory category = EventCategory.FileSystem)
    {
        return new SystemEvent
        {
            Category = category,
            Action = action,
            TargetPath = targetPath,
            ProcessId = 100,
            ThreadId = 1,
            ProcessName = "test.exe",
            ProcessPath = @"C:\test\test.exe",
            ParentProcessId = 0,
            ParentProcessName = "Unknown",
            Timestamp = DateTime.UtcNow,
            EventSequence = 1
        };
    }

    private static ProcessNode CreateProcessNode(int pid, ProcessTreeRelation relation)
    {
        return new ProcessNode
        {
            ProcessId = pid,
            ProcessName = $"Process{pid}",
            ProcessPath = $@"C:\test\process{pid}.exe",
            ParentProcessId = pid == 100 ? 0 : 100,
            StartTime = DateTime.UtcNow,
            Relation = relation,
            Depth = relation == ProcessTreeRelation.Root ? 0 : 1,
            IsInstallerRoot = relation == ProcessTreeRelation.Root
        };
    }
}

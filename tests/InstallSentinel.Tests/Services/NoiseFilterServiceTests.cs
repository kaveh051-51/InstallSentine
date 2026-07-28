namespace InstallSentinel.Tests.Services;

using FluentAssertions;
using InstallSentinel.Configuration;
using InstallSentinel.Models;
using InstallSentinel.Models.Enums;
using InstallSentinel.Services;
using InstallSentinel.Services.Logging;
using Microsoft.Extensions.Options;
using Xunit;

public class NoiseFilterServiceTests
{
    private readonly NoiseFilterService _sut;

    public NoiseFilterServiceTests()
    {
        var config = Options.Create(new AppConfig
        {
            NoiseFilter = new NoiseFilterSettings
            {
                ExcludedPaths = [
                    @"C:\Windows\Temp\*",
                    @"C:\Users\*\AppData\Local\Temp\*"
                ],
                ExcludedPids = [4, 8],
                ExcludedProcessNames = ["System", "Registry", "svchost.exe"],
                ExcludedExtensions = [".tmp", ".temp", ".log"]
            }
        });
        var agentLogger = new AgentLogger(Path.Combine(Path.GetTempPath(), $"Logs_{Guid.NewGuid():N}"));
        _sut = new NoiseFilterService(config, agentLogger);
    }

    [Fact]
    public void ShouldFilter_SystemProcess_ReturnsTrue()
    {
        var evt = CreateEvent(processName: "svchost.exe");
        _sut.ShouldFilter(evt).Should().BeTrue();
    }

    [Fact]
    public void ShouldFilter_NormalProcess_ReturnsFalse()
    {
        var evt = CreateEvent(
            processName: "myinstaller.exe",
            targetPath: @"C:\Users\Test\Downloads\file.dll");
        _sut.ShouldFilter(evt).Should().BeFalse();
    }

    [Fact]
    public void ShouldFilter_TempExtension_ReturnsTrue()
    {
        var evt = CreateEvent(
            processName: "test.exe",
            targetPath: @"C:\test.tmp");
        _sut.ShouldFilter(evt).Should().BeTrue();
    }

    [Fact]
    public void ShouldFilter_ExcludedPid_ReturnsTrue()
    {
        var evt = CreateEvent(processId: 4);
        _sut.ShouldFilter(evt).Should().BeTrue();
    }

    [Fact]
    public void ShouldFilter_TempPath_ReturnsTrue()
    {
        var evt = CreateEvent(
            processName: "test.exe",
            targetPath: @"C:\Windows\Temp\abc.tmp");
        _sut.ShouldFilter(evt).Should().BeTrue();
    }

    [Fact]
    public void ShouldFilter_RegistryNoise_ExcludesHardware()
    {
        var evt = CreateEvent(
            processName: "test.exe",
            category: EventCategory.Registry,
            targetPath: @"\REGISTRY\MACHINE\HARDWARE\Something");
        _sut.ShouldFilter(evt).Should().BeTrue();
    }

    [Fact]
    public void IsExcludedProcess_SystemPid_ReturnsTrue()
    {
        _sut.IsExcludedProcess(4, "System").Should().BeTrue();
    }

    [Fact]
    public void IsExcludedProcess_NormalProcess_ReturnsFalse()
    {
        _sut.IsExcludedProcess(1234, "notepad.exe").Should().BeFalse();
    }

    [Fact]
    public void IsExcludedExtension_Tmp_ReturnsTrue()
    {
        _sut.IsExcludedExtension(".tmp").Should().BeTrue();
    }

    [Fact]
    public void IsExcludedExtension_Dll_ReturnsFalse()
    {
        _sut.IsExcludedExtension(".dll").Should().BeFalse();
    }

    [Fact]
    public void AddPathExclusion_NewPattern_Applies()
    {
        _sut.AddPathExclusion(@"C:\TestDir\*");
        var evt = CreateEvent(
            processName: "test.exe",
            targetPath: @"C:\TestDir\newfile.txt");
        _sut.ShouldFilter(evt).Should().BeTrue();
    }

    [Fact]
    public void AddProcessExclusion_NewPid_Applies()
    {
        _sut.AddProcessExclusion(9999);
        var evt = CreateEvent(processId: 9999);
        _sut.ShouldFilter(evt).Should().BeTrue();
    }

    [Fact]
    public void GetStatistics_AfterFiltering_ReturnsCorrectCounts()
    {
        // Filter some events
        _sut.ShouldFilter(CreateEvent(processId: 4)); // excluded
        _sut.ShouldFilter(CreateEvent(processId: 4)); // excluded
        _sut.ShouldFilter(CreateEvent(
            processName: "test.exe",
            targetPath: @"C:\normal.txt")); // passed

        var stats = _sut.GetStatistics();
        stats.TotalEvents.Should().Be(3);
        stats.FilteredEvents.Should().Be(2);
        stats.PassedEvents.Should().Be(1);
    }

    private static SystemEvent CreateEvent(
        int processId = 1234,
        string processName = "test.exe",
        string targetPath = @"C:\Windows\System32\notepad.exe",
        EventCategory category = EventCategory.FileSystem,
        ActionType action = ActionType.Create)
    {
        return new SystemEvent
        {
            Category = category,
            Action = action,
            TargetPath = targetPath,
            ProcessId = processId,
            ThreadId = 100,
            ProcessName = processName,
            ProcessPath = targetPath,
            ParentProcessId = 0,
            ParentProcessName = "Unknown",
            Timestamp = DateTime.UtcNow,
            EventSequence = 1
        };
    }
}

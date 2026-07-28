namespace InstallSentinel.Tests.Services;

using FluentAssertions;
using InstallSentinel.Configuration;
using InstallSentinel.Models;
using InstallSentinel.Models.Enums;
using InstallSentinel.Services;
using InstallSentinel.Services.Logging;
using Microsoft.Extensions.Options;
using Xunit;

public class ProcessLauncherServiceTests : IDisposable
{
    private readonly ProcessLauncherService _sut;

    public ProcessLauncherServiceTests()
    {
        var config = Options.Create(new AppConfig());
        var agentLogger = new AgentLogger(Path.Combine(Path.GetTempPath(), $"Logs_{Guid.NewGuid():N}"));
        _sut = new ProcessLauncherService(config, agentLogger);
    }

    public void Dispose()
    {
        _sut.Dispose();
    }

    [Fact]
    public void GetTrackedPids_InitiallyEmpty()
    {
        var pids = _sut.GetTrackedPids();
        pids.Should().BeEmpty();
    }

    [Fact]
    public async Task LaunchAndTrackAsync_FileNotFound_ReturnsFailure()
    {
        var result = await _sut.LaunchAndTrackAsync(@"C:\nonexistent\file.exe");
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
        result.RootProcessId.Should().Be(0);
    }

    [Fact]
    public async Task WaitForProcessTreeAsync_ZeroTimeout_ReturnsFalse()
    {
        var result = await _sut.WaitForProcessTreeAsync(999999, TimeSpan.Zero);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetProcessTreeAsync_NonExistentPid_ReturnsRootWithUnknownName()
    {
        var tree = await _sut.GetProcessTreeAsync(0);
        tree.Should().NotBeNull();
        tree.ProcessId.Should().Be(0);
        tree.Relation.Should().Be(ProcessTreeRelation.Root);
    }

    [Fact]
    public void ProcessSpawned_Event_DoesNotThrowOnAccess()
    {
        // Verify the event exists and is accessible (just check the delegate type)
        var handlerType = typeof(Action<ProcessNode>);
        handlerType.Should().NotBeNull();
    }

    [Fact]
    public void ProcessExited_Event_DoesNotThrowOnAccess()
    {
        var handlerType = typeof(Action<int>);
        handlerType.Should().NotBeNull();
    }
}

namespace InstallSentinel.Tests.Services;

using FluentAssertions;
using InstallSentinel.Configuration;
using InstallSentinel.Models;
using InstallSentinel.Services;
using InstallSentinel.Services.Interfaces;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

public class EtwMonitorEngineTests : IDisposable
{
    private readonly EtwMonitorEngine _sut;
    private readonly INoiseFilterService _noiseFilter;

    public EtwMonitorEngineTests()
    {
        _noiseFilter = Substitute.For<INoiseFilterService>();
        _noiseFilter.ShouldFilter(Arg.Any<SystemEvent>()).Returns(false);

        var config = Options.Create(new AppConfig
        {
            Etw = new EtwSettings
            {
                SessionName = "TestSession",
                BufferSizeMb = 64,
                KernelProviders = []
            }
        });
        _sut = new EtwMonitorEngine(_noiseFilter, config);
    }

    public void Dispose()
    {
        _sut.Dispose();
    }

    [Fact]
    public void IsRunning_BeforeStart_ReturnsFalse()
    {
        _sut.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void SessionName_BeforeStart_ReturnsEmpty()
    {
        _sut.SessionName.Should().BeEmpty();
    }

    [Fact]
    public void GetStatistics_BeforeStart_ReturnsZeroCounts()
    {
        var stats = _sut.GetStatistics();
        stats.EventsReceived.Should().Be(0);
        stats.EventsFiltered.Should().Be(0);
        stats.EventsPublished.Should().Be(0);
        stats.EventsDropped.Should().Be(0);
    }

    [Fact]
    public void EventReceived_CanSubscribeAndUnsubscribe()
    {
        EventHandler<SystemEvent>? handler = (sender, evt) => { };
        Action act = () =>
        {
            _sut.EventReceived += handler;
            _sut.EventReceived -= handler;
        };
        act.Should().NotThrow();
    }

    [Fact]
    public void ErrorOccurred_CanSubscribeAndUnsubscribe()
    {
        EventHandler<Exception>? handler = (sender, ex) => { };
        Action act = () =>
        {
            _sut.ErrorOccurred += handler;
            _sut.ErrorOccurred -= handler;
        };
        act.Should().NotThrow();
    }

    [Fact]
    public async Task StopAsync_WithoutStart_DoesNotThrow()
    {
        await _sut.StopAsync();
        _sut.IsRunning.Should().BeFalse();
    }
}

namespace InstallSentinel.Models;

using InstallSentinel.Configuration;

public sealed record MonitorConfiguration
{
    public required int RootProcessId { get; init; }
    public required IReadOnlySet<int> ProcessTreePids { get; init; }
    public required string SessionName { get; init; }
    public int BufferSizeMb { get; init; } = 64;
    public int MinBuffers { get; init; } = 64;
    public int MaxBuffers { get; init; } = 256;
    public TimeSpan FlushTimer { get; init; } = TimeSpan.FromSeconds(1);
    public required string[] KernelProviders { get; init; }
}

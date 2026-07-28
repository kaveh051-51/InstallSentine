namespace InstallSentinel.Services.Interfaces;

using InstallSentinel.Models;

public interface INoiseFilterService
{
    bool ShouldFilter(SystemEvent evt);
    bool IsExcludedPath(string path);
    bool IsExcludedProcess(int pid, string processName);
    bool IsExcludedExtension(string extension);
    void AddPathExclusion(string pattern);
    void AddProcessExclusion(int pid);
    void AddProcessNameExclusion(string processName);
    void AddExtensionExclusion(string extension);
    IReadOnlySet<string> GetPathExclusions();
    IReadOnlySet<int> GetPidExclusions();
    IReadOnlySet<string> GetProcessNameExclusions();
    IReadOnlySet<string> GetExtensionExclusions();
    NoiseFilterStatistics GetStatistics();
}

public record NoiseFilterStatistics
{
    public required long TotalEvents { get; init; }
    public required long FilteredEvents { get; init; }
    public required long PassedEvents { get; init; }
    public required Dictionary<string, long> FilteredByReason { get; init; }
}
namespace InstallSentinel.Services.Interfaces;

using InstallSentinel.Models;

public interface IVirusTotalService
{
    Task<VirusTotalReport?> ScanFileAsync(string filePath, CancellationToken cancellationToken = default);
    Task<VirusTotalReport?> ScanHashAsync(string sha256, CancellationToken cancellationToken = default);
    bool IsConfigured { get; }
}
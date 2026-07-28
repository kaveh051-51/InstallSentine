namespace InstallSentinel.Services.Interfaces;

using InstallSentinel.Models;

public interface IRollbackGenerator
{
    Task<string> GenerateRollbackScriptAsync(
        MonitoringReport report,
        string? outputPath = null,
        CancellationToken cancellationToken = default);

    Task<string> GenerateRollbackScriptAsync(
        IReadOnlyList<SystemEvent> events,
        IReadOnlyList<ProcessNode> processTree,
        string installerPath,
        string installerSha256,
        string? outputPath = null,
        CancellationToken cancellationToken = default);

    RollbackGenerationResult ValidateScript(string scriptPath);
}

public record RollbackGenerationResult
{
    public required bool Success { get; init; }
    public required string ScriptPath { get; init; }
    public required int TotalActions { get; init; }
    public required int FileActions { get; init; }
    public required int RegistryActions { get; init; }
    public required int ProcessActions { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
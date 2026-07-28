namespace InstallSentinel.Services;
using InstallSentinel.Services;
using InstallSentinel.Models;
using InstallSentinel.Models.Enums;
using InstallSentinel.Services.Interfaces;
using InstallSentinel.Common;
using InstallSentinel.Common.Helpers;
using InstallSentinel.Configuration;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text;

public sealed class RollbackGenerator : IRollbackGenerator
{
    private readonly RollbackSettings _settings;
    private int _scriptCounter = 0;

    public RollbackGenerator(IOptions<AppConfig> config)
    {
        _settings = config.Value.Rollback;
        Directory.CreateDirectory(_settings.OutputDirectory);
    }

    public async Task<string> GenerateRollbackScriptAsync(
        MonitoringReport report,
        string? outputPath = null,
        CancellationToken cancellationToken = default)
    {
        return await GenerateRollbackScriptAsync(
            report.AllEvents,
            [report.ProcessTree],
            report.InstallerPath,
            report.InstallerSha256,
            outputPath,
            cancellationToken);
    }

    public async Task<string> GenerateRollbackScriptAsync(
        IReadOnlyList<SystemEvent> events,
        IReadOnlyList<ProcessNode> processTree,
        string installerPath,
        string installerSha256,
        string? outputPath = null,
        CancellationToken cancellationToken = default)
    {
        var actions = BuildRollbackActions(events, processTree);
        var scriptPath = outputPath ?? GetDefaultScriptPath();

        var script = BuildPowerShellScript(actions, installerPath, installerSha256);
        await File.WriteAllTextAsync(scriptPath, script, Encoding.UTF8, cancellationToken);

        CleanupOldScripts();
        return scriptPath;
    }

    public RollbackGenerationResult ValidateScript(string scriptPath)
    {
        if (!File.Exists(scriptPath))
        {
            return new RollbackGenerationResult
            {
                Success = false,
                ScriptPath = scriptPath,
                TotalActions = 0,
                FileActions = 0,
                RegistryActions = 0,
                ProcessActions = 0,
                ErrorMessage = "Script file not found"
            };
        }

        var content = File.ReadAllText(scriptPath);
        var warnings = new List<string>();

        var fileActions = CountMatches(content, "Remove-Item") + CountMatches(content, "Copy-Item") + CountMatches(content, "Move-Item");
        var registryActions = CountMatches(content, "Remove-ItemProperty") + CountMatches(content, "Set-ItemProperty") + CountMatches(content, "Remove-Item -Path \"HK");
        var processActions = CountMatches(content, "Stop-Process");

        if (!content.Contains("Set-StrictMode"))
            warnings.Add("Script does not use Set-StrictMode");
        if (!content.Contains("ErrorAction"))
            warnings.Add("Script may not handle errors gracefully");
        if (!content.Contains("WhatIf"))
            warnings.Add("Consider adding -WhatIf for safety testing");

        return new RollbackGenerationResult
        {
            Success = true,
            ScriptPath = scriptPath,
            TotalActions = fileActions + registryActions + processActions,
            FileActions = fileActions,
            RegistryActions = registryActions,
            ProcessActions = processActions,
            Warnings = warnings
        };
    }

    public string GetRollbackDirectory() => _settings.OutputDirectory;

    private List<RollbackAction> BuildRollbackActions(
        IReadOnlyList<SystemEvent> events,
        IReadOnlyList<ProcessNode> processTree)
    {
        var actions = new List<RollbackAction>();
        var fileCreations = new Dictionary<string, SystemEvent>(StringComparer.OrdinalIgnoreCase);
        var fileModifications = new Dictionary<string, SystemEvent>(StringComparer.OrdinalIgnoreCase);
        var fileDeletions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var registryChanges = new Dictionary<string, RegistryChangeInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var evt in events)
        {
            if (evt.Category == EventCategory.FileSystem)
            {
                var normalizedPath = PathSanitizer.NormalizePath(evt.TargetPath);

                switch (evt.Action)
                {
                    case ActionType.Create:
                        fileCreations[normalizedPath] = evt;
                        break;
                    case ActionType.Modify:
                    case ActionType.Write:
                        if (!fileCreations.ContainsKey(normalizedPath))
                            fileModifications[normalizedPath] = evt;
                        break;
                    case ActionType.Delete:
                        fileDeletions.Add(normalizedPath);
                        break;
                    case ActionType.Rename:
                        if (!string.IsNullOrEmpty(evt.OldPath))
                        {
                            var oldPath = PathSanitizer.NormalizePath(evt.OldPath);
                            fileDeletions.Add(oldPath);
                            fileCreations[normalizedPath] = evt;
                        }
                        break;
                }
            }
            else if (evt.Category == EventCategory.Registry)
            {
                var key = $"{evt.TargetPath}|{evt.Action}";
                registryChanges[key] = new RegistryChangeInfo
                {
                    Path = evt.TargetPath,
                    Action = evt.Action,
                    Event = evt
                };
            }
        }

        // File rollback actions
        foreach (var (path, evt) in fileCreations)
        {
            if (fileDeletions.Contains(path))
                continue; // Was created then deleted, net zero

            actions.Add(new RollbackAction
            {
                ActionType = RollbackActionType.DeleteFile,
                TargetPath = path,
                Description = $"Delete created file: {path}",
                ProcessId = evt.ProcessId,
                Timestamp = evt.Timestamp
            });
        }

        foreach (var path in fileDeletions)
        {
            if (fileCreations.ContainsKey(path))
                continue; // Was deleted then recreated, handled above

            actions.Add(new RollbackAction
            {
                ActionType = RollbackActionType.RestoreFile,
                TargetPath = path,
                Description = $"Restore deleted file: {path}",
                ProcessId = 0,
                Timestamp = DateTime.UtcNow
            });
        }

        foreach (var (path, evt) in fileModifications)
        {
            actions.Add(new RollbackAction
            {
                ActionType = RollbackActionType.RestoreFile,
                TargetPath = path,
                Description = $"Restore modified file: {path}",
                ProcessId = evt.ProcessId,
                Timestamp = evt.Timestamp
            });
        }

        // Registry rollback actions
        if (_settings.IncludeRegistryRollback)
        {
            foreach (var (_, change) in registryChanges)
            {
                var actionType = change.Action switch
                {
                    ActionType.CreateKey => RollbackActionType.DeleteRegistryKey,
                    ActionType.DeleteKey => RollbackActionType.RestoreRegistryKey,
                    ActionType.SetValue => RollbackActionType.RestoreRegistryValue,
                    ActionType.DeleteValue => RollbackActionType.RestoreRegistryValue,
                    _ => RollbackActionType.DeleteRegistryValue
                };

                actions.Add(new RollbackAction
                {
                    ActionType = actionType,
                    TargetPath = change.Path,
                    Description = $"Registry rollback: {change.Action} on {change.Path}",
                    ProcessId = change.Event.ProcessId,
                    Timestamp = change.Event.Timestamp,
                    RegistryValueName = change.Event.Metadata?.GetValueOrDefault("ValueName")?.ToString(),
                    RegistryValueData = change.Event.Metadata?.GetValueOrDefault("ValueData"),
                    RegistryValueKind = change.Event.Metadata?.GetValueOrDefault("ValueKind") as Microsoft.Win32.RegistryValueKind?
                });
            }
        }

        // Process rollback actions - kill spawned processes
        var allPids = new HashSet<int>();
        foreach (var root in processTree)
        {
            CollectPids(root, allPids);
        }

        foreach (var pid in allPids)
        {
            if (pid != 0 && pid != Environment.ProcessId)
            {
                actions.Add(new RollbackAction
                {
                    ActionType = RollbackActionType.TerminateProcess,
                    TargetPath = pid.ToString(),
                    Description = $"Terminate process PID: {pid}",
                    ProcessId = pid,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        return [.. actions.OrderBy(a => a.ActionType).ThenBy(a => a.TargetPath)];
    }

    private static void CollectPids(ProcessNode node, HashSet<int> pids)
    {
        pids.Add(node.ProcessId);
        foreach (var child in node.Children)
            CollectPids(child, pids);
    }

    private string BuildPowerShellScript(List<RollbackAction> actions, string installerPath, string installerSha256)
    {
        var sb = new StringBuilder();
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        sb.AppendLine("#requires -RunAsAdministrator");
        sb.AppendLine($"# InstallSentinel Rollback Script");
        sb.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"# Installer: {installerPath}");
        sb.AppendLine($"# SHA256: {installerSha256}");
        sb.AppendLine($"# Actions: {actions.Count}");
        sb.AppendLine();
        sb.AppendLine("Set-StrictMode -Version Latest");
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine();
        sb.AppendLine("try {");
        sb.AppendLine("    Write-Host 'InstallSentinel Rollback Script' -ForegroundColor Cyan");
        sb.AppendLine($"    Write-Host 'Rolling back {actions.Count} actions...' -ForegroundColor Yellow");
        sb.AppendLine();

        var fileActions = actions.Where(a => a.ActionType is RollbackActionType.DeleteFile or RollbackActionType.RestoreFile).ToList();
        var registryActions = actions.Where(a => a.ActionType.ToString().StartsWith("Registry")).ToList();
        var processActions = actions.Where(a => a.ActionType == RollbackActionType.TerminateProcess).ToList();

        if (fileActions.Count > 0)
        {
            sb.AppendLine("    # ===== FILE SYSTEM ROLLBACK =====");
            foreach (var action in fileActions)
            {
                var escapedPath = EscapePowerShellString(action.TargetPath);
                if (action.ActionType == RollbackActionType.DeleteFile)
                {
                    sb.AppendLine($"    if (Test-Path '{escapedPath}') {{");
                    sb.AppendLine($"        Remove-Item -Path '{escapedPath}' -Force -ErrorAction SilentlyContinue");
                    sb.AppendLine($"        Write-Host \"Deleted: {escapedPath}\" -ForegroundColor Red");
                    sb.AppendLine("    }");
                }
                else if (action.ActionType == RollbackActionType.RestoreFile)
                {
                    sb.AppendLine($"    # Restore: {escapedPath} (backup required manually)");
                    sb.AppendLine($"    Write-Host \"Manual restore needed: {escapedPath}\" -ForegroundColor Yellow");
                }
            }
            sb.AppendLine();
        }

        if (registryActions.Count > 0 && _settings.IncludeRegistryRollback)
        {
            sb.AppendLine("    # ===== REGISTRY ROLLBACK =====");
            foreach (var action in registryActions)
            {
                var escapedPath = EscapePowerShellString(action.TargetPath);
                var valueName = action.RegistryValueName ?? string.Empty;

                switch (action.ActionType)
                {
                    case RollbackActionType.DeleteRegistryKey:
                        sb.AppendLine($"    if (Test-Path '{escapedPath}') {{");
                        sb.AppendLine($"        Remove-Item -Path '{escapedPath}' -Recurse -Force -ErrorAction SilentlyContinue");
                        sb.AppendLine($"        Write-Host \"Deleted registry key: {escapedPath}\" -ForegroundColor Red");
                        sb.AppendLine("    }");
                        break;
                    case RollbackActionType.RestoreRegistryKey:
                        sb.AppendLine($"    # Restore registry key: {escapedPath} (backup required manually)");
                        sb.AppendLine($"    Write-Host \"Manual restore needed: {escapedPath}\" -ForegroundColor Yellow");
                        break;
                    case RollbackActionType.DeleteRegistryValue:
                        sb.AppendLine($"    if (Test-Path '{escapedPath}') {{");
                        sb.AppendLine($"        Remove-ItemProperty -Path '{escapedPath}' -Name '{valueName}' -ErrorAction SilentlyContinue");
                        sb.AppendLine($"        Write-Host \"Deleted registry value: {escapedPath}\\{valueName}\" -ForegroundColor Red");
                        sb.AppendLine("    }");
                        break;
                    case RollbackActionType.RestoreRegistryValue:
                        sb.AppendLine($"    # Restore registry value: {escapedPath}\\{valueName} (backup required manually)");
                        sb.AppendLine($"    Write-Host \"Manual restore needed: {escapedPath}\\{valueName}\" -ForegroundColor Yellow");
                        break;
                }
            }
            sb.AppendLine();
        }

        if (processActions.Count > 0)
        {
            sb.AppendLine("    # ===== PROCESS TERMINATION =====");
            foreach (var action in processActions)
            {
                sb.AppendLine($"    try {{");
                sb.AppendLine($"        $proc = Get-Process -Id {action.TargetPath} -ErrorAction SilentlyContinue");
                sb.AppendLine($"        if ($proc) {{");
                sb.AppendLine($"            Stop-Process -Id {action.TargetPath} -Force -ErrorAction SilentlyContinue");
                sb.AppendLine($"            Write-Host \"Terminated PID: {action.TargetPath}\" -ForegroundColor Red");
                sb.AppendLine($"        }}");
                sb.AppendLine($"    }} catch {{");
                sb.AppendLine($"        Write-Host \"Failed to terminate PID {action.TargetPath}: $_\" -ForegroundColor Red");
                sb.AppendLine($"    }}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("    Write-Host 'Rollback completed.' -ForegroundColor Green");
        sb.AppendLine("} catch {");
        sb.AppendLine("    Write-Host \"Rollback failed: $_\" -ForegroundColor Red");
        sb.AppendLine("    exit 1");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string EscapePowerShellString(string input)
    {
        return input.Replace("'", "''").Replace("$", "`$").Replace("@", "`@");
    }

    private static int CountMatches(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }

    private string GetDefaultScriptPath()
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var counter = Interlocked.Increment(ref _scriptCounter);
        var fileName = $"{Constants.Rollback.ScriptPrefix}{timestamp}_{counter}{Constants.Rollback.ScriptExtension}";
        return Path.Combine(_settings.OutputDirectory, fileName);
    }

    private void CleanupOldScripts()
    {
        try
        {
            var files = new DirectoryInfo(_settings.OutputDirectory)
                .GetFiles($"{Constants.Rollback.ScriptPrefix}*{Constants.Rollback.ScriptExtension}")
                .OrderByDescending(f => f.LastWriteTime)
                .Skip(_settings.MaxRollbackScripts)
                .ToList();

            foreach (var file in files)
                file.Delete();
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private sealed class RegistryChangeInfo
    {
        public required string Path { get; init; }
        public required ActionType Action { get; init; }
        public required SystemEvent Event { get; init; }
    }
}
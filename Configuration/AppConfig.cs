namespace InstallSentinel.Configuration;

public sealed class AppConfig
{
    public VirusTotalSettings VirusTotal { get; init; } = new();
    public EtwSettings Etw { get; init; } = new();
    public NoiseFilterSettings NoiseFilter { get; init; } = new();
    public RollbackSettings Rollback { get; init; } = new();
    public UiSettings Ui { get; init; } = new();
}

public sealed class VirusTotalSettings
{
    public string ApiKey { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = "https://www.virustotal.com/api/v3";
    public int TimeoutSeconds { get; init; } = 30;
    public bool Enabled { get; init; } = false;
}

public sealed class EtwSettings
{
    public string SessionName { get; init; } = "InstallSentinel_ETW";
    public int BufferSizeMb { get; init; } = 64;
    public int MinBuffers { get; init; } = 64;
    public int MaxBuffers { get; init; } = 256;
    public TimeSpan FlushTimer { get; init; } = TimeSpan.FromSeconds(1);
    public string[] KernelProviders { get; init; } = ["Microsoft-Windows-Kernel-File", "Microsoft-Windows-Kernel-Registry", "Microsoft-Windows-Kernel-Process"];
}

public sealed class NoiseFilterSettings
{
    public string[] ExcludedPaths { get; init; } =
    [
        @"C:\Windows\Temp\*",
        @"C:\Windows\Prefetch\*",
        @"C:\Windows\Logs\*",
        @"C:\ProgramData\Microsoft\Windows\WER\*",
        @"C:\Users\*\AppData\Local\Temp\*",
        @"C:\Windows\System32\config\*",
        @"C:\Windows\System32\wbem\*",
        @"C:\Windows\System32\wbem\Repository\*",
        @"C:\Windows\System32\LogFiles\*",
        @"C:\Windows\Temp\*",
        @"C:\$Recycle.Bin\*",
        @"C:\System Volume Information\*",
        @"C:\Windows\Microsoft.NET\Framework*\Temporary ASP.NET Files\*",
        @"C:\Windows\Microsoft.NET\Framework64*\Temporary ASP.NET Files\*",
        @"C:\Windows\SoftwareDistribution\*",
        @"C:\Windows\WinSxS\*",
        @"C:\Windows\servicing\*"
    ];

    public int[] ExcludedPids { get; init; } = [4, 8]; // System, Registry
    public string[] ExcludedProcessNames { get; init; } = ["System", "Registry", "smss.exe", "csrss.exe", "wininit.exe", "services.exe", "lsass.exe", "svchost.exe", "fontdrvhost.exe", "dwm.exe", "SearchIndexer.exe", "MsMpEng.exe", "NisSrv.exe"];
    public string[] ExcludedExtensions { get; init; } = [".tmp", ".temp", ".log", ".etl", ".evt", ".evtx", ".wer", ".dmp", ".mdmp", ".hdmp"];
}

public sealed class RollbackSettings
{
    public string OutputDirectory { get; init; } = @"C:\InstallSentinel\Rollbacks";
    public bool CreateSystemRestorePoint { get; init; } = true;
    public bool IncludeRegistryRollback { get; init; } = true;
    public bool IncludeFileRollback { get; init; } = true;
    public int MaxRollbackScripts { get; init; } = 50;
}

public sealed class UiSettings
{
    public int TableRefreshRateMs { get; init; } = 250;
    public int MaxTableRows { get; init; } = 200;
    public bool ShowPidColumn { get; init; } = true;
    public bool ShowThreadIdColumn { get; init; } = false;
    public bool EnableColors { get; init; } = true;
    public string Theme { get; init; } = "dark"; // dark, light, auto
}
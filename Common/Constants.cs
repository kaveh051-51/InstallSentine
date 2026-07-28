namespace InstallSentinel.Common;

public static class Constants
{
    public static class KernelEventProviders
    {
        public const string FileIo = "Microsoft-Windows-Kernel-File";
        public const string Registry = "Microsoft-Windows-Kernel-Registry";
        public const string Process = "Microsoft-Windows-Kernel-Process";
        public const string Thread = "Microsoft-Windows-Kernel-Thread";
        public const string ImageLoad = "Microsoft-Windows-Kernel-ImageLoad";
        public const string Network = "Microsoft-Windows-Kernel-Network";
    }

    public static class KernelEventKeywords
    {
        public const ulong FileIo = 0x20;
        public const ulong Registry = 0x40;
        public const ulong Process = 0x10;
        public const ulong Thread = 0x200;
        public const ulong ImageLoad = 0x40;
        public const ulong Network = 0x1000;
    }

    public static class Process
    {
        public static readonly int[] SystemPids = [4, 8];
        public static readonly string[] SystemProcessNames =
        [
            "System", "Registry", "smss.exe", "csrss.exe", "wininit.exe",
            "services.exe", "lsass.exe", "svchost.exe", "fontdrvhost.exe",
            "dwm.exe", "SearchIndexer.exe", "MsMpEng.exe", "NisSrv.exe",
            "taskhostw.exe", "explorer.exe", "RuntimeBroker.exe", "ShellExperienceHost.exe"
        ];
    }

    public static class FileSystem
    {
        public static readonly string[] ExcludedExtensions =
        [
            ".tmp", ".temp", ".log", ".etl", ".evt", ".evtx", ".wer", ".dmp",
            ".mdmp", ".hdmp", ".old", ".bak", ".swp", ".swo", ".~", ".tmp"
        ];

        public static readonly string[] ProtectedPaths =
        [
            @"C:\Windows",
            @"C:\Program Files",
            @"C:\Program Files (x86)",
            @"C:\ProgramData\Microsoft",
            @"C:\Users\Default",
            @"C:\Users\Public"
        ];
    }

    public static class Registry
    {
        public static readonly string[] ExcludedKeys =
        [
            @"\REGISTRY\MACHINE\HARDWARE",
            @"\REGISTRY\MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager",
            @"\REGISTRY\MACHINE\SYSTEM\CurrentControlSet\Services\Tcpip",
            @"\REGISTRY\MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer",
            @"\REGISTRY\USER\S-1-5-18",
            @"\REGISTRY\USER\S-1-5-19",
            @"\REGISTRY\USER\S-1-5-20"
        ];
    }

    public static class VirusTotal
    {
        public const string UserAgent = "InstallSentinel/1.0";
        public const int RateLimitPerMinute = 4;
    }

    public static class Rollback
    {
        public const string ScriptPrefix = "rollback_";
        public const string ScriptExtension = ".ps1";
        public const string BackupDirName = "InstallSentinel_Backups";
    }

    public static class Etw
    {
        public const string DefaultSessionName = "InstallSentinel_ETW";
        public const int DefaultBufferSizeMb = 64;
        public const int DefaultMinBuffers = 64;
        public const int DefaultMaxBuffers = 256;
        public static readonly TimeSpan DefaultFlushTimer = TimeSpan.FromSeconds(1);
    }

    public static class Ui
    {
        public const int DefaultTableRefreshMs = 250;
        public const int DefaultMaxTableRows = 200;
        public const string DefaultTheme = "dark";
    }
}
namespace InstallSentinel.Models.Enums;

public enum EventCategory
{
    Unknown = 0,
    FileSystem = 1,
    Registry = 2,
    Process = 3,
    Network = 4,
    ImageLoad = 5,
    Thread = 6
}

public enum ActionType
{
    Unknown = 0,
    Create = 1,
    Delete = 2,
    Modify = 3,
    Rename = 4,
    Read = 5,
    Write = 6,
    SetValue = 7,
    DeleteValue = 8,
    CreateKey = 9,
    DeleteKey = 10,
    Start = 11,
    Exit = 12,
    Load = 13,
    Unload = 14
}

public enum ThreatStatus
{
    Unknown = 0,
    Benign = 1,
    Suspicious = 2,
    Malicious = 3,
    NotScanned = 4
}

public enum ProcessTreeRelation
{
    Root = 0,
    Child = 1,
    GrandChild = 2,
    Injected = 3
}

public enum RollbackActionType
{
    DeleteFile = 0,
    RestoreFile = 1,
    DeleteRegistryKey = 2,
    RestoreRegistryKey = 3,
    DeleteRegistryValue = 4,
    RestoreRegistryValue = 5,
    TerminateProcess = 6,
    DeleteDirectory = 7,
    RestoreDirectory = 8
}
# Architecture Specification & Folder Map: InstallSentinel

## 1. System High-Level Architecture

`InstallSentinel` is built on a decoupled, pipeline-based architecture in C# (.NET 8/9) for Windows 10/11.

```text
[Installer .exe]
       │
       ▼
[ProcessLauncherService] ────► Track Root PID & Child PIDs
       │
       ├──► [EtwMonitorEngine] (Kernel Trace) ──► [NoiseFilterService]
       │                                                 │
       │                                                 ▼
       │                                    [Channel<SystemEvent>]
       │                                                 │
       ▼                                                 ▼
[Report / State Aggregator] ◄────────────────────────────┘
       │
       ├──► [RollbackGenerator] ──► Output: `rollback.ps1`
       └──► [UI Endpoint]        ──► Rendered by UI Layer
```

---

## 2. Directory Structure & File Responsibilities

```text
InstallSentinel/
├── InstallSentinel.csproj           # C# project settings, target framework, and NuGet dependencies.
├── app.manifest                     # Windows UAC manifest requesting Administrator privileges.
├── Program.cs                       # Application entry point, Host Builder, and DI Container setup.
│
├── Configuration/                   # App Settings & Options Mapping
│   └── AppConfig.cs                 # C# strongly-typed class mapped from appsettings.json.
│
├── Models/                          # Data Structures & Entities
│   ├── Enums/
│   │   └── Enums.cs                 # EventCategory, ActionType, ThreatStatus, ProcessTreeRelation, RollbackActionType.
│   ├── Models.cs                    # SystemEvent, ProcessNode, VirusTotalReport, MonitoringReport, RollbackAction.
│   └── MonitorConfiguration.cs      # ETW monitor configuration record.
│
├── Services/                        # Core Business & System Logic
│   ├── Interfaces/                  # Service Contracts (Enforces decoupled architecture)
│   │   ├── IPrivilegeService.cs     # Contract for Admin rights checking.
│   │   ├── IVirusTotalService.cs    # Contract for SHA256 VT API scanning.
│   │   ├── IProcessLauncher.cs      # Contract for running target EXE & capturing child PIDs.
│   │   ├── IEtwMonitorEngine.cs     # Contract for ETW kernel session & streaming events.
│   │   ├── INoiseFilterService.cs   # Contract for filtering Windows background noise.
│   │   └── IRollbackGenerator.cs    # Contract for producing the PowerShell rollback script.
│   │
│   ├── PrivilegeService.cs          # Checks if app is executing with elevated Administrator rights.
│   ├── VirusTotalService.cs         # Queries VirusTotal API for installer executable hash.
│   ├── ProcessLauncherService.cs    # Executes installer .exe, hooks process start events, tracks PID tree.
│   ├── EtwMonitorEngine.cs          # Initializes TraceEventSession, parses raw Kernel FileIO/Registry events.
│   ├── NoiseFilterService.cs        # Applies path and PID exclusion rules to strip OS noise.
│   └── RollbackGenerator.cs         # Compiles MonitoringReport into an idempotent rollback.ps1 script.
│
├── UI/                              # User Interface Layer (Detailed specs in DESIGN.md)
│   ├── Components/
│   │   ├── LiveEventTable.cs        # Real-time event table with Spectre.Console.Live.
│   │   └── SummaryTreeRenderer.cs   # Process hierarchy tree + summary panel.
│   └── TerminalApp.cs               # Orchestrates application workflow (3 screens).
│
├── Common/                          # Cross-Cutting Utilities & Helpers
│   ├── Constants.cs                 # System-wide static constants, default paths, and system strings.
│   └── Helpers/
│       ├── PathSanitizer.cs         # Translates kernel device paths to drive letters, truncation, GetShortPath.
│       └── HashUtils.cs             # Computes SHA256/MD5 hashes of files.
│
├── tests/                           # Unit Test Project
│   └── InstallSentinel.Tests/
│       ├── Helpers/
│       │   └── PathSanitizerTests.cs       # 9 tests: NormalizePath, TruncatePath, GetShortPath.
│       ├── Models/
│       │   └── ModelsTests.cs              # 7 tests: SystemEvent, MonitoringReport, VirusTotalReport, ProcessNode.
│       └── Services/
│           ├── NoiseFilterServiceTests.cs  # 11 tests: ShouldFilter, IsExcluded*, exclusions, statistics.
│           ├── RollbackGeneratorTests.cs   # 7 tests: GenerateRollbackScript, ValidateScript, directory.
│           ├── ProcessLauncherServiceTests.cs # 5 tests: GetTrackedPids, LaunchAndTrack, WaitForProcessTree.
│           └── EtwMonitorEngineTests.cs    # 6 tests: IsRunning, SessionName, GetStatistics, events, StopAsync.
│
├── Plans/                           # Implementation Plans
│   └── 01-fix-all-build-errors.md   # Categorized plan for fixing 35 build errors.
│
├── appsettings.json                 # Runtime configuration (ETW, VT API, NoiseFilter, Rollback settings).
│
├── ARCHITECTURE.md                  # This file.
├── DESIGN.md                        # Terminal UI/UX design specifications.
├── AGENTS.md                        # AI Agent coding guidelines.
├── CONTEXT.md                       # Implementation status and current context.
├── IDEA.md                          # Project vision and rationale.
└── README.md                        # Project overview and quick start.
```

---

## 3. Component Interaction & Data Pipeline

1. **Host Bootstrapping (`Program.cs`):**
   Configures Dependency Injection (DI) and registers all services from `Services/` as singletons/transients.

2. **Target Initialization (`ProcessLauncherService` & `VirusTotalService`):**
   `VirusTotalService` checks file SHA256. `ProcessLauncherService` starts the installer, records the root PID, and maintains an active set of tracked PIDs (Root + Child processes).

3. **Event Capture & Streaming (`EtwMonitorEngine` & `NoiseFilterService`):**
   `EtwMonitorEngine` hooks Windows Kernel ETW events via `MonitorConfiguration`. Valid events are pushed into an asynchronous `Channel<SystemEvent>`.

4. **Aggregation & Script Generation (`RollbackGenerator`):**
   Events from the channel are aggregated into `MonitoringReport`. Upon installation completion, `RollbackGenerator` converts this report into a PowerShell `rollback.ps1` script.

5. **UI Layer Boundary (`UI/TerminalApp.cs`):**
   Consumes events to display real-time progress via `LiveEventTable` and renders the final summary via `SummaryTreeRenderer`.

---

## 4. Key Design Patterns

| Pattern | Where Used | Purpose |
|---|---|---|
| **Interface Segregation** | `Services/Interfaces/` | Loose coupling, testability |
| **Pipeline** | ETW → NoiseFilter → Channel → UI | Clean data flow |
| **Producer-Consumer** | `Channel<SystemEvent>` | Async event streaming |
| **Observer** | `ProcessSpawned`, `EventReceived` events | Decoupled notifications |
| **Strategy** | Noise filter rules | Configurable exclusion logic |

---

## 5. NuGet Dependencies

| Package | Version | Purpose |
|---|---|---|
| `Spectre.Console` | 0.49.1 | Terminal UI framework |
| `Spectre.Console.Cli` | 0.49.1 | CLI command parsing |
| `Microsoft.Diagnostics.Tracing.TraceEvent` | 3.1.10 | ETW kernel tracing |
| `Microsoft.Extensions.DependencyInjection` | 9.0.0 | DI container |
| `Microsoft.Extensions.Options` | 9.0.0 | Configuration binding |
| `Microsoft.Extensions.Configuration.Json` | 9.0.0 | JSON config loading |
| `Microsoft.Extensions.Logging` | 9.0.0 | Logging abstractions |
| `System.Management` | 9.0.0 | WMI process queries |

---

## 6. Test Project Dependencies

| Package | Version | Purpose |
|---|---|---|
| `xunit` | 2.9.0 | Test framework |
| `xunit.runner.visualstudio` | 2.8.2 | Test runner |
| `NSubstitute` | 5.1.0 | Mocking framework |
| `FluentAssertions` | 6.12.0 | Assertion library |
| `Microsoft.NET.Test.Sdk` | 17.11.0 | Test SDK |
| `coverlet.collector` | 6.0.0 | Code coverage |
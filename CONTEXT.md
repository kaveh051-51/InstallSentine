# InstallSentinel - Implementation Context & Plan

## Project Goal
Terminal-based security monitoring tool for Windows installer tracking (.exe/.msi) using ETW kernel tracing, with rollback script generation.

## Architecture Reference
- **ARCHITECTURE.md** - Folder structure, interfaces, data pipeline, NuGet dependencies
- **DESIGN.md** - Spectre.Console UI specs (3 screens, color palette, components)
- **AGENTS.md** - Coding standards, execution protocol, testing requirements
- **Plans/01-fix-all-build-errors.md** - Categorized plan for fixing all 35 build errors

---

## Implementation Phases (Ordered)

### Phase 1: Foundation (Interfaces + Models + Config) ✅ COMPLETE
- [x] `Configuration/AppConfig.cs` - Strongly-typed settings from appsettings.json
- [x] `Models/Enums/Enums.cs` - EventCategory, ActionType, ThreatStatus, ProcessTreeRelation, RollbackActionType
- [x] `Models/Models.cs` - SystemEvent, ProcessNode, VirusTotalReport, MonitoringReport, RollbackAction
- [x] `Models/MonitorConfiguration.cs` - ETW monitor configuration record
- [x] `Services/Interfaces/` - All 6 service contracts
- [x] `Common/Helpers/PathSanitizer.cs` - Device path → drive letter translation, path truncation, GetShortPath
- [x] `Common/Helpers/HashUtils.cs` - SHA256/MD5 computation
- [x] `Common/Constants.cs` - System-wide constants, default paths, exclusion lists

### Phase 2: Core Services Implementation ✅ COMPLETE
- [x] `Services/PrivilegeService.cs` - Admin elevation check
- [x] `Services/VirusTotalService.cs` - SHA256 API scanning
- [x] `Services/ProcessLauncherService.cs` - Execute installer, track PID tree (WMI), WaitForProcessTreeAsync
- [x] `Services/EtwMonitorEngine.cs` - TraceEventSession, Kernel FileIO/Registry events
- [x] `Services/NoiseFilterService.cs` - Path/PID exclusion rules
- [x] `Services/RollbackGenerator.cs` - MonitoringReport → rollback.ps1

### Phase 3: UI Layer (Spectre.Console) ✅ COMPLETE
- [x] `UI/Components/LiveEventTable.cs` - Real-time event table with color coding
- [x] `UI/Components/SummaryTreeRenderer.cs` - Process hierarchy tree + summary panel
- [x] `UI/TerminalApp.cs` - Main workflow orchestrator (3 screens)

### Phase 4: Host & Integration ✅ COMPLETE
- [x] `Program.cs` - DI container, service registration, app entry point
- [x] `app.manifest` - UAC admin requirement

### Phase 5: Build Error Fixes ✅ COMPLETE
- [x] Fixed 35 build errors across 12 categorized plans (see Plans/01-fix-all-build-errors.md)
- [x] Build: 0 errors, 0 warnings
- [x] dotnet format applied

### Phase 6: Unit Tests ✅ COMPLETE
- [x] Created test project: `tests/InstallSentinel.Tests/InstallSentinel.Tests.csproj`
- [x] Test dependencies: xUnit, NSubstitute, FluentAssertions, coverlet
- [x] 54 tests across 6 test classes — all passing

#### Test Coverage:
- **PathSanitizerTests** (9 tests) - NormalizePath, TruncatePath, GetShortPath
- **NoiseFilterServiceTests** (11 tests) - ShouldFilter, IsExcluded*, AddExclusions, GetStatistics
- **RollbackGeneratorTests** (7 tests) - GenerateRollbackScriptAsync, ValidateScript, GetRollbackDirectory
- **ProcessLauncherServiceTests** (5 tests) - GetTrackedPids, LaunchAndTrackAsync, WaitForProcessTreeAsync, GetProcessTreeAsync
- **EtwMonitorEngineTests** (6 tests) - IsRunning, SessionName, GetStatistics, EventReceived/ErrorOccurred, StopAsync
- **ModelsTests** (7 tests) - SystemEvent.DisplayAction, MonitoringReport.Duration/Counts, VirusTotalReport.ThreatStatus, ProcessNode.TotalEvents

---

## Current Status

| Metric | Status |
|---|---|
| **Build** | ✅ 0 errors, 0 warnings |
| **Tests** | ✅ 54/54 passing (205ms) |
| **Format** | ✅ dotnet format clean |
| **Phase** | 6/7 complete |

**Next Action:** Phase 7 — Integration testing with real ETW sessions

---

## Key Technical Decisions
- .NET 8 Windows (net8.0-windows10.0.19041.0)
- Single-file self-contained publish (win-x64)
- Spectre.Console for TUI
- Microsoft.Diagnostics.Tracing.TraceEvent for ETW
- Channel\<SystemEvent\> for async event streaming
- NSubstitute + xUnit + FluentAssertions for testing

## Risk Areas
- ETW session requires Admin + proper cleanup (IDisposable)
- PathSanitizer must handle all \Device\HarddiskVolumeX formats
- NoiseFilter rules must be comprehensive to avoid false positives
- Rollback script must be idempotent and safe
- ProcessLauncherService uses WMI (System.Management) for parent PID lookup
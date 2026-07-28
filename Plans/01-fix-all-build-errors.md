# Fix All Build Errors — Categorized Plan

## Build Status: 35 Errors, 5 Warnings

---

## Plan A: Missing `using InstallSentinel.Common.Helpers` (6 errors)

**Files affected:** SummaryTreeRenderer.cs, LiveEventTable.cs, RollbackGenerator.cs, NoiseFilterService.cs (×2)

**Root cause:** `PathSanitizer` is in `InstallSentinel.Common.Helpers` namespace. Multiple files reference it without the using directive.

**Fix:** Add `using InstallSentinel.Common.Helpers;` to:
- `UI/Components/SummaryTreeRenderer.cs`
- `UI/Components/LiveEventTable.cs`
- `Services/RollbackGenerator.cs`
- `Services/NoiseFilterService.cs`

**Errors fixed:**
- CS0103: SummaryTreeRenderer.cs:50
- CS0103: LiveEventTable.cs:105
- CS0103: RollbackGenerator.cs:112, 130
- CS0103: NoiseFilterService.cs:99, 178

---

## Plan B: Add `PathSanitizer.GetShortPath` method (1 error)

**File:** Models/Models.cs:24

**Root cause:** `SystemEvent.ShortPath` calls `PathSanitizer.GetShortPath(TargetPath)` but this method doesn't exist.

**Fix:** Add `GetShortPath` method to `PathSanitizer.cs` — simply delegates to `TruncatePath`:
```csharp
public static string GetShortPath(string path, int maxLength = 80)
    => TruncatePath(path, maxLength);
```

**Errors fixed:** CS0117: Models.cs:24

---

## Plan C: Add `ProcessPath` and `StartTime` to `ProcessLaunchResult` (6 errors)

**File:** Services/Interfaces/IProcessLauncher.cs + Services/ProcessLauncherService.cs

**Root cause:** `ProcessLauncherService` constructs `ProcessLaunchResult` with `ProcessPath` and `StartTime` fields that don't exist on the record.

**Fix:** Add fields to `ProcessLaunchResult` record:
```csharp
public string? ProcessPath { get; init; }
public DateTime StartTime { get; init; }
```

**Errors fixed:**
- CS0117: ProcessLauncherService.cs:40, 41, 65, 66, 104, 105, 116, 117

---

## Plan D: Fix `Process.WaitForInputIdleAsync` + `ProcessTreeRelation` using (5 errors)

**File:** Services/ProcessLauncherService.cs

**Root cause 1:** `Process.WaitForInputIdleAsync(TimeSpan, CancellationToken)` doesn't exist in .NET 8. Only `WaitForInputIdle()` (sync, no args) exists.

**Fix 1:** Replace with:
```csharp
_rootProcess.WaitForInputIdle(10000); // 10s timeout, synchronous
```
Or wrap in `Task.Run` for async pattern.

**Root cause 2:** `ProcessTreeRelation` enum is in `InstallSentinel.Models.Enums` — missing using directive.

**Fix 2:** Add `using InstallSentinel.Models.Enums;` to ProcessLauncherService.cs.

**Errors fixed:**
- CS1061: ProcessLauncherService.cs:71
- CS0103: ProcessLauncherService.cs:92, 221, 271, 294

---

## Plan E: Add `EventReceived` and `ErrorOccurred` to `IEtwMonitorEngine` (2 errors)

**File:** Services/Interfaces/IEtwMonitorEngine.cs

**Root cause:** `EtwMonitorEngine` (concrete class) declares `event EventHandler<SystemEvent>? EventReceived` and `event EventHandler<Exception>? ErrorOccurred`, but the interface `IEtwMonitorEngine` does not. `TerminalApp` codes against the interface and can't see them.

**Fix:** Add to interface:
```csharp
event EventHandler<SystemEvent>? EventReceived;
event EventHandler<Exception>? ErrorOccurred;
```

**Errors fixed:**
- CS1061: TerminalApp.cs:224, 225

---

## Plan F: Add `MonitorConfiguration` + `WaitForProcessTreeAsync` (2 errors)

**File:** UI/TerminalApp.cs:241, 278

**Root cause 1:** `TerminalApp` creates `new MonitorConfiguration { ... }` with fields like `RootProcessId`, `ProcessTreePids`, `SessionName`, `BufferSizeMb`, etc. This type doesn't exist.

**Fix 1:** Two options:
  - Option A: Create `MonitorConfiguration` record in `Models/` or `Services/Interfaces/`
  - Option B: Refactor `IEtwMonitorEngine.StartAsync` to accept a config object instead of separate params

  **Decision:** Option B — Change `IEtwMonitorEngine.StartAsync` to accept a `MonitorConfiguration` record. Create it in `Models/MonitorConfiguration.cs` (or in interfaces file). Add `EtwSettings` fields to it.

**Root cause 2:** `IProcessLauncher` doesn't declare `WaitForProcessTreeAsync`. The `ProcessLauncherService` doesn't implement it either.

**Fix 2:** Add to `IProcessLauncher`:
```csharp
Task<bool> WaitForProcessTreeAsync(int rootPid, TimeSpan timeout, CancellationToken cancellationToken = default);
```
Implement in `ProcessLauncherService` — check if root process has exited within timeout.

**Errors fixed:**
- CS0246: TerminalApp.cs:241
- CS1061: TerminalApp.cs:278

---

## Plan G: Fix Spectre.Console Tree API (3 errors)

**File:** UI/Components/SummaryTreeRenderer.cs:14, 35, 36

**Root cause:**
1. Line 14: `RenderNode(tree, root)` — `tree` is `Tree`, but `RenderNode` expects `TreeNode`. `Tree.AddNode()` returns `TreeNode`. Need to add root as a node first.
2. Line 35: `style.Foreground?.ToString()` — `Color` is a struct, `?` can't apply.
3. Line 36: `childNode.Style = style` — `Style` is a method group on `TreeNode`, not a settable property.

**Fix:** Rewrite `RenderProcessTree` and `RenderNode`:
- `RenderProcessTree`: create tree, then add first node manually, then recurse on children
- `RenderNode`: use `IHasTreeBranch` (parent) — actually `TreeNode.AddNode(string)` returns `TreeNode`
- For style: use `node.AddNode(new Markup(text, style))` instead of string with color

**Errors fixed:**
- CS1503: SummaryTreeRenderer.cs:14
- CS0023: SummaryTreeRenderer.cs:35
- CS1656: SummaryTreeRenderer.cs:36

---

## Plan H: Fix `AnsiConsole.MarkupLine()` + init-only props (3 errors)

**File:** UI/TerminalApp.cs:179, 342, 343

**Root cause 1:** `AnsiConsole.MarkupLine()` requires a string argument. Line 179 calls it with no args (empty line).

**Fix 1:** Change `AnsiConsole.MarkupLine();` → `AnsiConsole.WriteLine();`

**Root cause 2:** `MonitoringReport` is a `record` with `init`-only properties `RollbackScriptPath` and `RollbackScriptGenerated`. TerminalApp tries to set them after construction (lines 342, 343).

**Fix 2:** Change `MonitoringReport` properties from `init` to `set`:
```csharp
public string RollbackScriptPath { get; set; } = string.Empty;
public bool RollbackScriptGenerated { get; set; }
```

**Errors fixed:**
- CS1501: TerminalApp.cs:179
- CS8852: TerminalApp.cs:342, 343

---

## Plan I: Fix RollbackGenerator missing `System.Diagnostics` using (1 error)

**File:** Services/RollbackGenerator.cs:229

**Root cause:** Line 229 calls `Process.GetCurrentProcess().Id` but `Process` class is in `System.Diagnostics` which isn't imported.

**Fix:** Add `using System.Diagnostics;` to RollbackGenerator.cs.

**Errors fixed:** CS0103: RollbackGenerator.cs:229

---

## Plan J: Fix NoiseFilterService type mismatch + `EventCategory` using (3 errors)

**File:** Services/NoiseFilterService.cs:119, 128, 135

**Root cause 1:** Line 119: `_extensionExclusions.Contains(extension)` — `extension` is `string` (from `Path.GetExtension`), but `_extensionExclusions` is `HashSet<string>`. Wait — actually the error says `int` to `string`. Need to re-check line 119.

**Root cause 2:** Lines 128, 135: `EventCategory` is referenced but not imported. `EventCategory` is in `InstallSentinel.Models.Enums`.

**Fix:** Add `using InstallSentinel.Models.Enums;` to NoiseFilterService.cs.
For line 119: check if `_extensionExclusions.Contains(extension)` — the `extension` variable might be getting an int from somewhere. Investigate.

**Errors fixed:**
- CS1503: NoiseFilterService.cs:119
- CS0103: NoiseFilterService.cs:128, 135

---

## Plan K: Fix `KernelTraceEventParser.RegistryRename` (1 error)

**File:** Services/EtwMonitorEngine.cs:511

**Root cause:** `RegistryRename` event does not exist on `KernelTraceEventParser`. The TraceEvent library doesn't expose a kernel-level registry rename event.

**Fix:** Remove the `RegistryRename` subscription/unsubscription lines from both `StartAsync` and `StopAsync`. Remove the `OnRegRename` handler (or keep it but don't wire it up).

**Errors fixed:** CS1061: EtwMonitorEngine.cs:511

---

## Plan L: Separate test packages into test csproj (warning, structural)

**File:** InstallSentinel.csproj

**Root cause:** Test packages (xunit, NSubstitute, FluentAssertions, Microsoft.NET.Test.Sdk) are in the main project csproj. This causes CS7022 warning (entry point conflict) and is architecturally wrong.

**Fix:** Create `tests/InstallSentinel.Tests/InstallSentinel.Tests.csproj` with test packages, referencing the main project. Remove test packages from main csproj. This is Phase 5 work — defer for now, just remove from main csproj to fix the warning.

**Warnings fixed:** CS7022 (entry point conflict)

---

## Execution Order

1. **Plan A** (using directives) — touches 4 files, no logic changes
2. **Plan B** (GetShortPath) — add 1 method to PathSanitizer.cs
3. **Plan C** (ProcessLaunchResult fields) — add 2 fields to record
4. **Plan D** (WaitForInputIdleAsync + using) — fix 1 method call + 1 using
5. **Plan E** (IEtwMonitorEngine events) — add 2 events to interface
6. **Plan F** (MonitorConfiguration + WaitForProcessTreeAsync) — new record + new method
7. **Plan G** (Spectre.Console Tree) — rewrite SummaryTreeRenderer methods
8. **Plan H** (MarkupLine + init props) — 1 line fix + 2 property changes
9. **Plan I** (System.Diagnostics using) — 1 line fix
10. **Plan J** (EventCategory using + int→string) — 1 using + investigate
11. **Plan K** (RegistryRename) — remove 2 lines
12. **Plan L** (csproj cleanup) — remove test packages
13. **Build + verify**
14. **Re-index + update CONTEXT.md**

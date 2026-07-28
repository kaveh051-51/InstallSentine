# Terminal UI & UX Design Specification: InstallSentinel

## 1. Visual Identity & Theme (Dark Modern)

`InstallSentinel` adopts a **Modern Dark Cyber-Security** aesthetic. It uses high-contrast neon accents over deep dark terminal backgrounds to maximize readability, provide immediate visual feedback on critical system actions, and project a sleek, professional security tool feel.

### Color Palette Specification

| Role | Color Name | Hex Code | Spectre.Console Markup | Application |
| :--- | :--- | :--- | :--- | :--- |
| **Primary Accent** | Neon Cyan | `#06B6D4` | `[cyan]` or `[rgb(6,182,212)]` | Banners, Active Spinners, Key Headers |
| **Secondary Accent**| Electric Violet | `#8B5CF6` | `[purple]` or `[rgb(139,92,246)]` | Borders, Secondary Highlights, PIDs |
| **Success / Created**| Emerald Green | `#10B981` | `[green]` or `[rgb(16,185,129)]` | File/Registry Created, Clean Scans |
| **Warning / Modified**| Amber Yellow | `#F59E0B` | `[yellow]` or `[rgb(245,158,11)]` | File/Registry Modified, Suspicious |
| **Danger / Deleted**| Crimson Red | `#EF4444` | `[red]` or `[rgb(239,68,68)]` | File/Registry Deleted, Malicious Hash |
| **Muted / Subtitle**| Slate Gray | `#64748B` | `[grey]` or `[rgb(100,116,139)]` | Timestamps, System Noise, Inactive Paths |
| **Text Primary** | Pure White | `#F8FAFC` | `[white]` | Primary Labels, User Prompt Inputs |

---

## 2. Typography & ASCII Branding

### Main Banner (ASCII Art)

Rendered at application startup using a modern Figlet font or custom ASCII art block styled in `[bold cyan]`:

```text
  ___           _        _ll   ____             _   _inel 
 |_ _|_ __  ___| |_ __ _| | | / ___|  ___ _ __ | |_(_)_ __   ___| |
  | || '_ \/ __| __/ _` | | | \___ \ / _ \ '_ \| __| | '_ \ / _ \ |
  | || | | \__ \ || (_| | | |  ___) |  __/ | | | |_| | | | |  __/ |
 |___|_| |_\___/\__\__,_|_|_| |____/ \___|_| |_|\__|_|_| |_|\___|_|
```

### Badges & Status Indicators

- **Admin Elevation Active:** `[black on green] ADMIN ELEVATED [/]`
- **Admin Not Elevated:** `[black on red] NOT ADMIN - Limited functionality [/]`
- **VirusTotal Clean:** `[bold green] VirusTotal Scan Passed [/]`
- **VirusTotal Flagged:** `[bold red] VIRUS TOTAL: MALICIOUS DETECTED! [/]`
- **Live Tracing Active:** `[bold cyan] Step 2: Live Monitoring [/]`

---

## 3. UI Workflow & Screen Layouts

The CLI operates across 3 distinct interactive screens:

### Screen 1: Target Selection & Pre-Scan Screen

Includes the header banner, Administrator verification badge, executable input prompt, and VirusTotal hash checking spinner.

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│ [bold cyan]InstallSentinel v1.0[/]                          [black on green] ADMIN ELEVATED [/] │
└──────────────────────────────────────────────────────────────────────────────┘

 [?] Enter path to installer (.exe/.msi): C:\Downloads\setup.exe
 
 ⚋ [cyan]Checking VirusTotal Hash (SHA256: 8f3c...a912)...[/]
 [bold green]✓ VirusTotal Scan:[/] 0/72 engines flagged this binary.
```

### Screen 2: Real-time Live Monitoring Screen (Spectre.Console.Live)

During execution, a dynamic live-updating table renders incoming `SystemEvent` instances streamed from the ETW Channel.

- **Table Border:** `TableBorder.Rounded` styled in `[purple]`.
- **Top Bar:** Shows active Root PID, total event count, and uptime timer.
- **Bottom Status:** Real-time counters for Created, Modified, and Deleted actions.

```text
 [Live Tracing Target PID: 4820 (setup.exe)] ──────────────── Uptime: 00:00:14

 ┌──────────┬──────────┬───────────┬──────────────────────────────────────────┐
 │ Time     │ Category │ Action    │ Target Path                              │
 ├──────────┼──────────┼───────────┼──────────────────────────────────────────┤
 │ 14:02:01 │ File     │ [green]Created  [/] │ C:\Program Files\App\app.exe             │
 │ 14:02:02 │ Registry │ [yellow]Modified [/] │ HKLM\SOFTWARE\App\Settings\InstallDir    │
 │ 14:02:03 │ File     │ [red]Deleted  [/] │ C:\Users\User\AppData\Local\Temp\tmp.tmp │
 │ 14:02:05 │ Process  │ [cyan]Spawned  [/] │ PID: 6104 (cmd.exe /c helper.bat)        │
 └──────────┴──────────┴───────────┴──────────────────────────────────────────┘
  Events Captured: 142 | Files: 110 | Registry: 30 | Processes: 2
```

### Screen 3: Post-Install Analysis & Rollback Screen

Appears after the installer exits. Renders the **Process Hierarchy Tree** and provides a summary panel with the location of the generated `rollback.ps1` script.

```text
 ─── [bold cyan]Execution Summary & Process Tree[/] ───────────────────────────

 Root Process: setup.exe [PID: 4820]
 ├── helper.exe [PID: 6104]
 │   └── vcredist_x64.exe [PID: 7210]
 └── msiexec.exe [PID: 8012]

 ┌─ [bold white]Execution Summary[/] ─────────────────────────────────────────┐
 │ Installer Path : C:\Downloads\setup.exe                                    │
 │ SHA256         : abc123def456...                                           │
 │ Total Events   : 142                                                       │
 │ File Changes   : 110 (Created: 90, Modified: 15, Deleted: 5)              │
 │ Registry       : 30                                                        │
 │ Processes      : 2                                                         │
 │                                                                              │
 │ [bold yellow]Rollback Script:[/] C:\InstallSentinel\Rollbacks\rollback_YYYYMMDD.ps1 │
 └────────────────────────────────────────────────────────────────────────────┘
```

---

## 4. Spectre.Console Component Styling Rules

AI Coders implementing UI components in `UI/Components/` MUST use the following Spectre styling settings:

1. **Table Styling (`LiveEventTable.cs`):**
   - Use `TableBorder.Rounded` or `TableBorder.Minimal`.
   - Set column headers to `[bold cyan]`.
   - Action styling rule:
     - `ActionType.Create` ➔ `[bold green]Created[/]`
     - `ActionType.Modify` ➔ `[bold yellow]Modified[/]`
     - `ActionType.Delete` ➔ `[bold red]Deleted[/]`
     - `ActionType.Rename` ➔ `[bold purple]Renamed[/]`
     - `ActionType.Start` ➔ `[bold cyan]Spawned[/]`

2. **Panel Styling (`SummaryTreeRenderer.cs`):**
   - Header text styled in `[bold white]`.
   - Border colors set to `Color.Purple` or `Color.Cyan1`.
   - Use `RoundedBorder()` for modern look.

3. **Tree Styling (`SummaryTreeRenderer.cs`):**
   - Use `Tree` with `Markup` nodes (not raw strings with color tags).
   - Root node styled with `Color.Purple`.
   - Child nodes use `BuildNodeText()` for consistent formatting.

4. **Spinners & Status:**
   - Use `Spinner.Known.Dots` styled in `[cyan]`.

5. **Path Truncation:**
   - Long paths exceeding terminal width must be truncated gracefully using `PathSanitizer.GetShortPath()` or `PathSanitizer.TruncatePath()`.

---

## 5. Implementation Notes

- `TerminalApp.cs` orchestrates all 3 screens.
- `LiveEventTable.cs` handles Screen 2 with `AnsiConsole.Live()`.
- `SummaryTreeRenderer.cs` handles Screen 3 tree and summary panels.
- Events are streamed via `Channel<SystemEvent>` from `EtwMonitorEngine`.
- The UI never directly accesses ETW — it consumes events through the channel.
# InstallSentinel 🛡️

> A modern, real-time Windows installer behavioral monitor and automated rollback script generator powered by C# and Event Tracing for Windows (ETW).

![.NET 8.0+](https://img.shields.io/badge/.NET-8.0%2F9.0-512BD4?logo=dotnet)
![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20x64-0078D6?logo=windows)
![Privileges](https://img.shields.io/badge/Requires-Administrator-red)
![License](https://img.shields.io/badge/License-MIT-green)
![Tests](https://img.shields.io/badge/Tests-54%20passing-brightgreen)
![Build](https://img.shields.io/badge/Build-passing-success)

---

## 📖 Overview

**InstallSentinel** is a lightweight, high-performance security and behavioral analysis CLI tool designed for Windows. When executing an unknown installer (`.exe`/`.msi`), `InstallSentinel` launches the target in an isolated monitoring session, hooks into kernel-level events via **ETW**, and captures every file and registry modification made by the installer and its child processes.

Once installation finishes, `InstallSentinel` compiles a complete audit report and automatically generates a standalone PowerShell cleanup script (`rollback.ps1`) that can completely revert all modifications made to the system.

---

## ✨ Key Features

- ⚡ **Real-Time Kernel Tracing:** Monitors file creation, modification, deletion, and registry alterations without heavy kernel drivers using native ETW.
- 🌳 **Process Tree Tracking:** Automatically hooks child and grandchild processes spawned by the main installer (e.g., `cmd.exe`, `powershell.exe`, `vcredist.exe`).
- 🧹 **Automated Rollback Scripting:** Produces an idempotent `rollback.ps1` PowerShell script to safely revert system changes.
- 🧹 **Smart Noise Filtering:** Strips away ambient OS background noise (Prefetch, System32 logs, temp caches) to keep logs clean.
- 🔍 **VirusTotal Threat Check:** Pre-scans executable SHA256 hashes against VirusTotal API prior to execution.
- 🎨 **Modern Dark TUI:** Rich terminal interface built with `Spectre.Console` featuring live-updating tables, color-coded actions, and interactive process trees.
- ✅ **Comprehensive Unit Tests:** 54 unit tests covering PathSanitizer, NoiseFilterService, RollbackGenerator, ProcessLauncherService, EtwMonitorEngine, and Models.

---

## 📋 Prerequisites

- **Operating System:** Windows 10 or Windows 11 (x64)
- **SDK Runtime:** [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- **Privileges:** Administrator Rights (Required for Kernel ETW session tracing)

---

## 🚀 Quick Start & Building

### 1. Clone the Repository

```bash
git clone https://github.com/your-username/InstallSentinel.git
cd InstallSentinel
```

### 2. Build the Project

```bash
dotnet build InstallSentinel.csproj -c Release
```

### 3. Run with Administrator Privileges

Open PowerShell or Command Prompt **as Administrator** and run:

```powershell
dotnet run --project InstallSentinel.csproj -c Release
```

Or run the published single-file executable:

```powershell
.\bin\Release\net8.0-windows10.0.19041.0\win-x64\InstallSentinel.exe
```

### 4. Run Unit Tests

```bash
dotnet test tests/InstallSentinel.Tests/InstallSentinel.Tests.csproj
```

---

## 💻 Usage Example

1. Launch `InstallSentinel` in an elevated terminal.
2. Enter the full path to the installer when prompted:
   ```text
   [?] Enter path to installer (.exe/.msi): C:\Downloads\setup.exe
   ```
3. `InstallSentinel` will verify the binary hash with VirusTotal and spawn the installer.
4. Watch real-time file and registry modifications on the live terminal dashboard.
5. After installation completes, find your generated cleanup script at:
   ```text
   C:\InstallSentinel\Rollbacks\rollback_YYYYMMDD_HHMMSS_N.ps1
   ```

### Executing a Rollback

To undo all changes created during the installation:

```powershell
# Run in Elevated PowerShell
Set-ExecutionPolicy Bypass -Scope Process
.\rollback_YYYYMMDD_HHMMSS_N.ps1
```

---

## 📂 Project Documentation

For deeper technical details, refer to the documents in the root directory:

- 🏗️ **[ARCHITECTURE.md](ARCHITECTURE.md):** System architecture, data flow pipeline, DAG, and C# folder/file map.
- 🎨 **[DESIGN.md](DESIGN.md):** Terminal UI/UX design specifications, color schemes, and Spectre.Console styling.
- 🤖 **[AGENTS.md](AGENTS.md):** AI Agent coding rules, C# 12 standards, and development protocols.
- 📋 **[CONTEXT.md](CONTEXT.md):** Implementation status, phases, and current context.
- 💡 **[IDEA.md](IDEA.md):** Project vision and design rationale.

---

## ⚠️ Disclaimer

`InstallSentinel` is designed for system administration, software auditing, and security analysis purposes. Always test untrusted installers inside a dedicated Virtual Machine (VM) or sandbox environment.

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
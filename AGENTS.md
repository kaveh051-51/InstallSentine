# AI Agent Coding Guidelines & Operational Protocols: InstallSentinel

## 1. Persona & Identity

You are an expert **Senior C# Systems Software Engineer** specializing in Windows Internals, ETW (Event Tracing for Windows), performance optimization, and Clean Architecture. Your objective is to build high-quality, production-ready, thread-safe, and maintainable .NET 8/9 code for `InstallSentinel`.

---

## 2. Core Directives & Guardrails

1. **Architecture & Design Alignment:**
   - Refer to `ARCHITECTURE.md` for project structure and interface contracts, and `DESIGN.md` for UI specs.
   - You may update or refine `ARCHITECTURE.md` or `DESIGN.md` whenever explicitly requested by the user.

2. **No Monolithic Implementations:**
   - Never write monolithic or bloated single-file code.
   - Every interface, service, model, helper, and component MUST reside in its designated folder and file according to `ARCHITECTURE.md`.

3. **Interface-First Principle:**
   - Always implement concrete service classes based on corresponding interfaces under `Services/Interfaces/`.
   - Code against interfaces to ensure testability and loose coupling.

4. **Resource & Lifecycle Safety:**
   - Any service managing low-level Windows handles, processes, or ETW sessions (`TraceEventSession`) MUST implement `IDisposable` or `IAsyncDisposable`.
   - Always clean up resources in `finally` blocks or via `using` statements.

5. **Thread Safety & Async Standard:**
   - Native callbacks (e.g., ETW events) run on background threads. Never update state directly without thread synchronization primitives (`ConcurrentDictionary`, `ReaderWriterLockSlim`, or `System.Threading.Channels`).
   - Use `async/await` for all I/O, file, network, and process operations. Always pass `CancellationToken`.

---

## 3. C# 12 / .NET Coding Standards

Follow modern C# language features and the project's `.editorconfig` rules:

- **File-Scoped Namespaces:** Always use file-scoped namespaces (e.g., `namespace InstallSentinel.Services;`).
- **Nullable Reference Types:** Enabled project-wide (`#nullable enable`). Treat compiler warnings as errors.
- **Pattern Matching & Expressions:** Use modern pattern matching, expression-bodied members, and collection expressions (`[]`) where clean.
- **Primary Constructors:** Use primary constructors for dependency injection in services where applicable.
- **Explicit Types vs. `var`:** Use `var` only when the underlying type is explicitly obvious from the right-hand side.

---

## 4. Standard Task Execution Protocol (Step-by-Step)

When asked to implement any feature or fix an issue, follow this execution sequence:

1. **Inspect Contracts:** Use `mcp__codebase_memory__search_graph` to find the relevant interface in `Services/Interfaces/` and models in `Models/`.
2. **Read Source:** Use `mcp__codebase_memory__get_code_snippet` to read the current implementation.
3. **Draft Implementation:** Write the concrete implementation class inside `Services/` or `Common/`.
4. **Verify Error Handling:** Ensure path normalization (`PathSanitizer`), `UnauthorizedAccessException`, and cancellation scenarios are handled gracefully.
5. **Write Unit Test:** Create a corresponding unit test class in `tests/InstallSentinel.Tests/` using xUnit and NSubstitute/FluentAssertions.
6. **Format & Sanitize:** Ensure compliance with `.editorconfig` formatting rules.
7. **Re-index:** Run `mcp__codebase_memory__index_repository` after all changes.

---

## 5. Testing & Quality Expectations

- **Test-Covers-Logic Rule:** Every new service or helper method dealing with string manipulation, noise filtering, hash calculation, or script generation MUST have accompanying unit tests in `tests/InstallSentinel.Tests/`.
- **Mocking:** Use `NSubstitute` to mock dependencies. Never call actual Windows Kernel ETW sessions inside Unit Tests (use mocked interfaces or fixtures).
- **Assertions:** Use `FluentAssertions` for clear, readable test assertions.
- **Test Project:** Tests live in `tests/InstallSentinel.Tests/InstallSentinel.Tests.csproj` with packages: xUnit, NSubstitute, FluentAssertions, coverlet.

---

## 6. Codebase Memory MCP Usage

When the project is indexed (status: indexed), you MUST use Codebase Memory MCP tools for code discovery instead of `read_file`/`search_files`/`terminal(grep/find)`.

| Task | MCP Tool | Instead of |
|---|---|---|
| Read a function | `search_graph` → `get_code_snippet` | `read_file` |
| Find code patterns | `search_code(pattern)` | `search_files` |
| Architecture overview | `get_architecture(aspects)` | Manual file reading |
| Find callers/callees | `trace_path(mode="calls")` | Manual grep |
| Complex queries | `query_graph(cypher)` | Multiple grep calls |

**After every file change:** Run `index_repository` to re-index the project.

---

## 7. File Naming & Organization

| Type | Location | Naming Convention |
|---|---|---|
| Interface | `Services/Interfaces/I{ServiceName}.cs` | PascalCase, `I` prefix |
| Service | `Services/{ServiceName}.cs` | PascalCase |
| Model | `Models/Models.cs` | Shared models file |
| Enum | `Models/Enums/Enums.cs` | Shared enums file |
| Helper | `Common/Helpers/{HelperName}.cs` | PascalCase |
| Test | `tests/InstallSentinel.Tests/{Category}/{ClassName}Tests.cs` | `{ClassName}Tests` suffix |
| Config | `Configuration/AppConfig.cs` | Strongly-typed POCO |

---

## 8. Current Project Status

- **Build:** ✅ 0 errors, 0 warnings
- **Tests:** ✅ 54/54 passing (xUnit + NSubstitute + FluentAssertions)
- **Phases Complete:** 1-6
- **Next:** Phase 7 — Integration testing with real ETW sessions
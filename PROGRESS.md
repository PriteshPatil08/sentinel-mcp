# PROGRESS — Sentinel.MCP

Pickup guide. Every step documented with enough detail to resume cold.

---

## Environment

- **Platform:** Windows 11, .NET 10 (preview SDK)
- **Solution:** `Sentinel.MCP.slnx` (new-format .slnx, auto-discovers projects)
- **Repo:** `sentinel-mcp` on GitHub (renamed from `AgentToolbox.DotNet`)
- **Local folder:** `C:\Users\Pritesh\OneDrive\Desktop\PROJECTS\DOTNET\AgentToolbox.DotNet`
- **MCP server registered in Claude:** via `.mcp.json` at repo root
- **API key:** stored in user-secrets under `Sentinel.MCP.Client` project, key name `Anthropic:ApiKey`

---

## Project Layout

```
src/
  Sentinel.MCP.Contracts/     — DTOs + IToolResult (no deps, everything points here)
  Sentinel.MCP.Tools/         — Tool implementations (depends on Contracts)
  Sentinel.MCP.Server/        — MCP stdio server host (depends on Tools)
  Sentinel.MCP.Client/        — LLM agent client (Step 5 — in progress)
tests/
  Sentinel.MCP.Tools.Tests/
  Sentinel.MCP.Integration.Tests/
```

---

## Steps Completed

---

### Step 1 — Solution Structure & Repository Setup
**Date:** 2026-04-25 | **Commit:** `52aea87`, `cd3572f`

**What was built:**
- `dotnet new sln`, projects created under `src/` and `tests/`
- `Directory.Build.props` — solution-wide MSBuild settings (`TreatWarningsAsErrors=true`, `AnalysisMode=All`, `Nullable=enable`, `ImplicitUsings=enable`)
- `.editorconfig` — formatting contract
- `.gitignore` — via `dotnet new gitignore`
- `CA1303` suppressed globally (no localisation needed for this project)
- `LEARNINGS.md` created

**Key decisions:**
- `TreatWarningsAsErrors` on from day one — warnings are build failures, not suggestions
- `AnalysisMode=All` — all Roslyn analysers enabled

**Files created:** `Directory.Build.props`, `.editorconfig`, `LEARNINGS.md`

---

### Step 2 — Minimal MCP Server (Empty Shell)
**Date:** 2026-04-25 | **Commit:** `fe9032f`, `1255187`

**What was built:**
- `Sentinel.MCP.Server` as a console app using `Microsoft.NET.Sdk` (not Web)
- `ModelContextProtocol` NuGet 1.2.0 added
- `Program.cs` wires up: `Host.CreateApplicationBuilder` → `AddMcpServer()` → `WithStdioServerTransport()`
- Logging redirected to stderr via `LogToStandardErrorThreshold = LogLevel.Trace` (stdout belongs to the JSON-RPC protocol)
- Server identity set via `McpServerOptions` from `appsettings.json`

**Key decisions:**
- `Host.CreateApplicationBuilder` not `WebApplication.CreateBuilder` — no Kestrel, no HTTP listener
- stdout strictly reserved for MCP JSON-RPC frames — any console log corrupts the protocol

**Key files:** `src/Sentinel.MCP.Server/Program.cs`, `src/Sentinel.MCP.Server/appsettings.json`

---

### Chore — .NET 10 Migration
**Date:** 2026-04-25 | **Commit:** `70decf9`

**What changed:**
- `Directory.Build.props`: `net9.0` → `net10.0`
- All individual `.csproj` files: removed redundant `<TargetFramework>` (inherited from props)
- `NETSDK1057` info message appears on every build — expected, not an error (preview SDK)

**Key learning:** MSBuild property evaluation — project file wins over `Directory.Build.props`, so both had to change.

---

### Step 3 (Part 1) — Tool Contracts Layer
**Date:** 2026-04-26 | **Commit:** `2046951`, `dbc1cb5`

**What was built in `Sentinel.MCP.Contracts`:**

| File | Purpose |
|------|---------|
| `IToolResult.cs` | Covariant `IToolResult<out T>` interface |
| `ToolResult.cs` | `ToolResult<T>` sealed class + non-generic `ToolResult` companion with `Ok<T>` / `Fail<T>` factory methods |
| `ToolErrorCode.cs` | Enum: `Timeout`, `ConnectionFailed`, `SslError`, `InvalidInput`, `Unknown` |
| `ToolError.cs` | Structured error: `ErrorCode`, `Message`, `FieldErrors` |
| `HealthCheckRequest.cs` | `Uri? Url`, `int TimeoutSeconds`, `bool FollowRedirects` |
| `HealthCheckResponse.cs` | `StatusCode`, `LatencyMs`, `IsHealthy`, `ResponseHeaders`, etc. |

**Key decisions:**
- `out T` covariance on interface — `ToolResult<HealthCheckResponse>` satisfies `IToolResult<object>`
- `ToolResult` non-generic companion — enables type inference so callers write `ToolResult.Ok(data, ms)` not `ToolResult.Ok<HealthCheckResponse>(data, ms)`
- `init` properties + `sealed` — immutable-after-construction data carriers
- `IReadOnlyList<string>` not `List<string>` for collection properties (CA1002)

---

### Step 3 (Part 2) — HealthCheckTool Implementation
**Date:** 2026-04-27 | **Commit:** `c8ccfe8`, `76e4637`

**What was built in `Sentinel.MCP.Tools`:**

`HealthCheckTool.cs` — full implementation:
- Constructor injects `IHttpClientFactory` (no `new HttpClient()` — socket exhaustion)
- Two named clients registered in `Program.cs`: `HealthCheck.Follow` (default) and `HealthCheck.NoFollow` (`AllowAutoRedirect = false`) — redirect behaviour can't be changed mid-flight on a live client
- `Stopwatch.StartNew()` for latency — monotonic counter, immune to NTP jumps
- `CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)` + `CancelAfter(timeout)` — composes caller's abort signal with tool timeout
- `catch when (!cancellationToken.IsCancellationRequested)` — routes timeout vs clean shutdown
- Specific catches: `HttpRequestError.NameResolutionError`, `HttpRequestError.SecureConnectionError`, generic `HttpRequestException`, broad `Exception` (CA1031 suppressed with `#pragma`)
- `is >= 200 and < 300` — C# 9 relational pattern for `IsHealthy`

**Registered in Program.cs:** `.WithTools<HealthCheckTool>()`

**Key file:** `src/Sentinel.MCP.Tools/HealthCheckTool.cs`

---

### Chore — Rename AgentToolbox → Sentinel.MCP
**Date:** 2026-04-27 | **Commit:** `167e0a0`

- All project names, namespaces, solution file renamed to `Sentinel.MCP.*`
- GitHub repo renamed from `AgentToolbox.DotNet` to `sentinel-mcp` via `gh api`
- `.mcp.json` updated with new server path
- Plan file renamed: `Sentinel_MCP_Project_Plan.md`

---

### Step 4 (Part 1) — InspectSSLCertificate Happy Path
**Date:** 2026-05-04 | **Commit:** `2690aef`

**New contracts added:**

| File | Key fields |
|------|-----------|
| `SSLCertificateRequest.cs` | `Hostname`, `Port` |
| `SSLCertificateResponse.cs` | `Subject`, `Issuer`, `ValidFrom`, `ExpiresOn`, `DaysUntilExpiry`, `IsExpired`, `IsExpiringSoon`, `TlsVersion`, `CertificateChainValid`, `SubjectAlternativeNames`, `Thumbprint` |

**InspectSSLCertificateTool.cs — happy path walkthrough:**

```
TcpClient.ConnectAsync()              → raw TCP socket to hostname:port
SslStream(tcpClient.GetStream(), ...) → wraps TCP in TLS layer
  validation callback → chain.Build() → captures chainValid (outside scope), always returns true
AuthenticateAsClientAsync()           → triggers TLS handshake + fires callback
  TargetHost = hostname               → SNI header (server picks correct cert)
  SslProtocols.None                   → OS picks best (TLS 1.3 > 1.2)
  X509RevocationMode.NoCheck          → skip CRL/OCSP (slow, unnecessary)
sslStream.RemoteCertificate           → base X509Certificate after handshake
X509CertificateLoader.LoadCertificate → rich X509Certificate2 (SYSLIB0057 fix)
cert.NotAfter.ToUniversalTime()       → UTC-safe expiry comparison
daysUntilExpiry = (int)(expiry - now).TotalDays
isExpired = expiry < now              → separate from days (handles sub-day case)
isExpiringSoon = !expired && days <= 30
cert.Extensions.OfType<X509SubjectAlternativeNameExtension>()
  .EnumerateDnsNames()                → SAN DNS names (.NET 7+ API)
sslStream.SslProtocol.ToString()      → actual negotiated TLS version
cert.Thumbprint                       → SHA-1 of DER bytes (unique cert ID)
```

**Analyser fixes:**
- `CA1822` → method marked `static` (no instance data)
- `CA5359` → suppressed with `#pragma` (intentional: inspection tool, not browser)
- `SYSLIB0057` → `new X509Certificate2(bytes)` replaced with `X509CertificateLoader.LoadCertificate(bytes)`
- `CA1002` → `List<string>` → `IReadOnlyList<string>` on `SubjectAlternativeNames`

---

### Step 4 (Part 2) — InspectSSLCertificate Error Paths
**Date:** 2026-05-04 | **Commit:** `b28c1b2`

**Error paths added:**

| Exception | Condition | ErrorCode |
|-----------|-----------|-----------|
| `SocketException` | `HostNotFound` or `NoData` | `ConnectionFailed` — DNS failure |
| `SocketException` | any other | `ConnectionFailed` — TCP refused/unreachable |
| `AuthenticationException` | any | `SslError` — TLS handshake failed |
| `OperationCanceledException` | `!cancellationToken.IsCancellationRequested` | `Timeout` |
| `Exception` | catch-all | `Unknown` — CA1031 suppressed |

**Key learning:** `AuthenticationException` fires even when `return true` in the callback — the OS or .NET runtime can still abort before our callback runs (e.g. protocol mismatch).

**Registered in Program.cs:** `.WithTools<InspectSSLCertificateTool>()` chained after `HealthCheckTool`

---

## Step 5 — LLM Integration (IN PROGRESS)
**Date:** 2026-05-06 | Commits: `aedb0f3` (step-5.1)

**Goal:** Wire Claude into the client so natural language → tool call → synthesised answer.

### Step 5.1 — Packages + Configuration + MCP Client ✅
**Commit:** `aedb0f3`

**Packages added to `Sentinel.MCP.Client`:**
- `ModelContextProtocol` 1.2.0
- `Microsoft.Extensions.AI` 10.5.1
- `Anthropic.SDK` 5.10.0

**User-secrets initialised.** API key stored as `Anthropic:ApiKey`.

**`Program.cs` blocks done:**
- Block 1: `Host.CreateApplicationBuilder` + reads `Anthropic:ApiKey` from user-secrets (`?? throw` for fail-fast)
- Block 2: `McpClientFactory.CreateAsync` with `StdioClientTransport` — spawns server as child process, calls `GetAIFunctionsAsync()` to get tools as MEA `AIFunction` objects

**What still needs to be built:**
- Block 3: `AnthropicClient` → `IChatClient` with `.UseFunctionInvocation()` middleware
- Block 4: Chat loop — read input → send to Claude with tools → print answer

---

## Pending Steps

| # | Step | Status |
|---|------|--------|
| 5 | LLM Integration (Claude client + MCP tool loop) | 🔄 In progress |
| 6 | Typed request/response contracts & validation | 🔲 |
| 7 | AnalyseResponsePattern tool | 🔲 |
| 8 | DiagnoseEndpoint orchestration tool | 🔲 |
| 9 | Rate limiting & tool governance | 🔲 |
| 10 | Telemetry & execution tracing | 🔲 |
| 11 | Tests | 🔲 |
| 12 | README, architecture diagram & demo | 🔲 |

---

## Key Commands

```powershell
# Build solution
dotnet build

# Run MCP server directly
dotnet run --project src/Sentinel.MCP.Server

# Run client (Step 5+)
dotnet run --project src/Sentinel.MCP.Client

# Set API key
dotnet user-secrets set "Anthropic:ApiKey" "<key>" --project src/Sentinel.MCP.Client

# Run tests
dotnet test
```

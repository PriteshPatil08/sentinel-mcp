# PROGRESS — Sentinel.MCP

Pickup guide. Every step documented with enough detail to resume cold.

---

## Environment

- **Platform:** Windows 11, .NET 9 (`net9.0`)
- **Solution:** `Sentinel.MCP.slnx` (new-format .slnx, auto-discovers projects)
- **Repo:** `sentinel-mcp` on GitHub (renamed from `AgentToolbox.DotNet`)
- **Local folder:** `C:\Users\P124488\Desktop\DOTNET\sentinel-mcp`
- **MCP server registered in Claude:** via `.mcp.json` at repo root
- **API key:** stored in user-secrets under `Sentinel.MCP.Client` project, key name `Anthropic:ApiKey`

---

## Project Layout

```
src/
  Sentinel.MCP.Contracts/     — DTOs, IToolResult, ToolError (no deps — everything points here)
  Sentinel.MCP.Tools/         — HealthCheckTool, InspectSSLCertificateTool (depends on Contracts)
  Sentinel.MCP.Server/        — MCP stdio server host (depends on Tools)
  Sentinel.MCP.Client/        — LLM agent client — chat loop wired to Claude (Step 5 complete)
tests/
  Sentinel.MCP.Tools.Tests/           — unit tests (stub)
  Sentinel.MCP.Integration.Tests/     — integration tests (stub)
docs/
  architecture/
  demo/
```

---

## Steps Completed

---

### Step 1 — Solution Structure & Repository Setup
**Date:** 2026-04-25 | **Commits:** `52aea87`, `cd3572f`

**What was built:**
- `dotnet new sln`, projects created under `src/` and `tests/` following the layout above
- `Directory.Build.props` — solution-wide MSBuild settings inherited by every `.csproj`:
  - `TreatWarningsAsErrors=true` — compiler is a quality gate, not a suggestion box
  - `AnalysisMode=All` — all Roslyn analysers enabled
  - `Nullable=enable` — nullability enforced across the solution
  - `ImplicitUsings=enable` — common namespaces implicit
  - `NoWarn=CA1303` — localisation warnings suppressed (no i18n needed)
- `.editorconfig` — formatting contract (indentation, line endings, charset)
- `.gitignore` — via `dotnet new gitignore`
- `LEARNINGS.md` created as the conceptual learning log

**Key decisions:**
- `TreatWarningsAsErrors` on from day one — prevents warning debt accumulation
- `AnalysisMode=All` surfaces real design issues (CA1054 for URL types, CA2007 for ConfigureAwait, etc.)
- All projects inherit from `Directory.Build.props` — single source of truth for build policy

**Files created:** `Directory.Build.props`, `.editorconfig`, `LEARNINGS.md`

---

### Step 2 — Minimal MCP Server (Empty Shell)
**Date:** 2026-04-25 | **Commits:** `fe9032f`, `1255187`

**What was built:**
- `Sentinel.MCP.Server` configured as a console app using `Microsoft.NET.Sdk` (not `Microsoft.NET.Sdk.Web` — no Kestrel, no HTTP listener needed for stdio transport)
- `ModelContextProtocol` NuGet 1.2.0 added
- `Program.cs` wire-up:
  ```
  Host.CreateApplicationBuilder
    → .AddMcpServer()
    → .WithStdioServerTransport()
    → builder.Build().RunAsync()
  ```
- Logging redirected to stderr via `LogToStandardErrorThreshold = LogLevel.Trace`
- Server identity set via `McpServerOptions` bound from `appsettings.json`:
  ```json
  { "McpServer": { "Name": "Sentinel.MCP", "Version": "1.0.0" } }
  ```

**Key decisions:**
- `Host.CreateApplicationBuilder` not `WebApplication.CreateBuilder` — generic host gives DI + config + lifetime without pulling in ASP.NET
- **stdout is sacred** — all console logging redirected to stderr because stdout belongs exclusively to the MCP JSON-RPC protocol stream. Any `Console.WriteLine` on stdout corrupts the protocol and breaks the client handshake.

**Key files:** `src/Sentinel.MCP.Server/Program.cs`, `src/Sentinel.MCP.Server/appsettings.json`

---

### Chore — .NET 10 Migration (later reverted to net9.0)
**Date:** 2026-04-25 | **Commit:** `70decf9`

**What changed:**
- `Directory.Build.props`: `net9.0` → `net10.0`
- All individual `.csproj` files: removed redundant `<TargetFramework>` (inherited from props — commit `7f19006`)
- `NETSDK1057` info message appeared on every build — expected for preview SDK, not an error

---

### Step 3 (Part 1) — Tool Contracts Layer
**Date:** 2026-04-26 | **Commits:** `2046951`, `dbc1cb5`

**What was built in `Sentinel.MCP.Contracts`:**

| File | Purpose |
|------|---------|
| `IToolResult.cs` | Covariant `IToolResult<out T>` — `Success`, `Data`, `Error`, `ExecutedAtUtc`, `DurationMs` |
| `ToolResult.cs` | `ToolResult<T>` sealed class + non-generic `ToolResult` companion with `Ok<T>` / `Fail<T>` factory methods |
| `ToolErrorCode.cs` | Enum: `ValidationFailed`, `Timeout`, `ConnectionFailed`, `SslError`, `RateLimited`, `Unknown` |
| `ToolError.cs` | Structured error: `ErrorCode`, `Message`, `FieldErrors (Dictionary<string, string[]>?)` |
| `HealthCheckRequest.cs` | `Uri? Url`, `int TimeoutSeconds = 10`, `bool FollowRedirects = true` |
| `HealthCheckResponse.cs` | `StatusCode`, `StatusDescription`, `LatencyMs`, `ContentType`, `ResponseHeaders`, `IsHealthy`, `ServerHeader` |

**Key decisions:**
- `out T` covariance on `IToolResult<out T>` — `ToolResult<HealthCheckResponse>` satisfies `IToolResult<object>` without a cast
- Non-generic `ToolResult` companion class — enables type inference: callers write `ToolResult.Ok(data, ms)` not `ToolResult.Ok<HealthCheckResponse>(data, ms)`
- `init` properties + `sealed` — immutable-after-construction data carriers, no subclassing
- `IReadOnlyList<string>` not `List<string>` for collections (CA1002 compliance)
- `DateTime.UtcNow` on every result — telemetry-ready from day one

---

### Step 3 (Part 2) — HealthCheckTool Implementation
**Date:** 2026-04-27 | **Commits:** `c8ccfe8`, `76e4637`

**What was built in `Sentinel.MCP.Tools`:**

`HealthCheckTool.cs` — full implementation:

| Concern | Implementation |
|---------|---------------|
| HTTP client | `IHttpClientFactory` injected (no `new HttpClient()` — socket exhaustion risk) |
| Redirect behaviour | Two named clients: `HealthCheck.Follow` (default) and `HealthCheck.NoFollow` (`AllowAutoRedirect = false`) — can't change mid-flight on a live client |
| Latency | `Stopwatch.StartNew()` — monotonic counter, immune to NTP adjustments |
| Timeout | `CancellationTokenSource.CreateLinkedTokenSource` + `CancelAfter(timeout)` — composes caller's abort signal with tool timeout |
| Timeout detection | `catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)` — routes timeout vs clean shutdown |
| Error mapping | `HttpRequestError.NameResolutionError` → `ConnectionFailed`, `SecureConnectionError` → `SslError`, generic → `ConnectionFailed`, broad `Exception` → `Unknown` (CA1031 suppressed) |
| Health signal | `is >= 200 and < 300` — C# 9 relational pattern for `IsHealthy` |

**MCP description (what the LLM reads):**
> "Performs an HTTP health check against a given URL. Returns status code, latency, response headers, and a health assessment. Use this when asked about whether an API or website is up, slow, or responding correctly."

**Registered in Server Program.cs:** `.WithTools<HealthCheckTool>()`

---

### Chore — Rename AgentToolbox → Sentinel.MCP
**Date:** 2026-04-27 | **Commit:** `167e0a0`

- All project names, namespaces, solution file renamed to `Sentinel.MCP.*`
- GitHub repo renamed from `AgentToolbox.DotNet` to `sentinel-mcp` via `gh api`
- `.mcp.json` updated with new server path
- Plan file renamed: `AgentToolbox_DotNet_Project_Plan.md` → `Sentinel_MCP_Project_Plan.md` (commit `481659c`)

---

### Step 4 (Part 1) — InspectSSLCertificate Happy Path
**Date:** 2026-05-04 | **Commit:** `2690aef`

**New contracts added to `Sentinel.MCP.Contracts`:**

| File | Key fields |
|------|-----------|
| `SSLCertificateRequest.cs` | `string Hostname`, `int Port = 443` |
| `SSLCertificateResponse.cs` | `Subject`, `Issuer`, `ValidFrom`, `ExpiresOn`, `DaysUntilExpiry`, `IsExpired`, `IsExpiringSoon` (<30 days), `TlsVersion`, `CertificateChainValid`, `SubjectAlternativeNames`, `Thumbprint` |

**InspectSSLCertificateTool.cs — happy path walkthrough:**

```
TcpClient.ConnectAsync(hostname, port)         → raw TCP socket
SslStream(tcpClient.GetStream(), ...)          → wraps TCP in TLS layer
  validation callback                          → chain.Build() → captures chainValid, always returns true (CA5359 suppressed)
AuthenticateAsClientAsync(options)             → triggers TLS handshake
  TargetHost = hostname                        → SNI header (server picks correct cert for hostname)
  SslProtocols.None                            → OS picks best available (TLS 1.3 preferred)
  X509RevocationMode.NoCheck                   → skip CRL/OCSP (slow, unnecessary for inspection)
sslStream.RemoteCertificate                    → base X509Certificate after handshake
X509CertificateLoader.LoadCertificate(bytes)   → rich X509Certificate2 (SYSLIB0057-safe API)
cert.NotAfter.ToUniversalTime()                → UTC-safe expiry
daysUntilExpiry = (int)(expiry - now).TotalDays
isExpiringSoon = !isExpired && days <= 30
X509SubjectAlternativeNameExtension
  .EnumerateDnsNames()                         → SAN DNS names (.NET 7+ API)
sslStream.SslProtocol.ToString()               → negotiated TLS version string
cert.Thumbprint                                → SHA-1 fingerprint (unique cert identity)
```

**Analyser suppressions:**
- `CA5359` — intentional: inspection tool accepts all certs (reading, not trusting)
- `SYSLIB0057` — `new X509Certificate2(bytes)` replaced with `X509CertificateLoader.LoadCertificate`

---

### Step 4 (Part 2) — InspectSSLCertificate Error Paths
**Date:** 2026-05-04 | **Commit:** `b28c1b2`

**Error handling pattern:** entire method body wrapped in `try/catch`. Each failure maps to a typed `ToolResult.Fail(...)` — no exceptions escape to the MCP host.

| Exception | `when` condition | `ErrorCode` | Meaning |
|-----------|-----------------|-------------|---------|
| `SocketException` | `SocketErrorCode is HostNotFound or NoData` | `ConnectionFailed` | DNS resolution failed |
| `SocketException` | _(none — catches all others)_ | `ConnectionFailed` | TCP refused, network unreachable, etc. |
| `AuthenticationException` | _(none)_ | `SslError` | TLS handshake failed (self-signed, protocol mismatch, OS rejection) |
| `OperationCanceledException` | `!cancellationToken.IsCancellationRequested` | `Timeout` | Timeout fired; caller shutdown propagates naturally |
| `Exception` | _(none — catch-all)_ | `Unknown` | Unexpected failure; CA1031 suppressed with `#pragma` |

**Key insight:** `AuthenticationException` fires even when the validation callback returns `true` — the OS or .NET runtime can abort the handshake before the callback runs (e.g. protocol version disabled at OS level).

**Registered in Server Program.cs:** `.WithTools<InspectSSLCertificateTool>()` chained after `HealthCheckTool`

---

### Step 5 (Part 1) — Client Packages + Configuration + MCP Connection
**Date:** 2026-05-06 | **Commit:** `aedb0f3`

**Packages added to `Sentinel.MCP.Client`:**

| Package | Version | Purpose |
|---------|---------|---------|
| `ModelContextProtocol` | 1.2.0 | MCP client — stdio transport, `tools/list`, tool invocation |
| `Microsoft.Extensions.AI` | 10.5.1 | `IChatClient` abstraction + `UseFunctionInvocation` middleware |
| `Microsoft.Extensions.Hosting` | 10.0.7 | Generic host: DI, config, user-secrets loading |
| `Microsoft.Extensions.Configuration.UserSecrets` | 10.0.7 | Explicit user-secrets registration |

**User-secrets initialised:** `UserSecretsId = 9b7c82e5-40d8-4f4b-8508-b19b8e3083de`
**API key stored as:** `Anthropic:ApiKey` in user-secrets store

**`Program.cs` — Block 1: Host + API key:**
```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddUserSecrets<Program>(optional: true);
var apiKey = builder.Configuration["Anthropic:ApiKey"]
    ?? throw new InvalidOperationException("Anthropic:ApiKey not set in user-secrets.");
```

**`Program.cs` — Block 2: MCP client + tool discovery:**
```csharp
await using var mcpClient = await McpClient.CreateAsync(
    new StdioClientTransport(new StdioClientTransportOptions
    {
        Command = "dotnet",
        Arguments = ["run", "--project", "src/Sentinel.MCP.Server", "--no-launch-profile"],
        WorkingDirectory = Directory.GetCurrentDirectory(),
        Name = "Sentinel"
    }));

var tools = await mcpClient.ListToolsAsync();
```

The client **spawns the MCP server as a child process** via stdio. `ListToolsAsync()` calls `tools/list` over the JSON-RPC channel and returns `McpClientTool` objects — which implement `AITool` from `Microsoft.Extensions.AI`.

---

### Step 5 (Part 2) — Anthropic IChatClient + Chat Loop
**Date:** 2026-05-11 | **Commit:** `ea1898b`

**Package swap:** `Anthropic.SDK` 5.10.0 (community) → `Anthropic` 12.20.0 (official Anthropic SDK) to resolve `Microsoft.Extensions.AI.Abstractions` version conflict with `ModelContextProtocol` 1.2.0.

**`Program.cs` — Block 3: Anthropic client wired to IChatClient:**
```csharp
Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", apiKey);

AnthropicClient anthropicClient = new();

using IChatClient chatClient = anthropicClient
    .AsIChatClient("claude-sonnet-4-5-20250929")
    .AsBuilder()
    .UseFunctionInvocation()
    .Build();
```

- `Environment.SetEnvironmentVariable` — official Anthropic SDK reads the key from the environment
- `AsIChatClient(model)` — adapter that wraps `AnthropicClient` in the `IChatClient` abstraction
- `UseFunctionInvocation()` — MEai middleware that intercepts `tool_use` responses from Claude, routes them back to the MCP server, appends the result, and re-calls the LLM — **the tool loop is handled automatically**
- `Build()` — materialises the middleware pipeline

**`Program.cs` — Block 4: Multi-turn chat loop:**
```csharp
var history = new List<ChatMessage>();

Console.WriteLine("Sentinel is ready. Ask anything (or type 'exit' to quit).");

while (true)
{
    Console.Write("> ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input)) continue;
    if (input.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

    history.Add(new ChatMessage(ChatRole.User, input));

    var response = await chatClient.GetResponseAsync(
        history,
        new ChatOptions
        {
            MaxOutputTokens = 4096,
            Tools = [.. tools]
        });

    history.AddRange(response.Messages);

    Console.WriteLine();
    Console.WriteLine(response.Text ?? "(no response)");
    Console.WriteLine();
}
```

- `history` — `List<ChatMessage>` accumulates the full conversation. Every turn appends user input and assistant response, giving Claude memory across questions.
- `Tools = [.. tools]` — spreads the `McpClientTool` list into `ChatOptions`. Claude sees the tool schemas and decides when to call them.
- `response.Messages` — may contain multiple messages (tool call + tool result + final answer) when the LLM invoked a tool. All are appended to history.
- `UseFunctionInvocation()` middleware means the tool call/response cycle is invisible to our loop — `GetResponseAsync` returns only after all tool calls are resolved and Claude has produced its final text.

**Full data flow:**
```
User types question
  → ChatMessage(User, input) added to history
  → GetResponseAsync(history, tools) called
    → Claude receives question + tool schemas
    → Claude emits tool_use for e.g. HealthCheck
    → UseFunctionInvocation middleware intercepts
    → McpClient.CallToolAsync("HealthCheck", args)
    → MCP server executes HealthCheckTool
    → Result returned to middleware
    → Middleware appends tool result, re-calls Claude
    → Claude synthesises final answer
  → response.Text printed to console
  → All messages appended to history
User types next question (has full context)
```

---

## Pending Steps

| # | Step | Status |
|---|------|--------|
| 6 | Typed request/response contracts & validation (FluentValidation pipeline) | 🔲 Not started |
| 7 | AnalyseResponsePattern tool (in-memory history + trend detection) | 🔲 Not started |
| 8 | DiagnoseEndpoint orchestration tool (parallel sub-tool execution) | 🔲 Not started |
| 9 | Rate limiting & tool governance (`System.Threading.RateLimiting`) | 🔲 Not started |
| 10 | Telemetry & execution tracing (self-introspection tool) | 🔲 Not started |
| 11 | Unit + integration tests | 🔲 Not started |
| 12 | README, architecture diagram, ADRs, demo recording | 🔲 Not started |

---

## Key Commands

```powershell
# Build solution
dotnet build

# Run MCP server directly (for testing with MCP Inspector)
dotnet run --project src/Sentinel.MCP.Server

# Run client (full agent loop)
dotnet run --project src/Sentinel.MCP.Client

# Set API key in user-secrets
dotnet user-secrets set "Anthropic:ApiKey" "<sk-ant-...>" --project src/Sentinel.MCP.Client

# Run tests
dotnet test
```

---

## Package Reference (current)

| Project | Package | Version |
|---------|---------|---------|
| Server | `ModelContextProtocol` | 1.2.0 |
| Server | `Microsoft.Extensions.Hosting` | 10.0.7 |
| Tools | `ModelContextProtocol` | 1.2.0 |
| Tools | `Microsoft.Extensions.Http` | 10.0.7 |
| Client | `Anthropic` | 12.20.0 |
| Client | `Microsoft.Extensions.AI` | 10.5.1 |
| Client | `Microsoft.Extensions.Hosting` | 10.0.7 |
| Client | `Microsoft.Extensions.Configuration.UserSecrets` | 10.0.7 |
| Client | `ModelContextProtocol` | 1.2.0 |

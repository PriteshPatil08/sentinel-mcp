# AgentToolbox.DotNet — Detailed Project Plan

## AI-Powered API Health Monitor & Diagnostics Platform

A .NET MCP platform that gives AI agents real diagnostic tools for API health monitoring — with rate limiting, structured telemetry, and typed contracts — demonstrating how to build safe, observable, production-grade AI tool execution.

---

## Phase 1: Foundation (Steps 1–3)

### Step 1 — Solution Structure & Repository Setup

**Goal:** A clean, navigable repo that signals architectural maturity before a single line of logic is written.

**Tasks:**

- Create a new .NET solution `AgentToolbox.sln`
- Set up the following project structure:
  ```
  /src
    /AgentToolbox.McpServer        → ASP.NET Core host for the MCP server
    /AgentToolbox.Tools            → Class library: all tool implementations
    /AgentToolbox.Tools.Contracts  → Class library: shared DTOs, interfaces, enums
    /AgentToolbox.Client           → Console app: MCP client + LLM orchestration
  /tests
    /AgentToolbox.Tools.Tests      → Unit tests for tools
    /AgentToolbox.Integration.Tests → End-to-end client→server→tool tests
  /docs
    /architecture                  → Architecture diagrams, ADRs
    /demo                          → Demo scripts, GIFs, walkthrough notes
  ```
- Initialise Git repo with a proper `.gitignore` for .NET
- Add a skeleton `README.md` with project title, one-liner, and "under construction" badge
- Add an `EditorConfig` and `Directory.Build.props` for consistent formatting across all projects
- Set target framework to `net8.0` (or `net9.0` if stable at time of build)

**Commit:** `feat: initialise solution structure with src, tests, and docs layout`

**Why this matters:** Hiring managers open repo root first. If they see `/src`, `/tests`, `/docs` with clean separation, they've already decided you think in systems before reading a line of code.

---

### Step 2 — Minimal MCP Server (Empty Shell)

**Goal:** A running MCP server that starts, registers with the protocol, and exposes zero tools — proving you have the plumbing right before adding business logic.

**Tasks:**

- Install the official `ModelContextProtocol` NuGet package (v1.0+) into `AgentToolbox.McpServer`
- Configure the MCP server host using `Microsoft.Extensions.Hosting`
- Set up the server to use **stdio transport** initially (simplest to test locally, compatible with Claude Desktop and VS Code)
- Implement server metadata: name (`AgentToolbox`), version, capabilities declaration
- Add basic `appsettings.json` with structured configuration sections
- Verify the server starts, responds to MCP `initialize` handshake, and returns an empty tool list
- Test manually by connecting from Claude Desktop or the MCP Inspector tool

**Commit:** `feat: add minimal MCP server with stdio transport and protocol handshake`

**Technical notes:**
- Use `IMcpServerBuilder` from the C# SDK for registration
- Keep the `Program.cs` minimal — configuration, host build, run
- Do NOT add any tools yet; the point is to validate the transport layer in isolation

---

### Step 3 — First Real Tool: `HealthCheck`

**Goal:** A tool that makes a real HTTP call to a real URL and returns real data. No mocks. No hardcoded JSON. This is where the project stops being a tutorial and starts being useful.

**Tasks:**

- Create the `IToolResult<T>` interface in `AgentToolbox.Tools.Contracts`:
  ```
  - Success (bool)
  - Data (T)
  - Error (ToolError?)
  - ExecutedAtUtc (DateTime)
  - DurationMs (long)
  ```
- Create `HealthCheckRequest` DTO:
  ```
  - Url (string, required)
  - TimeoutSeconds (int, default 10)
  - FollowRedirects (bool, default true)
  ```
- Create `HealthCheckResponse` DTO:
  ```
  - StatusCode (int)
  - StatusDescription (string)
  - LatencyMs (long)
  - ContentType (string?)
  - ResponseHeaders (Dictionary<string, string>)
  - IsHealthy (bool) — derived: 2xx = healthy
  - ServerHeader (string?) — extracted from response
  ```
- Implement `HealthCheckTool` in `AgentToolbox.Tools`:
  - Uses `HttpClient` (injected via DI, not `new`'d up)
  - Handles timeouts gracefully (returns structured error, does not throw)
  - Handles DNS resolution failure, connection refused, SSL errors — each as a categorised `ToolError`
  - Measures latency with `Stopwatch`, not `DateTime` subtraction
- Register `HealthCheckTool` with the MCP server using `[McpServerToolType]` or explicit registration
- Write the MCP tool description carefully — this is what the LLM reads to decide when to use it:
  ```
  "Performs an HTTP health check against a given URL. Returns status code, 
  latency, response headers, and a health assessment. Use this when asked 
  about whether an API or website is up, slow, or responding correctly."
  ```
- Test by connecting to the MCP server and asking it to check `https://api.github.com`

**Commit:** `feat: implement HealthCheck tool with real HTTP execution and structured response`

**Why real I/O matters:** In a demo, you can point this at any public URL. The interviewer can say "try our staging API" and it works. That moment is worth more than a slide deck.

---

## Phase 2: Depth & Differentiation (Steps 4–6)

### Step 4 — Second Real Tool: `InspectSSLCertificate`

**Goal:** A tool that checks TLS certificate details for any domain. Every ops engineer has been burned by an expired cert at 2am. This tool resonates instantly.

**Tasks:**

- Create `SSLCertificateRequest` DTO:
  ```
  - Hostname (string, required)
  - Port (int, default 443)
  ```
- Create `SSLCertificateResponse` DTO:
  ```
  - Subject (string)
  - Issuer (string)
  - ValidFrom (DateTime)
  - ExpiresOn (DateTime)
  - DaysUntilExpiry (int)
  - IsExpired (bool)
  - IsExpiringSoon (bool) — <30 days
  - TlsVersion (string) — e.g., "TLS 1.3"
  - CertificateChainValid (bool)
  - SubjectAlternativeNames (List<string>)
  - Thumbprint (string)
  ```
- Implement `InspectSSLCertificateTool`:
  - Use `SslStream` + `TcpClient` to connect and retrieve the server certificate
  - Extract all fields from `X509Certificate2`
  - Handle connection failures, self-signed certs, and expired certs as structured errors (not exceptions)
  - Calculate `DaysUntilExpiry` and flag `IsExpiringSoon` automatically
- Register with MCP server
- MCP description:
  ```
  "Inspects the SSL/TLS certificate of a given hostname. Returns certificate 
  details including issuer, expiry date, days until expiration, TLS version, 
  and chain validity. Use when asked about certificate health, security posture, 
  or upcoming certificate expirations."
  ```

**Commit:** `feat: implement SSLCertificate inspection tool with expiry detection`

**Demo value:** "Your cert expires in 12 days" is the kind of output that makes non-technical stakeholders sit up. It's concrete, actionable, and clearly valuable.

---

### Step 5 — LLM Integration (The Missing Piece)

**Goal:** Connect the MCP client to an actual LLM so the system can receive a natural language question, decide which tool to call, execute it, and return a synthesised answer. Without this step, you have a tool library, not an agent.

**Tasks:**

- Add the `Microsoft.Extensions.AI` abstractions package to `AgentToolbox.Client`
- Add an LLM provider package (choose one):
  - `Microsoft.Extensions.AI.Anthropic` (for Claude), OR
  - `Microsoft.Extensions.AI.OpenAI` (for GPT-4o/4.1)
  - Recommendation: support both via configuration, default to one
- Configure the client to:
  1. Connect to your local MCP server (stdio transport)
  2. Discover available tools via MCP `tools/list`
  3. Convert MCP tool definitions into the LLM's function/tool calling format
  4. Send the user's natural language query to the LLM with available tools
  5. When the LLM requests a tool call → route it to the MCP server
  6. Return the tool result to the LLM for final synthesis
  7. Print the LLM's final response to the user
- Handle the full conversation loop:
  ```
  User: "Is api.github.com healthy?"
  → LLM decides to call HealthCheck(url: "https://api.github.com")
  → MCP server executes tool
  → Tool returns: { statusCode: 200, latencyMs: 142, isHealthy: true, ... }
  → LLM synthesises: "api.github.com is healthy — responding in 142ms with a 200 status."
  ```
- Support multi-turn: the LLM may call multiple tools in sequence
- Store API keys in user secrets (`dotnet user-secrets`), never in `appsettings.json`

**Commit:** `feat: add MCP client with LLM integration for natural language tool invocation`

**This is the step that makes the project an agent.** Everything before this is infrastructure. Everything after this is refinement. This step is the heartbeat.

---

### Step 6 — Typed Request/Response Contracts & Validation

**Goal:** Enforce strong schemas at the tool boundary so the LLM cannot send garbage into your business logic. This is where you demonstrate the core thesis: "AI decides, tools execute deterministically."

**Tasks:**

- Add `FluentValidation` (or `System.ComponentModel.DataAnnotations`) to `AgentToolbox.Tools.Contracts`
- Create validators for each request DTO:
  - `HealthCheckRequest`: URL must be valid URI, timeout 1–60 seconds
  - `SSLCertificateRequest`: hostname must not be empty, port 1–65535
- Create a `ToolExecutionPipeline` that wraps every tool call:
  1. Deserialise the MCP tool input into the typed request DTO
  2. Validate the request
  3. If validation fails → return a structured `ToolError` with field-level details (the LLM can use this to self-correct)
  4. If validation passes → execute the tool
  5. Wrap the result in the standard `IToolResult<T>` envelope
- Create `ToolError` model:
  ```
  - ErrorCode (enum: ValidationFailed, Timeout, ConnectionFailed, SSLError, RateLimited, Unknown)
  - Message (string)
  - FieldErrors (Dictionary<string, string[]>?) — for validation failures
  ```
- Ensure all tool responses are serialised as clean JSON with consistent casing (`camelCase`)
- Add XML doc comments on all public DTOs — these generate the JSON schema that the LLM sees

**Commit:** `feat: add typed contracts with validation pipeline and structured error responses`

**Why this matters for interviews:** When asked "how do you prevent the AI from doing something stupid?", you can point to the validation layer and say "the tool rejects invalid input with a structured error, and the LLM can read that error and retry with corrected parameters."

---

## Phase 3: Intelligence & Production Thinking (Steps 7–9)

### Step 7 — Third Tool: `AnalyseResponsePattern`

**Goal:** A tool that stores historical health check results and detects trends — increasing latency, intermittent failures, degradation over time. This gives the system memory and makes it genuinely analytical.

**Tasks:**

- Create an `IHealthCheckStore` interface:
  ```
  - RecordResult(string url, HealthCheckResponse result)
  - GetHistory(string url, int lastN) → List<HealthCheckResponse>
  ```
- Implement `InMemoryHealthCheckStore` (singleton, thread-safe via `ConcurrentDictionary`)
  - Stores last 100 results per URL
  - Timestamped entries
- Create `ResponsePatternRequest` DTO:
  ```
  - Url (string, required)
  - WindowSize (int, default 10) — how many recent checks to analyse
  ```
- Create `ResponsePatternResponse` DTO:
  ```
  - AverageLatencyMs (double)
  - P95LatencyMs (double)
  - LatencyTrend (enum: Stable, Increasing, Decreasing, Volatile)
  - FailureRate (double) — percentage of non-2xx in window
  - ConsecutiveFailures (int)
  - Pattern (enum: Healthy, Degrading, Intermittent, Down)
  - Summary (string) — human-readable one-liner
  - DataPoints (int) — how many results were analysed
  ```
- Implement `AnalyseResponsePatternTool`:
  - Calculates statistics using the stored history
  - Determines trend by comparing first half vs second half of the window
  - Returns `InsufficientData` error if fewer than 3 data points exist
- Wire `HealthCheckTool` to automatically record results into the store after each execution
- MCP description:
  ```
  "Analyses historical health check data for a URL to detect patterns such as 
  increasing latency, intermittent failures, or service degradation. Requires 
  prior health checks to have been run. Use when asked about trends, reliability, 
  or whether a service is getting worse over time."
  ```

**Commit:** `feat: implement response pattern analysis with trend detection and in-memory history`

**Architectural signal:** This tool depends on state from another tool's executions. That's a real system design concern — and you've solved it with a clean abstraction (`IHealthCheckStore`) that could be swapped for Redis, SQL, etc.

---

### Step 8 — Orchestration Tool: `DiagnoseEndpoint`

**Goal:** A meta-tool that runs a full diagnostic suite against an endpoint by composing the other tools. The LLM can call this single tool to get a comprehensive report, or call individual tools for specific questions.

**Tasks:**

- Create `DiagnoseEndpointRequest` DTO:
  ```
  - Url (string, required)
  - IncludeSSLCheck (bool, default true)
  - IncludePatternAnalysis (bool, default true)
  - RunFreshHealthCheck (bool, default true)
  ```
- Create `DiagnoseEndpointResponse` DTO:
  ```
  - Url (string)
  - OverallStatus (enum: Healthy, Warning, Critical, Unknown)
  - HealthCheck (HealthCheckResponse?)
  - SSLCertificate (SSLCertificateResponse?)
  - ResponsePattern (ResponsePatternResponse?)
  - Warnings (List<string>) — aggregated warnings across all checks
  - DiagnosedAtUtc (DateTime)
  - TotalDurationMs (long)
  ```
- Implement `DiagnoseEndpointTool`:
  - Orchestrates calls to `HealthCheckTool`, `InspectSSLCertificateTool`, and `AnalyseResponsePatternTool`
  - Extracts hostname from URL for SSL check
  - Runs checks in parallel where possible (`Task.WhenAll`)
  - Aggregates warnings:
    - SSL expiring soon? → warning
    - Latency trend increasing? → warning
    - Failure rate > 10%? → warning
    - Any tool errors? → captured, not thrown
  - Calculates `OverallStatus` from combined results
  - Individual sub-tool failures don't fail the whole diagnosis — partial results are returned with the failed section marked
- MCP description:
  ```
  "Runs a comprehensive diagnostic against an endpoint: health check, SSL 
  certificate inspection, and historical pattern analysis. Returns a unified 
  report with an overall status assessment. Use when asked to fully diagnose 
  or investigate an endpoint."
  ```

**Commit:** `feat: implement DiagnoseEndpoint orchestration tool with parallel execution`

**Demo power:** One natural language question — "diagnose api.github.com" — triggers four tools, returns a structured report with an overall health verdict. That's the demo moment.

---

### Step 9 — Rate Limiting & Tool Governance

**Goal:** Demonstrate that you understand giving AI unrestricted tool access is a liability. Add configurable rate limiting so tools can't be hammered by an overenthusiastic LLM.

**Tasks:**

- Create `ToolGovernanceOptions` configuration section:
  ```json
  {
    "ToolGovernance": {
      "GlobalRateLimit": {
        "MaxCallsPerMinute": 30
      },
      "ToolLimits": {
        "HealthCheck": { "MaxCallsPerMinute": 10, "MaxConcurrent": 3 },
        "InspectSSLCertificate": { "MaxCallsPerMinute": 5, "MaxConcurrent": 2 },
        "DiagnoseEndpoint": { "MaxCallsPerMinute": 3, "MaxConcurrent": 1 }
      }
    }
  }
  ```
- Implement `IRateLimiter` interface:
  ```
  - TryAcquire(string toolName) → (bool allowed, TimeSpan? retryAfter)
  ```
- Implement `SlidingWindowRateLimiter` using `System.Threading.RateLimiting`
- Integrate rate limiting into the `ToolExecutionPipeline` (from Step 6):
  1. Check rate limit → if exceeded, return `ToolError` with `ErrorCode.RateLimited` and `retryAfter`
  2. The LLM receives this error and can decide to wait or inform the user
- Add concurrency limiting using `SemaphoreSlim` per tool
- Log every rate limit hit (this feeds into telemetry in Step 10)

**Commit:** `feat: add configurable rate limiting and concurrency governance for tools`

**Interview talking point:** "I added rate limiting because in production, an LLM in a loop could hammer an external API. The governance layer means we control the blast radius of AI-driven tool execution."

---

## Phase 4: Observability & Quality (Steps 10–11)

### Step 10 — Telemetry & Execution Tracing

**Goal:** Full observability of every tool invocation — what was called, with what inputs, what it returned, how long it took, and whether it succeeded. This is your "results" section.

**Tasks:**

- Create `ToolExecutionRecord` model:
  ```
  - TraceId (string — GUID)
  - ToolName (string)
  - RequestPayload (object — serialised input)
  - ResponsePayload (object — serialised output)
  - Success (bool)
  - ErrorCode (ToolErrorCode?)
  - DurationMs (long)
  - ExecutedAtUtc (DateTime)
  - RateLimited (bool)
  - CalledByTool (string?) — if invoked by DiagnoseEndpoint
  ```
- Create `IExecutionTracer` interface:
  ```
  - RecordExecution(ToolExecutionRecord record)
  - GetRecentExecutions(int lastN) → List<ToolExecutionRecord>
  - GetExecutionsByTool(string toolName, int lastN) → List<ToolExecutionRecord>
  ```
- Implement `InMemoryExecutionTracer` (circular buffer, configurable max size)
- Integrate into `ToolExecutionPipeline` — every tool call is automatically traced
- Add structured logging using `ILogger<T>` with:
  - Log level `Information` for successful executions
  - Log level `Warning` for rate-limited calls
  - Log level `Error` for tool failures
  - Structured log properties: `{ToolName}`, `{DurationMs}`, `{TraceId}`
- **Bonus: Expose tracing as an MCP tool itself** — `GetExecutionHistory`:
  ```
  "Returns a log of recent tool executions including timing, success/failure, 
  and error details. Use when asked about what tools have been called, how 
  the system has been performing, or to review diagnostic history."
  ```
  This means the AI can introspect on its own tool usage.

**Commit:** `feat: add execution tracing, structured logging, and self-introspection tool`

**Why self-introspection matters:** The AI can answer "what have you checked so far?" by calling `GetExecutionHistory`. That's meta-cognition at the tool level, and it's genuinely interesting to discuss in an interview.

---

### Step 11 — Tests

**Goal:** Prove that your tools are deterministic and your contracts are enforced. Not 100% coverage for the sake of it — targeted tests that prove the system's promises.

**Tasks:**

**Unit tests (`AgentToolbox.Tools.Tests`):**

- `HealthCheckTool`:
  - Returns structured error on invalid URL (not an exception)
  - Returns structured error on timeout
  - Returns correct `IsHealthy` for 2xx vs 4xx vs 5xx
  - Measures latency (mock `HttpClient` with configurable delay)
- `InspectSSLCertificateTool`:
  - Returns correct `DaysUntilExpiry` calculation
  - Flags `IsExpiringSoon` when <30 days
  - Handles connection refused gracefully
- `AnalyseResponsePatternTool`:
  - Returns `InsufficientData` error with <3 data points
  - Correctly identifies `Increasing` latency trend
  - Correctly calculates failure rate
  - Returns `Stable` for consistent results
- `DiagnoseEndpointTool`:
  - Returns partial results when one sub-tool fails
  - Calculates correct `OverallStatus` from combined results
  - Runs sub-tools in parallel (verify with timing)
- Validation:
  - Rejects empty URL
  - Rejects timeout outside 1–60 range
  - Returns field-level error details
- Rate limiter:
  - Allows calls within limit
  - Rejects calls over limit with correct `retryAfter`

**Integration tests (`AgentToolbox.Integration.Tests`):**

- Start MCP server in-process
- Connect MCP client
- Discover tools via `tools/list` — verify all 5 tools are registered
- Call `HealthCheck` tool via MCP protocol — verify structured response
- Call `HealthCheck` with invalid input via MCP — verify structured error
- Call `DiagnoseEndpoint` via MCP — verify orchestrated response
- Verify execution tracer records all calls from the integration test

**Commit:** `feat: add unit and integration tests for tools, validation, and MCP protocol flow`

**Testing philosophy:** Test the contracts (does invalid input produce the right error?), test the logic (does trend detection work?), and test the integration (does the full MCP loop work?). Skip testing things the framework already guarantees.

---

## Phase 5: Ship It (Step 12)

### Step 12 — README, Architecture Diagram & Demo

**Goal:** Make the entire project understandable in 60 seconds and impressive in 120.

**Tasks:**

**README.md — write in this exact order:**

1. **Title + one-liner:**
   ```
   AgentToolbox.DotNet
   A .NET MCP platform that gives AI agents real diagnostic tools for 
   API health monitoring — with governance, telemetry, and typed contracts.
   ```
2. **Problem statement (3 sentences max):**
   - AI systems generate answers but can't safely execute real-world checks
   - Ad-hoc integrations create chaos
   - Enterprises need AI tool execution that is auditable and deterministic
3. **What this project demonstrates (bullet list):**
   - Tool-based AI execution via MCP
   - Real I/O: actual HTTP calls, SSL inspection, pattern analysis
   - Typed contracts with validation
   - Rate limiting and governance
   - Full execution tracing and observability
   - Orchestration: AI composes tools for complex diagnostics
4. **Architecture diagram** (Mermaid or image):
   ```
   User Question → LLM → MCP Client → MCP Server → Tool Execution Pipeline
                                                          ↓
                                                   [Validation → Rate Limit → Tool → Trace]
                                                          ↓
                                                   Structured Result → LLM → Answer
   ```
5. **Tools reference** — table with: tool name, description, input, output
6. **Demo walkthrough** — exact steps to run locally:
   - Clone repo
   - Set API key in user secrets
   - `dotnet run` the server
   - `dotnet run` the client
   - Type: "Diagnose https://api.github.com"
   - Show what happens step by step
7. **Design decisions** — short section explaining:
   - Why rate limiting on AI tool access
   - Why typed contracts at the tool boundary
   - Why execution tracing is a tool itself
8. **Roadmap** — future ideas:
   - Streamable HTTP transport (remote deployment)
   - Persistent history (SQLite/Redis)
   - Authentication & OAuth
   - Multi-agent support
   - Dashboard UI for execution traces
   - Webhook-based continuous monitoring

**Demo recording:**

- Record a 60–90 second terminal GIF or video
- Show the full loop: question → tool selection → execution → result
- Include at least one error case (invalid URL or rate limit hit) to show graceful handling
- Use `asciinema` or screen recording, not a slideshow

**Architecture Decision Records (ADRs) in `/docs/architecture/`:**

- `001-why-mcp.md` — why MCP over custom tool calling
- `002-real-io-over-mocks.md` — why tools do real work
- `003-rate-limiting.md` — why governance matters for AI tool access
- `004-typed-contracts.md` — why strong schemas at the tool boundary

**Commit:** `docs: add README, architecture diagram, demo walkthrough, and ADRs`

---

## Appendix: Commit Log (Full Sequence)

| # | Commit Message |
|---|----------------|
| 1 | `feat: initialise solution structure with src, tests, and docs layout` |
| 2 | `feat: add minimal MCP server with stdio transport and protocol handshake` |
| 3 | `feat: implement HealthCheck tool with real HTTP execution and structured response` |
| 4 | `feat: implement SSLCertificate inspection tool with expiry detection` |
| 5 | `feat: add MCP client with LLM integration for natural language tool invocation` |
| 6 | `feat: add typed contracts with validation pipeline and structured error responses` |
| 7 | `feat: implement response pattern analysis with trend detection and in-memory history` |
| 8 | `feat: implement DiagnoseEndpoint orchestration tool with parallel execution` |
| 9 | `feat: add configurable rate limiting and concurrency governance for tools` |
| 10 | `feat: add execution tracing, structured logging, and self-introspection tool` |
| 11 | `feat: add unit and integration tests for tools, validation, and MCP protocol flow` |
| 12 | `docs: add README, architecture diagram, demo walkthrough, and ADRs` |

---

## Appendix: Technology Stack

| Component | Technology |
|-----------|-----------|
| Runtime | .NET 8 / .NET 9 |
| MCP SDK | `ModelContextProtocol` NuGet (v1.0+) |
| AI Abstraction | `Microsoft.Extensions.AI` |
| LLM Provider | Anthropic Claude or OpenAI (configurable) |
| HTTP Client | `System.Net.Http.HttpClient` via DI |
| SSL Inspection | `System.Net.Security.SslStream` + `X509Certificate2` |
| Validation | `FluentValidation` or `DataAnnotations` |
| Rate Limiting | `System.Threading.RateLimiting` |
| Logging | `Microsoft.Extensions.Logging` (Serilog optional) |
| Testing | xUnit + Moq + `Microsoft.AspNetCore.TestHost` |
| Demo Recording | `asciinema` or screen capture |

---

## Appendix: Key Architectural Principles

1. **Real I/O over mocks** — At least two tools make genuine network calls. The system works against any public URL, not simulated data.

2. **Deterministic tools, non-deterministic decisions** — The LLM chooses *which* tool to call. The tool itself is pure logic with predictable outputs for given inputs.

3. **Governance as a first-class concern** — Rate limiting and validation exist at the pipeline level, not bolted on as afterthoughts.

4. **Observable by default** — Every tool invocation is traced. The AI can query its own execution history.

5. **Graceful degradation** — Tool failures produce structured errors, not exceptions. Partial results are preferred over total failure.

6. **Clean contracts at the boundary** — The line between "AI territory" and "deterministic territory" is explicitly defined by typed request/response schemas.

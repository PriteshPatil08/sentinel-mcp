# Sentinel.MCP — Quiz Bank

---

## Quiz 1 — MCP Server Setup & .NET Fundamentals

**Q1. What does `ConfigureAwait(false)` do and why do we use it?**
> It tells the awaiter not to resume on the original synchronization context after the await completes.
> In UI or ASP.NET Classic apps, the sync context is the UI thread — resuming on it unnecessarily causes deadlocks.
> In a console/generic host (like our MCP server), there is no sync context, but it's still correct practice and avoids overhead.

---

**Q2. What is the difference between `Host.CreateApplicationBuilder` and `WebApplication.CreateBuilder`?**
> `WebApplication.CreateBuilder` sets up a full web stack — Kestrel HTTP server, middleware pipeline, routing, and HTTP-specific services.
> `Host.CreateApplicationBuilder` sets up only the generic hosting infrastructure — DI, configuration, logging, and background services.
> We use the generic host because our MCP server communicates over stdio, not HTTP, so spinning up Kestrel would be wasteful and wrong.

---

**Q3. Why do we redirect all logs to stderr in an MCP stdio server?**
> The MCP protocol uses stdout as the communication channel — JSON-RPC messages flow in and out of it.
> If log lines leak into stdout, the client receives garbled JSON and the protocol breaks.
> Redirecting everything to stderr via `LogToStandardErrorThreshold = LogLevel.Trace` keeps stdout clean for the protocol.

---

**Q4. Why register tools with `AddMcpServer().WithTools<T>()` instead of instantiating them manually?**
> The MCP SDK uses reflection to discover tool methods, read their `[Description]` attributes, and build the `tools/list` schema.
> Registering through the SDK wires up dependency injection — so your tool can receive `IHttpClientFactory` and other services through the constructor.
> If you new them up manually, you bypass DI and the SDK never knows the tool exists.

---

**Q5. Why does the Contracts project exist as a separate project?**
> Contracts hold shared data shapes — request/response DTOs and interfaces — that both the Tools project and any client need to reference.
> If they lived inside Tools, a client project would have to take a dependency on tool implementation code just to understand the response shape.
> Separation of Contracts from implementation is how you avoid circular dependencies and keep the surface area each project exposes minimal.

---

**Q6. Why do MCP tools return `ToolResult` instead of throwing exceptions?**
> The MCP protocol is JSON-RPC — exceptions don't cross process or network boundaries in a meaningful way.
> An unhandled exception from a tool gets swallowed or turned into a generic error message; the AI model can't reason about it or suggest a fix.
> Returning a structured `ToolError` with an `ErrorCode` gives the model something actionable — it can tell the user "DNS failed" vs "SSL error" vs "timed out."

---

## Quiz 2 — HealthCheckTool Internals

**Q7. Why use `Stopwatch` instead of subtracting two `DateTime.UtcNow` values for latency?**
> `DateTime.UtcNow` is tied to the system clock, which can jump forward or backward due to NTP synchronisation.
> If NTP adjusts the clock between your two reads, the subtraction gives a nonsensical or even negative duration.
> `Stopwatch` uses a monotonic hardware counter that only ever increases, making it the correct tool for elapsed time measurement.

---

**Q8. What does `out T` covariance mean on `IToolResult<out T>`?**
> The `out` keyword means `T` can only appear in output positions — return types and readable properties — never as a method parameter.
> This allows `IToolResult<HealthCheckResponse>` to be treated as `IToolResult<object>` without a cast, because the narrower type can always satisfy the broader contract.
> In practice it means you can hold any tool result in a variable typed as `IToolResult<object>` without losing type safety.

---

**Q9. What does `CancellationTokenSource.CreateLinkedTokenSource` do?**
> It creates a new `CancellationTokenSource` that cancels when *either* of the two source tokens is cancelled.
> This lets us combine the caller's cancellation token (user abort) with our own timeout token (CancelAfter) without replacing either.
> Neither the caller nor our timeout "owns" the new token — the linked source manages that, which is why creation responsibility is outsourced.

---

**Q10. Why use `IHttpClientFactory` instead of `new HttpClient()`?**
> `HttpClient` holds a `HttpMessageHandler` that caches DNS resolutions and keeps sockets open; creating a new one per request exhausts OS socket handles (socket exhaustion).
> `IHttpClientFactory` manages a pool of handlers with controlled lifetimes — it recycles them before their DNS cache goes stale.
> It also enables named clients with pre-configured settings (like `AllowAutoRedirect = false`) registered once at startup.

---

**Q11. Why can't we change `HttpClient.DefaultRequestHeaders` or `AllowAutoRedirect` per request?**
> `HttpClient` is designed to be reused across many requests — its handler and default settings are shared state.
> Mutating them mid-flight would affect concurrent requests running on the same instance, causing race conditions.
> The correct pattern is to bake the difference into separate named clients at startup — one with `AllowAutoRedirect = true`, one with `false`.

---

**Q12. Why does HTTP/2 have no reason phrase (e.g. "OK", "Not Found")?**
> HTTP/1.1 sent status lines as text: `HTTP/1.1 200 OK` — the reason phrase was part of the wire format.
> HTTP/2 is a binary protocol; headers are compressed with HPACK and there is no status line, only a `:status` pseudo-header with just the numeric code.
> Sending human-readable reason phrases over HTTP/2 would waste bandwidth and defeat compression — so they were dropped entirely.

---

**Q13. What does the `out` keyword require of `T` in `IToolResult<out T>`, and what would break if you removed it?**
> With `out T`, the compiler enforces that `T` appears only in return/output positions — you cannot write a method that *accepts* a `T` parameter.
> This guarantees that reading a `T` from a covariant interface is always safe — a `HealthCheckResponse` is always an `object`, so upcasting is sound.
> Without `out`, the interface is invariant — `IToolResult<HealthCheckResponse>` and `IToolResult<object>` are completely unrelated types.

---

## Quiz 3 — HealthCheckTool Completion

**Q14. What is CA1031 and why do we suppress it with `#pragma` instead of removing the catch?**
> CA1031 warns that catching `Exception` (the base type) is too broad — you might silently swallow bugs that should crash the program.
> We need the broad catch as a last resort because we have already handled all known specific exceptions above it; anything left is truly unknown.
> We suppress it with `#pragma warning disable/restore CA1031` scoped tightly to just that catch block, not the whole file, to keep the intent clear.

---

**Q15. What is the `Server` response header and why did we end up removing it from `HealthCheckResponse`?**
> The `Server` header is sent by the web server to identify itself — e.g. `nginx/1.25.3` or `Microsoft-IIS/10.0`.
> It became redundant in our response because the status code already carries the health signal the tool is meant to provide.
> Many production servers also suppress or spoof this header for security reasons, making it unreliable data.

---

**Q16. Why do we expose `IReadOnlyList<string>` instead of `List<string>` on a public response DTO property?**
> CA1002 flags `List<T>` in public APIs because it exposes mutation methods (`Add`, `Remove`, `Clear`) that callers should not be using on a response.
> `IReadOnlyList<string>` communicates intent clearly: this is data you read, not a collection you modify.
> A `List<string>` internally still satisfies `IReadOnlyList<string>` at assignment, so no extra wrapping is needed.

---

**Q17. What is CA1000 and how did we fix it?**
> CA1000 warns against static members on generic types — calling `ToolResult<T>.Ok(...)` requires you to specify `T` explicitly at the call site.
> The fix is to move the static factory methods to a non-generic companion class `ToolResult` (no type parameter), so the compiler can infer `T` from the argument.
> `ToolResult.Ok(data, ms)` lets the compiler see the type of `data` and infer `T` automatically — no `<HealthCheckResponse>` annotation needed.

---

**Q18. Why is `ExecutedAtUtc = DateTime.UtcNow` inside `ToolResult.Ok()` a subtle timing bug?**
> `ToolResult.Ok()` is called *after* the operation completes and the stopwatch is stopped — so `DateTime.UtcNow` captures the end time, not the start time.
> `ExecutedAtUtc` implies "when was this tool executed," which should be the moment it *started*, not the moment the result was assembled.
> The correct fix is to capture `DateTime.UtcNow` at the top of the method alongside `Stopwatch.StartNew()` and pass it through to `ToolResult.Ok()`.

---

**Q19. Why does `Dictionary<string, string>` work for response headers even though headers can have multiple values?**
> HTTP allows multiple values for the same header name — e.g. multiple `Set-Cookie` headers.
> We use `string.Join(", ", values)` to collapse multiple values into one comma-separated string per key before putting them in the dictionary.
> This is a deliberate lossy simplification for readability — if we needed to round-trip headers exactly, we'd use `Dictionary<string, List<string>>`.

---

**Q20. What is `IReadOnlyList<string>` vs `IList<string>` vs `List<string>` — and when do you pick each?**
> `List<string>` is the concrete type — pick it internally when you need to build and mutate a collection.
> `IList<string>` is the mutable interface — expose it when callers are allowed to add or remove items.
> `IReadOnlyList<string>` is the read-only interface — expose it on DTOs and responses where callers should only read, preserving the flexibility to change the backing type later.

---

## Quiz 4 — InspectSSLCertificate Tool

**Q21. Why use `TcpClient` + `SslStream` instead of `HttpClient` to inspect SSL certificates?**
> `HttpClient` validates the cert before you can access it — invalid certs (self-signed, expired, broken chain) throw before you ever get a reference to the cert object.
> `TcpClient` + `SslStream` with a custom validation callback lets us connect regardless and access `sslStream.RemoteCertificate` for every cert, good or bad.
> Inspection requires observing without judging — `HttpClient` is built for trust decisions, not observation.

---

**Q22. What is the concrete technical difference between `catch (T ex) when (condition)` and `catch (T ex) { if (!condition) throw; }`?**
> With `when`, if the condition is false the stack never unwinds — the runtime keeps searching for the next handler with all frames intact and `finally`/`using` blocks not yet run.
> With `catch { throw; }`, the stack has already unwound to enter the catch block — `finally` blocks and `using` disposals have already fired before you rethrow.
> The debugger also breaks at the original throw site with `when`, not at the rethrow point — full stack context is preserved.

---

**Q23. What does `SslProtocols.None` actually mean, and what regresses if you hardcode `SslProtocols.Tls13`?**
> `SslProtocols.None` defers protocol selection to the OS security policy — when the OS patches out TLS 1.0/1.1, your code inherits that improvement automatically with no redeploy.
> Hardcoding `Tls13` breaks against any server still on TLS 1.2 (common in enterprise) and overrides OS-level security patches, making you less secure than saying nothing.
> `None` is not a lack of configuration — it's deliberate delegation of security policy to the authority best placed to maintain it.

---

**Q24. In the validation callback, `chain.Build()` and `return true` are both booleans — what does each one mean and why do we separate them?**
> `chain.Build()` answers "can this cert be traced to a trusted root CA?" — we capture this as `chainValid` to report it truthfully in the response.
> `return true` answers "should the TLS handshake proceed?" — we always return `true` so we can see every cert regardless of chain validity.
> Collapsing them into `return chain.Build()` would reject self-signed and broken-chain certs before we could inspect them — the same problem as using `HttpClient`.

---

**Q25. Why check `ex.SocketErrorCode` as an enum rather than `ex.Message.Contains("Host not found")`?**
> `ex.Message` changes across OS versions, .NET versions, and locales — a French Windows machine produces a different string, silently breaking your catch filter with no compile error.
> `SocketErrorCode` is a numeric contract between the OS and the runtime — `SocketError.HostNotFound` is the same value everywhere, forever.
> The same principle drives `ToolErrorCode` — we return typed enum values to the LLM so it can match against stable codes, not parse fragile free-text strings.

---

## Quiz 5 — LLM Integration, MCP Client & the MEA Stack

**Q27. When you call `McpClient.CreateAsync` with `StdioClientTransport` — what is actually happening at the OS level?**
> The OS spawns a new child process running the MCP server; the client is the parent.
> Two anonymous pipes are wired between them — client writes to the server's stdin, reads from the server's stdout.
> Stdout is a protocol pipe, not a console — any log line on stdout is a malformed JSON-RPC frame. That's why server logs go to stderr.
> `await using var mcpClient` sends the kill signal to the child on exit — the server lifetime is bound to the client.
> The MCP handshake sequence is: `initialize` → `initialized` → `tools/list`, before any tool call is made.

---

**Q28. Why `await using` specifically — what does the `await` add over plain `using`?**
> `McpClient` implements `IAsyncDisposable`, not just `IDisposable`.
> Disposal involves I/O waits — draining the pipe, sending kill signal, waiting for the child process to exit.
> `await using` lets those waits happen asynchronously without blocking the calling thread.
> `using` alone would call synchronous `Dispose()`, blocking the thread for the full teardown duration.

---

**Q29. `ListToolsAsync()` returns `IList<McpClientTool>`. `ChatOptions.Tools` expects `IList<AITool>`. How does the assignment work without a cast?**
> `McpClientTool` extends `AIFunction` which extends `AITool` — it IS an `AITool` through inheritance.
> The MCP SDK was deliberately built to implement MEA's `AITool` contract so tools are plug-and-play with any MEA-compatible chat client.
> The consumer codes to `AITool`; the provider implements `AITool` — they never need to know each other's concrete types.

---

**Q30. Walk through what `UseFunctionInvocation()` does when Claude returns a `tool_use` response.**
> Claude returns `stop_reason: "tool_use"` with a content block containing the tool name and arguments as JSON.
> The middleware intercepts, calls `McpClientTool.InvokeAsync()` which sends a `tools/call` JSON-RPC message down the stdio pipe.
> The tool result is appended to history alongside the tool call turn, then a second request is sent to Claude.
> Claude synthesises the raw result into natural language — minimum two round trips per tool call.

---

**Q31. What happens if you remove `history.AddRange(response.Messages)` and only track user messages?**
> Claude sees a stream of user messages with no assistant responses between them — one side of a phone call.
> Tool call turns vanish from context — Claude has no record of what it called or what the result was, causing hallucination or repeated calls.
> `List<ChatMessage>` is the entire session state; Claude is stateless — every API call is cold and history is the only memory.

---

**Q32. `AnthropicClient anthropicClient = new()` — where does it get the API key, and what is the production risk?**
> The SDK reads `ANTHROPIC_API_KEY` from the environment by convention.
> In our code we manually bridge: user-secrets → string → `Environment.SetEnvironmentVariable` → SDK reads it back. Inelegant; the cleaner form passes the key directly to the constructor.
> Production risk: `Environment.SetEnvironmentVariable` at runtime sets a process-level env var in code — any monitoring agent or crash dump that snapshots the process environment exposes the key in plaintext.
> In production, secrets should be injected at the infrastructure level (container env vars, Key Vault), never created inside application code.

---

**Q33. What is the concrete benefit of coding against `IChatClient` instead of `AnthropicClient` directly?**
> `IChatClient` is MEA's common chat abstraction — all LLMs share the same shape (send messages, get response), so they can all satisfy one interface.
> Swapping providers (Claude → GPT-4) requires changing one line — the `AsIChatClient()` call. The history management, middleware, and chat loop are untouched.
> It also enables test doubles: swap the real client for a scripted fake — deterministic tests, no API calls, no cost.

---

**Q26. When is suppressing a Roslyn analyser warning correct, and when is it a red flag?**
> Suppression is correct when you can state in one sentence exactly why this specific case is a justified exception to a rule you still agree with in general.
> It is a red flag when the suppression is lazy, file-scoped, or you cannot articulate the reason — the analyser is now blind and future readers have no idea why.
> Scope matters too: suppress with `#pragma disable/restore` around the single line, not the file — the suppression should be as small as the justification.

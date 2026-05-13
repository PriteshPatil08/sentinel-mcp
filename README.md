# Sentinel.MCP

> A .NET MCP platform that gives AI agents real diagnostic tools for API health monitoring — with typed contracts, structured telemetry, and safe tool execution.

![Status](https://img.shields.io/badge/status-under%20construction-yellow)
![.NET](https://img.shields.io/badge/.NET-10-blue)

## What it is

Sentinel.MCP is a hands-on tutorial project that builds a production-grade [Model Context Protocol](https://modelcontextprotocol.io) server and LLM client in .NET 10. It demonstrates how to expose typed diagnostic tools to AI agents — and how those agents call them via natural language.

## Solution Structure

```
src/
  Sentinel.MCP.Contracts/        — DTOs, IToolResult<T>, ToolErrorCode (no deps)
  Sentinel.MCP.Tools/            — Tool implementations (HealthCheck, InspectSSLCertificate)
  Sentinel.MCP.Server/           — MCP stdio server host (spawned as child process)
  Sentinel.MCP.Client/           — Console LLM client: Claude + MCP tool loop
tests/
  Sentinel.MCP.Tools.Tests/      — Unit tests
  Sentinel.MCP.Integration.Tests/ — End-to-end MCP protocol tests
```

## Tools Implemented

| Tool | What it does |
|------|-------------|
| `HealthCheck` | HTTP GET to any URL — returns status code, latency, headers, redirect chain |
| `InspectSSLCertificate` | TLS handshake via raw `SslStream` — returns cert subject, expiry, SAN, chain validity, TLS version |

## Running the client

```powershell
# Set your Anthropic API key (once)
dotnet user-secrets set "Anthropic:ApiKey" "<key>" --project src/Sentinel.MCP.Client

# Run — spawns the MCP server automatically, connects Claude
dotnet run --project src/Sentinel.MCP.Client
```

## Build

```powershell
dotnet build
dotnet test
```

## Progress

| Step | Description | Status |
|------|-------------|--------|
| 1 | Solution structure & repo setup | ✅ |
| 2 | Minimal MCP server (stdio shell) | ✅ |
| 3 | Tool contracts layer + HealthCheck tool | ✅ |
| 4 | InspectSSLCertificate tool | ✅ |
| 5 | LLM integration (Claude client + MCP tool loop) | ✅ |
| 6 | Typed request/response validation | 🔲 |
| 7 | AnalyseResponsePattern tool | 🔲 |
| 8 | DiagnoseEndpoint orchestration tool | 🔲 |
| 9 | Rate limiting & tool governance | 🔲 |
| 10 | Telemetry & execution tracing | 🔲 |
| 11 | Tests | 🔲 |
| 12 | README, architecture diagram & demo | 🔲 |

## Key tech

- **.NET 10** — target framework
- **ModelContextProtocol 1.2.0** — MCP server + client SDK
- **Microsoft.Extensions.AI** — `IChatClient` abstraction, `UseFunctionInvocation()` middleware
- **Anthropic .NET SDK** — Claude integration via `AnthropicClient.AsIChatClient()`
- **xUnit** — test framework

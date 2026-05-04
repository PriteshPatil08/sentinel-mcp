using Sentinel.MCP.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(options =>
    options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddHttpClient("HealthCheck.Follow");
builder.Services.AddHttpClient("HealthCheck.NoFollow")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<HealthCheckTool>()
    .WithTools<InspectSSLCertificateTool>();

builder.Services.Configure<McpServerOptions>(options =>
{
    options.ServerInfo = new Implementation
    {
        Name = builder.Configuration["McpServer:Name"] ?? "Sentinel.MCP",
        Version = builder.Configuration["McpServer:Version"] ?? "1.0.0"
    };
});

await builder.Build().RunAsync().ConfigureAwait(false);

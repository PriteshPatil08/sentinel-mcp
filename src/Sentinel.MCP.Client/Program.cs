using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Constants;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;

var builder = Host.CreateApplicationBuilder(args);
var apiKey = builder.Configuration["Anthropic:ApiKey"]
    ?? throw new InvalidOperationException("Anthropic:ApiKey not set in user-secrets.");

#pragma warning disable CA2007
await using var mcpClient = await McpClient.CreateAsync(
    new StdioClientTransport(new StdioClientTransportOptions
    {
        Command = "dotnet",
        Arguments = ["run", "--project", "src/Sentinel.MCP.Server", "--no-launch-profile"],
        Name = "Sentinel"
    })).ConfigureAwait(false);
#pragma warning restore CA2007

var tools = await mcpClient.ListToolsAsync().ConfigureAwait(false);

using var anthropicClient = new AnthropicClient(new APIAuthentication(apiKey));
IChatClient chatClient = anthropicClient
    .Messages
    .AsBuilder()
    .UseFunctionInvocation()
    .Build();

var history = new List<ChatMessage>();

Console.WriteLine("Sentinel is ready. Ask anything (or type 'exit' to quit).");
Console.WriteLine();

while (true)
{
    Console.Write("> ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
    {
        continue;
    }

    if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    history.Add(new ChatMessage(ChatRole.User, input));

    var response = await chatClient.GetResponseAsync(
        history,
        new ChatOptions
        {
            ModelId = AnthropicModels.Claude46Sonnet,
            MaxOutputTokens = 4096,
            Tools = [.. tools]
        }).ConfigureAwait(false);

    var reply = response.Text ?? "(no response)";
    history.Add(response.Messages[^1]);

    Console.WriteLine();
    Console.WriteLine(reply);
    Console.WriteLine();
}

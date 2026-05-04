namespace Sentinel.MCP.Contracts;

public sealed class SSLCertificateRequest
{
    public string Hostname { get; init; } = string.Empty;
    public int Port { get; init; } = 443;
}

namespace Sentinel.MCP.Contracts;

public sealed class SSLCertificateResponse
{
    public string Subject { get; init; } = string.Empty;
    public string Issuer { get; init; } = string.Empty;
    public DateTime ValidFrom { get; init; }
    public DateTime ExpiresOn { get; init; }
    public int DaysUntilExpiry { get; init; }
    public bool IsExpired { get; init; }
    public bool IsExpiringSoon { get; init; }
    public string TlsVersion { get; init; } = string.Empty;
    public bool CertificateChainValid { get; init; }
    public IReadOnlyList<string> SubjectAlternativeNames { get; init; } = [];
    public string Thumbprint { get; init; } = string.Empty;
}

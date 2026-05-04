using System.ComponentModel;
using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

using ModelContextProtocol.Server;

using Sentinel.MCP.Contracts;

namespace Sentinel.MCP.Tools;

[McpServerToolType]
public sealed class InspectSSLCertificateTool
{
    [McpServerTool(Name = "InspectSSLCertificate")]
    [Description("Inspects the SSL/TLS certificate of a given hostname. Returns certificate details including issuer, expiry date, days until expiration, TLS version, and chain validity. Use when asked about certificate health, security posture, or upcoming certificate expirations.")]
    public static async Task<ToolResult<SSLCertificateResponse>> InspectAsync(
        [Description("The hostname to inspect (e.g. api.github.com)")] string hostname,
        [Description("The port to connect to")] int port = 443,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var chainValid = false;

        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(hostname, port, cancellationToken).ConfigureAwait(false);

#pragma warning disable CA5359
        using var sslStream = new SslStream(
            tcpClient.GetStream(),
            leaveInnerStreamOpen: false,
            (_, certificate, chain, _) =>
            {
                chainValid = chain?.Build(new X509Certificate2(certificate!)) ?? false;
                return true;
            });
#pragma warning restore CA5359

        await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = hostname,
            EnabledSslProtocols = SslProtocols.None,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        }, cancellationToken).ConfigureAwait(false);

        var rawCert = sslStream.RemoteCertificate!;
        using var cert = X509CertificateLoader.LoadCertificate(rawCert.Export(X509ContentType.Cert));

        var now = DateTime.UtcNow;
        var daysUntilExpiry = (int)(cert.NotAfter.ToUniversalTime() - now).TotalDays;
        var isExpired = cert.NotAfter.ToUniversalTime() < now;
        var isExpiringSoon = !isExpired && daysUntilExpiry <= 30;

        var sanExtension = cert.Extensions
            .OfType<X509SubjectAlternativeNameExtension>()
            .FirstOrDefault();
        var sans = sanExtension?.EnumerateDnsNames().ToList() ?? [];

        stopwatch.Stop();
        return ToolResult.Ok(new SSLCertificateResponse
        {
            Subject = cert.Subject,
            Issuer = cert.Issuer,
            ValidFrom = cert.NotBefore.ToUniversalTime(),
            ExpiresOn = cert.NotAfter.ToUniversalTime(),
            DaysUntilExpiry = daysUntilExpiry,
            IsExpired = isExpired,
            IsExpiringSoon = isExpiringSoon,
            TlsVersion = sslStream.SslProtocol.ToString(),
            CertificateChainValid = chainValid,
            SubjectAlternativeNames = sans,
            Thumbprint = cert.Thumbprint
        }, stopwatch.ElapsedMilliseconds);
    }
}

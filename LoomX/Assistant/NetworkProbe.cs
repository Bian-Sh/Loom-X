using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace LoomX.Assistant;

/// <summary>
/// 网络分层探针：DNS / TCP / TLS。供 Diagnostic Subagent 逐层定位连通性问题。
/// 结果只含安全摘要（地址族、耗时、TLS 版本、证书有效期），不含任何 Secret。
/// </summary>
public sealed class NetworkProbe
{
    private readonly ILogger<NetworkProbe> logger;

    public NetworkProbe(ILogger<NetworkProbe> logger)
    {
        this.logger = logger;
    }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    public async Task<JsonObject> DnsAsync(string host, CancellationToken cancellationToken)
    {
        var result = new JsonObject { ["layer"] = "dns", ["host"] = host };
        try
        {
            var started = Environment.TickCount64;
            var addresses = await System.Net.Dns.GetHostAddressesAsync(host, cancellationToken);
            result["ok"] = true;
            result["latency_ms"] = Environment.TickCount64 - started;
            result["addresses"] = new JsonArray(addresses.Select(address => (JsonNode?)JsonValue.Create(address.ToString())).ToArray());
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException)
        {
            result["ok"] = false;
            result["error"] = "dns_resolution_failed";
        }

        return result;
    }

    public async Task<JsonObject> TcpAsync(string host, int port, CancellationToken cancellationToken)
    {
        var result = new JsonObject { ["layer"] = "tcp", ["host"] = host, ["port"] = port };
        using var client = new TcpClient();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            var started = Environment.TickCount64;
            await client.ConnectAsync(host, port, timeout.Token);
            result["ok"] = true;
            result["latency_ms"] = Environment.TickCount64 - started;
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException)
        {
            result["ok"] = false;
            result["error"] = exception is OperationCanceledException ? "tcp_timeout" : "tcp_connect_failed";
        }

        return result;
    }

    public async Task<JsonObject> TlsAsync(string host, int port, CancellationToken cancellationToken)
    {
        var result = new JsonObject { ["layer"] = "tls", ["host"] = host, ["port"] = port };
        using var client = new TcpClient();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            var started = Environment.TickCount64;
            await client.ConnectAsync(host, port, timeout.Token);
            await using var stream = client.GetStream();
            await using var tls = new SslStream(stream, false);
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            }, timeout.Token);

            result["ok"] = true;
            result["latency_ms"] = Environment.TickCount64 - started;
            result["protocol"] = tls.SslProtocol.ToString();
            if (tls.RemoteCertificate is not null)
            {
                result["certificate_expires"] = tls.RemoteCertificate.GetExpirationDateString();
            }
        }
        catch (AuthenticationException)
        {
            result["ok"] = false;
            result["error"] = "tls_handshake_failed";
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException or IOException)
        {
            result["ok"] = false;
            result["error"] = exception is OperationCanceledException ? "tls_timeout" : "tcp_connect_failed";
        }

        return result;
    }
}

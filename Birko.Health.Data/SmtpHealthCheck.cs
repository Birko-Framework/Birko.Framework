using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Health.Data;

/// <summary>
/// Health check for an SMTP server. Tests connectivity by performing a TCP connect
/// and reading the SMTP banner (220 greeting), then sending QUIT.
/// </summary>
public sealed class SmtpHealthCheck : IHealthCheck
{
    private readonly string _host;
    private readonly int _port;

    /// <summary>
    /// Creates an SMTP health check.
    /// </summary>
    /// <param name="host">SMTP server host.</param>
    /// <param name="port">SMTP server port. Default: 25.</param>
    public SmtpHealthCheck(string host, int port = 25)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Host cannot be null or empty.", nameof(host));
        }

        _host = host;
        _port = port;
    }

    public async Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var sw = Stopwatch.StartNew();

            using var client = new TcpClient();
            await client.ConnectAsync(_host, _port, ct).ConfigureAwait(false);

            using var stream = client.GetStream();
            var buffer = new byte[1024];
            var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            var banner = Encoding.ASCII.GetString(buffer, 0, read).Trim();

            // Send QUIT to be polite
            var quit = Encoding.ASCII.GetBytes("QUIT\r\n");
            await stream.WriteAsync(quit, ct).ConfigureAwait(false);

            sw.Stop();

            var data = new Dictionary<string, object>
            {
                ["host"] = _host,
                ["port"] = _port,
                ["latencyMs"] = Math.Round(sw.Elapsed.TotalMilliseconds, 2),
                ["banner"] = banner.Length > 100 ? banner[..100] : banner
            };

            // SMTP 220 = service ready
            if (!banner.StartsWith("220"))
            {
                return HealthCheckResult.Unhealthy($"SMTP unexpected banner: {banner[..Math.Min(banner.Length, 50)]}", data: data);
            }

            if (sw.Elapsed.TotalMilliseconds > 2000)
            {
                return HealthCheckResult.Degraded($"SMTP ({_host}:{_port}) responding slowly: {sw.Elapsed.TotalMilliseconds:F0}ms.", data: data);
            }

            return HealthCheckResult.Healthy($"SMTP ({_host}:{_port}) OK ({sw.Elapsed.TotalMilliseconds:F0}ms).", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"SMTP ({_host}:{_port}) connection failed: {ex.Message}", ex);
        }
    }
}

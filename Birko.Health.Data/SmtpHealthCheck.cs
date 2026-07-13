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
    private readonly int _timeoutMs;

    /// <summary>
    /// Creates an SMTP health check.
    /// </summary>
    /// <param name="host">SMTP server host.</param>
    /// <param name="port">SMTP server port. Default: 25.</param>
    /// <param name="timeoutMs">
    /// Bounded timeout (ms) for the connect + banner read + QUIT write. CR-M193: a hung/half-open
    /// server that completes the TCP handshake but never sends a banner must not block forever when
    /// the caller passes no CancellationToken. Default: 5000.
    /// </param>
    public SmtpHealthCheck(string host, int port = 25, int timeoutMs = 5000)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Host cannot be null or empty.", nameof(host));
        }

        _host = host;
        _port = port;
        _timeoutMs = timeoutMs > 0 ? timeoutMs : 5000;
    }

    public async Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        // CR-M193: bound the whole probe so a silent server surfaces as Unhealthy rather than hanging.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_timeoutMs);
        var opCt = timeoutCts.Token;
        try
        {
            var sw = Stopwatch.StartNew();

            using var client = new TcpClient();
            await client.ConnectAsync(_host, _port, opCt).ConfigureAwait(false);

            using var stream = client.GetStream();
            var buffer = new byte[1024];
            var read = await stream.ReadAsync(buffer, opCt).ConfigureAwait(false);
            var banner = Encoding.ASCII.GetString(buffer, 0, read).Trim();

            // Send QUIT to be polite
            var quit = Encoding.ASCII.GetBytes("QUIT\r\n");
            await stream.WriteAsync(quit, opCt).ConfigureAwait(false);

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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller cancelled — propagate so the runner's timeout handling applies (CR-M191 pattern).
            throw;
        }
        catch (OperationCanceledException)
        {
            // Our own bounded timeout fired (server hung / never sent a banner) — report it, don't hang.
            return HealthCheckResult.Unhealthy($"SMTP ({_host}:{_port}) timed out after {_timeoutMs}ms (no banner).");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"SMTP ({_host}:{_port}) connection failed: {ex.Message}", ex);
        }
    }
}

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Birko.Health;
using Birko.Health.Data;
using FluentAssertions;
using Xunit;

namespace Birko.Health.Tests;

/// <summary>
/// CR-M193: SmtpHealthCheck awaited the banner read with no bounded timeout, so a server that
/// completes the TCP handshake but never sends a banner made the check hang forever when the caller
/// passed no CancellationToken. It now bounds the probe with a linked-CTS timeout.
/// </summary>
public class SmtpHealthCheckTimeoutTests
{
    [Fact]
    public async Task CheckAsync_ServerAcceptsButNeverSendsBanner_ReturnsUnhealthyWithinTimeout()
    {
        // A loopback listener that accepts the connection but never writes a banner (half-open server).
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        // Accept and hold the socket open without responding.
        var accepting = Task.Run(async () =>
        {
            try { using var s = await listener.AcceptSocketAsync(); await Task.Delay(2000); }
            catch { /* listener stopped */ }
        });

        try
        {
            var check = new SmtpHealthCheck("127.0.0.1", port, timeoutMs: 200);

            var sw = Stopwatch.StartNew();
            var result = await check.CheckAsync(); // no caller token — must still not hang
            sw.Stop();

            result.Status.Should().Be(HealthStatus.Unhealthy);
            result.Description.Should().Contain("timed out");
            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3), "the bounded timeout must fire well before any test-runner timeout");
        }
        finally
        {
            listener.Stop();
            await accepting;
        }
    }

    [Fact]
    public async Task CheckAsync_CallerCancels_Propagates()
    {
        // A pre-cancelled caller token must propagate (CR-M191 pattern), not be masked as Unhealthy.
        var check = new SmtpHealthCheck("127.0.0.1", 65000, timeoutMs: 5000);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await check.Invoking(c => c.CheckAsync(cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }
}

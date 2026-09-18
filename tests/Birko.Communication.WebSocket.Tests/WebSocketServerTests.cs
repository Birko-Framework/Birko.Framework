using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Birko.Communication.WebSocket.Servers;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.WebSocket.Tests;

/// <summary>
/// CR-M075 coverage: WebSocketServer's Start/Stop/restart lifecycle, IsListening transitions,
/// Stop idempotency, and Broadcast guards were untested.
///
/// Robustness: the server binds a real HttpListener, which does NOT support port 0, so we grab an
/// OS-assigned free loopback port via a throwaway TcpListener and bind a fresh one per attempt
/// (retrying on the small bind race). StartAsync runs an accept loop that never completes until
/// Stop, so it is launched fire-and-forget and we poll IsListening with a bounded timeout instead
/// of relying on fixed sleeps.
/// </summary>
public class WebSocketServerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static int GetFreeLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < Timeout)
        {
            if (condition()) return true;
            await Task.Delay(20).ConfigureAwait(false);
        }
        return condition();
    }

    /// <summary>
    /// Launches the accept loop fire-and-forget on a free port and waits until it is listening.
    /// Retries a few times to absorb the free-port bind race. Returns the running server plus the
    /// (still-running) accept-loop task so the caller can stop it and observe completion.
    /// </summary>
    private static async Task<(WebSocketServer server, Task runTask)> StartServerAsync()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var prefix = $"http://localhost:{GetFreeLoopbackPort()}/";
            var server = new WebSocketServer();

            // async method: any bind failure faults the returned task rather than throwing here.
            var runTask = server.StartAsync(prefix);

            await WaitUntilAsync(() => server.IsListening || runTask.IsFaulted).ConfigureAwait(false);

            if (server.IsListening)
            {
                return (server, runTask);
            }

            // Bind lost the port race (or similar) — observe the fault and retry a fresh port.
            await ObserveAsync(runTask).ConfigureAwait(false);
            server.Dispose();
        }

        throw new InvalidOperationException("Could not bind a WebSocket server to a free loopback port after several attempts.");
    }

    /// <summary>Awaits the accept-loop task, swallowing the expected shutdown exceptions.</summary>
    private static async Task ObserveAsync(Task runTask)
    {
        try
        {
            await runTask.ConfigureAwait(false);
        }
        catch
        {
            // Stopping the listener tears down the in-flight GetContextAsync; that is expected.
        }
    }

    [Fact]
    public void IsListening_False_BeforeStart()
    {
        using var server = new WebSocketServer();

        server.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task StopAsync_IsIdempotent_OnNeverStartedServer()
    {
        var server = new WebSocketServer();

        var act = async () => await server.StopAsync();

        await act.Should().NotThrowAsync();
        server.IsListening.Should().BeFalse();
    }

    [Fact]
    public void Dispose_OnNeverStartedServer_DoesNotThrow()
    {
        var server = new WebSocketServer();

        var act = () => server.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Start_SetsIsListening_ThenStop_ClearsIt()
    {
        var (server, runTask) = await StartServerAsync();
        try
        {
            server.IsListening.Should().BeTrue();

            await server.StopAsync();

            server.IsListening.Should().BeFalse();
        }
        finally
        {
            await ObserveAsync(runTask);
            server.Dispose();
        }
    }

    [Fact]
    public async Task Restart_AfterStop_Works()
    {
        // The restart path (CR-M075): a second Start after Stop must bind again and re-listen,
        // and IsListening must track the full down->up->down->up->down cycle.
        var (server, runTask) = await StartServerAsync();
        try
        {
            server.IsListening.Should().BeTrue();

            await server.StopAsync();
            server.IsListening.Should().BeFalse();
            await ObserveAsync(runTask);

            // Restart on a fresh free port.
            var prefix = $"http://localhost:{GetFreeLoopbackPort()}/";
            runTask = server.StartAsync(prefix);

            var restarted = await WaitUntilAsync(() => server.IsListening || runTask.IsFaulted);
            restarted.Should().BeTrue();
            server.IsListening.Should().BeTrue();

            await server.StopAsync();
            server.IsListening.Should().BeFalse();
        }
        finally
        {
            await ObserveAsync(runTask);
            server.Dispose();
        }
    }

    [Fact]
    public async Task StopAsync_WhileRunning_IsIdempotent()
    {
        var (server, runTask) = await StartServerAsync();
        try
        {
            await server.StopAsync();
            var act = async () => await server.StopAsync();

            await act.Should().NotThrowAsync();
            server.IsListening.Should().BeFalse();
        }
        finally
        {
            await ObserveAsync(runTask);
            server.Dispose();
        }
    }

    [Fact]
    public async Task StartAsync_WhileAlreadyRunning_Throws()
    {
        var (server, runTask) = await StartServerAsync();
        try
        {
            var prefix = $"http://localhost:{GetFreeLoopbackPort()}/";
            var act = async () => await server.StartAsync(prefix);

            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        finally
        {
            await server.StopAsync();
            await ObserveAsync(runTask);
            server.Dispose();
        }
    }

    [Fact]
    public async Task BroadcastAsync_WithNoClients_DoesNotThrow()
    {
        var (server, runTask) = await StartServerAsync();
        try
        {
            var act = async () => await server.BroadcastAsync(new byte[] { 1, 2, 3 });

            await act.Should().NotThrowAsync();
        }
        finally
        {
            await server.StopAsync();
            await ObserveAsync(runTask);
            server.Dispose();
        }
    }

    [Fact]
    public async Task BroadcastAsync_Null_Throws()
    {
        var server = new WebSocketServer();
        try
        {
            var act = async () => await server.BroadcastAsync(null!);

            await act.Should().ThrowAsync<ArgumentNullException>();
        }
        finally
        {
            server.Dispose();
        }
    }
}

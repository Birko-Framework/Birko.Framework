using Birko.Communication.IR.Protocols;
using Birko.Communication.IR.Transports;

namespace Birko.Communication.IR.Tests;

/// <summary>
/// In-memory <see cref="IIrTransport"/> test double — records transmitted timings and lets a test
/// raise <see cref="OnReceived"/> to exercise <c>InfraredPort.HandleReceivedTiming</c> without hardware.
/// </summary>
internal sealed class FakeIrTransport : IIrTransport
{
    public string Name => "Fake";
    public bool IsConnected { get; private set; }

    public int ConnectCalls { get; private set; }
    public int DisconnectCalls { get; private set; }
    public int StartReceiveCalls { get; private set; }
    public int StopReceiveCalls { get; private set; }
    public bool Disposed { get; private set; }

    public readonly List<IrTiming> Transmitted = new();
    public IrTiming? LastTiming => Transmitted.Count > 0 ? Transmitted[^1] : null;

    public event EventHandler<IrTiming>? OnReceived;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ConnectCalls++;
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        DisconnectCalls++;
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task TransmitAsync(IrTiming timing, CancellationToken cancellationToken = default)
    {
        Transmitted.Add(timing);
        return Task.CompletedTask;
    }

    public Task StartReceiveAsync(CancellationToken cancellationToken = default)
    {
        StartReceiveCalls++;
        return Task.CompletedTask;
    }

    public Task StopReceiveAsync(CancellationToken cancellationToken = default)
    {
        StopReceiveCalls++;
        return Task.CompletedTask;
    }

    /// <summary>Simulate an inbound IR capture.</summary>
    public void RaiseReceived(IrTiming timing) => OnReceived?.Invoke(this, timing);

    public void Dispose() => Disposed = true;
}

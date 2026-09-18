using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.NFC.Models;
using Birko.Communication.NFC.Ports;
using Birko.Communication.NFC.Transports;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.NFC.Tests;

/// <summary>
/// CR-H028: NfcReaderPort wraps an IDisposable transport but never disposed it or unsubscribed the
/// TagDetected/TagRemoved handlers, leaking serial/HttpClient/CTS handles and keeping the port alive.
/// </summary>
public class NfcReaderPortDisposeTests
{
    private sealed class FakeTransport : INfcTransport
    {
        public string Name => "Fake";
        public bool Disposed { get; private set; }
        public bool IsConnected { get; private set; }
        public event EventHandler<NfcTagData>? TagDetected;
        public event EventHandler? TagRemoved;
        public event EventHandler<Exception>? PollingError;

        public int TagDetectedSubscribers => TagDetected?.GetInvocationList().Length ?? 0;

        public Task ConnectAsync(CancellationToken ct = default) { IsConnected = true; return Task.CompletedTask; }
        public Task DisconnectAsync(CancellationToken ct = default) { IsConnected = false; return Task.CompletedTask; }
        public Task<NfcTagData?> ReadTagAsync(int timeoutMs, CancellationToken ct = default) => Task.FromResult<NfcTagData?>(null);
        public Task StartPollingAsync(int intervalMs, CancellationToken ct = default) => Task.CompletedTask;
        public Task StopPollingAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<byte[]?> TransceiveAsync(byte[] apdu, CancellationToken ct = default) => Task.FromResult<byte[]?>(null);
        public void Dispose() => Disposed = true;

        public void RaiseTagDetected(NfcTagData tag) => TagDetected?.Invoke(this, tag);
    }

    [Fact]
    public void Dispose_DisposesTransportAndUnsubscribes()
    {
        var transport = new FakeTransport();
        var port = new NfcReaderPort(new NfcReaderSettings { Name = "reader" }, transport);
        transport.TagDetectedSubscribers.Should().Be(1, "the port subscribes in its constructor");

        port.Dispose();

        transport.Disposed.Should().BeTrue("CR-H028: the owned transport must be disposed");
        transport.TagDetectedSubscribers.Should().Be(0, "the port must unsubscribe on dispose");
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var transport = new FakeTransport();
        var port = new NfcReaderPort(new NfcReaderSettings { Name = "reader" }, transport);

        var act = () => { port.Dispose(); port.Dispose(); };

        act.Should().NotThrow();
    }

    [Fact]
    public void Port_IsDisposable()
    {
        typeof(NfcReaderPort).Should().BeAssignableTo<IDisposable>();
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.NFC.Models;
using Birko.Communication.NFC.Ports;
using Birko.Communication.NFC.Protocols;
using Birko.Communication.NFC.Transports;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.NFC.Tests;

/// <summary>
/// Covers <see cref="NfcReaderPort"/> — the Open/Close lifecycle, transport event forwarding,
/// ReadData population, the protocol-application pipeline, and APDU passthrough (CR-M057).
/// Uses a controllable fake <see cref="INfcTransport"/> so everything runs offline (no real hardware).
/// </summary>
public class NfcReaderPortTests
{
    /// <summary>A fully controllable in-memory transport: lets a test drive connection state and
    /// raise TagDetected/TagRemoved, and records the APDU it was asked to transceive.</summary>
    private sealed class ControllableTransport : INfcTransport
    {
        public string Name => "Controllable";
        public bool IsConnected { get; private set; }
        public bool Disposed { get; private set; }
        public byte[]? LastApdu { get; private set; }
        public byte[]? ApduResponse { get; set; }
        public NfcTagData? ReadTagResult { get; set; }

        public event EventHandler<NfcTagData>? TagDetected;
        public event EventHandler? TagRemoved;
        public event EventHandler<Exception>? PollingError;

        public Task ConnectAsync(CancellationToken ct = default) { IsConnected = true; return Task.CompletedTask; }
        public Task DisconnectAsync(CancellationToken ct = default) { IsConnected = false; return Task.CompletedTask; }
        public Task<NfcTagData?> ReadTagAsync(int timeoutMs, CancellationToken ct = default) => Task.FromResult(ReadTagResult);
        public Task StartPollingAsync(int intervalMs, CancellationToken ct = default) => Task.CompletedTask;
        public Task StopPollingAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<byte[]?> TransceiveAsync(byte[] apdu, CancellationToken ct = default) { LastApdu = apdu; return Task.FromResult(ApduResponse); }
        public void Dispose() => Disposed = true;

        public void RaiseTagDetected(NfcTagData tag) => TagDetected?.Invoke(this, tag);
        public void RaiseTagRemoved() => TagRemoved?.Invoke(this, EventArgs.Empty);
        // Silence unused-event warning; PollingError is part of the interface contract but the port
        // does not subscribe to it (it is a transport-level concern — see CR-M056).
        public void RaisePollingError(Exception ex) => PollingError?.Invoke(this, ex);
    }

    /// <summary>Records whether Parse/CanHandle ran, for asserting the protocol pipeline.</summary>
    private sealed class RecordingProtocol : INfcProtocol
    {
        public string Name => "Recording";
        public bool CanHandleResult { get; set; } = true;
        public int ParseCount { get; private set; }
        public NfcTagData? LastParsed { get; private set; }

        public bool CanHandle(NfcTagData tag) => CanHandleResult;
        public void Parse(NfcTagData tag, byte[] rawData) { ParseCount++; LastParsed = tag; tag.Metadata["Recorded"] = "yes"; }
    }

    private static NfcReaderPort NewPort(ControllableTransport transport) =>
        new(new NfcReaderSettings { Name = "reader", ReadTimeoutMs = 100, PollingIntervalMs = 50 }, transport);

    // ── Construction ──

    [Fact]
    public void Ctor_NullTransport_Throws()
    {
        var act = () => new NfcReaderPort(new NfcReaderSettings { Name = "reader" }, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_ExposesTransport()
    {
        var transport = new ControllableTransport();
        using var port = NewPort(transport);
        port.Transport.Should().BeSameAs(transport);
    }

    // ── Open / Close lifecycle ──

    [Fact]
    public void Open_ConnectsTransport_AndReportsOpen()
    {
        var transport = new ControllableTransport();
        using var port = NewPort(transport);

        port.IsOpen().Should().BeFalse("nothing is connected yet");

        port.Open();

        transport.IsConnected.Should().BeTrue();
        port.IsOpen().Should().BeTrue("IsOpen reflects the transport's connection state");
    }

    [Fact]
    public void Close_DisconnectsTransport_AndReportsClosed()
    {
        var transport = new ControllableTransport();
        using var port = NewPort(transport);
        port.Open();

        port.Close();

        transport.IsConnected.Should().BeFalse();
        port.IsOpen().Should().BeFalse();
    }

    // ── Event forwarding + ReadData population ──

    [Fact]
    public void TagDetected_ForwardsToPortEvent_AndPopulatesReadData()
    {
        var transport = new ControllableTransport();
        using var port = NewPort(transport);

        NfcTagData? forwarded = null;
        port.OnTagDetected += (_, tag) => forwarded = tag;

        var uid = new byte[] { 0x04, 0xA1, 0xB2, 0xC3 };
        var tag = new NfcTagData { Uid = "04A1B2C3", UidBytes = uid, TagType = NfcTagType.MifareClassic };

        transport.RaiseTagDetected(tag);

        forwarded.Should().BeSameAs(tag, "the transport's TagDetected must surface on the port");
        port.HasReadData(uid.Length).Should().BeTrue("UID bytes are appended to the low-level ReadData buffer");
        port.Read(uid.Length).Should().Equal(uid);
    }

    [Fact]
    public void TagDetected_InvokesProcessDataSubscribers()
    {
        var transport = new ControllableTransport();
        using var port = NewPort(transport);

        var processed = false;
        port.SubscribeProcessData(() => processed = true);

        transport.RaiseTagDetected(new NfcTagData { Uid = "AA", UidBytes = new byte[] { 0xAA } });

        processed.Should().BeTrue("HandleTagDetected calls InvokeProcessData after buffering the UID");
    }

    [Fact]
    public void TagRemoved_ForwardsToPortEvent()
    {
        var transport = new ControllableTransport();
        using var port = NewPort(transport);

        var removed = false;
        port.OnTagRemoved += (_, _) => removed = true;

        transport.RaiseTagRemoved();

        removed.Should().BeTrue();
    }

    // ── Protocol-application pipeline ──

    [Fact]
    public void RegisterProtocol_Null_Throws()
    {
        var transport = new ControllableTransport();
        using var port = NewPort(transport);

        var act = () => port.RegisterProtocol(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TagDetected_AppliesRegisteredProtocols_WhenTheyCanHandle()
    {
        var transport = new ControllableTransport();
        using var port = NewPort(transport);
        var protocol = new RecordingProtocol { CanHandleResult = true };
        port.RegisterProtocol(protocol);
        port.Protocols.Should().ContainSingle().Which.Should().BeSameAs(protocol);

        var tag = new NfcTagData { Uid = "04A1", UidBytes = new byte[] { 0x04, 0xA1 } };
        transport.RaiseTagDetected(tag);

        protocol.ParseCount.Should().Be(1);
        protocol.LastParsed.Should().BeSameAs(tag);
        tag.Metadata.Should().ContainKey("Recorded");
    }

    [Fact]
    public void TagDetected_SkipsProtocols_ThatCannotHandle()
    {
        var transport = new ControllableTransport();
        using var port = NewPort(transport);
        var protocol = new RecordingProtocol { CanHandleResult = false };
        port.RegisterProtocol(protocol);

        transport.RaiseTagDetected(new NfcTagData { Uid = "04A1", UidBytes = new byte[] { 0x04, 0xA1 } });

        protocol.ParseCount.Should().Be(0, "Parse must run only when CanHandle returns true");
    }

    [Fact]
    public async Task ReadTagAsync_ReturnsTransportTag_AndAppliesProtocols()
    {
        var transport = new ControllableTransport();
        var tag = new NfcTagData { Uid = "DEADBEEF", UidBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, TagType = NfcTagType.Iso14443A };
        transport.ReadTagResult = tag;
        using var port = NewPort(transport);
        var protocol = new RecordingProtocol();
        port.RegisterProtocol(protocol);

        var result = await port.ReadTagAsync();

        result.Should().BeSameAs(tag);
        protocol.ParseCount.Should().Be(1, "ReadTagAsync applies protocols to the tag it read");
    }

    [Fact]
    public async Task ReadTagAsync_NoTag_ReturnsNull_AndSkipsProtocols()
    {
        var transport = new ControllableTransport { ReadTagResult = null };
        using var port = NewPort(transport);
        var protocol = new RecordingProtocol();
        port.RegisterProtocol(protocol);

        var result = await port.ReadTagAsync();

        result.Should().BeNull();
        protocol.ParseCount.Should().Be(0);
    }

    // ── APDU passthrough ──

    [Fact]
    public async Task TransceiveApduAsync_ForwardsToTransport_AndReturnsResponse()
    {
        var transport = new ControllableTransport { ApduResponse = new byte[] { 0x90, 0x00 } };
        using var port = NewPort(transport);

        var apdu = new byte[] { 0x00, 0xA4, 0x04, 0x00 };
        var response = await port.TransceiveApduAsync(apdu);

        transport.LastApdu.Should().Equal(apdu);
        response.Should().Equal(new byte[] { 0x90, 0x00 });
    }

    [Fact]
    public async Task TransceiveApduAsync_NullApdu_Throws()
    {
        var transport = new ControllableTransport();
        using var port = NewPort(transport);

        var act = async () => await port.TransceiveApduAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Dispose_UnsubscribesTransportEvents()
    {
        var transport = new ControllableTransport();
        var port = NewPort(transport);

        NfcTagData? forwarded = null;
        port.OnTagDetected += (_, tag) => forwarded = tag;

        port.Dispose();
        transport.Disposed.Should().BeTrue();

        // After dispose the port has unsubscribed from the transport, so a raised event is inert.
        transport.RaiseTagDetected(new NfcTagData { Uid = "01", UidBytes = new byte[] { 0x01 } });
        forwarded.Should().BeNull("the port unsubscribed HandleTagDetected on Dispose");
    }
}

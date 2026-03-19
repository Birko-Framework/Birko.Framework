using FluentAssertions;
using Birko.Communication.NFC.Models;
using Birko.Communication.NFC.Transports;

namespace Birko.Communication.NFC.Tests;

public class HidNfcTransportTests
{
    [Fact]
    public void Name_IsHID()
    {
        using var transport = new HidNfcTransport();
        transport.Name.Should().Be("HID");
    }

    [Fact]
    public async Task ConnectAsync_SetsIsConnected()
    {
        using var transport = new HidNfcTransport();
        transport.IsConnected.Should().BeFalse();

        await transport.ConnectAsync();
        transport.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task DisconnectAsync_ClearsIsConnected()
    {
        using var transport = new HidNfcTransport();
        await transport.ConnectAsync();
        await transport.DisconnectAsync();

        transport.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task ReadTagAsync_NotConnected_Throws()
    {
        using var transport = new HidNfcTransport();
        var act = async () => await transport.ReadTagAsync(100);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ReadTagAsync_NoInput_ReturnsNull()
    {
        using var transport = new HidNfcTransport();
        await transport.ConnectAsync();

        var tag = await transport.ReadTagAsync(100);
        tag.Should().BeNull();
    }

    [Fact]
    public async Task ReadTagAsync_WithHexInput_ReturnsTag()
    {
        using var transport = new HidNfcTransport();
        await transport.ConnectAsync();

        // Feed input in background after a short delay
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            await transport.FeedInputAsync("04A1B2C3\n");
        });

        var tag = await transport.ReadTagAsync(2000);

        tag.Should().NotBeNull();
        tag!.Uid.Should().Be("04A1B2C3");
        tag.UidBytes.Should().Equal(new byte[] { 0x04, 0xA1, 0xB2, 0xC3 });
    }

    [Fact]
    public async Task ReadTagAsync_WithColonSeparatedInput_ReturnsTag()
    {
        using var transport = new HidNfcTransport();
        await transport.ConnectAsync();

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            await transport.FeedInputAsync("04:A1:B2:C3\n");
        });

        var tag = await transport.ReadTagAsync(2000);

        tag.Should().NotBeNull();
        tag!.Uid.Should().Be("04A1B2C3");
    }

    [Fact]
    public async Task ReadTagAsync_WithDecimalInput_ReturnsTag()
    {
        using var transport = new HidNfcTransport();
        await transport.ConnectAsync();

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            await transport.FeedInputAsync("12345678\n");
        });

        var tag = await transport.ReadTagAsync(2000);

        // "12345678" is valid hex, so parsed as hex
        tag.Should().NotBeNull();
        tag!.Uid.Should().Be("12345678");
    }

    [Fact]
    public void TransceiveAsync_ThrowsNotSupported()
    {
        using var transport = new HidNfcTransport();
        var act = async () => await transport.TransceiveAsync(new byte[] { 0x00 });
        act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task FeedInputAsync_PartialInput_NoTagUntilNewline()
    {
        using var transport = new HidNfcTransport();
        await transport.ConnectAsync();

        // Feed partial input without newline
        await transport.FeedInputAsync("04A1");

        // Should timeout since no complete UID
        var tag = await transport.ReadTagAsync(100);
        tag.Should().BeNull();
    }

    [Fact]
    public async Task FeedInputAsync_CompletedLater_ReturnsTag()
    {
        using var transport = new HidNfcTransport();
        await transport.ConnectAsync();

        _ = Task.Run(async () =>
        {
            await Task.Delay(30);
            await transport.FeedInputAsync("04A1");
            await Task.Delay(30);
            await transport.FeedInputAsync("B2C3\n");
        });

        var tag = await transport.ReadTagAsync(2000);

        tag.Should().NotBeNull();
        tag!.Uid.Should().Be("04A1B2C3");
    }

    [Fact]
    public async Task TagDetected_Event_FiredDuringPolling()
    {
        using var transport = new HidNfcTransport();
        await transport.ConnectAsync();

        NfcTagData? detectedTag = null;
        transport.TagDetected += (_, tag) => detectedTag = tag;

        await transport.StartPollingAsync(200);

        await Task.Delay(100);
        await transport.FeedInputAsync("AABBCCDD\n");
        await Task.Delay(500);

        await transport.StopPollingAsync();

        detectedTag.Should().NotBeNull();
        detectedTag!.Uid.Should().Be("AABBCCDD");
    }
}

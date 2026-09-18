using System;
using System.Reflection;
using System.Threading.Tasks;
using Birko.Communication.NFC.Models;
using Birko.Communication.NFC.Transports;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.NFC.Tests;

/// <summary>
/// Covers <see cref="SerialNfcTransport"/> (CR-M057). The PN532 frame parser
/// <c>ParseResponse(byte[], int)</c> and the SAK-based <c>DetectTagType(byte)</c> are both
/// <c>private static</c> and are pure/table-testable, so they are exercised via reflection
/// (there is no public path that reaches them without opening a real serial port). The public
/// surface (ctor guards, Name, not-connected guards) is tested directly — all offline.
/// </summary>
public class SerialNfcTransportTests
{
    private static NfcTagData? InvokeParseResponse(byte[] buffer, int length)
    {
        var method = typeof(SerialNfcTransport).GetMethod(
            "ParseResponse", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("ParseResponse must exist as a private static method");
        return (NfcTagData?)method!.Invoke(null, new object[] { buffer, length });
    }

    private static NfcTagType InvokeDetectTagType(byte sak)
    {
        var method = typeof(SerialNfcTransport).GetMethod(
            "DetectTagType", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("DetectTagType must exist as a private static method");
        return (NfcTagType)method!.Invoke(null, new object[] { sak })!;
    }

    // ── Public surface (no hardware) ──

    [Fact]
    public void Name_IsSerial()
    {
        using var transport = new SerialNfcTransport("COM3");
        transport.Name.Should().Be("Serial");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Ctor_EmptyPortName_Throws(string? portName)
    {
        var act = () => new SerialNfcTransport(portName!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IsConnected_False_WhenNotOpened()
    {
        using var transport = new SerialNfcTransport("COM3");
        transport.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task ReadTagAsync_NotConnected_Throws()
    {
        using var transport = new SerialNfcTransport("COM3");
        var act = async () => await transport.ReadTagAsync(100);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task TransceiveAsync_NotConnected_Throws()
    {
        using var transport = new SerialNfcTransport("COM3");
        var act = async () => await transport.TransceiveAsync(new byte[] { 0x00 });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── DetectTagType (SAK classification table) ──

    [Theory]
    [InlineData(0x08, NfcTagType.MifareClassic)]
    [InlineData(0x18, NfcTagType.MifareClassic)]
    [InlineData(0x00, NfcTagType.MifareUltralight)]
    [InlineData(0x04, NfcTagType.MifareUltralight)]
    [InlineData(0x20, NfcTagType.MifareDESFire)]
    [InlineData(0x01, NfcTagType.Iso14443A)] // unmapped SAK falls through to the base type
    [InlineData(0xFF, NfcTagType.Iso14443A)]
    public void DetectTagType_MapsSakToTagType(byte sak, NfcTagType expected)
    {
        InvokeDetectTagType(sak).Should().Be(expected);
    }

    // ── ParseResponse (PN532 InListPassiveTarget frame parser) ──

    [Fact]
    public void ParseResponse_ValidFrame_ReturnsTagWithUidSakAtqa()
    {
        // Preamble ... D5 4B [NbTg=1] [Tg] [ATQA hi] [ATQA lo] [SAK] [UIDlen] [UID...] postamble
        var frame = new byte[]
        {
            0x00, 0x00, 0xFF, 0x00, 0xFF, 0x00, // preamble noise (parser scans for D5 4B)
            0xD5, 0x4B,                         // response code
            0x01,                               // NbTg (one target)
            0x01,                               // Tg (target number, skipped)
            0x00, 0x04,                         // ATQA
            0x08,                               // SAK -> MIFARE Classic
            0x04,                               // UID length
            0x04, 0xA1, 0xB2, 0xC3,             // UID
            0x00                                // postamble
        };

        var tag = InvokeParseResponse(frame, frame.Length);

        tag.Should().NotBeNull();
        tag!.Uid.Should().Be("04A1B2C3");
        tag.UidBytes.Should().Equal(new byte[] { 0x04, 0xA1, 0xB2, 0xC3 });
        tag.TagType.Should().Be(NfcTagType.MifareClassic);
        tag.Sak.Should().Be(0x08);
        tag.Atqa.Should().Equal(new byte[] { 0x00, 0x04 });
    }

    [Fact]
    public void ParseResponse_DesfireSak_ClassifiesAsDesfire()
    {
        var frame = new byte[]
        {
            0xD5, 0x4B,
            0x01,                   // NbTg
            0x01,                   // Tg
            0x03, 0x44,             // ATQA
            0x20,                   // SAK -> DESFire
            0x07,                   // UID length (7-byte double-size UID)
            0x04, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66,
            0x00
        };

        var tag = InvokeParseResponse(frame, frame.Length);

        tag.Should().NotBeNull();
        tag!.TagType.Should().Be(NfcTagType.MifareDESFire);
        tag.UidBytes.Should().HaveCount(7);
        tag.Uid.Should().Be("04112233445566");
    }

    [Fact]
    public void ParseResponse_TooShort_ReturnsNull()
    {
        InvokeParseResponse(new byte[] { 0xD5, 0x4B, 0x01, 0x01, 0x00 }, 5)
            .Should().BeNull("frames shorter than 10 bytes are rejected");
    }

    [Fact]
    public void ParseResponse_NoResponseCode_ReturnsNull()
    {
        var garbage = new byte[20];
        Array.Fill(garbage, (byte)0xEE); // never contains the D5 4B marker
        InvokeParseResponse(garbage, garbage.Length).Should().BeNull();
    }

    [Fact]
    public void ParseResponse_ZeroTargets_ReturnsNull()
    {
        var frame = new byte[]
        {
            0xD5, 0x4B,
            0x00,                   // NbTg = 0 -> no tag
            0x01, 0x00, 0x04, 0x08, 0x04, 0x04, 0xA1, 0xB2, 0xC3
        };
        InvokeParseResponse(frame, frame.Length).Should().BeNull();
    }

    [Fact]
    public void ParseResponse_TruncatedUid_ReturnsNull()
    {
        // UID length byte claims 10 bytes but the buffer does not contain them.
        var frame = new byte[]
        {
            0x00, 0x00, 0xFF,
            0xD5, 0x4B,
            0x01,                   // NbTg
            0x01,                   // Tg
            0x00, 0x04,             // ATQA
            0x08,                   // SAK
            0x0A,                   // UID length = 10 (but only 2 bytes follow)
            0xA1, 0xB2
        };
        InvokeParseResponse(frame, frame.Length).Should().BeNull("UID length exceeds the frame");
    }
}

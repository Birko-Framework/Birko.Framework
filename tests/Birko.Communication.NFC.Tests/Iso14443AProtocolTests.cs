using FluentAssertions;
using Birko.Communication.NFC.Models;
using Birko.Communication.NFC.Protocols;

namespace Birko.Communication.NFC.Tests;

public class Iso14443AProtocolTests
{
    private readonly Iso14443AProtocol _protocol = new();

    [Fact]
    public void Name_IsISO14443A()
    {
        _protocol.Name.Should().Be("ISO14443A");
    }

    // ── CanHandle ──

    [Theory]
    [InlineData(NfcTagType.Iso14443A)]
    [InlineData(NfcTagType.MifareClassic)]
    [InlineData(NfcTagType.MifareUltralight)]
    [InlineData(NfcTagType.MifareDESFire)]
    [InlineData(NfcTagType.Ntag)]
    public void CanHandle_SupportedTypes_True(NfcTagType tagType)
    {
        var tag = new NfcTagData { TagType = tagType };
        _protocol.CanHandle(tag).Should().BeTrue();
    }

    [Theory]
    [InlineData(NfcTagType.Unknown)]
    [InlineData(NfcTagType.Iso14443B)]
    [InlineData(NfcTagType.Iso15693)]
    [InlineData(NfcTagType.FeliCa)]
    [InlineData(NfcTagType.Em4100)]
    [InlineData(NfcTagType.HidProx)]
    public void CanHandle_UnsupportedTypes_False(NfcTagType tagType)
    {
        var tag = new NfcTagData { TagType = tagType };
        _protocol.CanHandle(tag).Should().BeFalse();
    }

    // ── SAK Classification ──

    [Theory]
    [InlineData(0x08, NfcTagType.MifareClassic)]  // MIFARE Classic 1K
    [InlineData(0x18, NfcTagType.MifareClassic)]  // MIFARE Classic 4K
    [InlineData(0x09, NfcTagType.MifareClassic)]  // MIFARE Mini
    [InlineData(0x00, NfcTagType.MifareUltralight)]
    [InlineData(0x04, NfcTagType.Ntag)]
    [InlineData(0x20, NfcTagType.MifareDESFire)]
    [InlineData(0x28, NfcTagType.MifareClassic)]  // SmartMX + Classic 1K
    [InlineData(0x38, NfcTagType.MifareClassic)]  // SmartMX + Classic 4K
    [InlineData(0xFF, NfcTagType.Iso14443A)]       // Unknown SAK
    public void ClassifySak_ReturnsCorrectType(byte sak, NfcTagType expected)
    {
        Iso14443AProtocol.ClassifySak(sak).Should().Be(expected);
    }

    // ── Parse ──

    [Fact]
    public void Parse_WithSak_ClassifiesAndAddsMetadata()
    {
        var tag = new NfcTagData
        {
            TagType = NfcTagType.Iso14443A,
            Sak = 0x08,
            Atqa = new byte[] { 0x00, 0x04 },
            UidBytes = new byte[] { 0x04, 0xA1, 0xB2, 0xC3 }
        };

        _protocol.Parse(tag, Array.Empty<byte>());

        tag.TagType.Should().Be(NfcTagType.MifareClassic);
        tag.Metadata.Should().ContainKey("SAK");
        tag.Metadata["SAK"].Should().Be("0x08");
        tag.Metadata.Should().ContainKey("ATQA");
        tag.Metadata["ATQA"].Should().Be("0x0004");
        tag.Metadata["UIDLength"].Should().Be("4");
        tag.Metadata["UIDType"].Should().Be("Single");
    }

    [Fact]
    public void Parse_7ByteUid_DetectsDouble()
    {
        var tag = new NfcTagData
        {
            TagType = NfcTagType.Iso14443A,
            UidBytes = new byte[] { 0x04, 0xA1, 0xB2, 0xC3, 0xD4, 0xE5, 0xF6 }
        };

        _protocol.Parse(tag, Array.Empty<byte>());

        tag.Metadata["UIDType"].Should().Be("Double");
    }

    [Fact]
    public void Parse_10ByteUid_DetectsTriple()
    {
        var tag = new NfcTagData
        {
            TagType = NfcTagType.Iso14443A,
            UidBytes = new byte[10]
        };

        _protocol.Parse(tag, Array.Empty<byte>());

        tag.Metadata["UIDType"].Should().Be("Triple");
    }

    [Fact]
    public void Parse_WithPayload_SetsPayload()
    {
        var tag = new NfcTagData
        {
            TagType = NfcTagType.Iso14443A,
            UidBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 }
        };
        var rawData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

        _protocol.Parse(tag, rawData);

        tag.Payload.Should().Equal(rawData);
    }

    [Fact]
    public void Parse_EmptyPayload_DoesNotSetPayload()
    {
        var tag = new NfcTagData
        {
            TagType = NfcTagType.Iso14443A,
            UidBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 }
        };

        _protocol.Parse(tag, Array.Empty<byte>());

        tag.Payload.Should().BeNull();
    }
}

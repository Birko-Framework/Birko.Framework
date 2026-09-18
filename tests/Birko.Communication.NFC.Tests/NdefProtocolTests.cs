using System.Text;
using FluentAssertions;
using Birko.Communication.NFC.Models;
using Birko.Communication.NFC.Protocols;

namespace Birko.Communication.NFC.Tests;

public class NdefProtocolTests
{
    private readonly NdefProtocol _protocol = new();

    [Fact]
    public void Name_IsNDEF()
    {
        _protocol.Name.Should().Be("NDEF");
    }

    [Fact]
    public void CanHandle_Ntag_True()
    {
        var tag = new NfcTagData { TagType = NfcTagType.Ntag };
        _protocol.CanHandle(tag).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_MifareUltralight_True()
    {
        var tag = new NfcTagData { TagType = NfcTagType.MifareUltralight };
        _protocol.CanHandle(tag).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_DESFire_True()
    {
        var tag = new NfcTagData { TagType = NfcTagType.MifareDESFire };
        _protocol.CanHandle(tag).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_MifareClassic_False()
    {
        var tag = new NfcTagData { TagType = NfcTagType.MifareClassic };
        _protocol.CanHandle(tag).Should().BeFalse();
    }

    [Fact]
    public void ParseNdefMessage_UriRecord_ParsesCorrectly()
    {
        // Build a minimal NDEF message with TLV wrapper
        // TLV: Type=0x03, Length, NDEF record
        var uriBytes = Encoding.UTF8.GetBytes("example.com");
        var ndefPayload = new byte[1 + uriBytes.Length];
        ndefPayload[0] = 0x04; // https:// prefix
        Array.Copy(uriBytes, 0, ndefPayload, 1, uriBytes.Length);

        // NDEF record header: MB=1,ME=1,CF=0,SR=1,IL=0,TNF=0x01
        byte header = 0xD1; // 1101_0001
        byte typeLen = 1; // "U"
        byte payloadLen = (byte)ndefPayload.Length;

        var ndefMessage = new byte[]
        {
            0x03, // TLV type (NDEF Message)
            (byte)(3 + ndefPayload.Length), // TLV length
            header, typeLen, payloadLen, (byte)'U'
        };
        ndefMessage = ndefMessage.Concat(ndefPayload).Append((byte)0xFE).ToArray(); // + terminator

        var records = NdefProtocol.ParseNdefMessage(ndefMessage);

        records.Should().HaveCount(1);
        records[0].Tnf.Should().Be(NdefTnf.WellKnown);
        records[0].TypeString.Should().Be("U");
        records[0].GetUri().Should().Be("https://example.com");
    }

    [Fact]
    public void ParseNdefMessage_Empty_ReturnsEmpty()
    {
        var records = NdefProtocol.ParseNdefMessage(Array.Empty<byte>());
        records.Should().BeEmpty();
    }

    [Fact]
    public void ParseNdefMessage_TerminatorOnly_ReturnsEmpty()
    {
        var records = NdefProtocol.ParseNdefMessage(new byte[] { 0xFE });
        records.Should().BeEmpty();
    }

    [Fact]
    public void ParseNdefMessage_TextRecord_ParsesCorrectly()
    {
        var text = "Hello";
        var langCode = "en";
        var textBytes = Encoding.UTF8.GetBytes(text);
        var langBytes = Encoding.ASCII.GetBytes(langCode);

        // Text payload: status(1) + language(n) + text
        var ndefPayload = new byte[1 + langBytes.Length + textBytes.Length];
        ndefPayload[0] = (byte)langBytes.Length; // UTF-8, lang length
        Array.Copy(langBytes, 0, ndefPayload, 1, langBytes.Length);
        Array.Copy(textBytes, 0, ndefPayload, 1 + langBytes.Length, textBytes.Length);

        // NDEF record: MB=1,ME=1,SR=1,TNF=WellKnown
        byte header = 0xD1;
        var record = new byte[] { header, 1, (byte)ndefPayload.Length, (byte)'T' };
        var message = new byte[] { 0x03, (byte)(record.Length + ndefPayload.Length) };
        message = message.Concat(record).Concat(ndefPayload).Append((byte)0xFE).ToArray();

        var records = NdefProtocol.ParseNdefMessage(message);

        records.Should().HaveCount(1);
        var parsed = records[0].GetText();
        parsed.Should().NotBeNull();
        parsed!.Value.Language.Should().Be("en");
        parsed.Value.Text.Should().Be("Hello");
    }

    [Fact]
    public void Parse_AddsRecordsToTag()
    {
        // Minimal valid NDEF with terminator
        var data = new byte[] { 0x03, 0x00, 0xFE };
        var tag = new NfcTagData { TagType = NfcTagType.Ntag };

        _protocol.Parse(tag, data);

        tag.Metadata.Should().ContainKey("NdefRecordCount");
    }
}

using System.Text;
using FluentAssertions;
using Birko.Communication.NFC.Models;

namespace Birko.Communication.NFC.Tests;

public class NdefRecordTests
{
    [Fact]
    public void GetUri_HttpsWww_ReturnsFullUri()
    {
        var record = new NdefRecord
        {
            Tnf = NdefTnf.WellKnown,
            Type = new[] { (byte)'U' },
            Payload = BuildUriPayload(0x02, "google.com")
        };

        record.GetUri().Should().Be("https://www.google.com");
    }

    [Fact]
    public void GetUri_Http_ReturnsFullUri()
    {
        var record = new NdefRecord
        {
            Tnf = NdefTnf.WellKnown,
            Type = new[] { (byte)'U' },
            Payload = BuildUriPayload(0x03, "example.com")
        };

        record.GetUri().Should().Be("http://example.com");
    }

    [Fact]
    public void GetUri_Https_ReturnsFullUri()
    {
        var record = new NdefRecord
        {
            Tnf = NdefTnf.WellKnown,
            Type = new[] { (byte)'U' },
            Payload = BuildUriPayload(0x04, "example.com")
        };

        record.GetUri().Should().Be("https://example.com");
    }

    [Fact]
    public void GetUri_Tel_ReturnsFullUri()
    {
        var record = new NdefRecord
        {
            Tnf = NdefTnf.WellKnown,
            Type = new[] { (byte)'U' },
            Payload = BuildUriPayload(0x05, "+1234567890")
        };

        record.GetUri().Should().Be("tel:+1234567890");
    }

    [Fact]
    public void GetUri_Mailto_ReturnsFullUri()
    {
        var record = new NdefRecord
        {
            Tnf = NdefTnf.WellKnown,
            Type = new[] { (byte)'U' },
            Payload = BuildUriPayload(0x06, "test@example.com")
        };

        record.GetUri().Should().Be("mailto:test@example.com");
    }

    [Fact]
    public void GetUri_NonUriRecord_ReturnsNull()
    {
        var record = new NdefRecord
        {
            Tnf = NdefTnf.WellKnown,
            Type = new[] { (byte)'T' },
            Payload = new byte[] { 0x02, 0x65, 0x6E, 0x48, 0x69 }
        };

        record.GetUri().Should().BeNull();
    }

    [Fact]
    public void GetUri_WrongTnf_ReturnsNull()
    {
        var record = new NdefRecord
        {
            Tnf = NdefTnf.MimeMedia,
            Type = new[] { (byte)'U' },
            Payload = BuildUriPayload(0x03, "example.com")
        };

        record.GetUri().Should().BeNull();
    }

    [Fact]
    public void GetUri_EmptyPayload_ReturnsNull()
    {
        var record = new NdefRecord
        {
            Tnf = NdefTnf.WellKnown,
            Type = new[] { (byte)'U' },
            Payload = Array.Empty<byte>()
        };

        record.GetUri().Should().BeNull();
    }

    [Fact]
    public void GetText_Utf8English_ReturnsTextAndLanguage()
    {
        // Status byte: UTF-8 (bit 7=0), language length = 2
        var payload = new byte[] { 0x02, (byte)'e', (byte)'n' };
        payload = payload.Concat(Encoding.UTF8.GetBytes("Hello")).ToArray();

        var record = new NdefRecord
        {
            Tnf = NdefTnf.WellKnown,
            Type = new[] { (byte)'T' },
            Payload = payload
        };

        var result = record.GetText();
        result.Should().NotBeNull();
        result!.Value.Language.Should().Be("en");
        result.Value.Text.Should().Be("Hello");
    }

    [Fact]
    public void GetText_NonTextRecord_ReturnsNull()
    {
        var record = new NdefRecord
        {
            Tnf = NdefTnf.WellKnown,
            Type = new[] { (byte)'U' },
            Payload = new byte[] { 0x03, 0x65, 0x78 }
        };

        record.GetText().Should().BeNull();
    }

    [Fact]
    public void TypeString_ReturnsUtf8String()
    {
        var record = new NdefRecord
        {
            Type = Encoding.UTF8.GetBytes("Sp")
        };

        record.TypeString.Should().Be("Sp");
    }

    [Fact]
    public void ToString_ShowsTnfAndType()
    {
        var record = new NdefRecord
        {
            Tnf = NdefTnf.WellKnown,
            Type = new[] { (byte)'U' },
            Payload = new byte[10]
        };

        record.ToString().Should().Contain("WellKnown");
        record.ToString().Should().Contain("U");
        record.ToString().Should().Contain("10");
    }

    private static byte[] BuildUriPayload(byte prefix, string uri)
    {
        var uriBytes = Encoding.UTF8.GetBytes(uri);
        var payload = new byte[1 + uriBytes.Length];
        payload[0] = prefix;
        Array.Copy(uriBytes, 0, payload, 1, uriBytes.Length);
        return payload;
    }
}

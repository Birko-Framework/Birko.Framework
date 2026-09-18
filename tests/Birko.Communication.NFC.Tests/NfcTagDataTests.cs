using FluentAssertions;
using Birko.Communication.NFC.Models;

namespace Birko.Communication.NFC.Tests;

public class NfcTagDataTests
{
    [Fact]
    public void ToString_ShowsTypeAndUid()
    {
        var tag = new NfcTagData
        {
            TagType = NfcTagType.MifareClassic,
            Uid = "04A1B2C3"
        };

        tag.ToString().Should().Be("MifareClassic UID=04A1B2C3");
    }

    [Fact]
    public void GetFormattedUid_FromBytes_ColonSeparated()
    {
        var tag = new NfcTagData
        {
            UidBytes = new byte[] { 0x04, 0xA1, 0xB2, 0xC3 }
        };

        tag.GetFormattedUid().Should().Be("04:A1:B2:C3");
    }

    [Fact]
    public void GetFormattedUid_EmptyBytes_FallsBackToUid()
    {
        var tag = new NfcTagData
        {
            Uid = "04A1B2C3",
            UidBytes = Array.Empty<byte>()
        };

        tag.GetFormattedUid().Should().Be("04A1B2C3");
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var tag = new NfcTagData();

        tag.Uid.Should().BeEmpty();
        tag.UidBytes.Should().BeEmpty();
        tag.TagType.Should().Be(NfcTagType.Unknown);
        tag.Ats.Should().BeNull();
        tag.Sak.Should().BeNull();
        tag.Atqa.Should().BeNull();
        tag.NdefRecords.Should().BeEmpty();
        tag.Payload.Should().BeNull();
        tag.Metadata.Should().BeEmpty();
    }
}

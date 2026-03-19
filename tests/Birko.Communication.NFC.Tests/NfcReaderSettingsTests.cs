using FluentAssertions;
using Birko.Communication.NFC.Ports;

namespace Birko.Communication.NFC.Tests;

public class NfcReaderSettingsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var settings = new NfcReaderSettings();

        settings.TransportType.Should().Be("hid");
        settings.ConnectionString.Should().BeEmpty();
        settings.ReadTimeoutMs.Should().Be(5000);
        settings.PollingIntervalMs.Should().Be(250);
        settings.AutoReadNdef.Should().BeTrue();
        settings.AllowRepeatReads.Should().BeFalse();
    }

    [Fact]
    public void GetID_ReturnsFormattedString()
    {
        var settings = new NfcReaderSettings
        {
            Name = "TestReader",
            TransportType = "serial",
            ConnectionString = "COM3"
        };

        settings.GetID().Should().Be("NFC|TestReader|serial|COM3");
    }
}

using FluentAssertions;
using Birko.Security.NFC;

namespace Birko.Security.NFC.Tests;

public class NfcAuthSettingsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var settings = new NfcAuthSettings();

        settings.IssueTokens.Should().BeTrue();
        settings.TrackUsage.Should().BeTrue();
        settings.EnforceExpiration.Should().BeTrue();
        settings.MaxTagsPerUser.Should().Be(5);
        settings.NormalizeUids.Should().BeTrue();
    }
}

using FluentAssertions;
using Birko.Security.NFC;

namespace Birko.Security.NFC.Tests;

public class NfcTagMappingTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var mapping = new NfcTagMapping();

        mapping.Id.Should().NotBe(Guid.Empty);
        mapping.TagUid.Should().BeEmpty();
        mapping.UserId.Should().Be(Guid.Empty);
        mapping.IsActive.Should().BeTrue();
        mapping.LastUsedAt.Should().BeNull();
        mapping.Label.Should().BeNull();
        mapping.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public void IsExpired_NoExpiration_ReturnsFalse()
    {
        var mapping = new NfcTagMapping { ExpiresAt = null };
        mapping.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void IsExpired_FutureDate_ReturnsFalse()
    {
        var mapping = new NfcTagMapping { ExpiresAt = DateTime.UtcNow.AddDays(30) };
        mapping.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void IsExpired_PastDate_ReturnsTrue()
    {
        var mapping = new NfcTagMapping { ExpiresAt = DateTime.UtcNow.AddDays(-1) };
        mapping.IsExpired.Should().BeTrue();
    }
}

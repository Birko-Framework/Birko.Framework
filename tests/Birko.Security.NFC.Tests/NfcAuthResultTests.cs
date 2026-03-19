using FluentAssertions;
using Birko.Security.NFC;

namespace Birko.Security.NFC.Tests;

public class NfcAuthResultTests
{
    [Fact]
    public void Success_SetsAllFields()
    {
        var userId = Guid.NewGuid();
        var result = NfcAuthResult.Success(userId, "04A1B2C3", userName: "John", email: "john@test.com");

        result.IsAuthenticated.Should().BeTrue();
        result.UserId.Should().Be(userId);
        result.TagUid.Should().Be("04A1B2C3");
        result.UserName.Should().Be("John");
        result.Email.Should().Be("john@test.com");
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Failure_SetsErrorAndTagUid()
    {
        var result = NfcAuthResult.Failure("DEADBEEF", "Tag not registered");

        result.IsAuthenticated.Should().BeFalse();
        result.UserId.Should().BeNull();
        result.TagUid.Should().Be("DEADBEEF");
        result.Error.Should().Be("Tag not registered");
    }

    [Fact]
    public void Success_WithToken_IncludesToken()
    {
        var token = new TokenResult { Token = "jwt.token.here", ExpiresAt = DateTime.UtcNow.AddHours(1) };
        var result = NfcAuthResult.Success(Guid.NewGuid(), "04A1B2C3", token);

        result.Token.Should().NotBeNull();
        result.Token!.Token.Should().Be("jwt.token.here");
    }
}

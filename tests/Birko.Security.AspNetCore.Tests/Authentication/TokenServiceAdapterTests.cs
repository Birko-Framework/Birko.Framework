using FluentAssertions;
using Birko.Security;
using Birko.Security.AspNetCore;
using Birko.Security.Jwt;
using Xunit;

namespace Birko.Security.AspNetCore.Tests.Authentication;

public class TokenServiceAdapterTests
{
    private static readonly string TestSecret = "this-is-a-test-secret-key-that-is-at-least-32-characters-long!";

    private static TokenServiceAdapter CreateAdapter()
    {
        var tokenOptions = new TokenOptions
        {
            Secret = TestSecret,
            Issuer = "test-issuer",
            Audience = "test-audience",
            ExpirationMinutes = 60
        };
        var provider = new JwtTokenProvider(tokenOptions);
        var authOptions = new JwtAuthenticationOptions
        {
            Secret = TestSecret,
            Issuer = "test-issuer",
            Audience = "test-audience"
        };
        return new TokenServiceAdapter(provider, authOptions);
    }

    [Fact]
    public void GenerateAccessToken_ReturnsValidToken()
    {
        var adapter = CreateAdapter();
        var request = new TokenRequest(
            Guid.NewGuid(), "user@example.com", Guid.NewGuid(),
            new HashSet<string> { "Admin" }.AsReadOnly(),
            new HashSet<string> { "users.read" }.AsReadOnly());

        var result = adapter.GenerateAccessToken(request);

        result.Should().NotBeNull();
        result.Token.Should().NotBeNullOrEmpty();
        result.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public void GenerateAccessToken_ValidateToken_Roundtrip()
    {
        var adapter = CreateAdapter();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var request = new TokenRequest(
            userId, "user@example.com", tenantId,
            new HashSet<string> { "Admin", "User" }.AsReadOnly(),
            new HashSet<string> { "users.read", "users.write" }.AsReadOnly());

        var tokenResult = adapter.GenerateAccessToken(request);
        var info = adapter.ValidateToken(tokenResult.Token);

        info.IsValid.Should().BeTrue();
        info.UserId.Should().Be(userId);
        info.Email.Should().Be("user@example.com");
        info.TenantId.Should().Be(tenantId);
        info.Roles.Should().Contain("Admin").And.Contain("User");
        info.Permissions.Should().Contain("users.read").And.Contain("users.write");
    }

    [Fact]
    public void GenerateAccessToken_WithoutOptionalFields_Roundtrip()
    {
        var adapter = CreateAdapter();
        var userId = Guid.NewGuid();
        var request = new TokenRequest(userId, "user@example.com");

        var tokenResult = adapter.GenerateAccessToken(request);
        var info = adapter.ValidateToken(tokenResult.Token);

        info.IsValid.Should().BeTrue();
        info.UserId.Should().Be(userId);
        info.Email.Should().Be("user@example.com");
        info.TenantId.Should().BeNull();
    }

    [Fact]
    public void ValidateToken_InvalidToken_ReturnsInvalid()
    {
        var adapter = CreateAdapter();

        var info = adapter.ValidateToken("not-a-valid-jwt-token");

        info.IsValid.Should().BeFalse();
    }

    [Fact]
    public void GenerateRefreshToken_ReturnsNonEmptyString()
    {
        var adapter = CreateAdapter();

        var token = adapter.GenerateRefreshToken();

        token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateRefreshToken_ReturnsDifferentTokensEachCall()
    {
        var adapter = CreateAdapter();

        var token1 = adapter.GenerateRefreshToken();
        var token2 = adapter.GenerateRefreshToken();

        token1.Should().NotBe(token2);
    }
}

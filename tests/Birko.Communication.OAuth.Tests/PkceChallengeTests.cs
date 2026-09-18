using FluentAssertions;
using Xunit;

namespace Birko.Communication.OAuth.Tests;

public class PkceChallengeTests
{
    [Fact]
    public void Generate_ProducesNonEmptyVerifierAndChallenge()
    {
        var pkce = PkceChallenge.Generate();

        pkce.CodeVerifier.Should().NotBeNullOrEmpty();
        pkce.CodeChallenge.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Generate_ProducesUniquePairs()
    {
        var pkce1 = PkceChallenge.Generate();
        var pkce2 = PkceChallenge.Generate();

        pkce1.CodeVerifier.Should().NotBe(pkce2.CodeVerifier);
        pkce1.CodeChallenge.Should().NotBe(pkce2.CodeChallenge);
    }

    [Fact]
    public void CodeChallengeMethod_IsAlwaysS256()
    {
        var pkce = PkceChallenge.Generate();
        pkce.CodeChallengeMethod.Should().Be("S256");
    }

    [Fact]
    public void CodeVerifier_IsBase64UrlEncoded()
    {
        var pkce = PkceChallenge.Generate();

        // Base64url must not contain +, /, or =
        pkce.CodeVerifier.Should().NotContain("+");
        pkce.CodeVerifier.Should().NotContain("/");
        pkce.CodeVerifier.Should().NotContain("=");
    }

    [Fact]
    public void CodeChallenge_IsBase64UrlEncoded()
    {
        var pkce = PkceChallenge.Generate();

        pkce.CodeChallenge.Should().NotContain("+");
        pkce.CodeChallenge.Should().NotContain("/");
        pkce.CodeChallenge.Should().NotContain("=");
    }

    [Fact]
    public void CodeVerifier_HasCorrectLength()
    {
        var pkce = PkceChallenge.Generate();

        // 32 bytes -> ~43 chars in base64url (no padding)
        pkce.CodeVerifier.Length.Should().Be(43);
    }
}

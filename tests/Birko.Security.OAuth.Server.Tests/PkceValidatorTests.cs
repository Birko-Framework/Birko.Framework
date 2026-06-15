using Birko.Security.OAuth.Server.Internal;
using FluentAssertions;
using Xunit;

namespace Birko.Security.OAuth.Server.Tests;

public class PkceValidatorTests
{
    [Fact]
    public void Verify_S256_PositiveMatch()
    {
        // RFC 7636 Appendix B canonical vector
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        const string challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
        PkceValidator.Verify(verifier, challenge, PkceValidator.MethodS256).Should().BeTrue();
    }

    [Fact]
    public void Verify_S256_RejectsWrongVerifier()
    {
        const string challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
        PkceValidator.Verify("wrong-verifier", challenge, PkceValidator.MethodS256).Should().BeFalse();
    }

    [Fact]
    public void Verify_Plain_Match()
    {
        PkceValidator.Verify("abc", "abc", PkceValidator.MethodPlain).Should().BeTrue();
    }

    [Fact]
    public void Verify_UnknownMethod_ReturnsFalse()
    {
        PkceValidator.Verify("abc", "abc", "SHA512").Should().BeFalse();
    }

    [Fact]
    public void Verify_EmptyInputs_ReturnsFalse()
    {
        PkceValidator.Verify("", "challenge", PkceValidator.MethodS256).Should().BeFalse();
        PkceValidator.Verify("verifier", "", PkceValidator.MethodS256).Should().BeFalse();
    }
}

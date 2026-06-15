using Birko.Security.OAuth.Server.Internal;
using FluentAssertions;
using Xunit;

namespace Birko.Security.OAuth.Server.Tests;

public class RandomStringGeneratorTests
{
    [Fact]
    public void Base64Url_HasNoPadding()
    {
        var value = RandomStringGenerator.Base64Url(32);
        value.Should().NotContain("=").And.NotContain("+").And.NotContain("/");
    }

    [Fact]
    public void Base64Url_IsUnique()
    {
        var a = RandomStringGenerator.Base64Url(32);
        var b = RandomStringGenerator.Base64Url(32);
        a.Should().NotBe(b);
    }

    [Fact]
    public void UserCode_UsesUnambiguousAlphabet()
    {
        for (var i = 0; i < 50; i++)
        {
            var code = RandomStringGenerator.UserCode(8);
            code.Should().HaveLength(8);
            code.Should().NotContain("0").And.NotContain("O");
            code.Should().NotContain("1").And.NotContain("I").And.NotContain("L");
        }
    }
}

using Birko.Security.Authentication;
using FluentAssertions;
using Xunit;

namespace Birko.Security.Tests;

/// <summary>TASK-709: the one token-extraction rule the REST, SSE and SOAP adapters share.</summary>
public class Task709TokenExtractionTests
{
    [Theory]
    [InlineData("Bearer abc", "abc")]
    [InlineData("bearer abc", "abc")]
    [InlineData("BEARER  abc ", "abc")]
    [InlineData("Basic dXNlcjpwYXNz", null)]
    [InlineData("abc", null)]
    [InlineData("Bearerabc", null)]
    [InlineData("Bearer ", null)]
    [InlineData("Bearer    ", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void A_bearer_token_is_read_and_anything_else_is_not_a_token(string? header, string? token)
    {
        AuthenticationService.TokenFromAuthorizationHeader(header).Should().Be(token);
    }

    [Fact]
    public void The_scheme_is_configurable()
    {
        AuthenticationService.TokenFromAuthorizationHeader("Token abc", "Token").Should().Be("abc");
        AuthenticationService.TokenFromAuthorizationHeader("Bearer abc", "Token").Should().BeNull();
    }

    [Theory]
    [InlineData("?token=abc", "abc")]
    [InlineData("token=abc", "abc")]
    [InlineData("?a=1&token=abc==&b=2", "abc==")]
    [InlineData("?token=a%2Bb", "a+b")]
    [InlineData("?token=first&token=second", "first")]
    [InlineData("?Token=abc", null)]
    [InlineData("?tokenx=abc", null)]
    [InlineData("?=abc", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void The_query_token_is_read_by_its_exact_name(string? query, string? token)
    {
        AuthenticationService.TokenFromQueryString(query, "token").Should().Be(token);
    }
}

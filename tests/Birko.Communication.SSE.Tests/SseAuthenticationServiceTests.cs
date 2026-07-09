using System.Reflection;
using Birko.Communication.SSE.Middleware;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.SSE.Tests;

/// <summary>
/// CR-M069: ExtractTokenFromQueryString now TrimStart('?')s the raw query (so a leading '?' does
/// not turn the first key into "?token") and splits each pair with a limit of 2 (so a value that
/// itself contains '=', e.g. base64 '=' padding, is preserved). Both previously caused spurious
/// 401s. The method is private, so it is exercised via reflection against a real service instance.
/// </summary>
public class SseAuthenticationServiceTests
{
    private static string? ExtractQueryToken(SseAuthenticationConfiguration config, string queryString)
    {
        using var service = new SseAuthenticationService(config);
        var method = typeof(SseAuthenticationService).GetMethod(
            "ExtractTokenFromQueryString",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull("the private query-token parser must exist to test CR-M069");
        return (string?)method!.Invoke(service, new object[] { queryString });
    }

    [Fact]
    public void ExtractTokenFromQueryString_StripsLeadingQuestionMark()
    {
        var config = new SseAuthenticationConfiguration(); // QueryTokenName defaults to "token"

        ExtractQueryToken(config, "?token=abc").Should().Be("abc");
    }

    [Fact]
    public void ExtractTokenFromQueryString_PreservesEqualsInValue()
    {
        var config = new SseAuthenticationConfiguration();

        // base64 '=' padding in the value must survive the split(limit 2).
        ExtractQueryToken(config, "?token=abc==").Should().Be("abc==");
    }

    [Fact]
    public void ExtractTokenFromQueryString_WorksWithoutLeadingQuestionMark()
    {
        var config = new SseAuthenticationConfiguration();

        ExtractQueryToken(config, "token=abc").Should().Be("abc");
    }

    [Fact]
    public void ExtractTokenFromQueryString_HonoursConfiguredTokenName_AmongOtherPairs()
    {
        var config = new SseAuthenticationConfiguration { QueryTokenName = "access_token" };

        ExtractQueryToken(config, "?foo=1&access_token=xyz==&bar=2").Should().Be("xyz==");
    }
}

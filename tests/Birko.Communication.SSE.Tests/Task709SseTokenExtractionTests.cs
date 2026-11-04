using System.Collections.Generic;
using Birko.Communication.SSE.Middleware;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.SSE.Tests;

/// <summary>
/// TASK-709. Measured before the fix: an <c>Authorization</c> header with another scheme (a proxy's <c>Basic</c>) ended
/// the search with no token, so a valid <c>?token=</c> was never read and the connection was refused. The query-token
/// name matched in any case, where REST and SOAP match it exactly.
/// </summary>
public class Task709SseTokenExtractionTests
{
    private static SseAuthenticationService Service() => new(new SseAuthenticationConfiguration
    {
        Enabled = true,
        Tokens = new List<string> { "good" },
    });

    [Fact]
    public void An_Authorization_header_of_another_scheme_does_not_hide_the_query_token()
    {
        using var service = Service();

        var result = service.AuthenticateConnection(new Dictionary<string, string> { ["Authorization"] = "Basic dXNlcjpwYXNz" }, "?token=good", "10.0.0.1");

        result.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void A_bearer_token_is_still_read()
    {
        using var service = Service();

        service.AuthenticateConnection(new Dictionary<string, string> { ["Authorization"] = "Bearer good" }, null, "10.0.0.1")
            .IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void The_query_token_name_matches_exactly_as_on_REST_and_SOAP()
    {
        using var service = Service();

        service.AuthenticateConnection(null, "?Token=good", "10.0.0.1").IsAuthenticated.Should().BeFalse();
        service.AuthenticateConnection(null, "?token=good", "10.0.0.1").IsAuthenticated.Should().BeTrue();
    }
}

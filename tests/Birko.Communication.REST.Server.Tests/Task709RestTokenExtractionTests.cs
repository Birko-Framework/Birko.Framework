using System.Collections.Generic;
using Birko.Communication.REST;
using Birko.Communication.REST.Middleware;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.REST.Server.Tests;

/// <summary>
/// TASK-709. Measured before the fix: an <c>Authorization</c> value that is not a bearer token (<c>Basic …</c>) was
/// passed whole to the validator as the token, and the API-key header and query token were never read.
/// </summary>
public class Task709RestTokenExtractionTests
{
    private static RestAuthenticationService Service() => new(new RestAuthenticationConfiguration { Enabled = true, Tokens = new List<string> { "good" } });

    private static RestRequest Request(Dictionary<string, string>? headers = null, Dictionary<string, string>? query = null)
        => new() { Headers = headers ?? new(), QueryParameters = query ?? new() };

    [Fact]
    public void A_basic_credential_is_not_a_token()
    {
        using var service = Service();

        service.ExtractTokenFromRequest(Request(new() { ["Authorization"] = "Basic dXNlcjpwYXNz" })).Should().BeNull();
    }

    [Fact]
    public void Another_scheme_falls_through_to_the_api_key_and_the_query_token()
    {
        using var service = Service();

        service.ExtractTokenFromRequest(Request(new() { ["Authorization"] = "Basic x", ["X-API-Key"] = "key" })).Should().Be("key");
        service.ExtractTokenFromRequest(Request(new() { ["Authorization"] = "Basic x" }, new() { ["token"] = "good" })).Should().Be("good");
    }

    [Fact]
    public void A_bearer_token_is_read_without_its_scheme()
    {
        using var service = Service();

        service.ExtractTokenFromRequest(Request(new() { ["Authorization"] = "bearer good" })).Should().Be("good");
    }
}

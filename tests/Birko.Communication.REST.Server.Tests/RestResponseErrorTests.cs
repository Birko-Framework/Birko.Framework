using System.Text.Json;
using Birko.Communication.REST;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.REST.Server.Tests;

/// <summary>
/// Regression for CR-H032: the RestResponse error factories interpolated the message straight into
/// JSON with no escaping, so a message containing a quote/backslash/newline (e.g. an exception
/// message or echoed user input) produced malformed JSON / a JSON-injection vector. They now route
/// through the shared EscapeJson helper.
/// </summary>
public class RestResponseErrorTests
{
    public static IEnumerable<object[]> Factories => new[]
    {
        new object[] { "BadRequest", (System.Func<string, RestResponse>)RestResponse.BadRequest },
        new object[] { "Unauthorized", (System.Func<string, RestResponse>)RestResponse.Unauthorized },
        new object[] { "Forbidden", (System.Func<string, RestResponse>)RestResponse.Forbidden },
        new object[] { "NotFound", (System.Func<string, RestResponse>)RestResponse.NotFound },
        new object[] { "InternalServerError", (System.Func<string, RestResponse>)RestResponse.InternalServerError },
    };

    [Theory]
    [MemberData(nameof(Factories))]
    public void ErrorFactory_EscapesMessage_IntoValidJson(string name, System.Func<string, RestResponse> factory)
    {
        // A message with characters that would break unescaped JSON.
        const string message = "bad \"quote\" and \\slash\\ and\nnewline";

        var response = factory(message);

        // Must be well-formed JSON with the original message preserved after unescaping.
        var act = () => JsonDocument.Parse(response.Content!);
        act.Should().NotThrow($"{name} must produce valid JSON");

        using var doc = JsonDocument.Parse(response.Content!);
        doc.RootElement.GetProperty("error").GetString().Should().Be(message);
    }

    [Fact]
    public void InternalServerError_WithExceptionMessage_IsValidJson()
    {
        var ex = new System.InvalidOperationException("boom: \"x\" \\ \t done");

        var response = RestResponse.InternalServerError(ex.Message);

        using var doc = JsonDocument.Parse(response.Content!);
        doc.RootElement.GetProperty("error").GetString().Should().Be(ex.Message);
    }
}

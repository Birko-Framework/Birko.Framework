using Birko.Communication.SSE.Middleware;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.SSE.Tests;

/// <summary>
/// CR-M068: SseResponse.Denied now carries its human-readable reason on a new Reason property,
/// so the host can emit it in the response — previously Denied() dropped its reason argument.
/// </summary>
public class SseResponseTests
{
    [Fact]
    public void Denied_CarriesReason_StatusCode_AndDisallowsConnection()
    {
        var response = SseResponse.Denied(403, "nope");

        response.Reason.Should().Be("nope");
        response.AllowConnection.Should().BeFalse();
        response.StatusCode.Should().Be(403);
    }

    [Fact]
    public void Denied_DefaultsToUnauthorizedWithNoReason()
    {
        var response = SseResponse.Denied();

        response.AllowConnection.Should().BeFalse();
        response.StatusCode.Should().Be(401);
        response.Reason.Should().BeNull();
    }
}

using Birko.Security.OAuth.Server.Endpoints.DeviceAuthorization;
using Birko.Security.OAuth.Server.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Security.OAuth.Server.Tests;

public class DeviceAuthorizationHandlerTests
{
    [Fact]
    public async Task RequestDeviceAuthorization_IssuesCodes()
    {
        var fixture = new TestServer();
        fixture.RegisterConfidentialClient("c1", "s1", OAuthGrantTypes.DeviceCode);

        var response = await fixture.Server.DeviceAuthorization.HandleAsync(new DeviceAuthorizationRequest
        {
            ClientId = "c1",
            Scope = "read"
        });

        response.DeviceCode.Should().NotBeNullOrWhiteSpace();
        response.UserCode.Should().NotBeNullOrWhiteSpace();
        response.VerificationUri.Should().Be("https://auth.test/device");
        response.VerificationUriComplete.Should().Contain(response.UserCode);
        response.Interval.Should().Be(fixture.Settings.DeviceCodePollingIntervalSeconds);
        response.ExpiresIn.Should().Be(fixture.Settings.DeviceCodeLifetimeSeconds);
    }

    [Fact]
    public async Task Approve_SetsAuthorizedStatusAndUserId()
    {
        var fixture = new TestServer();
        fixture.RegisterConfidentialClient("c1", "s1", OAuthGrantTypes.DeviceCode);
        var response = await fixture.Server.DeviceAuthorization.HandleAsync(new DeviceAuthorizationRequest
        {
            ClientId = "c1",
            Scope = "read"
        });

        await fixture.Server.DeviceAuthorization.ApproveAsync(response.UserCode, userId: "user-7", approved: true);

        var stored = await fixture.Devices.GetByUserCodeAsync(response.UserCode);
        stored.Should().NotBeNull();
        stored!.Status.Should().Be(DeviceCodeStatus.Authorized);
        stored.UserId.Should().Be("user-7");
    }

    [Fact]
    public async Task Approve_DeniedClearsUserId()
    {
        var fixture = new TestServer();
        fixture.RegisterConfidentialClient("c1", "s1", OAuthGrantTypes.DeviceCode);
        var response = await fixture.Server.DeviceAuthorization.HandleAsync(new DeviceAuthorizationRequest
        {
            ClientId = "c1"
        });

        await fixture.Server.DeviceAuthorization.ApproveAsync(response.UserCode, userId: "user-7", approved: false);

        var stored = await fixture.Devices.GetByUserCodeAsync(response.UserCode);
        stored!.Status.Should().Be(DeviceCodeStatus.Denied);
        stored.UserId.Should().BeNull();
    }

    [Fact]
    public async Task UnauthorizedClientForDeviceGrant_Throws()
    {
        var fixture = new TestServer();
        // Client allowed for ClientCredentials only — not DeviceCode
        fixture.RegisterConfidentialClient("c1", "s1", OAuthGrantTypes.ClientCredentials);

        var act = async () => await fixture.Server.DeviceAuthorization.HandleAsync(new DeviceAuthorizationRequest
        {
            ClientId = "c1"
        });

        (await act.Should().ThrowAsync<OAuthServerException>())
            .Which.ErrorCode.Should().Be(OAuthErrorCodes.UnauthorizedClient);
    }
}

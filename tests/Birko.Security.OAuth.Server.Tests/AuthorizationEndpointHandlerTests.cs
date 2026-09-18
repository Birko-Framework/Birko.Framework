using Birko.Security.OAuth.Server.Endpoints.Authorize;
using Birko.Security.OAuth.Server.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Security.OAuth.Server.Tests;

public class AuthorizationEndpointHandlerTests
{
    [Fact]
    public async Task Authorize_FirstTime_RequiresConsent()
    {
        var fixture = new TestServer();
        fixture.RegisterConfidentialClient("c1", "s1", OAuthGrantTypes.AuthorizationCode);

        var response = await fixture.Server.Authorize.HandleAuthorizeAsync(new AuthorizeRequest
        {
            ResponseType = OAuthResponseTypes.Code,
            ClientId = "c1",
            RedirectUri = "https://app.test/callback",
            Scope = "read"
        }, userId: "user-1");

        response.RequiresConsent.Should().BeTrue();
        response.Code.Should().BeNull();
    }

    [Fact]
    public async Task Authorize_PriorConsent_IssuesCode()
    {
        var fixture = new TestServer();
        fixture.RegisterConfidentialClient("c1", "s1", OAuthGrantTypes.AuthorizationCode);
        await fixture.Consents.CreateAsync(new ConsentRecord
        {
            UserId = "user-1",
            ClientId = "c1",
            Scope = "read write"
        });

        var response = await fixture.Server.Authorize.HandleAuthorizeAsync(new AuthorizeRequest
        {
            ResponseType = OAuthResponseTypes.Code,
            ClientId = "c1",
            RedirectUri = "https://app.test/callback",
            Scope = "read",
            State = "xyz"
        }, userId: "user-1");

        response.RequiresConsent.Should().BeFalse();
        response.Code.Should().NotBeNullOrEmpty();
        response.State.Should().Be("xyz");
        response.RedirectUri.Should().Be("https://app.test/callback");
    }

    [Fact]
    public async Task Consent_Approved_RecordsConsentAndIssuesCode()
    {
        var fixture = new TestServer();
        fixture.RegisterConfidentialClient("c1", "s1", OAuthGrantTypes.AuthorizationCode);

        var response = await fixture.Server.Authorize.HandleConsentAsync(new AuthorizeRequest
        {
            ResponseType = OAuthResponseTypes.Code,
            ClientId = "c1",
            RedirectUri = "https://app.test/callback",
            Scope = "read"
        }, userId: "user-1", approved: true);

        response.Code.Should().NotBeNullOrEmpty();
        var stored = await fixture.Consents.GetAsync("user-1", "c1");
        stored.Should().NotBeNull();
        stored!.Scope.Should().Be("read");
    }

    [Fact]
    public async Task Consent_Denied_ThrowsAccessDenied()
    {
        var fixture = new TestServer();
        fixture.RegisterConfidentialClient("c1", "s1", OAuthGrantTypes.AuthorizationCode);

        var act = async () => await fixture.Server.Authorize.HandleConsentAsync(new AuthorizeRequest
        {
            ResponseType = OAuthResponseTypes.Code,
            ClientId = "c1",
            RedirectUri = "https://app.test/callback",
            Scope = "read"
        }, userId: "user-1", approved: false);

        (await act.Should().ThrowAsync<OAuthServerException>())
            .Which.ErrorCode.Should().Be(OAuthErrorCodes.AccessDenied);
    }

    [Fact]
    public async Task Authorize_UnknownRedirectUri_Throws()
    {
        var fixture = new TestServer();
        fixture.RegisterConfidentialClient("c1", "s1", OAuthGrantTypes.AuthorizationCode);

        var act = async () => await fixture.Server.Authorize.HandleAuthorizeAsync(new AuthorizeRequest
        {
            ResponseType = OAuthResponseTypes.Code,
            ClientId = "c1",
            RedirectUri = "https://attacker.test/callback",
            Scope = "read"
        }, userId: "user-1");

        (await act.Should().ThrowAsync<OAuthServerException>())
            .Which.ErrorCode.Should().Be(OAuthErrorCodes.InvalidRequest);
    }

    [Fact]
    public async Task Authorize_PublicClientWithoutPkce_Throws()
    {
        var fixture = new TestServer();
        fixture.RegisterPublicClient("c1", OAuthGrantTypes.AuthorizationCode);

        var act = async () => await fixture.Server.Authorize.HandleAuthorizeAsync(new AuthorizeRequest
        {
            ResponseType = OAuthResponseTypes.Code,
            ClientId = "c1",
            RedirectUri = "https://app.test/callback",
            Scope = "read"
            // CodeChallenge intentionally omitted
        }, userId: "user-1");

        (await act.Should().ThrowAsync<OAuthServerException>())
            .Which.ErrorCode.Should().Be(OAuthErrorCodes.InvalidRequest);
    }
}

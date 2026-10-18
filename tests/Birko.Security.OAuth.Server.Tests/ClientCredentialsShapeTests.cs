using System.IdentityModel.Tokens.Jwt;
using Birko.Security.OAuth.Server.Endpoints.Token;
using FluentAssertions;
using Xunit;

namespace Birko.Security.OAuth.Server.Tests;

/// <summary>
/// TASK-520: <see cref="TokenEndpointHandler.ShapeClientCredentialsToken"/> lets the app name a service account's token
/// (subject and extra claims) without replacing the grant, as DraCode's ServiceAccountTokenIssuer did.
/// </summary>
public class ClientCredentialsShapeTests
{
    private static Task<TokenResponse> Request(TestServer fixture, string secret = "s1") =>
        fixture.Server.Token.HandleAsync(new TokenRequest
        {
            GrantType = OAuthGrantTypes.ClientCredentials, ClientId = "billing", ClientSecret = secret, Scope = "read write",
        });

    private static JwtSecurityToken Read(TokenResponse response) => new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken);

    [Fact]
    public async Task The_hook_sets_the_subject_and_extra_claims()
    {
        var fixture = new TestServer();
        fixture.RegisterConfidentialClient("billing", "s1", OAuthGrantTypes.ClientCredentials);
        fixture.Server.Token.ShapeClientCredentialsToken = (ctx, _) =>
        {
            ctx.Scope.Should().Be("read write");
            ctx.Subject = $"service:{ctx.Client.Name}";
            ctx.Claims["name"] = ctx.Client.Name;
            return Task.CompletedTask;
        };

        var token = Read(await Request(fixture));

        token.Subject.Should().Be("service:billing");
        token.Claims.Should().Contain(c => c.Type == "name" && c.Value == "billing");
        token.Claims.Should().Contain(c => c.Type == "scope" && c.Value == "read write", "the scope stays the grant's");
        token.Claims.Should().Contain(c => c.Type == "client_id" && c.Value == "billing");
    }

    [Fact]
    public async Task Without_a_hook_the_subject_is_the_client_id()
    {
        var fixture = new TestServer();
        fixture.RegisterConfidentialClient("billing", "s1", OAuthGrantTypes.ClientCredentials);

        Read(await Request(fixture)).Subject.Should().Be("billing");
    }

    [Fact]
    public async Task The_hook_runs_only_for_an_authenticated_client()
    {
        var fixture = new TestServer();
        fixture.RegisterConfidentialClient("billing", "s1", OAuthGrantTypes.ClientCredentials);
        var called = false;
        fixture.Server.Token.ShapeClientCredentialsToken = (_, _) => { called = true; return Task.CompletedTask; };

        await FluentActions.Awaiting(() => Request(fixture, "wrong")).Should().ThrowAsync<OAuthServerException>();
        called.Should().BeFalse();
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("sub")]
    [InlineData("exp")]
    public async Task A_registered_claim_from_the_hook_is_refused_not_dropped(string claim)
    {
        var fixture = new TestServer();
        fixture.RegisterConfidentialClient("billing", "s1", OAuthGrantTypes.ClientCredentials);
        fixture.Server.Token.ShapeClientCredentialsToken = (ctx, _) => { ctx.Claims[claim] = "x"; return Task.CompletedTask; };

        await FluentActions.Awaiting(() => Request(fixture)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task An_empty_subject_is_refused()
    {
        var fixture = new TestServer();
        fixture.RegisterConfidentialClient("billing", "s1", OAuthGrantTypes.ClientCredentials);
        fixture.Server.Token.ShapeClientCredentialsToken = (ctx, _) => { ctx.Subject = ""; return Task.CompletedTask; };

        await FluentActions.Awaiting(() => Request(fixture)).Should().ThrowAsync<InvalidOperationException>();
    }
}

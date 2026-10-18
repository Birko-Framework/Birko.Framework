using Birko.Security.OAuth.Server.Endpoints.Token;
using Birko.Security.OAuth.Server.Internal;
using FluentAssertions;
using Xunit;

namespace Birko.Security.OAuth.Server.Tests;

/// <summary>
/// TASK-517: <see cref="TokenEndpointHandler.IssueForSubjectAsync"/> issues a token pair for a subject that signed in
/// elsewhere (DraCode's GitHub sign-in), and that pair renews through the ordinary <c>refresh_token</c> grant.
/// </summary>
public class FederatedIssueTests
{
    private static readonly Dictionary<string, string> Profile = new()
    {
        ["name"] = "octocat",
        ["roles"] = "admin,user",
    };

    private static TestServer NewServer()
    {
        var fixture = new TestServer();
        fixture.RegisterPublicClient("web", OAuthGrantTypes.RefreshToken);
        return fixture;
    }

    private static IDictionary<string, string> ClaimsOf(TestServer fixture, string accessToken)
    {
        var validation = fixture.TokenProvider.ValidateToken(accessToken, new TokenOptions
        {
            Secret = fixture.TokenOptions.Secret,
            Issuer = fixture.Settings.GetIssuer(),
            Audience = fixture.TokenOptions.Audience,
        });
        validation.IsValid.Should().BeTrue();
        // The raw payload: validation applies the inbound claim-type mapping (sub becomes NameIdentifier).
        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(accessToken).Claims
            .GroupBy(c => c.Type).ToDictionary(g => g.Key, g => string.Join(";", g.Select(c => c.Value)));
    }

    private static Task<TokenResponse> Refresh(TestServer fixture, string refreshToken) =>
        fixture.Server.Token.HandleAsync(new TokenRequest
        {
            GrantType = OAuthGrantTypes.RefreshToken,
            ClientId = "web",
            RefreshToken = refreshToken,
        });

    [Fact]
    public async Task Issue_gives_a_token_pair_for_a_string_subject_with_the_extra_claims()
    {
        var fixture = NewServer();

        var issued = await fixture.Server.Token.IssueForSubjectAsync("web", "github:42", "read", Profile);

        issued.RefreshToken.Should().NotBeNullOrEmpty();
        issued.Scope.Should().Be("read");
        var claims = ClaimsOf(fixture, issued.AccessToken);
        claims["sub"].Should().Be("github:42");
        claims["name"].Should().Be("octocat");
        claims["roles"].Should().Be("admin,user");
        claims["scope"].Should().Be("read");
    }

    [Fact]
    public async Task Refresh_rotates_and_keeps_the_extra_claims()
    {
        var fixture = NewServer();
        var issued = await fixture.Server.Token.IssueForSubjectAsync("web", "github:42", "read write", Profile);

        var refreshed = await Refresh(fixture, issued.RefreshToken!);

        refreshed.RefreshToken.Should().NotBe(issued.RefreshToken, "rotation is on");
        var claims = ClaimsOf(fixture, refreshed.AccessToken);
        claims["sub"].Should().Be("github:42");
        claims["name"].Should().Be("octocat");
        claims["roles"].Should().Be("admin,user");

        var again = await Refresh(fixture, refreshed.RefreshToken!);
        ClaimsOf(fixture, again.AccessToken)["name"].Should().Be("octocat", "the claims travel with each rotated record");
    }

    [Fact]
    public async Task Without_rotation_a_refresh_keeps_the_extra_claims_too()
    {
        var fixture = NewServer();
        fixture.Settings.RotateRefreshTokens = false;
        var issued = await fixture.Server.Token.IssueForSubjectAsync("web", "github:42", null, Profile);

        var refreshed = await Refresh(fixture, issued.RefreshToken!);

        refreshed.RefreshToken.Should().Be(issued.RefreshToken);
        ClaimsOf(fixture, refreshed.AccessToken)["roles"].Should().Be("admin,user");
    }

    [Fact]
    public async Task A_reused_refresh_token_revokes_the_family()
    {
        var fixture = NewServer();
        var issued = await fixture.Server.Token.IssueForSubjectAsync("web", "github:42", "read", Profile);
        var refreshed = await Refresh(fixture, issued.RefreshToken!);

        await FluentActions.Awaiting(() => Refresh(fixture, issued.RefreshToken!))
            .Should().ThrowAsync<OAuthServerException>().Where(e => e.ErrorCode == OAuthErrorCodes.InvalidGrant);
        await FluentActions.Awaiting(() => Refresh(fixture, refreshed.RefreshToken!))
            .Should().ThrowAsync<OAuthServerException>("reuse of a rotated token revokes the whole family");
    }

    [Theory]
    [InlineData("sub")]
    [InlineData("scope")]
    [InlineData("iss")]
    [InlineData("client_id")]
    [InlineData("exp")]
    public async Task A_registered_claim_cannot_be_supplied(string claim)
    {
        var fixture = NewServer();

        await FluentActions.Awaiting(() => fixture.Server.Token.IssueForSubjectAsync("web", "github:42", "read",
                new Dictionary<string, string> { [claim] = "x" }))
            .Should().ThrowAsync<ArgumentException>();
        (await ((InMemoryRefreshTokenStore)fixture.Refreshes).CountAsync()).Should().Be(0, "nothing is issued");
    }

    [Fact]
    public async Task A_client_that_cannot_refresh_is_refused()
    {
        var fixture = new TestServer();
        fixture.RegisterConfidentialClient("svc", "s", OAuthGrantTypes.ClientCredentials);

        await FluentActions.Awaiting(() => fixture.Server.Token.IssueForSubjectAsync("svc", "github:42"))
            .Should().ThrowAsync<OAuthServerException>().Where(e => e.ErrorCode == OAuthErrorCodes.UnauthorizedClient);
    }

    [Fact]
    public async Task An_unknown_client_is_refused()
    {
        var fixture = NewServer();

        await FluentActions.Awaiting(() => fixture.Server.Token.IssueForSubjectAsync("nobody", "github:42"))
            .Should().ThrowAsync<OAuthServerException>().Where(e => e.ErrorCode == OAuthErrorCodes.InvalidClient);
    }

    [Fact]
    public async Task A_scope_outside_the_clients_is_refused()
    {
        var fixture = NewServer();

        await FluentActions.Awaiting(() => fixture.Server.Token.IssueForSubjectAsync("web", "github:42", "admin"))
            .Should().ThrowAsync<OAuthServerException>().Where(e => e.ErrorCode == OAuthErrorCodes.InvalidScope);
    }
}

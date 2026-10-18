using Birko.Security.OAuth.Server.Endpoints.Token;
using Birko.Security.OAuth.Server.Models;
using Birko.Security.OAuth.Server.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Security.OAuth.Server.Tests;

/// <summary>
/// TASK-518, decided by the user: when a refresh loses the rotation claim to a concurrent refresh of the same token
/// (two tabs refreshing together), only the loser fails. The winner's new pair stays valid. A token presented after its
/// rotation is still reuse and revokes the family (pinned by <see cref="FederatedIssueTests"/>).
/// </summary>
public class RefreshRaceTests
{
    /// <summary>A refresh store in which every rotation claim is lost: another request rotated the token first.</summary>
    private sealed class LosingClaimStore : InMemoryRefreshTokenStore, IRefreshTokenStore
    {
        Task<bool> IRefreshTokenStore.TryRevokeAsync(RefreshTokenRecord record, CancellationToken ct) => Task.FromResult(false);
    }

    [Fact]
    public async Task Losing_the_rotation_claim_fails_only_that_request()
    {
        var fixture = new TestServer();
        fixture.RegisterPublicClient("web", OAuthGrantTypes.RefreshToken);
        var refreshes = new LosingClaimStore();
        var server = new OAuthServer(fixture.Settings, fixture.TokenProvider, fixture.TokenOptions,
            fixture.Clients, fixture.Codes, refreshes, fixture.Devices, fixture.Consents, "https://auth.test/device", fixture.Clock);

        var loser = await server.Token.IssueForSubjectAsync("web", "u", "read");
        var winnersPair = await server.Token.IssueForSubjectAsync("web", "u", "read"); // stands for the winner's new pair

        await FluentActions.Awaiting(() => server.Token.HandleAsync(new TokenRequest
            {
                GrantType = OAuthGrantTypes.RefreshToken, ClientId = "web", RefreshToken = loser.RefreshToken,
            }))
            .Should().ThrowAsync<OAuthServerException>().Where(e => e.ErrorCode == OAuthErrorCodes.InvalidGrant);

        var record = await ((IRefreshTokenStore)refreshes).GetByHashAsync(Internal.ClientSecretHasher.Hash(winnersPair.RefreshToken!));
        record!.Revoked.Should().BeFalse("losing the race is not reuse: the winner's tokens are not revoked");
    }
}

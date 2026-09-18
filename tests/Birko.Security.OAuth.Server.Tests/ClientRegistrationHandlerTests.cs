using Birko.Security.OAuth.Server.Endpoints.ClientRegistration;
using Birko.Security.OAuth.Server.Internal;
using FluentAssertions;
using Xunit;

namespace Birko.Security.OAuth.Server.Tests;

public class ClientRegistrationHandlerTests
{
    [Fact]
    public async Task Register_Confidential_ReturnsPlaintextSecretAndStoresHash()
    {
        var fixture = new TestServer();
        var response = await fixture.Server.ClientRegistration.RegisterAsync(new ClientRegistrationRequest
        {
            Name = "My App",
            ClientType = OAuthClientType.Confidential,
            RedirectUris = { "https://app.test/cb" },
            AllowedGrantTypes = { OAuthGrantTypes.ClientCredentials },
            AllowedScopes = { "read" }
        });

        response.ClientId.Should().NotBeNullOrWhiteSpace();
        response.ClientSecret.Should().NotBeNullOrWhiteSpace();
        var stored = await fixture.Clients.GetByClientIdAsync(response.ClientId);
        stored.Should().NotBeNull();
        stored!.ClientSecretHash.Should().NotBe(response.ClientSecret); // never plaintext
        ClientSecretHasher.Verify(response.ClientSecret!, stored.ClientSecretHash!).Should().BeTrue();
    }

    [Fact]
    public async Task Register_Public_ReturnsNoSecret()
    {
        var fixture = new TestServer();
        var response = await fixture.Server.ClientRegistration.RegisterAsync(new ClientRegistrationRequest
        {
            Name = "Public",
            ClientType = OAuthClientType.Public,
            RedirectUris = { "https://app.test/cb" },
            AllowedGrantTypes = { OAuthGrantTypes.AuthorizationCode },
            AllowedScopes = { "read" }
        });

        response.ClientSecret.Should().BeNull();
        (await fixture.Clients.GetByClientIdAsync(response.ClientId))!.ClientSecretHash.Should().BeNull();
    }

    [Fact]
    public async Task Register_AuthorizationCodeWithoutRedirectUri_Throws()
    {
        var fixture = new TestServer();
        var act = async () => await fixture.Server.ClientRegistration.RegisterAsync(new ClientRegistrationRequest
        {
            Name = "X",
            ClientType = OAuthClientType.Confidential,
            AllowedGrantTypes = { OAuthGrantTypes.AuthorizationCode }
        });
        (await act.Should().ThrowAsync<OAuthServerException>())
            .Which.ErrorCode.Should().Be(OAuthErrorCodes.InvalidRequest);
    }

    [Fact]
    public async Task Get_OmitsSecret()
    {
        var fixture = new TestServer();
        var registered = await fixture.Server.ClientRegistration.RegisterAsync(new ClientRegistrationRequest
        {
            Name = "X",
            ClientType = OAuthClientType.Confidential,
            RedirectUris = { "https://app.test/cb" },
            AllowedGrantTypes = { OAuthGrantTypes.ClientCredentials },
            AllowedScopes = { "read" }
        });
        var fetched = await fixture.Server.ClientRegistration.GetAsync(registered.ClientId);
        fetched.Should().NotBeNull();
        fetched!.ClientSecret.Should().BeNull();
        fetched.Name.Should().Be("X");
    }

    [Fact]
    public async Task Update_ChangesNameAndScopes()
    {
        var fixture = new TestServer();
        var registered = await fixture.Server.ClientRegistration.RegisterAsync(new ClientRegistrationRequest
        {
            Name = "Old",
            ClientType = OAuthClientType.Confidential,
            RedirectUris = { "https://app.test/cb" },
            AllowedGrantTypes = { OAuthGrantTypes.ClientCredentials },
            AllowedScopes = { "read" }
        });

        var updated = await fixture.Server.ClientRegistration.UpdateAsync(registered.ClientId, new ClientRegistrationRequest
        {
            Name = "New",
            ClientType = OAuthClientType.Confidential,
            RedirectUris = { "https://app.test/cb" },
            AllowedGrantTypes = { OAuthGrantTypes.ClientCredentials },
            AllowedScopes = { "read", "write" }
        });

        updated.Name.Should().Be("New");
        updated.AllowedScopes.Should().Contain("write");
    }

    [Fact]
    public async Task Delete_RemovesClient()
    {
        var fixture = new TestServer();
        var registered = await fixture.Server.ClientRegistration.RegisterAsync(new ClientRegistrationRequest
        {
            Name = "X",
            ClientType = OAuthClientType.Confidential,
            RedirectUris = { "https://app.test/cb" },
            AllowedGrantTypes = { OAuthGrantTypes.ClientCredentials },
            AllowedScopes = { "read" }
        });
        await fixture.Server.ClientRegistration.DeleteAsync(registered.ClientId);
        (await fixture.Clients.GetByClientIdAsync(registered.ClientId)).Should().BeNull();
    }
}

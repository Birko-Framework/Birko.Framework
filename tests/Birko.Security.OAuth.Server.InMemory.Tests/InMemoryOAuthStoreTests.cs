using Birko.Security.OAuth.Server.InMemory.Extensions;
using Birko.Security.OAuth.Server.InMemory.Stores;
using Birko.Security.OAuth.Server.StoreContract;
using Birko.Security.OAuth.Server.Stores;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Birko.Security.OAuth.Server.InMemory.Tests;

/// <summary>TASK-518: the in-memory OAuth stores against the shared store contract, plus their DI registration.</summary>
public class InMemoryOAuthStoreTests : OAuthStoreContract
{
    protected override OAuthStores NewStores() => new(
        new InMemoryOAuthClientStore(),
        new InMemoryAuthorizationCodeStore(),
        new InMemoryRefreshTokenStore(),
        new InMemoryDeviceCodeStore(),
        new InMemoryConsentStore());

    [Fact]
    public void Registration_gives_one_shared_instance_per_store()
    {
        using var provider = new ServiceCollection().AddOAuthServerInMemoryStores().BuildServiceProvider();

        provider.GetRequiredService<IOAuthClientStore>().Should().BeSameAs(provider.GetRequiredService<IOAuthClientStore>());
        provider.GetRequiredService<IAuthorizationCodeStore>().Should().BeSameAs(provider.GetRequiredService<IAuthorizationCodeStore>());
        provider.GetRequiredService<IRefreshTokenStore>().Should().BeSameAs(provider.GetRequiredService<IRefreshTokenStore>());
        provider.GetRequiredService<IDeviceCodeStore>().Should().BeSameAs(provider.GetRequiredService<IDeviceCodeStore>());
        provider.GetRequiredService<IConsentStore>().Should().BeSameAs(provider.GetRequiredService<IConsentStore>());
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IDeviceCodeStore>().Should().BeSameAs(provider.GetRequiredService<IDeviceCodeStore>());
    }
}

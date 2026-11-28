using Birko.Data.Stores;
using Birko.Data.InMemory.Stores;
using Birko.Data.JSON.Stores;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.XML.Stores;
using Birko.Security.OAuth.Server.Extensions;
using Birko.Security.OAuth.Server.Models;
using Birko.Security.OAuth.Server.SqLite.Stores;
using Birko.Security.OAuth.Server.Stores;
using Birko.Security.OAuth.Server.Stores.Factory;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Birko.Security.OAuth.Server.StoreFactory.Tests;

/// <summary>
/// TASK-1013: what the factory stores do besides the contract — refuse a backend that cannot claim atomically, register
/// through DI, and read a database the SQLite OAuth package (TASK-518) wrote.
/// </summary>
public sealed class FactoryOAuthStoreTests : IDisposable
{
    private readonly string _dir = TestTempDirectory.Create("task1013-oauth");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        TestTempDirectory.Delete(_dir);
    }

    private IStoreFactory Create(string backend, string? connectionString = null)
        => Birko.Data.Stores.StoreFactory.Create(backend, connectionString, p => p.AddInMemory().AddJson().AddXml().AddSqLite());

    // ---- a backend that cannot claim ----------------------------------------------------------------------------

    [Theory]
    [InlineData("Json")]
    [InlineData("Xml")]
    public void A_backend_that_cannot_claim_atomically_is_refused_by_name_when_the_stores_are_built(string backend)
    {
        var factory = Create(backend, Path.Combine(_dir, backend));

        var stores = new Func<object>[]
        {
            () => new FactoryOAuthClientStore(factory),
            () => new FactoryAuthorizationCodeStore(factory),
            () => new FactoryRefreshTokenStore(factory),
            () => new FactoryDeviceCodeStore(factory),
            () => new FactoryConsentStore(factory),
        };
        foreach (var build in stores)
        {
            build.Should().Throw<ArgumentException>()
                .WithMessage($"*'{backend}' store backend cannot hold the OAuth server stores*IAsyncConditionalUpdateStore*");
        }
    }

    [Theory]
    [InlineData("Json")]
    [InlineData("Xml")]
    public void Registering_on_a_backend_that_cannot_claim_is_refused_at_registration(string backend)
    {
        var factory = Create(backend, Path.Combine(_dir, backend));
        var services = new ServiceCollection();

        var act = () => services.AddOAuthServerStores(factory);

        act.Should().Throw<ArgumentException>().WithMessage($"*'{backend}' store backend*");
        services.Should().BeEmpty("nothing is registered for a refused backend");
    }

    [Fact]
    public void A_null_factory_is_refused()
    {
        FluentActions.Invoking(() => new FactoryAuthorizationCodeStore(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new ServiceCollection().AddOAuthServerStores(null!)).Should().Throw<ArgumentNullException>();
    }

    // ---- DI -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Registration_gives_one_shared_factory_store_per_interface_and_a_server_runs_over_them()
    {
        using var provider = new ServiceCollection()
            .AddOAuthServerStores(Create("SqLite", Path.Combine(_dir, "di.db")))
            .BuildServiceProvider();

        provider.GetRequiredService<IOAuthClientStore>().Should().BeOfType<FactoryOAuthClientStore>()
            .And.BeSameAs(provider.GetRequiredService<IOAuthClientStore>());
        provider.GetRequiredService<IAuthorizationCodeStore>().Should().BeOfType<FactoryAuthorizationCodeStore>()
            .And.BeSameAs(provider.GetRequiredService<IAuthorizationCodeStore>());
        provider.GetRequiredService<IRefreshTokenStore>().Should().BeOfType<FactoryRefreshTokenStore>()
            .And.BeSameAs(provider.GetRequiredService<IRefreshTokenStore>());
        provider.GetRequiredService<IDeviceCodeStore>().Should().BeOfType<FactoryDeviceCodeStore>();
        using (var scope = provider.CreateScope())
            scope.ServiceProvider.GetRequiredService<IConsentStore>().Should().BeSameAs(provider.GetRequiredService<IConsentStore>());

        var clients = provider.GetRequiredService<IOAuthClientStore>();
        await clients.CreateAsync(new OAuthClient { ClientId = "svc", Name = "Service", AllowedScopes = { "read" } });
        (await clients.GetByNameAsync("Service"))!.AllowedScopes.Should().Equal("read");
    }

    // ---- a database the SQLite OAuth package wrote -------------------------------------------------------------

    [Fact]
    public async Task A_database_written_by_the_SQLite_OAuth_package_is_read_and_written_by_the_factory_stores()
    {
        var file = "legacy.db";
        var settings = new SqLiteSettings(_dir, file);
        var expires = new DateTime(2026, 12, 1, 12, 0, 0, DateTimeKind.Utc);

        IOAuthClientStore oldClients = new SqLiteOAuthClientStore(settings);
        IAuthorizationCodeStore oldCodes = new SqLiteAuthorizationCodeStore(settings);
        IRefreshTokenStore oldRefreshes = new SqLiteRefreshTokenStore(settings);
        await oldClients.CreateAsync(new OAuthClient
        {
            ClientId = "web", Name = "Web app", ClientType = OAuthClientType.Public, ClientSecretHash = null,
            RedirectUris = { "https://app.test/cb" }, AllowedGrantTypes = { OAuthGrantTypes.AuthorizationCode }, AllowedScopes = { "read" },
        });
        await oldRefreshes.CreateAsync(new RefreshTokenRecord
        {
            TokenHash = "H", ClientId = "web", UserId = "u", Scope = "read", ExpiresAt = expires,
            Claims = new Dictionary<string, string> { ["name"] = "octocat" },
        });
        await oldCodes.CreateAsync(new AuthorizationCode { Code = "c1", ClientId = "web", UserId = "u", ExpiresAt = expires });
        await new SqLiteDeviceCodeStore(settings).CreateAsync(new DeviceCodeRecord { DeviceCode = "d1", UserCode = "AAAA-BBBB", ClientId = "web", ExpiresAt = expires });
        await new SqLiteConsentStore(settings).CreateAsync(new ConsentRecord { UserId = "u", ClientId = "web", Scope = "read" });

        var factory = Create("SqLite", Path.Combine(_dir, file));
        IOAuthClientStore clients = new FactoryOAuthClientStore(factory);
        IAuthorizationCodeStore codes = new FactoryAuthorizationCodeStore(factory);
        IRefreshTokenStore refreshes = new FactoryRefreshTokenStore(factory);

        var client = await clients.GetByClientIdAsync("web");
        client!.Name.Should().Be("Web app");
        client.ClientType.Should().Be(OAuthClientType.Public);
        client.RedirectUris.Should().Equal("https://app.test/cb");
        client.AllowedGrantTypes.Should().Equal(OAuthGrantTypes.AuthorizationCode);
        (await clients.GetByNameAsync("Web app"))!.Guid.Should().Be(client.Guid);
        var refresh = await refreshes.GetByHashAsync("H");
        refresh!.Claims.Should().Contain("name", "octocat");
        refresh.ExpiresAt.Should().Be(expires);
        (await ((IDeviceCodeStore)new FactoryDeviceCodeStore(factory)).GetByUserCodeAsync("AAAA-BBBB"))!.DeviceCode.Should().Be("d1");
        (await ((IConsentStore)new FactoryConsentStore(factory)).GetAsync("u", "web"))!.Scope.Should().Be("read");

        var code = await codes.GetByCodeAsync("c1");
        (await codes.TryRedeemAsync(code!)).Should().BeTrue();
        (await refreshes.TryRevokeAsync(refresh)).Should().BeTrue();
        client.AllowedScopes.Add("write");
        await clients.UpdateAsync(client);

        (await oldCodes.GetByCodeAsync("c1"))!.Used.Should().BeTrue("the old package reads what the factory stores wrote");
        (await oldRefreshes.GetByHashAsync("H"))!.Revoked.Should().BeTrue();
        (await oldClients.GetByClientIdAsync("web"))!.AllowedScopes.Should().Equal("read", "write");
    }

    [Theory]
    [InlineData(typeof(AuthorizationCode), "oauth_auth_codes")]
    [InlineData(typeof(DeviceCodeRecord), "oauth_device_codes")]
    [InlineData(typeof(ConsentRecord), "oauth_consents")]
    [InlineData(typeof(Birko.Security.OAuth.Server.Models.Persistence.OAuthClientEntity), "oauth_clients")]
    [InlineData(typeof(Birko.Security.OAuth.Server.Models.Persistence.RefreshTokenEntity), "oauth_refresh_tokens")]
    public void Each_stored_type_declares_the_SQLite_packages_table_name(Type type, string table)
        => (type.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.Schema.TableAttribute), false)
                .Cast<System.ComponentModel.DataAnnotations.Schema.TableAttribute>().Single().Name)
            .Should().Be(table, "a model with no attribute would need the SQLite package's registration to find its table");
}

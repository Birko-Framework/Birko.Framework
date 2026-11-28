using Birko.Data.Stores;
using Birko.Data.InMemory.Stores;
using Birko.Data.JSON.Stores;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.XML.Stores;
using Birko.Security.OAuth.Server.Extensions;
using Birko.Security.OAuth.Server.Models;
using Birko.Security.OAuth.Server.Stores;
using Birko.Security.OAuth.Server.Stores.Factory;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Birko.Security.OAuth.Server.StoreFactory.Tests;

/// <summary>
/// TASK-1013: what the factory stores do besides the contract — refuse a backend that cannot claim atomically, register
/// through DI, and read a database the (deleted) SQLite OAuth package wrote.
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

    /// <summary>
    /// The package is deleted (TASK-1013 step 6); what it wrote is kept as a dump (Fixtures/LegacySqLiteOAuthDatabase.sql),
    /// so this checks the databases it left behind, not a package that could change.
    /// </summary>
    [Fact]
    public async Task A_database_written_by_the_SQLite_OAuth_package_is_read_and_written_by_the_factory_stores()
    {
        var path = Path.Combine(_dir, "legacy.db");
        var expires = new DateTime(2026, 12, 1, 12, 0, 0, DateTimeKind.Utc);
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using var load = connection.CreateCommand();
            load.CommandText = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LegacySqLiteOAuthDatabase.sql"));
            load.ExecuteNonQuery();
        }

        var factory = Create("SqLite", path);
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

        // The writes land in the package's own columns, not in new ones.
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            Scalar(connection, "SELECT Used FROM oauth_auth_codes WHERE Code = 'c1'").Should().Be(1L);
            Scalar(connection, "SELECT Revoked FROM oauth_refresh_tokens WHERE TokenHash = 'H'").Should().Be(1L);
            ((string)Scalar(connection, "SELECT CollectionsJson FROM oauth_clients WHERE ClientId = 'web'")!)
                .Should().Contain("\"allowedScopes\":[\"read\",\"write\"]");
            Scalar(connection, "SELECT COUNT(*) FROM oauth_clients").Should().Be(1L);
        }
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
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

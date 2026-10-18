using Birko.Data.Exceptions;
using Birko.Data.SQL;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Security.OAuth.Server.Models;
using Birko.Security.OAuth.Server.SqLite.Extensions;
using Birko.Security.OAuth.Server.SqLite.Stores;
using Birko.Security.OAuth.Server.StoreContract;
using Birko.Security.OAuth.Server.Stores;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Birko.Security.OAuth.Server.SqLite.Tests;

/// <summary>
/// TASK-518: the SQLite OAuth stores against the shared store contract (each test on its own database file), plus
/// persistence across store instances, the DI registration, and the column-mapper measurement that decided the
/// entity mappings.
/// </summary>
public class SqLiteOAuthStoreTests : OAuthStoreContract, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-oauth-sqlite-" + Guid.NewGuid().ToString("N"));

    public SqLiteOAuthStoreTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private SqLiteSettings Settings(string file = "oauth.db") => new(_root, file);

    private OAuthStores StoresOn(SqLiteSettings settings) => new(
        new SqLiteOAuthClientStore(settings),
        new SqLiteAuthorizationCodeStore(settings),
        new SqLiteRefreshTokenStore(settings),
        new SqLiteDeviceCodeStore(settings),
        new SqLiteConsentStore(settings));

    protected override OAuthStores NewStores() => StoresOn(Settings("db-" + Guid.NewGuid().ToString("N") + ".db"));

    [Fact]
    public async Task Clients_and_refresh_claims_survive_a_new_store_instance_on_the_same_file()
    {
        var settings = Settings("persist.db");
        var first = StoresOn(settings);
        var client = NewPublicClient("web", OAuthGrantTypes.RefreshToken);
        await first.Clients.CreateAsync(client);
        await first.Refreshes.CreateAsync(new RefreshTokenRecord
        {
            TokenHash = "H", ClientId = "web", UserId = "github:42", ExpiresAt = Clock.UtcNow.AddDays(1),
            Claims = new Dictionary<string, string> { ["name"] = "octocat" },
        });
        await first.Codes.CreateAsync(new AuthorizationCode { Code = "c", ClientId = "web", UserId = "u", ExpiresAt = Clock.UtcNow.AddMinutes(1) });

        var second = StoresOn(settings);

        (await second.Clients.GetByClientIdAsync("web"))!.AllowedGrantTypes.Should().Equal(OAuthGrantTypes.RefreshToken);
        (await second.Refreshes.GetByHashAsync("H"))!.Claims.Should().Contain("name", "octocat");
        (await second.Codes.GetByCodeAsync("c")).Should().NotBeNull();
        File.Exists(Path.Combine(_root, "persist.db")).Should().BeTrue();
    }

    [Fact]
    public void Registration_gives_one_shared_instance_per_store()
    {
        using var provider = new ServiceCollection().AddOAuthServerSqLiteStores(Settings()).BuildServiceProvider();

        provider.GetRequiredService<IOAuthClientStore>().Should().BeOfType<SqLiteOAuthClientStore>()
            .And.BeSameAs(provider.GetRequiredService<IOAuthClientStore>());
        provider.GetRequiredService<IAuthorizationCodeStore>().Should().BeSameAs(provider.GetRequiredService<IAuthorizationCodeStore>());
        provider.GetRequiredService<IRefreshTokenStore>().Should().BeSameAs(provider.GetRequiredService<IRefreshTokenStore>());
        provider.GetRequiredService<IDeviceCodeStore>().Should().BeSameAs(provider.GetRequiredService<IDeviceCodeStore>());
        provider.GetRequiredService<IConsentStore>().Should().BeSameAs(provider.GetRequiredService<IConsentStore>());
    }

    [Fact]
    public async Task Registration_by_path_creates_the_directory_and_works()
    {
        var path = Path.Combine(_root, "nested", "dir", "oauth.db");
        using var provider = new ServiceCollection().AddOAuthServerSqLiteStores(path).BuildServiceProvider();

        await provider.GetRequiredService<IConsentStore>().CreateAsync(new ConsentRecord { UserId = "u", ClientId = "c", Scope = "read" });

        File.Exists(path).Should().BeTrue("tables are created on first use");
    }

    [Fact]
    public async Task Updating_a_client_that_is_not_stored_throws_rather_than_creating_it()
    {
        var stores = NewStores();
        var client = NewPublicClient("ghost", OAuthGrantTypes.RefreshToken);
        client.Guid = Guid.NewGuid();

        await FluentActions.Awaiting(() => stores.Clients.UpdateAsync(client)).Should().ThrowAsync<EntityNotFoundException>();
        (await stores.Clients.SaveAsync(client)).Should().Be(client.Guid!.Value, "Save is the upsert");
        (await stores.Clients.GetByClientIdAsync("ghost")).Should().NotBeNull();
    }

    /// <summary>
    /// The measurement behind the entity mappings: the SQL column mapper has no mapping for a <c>List</c> or a
    /// <c>Dictionary</c> property, and since SH-H037 it refuses the type at table load (it no longer drops it
    /// silently, as DraCode's comment said). So storing <see cref="OAuthClient"/> or <see cref="RefreshTokenRecord"/>
    /// as they are fails loudly, and both need an entity with a JSON column.
    /// </summary>
    [Theory]
    [InlineData(typeof(OAuthClient), nameof(OAuthClient.RedirectUris))]
    [InlineData(typeof(OAuthClient), nameof(OAuthClient.AllowedScopes))]
    [InlineData(typeof(RefreshTokenRecord), nameof(RefreshTokenRecord.Claims))]
    public void The_column_mapper_refuses_the_models_with_collections(Type model, string property)
    {
        var load = () => DataBase.LoadField(model.GetProperty(property)!).ToList();
        load.Should().Throw<FieldAttributeException>().WithMessage("*has no SQL column mapping*");
    }
}

using Birko.Data.CosmosDB.Stores;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.InMemory.Stores;
using Birko.Data.JSON.Stores;
using Birko.Data.XML.Stores;
using Birko.Data.Models;
using Birko.Data.MongoDB.Stores;
using Birko.Data.RavenDB.Stores;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Security.OAuth.Server.Models;
using Birko.Security.OAuth.Server.Models.Persistence;
using Birko.Security.OAuth.Server.StoreContract;
using Birko.Security.OAuth.Server.Stores.Factory;
using Birko.Tests.Shared;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Security.OAuth.Server.StoreFactory.Tests;

/// <summary>
/// TASK-1013: the shared OAuth store contract over the factory stores, per backend, through
/// <c>Birko.Data.Stores.StoreFactory.Create(backend, connectionString, …)</c> — plus what only the factory stores do:
/// translate a predicate over the model to the entity, and refuse one on a member kept inside a JSON column.
/// </summary>
public abstract class FactoryOAuthStoreContract : OAuthStoreContract
{
    protected static readonly Action<StoreProviders> All = p => p
        .AddInMemory().AddJson().AddXml().AddSqLite().AddPostgreSQL().AddMySQL().AddMSSql()
        .AddMongoDB().AddElasticSearch().AddRavenDB().AddCosmosDB();

    /// <summary>The factory a test's stores come from.</summary>
    protected abstract IStoreFactory Factory();

    /// <summary>
    /// False for a backend whose factory is fresh per test (InMemory, a SQLite file each). A shared one (a live database
    /// for the process) is emptied before each test.
    /// </summary>
    protected virtual bool Shared => true;

    protected override OAuthStores NewStores()
    {
        var factory = Factory();
        if (Shared)
        {
            Purge<OAuthClientEntity>(factory);
            Purge<RefreshTokenEntity>(factory);
            Purge<AuthorizationCode>(factory);
            Purge<DeviceCodeRecord>(factory);
            Purge<ConsentRecord>(factory);
            SettleAsync().GetAwaiter().GetResult();
        }
        return new OAuthStores(
            new FactoryOAuthClientStore(factory),
            new FactoryAuthorizationCodeStore(factory),
            new FactoryRefreshTokenStore(factory),
            new FactoryDeviceCodeStore(factory),
            new FactoryConsentStore(factory));
    }

    private void Purge<T>(IStoreFactory factory) where T : AbstractModel
    {
        var store = factory.GetAsyncStore<T>();
        SettleAsync().GetAwaiter().GetResult();
        var all = store.ReadAsync(CancellationToken.None).GetAwaiter().GetResult().ToList();
        if (all.Count > 0) store.DeleteAsync(all).GetAwaiter().GetResult();
    }

    // ---- predicates over the model ---------------------------------------------------------------------------

    [Fact]
    public async Task A_client_predicate_on_scalar_members_is_answered_by_the_backend()
    {
        if (!Ready()) return;
        var stores = NewStores();
        var confidential = NewPublicClient("conf", OAuthGrantTypes.ClientCredentials);
        confidential.ClientType = OAuthClientType.Confidential;
        confidential.ClientSecretHash = "hash";
        var disabled = NewPublicClient("off", OAuthGrantTypes.RefreshToken);
        disabled.IsEnabled = false;
        await stores.Clients.CreateAsync(confidential);
        await stores.Clients.CreateAsync(NewPublicClient("pub", OAuthGrantTypes.RefreshToken));
        await stores.Clients.CreateAsync(disabled);

        var found = await stores.Clients.ReadAsync(c => c.ClientType == OAuthClientType.Confidential && c.IsEnabled);
        found!.ClientId.Should().Be("conf");
        (await stores.Clients.CountAsync(c => c.ClientType == OAuthClientType.Public)).Should().Be(2);
        (await stores.Clients.CountAsync(c => !c.IsEnabled)).Should().Be(1);
        (await stores.Clients.ReadAsync(c => c.ClientSecretHash == "hash" && c.Guid == confidential.Guid))!.Name.Should().Be("conf");
        (await stores.Clients.ReadAsync(c => c.Name == "nobody")).Should().BeNull();
    }

    [Fact]
    public async Task A_refresh_predicate_on_scalar_members_is_answered_by_the_backend()
    {
        if (!Ready()) return;
        var stores = NewStores();
        await stores.Refreshes.CreateAsync(new RefreshTokenRecord { TokenHash = "a", ClientId = "web", UserId = "u1", ExpiresAt = Clock.UtcNow.AddDays(1) });
        await stores.Refreshes.CreateAsync(new RefreshTokenRecord { TokenHash = "b", ClientId = "web", UserId = "u2", ExpiresAt = Clock.UtcNow.AddDays(1), Revoked = true });

        (await stores.Refreshes.CountAsync(r => r.ClientId == "web" && !r.Revoked)).Should().Be(1);
        (await stores.Refreshes.ReadAsync(r => r.Revoked))!.TokenHash.Should().Be("b");
    }

    [Fact]
    public async Task A_predicate_on_a_member_kept_in_a_JSON_column_is_refused_by_name()
    {
        if (!Ready()) return;
        var stores = NewStores();

        (await FluentActions.Awaiting(() => stores.Clients.ReadAsync(c => c.AllowedScopes.Contains("read")))
            .Should().ThrowAsync<NotSupportedException>()).WithMessage("*OAuthClient.AllowedScopes*JSON*");
        (await FluentActions.Awaiting(() => stores.Clients.CountAsync(c => c.Name == "x" || c.RedirectUris.Count > 0))
            .Should().ThrowAsync<NotSupportedException>()).WithMessage("*OAuthClient.RedirectUris*");
        (await FluentActions.Awaiting(() => stores.Refreshes.ReadAsync(r => r.Claims.ContainsKey("name")))
            .Should().ThrowAsync<NotSupportedException>()).WithMessage("*RefreshTokenRecord.Claims*");
    }
}

// ---- offline ---------------------------------------------------------------------------------------------------

public sealed class InMemoryFactoryOAuthStoreTests : FactoryOAuthStoreContract
{
    protected override bool Shared => false;
    protected override IStoreFactory Factory() => Birko.Data.Stores.StoreFactory.Create("InMemory", null, All);
}

public sealed class SqLiteFactoryOAuthStoreTests : FactoryOAuthStoreContract, IDisposable
{
    private readonly string _dir = TestTempDirectory.Create("task1013-oauth-sqlite");

    protected override bool Shared => false;

    protected override IStoreFactory Factory()
        => Birko.Data.Stores.StoreFactory.Create("SqLite", Path.Combine(_dir, "oauth-" + Guid.NewGuid().ToString("N") + ".db"), All);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        TestTempDirectory.Delete(_dir);
    }
}

// ---- live ------------------------------------------------------------------------------------------------------

/// <summary>A live backend: skipped without its variable, failed under <c>BIRKO_REQUIRE_LIVE</c>.</summary>
public abstract class LiveFactoryOAuthStoreContract : FactoryOAuthStoreContract
{
    private readonly ITestOutputHelper _output;

    protected LiveFactoryOAuthStoreContract(ITestOutputHelper output) => _output = output;

    /// <summary>The variable that names the server.</summary>
    protected abstract string Variable { get; }

    protected static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    /// <summary>One name per test process: a run's tests share its database and empty it before each test.</summary>
    protected static readonly string RunName = "t1013" + Guid.NewGuid().ToString("N")[..12];

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, IStoreFactory> Factories = new();

    /// <summary>The backend's factory, built once per test class: a NoSQL factory holds its client.</summary>
    protected abstract IStoreFactory CreateFactory();

    protected sealed override IStoreFactory Factory() => Factories.GetOrAdd(GetType(), _ => CreateFactory());

    protected override bool Ready()
    {
        if (Env(Variable) != null) return true;
        if (Env("BIRKO_REQUIRE_LIVE") != null)
            throw new InvalidOperationException($"{Variable} is not set but BIRKO_REQUIRE_LIVE is.");
        _output.WriteLine($"SKIPPED: {Variable} not set.");
        return false;
    }
}

public sealed class PostgreSqlFactoryOAuthStoreTests : LiveFactoryOAuthStoreContract
{
    public PostgreSqlFactoryOAuthStoreTests(ITestOutputHelper output) : base(output) { }
    protected override string Variable => "BIRKO_PG_HOST";

    protected override IStoreFactory CreateFactory() => Birko.Data.Stores.StoreFactory.Create("PostgreSQL",
        $"Host={Env("BIRKO_PG_HOST")};Port={Env("BIRKO_PG_PORT") ?? "5432"};Username={Env("BIRKO_PG_USER") ?? "postgres"};"
        + $"Password={Env("BIRKO_PG_PASSWORD") ?? "postgres"};Database={LiveDatabase.Name("PG")}", All);
}

public sealed class MySqlFactoryOAuthStoreTests : LiveFactoryOAuthStoreContract
{
    public MySqlFactoryOAuthStoreTests(ITestOutputHelper output) : base(output) { }
    protected override string Variable => "BIRKO_MYSQL_HOST";

    protected override IStoreFactory CreateFactory() => Birko.Data.Stores.StoreFactory.Create("MySQL",
        $"Server={Env("BIRKO_MYSQL_HOST")};Port={Env("BIRKO_MYSQL_PORT") ?? "3306"};User ID={Env("BIRKO_MYSQL_USER") ?? "root"};"
        + $"Password={Env("BIRKO_MYSQL_PASSWORD") ?? "root"};Database={LiveDatabase.Name("MYSQL")}", All);
}

public sealed class MSSqlFactoryOAuthStoreTests : LiveFactoryOAuthStoreContract
{
    public MSSqlFactoryOAuthStoreTests(ITestOutputHelper output) : base(output) { }
    protected override string Variable => "BIRKO_MSSQL_HOST";

    protected override IStoreFactory CreateFactory() => Birko.Data.Stores.StoreFactory.Create("MSSql",
        $"Data Source={Env("BIRKO_MSSQL_HOST")},{Env("BIRKO_MSSQL_PORT") ?? "1433"};Initial Catalog={LiveDatabase.Name("MSSQL")};"
        + $"User ID={Env("BIRKO_MSSQL_USER") ?? "sa"};Password={Env("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd"};"
        + "Encrypt=False;Trust Server Certificate=True", All);
}

public sealed class MongoDBFactoryOAuthStoreTests : LiveFactoryOAuthStoreContract
{
    public MongoDBFactoryOAuthStoreTests(ITestOutputHelper output) : base(output) { }
    protected override string Variable => "BIRKO_MONGO_HOST";

    protected override IStoreFactory CreateFactory() => Birko.Data.Stores.StoreFactory.Create("MongoDB",
        $"mongodb://{Env("BIRKO_MONGO_HOST")}:{Env("BIRKO_MONGO_PORT") ?? "27017"}/{RunName}", All);
}

public sealed class ElasticSearchFactoryOAuthStoreTests : LiveFactoryOAuthStoreContract
{
    public ElasticSearchFactoryOAuthStoreTests(ITestOutputHelper output) : base(output) { }
    protected override string Variable => "BIRKO_ES_URL";

    protected override IStoreFactory CreateFactory()
        => Birko.Data.Stores.StoreFactory.Create("ElasticSearch", $"Url={Env("BIRKO_ES_URL")};IndexPrefix={RunName}", All);

    protected override async Task SettleAsync()
    {
        using var http = new HttpClient { BaseAddress = new Uri(Env("BIRKO_ES_URL")!) };
        var response = await http.PostAsync($"/{RunName}_*/_refresh?allow_no_indices=true", null);
        response.EnsureSuccessStatusCode();
    }
}

public sealed class RavenDBFactoryOAuthStoreTests : LiveFactoryOAuthStoreContract
{
    public RavenDBFactoryOAuthStoreTests(ITestOutputHelper output) : base(output) { }
    protected override string Variable => "BIRKO_RAVEN_URL";

    protected override IStoreFactory CreateFactory()
        => Birko.Data.Stores.StoreFactory.Create("RavenDB", $"Url={Env("BIRKO_RAVEN_URL")};Database={RunName}", All);
}

public sealed class CosmosDBFactoryOAuthStoreTests : LiveFactoryOAuthStoreContract
{
    public CosmosDBFactoryOAuthStoreTests(ITestOutputHelper output) : base(output) { }
    protected override string Variable => "BIRKO_COSMOS_CONNECTION";

    protected override IStoreFactory CreateFactory()
    {
        var mode = Env("BIRKO_COSMOS_CONNECTION_MODE") is { } m ? $";ConnectionMode={m}" : "";
        return Birko.Data.Stores.StoreFactory.Create("CosmosDB", $"{Env("BIRKO_COSMOS_CONNECTION")!.TrimEnd(';')};Database={RunName}{mode}", All);
    }
}

using Birko.Configuration;
using Birko.Data.CosmosDB.Stores;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.InMemory.Stores;
using Birko.Data.JSON.Stores;
using Birko.Data.Models;
using Birko.Data.MongoDB.Stores;
using Birko.Data.RavenDB.Stores;
using Birko.Data.Repositories;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using Birko.Data.XML.Stores;
using Birko.Tests.Shared;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.StoreFactory.Tests;

/// <summary>
/// TASK-990: one <see cref="IStoreFactory"/> for any backend, SQL or not, chosen by a backend name and a connection string
/// (or by a settings object), so the same code runs on InMemory or SQLite in tests and on PostgreSQL, MongoDB or Cosmos DB
/// in production. The same store suite runs on every backend through the interface.
/// </summary>
public class StoreFactoryTests
{
    private readonly ITestOutputHelper _output;
    public StoreFactoryTests(ITestOutputHelper output) => _output = output;

    [Birko.Data.SQL.Attributes.Table("TASK990_Gadget")]
    public class Gadget : AbstractDatabaseModel
    {
        [Birko.Data.SQL.Attributes.MaxLengthField(64)]
        public string? Name { get; set; }

        public int Weight { get; set; }
    }

    private static readonly Action<StoreProviders> All = p => p
        .AddInMemory().AddJson().AddXml().AddSqLite()
        .AddMongoDB().AddElasticSearch().AddRavenDB().AddCosmosDB();

    /// <summary>
    /// What a consumer does through the neutral interface: schema if offered, CRUD, count, bulk, a filtered update, a
    /// repository, an aggregate if offered. <paramref name="settle"/> runs after each write on a near-real-time backend
    /// (Elasticsearch: a search sees a write after the next index refresh), as the store contract's Settle does.
    /// </summary>
    private static async Task ExerciseAsync(IStoreFactory factory, Func<Task>? settle = null)
    {
        settle ??= () => Task.CompletedTask;
        if (factory is ISchemaStoreFactory schema) await schema.CreateSchemaAsync(new[] { typeof(Gadget) });
        var store = factory.GetAsyncStore<Gadget>();
        var leftovers = (await store.ReadAsync(CancellationToken.None)).ToList();
        if (leftovers.Count > 0) await store.DeleteAsync(leftovers);
        await settle();

        var a = new Gadget { Name = "a", Weight = 2 };
        await store.CreateAsync(a);
        await store.CreateAsync(new[] { new Gadget { Name = "b", Weight = 3 }, new Gadget { Name = "c", Weight = 5 } });
        await settle();

        (await store.CountAsync()).Should().Be(3);
        (await store.ReadAsync(a.Guid!.Value))!.Name.Should().Be("a");

        await store.UpdateAsync(x => x.Name == "a", g => g.Weight = 7);
        await settle();
        (await store.ReadAsync(a.Guid!.Value))!.Weight.Should().Be(7);

        var repository = factory.GetAsyncRepository<Gadget>();
        (await repository.CountAsync(x => x.Weight > 2)).Should().Be(3);

        if (store is IAsyncAggregatableStore<Gadget> aggregatable)
        {
            var sum = await aggregatable.AggregateAsync(new AggregateQuery<Gadget>
            {
                Aggregates = [new AggregateField(AggregateFunction.Sum, nameof(Gadget.Weight), "total")],
            });
            Convert.ToInt64(sum.Single().Values["total"]).Should().Be(15);
        }

        await store.DeleteAsync((await store.ReadAsync(CancellationToken.None)).ToList());
        await settle();
        (await store.CountAsync()).Should().Be(0);
    }

    // ── choosing a backend ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void An_unregistered_backend_is_refused_naming_the_registered_ones()
    {
        var act = () => Birko.Data.Stores.StoreFactory.Create("Oracle", "x", p => p.AddInMemory().AddMongoDB());

        act.Should().Throw<ArgumentException>().WithMessage("*'Oracle' is not registered (registered: InMemory, MongoDB)*");
    }

    [Fact]
    public void A_backend_registered_twice_is_refused()
    {
        var act = () => Birko.Data.Stores.StoreFactory.Create("InMemory", null, p => p.AddInMemory().AddInMemory());

        act.Should().Throw<ArgumentException>().WithMessage("*'InMemory' is already registered*");
    }

    [Fact]
    public void The_backend_name_is_matched_ignoring_case()
        => Birko.Data.Stores.StoreFactory.Create("inmemory", "", All).Backend.Should().Be("InMemory");

    [Fact]
    public void Settings_choose_their_backend_by_type()
    {
        var mongo = Birko.Data.Stores.StoreFactory.Create(new Birko.Data.MongoDB.Stores.Settings("localhost", "app"), All);
        mongo.Backend.Should().Be("MongoDB");
        mongo.Should().BeOfType<MongoDBStoreFactory>();

        var path = Path.Combine(Path.GetTempPath(), "task990-" + Guid.NewGuid().ToString("N"), "app.db");
        var sqlite = Birko.Data.Stores.StoreFactory.Create(new SqLiteSettings(Path.GetDirectoryName(path)!, "app.db"), All);
        sqlite.Backend.Should().Be("SqLite");
        sqlite.Should().BeAssignableTo<ISqlStoreFactory>();
    }

    [Fact]
    public void Settings_that_more_than_one_backend_takes_are_refused_naming_them()
    {
        // JSON, XML and InMemory all take plain Settings; picking one would be a guess.
        var act = () => Birko.Data.Stores.StoreFactory.Create(new Birko.Configuration.Settings("data", "x"), All);

        act.Should().Throw<ArgumentException>().WithMessage("*more than one registered backend (InMemory, Json, Xml)*");
    }

    [Fact]
    public void Settings_with_one_backend_for_their_type_choose_it()
        => Birko.Data.Stores.StoreFactory.Create(new Birko.Configuration.Settings("x", "y"), p => p.AddInMemory()).Backend.Should().Be("InMemory");

    private sealed class TunedMongoSettings : Birko.Data.MongoDB.Stores.Settings { }

    [Fact]
    public void A_subclass_of_a_backends_settings_chooses_that_backend()
        => Birko.Data.Stores.StoreFactory.Create(new TunedMongoSettings { Location = "h", Name = "app" }, All).Backend.Should().Be("MongoDB");

    [Fact]
    public void Settings_no_registered_backend_takes_are_refused()
    {
        var act = () => Birko.Data.Stores.StoreFactory.Create(new Birko.Data.RavenDB.Stores.Settings("http://x", "db"), p => p.AddInMemory().AddMongoDB());

        act.Should().Throw<ArgumentException>().WithMessage("*No registered store backend takes*RavenDB*");
    }

    // ── the SQL providers join the same registry (TASK-988 unchanged) ────────────────────────────

    [Fact]
    public async Task SQLite_runs_the_suite_through_the_neutral_interface()
    {
        var dir = TestTempDirectory.Create("task990-sqlite");
        try
        {
            var factory = Birko.Data.Stores.StoreFactory.Create("SqLite", Path.Combine(dir, "app.db"), All);
            factory.Should().BeAssignableTo<ISchemaStoreFactory>("a SQL backend creates its tables");
            await ExerciseAsync(factory);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TestTempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void A_TASK_988_SQL_factory_is_also_a_neutral_one()
    {
        var path = Path.Combine(Path.GetTempPath(), "task990-" + Guid.NewGuid().ToString("N"), "app.db");
        ISqlStoreFactory sql = SqlStoreFactory.Create("SqLite", path, p => p.AddSqLite());
        IStoreFactory neutral = sql;

        neutral.Backend.Should().Be(sql.Provider);
        neutral.Settings.Should().BeSameAs(sql.Settings);
        neutral.GetAsyncStore<Gadget>().Should().BeAssignableTo<IAsyncSqlBulkStore<Gadget>>();
        sql.GetAsyncRepository<Gadget>().Should().BeOfType<Birko.Data.SQL.Repositories.AsyncSqlModelRepository<Gadget>>(
            "the SQL factory's own repository, with the connector, still wins for SQL callers");
    }

    [Fact]
    public void A_SQL_provider_through_the_neutral_registry_still_refuses_an_empty_connection_string()
    {
        var act = () => Birko.Data.Stores.StoreFactory.Create("SqLite", "  ", All);

        act.Should().Throw<ArgumentException>().WithMessage("*connection string is required*SqLite*");
    }

    // ── InMemory ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InMemory_runs_the_suite()
        => await ExerciseAsync(Birko.Data.Stores.StoreFactory.Create("InMemory", null, All));

    [Fact]
    public async Task InMemory_answers_one_store_per_type_so_callers_share_the_data()
    {
        var factory = Birko.Data.Stores.StoreFactory.Create("InMemory", "", All);
        await factory.GetAsyncStore<Gadget>().CreateAsync(new Gadget { Name = "kept" });

        factory.GetAsyncStore<Gadget>().Should().BeSameAs(factory.GetAsyncStore<Gadget>());
        (await factory.GetAsyncRepository<Gadget>().CountAsync()).Should().Be(1, "a repository from the factory sees the same store");
    }

    [Fact]
    public void InMemory_refuses_a_connection_string()
    {
        var act = () => Birko.Data.Stores.StoreFactory.Create("InMemory", "Path=x", All);

        act.Should().Throw<ArgumentException>().WithMessage("*'InMemory' takes no connection string*");
    }

    // ── JSON / XML files ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Json", ".json")]
    [InlineData("Xml", ".xml")]
    public async Task A_file_backend_runs_the_suite_and_keeps_each_type_in_its_own_file(string backend, string extension)
    {
        var dir = TestTempDirectory.Create("task990-" + backend);
        try
        {
            await ExerciseAsync(Birko.Data.Stores.StoreFactory.Create(backend, dir, All));

            var factory = Birko.Data.Stores.StoreFactory.Create(backend, "Path=" + dir, All);
            await factory.GetAsyncStore<Gadget>().CreateAsync(new Gadget { Name = "persisted", Weight = 1 });
            File.Exists(Path.Combine(dir, typeof(Gadget).FullName!.Replace('+', '.') + extension)).Should().BeTrue();

            var reopened = Birko.Data.Stores.StoreFactory.Create(backend, dir, All);
            (await reopened.GetAsyncStore<Gadget>().ReadAsync(CancellationToken.None)).Select(g => g.Name).Should().Equal("persisted");
        }
        finally
        {
            TestTempDirectory.Delete(dir);
        }
    }

    [Theory]
    [InlineData("Json")]
    [InlineData("Xml")]
    public void A_file_backend_refuses_an_unknown_key_and_an_empty_path(string backend)
    {
        var unknown = () => Birko.Data.Stores.StoreFactory.Create(backend, "Path=x;BatchSize=10", All);
        unknown.Should().Throw<ArgumentException>().WithMessage($"*'{backend}' cannot use connection string key(s) 'BatchSize'*");

        var empty = () => Birko.Data.Stores.StoreFactory.Create(backend, " ", All);
        empty.Should().Throw<ArgumentException>().WithMessage($"*'{backend}' needs a folder path*");
    }

    // ── connection strings of the networked backends (offline: nothing connects until a store is used) ──

    [Fact]
    public void MongoDB_takes_its_own_connection_string_and_needs_the_database_in_it()
    {
        var factory = (MongoDBStoreFactory)Birko.Data.Stores.StoreFactory.Create("MongoDB", "mongodb://u:p@h1:27017,h2:27018/app?replicaSet=rs0", All);
        factory.BackendSettings.Name.Should().Be("app");
        factory.BackendSettings.Location.Should().Be("h1:27017,h2:27018");
        factory.BackendSettings.UserName.Should().Be("u");
        factory.BackendSettings.GetConnectionString().Should().Be("mongodb://u:p@h1:27017,h2:27018/app?replicaSet=rs0", "the string reaches the driver verbatim");

        var noDatabase = () => Birko.Data.Stores.StoreFactory.Create("MongoDB", "mongodb://h1:27017", All);
        noDatabase.Should().Throw<ArgumentException>().WithMessage("*needs the database in the connection string's path*");

        var notMongo = () => Birko.Data.Stores.StoreFactory.Create("MongoDB", "Server=h1;Database=app", All);
        notMongo.Should().Throw<ArgumentException>().WithMessage("*takes MongoDB's own connection string*");
    }

    [Fact]
    public void ElasticSearch_takes_a_url_and_an_index_prefix_and_refuses_anything_else()
    {
        var factory = (ElasticSearchStoreFactory)Birko.Data.Stores.StoreFactory.Create("ElasticSearch", "Url=http://es:9200;IndexPrefix=app", All);
        factory.BackendSettings.Location.Should().Be("http://es:9200");
        factory.BackendSettings.Name.Should().Be("app");

        var password = () => Birko.Data.Stores.StoreFactory.Create("ElasticSearch", "Url=http://es:9200;IndexPrefix=app;Password=x", All);
        password.Should().Throw<ArgumentException>().WithMessage("*cannot use connection string key(s) 'Password'*");

        var noPrefix = () => Birko.Data.Stores.StoreFactory.Create("ElasticSearch", "Url=http://es:9200", All);
        noPrefix.Should().Throw<ArgumentException>().WithMessage("*needs IndexPrefix=…*");

        var badUrl = () => Birko.Data.Stores.StoreFactory.Create("ElasticSearch", "Url=es:9200;IndexPrefix=app", All);
        badUrl.Should().Throw<ArgumentException>().WithMessage("*needs an http(s) server address*");
    }

    [Fact]
    public void RavenDB_takes_url_and_database_and_refuses_a_certificate_key()
    {
        var factory = (RavenDBStoreFactory)Birko.Data.Stores.StoreFactory.Create("RavenDB", "Url=http://raven:8080;Database=app", All);
        factory.BackendSettings.Location.Should().Be("http://raven:8080");
        factory.BackendSettings.Name.Should().Be("app");

        var cert = () => Birko.Data.Stores.StoreFactory.Create("RavenDB", "Url=http://raven:8080;Database=app;Certificate=x.pfx", All);
        cert.Should().Throw<ArgumentException>().WithMessage("*cannot use connection string key(s) 'Certificate'*");

        var noDatabase = () => Birko.Data.Stores.StoreFactory.Create("RavenDB", "Url=http://raven:8080", All);
        noDatabase.Should().Throw<ArgumentException>().WithMessage("*needs Database=…*");
    }

    [Fact]
    public void CosmosDB_takes_its_own_string_plus_the_database_and_an_optional_connection_mode()
    {
        const string account = "AccountEndpoint=https://localhost:8081/;AccountKey=a2V5;";
        var factory = (CosmosDBStoreFactory)Birko.Data.Stores.StoreFactory.Create("CosmosDB", account + "Database=app;ConnectionMode=gateway", All);
        factory.BackendSettings.Location.Should().Be("https://localhost:8081/");
        factory.BackendSettings.Name.Should().Be("app");
        factory.BackendSettings.Password.Should().Be("a2V5");
        factory.BackendSettings.ConnectionMode.Should().Be(Microsoft.Azure.Cosmos.ConnectionMode.Gateway);
        factory.BackendSettings.UserName.Should().BeNullOrEmpty("each type gets its own container");

        var noDatabase = () => Birko.Data.Stores.StoreFactory.Create("CosmosDB", account, All);
        noDatabase.Should().Throw<ArgumentException>().WithMessage("*needs Database=…*");

        var badMode = () => Birko.Data.Stores.StoreFactory.Create("CosmosDB", account + "Database=app;ConnectionMode=Fast", All);
        badMode.Should().Throw<ArgumentException>().WithMessage("*ConnectionMode must be Direct or Gateway*");

        var container = () => new CosmosDBStoreFactory(new Birko.Data.CosmosDB.Stores.Settings("https://localhost:8081/", "app", "a2V5", "one"));
        container.Should().Throw<ArgumentException>().WithMessage("*gives each type its own container*");
    }

    // ── live NoSQL backends ──────────────────────────────────────────────────────────────────────

    private string? Live(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrWhiteSpace(value)) return value;
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException($"{variable} is not set but BIRKO_REQUIRE_LIVE is.");
        _output.WriteLine($"SKIPPED: {variable} not set.");
        return null;
    }

    private static string RunName() => "t990" + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public async Task MongoDB_runs_the_suite_live()
    {
        if (Live("BIRKO_MONGO_HOST") is not { } host) return;
        var port = Environment.GetEnvironmentVariable("BIRKO_MONGO_PORT") is { Length: > 0 } p ? p : "27017";
        await ExerciseAsync(Birko.Data.Stores.StoreFactory.Create("MongoDB", $"mongodb://{host}:{port}/{RunName()}", All));
    }

    [Fact]
    public async Task ElasticSearch_runs_the_suite_live()
    {
        if (Live("BIRKO_ES_URL") is not { } url) return;
        var prefix = RunName();
        using var http = new HttpClient { BaseAddress = new Uri(url) };
        await ExerciseAsync(
            Birko.Data.Stores.StoreFactory.Create("ElasticSearch", $"Url={url};IndexPrefix={prefix}", All),
            async () => (await http.PostAsync($"/{prefix}_*/_refresh", null)).EnsureSuccessStatusCode());
    }

    [Fact]
    public async Task RavenDB_runs_the_suite_live()
    {
        if (Live("BIRKO_RAVEN_URL") is not { } url) return;
        await ExerciseAsync(Birko.Data.Stores.StoreFactory.Create("RavenDB", $"Url={url};Database={RunName()}", All));
    }

    [Fact]
    public async Task CosmosDB_runs_the_suite_live()
    {
        if (Live("BIRKO_COSMOS_CONNECTION") is not { } connection) return;
        var mode = Environment.GetEnvironmentVariable("BIRKO_COSMOS_CONNECTION_MODE") is { Length: > 0 } m ? $";ConnectionMode={m}" : "";
        await ExerciseAsync(Birko.Data.Stores.StoreFactory.Create("CosmosDB", $"{connection.TrimEnd(';')};Database={RunName()}{mode}", All));
    }
}

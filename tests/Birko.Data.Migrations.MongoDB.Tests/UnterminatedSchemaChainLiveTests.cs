using System;
using System.Linq;
using Birko.Data.Migrations.Context;
using Birko.Data.Migrations.Exceptions;
using Birko.Data.Migrations.MongoDB;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.MongoDB.Tests;

/// <summary>
/// TASK-627 (SH-M245), on a document store: a <c>CreateIndex</c> chain without <c>Build()</c> created no index and
/// the migration was recorded. It now fails, naming the chain, and is not recorded. Live MongoDB.
/// </summary>
public class UnterminatedSchemaChainLiveTests
{
    private readonly ITestOutputHelper _output;
    public UnterminatedSchemaChainLiveTests(ITestOutputHelper output) => _output = output;

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MONGO_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MONGO_PORT"), out var p) ? p : 27017;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live MongoDB. Set BIRKO_MONGO_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    private sealed class Migration : Data.Migrations.AbstractMigration
    {
        private readonly Action<IMigrationContext> _up;
        public Migration(long version, Action<IMigrationContext> up) { Version = version; _up = up; }
        public override long Version { get; }
        public override string Name => "M" + Version;
        public override void Up(IMigrationContext context) => _up(context);
    }

    private (MongoMigrationRunner Runner, IMongoDatabase Db, string DbName) Setup(params IMigration[] migrations)
    {
        var dbName = "birko_task627_" + Guid.NewGuid().ToString("N");
        var mongo = new global::Birko.Data.MongoDB.MongoDBClient(new global::Birko.Data.MongoDB.Stores.Settings
        {
            RawConnectionString = $"mongodb://{Host}:{Port}/?replicaSet=rs0&directConnection=true",
            Name = dbName,
        });
        // No session: index builds inside a transaction are restricted, and the point here is the record, not atomicity.
        var runner = new MongoMigrationRunner(mongo, new Settings.MongoMigrationSettings { UseSession = false });
        runner.RegisterMigrations(migrations);
        runner.Initialize();
        return (runner, mongo.Database, dbName);
    }

    private static bool HasIndex(IMongoDatabase db, string name)
        => db.GetCollection<BsonDocument>("Widgets").Indexes.List().ToList().Any(i => i["name"].AsString == name);

    private static readonly Migration CreateWidgets = new(1, c => c.Schema.CreateCollection("Widgets").Build());

    [Fact]
    public void An_index_chain_without_Build_fails_the_migration_and_is_not_recorded()
    {
        if (!RequireServer()) return;
        var (runner, db, dbName) = Setup(CreateWidgets, new Migration(2, c => c.Schema.CreateIndex("Widgets", "ix_name").WithField("Name")));
        try
        {
            runner.Invoking(r => r.Migrate()).Should().Throw<MigrationException>()
                .Which.ToString().Should().Contain("CreateIndex(\"Widgets\", \"ix_name\")");

            runner.CurrentVersion.Should().Be(1);
            HasIndex(db, "ix_name").Should().BeFalse();
        }
        finally
        {
            db.Client.DropDatabase(dbName);
        }
    }

    [Fact]
    public void A_terminated_index_chain_is_applied_and_recorded()
    {
        if (!RequireServer()) return;
        var (runner, db, dbName) = Setup(CreateWidgets, new Migration(2, c => c.Schema.CreateIndex("Widgets", "ix_name").WithField("Name").Build()));
        try
        {
            runner.Migrate().Success.Should().BeTrue();

            runner.CurrentVersion.Should().Be(2);
            HasIndex(db, "ix_name").Should().BeTrue();
        }
        finally
        {
            db.Client.DropDatabase(dbName);
        }
    }

    [Fact]
    public void A_collection_chain_without_Build_fails_on_a_provider_that_creates_it_eagerly_too()
    {
        if (!RequireServer()) return;
        // The collection exists after CreateCollection here, but the same migration creates nothing on SQL: the rule
        // is uniform, so a migration that passes on one provider passes on all.
        var (runner, db, dbName) = Setup(new Migration(1, c => c.Schema.CreateCollection("Widgets")));
        try
        {
            runner.Invoking(r => r.Migrate()).Should().Throw<MigrationException>()
                .Which.ToString().Should().Contain("CreateCollection(\"Widgets\")");
            runner.CurrentVersion.Should().Be(0);
        }
        finally
        {
            db.Client.DropDatabase(dbName);
        }
    }
}

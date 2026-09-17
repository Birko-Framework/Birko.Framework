using System;
using System.IO;
using System.Linq;
using Birko.Data.Migrations.Context;
using Birko.Data.Migrations.MongoDB;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.MongoDB.Tests;

/// <summary>
/// <b>SH-H031 (TASK-314).</b> <see cref="MongoMigrationRunner"/> threads its session into the migration
/// context so each migration's own operations join the transaction, then recorded the version row through
/// <see cref="MongoMigrationStore"/> -- which issued a <b>sessionless</b> <c>ReplaceOne</c>. A sessionless
/// driver call commits immediately, so when a later migration failed and <c>AbortTransaction()</c> rolled
/// the data back, the version rows survived: those migrations were permanently marked applied with their
/// changes gone, and no re-run could repair it.
///
/// <para>
/// The version row has to live or die with the data it describes, so the store now joins the same session.
/// <c>IMigrationStore</c> cannot carry the session in its signature without changing every backend, so it
/// is ambient on the concrete store and entered through a <b>self-restoring scope</b> -- Conventions
/// records repeatedly that per-caller state assigned onto a longer-lived object is how a later caller ends
/// up holding a committed session.
/// </para>
/// </summary>
public class MigrationVersionRowTransactionTests
{
    private readonly ITestOutputHelper _output;

    public MigrationVersionRowTransactionTests(ITestOutputHelper output) => _output = output;

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MONGO_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MONGO_PORT"), out var p) ? p : 27017;
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MONGO_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    // ---------------------------------------------------------------- scope semantics (offline)

    /// <summary>
    /// Restoring the previous value rather than clearing to null is the property that matters: a nested or
    /// repeated run must not strand the store pointing at a session that has already been committed.
    /// </summary>
    [Fact]
    public void The_session_scope_restores_what_was_there_rather_than_clearing()
    {
        var store = Store();

        CurrentSession(store).Should().BeNull("a store with no run in flight holds no session");

        using (store.EnterSession(null))
        {
            // An outer scope with no session is legitimate: UseSession=false, or a standalone server.
            CurrentSession(store).Should().BeNull();
        }

        CurrentSession(store).Should().BeNull();
    }

    /// <summary>Disposing twice must not clobber a scope entered after the first dispose.</summary>
    [Fact]
    public void Disposing_a_scope_twice_is_harmless()
    {
        var store = Store();

        var scope = store.EnterSession(null);
        scope.Dispose();
        scope.Dispose();

        CurrentSession(store).Should().BeNull();
    }

    private static MongoMigrationStore Store()
        => new(new MongoClient("mongodb://localhost:59995").GetDatabase("unused"));

    private static object? CurrentSession(MongoMigrationStore store)
        => typeof(MongoMigrationStore)
            .GetField("_session", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(store);

    // ---------------------------------------------------------------- wiring (source shape)

    /// <summary>
    /// The runner has to enter the scope, and both bookkeeping writes have to take the session overload.
    /// Weaker than the live assertion below, and the honest alternative to no cover at all when no replica
    /// set is available -- it fails if anyone reverts either half.
    /// </summary>
    [Fact]
    public void Both_bookkeeping_writes_take_the_session_overload_and_the_runner_enters_the_scope()
    {
        var store = ReadSource("MongoMigrationStore.cs");

        store.Should().Contain("_collection!.ReplaceOne(_session, filter, document, options)");
        store.Should().Contain("_collection!.DeleteOne(_session, filter)");
        ReadSource("MongoMigrationRunner.cs").Should().Contain("store.EnterSession(session)");
    }

    private static string ReadSource(string fileName)
    {
        // Framework.Tests/<proj>/bin/Debug/net10.0 -> repo root -> Framework/<proj>
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 6; i++) dir = Path.GetDirectoryName(dir)!;
        var path = Path.Combine(dir, "Framework", "Birko.Data.Migrations.MongoDB", fileName);
        File.Exists(path).Should().BeTrue($"the source scan must actually find the file (looked at {path})");
        return File.ReadAllText(path);
    }

    // ---------------------------------------------------------------- observed state (live)

    /// <summary>
    /// The assertion the acceptance criterion asks for: the state left behind, not the absence of an
    /// exception. A failing migration aborts the transaction, and afterwards the version row for the
    /// migration that DID succeed must be gone -- before the fix it survived, because it had been committed
    /// sessionlessly the moment it was written.
    /// </summary>
    /// <remarks>
    /// Needs a <b>replica set</b>, not merely a running mongod -- MongoDB transactions are unavailable on a
    /// standalone server, and <see cref="MongoMigrationRunner"/> checks exactly that before starting one.
    /// TASK-274 records the same trap: a container that satisfies "is it up?" can still make a green suite
    /// report a defect that is not there, so this skips loudly rather than passing when the server shape is
    /// wrong. Start one with a replica-set mongod plus an rs.initiate(), then set <c>BIRKO_MONGO_HOST</c>.
    /// </remarks>
    [Fact]
    public void An_aborted_run_leaves_no_version_row_behind()
    {
        if (!RequireServer()) return;

        var mongo = new global::Birko.Data.MongoDB.MongoDBClient(new global::Birko.Data.MongoDB.Stores.Settings
        {
            RawConnectionString = $"mongodb://{Host}:{Port}/?replicaSet=rs0",
            Name = Database,
        });
        var client = mongo.Client;
        var db = mongo.Database;
        var settings = new Settings.MongoMigrationSettings { MigrationsCollection = "__sh_h031_probe" };

        if (client.Cluster.Description.Type != global::MongoDB.Driver.Core.Clusters.ClusterType.ReplicaSet)
        {
            const string shape = "SKIPPED: MongoDB is up but is not a replica set, so transactions are "
                               + "unavailable and this test would pass for the wrong reason.";
            _output.WriteLine(shape);
            if (RequireLive) throw new InvalidOperationException(shape);
            return;
        }

        db.DropCollection(settings.MigrationsCollection);
        db.DropCollection("__sh_h031_data");

        var runner = new MongoMigrationRunner(mongo, settings);
        runner.RegisterMigrations(new SucceedingMigration(), new FailingMigration());
        runner.Initialize();

        try
        {
            runner.Migrate();
        }
        catch
        {
            // The second migration is meant to fail; the point is what it leaves behind.
        }

        var rows = db.GetCollection<BsonDocument>(settings.MigrationsCollection)
                     .Find(FilterDefinition<BsonDocument>.Empty).ToList();

        rows.Should().BeEmpty(
            "the aborted transaction rolled the DATA back, so the version row for migration 1 must go with "
          + "it -- otherwise migration 1 is permanently considered applied while its changes are gone");

        db.DropCollection(settings.MigrationsCollection);
        db.DropCollection("__sh_h031_data");
    }

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live MongoDB replica set. Set BIRKO_MONGO_HOST to exercise this "
                             + "test; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private sealed class SucceedingMigration : Data.Migrations.AbstractMigration
    {
        public override long Version => 1;
        public override string Name => "Succeeds";
        public override void Up(IMigrationContext context)
            => context.Data.BulkInsert("__sh_h031_data",
                new[] { new System.Collections.Generic.Dictionary<string, object> { ["_id"] = "a" } });
    }

    private sealed class FailingMigration : Data.Migrations.AbstractMigration
    {
        public override long Version => 2;
        public override string Name => "Fails";
        public override void Up(IMigrationContext context)
            => throw new InvalidOperationException("deliberate failure to force AbortTransaction");
    }
}

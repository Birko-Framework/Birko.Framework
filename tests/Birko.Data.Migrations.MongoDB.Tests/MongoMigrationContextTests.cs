using Birko.Data.Migrations.MongoDB.Context;
using FluentAssertions;
using MongoDB.Driver;
using System;
using Xunit;

namespace Birko.Data.Migrations.MongoDB.Tests;

/// <summary>
/// Compile-guard + light tests for the MongoDB migration provider. Building this project verifies
/// the CR-C09 session-threading edits compile against the real MongoDB.Driver overloads
/// (the schema builder + data migrator now pass an optional IClientSessionHandle to every operation
/// so migrations join the runner's transaction). The transactional behavior itself needs a live
/// MongoDB replica set and is not exercised here.
/// </summary>
public class MongoMigrationContextTests
{
    [Fact]
    public void Context_NullDatabase_Throws()
    {
        Action act = () => new MongoMigrationContext(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Context_ExposesSchemaAndData()
    {
        var db = new MongoClient("mongodb://localhost:27017").GetDatabase("birko_migrations_test_unused");
        var ctx = new MongoMigrationContext(db, session: null);

        ctx.Schema.Should().NotBeNull();
        ctx.Data.Should().NotBeNull();
        ctx.ProviderName.Should().Be("MongoDB");
    }
}

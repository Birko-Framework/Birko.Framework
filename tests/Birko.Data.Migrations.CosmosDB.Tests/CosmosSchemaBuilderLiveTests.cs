using System;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.IndexManagement;
using Birko.Data.Migrations.Context;
using Birko.Data.Migrations.CosmosDB.Context;
using Birko.Data.Patterns.Schema;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.CosmosDB.Tests;

/// <summary>
/// TASK-152, live against the Cosmos emulator: a migration's collection is created by Build() with the key it declares
/// (SH-M174), its index through the index manager under its name (SH-M200), and a count reads every page (SH-M184).
/// Gated on <c>BIRKO_COSMOS_CONNECTION</c>.
/// </summary>
public sealed class CosmosSchemaBuilderLiveTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CosmosClient? _client;
    private Database? _db;

    public CosmosSchemaBuilderLiveTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        var conn = Environment.GetEnvironmentVariable("BIRKO_COSMOS_CONNECTION");
        if (string.IsNullOrWhiteSpace(conn))
        {
            _output.WriteLine("SKIPPED: no live Cosmos. Set BIRKO_COSMOS_CONNECTION.");
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_REQUIRE_LIVE is set but BIRKO_COSMOS_CONNECTION is not.");
            return;
        }
        _client = new CosmosClient(conn, new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway });
        _db = (await _client.CreateDatabaseIfNotExistsAsync("birko_task152_" + Guid.NewGuid().ToString("N"))).Database;
    }

    public async Task DisposeAsync()
    {
        if (_db != null) { try { await _db.DeleteAsync(); } catch { /* best-effort */ } }
        _client?.Dispose();
    }

    [Fact]
    public async Task A_collection_is_created_by_Build_and_an_index_is_created_under_its_name()
    {
        if (_db == null) return;
        var schema = new CosmosDBSchemaBuilder(_db);

        var builder = schema.CreateCollection("Orders").WithField("id", FieldType.String, isPrimary: true).WithField("Code", FieldType.String);
        schema.CollectionExists("Orders").Should().BeFalse("nothing is created before Build");
        builder.Build();
        schema.CollectionExists("Orders").Should().BeTrue();

        schema.CreateIndex("Orders", "IX_Code").WithField("Code").Build();

        (await new CosmosDBIndexManager(_db).ExistsAsync("IX_Code", "Orders")).Should().BeTrue();
        schema.PendingBuilds.Should().BeEmpty();
    }

    [Fact]
    public void A_primary_field_other_than_the_document_id_is_refused_before_anything_is_created()
    {
        if (_db == null) return;
        var schema = new CosmosDBSchemaBuilder(_db);

        Action act = () => schema.CreateCollection("Tenants").WithField("TenantId", FieldType.String, isPrimary: true).Build();

        act.Should().Throw<NotSupportedException>().Which.Message.Should().Contain("/id");
        schema.CollectionExists("Tenants").Should().BeFalse();
    }

    [Fact]
    public async Task A_count_reads_every_page()
    {
        if (_db == null) return;
        var container = (await _db.CreateContainerIfNotExistsAsync("Rows", "/id")).Container;
        for (var i = 0; i < 25; i++)
            await container.CreateItemAsync(new { id = "r" + i, kind = i % 2 == 0 ? "even" : "odd" }, new PartitionKey("r" + i));

        var data = new CosmosDBDataMigrator(_db);

        data.CountDocuments("Rows").Should().Be(25);
        data.CountDocuments("Rows", "{\"kind\":\"even\"}").Should().Be(13);
        data.CountDocuments("Rows", "{\"missing\":null}").Should().Be(25, "a property no document has is null for all of them (SH-M176)");
        data.CountDocuments("Rows", "{\"kind\":{\"$ne\":\"even\"}}").Should().Be(12);
    }

    [Fact]
    public void A_copy_with_a_transform_is_refused()
    {
        if (_db == null) return;
        Action act = () => new CosmosDBDataMigrator(_db).CopyData("A", "B", "{\"x\":1}");
        act.Should().Throw<NotSupportedException>();
    }
}

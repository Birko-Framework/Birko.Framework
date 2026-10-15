using System;
using System.Threading.Tasks;
using Birko.Data.Migrations.MongoDB.Context;
using Birko.Data.MongoDB;
using Birko.Data.MongoDB.IndexManagement;
using Birko.Data.Patterns.IndexManagement;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.MongoDB.Tests;

/// <summary>
/// SH-M203 (TASK-152), live MongoDB: the migration index builder recorded each field's type and built a plain ascending
/// index; it now takes its keys from MongoDBIndexManager.KeysFor. Gated on <c>BIRKO_MONGO_HOST</c>.
/// </summary>
public class IndexBuilderFieldTypeLiveTests
{
    private readonly ITestOutputHelper _output;
    public IndexBuilderFieldTypeLiveTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task A_migration_text_index_is_a_text_index()
    {
        var host = Environment.GetEnvironmentVariable("BIRKO_MONGO_HOST");
        if (string.IsNullOrWhiteSpace(host))
        {
            const string message = "SKIPPED: no live MongoDB. Set BIRKO_MONGO_HOST; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
            _output.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException(message);
            return;
        }
        var settings = new Birko.Data.MongoDB.Stores.Settings(host, "birko_task152_" + Guid.NewGuid().ToString("N"));
        var client = new MongoDBClient(settings);
        try
        {
            await client.Database.CreateCollectionAsync("Articles");
            new MongoSchemaBuilder(client.Database, null)
                .CreateIndex("Articles", "IX_Body").WithField("Body", fieldType: IndexFieldType.Text).Build();
            new MongoSchemaBuilder(client.Database, null)
                .CreateIndex("Articles", "IX_Owner").WithField("Owner", fieldType: IndexFieldType.Hashed).Build();

            var manager = new MongoDBIndexManager(client);
            (await manager.GetInfoAsync("IX_Body", "Articles"))!.Fields.Should().ContainSingle().Which.FieldType.Should().Be(IndexFieldType.Text);
            (await manager.GetInfoAsync("IX_Owner", "Articles"))!.Fields.Should().ContainSingle().Which.FieldType.Should().Be(IndexFieldType.Hashed);
        }
        finally
        {
            await client.Database.Client.DropDatabaseAsync(settings.Name);
        }
    }
}

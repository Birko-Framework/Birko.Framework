using System;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.MongoDB.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.MongoDB.Tests.Stores;

/// <summary>
/// TASK-634, measured: a document stored before its model gained a non-nullable property reads back with the property's
/// default. This is why MongoDB needs no repair plan when a model grows (it has no column schema), where SQL needed one
/// (TASK-633: an added nullable column read as NULL threw in a typed reader).
/// </summary>
/// <remarks>Gated on <c>BIRKO_MONGO_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.</remarks>
public class MissingFieldReadLiveTests
{
    private readonly ITestOutputHelper _output;
    public MissingFieldReadLiveTests(ITestOutputHelper output) => _output = output;

    private string? ResolveHost()
    {
        var host = Environment.GetEnvironmentVariable("BIRKO_MONGO_HOST");
        if (!string.IsNullOrWhiteSpace(host)) return host;
        const string message = "SKIPPED: no live MongoDB. Set BIRKO_MONGO_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return null;
    }

    // Two models over one collection: the store names it after typeof(T).Name, which is "Item" for both.
    public static class Before
    {
        public class Item : AbstractModel
        {
            public string? Name { get; set; }
        }
    }

    public static class After
    {
        public enum Stage { Draft = 0, Live = 1 }

        public class Item : AbstractModel
        {
            public string? Name { get; set; }
            public int Count { get; set; }
            public Guid Reference { get; set; }
            public bool Flag { get; set; }
            public decimal Amount { get; set; }
            public DateTime Seen { get; set; }
            public Stage State { get; set; }
        }
    }

    [Fact]
    public async Task A_document_stored_before_the_model_grew_reads_back_with_defaults()
    {
        var host = ResolveHost();
        if (host == null) return;
        var settings = new Settings(host, "birko_task634_" + Guid.NewGuid().ToString("N"));
        var before = new AsyncMongoDBStore<Before.Item>();
        before.SetSettings(settings);
        var after = new AsyncMongoDBStore<After.Item>();
        after.SetSettings(settings);

        try
        {
            var guid = await before.CreateAsync(new Before.Item { Name = "old" });

            var read = await after.ReadAsync(guid);

            read.Should().NotBeNull();
            read.Should().BeEquivalentTo(new After.Item { Guid = guid, Name = "old" });
        }
        finally
        {
            await new Birko.Data.MongoDB.MongoDBClient(settings).Database.Client.DropDatabaseAsync(settings.Name);
        }
    }
}

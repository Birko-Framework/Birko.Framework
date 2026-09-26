using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.MongoDB.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.MongoDB.Tests.Stores;

/// <summary>
/// TASK-498 — <c>PropertyUpdate.Increment</c> / <c>Decrement</c> as <c>$inc</c> against a live MongoDB. The
/// rendering is asserted ungated in <c>MongoPropertyUpdateTranslatorTests</c>; this suite asserts what lands on
/// the server, and that the refusal of an increment on a string-represented decimal leaves the document as it was.
///
/// Gated on <c>BIRKO_MONGO_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> so a missing server fails.
/// </summary>
public class PropertyUpdateIncrementLiveTests
{
    private const string HostEnv = "BIRKO_MONGO_HOST";

    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public PropertyUpdateIncrementLiveTests(ITestOutputHelper output) => _output = output;

    private string? ResolveHost()
    {
        var host = Environment.GetEnvironmentVariable(HostEnv);
        if (!string.IsNullOrWhiteSpace(host))
        {
            return host;
        }

        const string message = "SKIPPED: no live MongoDB. Set " + HostEnv + " to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive)
        {
            throw new InvalidOperationException(message);
        }
        return null;
    }

    public class CounterDoc : AbstractModel
    {
        public string? Name { get; set; }
        public int Hits { get; set; }
        public double Ratio { get; set; }

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal Price { get; set; }

        /// <summary>No attribute: the driver's default decimal representation is a string.</summary>
        public decimal PriceAsText { get; set; }
    }

    private static AsyncMongoDBStore<CounterDoc> NewStore(string host)
    {
        var store = new AsyncMongoDBStore<CounterDoc>();
        store.SetSettings(new Settings(host, "birko_task498_" + Guid.NewGuid().ToString("N")));
        return store;
    }

    private static async Task<CounterDoc> SeedAsync(AsyncMongoDBStore<CounterDoc> store)
    {
        var doc = new CounterDoc { Name = "target", Hits = 10, Ratio = 1.5, Price = 10.10m, PriceAsText = 10.10m };
        await store.CreateAsync(doc);
        return doc;
    }

    private static async Task<CounterDoc> ReadAsync(AsyncMongoDBStore<CounterDoc> store, Guid guid)
        => (await store.ReadAsync(x => x.Guid == guid)).Single();

    [Fact]
    public async Task Set_Increment_And_Decrement_Round_Trip()
    {
        var host = ResolveHost();
        if (host == null) return;

        var store = NewStore(host);
        try
        {
            var doc = await SeedAsync(store);
            var guid = doc.Guid!.Value;

            await store.UpdateAsync(x => x.Guid == guid,
                new PropertyUpdate<CounterDoc>().Set(x => x.Name, "renamed").Increment(x => x.Hits, 2).Decrement(x => x.Ratio, 0.5));

            var after = await ReadAsync(store, guid);
            after.Name.Should().Be("renamed");
            after.Hits.Should().Be(12);
            after.Ratio.Should().Be(1.0);
        }
        finally
        {
            await store.DestroyAsync();
        }
    }

    [Fact]
    public async Task A_Decimal128_Decimal_Increments_Exactly()
    {
        var host = ResolveHost();
        if (host == null) return;

        var store = NewStore(host);
        try
        {
            var guid = (await SeedAsync(store)).Guid!.Value;

            await store.UpdateAsync(x => x.Guid == guid, new PropertyUpdate<CounterDoc>().Increment(x => x.Price, 0.20m));

            (await ReadAsync(store, guid)).Price.Should().Be(10.30m);
        }
        finally
        {
            await store.DestroyAsync();
        }
    }

    [Fact]
    public async Task A_String_Represented_Decimal_Increment_Is_Refused_And_The_Document_Is_Unchanged()
    {
        var host = ResolveHost();
        if (host == null) return;

        var store = NewStore(host);
        try
        {
            var guid = (await SeedAsync(store)).Guid!.Value;

            var act = () => store.UpdateAsync(x => x.Guid == guid,
                new PropertyUpdate<CounterDoc>().Set(x => x.Name, "renamed").Increment(x => x.PriceAsText, 0.20m));

            await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*PriceAsText*Decimal128*");

            var after = await ReadAsync(store, guid);
            after.Name.Should().Be("target", "the refusal comes before anything is sent, so the Set did not land either");
            after.PriceAsText.Should().Be(10.10m);
        }
        finally
        {
            await store.DestroyAsync();
        }
    }
}
